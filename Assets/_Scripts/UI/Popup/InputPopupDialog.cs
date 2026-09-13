using System;
using System.Collections;
using System.Collections.Generic;
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

        [Header("指令补全（命令模式，样式在 prefab）")]
        [InspectorName("建议面板")] [SerializeField] private RectTransform suggestPanel;
        [InspectorName("建议行模板")] [SerializeField] private GameObject suggestRowTemplate;
        [InspectorName("最多显示行数")] [SerializeField] private int maxSuggestRows = 8;
        [InspectorName("行高")] [SerializeField] private float suggestRowHeight = 44f;

        private TextCombiner _titleText;
        private TextCombiner _confirmText;
        private TextCombiner _cancelText;
        private Action<string> _onConfirmCallback;
        private Coroutine _currentCoroutine;
        private int _defaultCharacterLimit = -1;   // prefab 默认字符上限（懒缓存，-1=未取）
        private bool _isClosing;                  // 关闭防重入（确认/取消/ESC 多路径同帧触发）

        // ---- 命令模式状态（suggester 非空时启用，IDE 式补全） ----
        private Func<string, List<CommandSuggestion>> _suggester;
        private readonly List<CommandSuggestion> _suggestions = new List<CommandSuggestion>();
        private readonly List<CommandSuggestRow> _rows = new List<CommandSuggestRow>();
        private int _selectedIndex = -1;
        private const float SuggestPanelPadding = 4f;

        /// <summary>运行时"已打开"标记（Show 置 true / Hide·ForceClosedState 置 false）：
        /// 非序列化默认值，专供 OnEnable 池化自愈判定"面板池化时弹窗还开着"场景。</summary>
        private bool _shown;

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
        /// <param name="suggester">指令补全提供器（非空=命令模式：输入即出建议、Tab 补词、↑↓ 选择、
        /// Enter 执行整行；null=普通输入模式，行为与既往完全一致）</param>
        public void Show(string titleKey, string currentValue, Action<string> onConfirm, int maxChars = 0,
            Func<string, List<CommandSuggestion>> suggester = null)
        {
            _isClosing = false; // 复用重开：清除上一次 Hide 置位的关闭态

            EnsureTextCombiners();
            _titleText.SetSingleEntry(new LocalizedString("UIText", titleKey));
            // 懒缓存 prefab 默认上限（prefab 根若为 inactive，Awake 晚于首次 Show，不能只在 Awake 取）
            if (_defaultCharacterLimit < 0) _defaultCharacterLimit = inputField.characterLimit;
            inputField.characterLimit = maxChars > 0 ? maxChars : _defaultCharacterLimit; // 先定上限再回显（长 key 不被截断）
            inputField.text = currentValue;
            _onConfirmCallback = onConfirm;

            // 命令模式接线（2026-09-13 指令系统统一）：本弹窗实例无其它 onValueChanged 订阅方，可安全重挂
            inputField.onValueChanged.RemoveAllListeners();
            _suggester = suggester;
            if (suggester != null)
                inputField.onValueChanged.AddListener(OnCommandInputChanged);
            HideSuggest();   // 复用弹窗：清掉上一次（含命令模式）的建议列表
            // 开窗即出初始建议（IDE 空行展示全部）：text 赋值在挂监听之前发生、
            // 空串到空串也不触发 onValueChanged——须显式种子一次，否则开窗空白要等首个字符
            if (_suggester != null)
                OnCommandInputChanged(currentValue);

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

            // 末尾置位：SetActive(true) 会触发 OnEnable，若开头就置 _shown 会被自愈当场自毁
            //（释放刚 Push 的 entering 锁——2026-09-13 自愈测试实证）。协程启动成功才算真正"开"。
            _shown = true;
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
            _currentCoroutine = null;   // 淡入完成=锁已 Pop；置空防 Hide 误判"正在淡入"而重复 Pop（2026-09-13 实证）

            inputField.Select();
            inputField.ActivateInputField();
        }

        /// <summary>池化自愈（docs/14 §50，§39b 第三例）：宿主面板入池 SetActive(false) 会硬杀
        /// Hide/Show 协程，残留半开态（alpha 冻结+_isClosing=true）或全开态（派蒙导航强切面板、
        /// 弹窗没人关）——面板从池里取出重激活时，本 OnEnable 强制复位为已关闭态。
        /// 收尾语义不依赖协程跑完（§39b 纪律）。_shown 为运行时标记（非序列化默认值），
        /// 新实例首次 OnEnable 恒为 false=无动作。</summary>
        private void OnEnable()
        {
            if (_isClosing || _shown) ForceClosedState();
        }

        /// <summary>强制复位到已关闭态（幂等）：视觉归零、状态清空、锁/Closable 全量释放。
        /// OnEnable 自愈与 OnDestroy 兜底共用 PopAll（只释放本 owner，重复调用安全）。</summary>
        private void ForceClosedState()
        {
            _isClosing = false;
            _shown = false;
            _suggester = null;
            HideSuggest();
            _inputManager?.UnregisterClosable(this);
            InputLocks.PopAll(this);
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
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

        // ==================== 命令模式：IDE 式补全（2026-09-13 指令系统统一） ====================

        /// <summary>键盘交互（仅命令模式）：Tab=补全当前词（↑↓ 选中的优先，默认第一条）、
        /// ↑↓=切换选中、Enter=执行整行。直接读物理键——本弹窗持有输入锁，别处不会抢。</summary>
        private void Update()
        {
            if (_suggester == null || _isClosing) return;

            if (Input.GetKeyDown(KeyCode.Tab) && _suggestions.Count > 0)
            {
                ApplySuggestion(_selectedIndex >= 0 ? _selectedIndex : 0);
                return;
            }
            if (Input.GetKeyDown(KeyCode.DownArrow) && _suggestions.Count > 0)
            {
                _selectedIndex = (_selectedIndex + 1) % _suggestions.Count;
                RefreshRowSelection();
                inputField.ActivateInputField();   // 焦点保持：防 EventSystem 方向导航把焦点从输入框带走
            }
            if (Input.GetKeyDown(KeyCode.UpArrow) && _suggestions.Count > 0)
            {
                _selectedIndex = _selectedIndex <= 0 ? _suggestions.Count - 1 : _selectedIndex - 1;
                RefreshRowSelection();
                inputField.ActivateInputField();
            }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                OnConfirm();
            }
        }

        private void OnCommandInputChanged(string text)
        {
            if (_suggester == null) return;
            _suggestions.Clear();
            var list = _suggester?.Invoke(text);
            if (list != null) _suggestions.AddRange(list);
            _selectedIndex = _suggestions.Count > 0 ? 0 : -1;
            RebuildSuggestRows();
        }

        /// <summary>重建建议行：行数对齐建议数（上限 maxSuggestRows，超出截断——补全列表不翻页，
        /// 前缀过滤本身就是导航手段）。行结构/样式全在模板 prefab，运行时只实例化+设文本+摆位置。</summary>
        private void RebuildSuggestRows()
        {
            if (suggestPanel == null || suggestRowTemplate == null) return;

            int showCount = Mathf.Min(_suggestions.Count, maxSuggestRows);
            // 行复用：不足补实例、多余销毁（≤10 行，销毁可接受）
            while (_rows.Count < showCount)
            {
                var go = Instantiate(suggestRowTemplate, suggestPanel);
                go.SetActive(true);
                var rowComp = go.GetComponent<CommandSuggestRow>();
                if (rowComp == null)
                {
                    // 模板脚本引用坏（missing script）→ 单条告警+收起列表，不逐行刷 NRE
                    GICLog.Warn("[InputPopupDialog] SuggestRowTemplate 的 CommandSuggestRow 脚本引用缺失（prefab 需重挂）——补全列表本次不可用");
                    Destroy(go);
                    HideSuggest();
                    return;
                }
                _rows.Add(rowComp);
            }
            while (_rows.Count > showCount)
            {
                Destroy(_rows[_rows.Count - 1].gameObject);
                _rows.RemoveAt(_rows.Count - 1);
            }

            for (int i = 0; i < _rows.Count; i++)
            {
                int index = i;   // 闭包捕获
                var row = _rows[i];
                row.Set(_suggestions[i]);
                row.SetSelected(index == _selectedIndex);
                row.Button.onClick.RemoveAllListeners();
                row.Button.onClick.AddListener(() => ApplySuggestion(index));
                // 手动摆位（无 LayoutGroup，确定性）：顶部向下排
                var rt = (RectTransform)row.transform;
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(0f, -(SuggestPanelPadding + i * suggestRowHeight));
                rt.sizeDelta = new Vector2(-SuggestPanelPadding * 2f, suggestRowHeight);
            }

            bool visible = _rows.Count > 0;
            suggestPanel.gameObject.SetActive(visible);
            if (visible)
                suggestPanel.sizeDelta = new Vector2(suggestPanel.sizeDelta.x,
                    SuggestPanelPadding * 2f + _rows.Count * suggestRowHeight);
        }

        private void RefreshRowSelection()
        {
            for (int i = 0; i < _rows.Count; i++)
                _rows[i].SetSelected(i == _selectedIndex);
        }

        /// <summary>应用补全（IDE "补一个词"）：替换**正在输入的词**为建议词，按建议决定是否尾随空格；
        /// 补全后立即刷新建议（进入参数阶段/更窄的过滤）并把光标挪到行尾。
        /// 正在输入的词=最后一个空格之后的段——**行尾是空格时该词为空串**（前一个词已完成，
        /// 此时按追加处理；2026-09-13 实弹测试实证：先 TrimEnd 再找边界会把已完成的命令词吃掉）。</summary>
        private void ApplySuggestion(int index)
        {
            if (index < 0 || index >= _suggestions.Count) return;
            var suggestion = _suggestions[index];

            string text = inputField.text.Replace((char)0x3000, ' ');   // 全角空格归一
            string prefix;
            if (text.EndsWith(" "))
            {
                prefix = text;   // 尾随空格=正在输入的词为空 → 追加（勿剪尾空格，否则上一词会被误替换）
            }
            else
            {
                text = text.TrimEnd();
                int lastSpace = text.LastIndexOf(' ');
                prefix = lastSpace >= 0 ? text.Substring(0, lastSpace + 1) : "";
            }

            string completed = prefix + suggestion.main + (suggestion.trailingSpace ? " " : "");
            inputField.SetTextWithoutNotify(completed);
            inputField.caretPosition = inputField.text.Length;
            inputField.stringPosition = inputField.text.Length;
            inputField.ActivateInputField();

            _suggestions.Clear();
            var list = _suggester?.Invoke(completed);
            if (list != null) _suggestions.AddRange(list);
            _selectedIndex = _suggestions.Count > 0 ? 0 : -1;
            RebuildSuggestRows();
        }

        /// <summary>隐藏并清空建议列表（Hide/复用重开/切回普通模式共用）</summary>
        private void HideSuggest()
        {
            foreach (var row in _rows) if (row != null) Destroy(row.gameObject);   // 空引用守卫：坏模板行不炸关闭路径
            _rows.Clear();
            _suggestions.Clear();
            _selectedIndex = -1;
            if (suggestPanel != null) suggestPanel.gameObject.SetActive(false);
        }

        private void Hide()
        {
            if (_isClosing) return; // 防重入：确认/取消/ESC 多路径
            _isClosing = true;
            _shown = false;

            _suggester = null;      // 命令模式交互随弹窗关闭停摆（Update/键盘处理立即失效）
            HideSuggest();
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
