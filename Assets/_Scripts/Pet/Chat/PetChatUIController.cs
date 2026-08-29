using System;
using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
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
        [FormerlySerializedAs("气泡Sprite")]
        [SerializeField] private Sprite 气泡贴图;
        [Tooltip("输入条底贴图（九切片；空=用气泡贴图）")]
        [FormerlySerializedAs("输入底Sprite")]
        [SerializeField] private Sprite 输入底贴图;
        [Tooltip("星形装饰贴图（可选，气泡左上角贴花）")]
        [FormerlySerializedAs("星形Sprite")]
        [SerializeField] private Sprite 星形贴图;
        [Tooltip("TMP 字体（空=TMP Settings 默认字体——项目已配中文回退）")]
        [SerializeField] private TMP_FontAsset 字体;

        [Header("会话")]
        [SerializeField] private PaimonChatSession 会话;

        [Header("布局")]
        [Tooltip("回复气泡最大宽度（Canvas 像素）")]
        [SerializeField] private float 气泡最大宽 = 520f;
        [Tooltip("回复气泡相对头部锚点的偏移（上移出头）")]
        [SerializeField] private Vector2 气泡偏移 = new Vector2(0f, 60f);
        [Tooltip("回复气泡在屏内自动钳制的边距")]
        [SerializeField] private float 屏内边距 = 30f;
        [Tooltip("输入条高度")]
        [SerializeField] private float 输入条高 = 56f;
        [Tooltip("输入条顶边距模型脚底的间距（输入条挂在模型下方；翻转时也用作与气泡的间距）")]
        [SerializeField] private float 输入条距模型底 = 20f;

        [Header("行为")]
        [Tooltip("回复完成后气泡停留秒（0=常驻到下一次交互）")]
        [SerializeField] private float 气泡停留秒 = 6f;
        [Tooltip("气泡淡出秒")]
        [SerializeField] private float 淡出秒 = 0.4f;
        [Tooltip("打字机：每字符间隔秒（0=直出）")]
        [SerializeField] private float 打字间隔秒 = 0.02f;
        [Tooltip("未设置 API Key 时的提示语")]
        [FormerlySerializedAs("无Key提示")]
        [SerializeField] private string 未设密钥提示 = "还没设置 API Key 哦！去 设置→派蒙→对话 API Key 里填一个吧。";

        // ---- 运行时 ----
        private Canvas _画布;
        private RectTransform _输入条根;
        private TMP_InputField _输入框;
        private RectTransform _气泡根;
        private TextMeshProUGUI _气泡文本;
        private CanvasGroup _气泡组;
        private RectTransform _星形;
        private readonly StringBuilder _打字缓存 = new StringBuilder();
        private Coroutine _打字协程;
        private Coroutine _淡出协程;
        private Coroutine _停留协程;
        private bool _输入开着;
        private bool _等待首字节; // 发送后的"…"占位：首个流式增量到达时清掉再追加（防"…回复"拼接）
        private Action<string> _界面完成处理器; // 界面侧 完成 事件处理器（持引用可摘——会话层也接此事件，勿整体赋值）

        /// <summary>头部锚点提供器（宿主注入：返回派蒙头顶的 Canvas 屏幕坐标；null=气泡藏屏顶中央）</summary>
        [HideInInspector] public System.Func<Vector2> 头部锚点提供器;

        /// <summary>底部锚点提供器（宿主注入：返回派蒙模型包围盒底中心的 Canvas 屏幕坐标——
        /// 输入条挂在模型下方；null=输入条退回屏底居中兜底）</summary>
        [HideInInspector] public System.Func<Vector2> 底部锚点提供器;

        /// <summary>输入框当前打开（宿主据此冻结拖拽误判；未接线=恒 false 不挡交互）</summary>
        public bool 输入开着 => _输入开着 && _输入条根 != null;

        /// <summary>屏幕点是否落在输入条矩形内（宿主区分"单击派蒙收起输入框"与"点输入条本身"——
        /// 输入期点派蒙=收起，点输入条（含发送按钮）=正常 UI 交互不收起）</summary>
        public bool 点在输入条上(Vector2 屏幕点)
        {
            if (_输入条根 == null || !_输入条根.gameObject.activeSelf || _画布 == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(_输入条根, 屏幕点, _画布.worldCamera);
        }

        /// <summary>会话客户端（发送前接流式事件用）——转发私有字段</summary>
        public DeepSeekClient 客户端 => 会话 != null ? 会话.客户端引用 : null;

        void Awake()
        {
            if (会话 == null) 会话 = GetComponentInParent<PaimonChatSession>(true);
            if (会话 == null) 会话 = GetComponent<PaimonChatSession>();
            // Canvas 接线延迟到 宿主接线()（宿主建好画中画 Canvas 后调用）：组件挂在 prefab 根，
            // 根下无父 Canvas——Awake 时建界面必失败（2026-08-28 单击报 NullReference 的根因）。
            // 未接线前 输入开着 恒 false 不挡交互。
        }

        /// <summary>宿主接线（宿主 Canvas 就绪后调用）：此刻才建界面。Canvas 由宿主直传（组件可挂
        /// 任意物体——如 prefab 根，Canvas 在其子树下，向上找不到）；空=向上找（组件挂 Canvas 下时用）。
        /// 2026-08-29 修单击 NRE：旧版靠宿主把本组件 SetParent 挪进 Canvas——但组件挂在 prefab 根
        /// （=宿主根）时挪根=挪进自己的子 Canvas（循环父子被拒）→向上仍无 Canvas→自禁用→NRE。
        /// 重复调用幂等。</summary>
        public void 宿主接线(Canvas 宿主画布 = null)
        {
            if (_画布 != null) return;
            _画布 = 宿主画布 != null ? 宿主画布 : GetComponentInParent<Canvas>();
            if (_画布 == null) { Debug.LogWarning("[PetChatUI] 无 Canvas，禁用"); enabled = false; return; }
            建界面();
        }

        void Update()
        {
            // ESC 收起输入条（2026-08-29：旧版输入期宿主交互全冻结+无 ESC=输入条打开后只能靠发送
            // 消失，用户被困在"打开→发送→消失"循环——多一条退出途径）
            if (_输入开着 && Input.GetKeyDown(KeyCode.Escape)) { 关闭输入(); return; }
            var 画布矩形 = (_画布.transform as RectTransform).rect;
            // 回复气泡跟随头部锚点（拖拽/移动/动画头部都在动——每帧跟；按半宽钳回屏内防贴边裁切）
            if (_气泡根 != null && _气泡根.gameObject.activeSelf && 头部锚点提供器 != null)
            {
                Vector2 p = 头部锚点提供器();
                float 气泡半宽 = _气泡根.sizeDelta.x * 0.5f;
                p.x = Mathf.Clamp(p.x + 气泡偏移.x,
                    Mathf.Min(屏内边距 + 气泡半宽, 画布矩形.width * 0.5f),
                    Mathf.Max(画布矩形.width - 屏内边距 - 气泡半宽, 画布矩形.width * 0.5f));
                p.y = Mathf.Clamp(p.y + 气泡偏移.y, 屏内边距, 画布矩形.height - 屏内边距);
                _气泡根.anchoredPosition = p;
            }
            // 输入条跟随模型（开着才需要；翻转判定依赖气泡当前高度，故在气泡跟随之后算）
            if (_输入开着 && _输入条根 != null) 跟随输入条(画布矩形);
        }

        /// <summary>输入条跟随模型（每帧，输入开着时）：默认挂模型脚底下方（底部锚点=模型包围盒底
        /// 中心）；脚底放不下（贴屏底/坐任务栏腿垂出屏）时翻转到气泡上方——输入条与气泡都不遮模型。
        /// 宿主未注入 底部锚点提供器 时退回屏底居中（理论兜底——现行宿主都会注入）。</summary>
        void 跟随输入条(Rect 画布矩形)
        {
            if (底部锚点提供器 == null)
            {
                // 顶中心 pivot：y=条底距屏底+条高，使条底贴 输入条距模型底
                _输入条根.anchoredPosition = new Vector2(画布矩形.width * 0.5f, 输入条距模型底 + 输入条高);
                return;
            }
            Vector2 模型底 = 底部锚点提供器();
            float 半宽 = _输入条根.sizeDelta.x * 0.5f;
            float x = Mathf.Clamp(模型底.x,
                Mathf.Min(屏内边距 + 半宽, 画布矩形.width * 0.5f),
                Mathf.Max(画布矩形.width - 屏内边距 - 半宽, 画布矩形.width * 0.5f));
            // 默认：输入条顶边贴模型脚底下方（pivot=顶中心）
            float y = 模型底.y - 输入条距模型底;
            if (y - 输入条高 < 屏内边距)
            {
                // 脚底放不下：翻转到气泡上方（气泡隐藏时按头锚点+偏移估气泡底）
                float 气泡顶 = _气泡根 != null && _气泡根.gameObject.activeSelf
                    ? _气泡根.anchoredPosition.y + _气泡根.sizeDelta.y
                    : (头部锚点提供器 != null ? 头部锚点提供器().y + 气泡偏移.y : 模型底.y + 240f);
                y = Mathf.Min(气泡顶 + 输入条距模型底 + 输入条高, 画布矩形.height - 屏内边距);
            }
            _输入条根.anchoredPosition = new Vector2(x, y);
        }

        // ---- 对外（宿主单击回调驱动） ----

        /// <summary>切换输入框（单击派蒙调用；流式回复进行中=收起不重开）</summary>
        public void 切换输入()
        {
            if (_输入条根 == null) return; // UI 未建（未接线/接线失败）——单击静默忽略，不再炸 NRE
            if (_输入开着) 关闭输入();
            else 打开输入();
        }

        void 打开输入()
        {
            _输入开着 = true;
            _输入条根.gameObject.SetActive(true);
            跟随输入条((_画布.transform as RectTransform).rect); // 立即摆位，防一帧闪在旧位置
            InputLocks.Push(this, InputLockReason.InputPopupEntering); // 输入期按键不漏进游戏
            _输入框.text = "";
            _输入框.ActivateInputField();
            _输入框.Select();
            停气泡淡出();
        }

        void 关闭输入()
        {
            if (!_输入开着) return;
            _输入开着 = false;
            InputLocks.Pop(this, InputLockReason.InputPopupEntering);
            _输入条根.gameObject.SetActive(false);
        }

        // ---- 界面构建（程序化） ----

        void 建界面()
        {
            var 画布矩形 = _画布.transform as RectTransform;

            // 输入条（挂模型下方，位置每帧由 跟随输入条 算——建时先藏屏外）
            var 输入条物体 = new GameObject("PetChatInputBar", typeof(RectTransform));
            输入条物体.transform.SetParent(_画布.transform, false);
            _输入条根 = 输入条物体.GetComponent<RectTransform>();
            _输入条根.anchorMin = new Vector2(0f, 0f);
            _输入条根.anchorMax = new Vector2(0f, 0f); // 左下锚：anchoredPosition 即画布绝对坐标（与气泡同约定，跟随输入条 按绝对坐标摆位）
            _输入条根.pivot = new Vector2(0.5f, 1f); // 顶中心：anchoredPosition.y=输入条顶边
            _输入条根.anchoredPosition = new Vector2(0f, -输入条高 * 2f);
            _输入条根.sizeDelta = new Vector2(Mathf.Min(气泡最大宽, 画布矩形.rect.width - 屏内边距 * 2f), 输入条高);

            var 输入底 = 输入条物体.AddComponent<Image>();
            输入底.sprite = 输入底贴图 != null ? 输入底贴图 : 气泡贴图;
            输入底.type = Image.Type.Sliced;
            输入底.color = new Color(1f, 1f, 1f, 0.96f);

            var 输入框物体 = new GameObject("Field", typeof(RectTransform));
            输入框物体.transform.SetParent(输入条物体.transform, false);
            var 输入框矩形 = 输入框物体.GetComponent<RectTransform>();
            输入框矩形.anchorMin = new Vector2(0f, 0f);
            输入框矩形.anchorMax = new Vector2(0.78f, 1f);
            输入框矩形.offsetMin = new Vector2(20f, 8f);
            输入框矩形.offsetMax = new Vector2(-8f, -8f);
            _输入框 = 输入框物体.AddComponent<TMP_InputField>();
            // TMP 3.0.9 陷阱（两连 NRE 实证）：fontAsset/pointSize 的 setter（SetGlobalFontAsset/
            // SetGlobalPointSize）**无条件**解引用 textComponent（placeholder 有判空、textComponent
            // 没有，源码 L4593/L4605）——必须先接 textComponent/placeholder 再设 fontAsset/pointSize
            //（setter 会把字体字号推给两者；建文本/建占位自带的字体字号被同值重推，无副作用）
            _输入框.textComponent = 建文本(输入框物体.transform, 字体, 30, TextAlignmentOptions.MidlineLeft);
            _输入框.placeholder = 建占位(输入框物体.transform, 字体, 30);
            _输入框.fontAsset = 字体 != null ? 字体 : TMP_Settings.defaultFontAsset;
            _输入框.pointSize = 30;
            _输入框.onEndEdit.AddListener(v => { if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) 发送(); });
            ((RectTransform)_输入框.textComponent.transform).offsetMin = new Vector2(4f, 0f);
            ((RectTransform)_输入框.textComponent.transform).offsetMax = new Vector2(-4f, 0f);

            var 发送按钮物体 = new GameObject("SendBtn", typeof(RectTransform));
            发送按钮物体.transform.SetParent(输入条物体.transform, false);
            var 发送按钮矩形 = 发送按钮物体.GetComponent<RectTransform>();
            发送按钮矩形.anchorMin = new Vector2(0.8f, 0.1f);
            发送按钮矩形.anchorMax = new Vector2(0.98f, 0.9f);
            var 发送按钮 = 发送按钮物体.AddComponent<Button>();
            var 发送按钮图 = 发送按钮物体.AddComponent<Image>();
            发送按钮图.sprite = 气泡贴图;
            发送按钮图.type = Image.Type.Sliced;
            发送按钮图.color = new Color(0.83f, 0.66f, 0.34f, 0.9f); // 暖金按钮
            发送按钮.onClick.AddListener(发送);
            var 发送文本 = 建文本(发送按钮物体.transform, _输入框.fontAsset, 26, TextAlignmentOptions.Center);
            发送文本.text = "➤";
            ((RectTransform)发送文本.transform).anchorMin = Vector2.zero;
            ((RectTransform)发送文本.transform).anchorMax = Vector2.one;

            // 回复气泡（锚点每帧跟随头部）
            var 气泡物体 = new GameObject("PetChatBubble", typeof(RectTransform));
            气泡物体.transform.SetParent(_画布.transform, false);
            _气泡根 = 气泡物体.GetComponent<RectTransform>();
            // 左下锚：anchoredPosition 即画布绝对坐标（与输入条同约定）。**程序化新建 RectTransform
            // 默认锚=画布中心**——不显式设锚，按绝对坐标写入=整体偏移(+半屏宽,+半屏高)，
            // 气泡飞到右上远处（2026-08-29 首测目检报障根因）。
            _气泡根.anchorMin = new Vector2(0f, 0f);
            _气泡根.anchorMax = new Vector2(0f, 0f);
            _气泡根.pivot = new Vector2(0.5f, 0f); // 底中心=头顶锚点
            _气泡根.sizeDelta = new Vector2(气泡最大宽, 120f);
            _气泡组 = 气泡物体.AddComponent<CanvasGroup>();
            _气泡组.blocksRaycasts = false; // 气泡不挡点击

            var 气泡底物体 = new GameObject("BG", typeof(RectTransform));
            气泡底物体.transform.SetParent(气泡物体.transform, false);
            var 气泡底矩形 = 气泡底物体.GetComponent<RectTransform>();
            气泡底矩形.anchorMin = Vector2.zero;
            气泡底矩形.anchorMax = Vector2.one;
            气泡底矩形.offsetMin = 气泡底矩形.offsetMax = Vector2.zero;
            var 气泡底图 = 气泡底物体.AddComponent<Image>();
            气泡底图.sprite = 气泡贴图;
            气泡底图.type = Image.Type.Sliced;
            气泡底图.color = new Color(1f, 1f, 1f, 0.96f);

            var 文本物体 = new GameObject("Text", typeof(RectTransform));
            文本物体.transform.SetParent(气泡物体.transform, false);
            var 文本矩形 = 文本物体.GetComponent<RectTransform>();
            文本矩形.anchorMin = new Vector2(0f, 0f);
            文本矩形.anchorMax = new Vector2(1f, 1f);
            文本矩形.offsetMin = new Vector2(24f, 14f);
            文本矩形.offsetMax = new Vector2(-24f, -14f);
            _气泡文本 = 文本物体.AddComponent<TextMeshProUGUI>();
            _气泡文本.font = _输入框.fontAsset;
            _气泡文本.fontSize = 28;
            _气泡文本.color = new Color(0.24f, 0.18f, 0.10f); // 深棕文字（暖白底）
            _气泡文本.enableWordWrapping = true;
            _气泡文本.raycastTarget = false;

            // 星形贴花（可选）
            if (星形贴图 != null)
            {
                var 星形物体 = new GameObject("Star", typeof(RectTransform));
                星形物体.transform.SetParent(气泡物体.transform, false);
                _星形 = 星形物体.GetComponent<RectTransform>();
                _星形.anchorMin = _星形.anchorMax = new Vector2(0f, 1f);
                _星形.pivot = new Vector2(0.5f, 0.5f);
                _星形.anchoredPosition = new Vector2(6f, 6f); // 左上角探出一点
                _星形.sizeDelta = new Vector2(36f, 36f);
                var 星形图 = 星形物体.AddComponent<Image>();
                星形图.sprite = 星形贴图;
                星形图.raycastTarget = false;
            }
            气泡物体.SetActive(false);
            输入条物体.SetActive(false); // 输入条初始隐藏（旧版建完不藏=接线即常驻屏底）
        }

        TextMeshProUGUI 建文本(Transform 父级, TMP_FontAsset 字体资产, float 字号, TMPro.TextAlignmentOptions 对齐)
        {
            var 文本物体 = new GameObject("TMP", typeof(RectTransform));
            文本物体.transform.SetParent(父级, false);
            var 文本矩形 = 文本物体.GetComponent<RectTransform>();
            文本矩形.anchorMin = Vector2.zero;
            文本矩形.anchorMax = Vector2.one;
            var 文本 = 文本物体.AddComponent<TextMeshProUGUI>();
            文本.font = 字体资产 != null ? 字体资产 : TMP_Settings.defaultFontAsset;
            文本.fontSize = 字号;
            文本.alignment = 对齐;
            文本.raycastTarget = false;
            return 文本;
        }

        TextMeshProUGUI 建占位(Transform 父级, TMP_FontAsset 字体资产, float 字号)
        {
            var 文本 = 建文本(父级, 字体资产, 字号, TextAlignmentOptions.MidlineLeft);
            文本.text = "问派蒙点什么…";
            文本.fontStyle = FontStyles.Italic;
            文本.color = new Color(0.5f, 0.45f, 0.4f, 0.7f);
            return 文本;
        }

        // ---- 发送与流式回复 ----

        void 发送()
        {
            string 文本 = _输入框.text.Trim();
            if (string.IsNullOrEmpty(文本)) return;
            // 流式进行中再发送=会话层静默丢弃（_请求中 直接 return）——给出可见反馈并保留输入
            if (会话 != null && 会话.请求中) { 显示气泡("等派蒙说完这句嘛！"); return; }
            // 发送后输入框保留（2026-08-29 用户实测反馈"点击发送后输入框按钮直接消失了"——聊天软件式
            // 连续对话：只清文本保持焦点，回复气泡照常显示；收起走 单击派蒙/ESC）
            _输入框.text = "";
            _输入框.ActivateInputField();
            显示气泡("…"); // 等待首字节
            _等待首字节 = true;
            if (客户端 == null) { 显示气泡("对话组件未接线"); return; }

            客户端.正文增量 = 增量 => 打字机(增量);
            客户端.错误 = 错误信息 =>
            {
                // 无 key 的错误给引导文案，其它原样
                string 文案 = 错误信息 != null && 错误信息.Contains("API Key") ? 未设密钥提示 : $"哎呀…{错误信息}";
                显示气泡(文案);
            };
            // 完成重排停留淡出：流式期间 打字机 会停掉 停留协程（防中途淡出），完成时在此重排
            // （旧版漏接=回复永驻不消失）。工具轮的空全量也重排——"…"占位不至滞留。
            if (_界面完成处理器 != null) 客户端.完成 -= _界面完成处理器;
            _界面完成处理器 = 全量 =>
            {
                if (_停留协程 != null) { StopCoroutine(_停留协程); _停留协程 = null; }
                if (气泡停留秒 > 0f) _停留协程 = StartCoroutine(停留后淡出(气泡停留秒));
            };
            客户端.完成 += _界面完成处理器;
            会话.发送(文本);
        }

        /// <summary>打字机追加（流式增量逐段追加；间隔=0 直出）</summary>
        void 打字机(string 增量)
        {
            if (string.IsNullOrEmpty(增量)) return;
            if (_淡出协程 != null) { StopCoroutine(_淡出协程); _淡出协程 = null; }
            if (_停留协程 != null) { StopCoroutine(_停留协程); _停留协程 = null; }
            if (_打字协程 != null) StopCoroutine(_打字协程);
            if (_等待首字节) { _打字缓存.Length = 0; _等待首字节 = false; } // 清"…"占位（错误文案同理——增量代表新回复）
            _气泡组.alpha = 1f;
            _气泡根.gameObject.SetActive(true);
            _打字协程 = StartCoroutine(打字协程体(增量));
        }

        IEnumerator 打字协程体(string 增量)
        {
            _打字缓存.Append(增量);
            _气泡文本.text = _打字缓存.ToString();
            自适应气泡高();
            if (打字间隔秒 > 0f)
            {
                yield return new WaitForSeconds(打字间隔秒); // 微停顿观感（增量本身已是分片，无需逐字符）
            }
            _打字协程 = null;
        }

        /// <summary>整段显示（错误/提示/非流式完成）</summary>
        void 显示气泡(string 文本)
        {
            if (_淡出协程 != null) { StopCoroutine(_淡出协程); _淡出协程 = null; }
            if (_停留协程 != null) { StopCoroutine(_停留协程); _停留协程 = null; }
            if (_打字协程 != null) { StopCoroutine(_打字协程); _打字协程 = null; }
            _打字缓存.Length = 0;
            _打字缓存.Append(文本);
            _气泡文本.text = 文本;
            _气泡组.alpha = 1f;
            _气泡根.gameObject.SetActive(true);
            自适应气泡高();
            if (气泡停留秒 > 0f)
                _停留协程 = StartCoroutine(停留后淡出(气泡停留秒));
        }

        IEnumerator 停留后淡出(float 延迟)
        {
            yield return new WaitForSeconds(延迟);
            _淡出协程 = StartCoroutine(淡出体());
            _停留协程 = null;
        }

        IEnumerator 淡出体()
        {
            float 计时 = 0f;
            while (计时 < 淡出秒)
            {
                计时 += Time.unscaledDeltaTime;
                _气泡组.alpha = 1f - 计时 / 淡出秒;
                yield return null;
            }
            _气泡根.gameObject.SetActive(false);
            _气泡组.alpha = 1f;
            _打字缓存.Length = 0;
            _淡出协程 = null;
        }

        void 停气泡淡出()
        {
            if (_淡出协程 != null) { StopCoroutine(_淡出协程); _淡出协程 = null; _气泡组.alpha = 1f; }
            if (_停留协程 != null) { StopCoroutine(_停留协程); _停留协程 = null; }
        }

        /// <summary>按文本内容自适应气泡高（九切片宽度恒定=最大宽，高度随行数）</summary>
        void 自适应气泡高()
        {
            _气泡文本.ForceMeshUpdate();
            float 文本高 = _气泡文本.renderedHeight;
            float 高 = Mathf.Max(72f, 文本高 + 34f);
            _气泡根.sizeDelta = new Vector2(气泡最大宽, 高);
        }

        void OnDestroy()
        {
            if (_输入开着) InputLocks.Pop(this, InputLockReason.InputPopupEntering);
            InputLocks.PopAll(this);
        }
    }
}
