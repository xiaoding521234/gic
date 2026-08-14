using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Serialization;
using UnityEngine.UI;
using GIC.Tool;

namespace GIC.UI
{
    /// <summary>
    /// 原神风格输入弹窗：标题 + TMP输入框 + 确认/取消按钮，带淡入淡出动画。
    /// 通过 Show(titleKey, currentValue, onConfirm) 调用，确认后回调返回输入文本。
    /// </summary>
    public class InputPopupDialog : MonoBehaviour
    {
        [Header("UI组件")]
        [FormerlySerializedAs("titleText")]
        [SerializeField] private TextMeshProUGUI titleTextObj;
        public TMP_InputField inputField;
        public Button confirmButton;
        public Button cancelButton;
        public Button backPanel;
        public CanvasGroup canvasGroup;

        [Header("动画")]
        [SerializeField] private float fadeInDuration = 0.2f;
        [SerializeField] private float fadeOutDuration = 0.2f;

        private TextCombiner _titleText;
        private TextCombiner _confirmText;
        private TextCombiner _cancelText;
        private Action<string> onConfirmCallback;
        private Coroutine currentCoroutine;

        /// <summary>
        /// 显示输入弹窗。
        /// </summary>
        /// <param name="titleKey">UIText 表中的本地化 Key</param>
        public void Show(string titleKey, string currentValue, Action<string> onConfirm)
        {
            EnsureTextCombiners();
            _titleText.SetSingleEntry(new LocalizedString("UIText", titleKey));
            inputField.text = currentValue;
            onConfirmCallback = onConfirm;

            LocalizeTexts();

            // 确保按钮事件只绑定一次
            confirmButton.onClick.RemoveAllListeners();
            confirmButton.onClick.AddListener(OnConfirm);

            cancelButton.onClick.RemoveAllListeners();
            cancelButton.onClick.AddListener(OnCancel);

            backPanel.onClick.RemoveAllListeners();
            backPanel.onClick.AddListener(OnCancel);

            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;

            gameObject.SetActive(true);

            if (currentCoroutine != null)
                StopCoroutine(currentCoroutine);
            currentCoroutine = StartCoroutine(ShowCoroutine());
        }

        private void EnsureTextCombiners()
        {
            if (_titleText == null && titleTextObj != null)
            {
                _titleText = titleTextObj.GetComponent<TextCombiner>();
                if (_titleText == null)
                    _titleText = titleTextObj.gameObject.AddComponent<TextCombiner>();
            }

            if (_confirmText == null)
            {
                var tmp = confirmButton.GetComponentInChildren<TextMeshProUGUI>();
                if (tmp != null)
                {
                    _confirmText = tmp.GetComponent<TextCombiner>();
                    if (_confirmText == null)
                        _confirmText = tmp.gameObject.AddComponent<TextCombiner>();
                }
            }

            if (_cancelText == null)
            {
                var tmp = cancelButton.GetComponentInChildren<TextMeshProUGUI>();
                if (tmp != null)
                {
                    _cancelText = tmp.GetComponent<TextCombiner>();
                    if (_cancelText == null)
                        _cancelText = tmp.gameObject.AddComponent<TextCombiner>();
                }
            }
        }

        private void LocalizeTexts()
        {
            _confirmText?.SetSingleEntry(new LocalizedString("UIText", "Confirm"));
            _cancelText?.SetSingleEntry(new LocalizedString("UIText", "Cancel"));

            // TMP_InputField 的 placeholder 无法挂 TextCombiner，用同步解析
            if (inputField.placeholder is TextMeshProUGUI placeholder)
                placeholder.text = new LocalizedString("UIText", "InputPlaceholder").GetLocalizedString();
        }

        private IEnumerator ShowCoroutine()
        {
            canvasGroup.alpha = 0f;

            float elapsed = 0f;
            while (elapsed < fadeInDuration)
            {
                elapsed += Time.deltaTime;
                canvasGroup.alpha = elapsed / fadeInDuration;
                yield return null;
            }
            canvasGroup.alpha = 1f;

            inputField.Select();
            inputField.ActivateInputField();
        }

        private void OnConfirm()
        {
            string newValue = inputField.text.Trim();
            if (!string.IsNullOrEmpty(newValue))
            {
                onConfirmCallback?.Invoke(newValue);
            }
            Hide();
        }

        private void OnCancel()
        {
            Hide();
        }

        private void Hide()
        {
            if (currentCoroutine != null)
                StopCoroutine(currentCoroutine);
            currentCoroutine = StartCoroutine(HideCoroutine());
        }

        private IEnumerator HideCoroutine()
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            float elapsed = 0f;
            while (elapsed < fadeOutDuration)
            {
                elapsed += Time.deltaTime;
                canvasGroup.alpha = 1f - (elapsed / fadeOutDuration);
                yield return null;
            }
            canvasGroup.alpha = 0f;
            gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            confirmButton?.onClick.RemoveAllListeners();
            cancelButton?.onClick.RemoveAllListeners();
            backPanel?.onClick.RemoveAllListeners();
        }
    }
}
