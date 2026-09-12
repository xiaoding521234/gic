// ==================== SettingsScreen.Animation.cs（入场/退场动画 + 动画缓存） ====================
using System.Collections.Generic;
using UnityEngine;
using System.Collections;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
using GIC.Tool;
namespace GIC.UI
{


    public partial class SettingsScreen
    {
        // 动画缓存
        private RectTransform topPanelRect;
        private RectTransform leftPanelRect;
        private RectTransform centerPanelRect;
        private Vector2 topPanelTargetPos;
        private Vector2 leftPanelTargetPos;
        private Vector2 centerPanelTargetPos;
        private bool animationsCached = false;

        private void CacheAnimationPositions()
        {
            if (animationsCached) return;

            if (topPanel != null)
            {
                topPanelRect = topPanel.GetComponent<RectTransform>();
                if (topPanelRect != null)
                    topPanelTargetPos = topPanelRect.anchoredPosition;
            }

            if (leftPanel != null)
            {
                leftPanelRect = leftPanel.GetComponent<RectTransform>();
                if (leftPanelRect != null)
                    leftPanelTargetPos = leftPanelRect.anchoredPosition;
            }

            if (centerPanel != null)
            {
                centerPanelRect = centerPanel.GetComponent<RectTransform>();
                if (centerPanelRect != null)
                    centerPanelTargetPos = centerPanelRect.anchoredPosition;
            }

            animationsCached = true;
        }

        /// <summary>
        /// 入场起始态：三面板移偏移位 + 内容区透明。幂等（目标位取自缓存）。
        /// OnShow（实例化同帧）与 PlayEnterAnimation（Start）双调用——OnShow 先行保证首帧渲染不可见
        /// （prefab 序列化态=完成态，Start 晚于首帧渲染，只靠 Start 会闪现一帧，docs/14 §37）。
        /// </summary>
        private void SetEntryOffsets()
        {
            if (topPanelRect != null)
                topPanelRect.anchoredPosition = topPanelTargetPos + Vector2.up * topPanelSlideOffset;

            if (leftPanelRect != null)
                leftPanelRect.anchoredPosition = leftPanelTargetPos + Vector2.left * leftPanelSlideOffset;

            if (centerPanelRect != null)
                centerPanelRect.anchoredPosition = centerPanelTargetPos + Vector2.down * centerFadeOffset;

            if (centerGroup != null)
                centerGroup.alpha = 0f;
        }

        private void PlayEnterAnimation()
        {
            SetEntryOffsets(); // 幂等：OnShow 已设置，此处兜底（重开/时序异常时保底）
            StartCoroutine(PlayEnterAnimationCoroutine());
        }

        private IEnumerator PlayEnterAnimationCoroutine()
        {
            InputLocks.Push(this, InputLockReason.Entering);

            // 首帧跳过计时（P2 回归修正）：面板制下 Resources.Load+Instantiate 同步发生在点击帧，
            // Start 在下一帧执行，首轮 Time.deltaTime=尖峰帧时长（常 >0.2s 动画时长）→ while 直接跳出=入场瞬完成。
            // 旧场景制为异步分帧加载无此问题。先 yield 一帧消化尖峰帧的 deltaTime。
            yield return null;

            float elapsed = 0f;

            while (elapsed < panelSlideDuration)
            {
                elapsed += Time.deltaTime;
                float t = slideCurve.Evaluate(elapsed / panelSlideDuration);

                if (topPanelRect != null)
                {
                    topPanelRect.anchoredPosition = Vector2.Lerp(
                        topPanelTargetPos + Vector2.up * topPanelSlideOffset,
                        topPanelTargetPos,
                        t
                    );
                }

                if (leftPanelRect != null)
                {
                    leftPanelRect.anchoredPosition = Vector2.Lerp(
                        leftPanelTargetPos + Vector2.left * leftPanelSlideOffset,
                        leftPanelTargetPos,
                        t
                    );
                }

                if (centerPanelRect != null)
                {
                    centerPanelRect.anchoredPosition = Vector2.Lerp(
                        centerPanelTargetPos + Vector2.down * centerFadeOffset,
                        centerPanelTargetPos,
                        t
                    );
                }

                if (centerGroup != null)
                {
                    centerGroup.alpha = Mathf.Lerp(0f, 1f, t);
                }

                yield return null;
            }

            // 确保最终位置
            if (topPanelRect != null)
                topPanelRect.anchoredPosition = topPanelTargetPos;
            if (leftPanelRect != null)
                leftPanelRect.anchoredPosition = leftPanelTargetPos;
            if (centerPanelRect != null)
                centerPanelRect.anchoredPosition = centerPanelTargetPos;
            if (centerGroup != null)
                centerGroup.alpha = 1f;

            InputLocks.Pop(this, InputLockReason.Entering);
        }

        private IEnumerator PlayExitAnimationCoroutine()
        {
            float elapsed = 0f;
            float slideOutDuration = panelSlideDuration * 0.7f;

            float startCenterAlpha = centerGroup != null ? centerGroup.alpha : 1f;

            while (elapsed < slideOutDuration)
            {
                elapsed += Time.deltaTime;
                float t = slideCurve.Evaluate(elapsed / slideOutDuration);

                if (topPanelRect != null)
                {
                    topPanelRect.anchoredPosition = Vector2.Lerp(
                        topPanelTargetPos,
                        topPanelTargetPos + Vector2.up * topPanelSlideOffset,
                        t
                    );
                }

                if (leftPanelRect != null)
                {
                    leftPanelRect.anchoredPosition = Vector2.Lerp(
                        leftPanelTargetPos,
                        leftPanelTargetPos + Vector2.left * leftPanelSlideOffset,
                        t
                    );
                }

                if (centerPanelRect != null)
                {
                    centerPanelRect.anchoredPosition = Vector2.Lerp(
                        centerPanelTargetPos,
                        centerPanelTargetPos + Vector2.down * centerFadeOffset,
                        t
                    );
                }

                if (centerGroup != null)
                {
                    centerGroup.alpha = Mathf.Lerp(startCenterAlpha, 0f, t);
                }

                yield return null;
            }

            if (centerGroup != null)
                centerGroup.alpha = 0f;
            // 收尾（Closing 锁 Pop + GoBack）由 ScreenBase.CloseScreen 模板统一处理
        }
    }
}
