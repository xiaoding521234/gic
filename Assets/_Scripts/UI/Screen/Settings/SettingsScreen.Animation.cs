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

        // 毛玻璃底图（BackPanel：UIBlurCapture 的 Image）——扫入扫出动画驱动
        // （2026-09-12 用户拍板：毛玻璃不再瞬间铺满全屏=观感闪烁源，改为顶→底扫入，时长与淡入一致）
        private UnityEngine.UI.Image _blurBackdrop;

        private UnityEngine.UI.Image BlurBackdrop
        {
            get
            {
                if (_blurBackdrop == null)
                {
                    // BackPanel 在 Canvas 子树下、与 SettingsScreen 脚本对象是兄弟——须从面板根搜
                    //（GetComponentInChildren 只搜自身后代，从脚本对象起搜必空——冒烟实证 fill 全程=0）
                    var root = transform.parent != null ? transform.parent : transform;
                    var cap = root.GetComponentInChildren<GIC.UI.UIBlurCapture>(true);
                    if (cap != null) _blurBackdrop = cap.GetComponent<UnityEngine.UI.Image>();
                }
                return _blurBackdrop;
            }
        }

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

            // 毛玻璃起始态：fill=0（顶→底扫入的起点；prefab 已预置 Filled/Vertical/Top）
            var blur = BlurBackdrop;
            if (blur != null) blur.fillAmount = 0f;
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
                // 秒开秒关守卫（docs/14 §37 纪律③）：关闭启动即提前跳出，
                // 释放 Entering 锁交由退场动画接管画面（残余泄漏由 OnDisable PopAll 兜底）
                if (isClosing)
                {
                    InputLocks.Pop(this, InputLockReason.Entering);
                    yield break;
                }

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

                // 毛玻璃顶→底扫入（时长=淡入动画，用户拍板 2026-09-12）
                if (BlurBackdrop != null)
                    BlurBackdrop.fillAmount = t;

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
            if (BlurBackdrop != null)
                BlurBackdrop.fillAmount = 1f;

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

                // 毛玻璃底部→顶部扫出（与入场镜像，用户拍板"关闭时同理"）
                if (BlurBackdrop != null)
                    BlurBackdrop.fillAmount = 1f - t;

                yield return null;
            }

            if (centerGroup != null)
                centerGroup.alpha = 0f;
            if (BlurBackdrop != null)
                BlurBackdrop.fillAmount = 0f;
            // 收尾（Closing 锁 Pop + GoBack）由 ScreenBase.CloseScreen 模板统一处理
        }
    }
}
