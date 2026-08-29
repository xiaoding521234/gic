using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace GIC.Tool
{
    /// <summary>
    /// 光柱特效 — 光柱与地面闪光动画（核心光束与外层光晕共用 AnimateBeam）
    /// </summary>
    public partial class LightPillarEffect
    {
        /// <summary>
        /// 光柱上升 → 过冲回弹 → 持续脉冲（直到 Stop() 被调用）
        /// </summary>
        private IEnumerator AnimateBeam(Image img, Color color, float burstAlpha, float sustainAlpha, float delay, bool useOvershoot)
        {
            if (delay > 0f)
                yield return new WaitForSecondsRealtime(delay);

            RectTransform rect = img.rectTransform;
            img.color = new Color(color.r, color.g, color.b, burstAlpha);
            rect.localScale = new Vector3(1f, 0f, 1f);

            // 上升
            float t = 0f;
            float targetScale = useOvershoot ? coreOvershoot : 1f;
            while (t < riseTime)
            {
                t += Time.unscaledDeltaTime;
                float p = t / riseTime;
                float scaleY = Mathf.LerpUnclamped(0f, targetScale, EaseOutCubic(p));
                // 上升过程中 alpha 从爆发值过渡到持续值
                float alpha = Mathf.Lerp(burstAlpha, sustainAlpha, p);
                img.color = new Color(color.r, color.g, color.b, alpha);
                rect.localScale = new Vector3(1f, scaleY, 1f);
                yield return null;
            }

            // 过冲回弹
            if (useOvershoot && coreOvershoot > 1f)
            {
                t = 0f;
                float settleDur = 0.08f;
                while (t < settleDur)
                {
                    t += Time.unscaledDeltaTime;
                    float p = t / settleDur;
                    float scaleY = Mathf.Lerp(coreOvershoot, 1f, EaseOutCubic(p));
                    rect.localScale = new Vector3(1f, scaleY, 1f);
                    yield return null;
                }
            }

            rect.localScale = Vector3.one;

            // 持续脉冲（无限循环，由 Stop 中断）
            float pulseT = 0f;
            while (true)
            {
                if (img == null) yield break;
                pulseT += Time.unscaledDeltaTime;
                float pulse = 1f + Mathf.Sin(pulseT * pulseSpeed) * pulseAmp;
                img.color = new Color(color.r, color.g, color.b, sustainAlpha * pulse);
                yield return null;
            }
        }

        /// <summary>
        /// 地面闪光：从中心向外扩散并淡出
        /// </summary>
        private IEnumerator AnimateGroundFlash(Image img, Color color, float maxAlpha)
        {
            img.transform.localScale = Vector3.zero;

            float t = 0f;
            while (t < flashTime)
            {
                t += Time.unscaledDeltaTime;
                float p = t / flashTime;
                float scale = Mathf.LerpUnclamped(0f, 1.6f, EaseOutCubic(p));
                float alpha = Mathf.Lerp(maxAlpha, 0f, p * p);
                img.transform.localScale = Vector3.one * scale;
                img.color = new Color(color.r, color.g, color.b, alpha);
                yield return null;
            }

            img.gameObject.SetActive(false);
        }
    }
}
