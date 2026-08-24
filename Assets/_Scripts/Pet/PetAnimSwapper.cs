using UnityEngine;

namespace GIC.Pet
{
    /// <summary>
    /// 方案B测试场景动作切换（运行时只管播放）：UI 按钮由管线在编辑期预置于场景
    /// （PaimonRetargetTest.unity 的 AnimUICanvas），onClick 持久绑定 Play(clip名)。
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

        [SerializeField] private Animation targetAnimation; // Paimon_MMD 根上的 Animation 组件

        [Header("动作→情绪映射（表情由情绪层驱动，不烘焙进 clip）")]
        [SerializeField] private 动作情绪映射[] 情绪映射 = new[]
        {
            new 动作情绪映射 { 动作名片段 = "Anger", 情绪名 = "Anger" },
        };

        [SerializeField] private PetEmotionController emotionController; // 情绪层（可空=无表情）
        [SerializeField] private PetFingerPoseController fingerPoseController; // 手指姿态层（可空=手指走 clip 曲线）

        [Header("过渡")]
        [Tooltip("动作切换 CrossFade 时长（秒）——过渡期双 clip 双采样，过长则混合开销放大顿挫（2026-08-24 实验：纯播大摆动动作零掉帧，顿挫全在过渡/叠加层）")]
        [SerializeField] private float 动作过渡秒 = 0.2f;
        [Tooltip("启动时预热全部已注册 clip（逐个 Play+Sample 后回待机）——legacy Animation 首播冷初始化实测 150ms 掉帧串（2026-08-24 Player.log 16 连掉帧无任何子系统标记，t=64 首次摆手实证）。开销=启动一次性几十 ms，不增加常驻内存（曲线数据本就随场景加载）")]
        [SerializeField] private bool 启动预热 = true;

        void Start()
        {
            if (启动预热 && targetAnimation != null) 预热全部Clip();
        }

        /// <summary>逐 clip Play→Sample→Stop（全部在 Start 帧内完成，渲染前无视觉闪现），
        /// 触发每个 clip 的首次求值（绑定/采样器构建），把冷初始化成本从"首次播放"挪到启动期。</summary>
        void 预热全部Clip()
        {
            float t0 = Time.realtimeSinceStartup;
            var names = new System.Collections.Generic.List<string>();
            foreach (AnimationState st in targetAnimation) names.Add(st.name);
            foreach (var n in names)
            {
                var st = targetAnimation[n];
                if (st == null || st.clip == null) continue;
                st.wrapMode = WrapMode.Loop;
                targetAnimation.Play(n);
                targetAnimation.Sample();
                targetAnimation.Stop();
            }
            // 回默认待机
            if (targetAnimation.clip != null)
            {
                var def = targetAnimation[targetAnimation.clip.name];
                if (def != null) { def.wrapMode = WrapMode.Loop; targetAnimation.Play(targetAnimation.clip.name); }
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
            return targetAnimation != null && targetAnimation.IsPlaying(clipName);
        }

        void 播放(string clipName, WrapMode 循环模式)
        {
            if (targetAnimation == null) return;
            var state = targetAnimation[clipName];
            if (state == null || state.clip == null) return;
            state.wrapMode = 循环模式;
            // v19 流畅度（2026-08-23）：Stop()+Play() 硬切 → CrossFade 平滑过渡（原神观感）；
            // 2026-08-24 0.3→0.2：过渡期双 clip 双采样是顿挫放大器，收紧窗口
            targetAnimation.CrossFade(clipName, 动作过渡秒);

            // 情绪下发：无映射的情绪动作 → 下发空名清回默认脸
            if (emotionController != null)
            {
                var emo = "";
                foreach (var m in 情绪映射)
                    if (!string.IsNullOrEmpty(m.动作名片段) && clipName.Contains(m.动作名片段)) { emo = m.情绪名; break; }
                emotionController.SetEmotion(emo);
            }

            // 手指姿态切换（2026-08-23 程序化手指层）：按 clip 名让 PetFingerPoseController 接管五指
            fingerPoseController?.SetPose(clipName);
        }
    }
}
