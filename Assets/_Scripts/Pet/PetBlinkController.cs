using System.Collections;
using UnityEngine;

namespace GIC.Pet
{
    /// <summary>
    /// 程序化眨眼（行业通行 face/body 分层架构的 face 侧，docs/19 §2.6）：
    /// 眨眼不烘焙进身体动画 clip，由运行时随机 timer 驱动 morph 权重——对齐原神运行时
    /// 表情系统的观感（clip 固定周期眨眼被否：频率机械且 loop 内无法随机化）。
    /// 分工：眨眼=本组件（随机 2.5~5.5s 一次）；情绪表情（怒り等）仍归 clip（与动作
    /// 强绑定，原神过场同样烘焙进 clip）；两者 morph 不同槽互不干扰。Animation 组件
    /// 只覆盖 clip 中存在的曲线，故本组件独占的眨眼 morph 不会被动画冲掉（前提：
    /// clip 不含眨眼曲线，PaimonFacePipeline 已负责清除）。
    /// </summary>
    public class PetBlinkController : MonoBehaviour
    {
        [Header("眨眼参数")]
        [SerializeField] private float 最小间隔秒 = 2.5f;   // 真人眨眼间隔 2-10s，取下段贴近原神观感
        [SerializeField] private float 最大间隔秒 = 5.5f;
        [SerializeField] private float 眨眼时长秒 = 0.13f;  // 升 0.05s 保持 0.03s 降 0.05s

        [Header("morph 名（2026-08-24 官方模型适配：默认 MMD 名，GI 官方模型配 Eye_WinkA）")]
        [SerializeField] private string 左眨眼morph名 = "ウィンク";
        [SerializeField] private string 右眨眼morph名 = "ウィンク右";

        private SkinnedMeshRenderer smr;
        private int winkL = -1, winkR = -1; // ウィンク / ウィンク右（MMD 无まばたき，双眼同权重合成）
        private Coroutine loop;
        private bool _静默; // 单次动作期间暂停眨眼（2026-08-24：morph 重评估与动作叠加互相放大顿挫，1-4s 动作少眨一次不可见）

        /// <summary>单次动作期间静默眨眼（PetBehaviorController 调；回待机时恢复）</summary>
        public void Set静默(bool 静默)
        {
            _静默 = 静默;
            if (静默 && smr != null && winkL >= 0)
            {
                smr.SetBlendShapeWeight(winkL, 0f);
                smr.SetBlendShapeWeight(winkR, 0f);
            }
        }

        void Awake()
        {
            // 多 SMR 场景（2026-08-24 GI 官方模型）：Body/Cloak/EyeStar/Face 并列，morph 全在 Face 上
            smr = PetMeshQuery.取Morph最多渲染器(transform);
            if (smr == null) { enabled = false; return; }
            var mesh = smr.sharedMesh;
            winkL = mesh.GetBlendShapeIndex(左眨眼morph名);
            winkR = mesh.GetBlendShapeIndex(右眨眼morph名);
            if (winkL < 0 || winkR < 0)
            {
                Debug.LogWarning($"[PetBlink] 模型缺少眨眼 morph（ウィンク={winkL} ウィンク右={winkR}），眨眼已禁用");
                enabled = false;
            }
        }

        void OnEnable()
        {
            if (winkL < 0) return;
            loop = StartCoroutine(BlinkLoop());
        }

        void OnDisable()
        {
            if (loop != null) StopCoroutine(loop);
            if (smr != null && winkL >= 0) { smr.SetBlendShapeWeight(winkL, 0f); smr.SetBlendShapeWeight(winkR, 0f); }
        }

        IEnumerator BlinkLoop()
        {
            while (true)
            {
                // 零分配等待（原 WaitForSeconds 每次 new 一个对象——GC 源之一，2026-08-24 实测基线 GC≈1.2 次/s）
                float wait = Random.Range(最小间隔秒, 最大间隔秒);
                float t0 = Time.time;
                while (Time.time - t0 < wait || _静默) yield return null;
                PetDiag.上次眨眼 = Time.unscaledTime; // 顿挫诊断标记（PetFrameStats 回查）
                float t = 0f;
                while (t < 眨眼时长秒 && !_静默)
                {
                    t += Time.deltaTime;
                    float w = BlinkWeight(t / 眨眼时长秒) * 100f;
                    smr.SetBlendShapeWeight(winkL, w);
                    smr.SetBlendShapeWeight(winkR, w);
                    yield return null;
                }
                smr.SetBlendShapeWeight(winkL, 0f);
                smr.SetBlendShapeWeight(winkR, 0f);
            }
        }

        /// <summary>0..1 眨眼进度 → 0..1 权重（升 40% / 峰值保持 25% / 降 35%）</summary>
        static float BlinkWeight(float f)
        {
            if (f < 0.4f) return f / 0.4f;
            if (f <= 0.65f) return 1f;
            return 1f - (f - 0.65f) / 0.35f;
        }
    }
}
