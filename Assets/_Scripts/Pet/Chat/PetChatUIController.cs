using System;
using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;
using GIC.Framework;

namespace GIC.Pet.Chat
{
    /// <summary>
    /// 派蒙对话 UI 控制器（docs/19 §6.5，2026-08-28）：单击派蒙弹出输入框 → DeepSeek 流式回复 →
    /// 打字机气泡（原神风九切片）。程序化构建（对齐 PetInGameHostController 建全屏画中画的模式——
    /// 挂 Canvas 根下运行时自建 UI，prefab 只需预挂本组件+接线气泡贴图与字体）。
    ///
    /// 结构（2026-08-29 重构：聊天 UI 全部跟随派蒙模型，输入条不再钉屏底）：回复气泡（锚在派蒙
    /// 头顶：头骨屏幕位置由宿主经 头部锚点提供器 注入，每帧跟随——拖走/移动气泡跟头走）+
    /// 输入条（挂模型脚底下方：底部锚点提供器=模型包围盒底中心，宿主注入；脚底放不下——贴屏底/
    /// 坐任务栏时自动翻转到气泡上方——输入条与气泡都不遮模型，派蒙拖到哪聊到哪）。
    /// 流式：正文增量 → 打字机追加；完成 → 停留 气泡停留秒 后淡出；错误 → 气泡显示错误文案。
    /// 输入期间 InputLocks 压锁（按键不漏进主游戏），关闭/发送后释放。
    ///
    /// 两形态共用（桌面版后续挂桌宠场景 Canvas 同款）：坐标全部走"宿主提供的屏幕锚点"，
    /// 本组件不关心窗口/RT 相机差异。
    /// </summary>
    public class PetChatUIController : MonoBehaviour
    {
        [Header("素材（prefab 接线；空=纯色底兜底）")]
        [Tooltip("九切片气泡贴图（原神风暖白圆角）")]
        [InspectorName("气泡贴图")]
        [SerializeField] private Sprite bubbleSprite;
        [Tooltip("输入条底贴图（九切片；空=用气泡贴图）")]
        [InspectorName("输入底贴图")]
        [SerializeField] private Sprite inputBottomSprite;
        [Tooltip("星形装饰贴图（可选，气泡左上角贴花）")]
        [InspectorName("星形贴图")]
        [SerializeField] private Sprite starSprite;
        [InspectorName("发送按钮贴图")]
        [Tooltip("发送按钮贴图（AI 生成原神风纸飞机图标按钮，Simple 显示；空=气泡贴图染色兜底）")]
        [SerializeField] private Sprite sendBtnSprite;
        [Tooltip("TMP 字体（空=TMP Settings 默认字体——项目已配中文回退）")]
        [InspectorName("字体")]
        [SerializeField] private TMP_FontAsset font;

        [Header("会话")]
        [InspectorName("会话")]
        [SerializeField] private PaimonChatSession session;

        [Header("布局")]
        [Tooltip("回复气泡最大宽度（Canvas 像素）")]
        [InspectorName("气泡最大宽")]
        [SerializeField] private float bubbleMaxW = 520f;
        [Tooltip("回复气泡相对头部锚点的偏移（上移出头）")]
        [InspectorName("气泡偏移")]
        [SerializeField] private Vector2 bubbleOffset = new Vector2(0f, 60f);
        [Tooltip("回复气泡在屏内自动钳制的边距")]
        [InspectorName("屏内边距")]
        [SerializeField] private float screenMargin = 30f;
        [Tooltip("输入条高度")]
        [InspectorName("输入条高")]
        [SerializeField] private float inputBarH = 56f;
        [Tooltip("输入条顶边距模型脚底的间距（输入条挂在模型下方；翻转时也用作与气泡的间距）")]
        [InspectorName("输入条距modelBottom")]
        [SerializeField] private float inputBarToModelBottom = 20f;

        [Header("行为")]
        [Tooltip("打字机：每字符间隔秒（0=直出）")]
        [InspectorName("打字间隔秒")]
        [SerializeField] private float typeIntervalSec = 0.02f;
        [Tooltip("未设置 API Key 时的提示语（Inspector 兜底；正常路径走 UIText 表 PetChatNoKey 键，2026-08-29 本地化）")]
        [InspectorName("未设密钥提示")]
        [SerializeField] private string noKeyPrompt = "还没设置 API Key 哦！去 设置→派蒙→对话 API Key 里填一个吧。";
        [Tooltip("主动气泡（AI 抽卡反应等非对话消息）打字完成后停留秒数，到时自动淡出——对话回复气泡不在此列（维持常驻规则）")]
        [InspectorName("主动气泡停留秒")]
        [SerializeField] private float proactiveHoldSec = 6f;
        [Tooltip("主动气泡/输入条/关闭气泡的淡出时长")]
        [InspectorName("淡出时长")]
        [SerializeField] private float fadeSec = 0.25f;
        [Tooltip("输入条打开/关闭的过渡时长")]
        [InspectorName("AnimateInputBar秒")]
        [SerializeField] private float inputFadeSec = 0.15f;

        // ---- 运行时 ----
        private Canvas _canvas;
        private RectTransform _inputBarRoot;
        private TMP_InputField _inputField;
        private RectTransform _bubbleRoot;
        private TextMeshProUGUI _bubbleText;
        private CanvasGroup _bubbleGroup;
        private CanvasGroup _inputBarGroup;   // 输入条淡入淡出（2026-08-31）
        private Coroutine _inputBarAnim;
        private Coroutine _bubbleFadeCoroutine; // 主动气泡/关闭对话的气泡淡出（新内容到达即取消）
        private bool _proactiveMode;    // 主动气泡模式（AI 反应）：完成后自动淡出；对话 Send 切回常驻模式
        private RectTransform _starIcon;
        private readonly StringBuilder _typingBuffer = new StringBuilder();
        private Coroutine _typingCoroutine;
        private Coroutine _thinkingCoroutine; // 思考指示动画（等待 LLM 首字节：省略号循环；首增量到达即停）
        private bool _inputVisible;
        private bool _prewarmed; // 对话预热已发（每进程一次：首次打开输入条触发——打字期间后台建连+建供应商前缀缓存，首次对话不再明显慢于后续）
        private bool _awaitingFirstByte; // 发送后的"…"占位：首个流式增量到达时清掉再追加（防"…回复"拼接）
        private Action<string> _uiCompleteHandler; // 界面侧 完成 事件处理器（持引用可摘——会话层也接此事件，勿整体赋值）
        private Action<string> _uiDeltaHandler; // 界面侧 正文增量 处理器（持引用摘挂——2026-08-31 前 Send 用 = 整体
                                                // 覆盖，会把 PetReactionConsumer 挂的反应处理器一起顶掉）
        private Action<string> _uiErrorHandler; // 界面侧 错误 处理器（持引用摘挂，同上）

        /// <summary>对话流接管通知（Send 重接流式回调前触发，2026-08-31）：PetReactionConsumer 订阅——
        /// 在途的反应流将被新请求 AbortActive **静默**中止（onComplete/onError 均不触发），
        /// 其 _generating 状态靠本通知复位并摘除自己的处理器（对话优先，本批反应错失不弹 fallback）。</summary>
        public event Action chatStreamTakingOver;

        /// <summary>头部锚点提供器（宿主注入：返回派蒙头顶的 Canvas 屏幕坐标；null=气泡藏屏顶中央）</summary>
        [HideInInspector] public System.Func<Vector2> headAnchorProvider;

        /// <summary>底部锚点提供器（宿主注入：返回派蒙模型包围盒底中心的 Canvas 屏幕坐标——
        /// 输入条挂在模型下方；null=输入条退回屏底居中兜底）</summary>
        [HideInInspector] public System.Func<Vector2> footAnchorProvider;

        /// <summary>输入框当前打开（宿主据此冻结拖拽误判；未接线=恒 false 不挡交互）</summary>
        public bool IsInputVisible => _inputVisible && _inputBarRoot != null;

        /// <summary>屏幕点是否落在输入条矩形内（宿主区分"单击派蒙收起输入框"与"点输入条本身"——
        /// 输入期点派蒙=收起，点输入条（含发送按钮）=正常 UI 交互不收起）</summary>
        public bool IsPointOnInputBar(Vector2 screenPoint)
        {
            if (_inputBarRoot == null || !_inputBarRoot.gameObject.activeSelf || _canvas == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(_inputBarRoot, screenPoint, _canvas.worldCamera);
        }

        /// <summary>屏幕点是否落在回复气泡矩形内（2026-08-29 外点关闭用：气泡是"对话元素"之一，
        /// 点它不该关对话——用户在回看内容）</summary>
        public bool IsPointOnBubble(Vector2 screenPoint)
        {
            if (_bubbleRoot == null || !_bubbleRoot.gameObject.activeSelf || _canvas == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(_bubbleRoot, screenPoint, _canvas.worldCamera);
        }

        /// <summary>关闭对话（2026-08-29 外点关闭/ESC 共用入口）：收输入条+藏气泡——气泡不再随时间
        /// 消失（用户拍板），生命周期=显式关闭（点对话元素以外的地方/ESC/收起）或下一条回复覆盖。
        /// 2026-08-31 改为淡出（输入条与气泡都有过渡动画）。</summary>
        public void CloseChat()
        {
            closeInput();
            if (_typingCoroutine != null) { StopCoroutine(_typingCoroutine); _typingCoroutine = null; }
            StopThinking();
            _awaitingFirstByte = false;
            _proactiveMode = false;
            if (_bubbleRoot != null && _bubbleRoot.gameObject.activeSelf)
                StartBubbleFade(0f); // 立即淡出（无停留）
        }

        /// <summary>主动气泡（LLM 失败兜底直出——AI 抽卡反应等）：整段显示，
        /// 停留 主动气泡停留秒 后自动淡出（2026-08-31：主动消息不常驻）。
        /// UI 未接线（无 Canvas）时静默。</summary>
        public void ShowProactive(string text)
        {
            if (string.IsNullOrEmpty(text) || _bubbleText == null) return;
            _proactiveMode = true;
            showBubble(text);
            StartBubbleFade(proactiveHoldSec);
        }

        // ---- 主动气泡流式入口（PetReactionConsumer 的 LLM 反应生成驱动，2026-08-31） ----

        /// <summary>开始一条主动流式反应（清场+进入主动模式；首字节前气泡内播放思考动画）</summary>
        public void BeginProactiveStream()
        {
            if (_bubbleText == null) return;
            _proactiveMode = true;
            CancelBubbleFade();
            if (_typingCoroutine != null) { StopCoroutine(_typingCoroutine); _typingCoroutine = null; }
            StartThinking(); // 内含清缓冲/显气泡/起省略号动画——首个正文增量到达由 typewriter 停止
        }

        /// <summary>主动流式增量（打字机追加——与对话流共用 typewriter；typewriter 内会取消淡出计时）</summary>
        public void ProactiveDelta(string delta) => typewriter(delta);

        /// <summary>主动流式完成：起自动淡出计时（停留 → 淡出 → 隐藏）</summary>
        public void ProactiveStreamDone()
        {
            if (!_proactiveMode) return;
            StartBubbleFade(proactiveHoldSec);
        }

        /// <summary>会话客户端（发送前接流式事件用）——转发私有字段</summary>
        public PetChatClient client => session != null ? session.clientRef : null;

        /// <summary>会话公开只读（宿主接线 Intent 工具表用：RegisterTools）</summary>
        public PaimonChatSession sessionRef => session;

        void Awake()
        {
            if (session == null) session = GetComponentInParent<PaimonChatSession>(true);
            if (session == null) session = GetComponent<PaimonChatSession>();
            // Canvas 接线延迟到 宿主接线()（宿主建好画中画 Canvas 后调用）：组件挂在 prefab 根，
            // 根下无父 Canvas——Awake 时建界面必失败（2026-08-28 单击报 NullReference 的根因）。
            // 未接线前 输入开着 恒 false 不挡交互。
        }

        /// <summary>宿主接线（宿主 Canvas 就绪后调用）：此刻才建界面。Canvas 由宿主直传（组件可挂
        /// 任意物体——如 prefab 根，Canvas 在其子树下，向上找不到）；空=向上找（组件挂 Canvas 下时用）。
        /// 2026-08-29 修单击 NRE：旧版靠宿主把本组件 SetParent 挪进 Canvas——但组件挂在 prefab 根
        /// （=宿主根）时挪根=挪进自己的子 Canvas（循环父子被拒）→向上仍无 Canvas→自禁用→NRE。
        /// 重复调用幂等。</summary>
        public void WireHost(Canvas hostCanvas = null)
        {
            if (_canvas != null) return;
            _canvas = hostCanvas != null ? hostCanvas : GetComponentInParent<Canvas>();
            if (_canvas == null) { Debug.LogWarning("[PetChatUI] 无 Canvas，禁用"); enabled = false; return; }
            BuildUI();
        }

        void Update()
        {
            // ESC 收起输入条（2026-08-29：旧版输入期宿主交互全冻结+无 ESC=输入条打开后只能靠发送
            // 消失，用户被困在"打开→发送→消失"循环——多一条退出途径）。ESC=完整关闭对话（含气泡）。
            if (_inputVisible && Input.GetKeyDown(KeyCode.Escape)) { CloseChat(); return; }
            if (_canvas == null) return; // 宿主接线前的防御（WireHost 未调用时恒静默——2026-08-30 桌面版窗口句柄事故：Start 提前 return 吞掉接线=此处每帧 NRE 刷屏 9.6 万条）
            var canvasRect = (_canvas.transform as RectTransform).rect;
            // 回复气泡跟随头部锚点（拖拽/移动/动画头部都在动——每帧跟；按半宽钳回屏内防贴边裁切）
            if (_bubbleRoot != null && _bubbleRoot.gameObject.activeSelf && headAnchorProvider != null)
            {
                Vector2 p = headAnchorProvider();
                float bubbleHalfWidth = _bubbleRoot.sizeDelta.x * 0.5f;
                p.x = Mathf.Clamp(p.x + bubbleOffset.x,
                    Mathf.Min(screenMargin + bubbleHalfWidth, canvasRect.width * 0.5f),
                    Mathf.Max(canvasRect.width - screenMargin - bubbleHalfWidth, canvasRect.width * 0.5f));
                p.y = Mathf.Clamp(p.y + bubbleOffset.y, screenMargin, canvasRect.height - screenMargin);
                _bubbleRoot.anchoredPosition = p;
            }
            // 输入条跟随模型（开着才需要；翻转判定依赖气泡当前高度，故在气泡跟随之后算）
            if (_inputVisible && _inputBarRoot != null) FollowInputBar(canvasRect);
        }

        /// <summary>输入条跟随模型（每帧，输入开着时）：默认挂模型脚底下方（底部锚点=模型包围盒底
        /// 中心）；脚底放不下（贴屏底/坐任务栏腿垂出屏）时翻转到气泡上方——输入条与气泡都不遮模型。
        /// 宿主未注入 底部锚点提供器 时退回屏底居中（理论兜底——现行宿主都会注入）。</summary>
        void FollowInputBar(Rect canvasRect)
        {
            if (footAnchorProvider == null)
            {
                // 顶中心 pivot：y=条底距屏底+条高，使条底贴 输入条距modelBottom
                _inputBarRoot.anchoredPosition = new Vector2(canvasRect.width * 0.5f, inputBarToModelBottom + inputBarH);
                return;
            }
            Vector2 modelBottom = footAnchorProvider();
            float halfWidth = _inputBarRoot.sizeDelta.x * 0.5f;
            float x = Mathf.Clamp(modelBottom.x,
                Mathf.Min(screenMargin + halfWidth, canvasRect.width * 0.5f),
                Mathf.Max(canvasRect.width - screenMargin - halfWidth, canvasRect.width * 0.5f));
            // 默认：输入条顶边贴模型脚底下方（pivot=顶中心）
            float y = modelBottom.y - inputBarToModelBottom;
            if (y - inputBarH < screenMargin)
            {
                // 脚底放不下：翻转到气泡上方（气泡隐藏时按头锚点+偏移估气泡底）
                float bubbleTop = _bubbleRoot != null && _bubbleRoot.gameObject.activeSelf
                    ? _bubbleRoot.anchoredPosition.y + _bubbleRoot.sizeDelta.y
                    : (headAnchorProvider != null ? headAnchorProvider().y + bubbleOffset.y : modelBottom.y + 240f);
                y = Mathf.Min(bubbleTop + inputBarToModelBottom + inputBarH, canvasRect.height - screenMargin);
            }
            _inputBarRoot.anchoredPosition = new Vector2(x, y);
        }

        // ---- 对外（宿主单击回调驱动） ----

        /// <summary>切换输入框（单击派蒙调用；流式回复进行中=收起不重开）</summary>
        public void ToggleInput()
        {
            if (_inputBarRoot == null) return; // UI 未建（未接线/接线失败）——单击静默忽略，不再炸 NRE
            if (_inputVisible) closeInput();
            else openInput();
        }

        void openInput()
        {
            _inputVisible = true;
            // 首次打开输入条=明确对话意图：后台预热（建连+供应商前缀缓存），用户打字的几秒正好用上
            if (!_prewarmed)
            {
                _prewarmed = true;
                session?.Prewarm();
            }
            _inputBarRoot.gameObject.SetActive(true);
            FollowInputBar((_canvas.transform as RectTransform).rect); // 立即摆位，防一帧闪在旧位置
            InputLocks.Push(this, InputLockReason.InputPopupEntering); // 输入期按键不漏进游戏
            AnimateInputBar(1f); // 淡入（2026-08-31）
            _inputField.text = "";
            _inputField.ActivateInputField();
            _inputField.Select();
        }

        void closeInput()
        {
            if (!_inputVisible) return;
            _inputVisible = false;
            InputLocks.Pop(this, InputLockReason.InputPopupEntering);
            AnimateInputBar(0f); // 淡出后自动隐藏（2026-08-31）
        }

        /// <summary>输入条透明度过渡（打开淡入/关闭淡出；关闭到位后 SetActive(false)）。
        /// 淡出开始即不挡点击（blocksRaycasts=false），淡入即恢复。</summary>
        void AnimateInputBar(float targetAlpha)
        {
            if (_inputBarGroup == null)
            {
                // 无 CanvasGroup（异常兜底）：退回硬切
                if (_inputBarRoot != null && targetAlpha <= 0f) _inputBarRoot.gameObject.SetActive(false);
                return;
            }
            if (_inputBarAnim != null) StopCoroutine(_inputBarAnim);
            _inputBarGroup.blocksRaycasts = targetAlpha > 0.5f;
            _inputBarAnim = StartCoroutine(inputFadeRoutine(targetAlpha));
        }

        IEnumerator inputFadeRoutine(float target)
        {
            float from = _inputBarGroup.alpha;
            float el = 0f;
            while (el < inputFadeSec)
            {
                el += Time.deltaTime;
                _inputBarGroup.alpha = Mathf.Lerp(from, target, el / inputFadeSec);
                yield return null;
            }
            _inputBarGroup.alpha = target;
            if (target <= 0f) _inputBarRoot.gameObject.SetActive(false);
            _inputBarAnim = null;
        }

        // ---- 气泡自动淡出（主动消息/关闭对话，2026-08-31） ----

        /// <summary>StartBubbleFade：延迟后淡出再隐藏（主动气泡停留 → 自动消失；关闭对话=延迟 0 立即淡出）</summary>
        void StartBubbleFade(float delaySec)
        {
            CancelBubbleFade();
            if (_bubbleRoot == null || !_bubbleRoot.gameObject.activeSelf) return;
            _bubbleFadeCoroutine = StartCoroutine(bubbleFadeRoutine(delaySec));
        }

        /// <summary>取消未完成的自动淡出并恢复不透明（新内容到达/对话流接管时调用）</summary>
        void CancelBubbleFade()
        {
            if (_bubbleFadeCoroutine == null) return;
            StopCoroutine(_bubbleFadeCoroutine);
            _bubbleFadeCoroutine = null;
            if (_bubbleGroup != null) _bubbleGroup.alpha = 1f;
        }

        IEnumerator bubbleFadeRoutine(float delaySec)
        {
            if (delaySec > 0f) yield return Wait.Seconds(delaySec);
            float el = 0f;
            while (el < fadeSec)
            {
                el += Time.deltaTime;
                _bubbleGroup.alpha = 1f - el / fadeSec;
                yield return null;
            }
            _bubbleRoot.gameObject.SetActive(false);
            _bubbleGroup.alpha = 1f;
            _typingBuffer.Length = 0;
            _bubbleFadeCoroutine = null;
        }

        // ---- 界面构建（程序化） ----

        void BuildUI()
        {
            var canvasRect = _canvas.transform as RectTransform;

            // 输入条（挂模型下方，位置每帧由 FollowInputBar 算——建时先藏屏外）
            var inputBarObj = new GameObject("PetChatInputBar", typeof(RectTransform));
            inputBarObj.transform.SetParent(_canvas.transform, false);
            _inputBarRoot = inputBarObj.GetComponent<RectTransform>();
            _inputBarRoot.anchorMin = new Vector2(0f, 0f);
            _inputBarRoot.anchorMax = new Vector2(0f, 0f); // 左下锚：anchoredPosition 即画布绝对坐标（与气泡同约定，FollowInputBar 按绝对坐标摆位）
            _inputBarRoot.pivot = new Vector2(0.5f, 1f); // 顶中心：anchoredPosition.y=输入条顶边
            _inputBarRoot.anchoredPosition = new Vector2(0f, -inputBarH * 2f);
            _inputBarRoot.sizeDelta = new Vector2(Mathf.Min(bubbleMaxW, canvasRect.rect.width - screenMargin * 2f), inputBarH);

            _inputBarGroup = inputBarObj.AddComponent<CanvasGroup>(); // 淡入淡出过渡（2026-08-31）

            var inputBottom = inputBarObj.AddComponent<Image>();
            inputBottom.sprite = inputBottomSprite != null ? inputBottomSprite : bubbleSprite;
            inputBottom.type = Image.Type.Sliced;
            inputBottom.color = new Color(1f, 1f, 1f, 0.96f);

            var inputObj = new GameObject("Field", typeof(RectTransform));
            inputObj.transform.SetParent(inputBarObj.transform, false);
            var inputRect = inputObj.GetComponent<RectTransform>();
            // 全锚+偏移：右端让位固定尺寸发送按钮（44+右距12+间隙8=64）
            inputRect.anchorMin = new Vector2(0f, 0f);
            inputRect.anchorMax = new Vector2(1f, 1f);
            inputRect.offsetMin = new Vector2(26f, 4f);    // 左距 26>圆角 24：起笔在圆角收口之后
            inputRect.offsetMax = new Vector2(-64f, -4f);  // 上下对称内缩：Midline 垂直居中的可靠基准
            _inputField = inputObj.AddComponent<PetChatInputField>();
            // TMP 3.0.9 陷阱（两连 NRE 实证）：fontAsset/pointSize 的 setter（SetGlobalFontAsset/
            // SetGlobalPointSize）**无条件**解引用 textComponent（placeholder 有判空、textComponent
            // 没有，源码 L4593/L4605）——必须先接 textComponent/placeholder 再设 fontAsset/pointSize
            //（setter 会把字体字号推给两者；建文本/建占位自带的字体字号被同值重推，无副作用）
            _inputField.textComponent = BuildText(inputObj.transform, font, 26, TextAlignmentOptions.MidlineLeft);
            _inputField.placeholder = BuildPlaceholder(inputObj.transform, font, 26);
            _inputField.textComponent.color = new Color(0.24f, 0.18f, 0.10f); // 深棕输入文字（旧版白字白底=看不清）
            _inputField.caretColor = new Color(0.45f, 0.32f, 0.18f);
            _inputField.selectionColor = new Color(0.55f, 0.45f, 0.30f, 0.5f);
            _inputField.fontAsset = font != null ? font : TMP_Settings.defaultFontAsset;
            _inputField.pointSize = 26;
            _inputField.onEndEdit.AddListener(v => { if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Send(); });
            // 占位与正文同矩形（同 insets）——两者矩形不一致会显成"打字时文字跳位"
            ((RectTransform)_inputField.textComponent.transform).offsetMin = new Vector2(4f, 0f);
            ((RectTransform)_inputField.textComponent.transform).offsetMax = new Vector2(-4f, 0f);
            ((RectTransform)((Component)_inputField.placeholder).transform).offsetMin = new Vector2(4f, 0f);
            ((RectTransform)((Component)_inputField.placeholder).transform).offsetMax = new Vector2(-4f, 0f);

            var sendBtnObj = new GameObject("SendBtn", typeof(RectTransform));
            sendBtnObj.transform.SetParent(inputBarObj.transform, false);
            var sendBtnRect = sendBtnObj.GetComponent<RectTransform>();
            // 右端固定尺寸 44×44 图标按钮（2026-08-29 用户拍板：去掉"发送"文字，只放纸飞机图案——
            // 原神风简约；整图按钮 Simple 显示，preserveAspect 防拉伸变形）
            sendBtnRect.anchorMin = new Vector2(1f, 0.5f);
            sendBtnRect.anchorMax = new Vector2(1f, 0.5f);
            sendBtnRect.pivot = new Vector2(1f, 0.5f);
            sendBtnRect.anchoredPosition = new Vector2(-12f, 0f);
            sendBtnRect.sizeDelta = new Vector2(44f, 44f);
            var sendBtn = sendBtnObj.AddComponent<Button>();
            var sendBtnImg = sendBtnObj.AddComponent<Image>();
            bool useBtnSprite = sendBtnSprite != null;
            sendBtnImg.sprite = useBtnSprite ? sendBtnSprite : bubbleSprite;
            // Simple：素材是整图按钮（非九切片）；兜底路径气泡贴图小矩形 Sliced 也会退化（border×2>高）
            sendBtnImg.type = Image.Type.Simple;
            sendBtnImg.preserveAspect = true;
            sendBtnImg.color = useBtnSprite ? Color.white : new Color(0.83f, 0.66f, 0.34f, 0.9f); // 素材自带配色勿染色
            sendBtn.onClick.AddListener(Send);
            // 图标按钮无文字子物体：一 GameObject 一 Graphic 的陷阱源头已随 Label 一并移除
            //（2026-08-29 版本前 Label 挂 TMP+TextCombiner 做"发送"静态标签——用户拍板删除）。

            // 回复气泡（锚点每帧跟随头部）
            var bubbleGO = new GameObject("PetChatBubble", typeof(RectTransform));
            bubbleGO.transform.SetParent(_canvas.transform, false);
            _bubbleRoot = bubbleGO.GetComponent<RectTransform>();
            // 左下锚：anchoredPosition 即画布绝对坐标（与输入条同约定）。**程序化新建 RectTransform
            // 默认锚=画布中心**——不显式设锚，按绝对坐标写入=整体偏移(+半屏宽,+半屏高)，
            // 气泡飞到右上远处（2026-08-29 首测目检报障根因）。
            _bubbleRoot.anchorMin = new Vector2(0f, 0f);
            _bubbleRoot.anchorMax = new Vector2(0f, 0f);
            _bubbleRoot.pivot = new Vector2(0.5f, 0f); // 底中心=头顶锚点
            _bubbleRoot.sizeDelta = new Vector2(bubbleMaxW, 120f);
            _bubbleGroup = bubbleGO.AddComponent<CanvasGroup>();
            _bubbleGroup.blocksRaycasts = false; // 气泡不挡点击

            var bubbleBottomObj = new GameObject("BG", typeof(RectTransform));
            bubbleBottomObj.transform.SetParent(bubbleGO.transform, false);
            var bubbleBottomRect = bubbleBottomObj.GetComponent<RectTransform>();
            bubbleBottomRect.anchorMin = Vector2.zero;
            bubbleBottomRect.anchorMax = Vector2.one;
            bubbleBottomRect.offsetMin = bubbleBottomRect.offsetMax = Vector2.zero;
            var bubbleBottomSprite = bubbleBottomObj.AddComponent<Image>();
            bubbleBottomSprite.sprite = bubbleSprite;
            bubbleBottomSprite.type = Image.Type.Sliced;
            bubbleBottomSprite.color = new Color(1f, 1f, 1f, 0.96f);

            var textObj = new GameObject("Text", typeof(RectTransform));
            textObj.transform.SetParent(bubbleGO.transform, false);
            var textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0f, 0f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.offsetMin = new Vector2(24f, 14f);
            textRect.offsetMax = new Vector2(-24f, -14f);
            _bubbleText = textObj.AddComponent<TextMeshProUGUI>();
            _bubbleText.font = _inputField.fontAsset;
            _bubbleText.fontSize = 28;
            _bubbleText.color = new Color(0.24f, 0.18f, 0.10f); // 深棕文字（暖白底）
            _bubbleText.enableWordWrapping = true;
            _bubbleText.raycastTarget = false;

            // 星形贴花（可选）
            if (starSprite != null)
            {
                var starObj = new GameObject("Star", typeof(RectTransform));
                starObj.transform.SetParent(bubbleGO.transform, false);
                _starIcon = starObj.GetComponent<RectTransform>();
                _starIcon.anchorMin = _starIcon.anchorMax = new Vector2(0f, 1f);
                _starIcon.pivot = new Vector2(0.5f, 0.5f);
                _starIcon.anchoredPosition = new Vector2(6f, 6f); // 左上角探出一点
                _starIcon.sizeDelta = new Vector2(36f, 36f);
                var starImg = starObj.AddComponent<Image>();
                starImg.sprite = starSprite;
                starImg.raycastTarget = false;
            }
            bubbleGO.SetActive(false);
            inputBarObj.SetActive(false); // 输入条初始隐藏（旧版建完不藏=接线即常驻屏底）
        }

        TextMeshProUGUI BuildText(Transform parent, TMP_FontAsset fontAsset, float fontSize, TMPro.TextAlignmentOptions alignment)
        {
            var textObj = new GameObject("TMP", typeof(RectTransform));
            textObj.transform.SetParent(parent, false);
            var textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            var label = textObj.AddComponent<TextMeshProUGUI>();
            label.font = fontAsset != null ? fontAsset : TMP_Settings.defaultFontAsset;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.raycastTarget = false;
            return label;
        }

        TextMeshProUGUI BuildPlaceholder(Transform parent, TMP_FontAsset fontAsset, float fontSize)
        {
            var label = BuildText(parent, fontAsset, fontSize, TextAlignmentOptions.MidlineLeft);
            // 占位文本本地化：TMP_InputField placeholder 无法挂 TextCombiner（InputField 会覆写 text），
            // 同步解析直赋——InputPopupDialog placeholder 同款例外（项目 UI 规范既定例外）
            label.text = GetLocalizedText("PetChatPlaceholder", "问派蒙点什么…");
            label.fontStyle = FontStyles.Italic;
            label.color = new Color(0.5f, 0.45f, 0.4f, 0.7f);
            return label;
        }

        /// <summary>运行时取 UIText 本地化文本（语言切换不自动刷新——仅用于 placeholder/动态气泡
        /// 文案等 TextCombiner 不适用的位置；静态标签一律挂 TextCombiner）</summary>
        static string GetLocalizedText(string key, string fallback)
        {
            var table = UnityEngine.Localization.Settings.LocalizationSettings.Instance.GetStringDatabase()
                .GetTable("UIText") as UnityEngine.Localization.Tables.StringTable;
            return table?.GetEntry(key)?.GetLocalizedString() ?? fallback;
        }

        // ---- 发送与流式回复 ----

        void Send()
        {
            string text = _inputField.text.Trim();
            if (string.IsNullOrEmpty(text)) return;
            // 流式进行中再发送=会话层静默丢弃（_请求中 直接 return）——给出可见反馈并保留输入
            if (session != null && session.IsBusy) { showBubble(GetLocalizedText("PetChatBusy", "等派蒙说完这句嘛！")); return; }
            // 发送后输入框保留（2026-08-29 用户实测反馈"点击发送后输入框按钮直接消失了"——聊天软件式
            // 连续对话：只清文本保持焦点，回复气泡照常显示；收起走 单击派蒙/ESC）
            _inputField.text = "";
            _inputField.ActivateInputField();
            _proactiveMode = false; // 用户发话=进入对话流：回复气泡恢复常驻规则（不自动淡出）
            StartThinking(); // 等待首字节（思考动画——替代旧静态"…"占位）
            _awaitingFirstByte = true;
            if (client == null) { showBubble(GetLocalizedText("PetChatNotWired", "对话组件未接线")); return; }

            // 先通知反应消费方复位+摘自己的处理器（对话优先：在途反应流将被新请求 AbortActive
            // 静默中止，其 onComplete/onError 不再触发——_generating 只能靠本通知复位，2026-08-31）
            chatStreamTakingOver?.Invoke();
            AttachStreamHandlers();
            session.Send(text);
        }

        /// <summary>重接对话流回调（Send 用）：三个处理器持引用摘旧挂新——链上恒最多一份界面处理器，
        /// 不整体覆盖（=）PetReactionConsumer 挂的反应处理器（2026-08-31 修复回调互踩）。</summary>
        void AttachStreamHandlers()
        {
            if (_uiDeltaHandler != null) client.onContentDelta -= _uiDeltaHandler;
            _uiDeltaHandler = delta => typewriter(delta);
            client.onContentDelta += _uiDeltaHandler;

            if (_uiErrorHandler != null) client.onError -= _uiErrorHandler;
            _uiErrorHandler = err =>
            {
                // 无 key 的错误给引导文案，其它原样
                string display = err != null && err.Contains("API Key")
                    ? GetLocalizedText("PetChatNoKey", noKeyPrompt)
                    : $"哎呀…{err}";
                showBubble(display);
            };
            client.onError += _uiErrorHandler;

            // 气泡常驻（2026-08-29 用户拍板：不随时间主动消失——显示到下一条回复/显式关闭为止）
            if (_uiCompleteHandler != null) client.onComplete -= _uiCompleteHandler;
            _uiCompleteHandler = 全量 => { };
            client.onComplete += _uiCompleteHandler;
        }

        /// <summary>摘除界面侧流式回调（PetReactionConsumer 发起反应流前调用——防反应增量同时触发
        /// 界面 typewriter 与反应打字=双重打字；UI 销毁清理也复用）</summary>
        public void DetachStreamHandlers()
        {
            var c = client;
            if (c == null) return;
            if (_uiDeltaHandler != null) { c.onContentDelta -= _uiDeltaHandler; _uiDeltaHandler = null; }
            if (_uiErrorHandler != null) { c.onError -= _uiErrorHandler; _uiErrorHandler = null; }
            if (_uiCompleteHandler != null) { c.onComplete -= _uiCompleteHandler; _uiCompleteHandler = null; }
        }

        /// <summary>打字机追加（流式增量逐段追加；间隔=0 直出）——新内容到达自动取消未完成的
        /// 主动气泡淡出计时（下一条反应重新起表）</summary>
        void typewriter(string delta)
        {
            if (string.IsNullOrEmpty(delta)) return;
            StopThinking(); // 首个正文增量到达：思考动画让位（对话/主动反应两路共用）
            if (_typingCoroutine != null) StopCoroutine(_typingCoroutine);
            CancelBubbleFade();
            if (_awaitingFirstByte) { _typingBuffer.Length = 0; _awaitingFirstByte = false; } // 清"…"占位（错误文案同理——增量代表新回复）
            _bubbleGroup.alpha = 1f;
            _bubbleRoot.gameObject.SetActive(true);
            _typingCoroutine = StartCoroutine(typingRoutineBody(delta));
        }

        IEnumerator typingRoutineBody(string delta)
        {
            _typingBuffer.Append(delta);
            _bubbleText.text = _typingBuffer.ToString();
            autoBubbleH();
            if (typeIntervalSec > 0f)
            {
                yield return new WaitForSeconds(typeIntervalSec); // 微停顿观感（增量本身已是分片，无需逐字符）
            }
            _typingCoroutine = null;
        }

        // ---- 思考动画（等待 LLM 首字节的气泡指示，2026-09-01） ----

        /// <summary>气泡内播放思考指示（省略号循环），等待 LLM 首字节——对话 Send 与主动反应流共用；
        /// 停止点=首个正文增量（typewriter）/整段直出（showBubble）/关闭对话（CloseChat）。
        /// 修复"反应气泡出现但空白"观感：LLM 生成（尤其思考型模型 reasoning 阶段）需要数秒，
        /// 此前无任何占位反馈。</summary>
        void StartThinking()
        {
            if (_bubbleText == null) return;
            StopThinking();
            _typingBuffer.Length = 0;
            _bubbleGroup.alpha = 1f;
            _bubbleRoot.gameObject.SetActive(true);
            _thinkingCoroutine = StartCoroutine(thinkingRoutine());
        }

        void StopThinking()
        {
            if (_thinkingCoroutine == null) return;
            StopCoroutine(_thinkingCoroutine);
            _thinkingCoroutine = null;
        }

        IEnumerator thinkingRoutine()
        {
            _bubbleRoot.sizeDelta = new Vector2(bubbleMaxW, 72f); // 紧凑高度（几个点撑不满整框）
            while (true)
            {
                _bubbleText.text = ".";
                yield return Wait.Seconds(0.35f);
                _bubbleText.text = "..";
                yield return Wait.Seconds(0.35f);
                _bubbleText.text = "...";
                yield return Wait.Seconds(0.35f);
            }
        }

        /// <summary>整段显示（错误/提示/非流式完成）</summary>
        void showBubble(string msg)
        {
            StopThinking(); // 整段内容直接顶掉思考动画（错误文案/兜底直出路径）
            if (_typingCoroutine != null) { StopCoroutine(_typingCoroutine); _typingCoroutine = null; }
            CancelBubbleFade();
            _typingBuffer.Length = 0;
            _typingBuffer.Append(msg);
            _bubbleText.text = msg;
            _bubbleGroup.alpha = 1f;
            _bubbleRoot.gameObject.SetActive(true);
            autoBubbleH();
        }

        /// <summary>按文本内容自适应气泡高（九切片宽度恒定=最大宽，高度随行数）</summary>
        void autoBubbleH()
        {
            _bubbleText.ForceMeshUpdate();
            float textHeight = _bubbleText.renderedHeight;
            float Height = Mathf.Max(72f, textHeight + 34f);
            _bubbleRoot.sizeDelta = new Vector2(bubbleMaxW, Height);
        }

        void OnDestroy()
        {
            DetachStreamHandlers(); // 摘界面侧流式回调（防 UI 销毁后回调仍指向 typewriter）
            if (_inputVisible) InputLocks.Pop(this, InputLockReason.InputPopupEntering);
            InputLocks.PopAll(this);
        }
    }

    /// <summary>聊天输入框（TMP_InputField 子类，2026-08-29）：覆写 OnDrag 掐掉
    /// MouseDragOutsideRect 协程路径——该协程经 eventData.pressEventCamera（ScreenSpaceOverlay
    /// 画布下恒 null）回退 Camera.main，而 GIC 纯 UI 场景（Settings 等）无 MainCamera tag 相机
    /// = 每帧 NullReferenceException 刷屏（TMP 3.0.9 源码 L1759 实证）。代价=拖拽出矩形选字失效
    /// （单击定位/双击选词/Shift+方向键选区不受影响——聊天输入条可接受）。</summary>
    public class PetChatInputField : TMP_InputField
    {
        public override void OnDrag(PointerEventData eventData)
        {
            // 刻意空实现：基类 OnDrag 在拖出文本视口时启动 MouseDragOutsideRect 协程（选字自动
            // 滚动），协程内 Camera.main 回退在无 MainCamera 的场景必炸。勿调 base.OnDrag。
        }
    }
}
