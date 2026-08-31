using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

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
        public class MorphTarget
        {
            [Tooltip("BlendShape 名"), InspectorName("Morph名")] public string morphName;
            [Tooltip("目标权重 0-100"), InspectorName("权重")] public float weight = 100f;
        }

        [System.Serializable]
        public class EmotionConfig
        {
            [InspectorName("情绪名")]
            [Tooltip("情绪名，与动作映射表对应")] public string emotionName;
            [Tooltip("morph 组合（同组同时淡入/保持/复位）"), InspectorName("Morph组合")] public MorphTarget[] morphSet;
            [Tooltip("淡入时长（秒）"), InspectorName("过渡秒")] public float TransitionSeconds = 0.25f;
            [InspectorName("延迟秒")]
            [Tooltip("指令下发后延迟生效（秒，表情先行于动作爆发）")] public float delaySec = 0f;
            [InspectorName("保持秒")]
            [Tooltip("保持时长（秒）；<0 = 持续到下一次指令；>0 = 到时自动回默认脸")] public float holdSec = -1f;
            [InspectorName("复位秒")]
            [Tooltip("复位时长（秒）")] public float resetSec = 0.33f;
        }

        [Header("情绪表（morph 组合映射，GI 官方 Face 网格 49 morph）")]
        [InspectorName("情绪表")]
        [SerializeField] private EmotionConfig[] emotionTable = new[]
        {
            new EmotionConfig { emotionName = "Anger", morphSet = new[] { new MorphTarget { morphName = "Mouth_Angry01" }, new MorphTarget { morphName = "Brow_Angry_L", weight = 80 }, new MorphTarget { morphName = "Brow_Angry_R", weight = 80 }, new MorphTarget { morphName = "Eye_Hostility", weight = 45 } } },
            new EmotionConfig { emotionName = "Happy", morphSet = new[] { new MorphTarget { morphName = "Mouth_Smile01" }, new MorphTarget { morphName = "Brow_Smily_L", weight = 55 }, new MorphTarget { morphName = "Brow_Smily_R", weight = 55 } } },
            new EmotionConfig { emotionName = "Sneer", morphSet = new[] { new MorphTarget { morphName = "Mouth_Smile04" }, new MorphTarget { morphName = "Eye_Jito", weight = 35 } } },
            new EmotionConfig { emotionName = "Shy", morphSet = new[] { new MorphTarget { morphName = "Mouth_Smile05", weight = 85 }, new MorphTarget { morphName = "Brow_Shy_L", weight = 70 }, new MorphTarget { morphName = "Brow_Shy_R", weight = 70 } } },
            new EmotionConfig { emotionName = "Confuse", morphSet = new[] { new MorphTarget { morphName = "Brow_Trouble_L" }, new MorphTarget { morphName = "Brow_Trouble_R" }, new MorphTarget { morphName = "Mouth_E01", weight = 30 } } },
            new EmotionConfig { emotionName = "Think", morphSet = new[] { new MorphTarget { morphName = "Brow_Serious_L", weight = 65 }, new MorphTarget { morphName = "Brow_Serious_R", weight = 65 }, new MorphTarget { morphName = "Mouth_U01", weight = 30 } } },
            new EmotionConfig { emotionName = "得意", morphSet = new[] { new MorphTarget { morphName = "Mouth_Doya01" }, new MorphTarget { morphName = "Brow_Up_L", weight = 45 }, new MorphTarget { morphName = "Brow_Up_R", weight = 45 } } },
            new EmotionConfig { emotionName = "期待", morphSet = new[] { new MorphTarget { morphName = "Brow_Up_L", weight = 80 }, new MorphTarget { morphName = "Brow_Up_R", weight = 80 }, new MorphTarget { morphName = "Mouth_Smile03", weight = 55 } } },
            new EmotionConfig { emotionName = "拒绝", morphSet = new[] { new MorphTarget { morphName = "Brow_Angry_L", weight = 55 }, new MorphTarget { morphName = "Brow_Angry_R", weight = 55 }, new MorphTarget { morphName = "Mouth_Line01", weight = 60 } } },
            new EmotionConfig { emotionName = "Sleepy", morphSet = new[] { new MorphTarget { morphName = "Eye_Tired" }, new MorphTarget { morphName = "Mouth_N01", weight = 35 } } },
        };

        private SkinnedMeshRenderer smr;
        private int currentIdx = -1;      // 当前激活情绪（-1=无）
        private Coroutine activeRoutine;
        private readonly System.Collections.Generic.HashSet<string> _warnedMorphs = new System.Collections.Generic.HashSet<string>(); // 告警去重（错配名反复触发不再刷屏）

        void Awake()
        {
            // 多 SMR 场景（2026-08-24 GI 官方模型）：morph 全在 Face SMR 上——取 morph 数最多的 SMR
            smr = PetMeshQuery.GetRichestMorphRenderer(transform);
        }

        void OnDisable()
        {
            // 组件禁用/协程中断兜底：morph 权重清零防停留半权重表情（SetActive(false) 或依赖断连时）
            ResetCurrent();
        }

        /// <summary>
        /// 下发情绪（按情绪名查表）。重复下发同名情绪幂等重启；下发未知名清空当前情绪回默认脸。
        /// </summary>
        public void SetEmotion(string emotionName)
        {
            if (smr == null) return;
            if (activeRoutine != null) StopCoroutine(activeRoutine);
            ResetCurrent();

            int idx = -1;
            for (int i = 0; i < emotionTable.Length; i++)
                if (emotionTable[i].emotionName == emotionName) { idx = i; break; }
            if (idx < 0) return; // 未知名=仅清空

            currentIdx = idx;
            activeRoutine = StartCoroutine(EmotionRoutine(emotionTable[idx]));
        }

        void ResetCurrent()
        {
            if (currentIdx < 0) return;
            foreach (var m in emotionTable[currentIdx].morphSet)
            {
                var idx = MorphIndex(m.morphName);
                if (idx >= 0) smr.SetBlendShapeWeight(idx, 0f);
            }
            currentIdx = -1;
        }

        int MorphIndex(string morphName)
        {
            var mesh = smr.sharedMesh;
            var idx = mesh.GetBlendShapeIndex(morphName);
            if (idx < 0 && _warnedMorphs.Add(morphName)) // 同名只警告一次（情绪表反复触发不刷屏）
                Debug.LogWarning($"[PetEmotion] morph 缺失: {morphName}");
            return idx;
        }

        IEnumerator EmotionRoutine(EmotionConfig cfg)
        {
            var targets = new (int idx, float weight)[cfg.morphSet.Length];
            for (int i = 0; i < cfg.morphSet.Length; i++)
            {
                var idx = MorphIndex(cfg.morphSet[i].morphName);
                if (idx < 0) yield break;
                targets[i] = (idx, cfg.morphSet[i].weight);
            }
            if (cfg.delaySec > 0f) yield return new WaitForSeconds(cfg.delaySec);

            // 淡入
            float t = 0f;
            while (t < cfg.TransitionSeconds)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / Mathf.Max(cfg.TransitionSeconds, 1e-5f));
                foreach (var (idx, weight) in targets)
                    smr.SetBlendShapeWeight(idx, k * weight);
                yield return null;
            }
            foreach (var (idx, weight) in targets)
                smr.SetBlendShapeWeight(idx, weight);

            // 保持（<0 = 持续到下一次指令）
            if (cfg.holdSec >= 0f)
            {
                yield return new WaitForSeconds(cfg.holdSec);
                // 复位（动作被打断时 SetEmotion 会 StopCoroutine，复位中断=正确）
                float r = 0f;
                while (r < cfg.resetSec)
                {
                    r += Time.deltaTime;
                    float k = 1f - Mathf.Clamp01(r / Mathf.Max(cfg.resetSec, 1e-5f));
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
