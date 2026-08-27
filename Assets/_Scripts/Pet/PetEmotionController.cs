using System.Collections;
using UnityEngine;

namespace GIC.Pet
{
    /// <summary>
    /// 运行时情绪层（行业通行 face/body 分层架构的 emotion 侧，docs/19 §2.6）：
    /// 表情由逻辑层指令驱动 morph 目标值 + 过渡曲线 + 复位，不烘焙进身体动画 clip——
    /// 对齐原神玩法态（状态机族 clip 零表情曲线，表情由对话/剧情脚本触发第二套系统）。
    /// 三层分工：身体=Animation clip；情绪=本组件（指令式）；微动作=PetBlinkController（随机 timer）。
    /// morph 独占约定：本组件管理的 morph 不得出现在任何 clip 曲线中（Animation 只覆盖 clip
    /// 中存在的曲线，clip 无曲线则组件权重生效——PetBlinkController 同理）。
    /// 典型调用：PetAnimSwapper.Play 按动作名映射下发 SetEmotion（Anger→怒り，延迟先行，尾段复位）。
    /// 2026-08-25：单 morph → Morph组合（一个情绪=多个 blendShape 同时驱动，如生气=嘴+眉+眼），
    /// 表结构升级后场景序列化值由接线脚本统一重建。
    /// </summary>
    public class PetEmotionController : MonoBehaviour
    {
        [System.Serializable]
        public class Morph目标
        {
            [Tooltip("BlendShape 名")] public string Morph名;
            [Tooltip("目标权重 0-100")] public float 权重 = 100f;
        }

        [System.Serializable]
        public class 情绪配置
        {
            [Tooltip("情绪名，与动作映射表对应")] public string 情绪名;
            [Tooltip("morph 组合（同组同时淡入/保持/复位）")] public Morph目标[] Morph组合;
            [Tooltip("淡入时长（秒）")] public float 过渡秒 = 0.25f;
            [Tooltip("指令下发后延迟生效（秒，表情先行于动作爆发）")] public float 延迟秒 = 0f;
            [Tooltip("保持时长（秒）；<0 = 持续到下一次指令；>0 = 到时自动回默认脸")] public float 保持秒 = -1f;
            [Tooltip("复位时长（秒）")] public float 复位秒 = 0.33f;
        }

        [Header("情绪表（morph 组合映射，GI 官方 Face 网格 49 morph）")]
        [SerializeField] private 情绪配置[] 情绪表 = new[]
        {
            new 情绪配置 { 情绪名 = "Anger", Morph组合 = new[] { new Morph目标 { Morph名 = "Mouth_Angry01" }, new Morph目标 { Morph名 = "Brow_Angry_L", 权重 = 80 }, new Morph目标 { Morph名 = "Brow_Angry_R", 权重 = 80 }, new Morph目标 { Morph名 = "Eye_Hostility", 权重 = 45 } } },
            new 情绪配置 { 情绪名 = "Happy", Morph组合 = new[] { new Morph目标 { Morph名 = "Mouth_Smile01" }, new Morph目标 { Morph名 = "Brow_Smily_L", 权重 = 55 }, new Morph目标 { Morph名 = "Brow_Smily_R", 权重 = 55 } } },
            new 情绪配置 { 情绪名 = "Sneer", Morph组合 = new[] { new Morph目标 { Morph名 = "Mouth_Smile04" }, new Morph目标 { Morph名 = "Eye_Jito", 权重 = 35 } } },
            new 情绪配置 { 情绪名 = "Shy", Morph组合 = new[] { new Morph目标 { Morph名 = "Mouth_Smile05", 权重 = 85 }, new Morph目标 { Morph名 = "Brow_Shy_L", 权重 = 70 }, new Morph目标 { Morph名 = "Brow_Shy_R", 权重 = 70 } } },
            new 情绪配置 { 情绪名 = "Confuse", Morph组合 = new[] { new Morph目标 { Morph名 = "Brow_Trouble_L" }, new Morph目标 { Morph名 = "Brow_Trouble_R" }, new Morph目标 { Morph名 = "Mouth_E01", 权重 = 30 } } },
            new 情绪配置 { 情绪名 = "Think", Morph组合 = new[] { new Morph目标 { Morph名 = "Brow_Serious_L", 权重 = 65 }, new Morph目标 { Morph名 = "Brow_Serious_R", 权重 = 65 }, new Morph目标 { Morph名 = "Mouth_U01", 权重 = 30 } } },
            new 情绪配置 { 情绪名 = "得意", Morph组合 = new[] { new Morph目标 { Morph名 = "Mouth_Doya01" }, new Morph目标 { Morph名 = "Brow_Up_L", 权重 = 45 }, new Morph目标 { Morph名 = "Brow_Up_R", 权重 = 45 } } },
            new 情绪配置 { 情绪名 = "期待", Morph组合 = new[] { new Morph目标 { Morph名 = "Brow_Up_L", 权重 = 80 }, new Morph目标 { Morph名 = "Brow_Up_R", 权重 = 80 }, new Morph目标 { Morph名 = "Mouth_Smile03", 权重 = 55 } } },
            new 情绪配置 { 情绪名 = "拒绝", Morph组合 = new[] { new Morph目标 { Morph名 = "Brow_Angry_L", 权重 = 55 }, new Morph目标 { Morph名 = "Brow_Angry_R", 权重 = 55 }, new Morph目标 { Morph名 = "Mouth_Line01", 权重 = 60 } } },
            new 情绪配置 { 情绪名 = "Sleepy", Morph组合 = new[] { new Morph目标 { Morph名 = "Eye_Tired" }, new Morph目标 { Morph名 = "Mouth_N01", 权重 = 35 } } },
        };

        private SkinnedMeshRenderer smr;
        private int currentIdx = -1;      // 当前激活情绪（-1=无）
        private Coroutine activeRoutine;

        void Awake()
        {
            // 多 SMR 场景（2026-08-24 GI 官方模型）：morph 全在 Face SMR 上——取 morph 数最多的 SMR
            smr = PetMeshQuery.取Morph最多渲染器(transform);
        }

        /// <summary>
        /// 下发情绪（按情绪名查表）。重复下发同名情绪幂等重启；下发未知名清空当前情绪回默认脸。
        /// </summary>
        public void SetEmotion(string 情绪名)
        {
            if (smr == null) return;
            if (activeRoutine != null) StopCoroutine(activeRoutine);
            ResetCurrent();

            int idx = -1;
            for (int i = 0; i < 情绪表.Length; i++)
                if (情绪表[i].情绪名 == 情绪名) { idx = i; break; }
            if (idx < 0) return; // 未知名=仅清空

            currentIdx = idx;
            activeRoutine = StartCoroutine(EmotionRoutine(情绪表[idx]));
        }

        void ResetCurrent()
        {
            if (currentIdx < 0) return;
            foreach (var m in 情绪表[currentIdx].Morph组合)
            {
                var idx = MorphIndex(m.Morph名);
                if (idx >= 0) smr.SetBlendShapeWeight(idx, 0f);
            }
            currentIdx = -1;
        }

        int MorphIndex(string morphName)
        {
            var mesh = smr.sharedMesh;
            var idx = mesh.GetBlendShapeIndex(morphName);
            if (idx < 0) Debug.LogWarning($"[PetEmotion] morph 缺失: {morphName}");
            return idx;
        }

        IEnumerator EmotionRoutine(情绪配置 cfg)
        {
            var targets = new (int idx, float weight)[cfg.Morph组合.Length];
            for (int i = 0; i < cfg.Morph组合.Length; i++)
            {
                var idx = MorphIndex(cfg.Morph组合[i].Morph名);
                if (idx < 0) yield break;
                targets[i] = (idx, cfg.Morph组合[i].权重);
            }
            if (cfg.延迟秒 > 0f) yield return new WaitForSeconds(cfg.延迟秒);

            // 淡入
            float t = 0f;
            while (t < cfg.过渡秒)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / Mathf.Max(cfg.过渡秒, 1e-5f));
                foreach (var (idx, weight) in targets)
                    smr.SetBlendShapeWeight(idx, k * weight);
                yield return null;
            }
            foreach (var (idx, weight) in targets)
                smr.SetBlendShapeWeight(idx, weight);

            // 保持（<0 = 持续到下一次指令）
            if (cfg.保持秒 >= 0f)
            {
                yield return new WaitForSeconds(cfg.保持秒);
                // 复位（动作被打断时 SetEmotion 会 StopCoroutine，复位中断=正确）
                float r = 0f;
                while (r < cfg.复位秒)
                {
                    r += Time.deltaTime;
                    float k = 1f - Mathf.Clamp01(r / Mathf.Max(cfg.复位秒, 1e-5f));
                    foreach (var (idx, weight) in targets)
                        smr.SetBlendShapeWeight(idx, k * weight);
                    yield return null;
                }
                foreach (var (idx, weight) in targets)
                    smr.SetBlendShapeWeight(idx, 0f);
                currentIdx = -1;
            }
        }
    }
}
