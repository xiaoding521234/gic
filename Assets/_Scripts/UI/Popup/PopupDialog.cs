using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Serialization;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
using GIC.Tool;
namespace GIC.UI
{


    public class PopupDialog : MonoBehaviour, IClosable
    {
        [FormerlySerializedAs("messageText")]
        [SerializeField] private TextMeshProUGUI messageTextObj;
        public Button backPanel;
        public CanvasGroup canvasGroup;

        [Header("动画")]
        [SerializeField] private float fadeInDuration = 0.08f;
        [SerializeField] private float displayDuration = 1.5f;
        [SerializeField] private float fadeOutDuration = 0.08f;

        [Header("轻提示动画")]
        [SerializeField] private float 滑入时长 = 0.3f;
        [SerializeField] private float 轻提示停留时长 = 2f;
        [SerializeField] private float 滑出时长 = 0.2f;
        [SerializeField] private float 滑出距离 = 150f;

        [Header("重复提示反馈")]
        [SerializeField] private Color 强调颜色 = new Color(1f, 0.3f, 0.3f, 1f);
        [SerializeField] private float 强调时长 = 0.4f;
        [SerializeField] private float 抖动强度 = 20f;

        public enum PopupMode { Modal, Toast }

        // ── IClosable 实现 ──
        void IClosable.Close() => Close();

        private PopupMode _mode = PopupMode.Modal;
        private Vector2 _toastTargetPos;
        private RectTransform _toastContentRect;
        private System.Action<PopupDialog> _onToastComplete;
        private Image _toastBgImage;
        private Color _toastBgOriginalColor;
        private string _toastMessage;

        private TextCombiner _messageText;
        private Coroutine currentCoroutine;

        [Autowired] private InputManager _inputManager;

        private void Awake()
        {
            // 运行时实例化的弹窗：容器早已就绪，Awake 注入
            Wargame.Instance?.Context?.Inject(this);
        }

        private void EnsureTextCombiner()
        {
            if (_messageText == null && messageTextObj != null)
            {
                _messageText = messageTextObj.GetComponent<TextCombiner>();
                if (_messageText == null)
                    _messageText = messageTextObj.gameObject.AddComponent<TextCombiner>();
            }
        }

        public void Init(string message)
        {
            EnsureTextCombiner();
            _messageText.SetSingleEntry(message);
            Show();
        }

        public void Init(LocalizedString localizedString)
        {
            EnsureTextCombiner();
            _messageText.SetSingleEntry(localizedString);
            Show();
        }

        /// <summary>
        /// 轻提示模式 — 从顶部滑入，不阻断点击，由 PopupManager 管理堆叠
        /// </summary>
        public void InitToast(string message, Vector2 targetPos, System.Action<PopupDialog> onComplete)
        {
            _mode = PopupMode.Toast;
            _toastTargetPos = targetPos;
            _onToastComplete = onComplete;
            _toastMessage = message;

            SetupToastLayout();
            EnsureTextCombiner();
            _messageText.SetSingleEntry(message);
            Show();
        }

        /// <summary>
        /// 轻提示模式（本地化文本）
        /// </summary>
        public void InitToast(LocalizedString localizedString, Vector2 targetPos, System.Action<PopupDialog> onComplete)
        {
            _mode = PopupMode.Toast;
            _toastTargetPos = targetPos;
            _onToastComplete = onComplete;
            _toastMessage = localizedString.TableEntryReference.Key;

            SetupToastLayout();
            EnsureTextCombiner();
            _messageText.SetSingleEntry(localizedString);
            Show();
        }

        /// <summary>
        /// 刷新已有 toast 的生命时间并播放强调动画（红色闪烁+抖动）
        /// </summary>
        public void RefreshToast()
        {
            // 重启生命协程
            if (currentCoroutine != null) StopCoroutine(currentCoroutine);
            currentCoroutine = StartCoroutine(ToastShowCoroutine());

            // 播放强调动画
            if (_toastBgImage != null)
                StartCoroutine(FlashShakeCoroutine());
        }

        private IEnumerator FlashShakeCoroutine()
        {
            Vector2 basePos = _toastTargetPos;
            float elapsed = 0f;

            while (elapsed < 强调时长)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / 强调时长;

                // 颜色闪烁：红→原色
                _toastBgImage.color = Color.Lerp(强调颜色, _toastBgOriginalColor, t);

                // 抖动：衰减
                float shake = 抖动强度 * (1f - t);
                if (_toastContentRect != null)
                    _toastContentRect.anchoredPosition = basePos + new Vector2(
                        Random.Range(-shake, shake), 0f);

                yield return null;
            }

            // 恢复
            _toastBgImage.color = _toastBgOriginalColor;
            if (_toastContentRect != null)
                _toastContentRect.anchoredPosition = basePos;
        }

        /// <summary>
        /// 将 prefab 布局改为 toast 布局：禁用模态遮罩，记录子节点初始位置供动画使用
        /// </summary>
        private void SetupToastLayout()
        {
            // 禁用模态遮罩
            if (backPanel != null)
            {
                backPanel.gameObject.SetActive(false);
                var bgImg = backPanel.GetComponent<Image>();
                if (bgImg != null) bgImg.raycastTarget = false;
            }

            // CanvasGroup 不拦截点击
            if (canvasGroup != null)
            {
                canvasGroup.blocksRaycasts = false;
                canvasGroup.interactable = false;
                canvasGroup.alpha = 0f;
            }

            // 计算屏幕高度（根节点自带 Canvas，ScreenSpaceOverlay 全屏）
            var canvas = GetComponent<Canvas>();
            float canvasHeight = canvas != null ? canvas.GetComponent<RectTransform>().rect.height : 1080f;

            // 找到内容容器（BackPanel 以外的子节点）并定位到 toast 目标位置
            _toastContentRect = null;
            _toastBgImage = null;
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child == backPanel?.transform) continue;

                _toastContentRect = child.GetComponent<RectTransform>();
                if (_toastContentRect != null)
                {
                    _toastContentRect.anchorMin = new Vector2(0.5f, 0.5f);
                    _toastContentRect.anchorMax = new Vector2(0.5f, 0.5f);
                    _toastContentRect.pivot = new Vector2(0.5f, 0.5f);
                    // 起始位置：屏幕顶部边缘之上（子节点锚定在屏幕中心，Y=半屏高度即顶部）
                    _toastContentRect.anchoredPosition = new Vector2(0f, canvasHeight * 0.5f + 滑出距离);

                    // 记录背景 Image 供强调动画使用
                    _toastBgImage = child.GetComponent<Image>();
                    if (_toastBgImage != null)
                        _toastBgOriginalColor = _toastBgImage.color;
                    break;
                }
            }
        }

        /// <summary>
        /// 更新轻提示目标位置（堆叠重排时调用）
        /// </summary>
        public void SetToastPosition(Vector2 newPos)
        {
            if (_mode != PopupMode.Toast) return;
            _toastTargetPos = newPos;
            if (_toastContentRect != null)
                _toastContentRect.anchoredPosition = newPos;
        }

        /// <summary>
        /// 获取当前 toast 的消息标识（用于去重判断）
        /// </summary>
        public string GetToastMessage()
        {
            return _toastMessage;
        }

        private void Show()
        {
            if (_mode == PopupMode.Modal)
            {
                Wargame.Instance?.Context?.Inject(this); // 幂等补注入（Awake 未执行的边缘时序）
                _inputManager?.RegisterClosable(this);
                if (backPanel != null)
                    backPanel.onClick.AddListener(Close);
            }
            gameObject.SetActive(true);

            if (_mode == PopupMode.Toast)
                currentCoroutine = StartCoroutine(ToastShowCoroutine());
            else
                currentCoroutine = StartCoroutine(ShowCoroutine());
        }

        private IEnumerator ShowCoroutine()
        {
            InputLocks.Push(this, InputLockReason.PopupEntering);
            canvasGroup.alpha = 0f;

            float elapsed = 0f;
            while (elapsed < fadeInDuration)
            {
                elapsed += Time.deltaTime;
                canvasGroup.alpha = elapsed / fadeInDuration;
                yield return null;
            }
            canvasGroup.alpha = 1f;
            InputLocks.Pop(this, InputLockReason.PopupEntering);

            yield return Wait.Seconds(displayDuration);

            elapsed = 0f;
            while (elapsed < fadeOutDuration)
            {
                elapsed += Time.deltaTime;
                canvasGroup.alpha = 1f - (elapsed / fadeOutDuration);
                yield return null;
            }

            Destroy(gameObject);
        }

        /// <summary>
        /// 轻提示动画 — 从顶部滑入→停留→滑出
        /// </summary>
        private IEnumerator ToastShowCoroutine()
        {
            // 初始状态已在 SetupToastLayout 中设置（alpha=0, 内容位于 startPos）
            Vector2 startPos = _toastContentRect != null
                ? _toastContentRect.anchoredPosition
                : _toastTargetPos + new Vector2(0f, 滑出距离);

            // 滑入 + 淡入
            float elapsed = 0f;
            while (elapsed < 滑入时长)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / 滑入时长;
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                if (canvasGroup != null) canvasGroup.alpha = eased;
                if (_toastContentRect != null)
                    _toastContentRect.anchoredPosition = Vector2.Lerp(startPos, _toastTargetPos, eased);
                yield return null;
            }

            if (canvasGroup != null) canvasGroup.alpha = 1f;
            if (_toastContentRect != null)
                _toastContentRect.anchoredPosition = _toastTargetPos;

            // 停留
            yield return Wait.Seconds(轻提示停留时长);

            // 滑出 + 淡出
            elapsed = 0f;
            while (elapsed < 滑出时长)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / 滑出时长;
                canvasGroup.alpha = 1f - t;
                if (_toastContentRect != null)
                    _toastContentRect.anchoredPosition = Vector2.Lerp(_toastTargetPos, startPos, t);
                yield return null;
            }

            var cb = _onToastComplete;
            _onToastComplete = null;
            cb?.Invoke(this);
            Destroy(gameObject);
        }

        private void Close()
        {
            _inputManager?.UnregisterClosable(this);

            if (currentCoroutine != null)
            {
                StopCoroutine(currentCoroutine);
                // Show 协程在淡入完成前被中断 — 释放入场锁（幂等）
                InputLocks.Pop(this, InputLockReason.PopupEntering);
            }
            if (_mode == PopupMode.Toast)
            {
                var cb = _onToastComplete;
                _onToastComplete = null;
                cb?.Invoke(this);
            }
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            _inputManager?.UnregisterClosable(this);
            // 兜底：淡入期间被外部销毁时释放本类持有的锁
            InputLocks.PopAll(this);

            if (backPanel != null)
                backPanel.onClick.RemoveListener(Close);
            // 外部销毁（如场景切换）时通知 PopupManager 清理引用
            _onToastComplete?.Invoke(this);
        }
    }

}
