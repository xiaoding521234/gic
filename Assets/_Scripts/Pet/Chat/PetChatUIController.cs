using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
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

        [Header("快捷消息气泡（对话开着时拖拽派蒙弹出，2026-09-13）")]
        [Tooltip("气泡从身体中心飞出的时长（秒）")]
        [InspectorName("飞出秒")]
        [SerializeField] private float quickFlySec = 0.16f;
        [Tooltip("多个气泡依次飞出的错峰间隔（秒）")]
        [InspectorName("飞出间隔秒")]
        [SerializeField] private float quickStaggerSec = 0.035f;
        [Tooltip("气泡与派蒙身体边缘的水平间距（画布像素）")]
        [InspectorName("气泡侧距")]
        [SerializeField] private float quickSideGap = 26f;
        [Tooltip("同列相邻气泡的垂直间距（画布像素）")]
        [InspectorName("气泡行距")]
        [SerializeField] private float quickRowGap = 16f;
        [Tooltip("单个气泡高度（画布像素）")]
        [InspectorName("气泡高")]
        [SerializeField] private float quickChipH = 58f;
        [Tooltip("气泡最大宽度（按文字自适应，超出省略号截断）")]
        [InspectorName("气泡最大宽")]
        [SerializeField] private float quickMaxW = 230f;
        [Tooltip("气泡最小宽度")]
        [InspectorName("气泡最小宽")]
        [SerializeField] private float quickMinW = 96f;

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

        // ---- "/" 本地指令模式（2026-09-13，docs/25 §8）：同设置页指令行——补全/Tab/↑↓/点击/Enter 执行，
        // 回执直出气泡（不经 LLM、不进对话历史）。执行路由与 LLM 工具同路（session.RunCommandDirect：
        // 游戏内=主进程直调；桌面=IPC 转发）——两形态零分叉。 ----
        private RectTransform _slashPanel;   // 建议列表（挂输入条顶边向上生长，随输入条 CanvasGroup 淡入淡出）
        private readonly List<(RectTransform rt, TextMeshProUGUI main, TextMeshProUGUI hint, Image bg, Button btn)> _slashRows
            = new List<(RectTransform, TextMeshProUGUI, TextMeshProUGUI, Image, Button)>();
        private readonly List<CommandSuggestion> _slashSuggestions = new List<CommandSuggestion>();
        private int _slashSelected = -1;
        private const int SlashMaxRows = 6;   // 聊天窗窄：少于设置页的 8
        private const float SlashRowH = 40f;
        private const float SlashPad = 4f;

        /// <summary>对话流接管通知（Send 重接流式回调前触发，2026-08-31）：PetReactionConsumer 订阅——
        /// 在途的反应流将被新请求 AbortActive **静默**中止（onComplete/onError 均不触发），
        /// 其 _generating 状态靠本通知复位并摘除自己的处理器（对话优先，本批反应错失不弹 fallback）。
        /// 语音说话层（PetVoiceSpeaker）同订：新对话=清语音队列停播（2026-09-16，docs/19 §6.5.11）。</summary>
        public event Action chatStreamTakingOver;

        /// <summary>对话关闭通知（CloseChat 收尾触发，2026-09-16）：语音说话层订阅——
        /// ESC/外点关闭后停播清队列（语音不念给已关闭的气泡）。</summary>
        public event Action chatClosed;

        /// <summary>新气泡开始通知（showBubble/BeginProactiveStream 两个气泡入口触发，2026-09-16 用户拍板
        /// 「下一个气泡出来时，之前的停止」——反应通道高频时旧语音必须被打断不能叠播）：语音说话层订阅
        /// →清队列停播+废弃在途合成。对话流首增量走 typewriter 不触发本事件（不会自己打断自己）。/summary>
        public event Action bubbleStarted;

        /// <summary>头部锚点提供器（宿主注入：返回派蒙头顶的 Canvas 屏幕坐标；null=气泡藏屏顶中央）</summary>
        [HideInInspector] public System.Func<Vector2> headAnchorProvider;

        /// <summary>底部锚点提供器（宿主注入：返回派蒙模型包围盒底中心的 Canvas 屏幕坐标——
        /// 输入条挂在模型下方；null=输入条退回屏底居中兜底）</summary>
        [HideInInspector] public System.Func<Vector2> footAnchorProvider;

        /// <summary>可见边界提供器（宿主注入；2026-09-12 桌面边缘自适应）：返回"屏幕上真正可用的
        /// 矩形"映射到画布坐标系（左下锚+anchoredPosition 同空间）。null=画布即边界（游戏内形态
        /// 全屏画布天然正确）；桌面形态画布=贴身小窗跟派蒙移动，派蒙贴屏边时窗口坐标系感知不到
        /// 屏幕边缘——输入条"脚底放不下翻转"等判定必须以屏幕工作区为准，否则派蒙拖到屏幕底时
        /// 输入条画在脚底方向出屏外，看不见也点不到（2026-09-12 用户实测报告）。</summary>
        [HideInInspector] public System.Func<Rect, Rect> visibleBoundsProvider;

        /// <summary>翻转判定诊断序列（-1=未记；0=不翻/1=翻——变化才记防刷屏）</summary>
        private int _flipLogSeq = -1;

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
            EndQuickBubbles(false); // 快捷气泡随对话关闭收回（未在弹出中=无害 no-op）
            closeInput();
            if (_typingCoroutine != null) { StopCoroutine(_typingCoroutine); _typingCoroutine = null; }
            StopThinking();
            _awaitingFirstByte = false;
            _proactiveMode = false;
            if (_bubbleRoot != null && _bubbleRoot.gameObject.activeSelf)
                StartBubbleFade(0f); // 立即淡出（无停留）
            chatClosed?.Invoke(); // 语音说话层停播清队列（2026-09-16，docs/19 §6.5.11）
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
            bubbleStarted?.Invoke(); // 新气泡开始=打断旧语音（说话层清队列+废弃在途，2026-09-16 拍板）
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
            // "/" 指令模式键盘（docs/25 §8）：Tab 补词（选中优先）/ ↑↓ 切换（焦点保持）——
            // Enter 由既有 onEndEdit→Send 路径统一进入 RunSlashCommand；输入期持有 InputLocks 无他处抢键
            if (_inputVisible && _slashSuggestions.Count > 0)
            {
                if (Input.GetKeyDown(KeyCode.Tab)) ApplySlashSuggestion(_slashSelected >= 0 ? _slashSelected : 0);
                else if (Input.GetKeyDown(KeyCode.DownArrow))
                {
                    _slashSelected = (_slashSelected + 1) % _slashSuggestions.Count;
                    RefreshSlashSelection();
                    _inputField.ActivateInputField();   // 焦点保持：防 EventSystem 方向导航把焦点带走
                }
                else if (Input.GetKeyDown(KeyCode.UpArrow))
                {
                    _slashSelected = _slashSelected <= 0 ? _slashSuggestions.Count - 1 : _slashSelected - 1;
                    RefreshSlashSelection();
                    _inputField.ActivateInputField();
                }
            }
            if (_canvas == null) return; // 宿主接线前的防御（WireHost 未调用时恒静默——2026-08-30 桌面版窗口句柄事故：Start 提前 return 吞掉接线=此处每帧 NRE 刷屏 9.6 万条）
            Rect bounds = CurrentBounds();
            // 回复气泡跟随头部锚点（拖拽/移动/动画头部都在动——每帧跟；按半宽钳回可见区内防贴边裁切）
            if (_bubbleRoot != null && _bubbleRoot.gameObject.activeSelf && headAnchorProvider != null)
            {
                Vector2 p = headAnchorProvider();
                float bubbleHalfWidth = _bubbleRoot.sizeDelta.x * 0.5f;
                p.x = Mathf.Clamp(p.x + bubbleOffset.x,
                    Mathf.Min(bounds.xMin + screenMargin + bubbleHalfWidth, bounds.center.x),
                    Mathf.Max(bounds.xMax - screenMargin - bubbleHalfWidth, bounds.center.x));
                p.y = Mathf.Clamp(p.y + bubbleOffset.y, bounds.yMin + screenMargin, bounds.yMax - screenMargin);
                _bubbleRoot.anchoredPosition = p;
            }
            // 输入条跟随模型（开着才需要；翻转判定依赖气泡当前高度，故在气泡跟随之后算）
            if (_inputVisible && _inputBarRoot != null) FollowInputBar(bounds);
        }

        /// <summary>当前可见边界（气泡跟随/输入条跟随/快捷气泡布局共用）：画布矩形经宿主
        /// visibleBoundsProvider 映射（桌面=屏幕工作区映射进画布系；游戏内未注入=画布即边界）。
        /// **根画布 pivot 恒居中：RectTransform.rect 是枢轴中心局部空间（xMin=-w/2）——直接拿它当
        /// bounds 钳锚点=两空间错位（2026-09-13 实证：派蒙贴屏边时输入条被钳进画布中带、气泡同理，
        /// docs/14 §42）；画布自身在锚定空间恒为 (0,0,w,h)。**</summary>
        Rect CurrentBounds()
        {
            var canvasLocal = (_canvas.transform as RectTransform).rect;
            Rect canvasRect = new Rect(0f, 0f, canvasLocal.width, canvasLocal.height);
            return visibleBoundsProvider != null ? visibleBoundsProvider(canvasRect) : canvasRect;
        }

        /// <summary>输入条跟随模型（每帧，输入开着时）：默认挂模型脚底下方（底部锚点=模型包围盒底
        /// 中心）；脚底放不下（贴屏底/坐任务栏腿垂出屏）时翻转到气泡上方——输入条与气泡都不遮模型。
        /// 边缘判定以 可见边界（屏幕工作区）为准，不以画布为准（2026-09-12：桌面画布=贴身小窗，
        /// 派蒙拖到屏幕底时窗口坐标感知不到屏边，输入条画在屏外点不到）。
        /// 宿主未注入 底部锚点提供器 时退回可见区底居中（理论兜底——现行宿主都会注入）。</summary>
        void FollowInputBar(Rect bounds)
        {
            if (footAnchorProvider == null)
            {
                // 顶中心 pivot：y=条底距可见区底+条高，使条底贴 输入条距modelBottom
                _inputBarRoot.anchoredPosition = new Vector2(bounds.center.x, bounds.yMin + inputBarToModelBottom + inputBarH);
                return;
            }
            Vector2 modelBottom = footAnchorProvider();
            float halfWidth = _inputBarRoot.sizeDelta.x * 0.5f;
            float x = Mathf.Clamp(modelBottom.x,
                Mathf.Min(bounds.xMin + screenMargin + halfWidth, bounds.center.x),
                Mathf.Max(bounds.xMax - screenMargin - halfWidth, bounds.center.x));
            // 默认：输入条顶边贴模型脚底下方（pivot=顶中心）
            float y = modelBottom.y - inputBarToModelBottom;
            bool flip = y - inputBarH < bounds.yMin + screenMargin;
            // 诊断日志（2026-09-12 桌面边缘自适应排查）：翻转判定变化或首次才记，防每帧刷屏
            int seq = flip ? 1 : 0;
            if (seq != _flipLogSeq)
            {
                _flipLogSeq = seq;
                GICLog.DevInfo($"[PetChat] 边缘判定: flip={flip} modelBottom=({modelBottom.x:F0},{modelBottom.y:F0}) " +
                               $"bounds=({bounds.xMin:F0},{bounds.yMin:F0})-({bounds.xMax:F0},{bounds.yMax:F0}) y={y:F0}");
            }
            if (flip)
            {
                // 脚底放不下（出可见区/压任务栏）：翻转到气泡上方（气泡隐藏时按头锚点+偏移估气泡底）
                float bubbleTop = _bubbleRoot != null && _bubbleRoot.gameObject.activeSelf
                    ? _bubbleRoot.anchoredPosition.y + _bubbleRoot.sizeDelta.y
                    : (headAnchorProvider != null ? headAnchorProvider().y + bubbleOffset.y : modelBottom.y + 240f);
                y = Mathf.Min(bubbleTop + inputBarToModelBottom + inputBarH, bounds.yMax - screenMargin);
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
            // 立即摆位防一帧闪旧位置——与 Update 同源走可见边界（2026-09-12：首摆曾直传画布矩形，
            // 桌面贴屏边时首帧会画在屏外）
            FollowInputBar(CurrentBounds());
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
            HideSlashSuggestions();   // 建议列表随输入条收起（docs/25 §8）
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
            _inputField.onValueChanged.AddListener(OnChatInputChanged);   // "/" 指令模式实时补全（docs/25 §8）
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
        /// 文案等 TextCombiner 不适用的位置；静态标签一律挂 TextCombiner）。
        /// 桌宠进程守卫（2026-09-12）：桌宠构建产物不含 Addressables 运行数据（aa/settings.json），
        /// 触发 Localization 初始化必报 Invalid path 错链——桌宠直接走 fallback 文案，不碰取表；
        /// 游戏内形态（编辑器/主进程）照常本地化。</summary>
        static string GetLocalizedText(string key, string fallback)
        {
            if (PetMode.Enabled) return fallback;
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
            // "/" 开头=本地指令模式（docs/25 §8）：不经 LLM——回执直出气泡
            if (text.StartsWith("/")) { RunSlashCommand(text); return; }
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

        // ---- "/" 本地指令模式（docs/25 §8）----

        /// <summary>执行 "/" 指令并回执直出气泡（不经 LLM、不进对话历史、开发者格式原文）。
        /// 执行路由=会话层 _toolExecutor（游戏内直调主进程 / 桌面 IPC 转发），与 LLM 工具同路。
        /// 回执按主动消息语义：停留 proactiveHoldSec 后自动淡出。</summary>
        void RunSlashCommand(string text)
        {
            _inputField.text = "";               // 同聊天发送惯例：清文本保焦点
            _inputField.ActivateInputField();
            HideSlashSuggestions();
            string resultJson = session != null
                ? session.RunCommandDirect(text.Trim().TrimStart('/'))
                : "{\"error\":\"会话未接线\"}";
            _proactiveMode = true;                // 主动消息语义（下一条对话 Send 自动切回常驻）
            showBubble(PetChatIntent.ResultMessageOf(resultJson));
            StartBubbleFade(proactiveHoldSec);
        }

        /// <summary>输入变化回调（BuildUI 挂 onValueChanged）：/ 开头=实时补全建议，否则收起</summary>
        void OnChatInputChanged(string text)
        {
            if (!_inputVisible || _inputField == null) return;
            if (text.StartsWith("/"))
            {
                EnsureSlashPanel();
                _slashSuggestions.Clear();
                var list = CommandSystem.Suggest(text);   // Tokenize 自剥 / 前缀——斜杠无需预剥
                if (list != null) _slashSuggestions.AddRange(list);
                _slashSelected = _slashSuggestions.Count > 0 ? 0 : -1;
                RebuildSlashRows();
            }
            else if (_slashPanel != null && _slashPanel.gameObject.activeSelf)
                HideSlashSuggestions();
        }

        /// <summary>建议列表面板（懒建；挂输入条顶边随其淡入淡出——父有 CanvasGroup 自动继承）</summary>
        void EnsureSlashPanel()
        {
            if (_slashPanel != null) return;
            var go = new GameObject("SlashSuggest", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_inputBarRoot, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0f);    // 底边贴输入条顶边、向上生长
            rt.anchoredPosition = new Vector2(0f, 6f);
            rt.sizeDelta = new Vector2(_inputBarRoot.sizeDelta.x, 0f);
            var img = go.GetComponent<Image>();
            img.sprite = null;
            img.color = new Color(0.08f, 0.12f, 0.16f, 0.95f);
            go.SetActive(false);
            _slashPanel = rt;
        }

        /// <summary>行数对齐建议数（≤SlashMaxRows，复用行实例）；行结构=程序化（本 UI 全程序化惯例），
        /// 主词左+对照提示右、NoWrap 单行（docs/25 §4 行宽预算同约束）</summary>
        void RebuildSlashRows()
        {
            if (_slashPanel == null) return;
            int showCount = Mathf.Min(_slashSuggestions.Count, SlashMaxRows);
            while (_slashRows.Count < showCount)
            {
                var rowGo = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(Button));
                rowGo.transform.SetParent(_slashPanel, false);
                var bg = rowGo.GetComponent<Image>();
                bg.sprite = null;
                bg.color = new Color(1f, 1f, 1f, 0f);   // 常态透明（选中态换色）
                var btn = rowGo.GetComponent<Button>();
                btn.targetGraphic = null;

                var main = BuildText(rowGo.transform, font, 24, TextAlignmentOptions.MidlineLeft);
                main.color = Color.white;
                main.enableWordWrapping = false;
                var mainRt = (RectTransform)main.transform;
                mainRt.anchorMin = new Vector2(0f, 0f); mainRt.anchorMax = new Vector2(0.5f, 1f);
                mainRt.offsetMin = new Vector2(10f, 0f); mainRt.offsetMax = Vector2.zero;

                var hint = BuildText(rowGo.transform, font, 20, TextAlignmentOptions.MidlineRight);
                hint.color = new Color(0.55f, 0.62f, 0.72f, 1f);
                hint.enableWordWrapping = false;
                var hintRt = (RectTransform)hint.transform;
                hintRt.anchorMin = new Vector2(0.5f, 0f); hintRt.anchorMax = new Vector2(1f, 1f);
                hintRt.offsetMin = Vector2.zero; hintRt.offsetMax = new Vector2(-10f, 0f);

                _slashRows.Add(((RectTransform)rowGo.transform, main, hint, bg, btn));
            }
            while (_slashRows.Count > showCount)
            {
                Destroy(_slashRows[_slashRows.Count - 1].rt.gameObject);
                _slashRows.RemoveAt(_slashRows.Count - 1);
            }

            for (int i = 0; i < _slashRows.Count; i++)
            {
                int index = i;   // 闭包捕获
                var row = _slashRows[i];
                row.main.text = _slashSuggestions[i].main;
                row.hint.text = _slashSuggestions[i].hint ?? "";
                row.btn.onClick.RemoveAllListeners();
                row.btn.onClick.AddListener(() => ApplySlashSuggestion(index));
                row.rt.anchorMin = new Vector2(0f, 1f);
                row.rt.anchorMax = new Vector2(1f, 1f);
                row.rt.pivot = new Vector2(0.5f, 1f);
                row.rt.anchoredPosition = new Vector2(0f, -(SlashPad + i * SlashRowH));
                row.rt.sizeDelta = new Vector2(-SlashPad * 2f, SlashRowH);
            }
            RefreshSlashSelection();

            bool visible = _slashRows.Count > 0;
            if (_slashPanel.gameObject.activeSelf != visible) _slashPanel.gameObject.SetActive(visible);
            if (visible)
                _slashPanel.sizeDelta = new Vector2(_slashPanel.sizeDelta.x, SlashPad * 2f + _slashRows.Count * SlashRowH);
        }

        void RefreshSlashSelection()
        {
            for (int i = 0; i < _slashRows.Count; i++)
                _slashRows[i].bg.color = i == _slashSelected ? new Color(0.16f, 0.22f, 0.30f, 1f) : new Color(1f, 1f, 1f, 0f);
        }

        /// <summary>应用补全（IDE "补一个词"，同 InputPopupDialog 语义）：替换正在输入的词；尾随空格=
        /// 追加；命令词阶段（无空格）前缀保 "/" 维持指令模式。补全后立即刷新建议。</summary>
        void ApplySlashSuggestion(int index)
        {
            if (index < 0 || index >= _slashSuggestions.Count) return;
            var s = _slashSuggestions[index];

            string text = _inputField.text.Replace((char)0x3000, ' ');
            string prefix;
            if (text.EndsWith(" "))
                prefix = text;                       // 尾随空格=正在输入的词为空→追加
            else
            {
                text = text.TrimEnd();
                int lastSpace = text.LastIndexOf(' ');
                prefix = lastSpace >= 0 ? text.Substring(0, lastSpace + 1) : "/";   // 命令词阶段保斜杠
            }

            string completed = prefix + s.main + (s.trailingSpace ? " " : "");
            _inputField.SetTextWithoutNotify(completed);
            _inputField.stringPosition = completed.Length;
            _inputField.ActivateInputField();

            _slashSuggestions.Clear();
            var list = CommandSystem.Suggest(completed);
            if (list != null) _slashSuggestions.AddRange(list);
            _slashSelected = _slashSuggestions.Count > 0 ? 0 : -1;
            RebuildSlashRows();
        }

        /// <summary>收起建议列表（行实例保留复用；清建议状态防 Update 键盘处理残留）</summary>
        void HideSlashSuggestions()
        {
            _slashSuggestions.Clear();
            _slashSelected = -1;
            if (_slashPanel != null && _slashPanel.gameObject.activeSelf)
                _slashPanel.gameObject.SetActive(false);
        }

        // ---- 快捷消息气泡（2026-09-13）：对话开着时在派蒙身上拖拽弹出（≤5），从身体中心
        //      飞出到身旁左右两列；宿主喂数光标→悬停高亮；松手在气泡上=以输入条文本走既有
        //      Send 全链路（/开头=指令回执直出、否则发 LLM），松手在气泡外=收回不发。
        //      交互帧序（升格判定/松手提交）在两宿主，UI 与数据在此——铁律 13 共用层分工。 ----

        private class QuickChip
        {
            public RectTransform rt;
            public CanvasGroup cg;
            public Image img;
            public Vector2 center;   // 飞出原点（=身体中心，收回也回这里）
            public Vector2 target;   // 展开目标位
            public bool arrived;     // 飞出完成（之后悬停高亮才接管外观）
        }

        private readonly List<QuickChip> _quickChips = new List<QuickChip>();
        private readonly List<string> _quickMessages = new List<string>();
        private int _quickHover = -1;
        private Coroutine _quickAnim;   // 飞出/收回动画（End 时停飞出起收回，互斥）

        /// <summary>快捷气泡进行中（宿主据此接管交互帧：悬停喂数/松手提交，冻结拖拽判定）</summary>
        public bool QuickBubblesActive { get; private set; }

        /// <summary>弹出快捷气泡（宿主在"对话开着+在派蒙身上拖拽升级"时调）。
        /// 无快捷消息/未接线/已在弹出中=false（宿主视为无动作——对话开着本就不允许普通拖拽）。
        /// 每次弹出重读 pet.json（另一进程 qm 指令刚改完立即可见，PetPrefs 直读通道）。</summary>
        public bool BeginQuickBubbles()
        {
            if (QuickBubblesActive || _canvas == null || _bubbleText == null) return false;
            _quickMessages.Clear();
            _quickMessages.AddRange(PetQuickMessages.ReadAll());
            if (_quickMessages.Count == 0) return false;

            if (_quickAnim != null) { StopCoroutine(_quickAnim); _quickAnim = null; }
            ClearQuickChips();
            QuickBubblesActive = true;
            _quickHover = -1;

            // 展开基准：身体中心=头/脚锚点中点；身体半宽按模型高估算（Q 版近方，0.30 偏保守）。
            // 锚点未注入的异常态退回可见区中心（气泡照常弹出，位置略糙但不至于炸）
            Rect bounds = CurrentBounds();
            Vector2 center; float bodyHalfW;
            if (headAnchorProvider != null && footAnchorProvider != null)
            {
                Vector2 head = headAnchorProvider();
                Vector2 foot = footAnchorProvider();
                center = new Vector2((head.x + foot.x) * 0.5f, (head.y + foot.y) * 0.5f);
                bodyHalfW = Mathf.Max(60f, (head.y - foot.y) * 0.30f);
            }
            else
            {
                center = bounds.center;
                bodyHalfW = Mathf.Max(60f, bounds.height * 0.10f);
            }

            // 左右两列均衡分布（右列先填：ceil(n/2)，多数人右手惯用），列内绕身体中心垂直居中
            int n = _quickMessages.Count;
            int rightCount = (n + 1) / 2;
            float rowSpacing = quickChipH + quickRowGap;
            for (int i = 0; i < n; i++)
            {
                bool right = i < rightCount;
                int row = right ? i : i - rightCount;
                int colCount = right ? rightCount : n - rightCount;
                float yOff = (row - (colCount - 1) * 0.5f) * rowSpacing;

                var chip = BuildQuickChip(i);
                float dx = bodyHalfW + chip.rt.sizeDelta.x * 0.5f + quickSideGap;
                Vector2 target = new Vector2(center.x + (right ? dx : -dx), center.y + yOff);
                // 钳入可见边界（派蒙贴屏边时列位可能出界——宁可叠回身上不可不见）
                target.x = Mathf.Clamp(target.x, bounds.xMin + chip.rt.sizeDelta.x * 0.5f + screenMargin,
                                       bounds.xMax - chip.rt.sizeDelta.x * 0.5f - screenMargin);
                target.y = Mathf.Clamp(target.y, bounds.yMin + quickChipH * 0.5f + screenMargin,
                                       bounds.yMax - quickChipH * 0.5f - screenMargin);
                chip.center = center;
                chip.target = target;
                chip.rt.anchoredPosition = center;   // 从身体中心起飞
                chip.cg.alpha = 0f;
                _quickChips.Add(chip);
            }

            _quickAnim = StartCoroutine(quickFlyOutRoutine());
            return true;
        }

        /// <summary>悬停帧（宿主每帧喂数光标屏幕位）：命中已到位的气泡=高亮（金色调+微放大）。
        /// 未到位的 chip 不参与（飞出途中光标多半还在身上，含入判定=误选）。</summary>
        public void QuickBubbleHover(Vector2 screenPoint)
        {
            if (!QuickBubblesActive || _canvas == null) return;
            int hit = -1;
            for (int i = 0; i < _quickChips.Count; i++)
            {
                var c = _quickChips[i];
                if (!c.arrived) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(c.rt, screenPoint, _canvas.worldCamera))
                {
                    hit = i;
                    break;
                }
            }
            if (hit == _quickHover) return;
            _quickHover = hit;
            RefreshQuickHover();
        }

        /// <summary>结束快捷气泡（宿主松手调）：commit 且悬停在气泡上=把它当输入条文本走既有
        /// Send 全链路（/开头=RunSlashCommand 回执直出，否则发 LLM——与手打完全同路零分叉）；
        /// 其余（松手在气泡外/取消）=只收回。收回动画与回执/对话气泡并行，互不等待。</summary>
        public void EndQuickBubbles(bool commit)
        {
            if (!QuickBubblesActive) return;
            QuickBubblesActive = false;
            string send = null;
            if (commit && _quickHover >= 0 && _quickHover < _quickMessages.Count)
                send = _quickMessages[_quickHover];
            _quickHover = -1;

            if (_quickAnim != null) { StopCoroutine(_quickAnim); _quickAnim = null; }
            _quickAnim = StartCoroutine(quickRetractRoutine());

            if (!string.IsNullOrEmpty(send))
            {
                _inputField.text = send;   // 走 Send 全链路（含忙碌守卫/斜杠分发/清文本保焦点）
                Send();
            }
        }

        /// <summary>屏幕点是否落在任一快捷气泡上（桌面版穿透判定用：气泡区窗口不穿透）</summary>
        public bool IsPointOnQuickBubble(Vector2 screenPoint)
        {
            if (!QuickBubblesActive || _canvas == null) return false;
            for (int i = 0; i < _quickChips.Count; i++)
                if (RectTransformUtility.RectangleContainsScreenPoint(_quickChips[i].rt, screenPoint, _canvas.worldCamera))
                    return true;
            return false;
        }

        /// <summary>建一个快捷气泡 chip：九切片暖白底+深棕单行文字（超宽省略号截断）；
        /// raycastTarget 全 false——悬停由宿主喂数光标判定，不占 EventSystem（与聊天气泡同纪律）</summary>
        QuickChip BuildQuickChip(int index)
        {
            var go = new GameObject($"QuickChip{index}", typeof(RectTransform));
            go.transform.SetParent(_canvas.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.zero;   // 左下锚：anchoredPosition 即画布绝对坐标（与气泡/输入条同约定）
            rt.pivot = new Vector2(0.5f, 0.5f);
            var img = go.AddComponent<Image>();
            img.sprite = bubbleSprite;
            img.type = Image.Type.Sliced;
            img.color = new Color(1f, 1f, 1f, 0.96f);
            img.raycastTarget = false;
            var cg = go.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;

            var textObj = new GameObject("Text", typeof(RectTransform));
            textObj.transform.SetParent(go.transform, false);
            var textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(14f, 0f);
            textRect.offsetMax = new Vector2(-14f, 0f);
            var tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.font = _inputField.fontAsset != null ? _inputField.fontAsset : TMP_Settings.defaultFontAsset;
            tmp.fontSize = 22;
            tmp.color = new Color(0.24f, 0.18f, 0.10f);   // 深棕文字（与聊天气泡同配色）
            tmp.alignment = TextAlignmentOptions.Midline;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;
            tmp.text = _quickMessages[index];   // 原样显示（含 / 前缀——斜杠可见，一眼区分指令行与普通文本消息）
            tmp.ForceMeshUpdate();
            float w = Mathf.Clamp(tmp.preferredWidth + 44f, quickMinW, quickMaxW);
            rt.sizeDelta = new Vector2(w, quickChipH);
            return new QuickChip { rt = rt, cg = cg, img = img };
        }

        /// <summary>悬停高亮刷新（金色调+微放大；-1=全部回常态色）</summary>
        void RefreshQuickHover()
        {
            for (int i = 0; i < _quickChips.Count; i++)
            {
                bool hover = i == _quickHover;
                _quickChips[i].img.color = hover
                    ? new Color(1f, 0.92f, 0.66f, 0.98f)
                    : new Color(1f, 1f, 1f, 0.96f);
                if (_quickChips[i].arrived)
                {
                    float s = hover ? 1.07f : 1f;
                    _quickChips[i].rt.localScale = new Vector3(s, s, 1f);
                }
            }
        }

        /// <summary>飞出动画：各 chip 依次（错峰 飞出间隔秒）从中心飞向目标位——位置 EaseOutCubic、
        /// 缩放 0.5→1 带轻微过冲、alpha 0→1。到位后不再驱动（悬停高亮接管外观）。</summary>
        IEnumerator quickFlyOutRoutine()
        {
            float t0 = Time.unscaledTime;
            while (true)
            {
                bool anyFlying = false;
                for (int i = 0; i < _quickChips.Count; i++)
                {
                    var c = _quickChips[i];
                    if (c.arrived) continue;
                    float k = Mathf.Clamp01((Time.unscaledTime - t0 - i * quickStaggerSec) / Mathf.Max(0.01f, quickFlySec));
                    if (k < 1f) anyFlying = true;
                    float e = 1f - Mathf.Pow(1f - k, 3f);   // EaseOutCubic
                    c.rt.anchoredPosition = Vector2.Lerp(c.center, c.target, e);
                    // 缩放过冲：0→1.12→1（k=1 精确收在 1）
                    float pop = 1f + 0.12f * Mathf.Sin(Mathf.Clamp01(k * 1.25f) * Mathf.PI);
                    float s = Mathf.Lerp(0.5f, 1f, e) * pop;
                    c.rt.localScale = new Vector3(s, s, 1f);
                    c.cg.alpha = k;
                    if (k >= 1f)
                    {
                        c.arrived = true;
                        c.rt.anchoredPosition = c.target;
                        c.rt.localScale = Vector3.one;
                        c.cg.alpha = 1f;
                    }
                }
                if (!anyFlying) break;
                yield return null;
            }
            _quickAnim = null;
        }

        /// <summary>收回动画：全部飞回身体中心+淡出后销毁。提交发送已在 EndQuickBubbles 同步完成，
        /// 本动画纯视觉收尾——开始时 QuickBubblesActive 已置 false，宿主可正常交互。</summary>
        IEnumerator quickRetractRoutine()
        {
            float el = 0f;
            var starts = new List<Vector2>(_quickChips.Count);
            for (int i = 0; i < _quickChips.Count; i++) starts.Add(_quickChips[i].rt.anchoredPosition);
            while (el < fadeSec)
            {
                el += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(el / Mathf.Max(0.01f, fadeSec));
                for (int i = 0; i < _quickChips.Count; i++)
                {
                    var c = _quickChips[i];
                    c.rt.anchoredPosition = Vector2.Lerp(starts[i], c.center, k);
                    c.rt.localScale = Vector3.one * Mathf.Lerp(1f, 0.6f, k);
                    c.cg.alpha = 1f - k;
                }
                yield return null;
            }
            ClearQuickChips();
            _quickAnim = null;
        }

        /// <summary>清全部 chip（编辑模式兜底 DestroyImmediate——程序化 UI 编辑器自检可安全收尾）</summary>
        void ClearQuickChips()
        {
            for (int i = 0; i < _quickChips.Count; i++)
                if (_quickChips[i].rt != null)
                {
                    if (Application.isPlaying) Destroy(_quickChips[i].rt.gameObject);
                    else DestroyImmediate(_quickChips[i].rt.gameObject);
                }
            _quickChips.Clear();
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
            bubbleStarted?.Invoke(); // 新气泡开始=打断旧语音（兜底直出/错误文案路径；随后新文本按需入队，2026-09-16 拍板）
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
