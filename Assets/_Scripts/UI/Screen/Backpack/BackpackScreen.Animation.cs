// ==================== BackpackScreen.Animation.cs ====================
using System.Collections;
using UnityEngine;
using GIC.Framework;
using GIC.Battle;
using GIC.Data;
using GIC.Data.Event;
using GIC.Tool;
namespace GIC.UI
{


    public partial class BackpackScreen
    {
        private Vector2 topPanelTargetPosition;
        private Vector2 bottomPanelTargetPosition;
        private Vector2 leftButtonTargetPosition;
        private Vector2 rightButtonTargetPosition;
        private RectTransform topPanelRect;
        private RectTransform bottomPanelRect;
        private RectTransform leftButtonRect;
        private RectTransform rightButtonRect;

        // 毛玻璃底图（BackPanel：UIBlurCapture 的 Image）——扫入扫出驱动
        //（双层配方 docs/14 §38b：变暗层 BackDim 瞬时，模糊层才扫）
        private UnityEngine.UI.Image _blurBackdrop;

        private UnityEngine.UI.Image BlurBackdrop
        {
            get
            {
                if (_blurBackdrop == null)
                {
                    // BackPanel 在 Canvas 子树下、与脚本对象是兄弟——须从面板根搜（docs/14 §37 连坐）
                    var root = transform.parent != null ? transform.parent : transform;
                    var cap = root.GetComponentInChildren<GIC.UI.UIBlurCapture>(true);
                    if (cap != null) _blurBackdrop = cap.GetComponent<UnityEngine.UI.Image>();
                }
                return _blurBackdrop;
            }
        }

        private void CachePanelPositions()
        {
            if (topPanel != null)
            {
                topPanelRect = topPanel.GetComponent<RectTransform>();
                if (topPanelRect != null) topPanelTargetPosition = topPanelRect.anchoredPosition;
            }
            if (bottomPanel != null)
            {
                bottomPanelRect = bottomPanel.GetComponent<RectTransform>();
                if (bottomPanelRect != null) bottomPanelTargetPosition = bottomPanelRect.anchoredPosition;
            }
        }

        private void CacheButtonPositions()
        {
            if (nextButtonLeft != null)
            {
                leftButtonRect = nextButtonLeft.GetComponent<RectTransform>();
                if (leftButtonRect != null) leftButtonTargetPosition = leftButtonRect.anchoredPosition;
            }
            if (nextButtonRight != null)
            {
                rightButtonRect = nextButtonRight.GetComponent<RectTransform>();
                if (rightButtonRect != null) rightButtonTargetPosition = rightButtonRect.anchoredPosition;
            }
        }

        private void SetPanelsOffScreen()
        {
            if (topPanelRect != null) topPanelRect.anchoredPosition = topPanelTargetPosition + Vector2.up * panelSlideOffset;
            if (bottomPanelRect != null) bottomPanelRect.anchoredPosition = bottomPanelTargetPosition + Vector2.down * panelSlideOffset;
        }

        private void SetButtonsOffScreen()
        {
            float offset = buttonSlideOffset > 0 ? buttonSlideOffset : panelSlideOffset;
            if (leftButtonRect != null) leftButtonRect.anchoredPosition = leftButtonTargetPosition + Vector2.left * offset;
            if (rightButtonRect != null) rightButtonRect.anchoredPosition = rightButtonTargetPosition + Vector2.right * offset;
        }

        private IEnumerator PlaySlideInAnimation()
        {
            InputLocks.Push(this, InputLockReason.Entering);

            // 首帧跳过计时（docs/14 §37 纪律②）：打开帧 deltaTime 会吃到激活/加载尖峰帧时长
            yield return null;

            float elapsed = 0f;
            while (elapsed < panelSlideDuration)
            {
                // 秒开秒关守卫（docs/14 §37 纪律③）：释放 Entering 锁交由退场接管
                if (isClosing)
                {
                    InputLocks.Pop(this, InputLockReason.Entering);
                    yield break;
                }

                elapsed += Time.deltaTime;
                float t = slideCurve.Evaluate(elapsed / panelSlideDuration);
                AnimateSlide(t, isOut: false);
                if (BlurBackdrop != null) BlurBackdrop.fillAmount = t; // 模糊顶→底扫入（变暗层瞬时，双层配方）
                yield return null;
            }
            SnapToTarget();
            if (BlurBackdrop != null) BlurBackdrop.fillAmount = 1f;
            InputLocks.Pop(this, InputLockReason.Entering);
        }

        private IEnumerator CloseWithAnimation()
        {
            float elapsed = 0f;
            float slideOutDuration = panelSlideDuration * 0.7f;
            float centerStartAlpha = centerCanvasGroup?.alpha ?? 1f;
            float detailStartAlpha = cardDetailCanvasGroup?.alpha ?? 1f;

            while (elapsed < slideOutDuration)
            {
                elapsed += Time.deltaTime;
                float t = slideCurve.Evaluate(elapsed / slideOutDuration);
                AnimateSlide(t, isOut: true);
                if (centerCanvasGroup != null) centerCanvasGroup.alpha = Mathf.Lerp(centerStartAlpha, 0f, t);
                if (cardDetailCanvasGroup != null) cardDetailCanvasGroup.alpha = Mathf.Lerp(detailStartAlpha, 0f, t);
                if (BlurBackdrop != null) BlurBackdrop.fillAmount = 1f - t; // 模糊底→顶扫出（用户拍板"关闭同理"）
                yield return null;
            }

            if (centerCanvasGroup != null) centerCanvasGroup.alpha = 0f;
            if (cardDetailCanvasGroup != null) cardDetailCanvasGroup.alpha = 0f;
            if (BlurBackdrop != null) BlurBackdrop.fillAmount = 0f;
            // 收尾（Closing 锁 Pop + GoBack）由 ScreenBase.CloseScreen 模板统一处理
        }

        private void AnimateSlide(float t, bool isOut)
        {
            float buttonOffset = buttonSlideOffset > 0 ? buttonSlideOffset : panelSlideOffset;

            if (isOut)
            {
                if (topPanelRect != null) topPanelRect.anchoredPosition = Vector2.Lerp(topPanelTargetPosition, topPanelTargetPosition + Vector2.up * panelSlideOffset, t);
                if (bottomPanelRect != null) bottomPanelRect.anchoredPosition = Vector2.Lerp(bottomPanelTargetPosition, bottomPanelTargetPosition + Vector2.down * panelSlideOffset, t);
                if (leftButtonRect != null) leftButtonRect.anchoredPosition = Vector2.Lerp(leftButtonTargetPosition, leftButtonTargetPosition + Vector2.left * buttonOffset, t);
                if (rightButtonRect != null) rightButtonRect.anchoredPosition = Vector2.Lerp(rightButtonTargetPosition, rightButtonTargetPosition + Vector2.right * buttonOffset, t);
            }
            else
            {
                if (topPanelRect != null) topPanelRect.anchoredPosition = Vector2.Lerp(topPanelTargetPosition + Vector2.up * panelSlideOffset, topPanelTargetPosition, t);
                if (bottomPanelRect != null) bottomPanelRect.anchoredPosition = Vector2.Lerp(bottomPanelTargetPosition + Vector2.down * panelSlideOffset, bottomPanelTargetPosition, t);
                if (leftButtonRect != null) leftButtonRect.anchoredPosition = Vector2.Lerp(leftButtonTargetPosition + Vector2.left * buttonOffset, leftButtonTargetPosition, t);
                if (rightButtonRect != null) rightButtonRect.anchoredPosition = Vector2.Lerp(rightButtonTargetPosition + Vector2.right * buttonOffset, rightButtonTargetPosition, t);
            }
        }

        private void SnapToTarget()
        {
            if (topPanelRect != null) topPanelRect.anchoredPosition = topPanelTargetPosition;
            if (bottomPanelRect != null) bottomPanelRect.anchoredPosition = bottomPanelTargetPosition;
            if (leftButtonRect != null) leftButtonRect.anchoredPosition = leftButtonTargetPosition;
            if (rightButtonRect != null) rightButtonRect.anchoredPosition = rightButtonTargetPosition;
        }
    }
}



