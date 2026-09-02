using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Tool;

namespace GIC.UI
{
    /// <summary>
    /// 原神风格输入弹窗：标题 + TMP输入框 + 确认/取消按钮，带淡入淡出动画。
    /// 通过 Show(titleKey, currentValue, onConfirm) 调用，确认后回调返回输入文本。
    /// </summary>
    public class InputPopupDialog : MonoBehaviour, IClosable
    {
        [Header("UI组件")]
        [InspectorName("标题文本")]
        [SerializeField] private TextMeshProUGUI titleTextObj;
        [InspectorName("输入框")]
        public TMP_InputField inputField;
        [InspectorName("确认按钮")]
        public Button confirmButton;
        [InspectorName("取消按钮")]
        public Button cancelButton;
        [InspectorName("模态遮罩按钮")]
        public Button backPanel;
        [InspectorName("淡入淡出组")]
        public CanvasGroup canvasGroup;

        [Header("动画")]
        [SerializeField] private float fadeInDuration = 0.2f;
        [SerializeField] private float fadeOutDuration = 0.2f;

        private TextCombiner _titleText;
        private TextCombiner _confirmText;
        private TextCombiner _cancelText;
        private Action<string> _onConfirmCallback;
        private Coroutine _currentCoroutine;
        private int _defaultCharacterLimit = -1;   // prefab 默认字符上限（懒缓存，-1=未取）
        private bool _isClosing;                  // 关闭防重入（确认/取消/ESC 多路径同帧触发）

        [Autowired] private InputManager _inputManager;

        private void Awake()
        {
            // 运行时实例化的弹窗：容器早已就绪，Awake 注入
            Wargame.Instance?.Context?.Inject(this);
        }

        // ── IClosable 实现 ──
        void IClosable.Close() => OnCancel();

        /// <summary>
        /// 显示输入弹窗。
        /// </summary>
        /// <param name="titleKey">UIText 表中的本地化 Key</param>
        /// <param name="maxChars">字符上限：0=还原 prefab 序列化默认（玩家名 16）；正数=按调用方覆盖
        /// （API Key 等长文本——2026-08-29 修"key 输不完"：prefab 上 16 截断了 35 字符的 DeepSeek key）。
        /// 2026-09-02 修跨设置项污染：实例复用，上一次的覆盖值不再泄入本次</param>
        public void Show(string titleKey, string currentValue, Action<string> onConfirm, int maxChars = 0)
        {
            _isClosing = false; // 复用重开：清除上一次 Hide 置位的关闭态

            EnsureTextCombiners();
            _titleText.SetSingleEntry(new LocalizedString("UIText", titleKey));
            // 懒缓存 prefab 默认上限（prefab 根若为 inactive，Awake 晚于首次 Show，不能只在 Awake 取）
            if (_defaultCharacterLimit < 0) _defaultCharacterLimit = inputField.characterLimit;
            inputField.characterLimit = maxChars > 0 ? maxChars : _defaultCharacterLimit; // 先定上限再回显（长 key 不被截断）
            inputField.text = currentValue;
            _onConfirmCallback = onConfirm;

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

            Wargame.Instance?.Context?.Inject(this); // 幂等：Awake 未执行（激活即调 Show）时补注入
            _inputManager?.RegisterClosable(this);
            InputLocks.Push(this, InputLockReason.InputPopupEntering);

            gameObject.SetActive(true);

            if (_currentCoroutine != null)
                StopCoroutine(_currentCoroutine);
            _currentCoroutine = StartCoroutine(ShowCoroutine());
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

            InputLocks.Pop(this, InputLockReason.InputPopupEntering);

            inputField.Select();
            inputField.ActivateInputField();
        }

        private void OnConfirm()
        {
            string newValue = inputField.text.Trim();
            if (!string.IsNullOrEmpty(newValue))
            {
                _onConfirmCallback?.Invoke(newValue);
            }
            Hide();
        }

        private void OnCancel()
        {
            Hide();
        }

        private void Hide()
        {
            if (_isClosing) return; // 防重入：确认/取消/ESC 多路径
            _isClosing = true;

            _inputManager?.UnregisterClosable(this);

            if (_currentCoroutine != null)
            {
                StopCoroutine(_currentCoroutine);
                // Show 协程在淡入完成前被中断 — 释放入场锁（幂等，已完成时为空操作）
                InputLocks.Pop(this, InputLockReason.InputPopupEntering);
            }
            _currentCoroutine = StartCoroutine(HideCoroutine());
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
            _inputManager?.UnregisterClosable(this);
            // 兜底：淡入期间被外部销毁时释放本类持有的锁
            InputLocks.PopAll(this);
            confirmButton?.onClick.RemoveAllListeners();
            cancelButton?.onClick.RemoveAllListeners();
            backPanel?.onClick.RemoveAllListeners();
        }
    }
}
