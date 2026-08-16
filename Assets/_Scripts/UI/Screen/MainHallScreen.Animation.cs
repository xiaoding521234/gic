using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;

namespace GIC.UI
{
    /// <summary>
    /// 主厅 — 按钮入场/退出交错动画与交互开关
    /// </summary>
    public partial class MainHallScreen
    {
        private void ResetAndPlayEnterAnimation()
        {
            // 停止可能正在进行的退出动画
            if (exitAnimationCoroutine != null)
            {
                StopCoroutine(exitAnimationCoroutine);
                exitAnimationCoroutine = null;
            }

            StopAllCoroutines();

            // 重置按钮位置
            ResetButtonPositions();

            // 播放入场动画
            PlayEnterAnimation();

            // 重新启用按钮交互
            SetButtonsInteractable(true);

            // 更新背景
            if (_positionManager != null)
            {
                UpdateBackground(_positionManager.CurrentPosition);
            }

            isExiting = false;
        }

        private void PlayEnterAnimation()
        {
            StartCoroutine(AnimateButtonsStaggered());
        }

        private void InitializeAnimation()
        {
            AddButtonToList(missionButton, leftButtons);
            AddButtonToList(mapButton, leftButtons);
            AddButtonToList(achievementButton, leftButtons);
            AddButtonToList(settingsButton, leftButtons);

            AddButtonToList(wishButton, rightButtons);
            AddButtonToList(backpackButton, rightButtons);
            AddButtonToList(coopButton, rightButtons);
            AddButtonToList(tutorialButton, rightButtons);

            foreach (var btn in leftButtons)
            {
                if (btn != null)
                {
                    originalPositions[btn] = btn.anchoredPosition;
                    btn.anchoredPosition = new Vector2(leftStartX, btn.anchoredPosition.y);
                }
            }

            foreach (var btn in rightButtons)
            {
                if (btn != null)
                {
                    originalPositions[btn] = btn.anchoredPosition;
                    btn.anchoredPosition = new Vector2(rightStartX, btn.anchoredPosition.y);
                }
            }
        }

        private void AddButtonToList(Button button, List<RectTransform> list)
        {
            if (button != null)
                list.Add(button.GetComponent<RectTransform>());
        }

        private void ResetButtonPositions()
        {
            foreach (var btn in leftButtons)
            {
                if (btn != null && originalPositions.ContainsKey(btn))
                {
                    btn.anchoredPosition = new Vector2(leftStartX, originalPositions[btn].y);
                }
            }

            foreach (var btn in rightButtons)
            {
                if (btn != null && originalPositions.ContainsKey(btn))
                {
                    btn.anchoredPosition = new Vector2(rightStartX, originalPositions[btn].y);
                }
            }
        }

        private IEnumerator AnimateButtonsStaggered()
        {
            for (int i = 0; i < leftButtons.Count; i++)
            {
                var btn = leftButtons[i];
                if (btn != null)
                {
                    StartCoroutine(AnimateSingleButton(btn, leftStartX, originalPositions[btn].x, i * staggerDelay));
                }
            }

            for (int i = 0; i < rightButtons.Count; i++)
            {
                var btn = rightButtons[i];
                if (btn != null)
                {
                    StartCoroutine(AnimateSingleButton(btn, rightStartX, originalPositions[btn].x, i * staggerDelay));
                }
            }

            float maxDelay = Mathf.Max(leftButtons.Count, rightButtons.Count) * staggerDelay;
            yield return Wait.Seconds(maxDelay + animationDuration);
        }

        private IEnumerator AnimateSingleButton(RectTransform button, float startX, float targetX, float delay)
        {
            if (delay > 0)
                yield return Wait.Seconds(delay);

            float elapsedTime = 0f;

            while (elapsedTime < animationDuration)
            {
                float t = easeCurve.Evaluate(elapsedTime / animationDuration);
                float x = Mathf.Lerp(startX, targetX, t);
                button.anchoredPosition = new Vector2(x, button.anchoredPosition.y);
                elapsedTime += Time.deltaTime;
                yield return null;
            }

            button.anchoredPosition = new Vector2(targetX, button.anchoredPosition.y);
        }

        private IEnumerator AnimateButtonExit(RectTransform button, float targetX, float delay)
        {
            if (delay > 0)
                yield return Wait.Seconds(delay);

            float startX = button.anchoredPosition.x;
            float elapsedTime = 0f;

            while (elapsedTime < animationDuration)
            {
                float t = easeCurve.Evaluate(elapsedTime / animationDuration);
                float x = Mathf.Lerp(startX, targetX, t);
                button.anchoredPosition = new Vector2(x, button.anchoredPosition.y);
                elapsedTime += Time.deltaTime;
                yield return null;
            }

            button.anchoredPosition = new Vector2(targetX, button.anchoredPosition.y);
        }

        private void SetButtonsInteractable(bool interactable)
        {
            foreach (var btn in leftButtons)
            {
                var button = btn?.GetComponent<Button>();
                if (button != null) button.interactable = interactable;
            }

            foreach (var btn in rightButtons)
            {
                var button = btn?.GetComponent<Button>();
                if (button != null) button.interactable = interactable;
            }
        }
    }
}
