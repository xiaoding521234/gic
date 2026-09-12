using System.Collections;
using UnityEngine;
using GIC.Framework;

namespace GIC.UI
{
    /// <summary>
    /// 结果展示 — HoldThenFly、FlyToPosition、最终展示
    /// </summary>
    public partial class WishDrawController
    {
        #region hold+fly

        private IEnumerator HoldThenFlyAndCount(RectTransform rect, Vector2 targetPos, float holdTime)
        {
            yield return HoldThenFly(rect, targetPos, holdTime);
            _holdThenFlyRemaining--;
        }

        private IEnumerator HoldThenFly(RectTransform rect, Vector2 targetPos, float holdTime)
        {
            yield return Wait.Seconds(holdTime);
            yield return FlyToPosition(rect, targetPos, resultCardSize / 160f);
        }

        private IEnumerator FlyToPosition(RectTransform rect, Vector2 targetPos, float targetScale = -1f)
        {
            Vector2 startPos = rect.anchoredPosition;
            Vector3 startScale = rect.localScale;
            Vector3 endScale = targetScale > 0 ? Vector3.one * targetScale : startScale;
            float duration = 0.4f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                rect.anchoredPosition = Vector2.Lerp(startPos, targetPos, eased);
                rect.localScale = Vector3.Lerp(startScale, endScale, eased);
                yield return null;
            }
            rect.anchoredPosition = targetPos;
            rect.localScale = endScale;
        }

        #endregion

        #region 最终展示

        private IEnumerator ShowFinalDisplay()
        {
            int totalShots = _flow.TotalShots;

            StopHoldThenFlyCoroutines();

            cardTrack.gameObject.SetActive(false);
            countdownBar.gameObject.SetActive(false);

            finalDisplayContainer.gameObject.SetActive(true);
            finalDisplayCanvasGroup.alpha = 0f;

            float startX = -(totalShots - 1) * (finalCardSize + resultCardSpacing) * 0.5f;
            for (int i = 0; i < _resultCards.Count; i++)
            {
                var rect = _resultCards[i];
                if (rect == null) continue;
                rect.transform.SetParent(finalDisplayContainer, true);
                float x = startX + i * (finalCardSize + resultCardSpacing);
                rect.localScale = Vector3.one * (finalCardSize / 160f);
                StartCoroutine(FlyToPosition(rect, new Vector2(x, 0f)));
            }

            float fadeElapsed = 0f;
            float fadeDuration = 0.3f;
            while (fadeElapsed < fadeDuration)
            {
                fadeElapsed += Time.deltaTime;
                finalDisplayCanvasGroup.alpha = fadeElapsed / fadeDuration;
                yield return null;
            }
            finalDisplayCanvasGroup.alpha = 1f;

            yield return new WaitUntil(WaitForAnyTap.Any);

            fadeElapsed = 0f;
            while (fadeElapsed < fadeDuration)
            {
                fadeElapsed += Time.deltaTime;
                finalDisplayCanvasGroup.alpha = 1f - fadeElapsed / fadeDuration;
                yield return null;
            }

            finalDisplayContainer.gameObject.SetActive(false);
            resultContainer.gameObject.SetActive(false);
            if (drawRoot != null) drawRoot.SetActive(false);

            ClearTrackCards();
            ClearCardPool();

            // 最终展示结束后才解除输入锁，之前保持锁定防止 CloseUI 关闭整个祈愿场景
            _flow?.Reset();
            _isWishActive = false;
            InputLocks.Pop(this, InputLockReason.WishInProgress);
        }

        #endregion
    }
}
