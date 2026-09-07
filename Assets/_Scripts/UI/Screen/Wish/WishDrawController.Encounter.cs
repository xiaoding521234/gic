using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using GIC.Data;

namespace GIC.UI
{
    /// <summary>
    /// 相遇之线/纠缠之线 — 卡片抖动+发光动画（纯表现层）
    /// 业务逻辑已在 WishFlowController.PlanShot 中预计算。
    /// 2026-09-07 拍板：纠缠之线不改变相遇的抖动发光表现（同款金色+基础强度），
    /// 差异化一律走"叠加"特效（瞄准期圆环风暴/暗幕等，见 WishDrawController.Effects.cs）。
    /// </summary>
    public partial class WishDrawController
    {
        /// <summary>
        /// 卡片抖动+越来越亮动画
        /// 每个星级阶段独立持续时间，幅度和持续时间都随星级递增
        /// 通过预制体上的 glowImage（Additive材质）控制亮度，不修改原始 Image 颜色
        /// </summary>
        private IEnumerator CardShakeGlowCoroutine(RectTransform rect, Image glowImage, int startStarLevel, int upgradeCount)
        {
            Vector2 basePos = rect.anchoredPosition;

            Color glowColor = StarVisualConfig.EncounterGlowColor;

            float[] phaseDurations = new float[upgradeCount + 1];
            float totalDuration = 0f;
            for (int i = 0; i <= upgradeCount; i++)
            {
                int star = Mathf.Clamp(startStarLevel + i, 1, 5);
                phaseDurations[i] = StarVisualConfig.GetShakeDuration(star);
                totalDuration += phaseDurations[i];
            }

            float elapsed = 0f;
            float shakeTimer = 0f;
            Vector2 currentShake = Vector2.zero;
            Vector2 targetShake = Vector2.zero;
            while (elapsed < totalDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / totalDuration;

                float phaseStart = 0f;
                int phaseIndex = 0;
                for (int i = 0; i < phaseDurations.Length; i++)
                {
                    if (elapsed < phaseStart + phaseDurations[i])
                    {
                        phaseIndex = i;
                        break;
                    }
                    phaseStart += phaseDurations[i];
                    phaseIndex = i;
                }
                float phaseT = (elapsed - phaseStart) / phaseDurations[phaseIndex];
                int currentStar = Mathf.Clamp(startStarLevel + phaseIndex, 1, 5);

                float baseIntensity = StarVisualConfig.GetShakeIntensity(currentStar);
                float shakeIntensity = baseIntensity * phaseT;

                shakeTimer += Time.deltaTime;
                if (shakeTimer >= StarVisualConfig.ShakeInterval)
                {
                    shakeTimer = 0f;
                    targetShake = new Vector2(
                        Random.Range(-shakeIntensity, shakeIntensity),
                        Random.Range(-shakeIntensity, shakeIntensity));
                }
                currentShake = Vector2.Lerp(currentShake, targetShake, Time.deltaTime / StarVisualConfig.ShakeInterval);
                rect.anchoredPosition = basePos + currentShake;

                if (glowImage != null)
                {
                    float maxGlow = StarVisualConfig.GetMaxGlow(currentStar);
                    float glowT = t * t * (3f - 2f * t);
                    float alpha = Mathf.Clamp01(maxGlow * glowT);
                    glowImage.color = new Color(glowColor.r, glowColor.g, glowColor.b, alpha);
                }

                yield return null;
            }
        }
    }
}
