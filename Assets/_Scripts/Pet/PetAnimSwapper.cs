using UnityEngine;
using UnityEngine.Serialization;

namespace GIC.Pet
{
    /// <summary>
    /// 动作切换（运行时只管播放）：UI 按钮由 PetSceneSyncTool 编辑期预置于场景
    /// （PaimonPet.unity 的 AnimUICanvas，2026-08-24 单场景方案），onClick 持久绑定 Play(clip名)。
    /// 表情走运行时情绪层（PetEmotionController，docs/19 §2.6 三层架构）：
    /// Play 按动作名映射下发情绪指令，映射表 Inspector 中文可配。
    /// </summary>
    public class PetAnimSwapper : MonoBehaviour
    {
        [System.Serializable]
        public class 动作情绪映射
        {
            [Tooltip("动作名片段（clip 名包含即命中，如 Anger）")] public string 动作名片段;
            [Tooltip("情绪名（PetEmotionController 情绪表中的名，空=不表情绪）")] public string 情绪名;
        }

        [SerializeField, FormerlySerializedAs("targetAnimation")] private Animation 目标动画; // GI 官方模型骨节点上的 Animation 组件

        /// <summary>当前驱动的 Animation 组件（编辑器同步工具按此注册 clip，勿按 FindObjectsOfType 顺序找）</summary>
        public Animation TargetAnimation => 目标动画;

        [Header("动作→情绪映射（表情由情绪层驱动，不烘焙进 clip）")]
        [SerializeField] private 动作情绪映射[] 情绪映射 = new[]
        {
            new 动作情绪映射 { 动作名片段 = "Greet", 情绪名 = "Happy" },
            new 动作情绪映射 { 动作名片段 = "Anger", 情绪名 = "Anger" },
            new 动作情绪映射 { 动作名片段 = "Sneer", 情绪名 = "Sneer" },
            new 动作情绪映射 { 动作名片段 = "Clap", 情绪名 = "Happy" },
            new 动作情绪映射 { 动作名片段 = "Show_", 情绪名 = "得意" },
            new 动作情绪映射 { 动作名片段 = "Shy", 情绪名 = "Shy" },
            new 动作情绪映射 { 动作名片段 = "Confuse", 情绪名 = "Confuse" },
            new 动作情绪映射 { 动作名片段 = "Think", 情绪名 = "Think" },
            new 动作情绪映射 { 动作名片段 = "Like", 情绪名 = "Happy" },
            new 动作情绪映射 { 动作名片段 = "Hope", 情绪名 = "期待" },
            new 动作情绪映射 { 动作名片段 = "Refuse", 情绪名 = "拒绝" },
            new 动作情绪映射 { 动作名片段 = "Sleep", 情绪名 = "Sleepy" },
            new 动作情绪映射 { 动作名片段 = "SitLoop", 情绪名 = "Sleepy" },
        };

        [SerializeField, FormerlySerializedAs("emotionController")] private PetEmotionController 情绪控制器; // 情绪层（可空=无表情）
        [SerializeField, FormerlySerializedAs("fingerPoseController")] private PetFingerPoseController 手指姿态控制器; // 手指姿态层（可空=手指走 clip 曲线）

        [Header("过渡")]
        [Tooltip("动作切换过渡时长（秒）——惯性化模式=偏移衰减时长；CrossFade 兜底模式=线性混合窗口（过渡期双 clip 双采样，过长则混合开销放大顿挫）")]
        [SerializeField] private float 动作过渡秒 = 0.6f;
        [Tooltip("惯性化层（Gears of War 4 式切换：硬切+当前姿势/速度 C2 衰减归零）。空/禁用=回退 CrossFade 线性混合")]
        [SerializeField] private PetInertializer 惯性化器;
        [Tooltip("启动时预热全部已注册 clip（逐个 Play+Sample 后回待机）——legacy Animation 首播冷初始化实测 150ms 掉帧串（2026-08-24 Player.log 16 连掉帧无任何子系统标记，t=64 首次摆手实证）。开销=启动一次性几十 ms，不增加常驻内存（曲线数据本就随场景加载）")]
        [SerializeField] private bool 启动预热 = true;

        void Start()
        {
            if (启动预热 && 目标动画 != null) 预热全部Clip();
        }

        /// <summary>逐 clip Play→Sample→Stop（全部在 Start 帧内完成，渲染前无视觉闪现），
        /// 触发每个 clip 的首次求值（绑定/采样器构建），把冷初始化成本从"首次播放"挪到启动期。</summary>
        void 预热全部Clip()
        {
            float t0 = Time.realtimeSinceStartup;
            var names = new System.Collections.Generic.List<string>();
            foreach (AnimationState st in 目标动画) names.Add(st.name);
            foreach (var n in names)
            {
                var st = 目标动画[n];
                if (st == null || st.clip == null) continue;
                st.wrapMode = WrapMode.Loop;
                目标动画.Play(n);
                目标动画.Sample();
                目标动画.Stop();
            }
            // 回默认待机
            if (目标动画.clip != null)
            {
                var def = 目标动画[目标动画.clip.name];
                if (def != null) { def.wrapMode = WrapMode.Loop; 目标动画.Play(目标动画.clip.name); }
            }
            Debug.Log($"[PetAnimSwapper] 预热 {names.Count} 个 clip 耗时 {(Time.realtimeSinceStartup - t0) * 1000:F0}ms");
        }

        public void Play(string clipName)
        {
            播放(clipName, WrapMode.Loop);
        }

        /// <summary>单次播放（播完停住，回什么由调用方决定）——行为层打招呼/随机小动作用</summary>
        public void PlayOnce(string clipName)
        {
            播放(clipName, WrapMode.Once);
        }

        /// <summary>clip 是否正在播放（单次动作结束判定用）</summary>
        public bool 是否在播(string clipName)
        {
            return 目标动画 != null && 目标动画.IsPlaying(clipName);
        }

        /// <summary>clip 是否已注册可播（出场/退场等关键动作的存在性判断；空名安全）</summary>
        public bool 动作存在(string clipName)
        {
            if (string.IsNullOrEmpty(clipName) || 目标动画 == null) return false;
            var state = 目标动画[clipName];
            return state != null && state.clip != null;
        }

        /// <summary>当前过渡时长（行为层尾段提前过渡用）</summary>
        public float 过渡秒 => 动作过渡秒;

        /// <summary>惯性化路径是否激活（行为层据此关闭尾段截尾——惯性化下速度承接使任意切点
        /// 无缝，动作播到自然结尾再切保留作者收尾，截尾反而丢动作且 0.6s 窗口下截断明显）</summary>
        public bool 惯性化启用 => 惯性化器 != null && 惯性化器.启用惯性化;

        /// <summary>单次动作剩余秒数（-1=未注册/未播；0=已播完）。行为层在剩余≈过渡秒时
        /// 提前切回待机，让 CrossFade 与动作尾部重叠——消除"播完定格→再淡入"的割裂感。</summary>
        public float 剩余秒(string clipName)
        {
            if (目标动画 == null || string.IsNullOrEmpty(clipName)) return -1f;
            var state = 目标动画[clipName];
            if (state == null || state.clip == null) return -1f;
            if (!目标动画.IsPlaying(clipName)) return 0f;
            return Mathf.Max(0f, state.clip.length - state.time);
        }

        void 播放(string clipName, WrapMode 循环模式)
        {
            if (目标动画 == null) return;
            var state = 目标动画[clipName];
            if (state == null || state.clip == null) return;
            state.wrapMode = 循环模式;
            if (惯性化器 != null && 惯性化器.启用惯性化)
            {
                // 惯性化切换（2026-08-27，GoW4 技术）：硬切新 clip——单 clip 求值无双采样开销，
                // 姿态连续性由惯性化层后处理保证（当前姿势+速度 C2 连续衰减归零，
                // 任意切点/中途打断都平滑，替代旧 CrossFade 的线性权重混合）
                目标动画.Play(clipName, PlayMode.StopAll);
                惯性化器.Trigger(动作过渡秒);
            }
            else
            {
                // v19 流畅度（2026-08-23）：Stop()+Play() 硬切 → CrossFade 平滑过渡（原神观感）；
                // 2026-08-24 0.3→0.2：过渡期双 clip 双采样是顿挫放大器，收紧窗口
                目标动画.CrossFade(clipName, 动作过渡秒);
            }

            // 情绪下发：无映射的情绪动作 → 下发空名清回默认脸
            if (情绪控制器 != null)
            {
                var emo = "";
                foreach (var m in 情绪映射)
                    if (!string.IsNullOrEmpty(m.动作名片段) && clipName.Contains(m.动作名片段)) { emo = m.情绪名; break; }
                情绪控制器.SetEmotion(emo);
            }

            // 手指姿态切换（2026-08-23 程序化手指层）：按 clip 名让 PetFingerPoseController 接管五指
            手指姿态控制器?.SetPose(clipName);
        }
    }
}
