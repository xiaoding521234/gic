// ==================== BackpackScreen.Animation.cs ====================
using System.Collections;
using UnityEngine;

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
        float elapsed = 0f;
        while (elapsed < panelSlideDuration)
        {
            elapsed += Time.deltaTime;
            float t = slideCurve.Evaluate(elapsed / panelSlideDuration);
            AnimateSlide(t, isOut: false);
            yield return null;
        }
        SnapToTarget();
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
            yield return null;
        }

        if (centerCanvasGroup != null) centerCanvasGroup.alpha = 0f;
        if (cardDetailCanvasGroup != null) cardDetailCanvasGroup.alpha = 0f;
        GameScene.Instance.GoBack();
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