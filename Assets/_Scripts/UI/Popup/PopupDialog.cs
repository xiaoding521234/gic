using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Tool;
namespace GIC.UI
{


    public class PopupDialog : MonoBehaviour, IClosable
    {
        [InspectorName("消息文本")]
        [SerializeField] private TextMeshProUGUI messageTextObj;
        [InspectorName("模态遮罩按钮")]
        public Button backPanel;
        [InspectorName("淡入淡出组")]
        public CanvasGroup canvasGroup;

        [Header("动画")]
        [SerializeField] private float fadeInDuration = 0.08f;
        [SerializeField] private float displayDuration = 1.5f;
        [SerializeField] private float fadeOutDuration = 0.08f;

        [Header("轻提示动画")]
        [InspectorName("滑入时长")]
        [SerializeField] private float slideInDuration = 0.3f;
        [InspectorName("轻提示停留时长")]
        [SerializeField] private float toastHoldDuration = 2f;
        [InspectorName("滑出时长")]
        [SerializeField] private float slideOutDuration = 0.2f;
        [InspectorName("滑出距离")]
        [SerializeField] private float slideOutDist = 150f;

        [Header("重复提示反馈")]
        [InspectorName("强调颜色")]
        [SerializeField] private Color emphasizeColor = new Color(1f, 0.3f, 0.3f, 1f);
        [InspectorName("强调时长")]
        [SerializeField] private float emphasizeDuration = 0.4f;
        [InspectorName("抖动强度")]
        [SerializeField] private float shakeIntensity = 20f;

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
        private Coroutine _currentCoroutine;
        private Coroutine _emphasizeCoroutine;  // 强调动画（红闪+抖动）——独立跟踪，防连点/移出时互抢位置
        private Button _toastClickBtn;   // 点击移出（2026-08-31）
        private bool _toastDismissing;   // 移出中（连点防重入 + 管理器去重跳过）
        private bool _isClosing;         // 模态关闭防重入（ESC 与遮罩点击同帧双触发）

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
        /// 刷新已有 toast：重置停留计时并播放强调动画（红色闪烁+抖动）。
        /// 保持当前可见度原地强调，不重播入场淡入（2026-09-02 审查拍板）
        /// </summary>
        public void RefreshToast()
        {
            if (_toastDismissing) return; // 移出中不复活（管理器去重会跳过并新建）

            // 重启生命协程（animateIn=false：只重置停留计时，不清 alpha）
            if (_currentCoroutine != null) StopCoroutine(_currentCoroutine);
            _currentCoroutine = StartCoroutine(ToastShowCoroutine(false));

            // 播放强调动画（连续刷新时先停旧的，防两段抖动互抢）
            if (_toastBgImage != null)
            {
                if (_emphasizeCoroutine != null) StopCoroutine(_emphasizeCoroutine);
                _emphasizeCoroutine = StartCoroutine(FlashShakeCoroutine());
            }
        }

        /// <summary>移出中（管理器去重跳过用：移出中的实例不刷新、新建替代）</summary>
        public bool IsDismissing => _toastDismissing;

        /// <summary>点击 toast 立即移出（2026-08-31）——快速淡出+滑出后走正常完成路径
        /// （管理器移除+重排+销毁）。连点防重入。</summary>
        public void DismissToastNow()
        {
            if (_mode != PopupMode.Toast || _toastDismissing) return;
            _toastDismissing = true;
            if (_emphasizeCoroutine != null)
            {
                StopCoroutine(_emphasizeCoroutine); // 移出动画独占位置，先停抖动
                _emphasizeCoroutine = null;
            }
            if (_currentCoroutine != null) StopCoroutine(_currentCoroutine);
            _currentCoroutine = StartCoroutine(ToastDismissCoroutine());
        }

        private IEnumerator ToastDismissCoroutine()
        {
            // 半个滑入时长（≈0.15s）：读作"立刻消失"但不闪断
            float dur = slideInDuration * 0.5f;
            Vector2 startPos = _toastContentRect != null
                ? _toastContentRect.anchoredPosition
                : _toastTargetPos;
            Vector2 endPos = startPos + new Vector2(0f, slideOutDist);

            float elapsed = 0f;
            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / dur;
                if (canvasGroup != null) canvasGroup.alpha = 1f - t;
                if (_toastContentRect != null)
                    _toastContentRect.anchoredPosition = Vector2.Lerp(startPos, endPos, t);
                yield return null;
            }
            if (canvasGroup != null) canvasGroup.alpha = 0f;

            _currentCoroutine = null;
            var cb = _onToastComplete;
            _onToastComplete = null;   // 先摘再调（OnDestroy 兜底不会二次触发）
            cb?.Invoke(this);
            Destroy(gameObject);
        }

        private IEnumerator FlashShakeCoroutine()
        {
            Vector2 basePos = _toastTargetPos;
            float elapsed = 0f;

            while (elapsed < emphasizeDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / emphasizeDuration;

                // 颜色闪烁：红→原色
                _toastBgImage.color = Color.Lerp(emphasizeColor, _toastBgOriginalColor, t);

                // 抖动：衰减
                float shake = shakeIntensity * (1f - t);
                if (_toastContentRect != null)
                    _toastContentRect.anchoredPosition = basePos + new Vector2(
                        Random.Range(-shake, shake), 0f);

                yield return null;
            }

            // 恢复
            _toastBgImage.color = _toastBgOriginalColor;
            if (_toastContentRect != null)
                _toastContentRect.anchoredPosition = basePos;
            _emphasizeCoroutine = null;
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

            // CanvasGroup 放行点击（2026-08-31 点击移出）：只有内容 Image 吃点击
            //（根节点无 Graphic、backPanel 已禁用），游戏其余区域不受影响；
            // interactable=false 会禁掉子 Button 的响应，须保持 true
            if (canvasGroup != null)
            {
                canvasGroup.blocksRaycasts = true;
                canvasGroup.interactable = true;
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
                    _toastContentRect.anchoredPosition = new Vector2(0f, canvasHeight * 0.5f + slideOutDist);

                    // 记录背景 Image 供强调动画使用
                    _toastBgImage = child.GetComponent<Image>();
                    if (_toastBgImage != null)
                        _toastBgOriginalColor = _toastBgImage.color;
                    break;
                }
            }

            // 点击移出（2026-08-31）：内容区挂 Button——点中 toast 立即移出。
            // 实例每次 Instantiate 新建，监听挂在实例上随实例销毁，无泄漏
            if (_toastContentRect != null)
            {
                if (_toastBgImage != null) _toastBgImage.raycastTarget = true;
                _toastClickBtn = _toastContentRect.GetComponent<Button>();
                if (_toastClickBtn == null)
                {
                    _toastClickBtn = _toastContentRect.gameObject.AddComponent<Button>();
                    _toastClickBtn.transition = Button.Transition.None; // 纯点击区，无按钮变色
                }
                _toastClickBtn.onClick.AddListener(DismissToastNow);
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
                _currentCoroutine = StartCoroutine(ToastShowCoroutine(true));
            else
                _currentCoroutine = StartCoroutine(ShowCoroutine());
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
        /// 轻提示动画 — 滑入→停留→滑出。
        /// animateIn=false 为刷新路径：保持可见度原地续命，不重播入场（2026-09-02 审查 #5）
        /// </summary>
        private IEnumerator ToastShowCoroutine(bool animateIn)
        {
            // 屏幕外起点（滑入起点 = 滑出终点，按目标位置推算而非当前位置——刷新重启不丢滑出位移）
            Vector2 offscreenPos = _toastTargetPos + new Vector2(0f, slideOutDist);

            if (animateIn)
            {
                // 滑入 + 淡入
                float elapsed = 0f;
                while (elapsed < slideInDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = elapsed / slideInDuration;
                    float eased = 1f - Mathf.Pow(1f - t, 3f);
                    if (canvasGroup != null) canvasGroup.alpha = eased;
                    if (_toastContentRect != null)
                        _toastContentRect.anchoredPosition = Vector2.Lerp(offscreenPos, _toastTargetPos, eased);
                    yield return null;
                }

                if (canvasGroup != null) canvasGroup.alpha = 1f;
                if (_toastContentRect != null)
                    _toastContentRect.anchoredPosition = _toastTargetPos;
            }
            else
            {
                // 刷新：确保满可见度落在目标位（中途刷新时从半路吸附，红闪抖动掩盖此跳变）
                if (canvasGroup != null) canvasGroup.alpha = 1f;
                if (_toastContentRect != null)
                    _toastContentRect.anchoredPosition = _toastTargetPos;
            }

            // 停留
            yield return Wait.Seconds(toastHoldDuration);

            // 滑出 + 淡出
            float elapsedOut = 0f;
            while (elapsedOut < slideOutDuration)
            {
                elapsedOut += Time.deltaTime;
                float t = elapsedOut / slideOutDuration;
                if (canvasGroup != null) canvasGroup.alpha = 1f - t;
                if (_toastContentRect != null)
                    _toastContentRect.anchoredPosition = Vector2.Lerp(_toastTargetPos, offscreenPos, t);
                yield return null;
            }

            var cb = _onToastComplete;
            _onToastComplete = null;
            cb?.Invoke(this);
            Destroy(gameObject);
        }

        private void Close()
        {
            if (_isClosing) return; // ESC 与遮罩点击同帧双触发防重入
            _isClosing = true;

            _inputManager?.UnregisterClosable(this);

            if (_currentCoroutine != null)
            {
                StopCoroutine(_currentCoroutine);
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
