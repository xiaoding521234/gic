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
    /// </summary>
    public class PetEmotionController : MonoBehaviour
    {
        [System.Serializable]
        public class 情绪配置
        {
            [Tooltip("情绪名，与动作映射表对应")] public string 情绪名;
            [Tooltip("MMD BlendShape 名（日文，如 怒り/にこり/困る）")] public string Morph名;
            [Tooltip("目标权重 0-100")] public float 强度 = 100f;
            [Tooltip("淡入时长（秒）")] public float 过渡秒 = 0.25f;
            [Tooltip("指令下发后延迟生效（秒，表情先行于动作爆发）")] public float 延迟秒 = 0f;
            [Tooltip("保持时长（秒）；<0 = 持续到下一次指令；>0 = 到时自动回默认脸")] public float 保持秒 = -1f;
            [Tooltip("复位时长（秒）")] public float 复位秒 = 0.33f;
        }

        [Header("情绪表（MMD morph 映射）")]
        [SerializeField] private 情绪配置[] 情绪表 = new[]
        {
            new 情绪配置 { 情绪名 = "Anger", Morph名 = "怒り", 强度 = 100f, 过渡秒 = 0.25f, 延迟秒 = 0f, 保持秒 = -1f },
        };

        private SkinnedMeshRenderer smr;
        private int currentIdx = -1;      // 当前激活情绪（-1=无）
        private Coroutine activeRoutine;

        void Awake()
        {
            smr = GetComponentInChildren<SkinnedMeshRenderer>();
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
            var cfg = 情绪表[currentIdx];
            var idx = MorphIndex(cfg);
            if (idx >= 0) smr.SetBlendShapeWeight(idx, 0f);
            currentIdx = -1;
        }

        int MorphIndex(情绪配置 cfg)
        {
            var mesh = smr.sharedMesh;
            var idx = mesh.GetBlendShapeIndex(cfg.Morph名);
            if (idx < 0) Debug.LogWarning($"[PetEmotion] morph 缺失: {cfg.Morph名}");
            return idx;
        }

        IEnumerator EmotionRoutine(情绪配置 cfg)
        {
            var morphIdx = MorphIndex(cfg);
            if (morphIdx < 0) yield break;
            if (cfg.延迟秒 > 0f) yield return new WaitForSeconds(cfg.延迟秒);

            // 淡入
            float t = 0f;
            while (t < cfg.过渡秒)
            {
                t += Time.deltaTime;
                smr.SetBlendShapeWeight(morphIdx, Mathf.Clamp01(t / Mathf.Max(cfg.过渡秒, 1e-5f)) * cfg.强度);
                yield return null;
            }
            smr.SetBlendShapeWeight(morphIdx, cfg.强度);

            // 保持（<0 = 持续到下一次指令）
            if (cfg.保持秒 >= 0f)
            {
                yield return new WaitForSeconds(cfg.保持秒);
                // 复位（动作被打断时 SetEmotion 会 StopCoroutine，复位中断=正确）
                float r = 0f;
                while (r < cfg.复位秒)
                {
                    r += Time.deltaTime;
                    smr.SetBlendShapeWeight(morphIdx, (1f - Mathf.Clamp01(r / Mathf.Max(cfg.复位秒, 1e-5f))) * cfg.强度);
                    yield return null;
                }
                smr.SetBlendShapeWeight(morphIdx, 0f);
                currentIdx = -1;
            }
        }
    }
}
