using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Battle;
using GIC.Tool;

namespace GIC.UI
{
    /// <summary>
    /// 卡组管理面板（王者荣耀式，v4）：背包长条卡组按钮点击后弹出。
    /// · 行由 rowPrefab 运行时实例化（卡组数量动态：1~30），ScrollView 支持滚动；打开时行逐个淡入上滑（同背包卡片节奏）；
    /// · 点行切换当前卡组（面板保持打开）、点名称改名、拖把手排序——拖动中的行挂到 dragLayer（面板最顶层、不受滚动/遮罩影响），
    ///   位置全程跟随指针（屏幕位移→画布单位换算），指针接近列表边缘自动滚动；
    /// · 商业级实时让位（v4）：拖拽期间禁用行布局并快照基准槽位——目标插入位按"指针越过基准槽中心"判定（不随让位动画
    ///   振荡），其余行指数趋近"插入后自己所在槽"的布局位平滑让出/回填；松手后拖动行留在 dragLayer 平滑飞向目标槽
    ///   （不受视口裁剪、保持拖拽浮层），到位后回容器精确落槽并提交——全程零跳变；拖拽中关闭面板=取消回原位不提交；
    /// · 复制/粘贴卡组、导出密语到剪贴板分享、输入密语一键配置当前卡组（剪贴板已有密语时自动预填）、新增/删除卡组；
    /// · 预览为完整卡牌（共享 CardPool 实例化 Card 预制体，OnlyDisplay 缩放 0.6，同编辑卡组小卡）。
    /// 关闭路径：关闭按钮 / 点遮罩 / ESC（IClosable 栈，优先于背包界面自身关闭）。
    /// </summary>
    public class DeckSwitchPanel : MonoBehaviour, IClosable
    {
        [Header("组件接线")]
        [InspectorName("所属背包界面")] public BackpackScreen screen;
        [InspectorName("淡入淡出组")] public CanvasGroup canvasGroup;
        [InspectorName("遮罩按钮（点外部关闭）")] public Button backdropButton;
        [InspectorName("关闭按钮")] public Button closeButton;
        [InspectorName("行容器（ScrollView Content，行按显示顺序排列）")] public Transform rowsRoot;
        [InspectorName("滚动视图")] public ScrollRect scrollRect;
        [InspectorName("滚动视口")] public RectTransform viewportRect;
        [InspectorName("滚动条")] public Scrollbar scrollbar;
        [InspectorName("行预制体")] public DeckRowView rowPrefab;
        [InspectorName("拖拽层（拖动中的行挂载到这，永远显示在最顶层）")] public RectTransform dragLayer;
        [InspectorName("面板尺寸自适应组件（挂在面板主体上）")] public PanelFitToCanvas panelFit;
        [InspectorName("新增卡组按钮")] public Button addDeckButton;
        [InspectorName("导入密语按钮")] public Button importButton;
        [InspectorName("输入弹窗预制体")] public InputPopupDialog inputPopupPrefab;

        [Header("预览")]
        [Tooltip("行内卡牌预览的缩放（Card 预制体原生 160×240，0.6=同编辑卡组小卡 96×144）")]
        [SerializeField] private float previewCardScale = 0.6f;

        [Header("动画")]
        [InspectorName("淡入时长")]
        [SerializeField] private float fadeInDuration = 0.2f;
        [InspectorName("淡出时长")]
        [SerializeField] private float fadeOutDuration = 0.2f;
        [InspectorName("行入场间隔（逐个出现）")]
        [SerializeField] private float rowEntranceInterval = 0.028f;
        [InspectorName("行入场时长")]
        [SerializeField] private float rowEntranceDuration = 0.18f;
        [InspectorName("行入场上滑距离")]
        [SerializeField] private float rowEntranceOffsetY = 30f;

        [Header("拖拽")]
        [InspectorName("拖动中列表边缘自动滚动速度")]
        [SerializeField] private float dragAutoScrollSpeed = 900f;
        [InspectorName("自动滚动触发边距（屏幕像素）")]
        [SerializeField] private float dragAutoScrollMargin = 90f;
        [InspectorName("其他行让位动画速度（越大跟手越快）")]
        [Tooltip("指数趋近系数：每秒剩余距离衰减到 e^-速度；14≈0.05s 走一半、0.15s 内基本到位")]
        [SerializeField] private float reorderEaseSpeed = 14f;
        [InspectorName("松手落位动画时长（秒）")]
        [SerializeField] private float dropInDuration = 0.16f;

        [Autowired] private CardManager cardManager;
        [Autowired] private SaveManager saveManager;
        [Autowired] private InputManager inputManager;

        private readonly List<DeckRowView> _rows = new();
        private InputPopupDialog _inputPopupInstance;
        private Coroutine _fadeCoroutine;
        private Coroutine _entranceCoroutine;
        private bool _open;

        // 预览卡牌共享池（池根 = 面板下不激活对象，卡牌按需挂到各行）
        private CardPool _previewPool;
        private Transform _poolRoot;

        // 卡组剪贴板（会话内复制/粘贴；跨会话分享走密语导出）
        private string _clipboardName;
        private List<CardId> _clipboardCards;
        private bool _hasClipboard;

        // 拖拽排序：拖动行挂 dragLayer 跟随指针；目标位实时计算，松手才提交
        private DeckRowView _dragRow;
        private RectTransform _dragRowRt;
        private int _dragFromPos = -1;
        private int _dragTargetPos = -1;
        private Vector2 _dragStartPointer;
        private Vector2 _dragStartAnchoredPos;
        private float _suppressClickUntil; // 拖拽中/刚结束：抑制行内按钮误触

        // 实时让位（商业级重排）：拖拽期间禁用行布局组件，其他行由 Update 驱动指数趋近"拖动行插到目标位后
        // 自己所在槽"的基准布局位；目标插入位按指针越过基准槽中心判定（不用实时位置，防让位中振荡）。
        // 松手后拖动行留在 dragLayer 平滑飞向目标槽，到位才回容器落槽提交——全程零跳变
        private bool _reorderActive;
        private RectTransform _rowsRt;
        private VerticalLayoutGroup _rowsLayout;
        private ContentSizeFitter _rowsCsf;  // 拖拽期与 VLG 一并禁用：单独禁 VLG 会让 CSF 高度坍缩触发滚动跳顶
        private Vector2[] _baseAnchoredPos;  // 拖拽起始各显示位的基准布局位（rowsRoot 坐标）
        private Vector2 _rowsAnchorRef;     // 行 anchor 参考点在 rowsRoot 局部空间的位置（拖拽期恒定）
        private readonly Dictionary<DeckRowView, Vector2> _letThroughTargets = new(); // 其他行让位目标
        private DeckRowView _dropInRow;     // 松手落位飞行中的行
        private int _dropInTarget = -1;
        private Coroutine _dropInCoroutine;

        // 行入场动画快照（拖拽/关闭时立即收尾复位用）
        private readonly List<(DeckRowView row, RectTransform rt, CanvasGroup cg, Vector2 target)> _entranceStates = new();

        private Canvas _canvas;
        private Camera _uiCamera; // Canvas 为 ScreenSpaceCamera 时的相机（Overlay 用 null）

        // ── IClosable 实现（ESC 关闭本面板而非整个背包） ──
        public void Close() => ClosePanel();

        private void Awake()
        {
            Wargame.Instance?.Context?.Inject(this);

            if (screen == null) screen = GetComponentInParent<BackpackScreen>();

            _canvas = GetComponentInParent<Canvas>();
            if (_canvas != null && _canvas.renderMode == RenderMode.ScreenSpaceCamera)
                _uiCamera = _canvas.worldCamera;

            backdropButton?.onClick.AddListener(ClosePanel);
            closeButton?.onClick.AddListener(ClosePanel);
            importButton?.onClick.AddListener(OnImportClicked);
            addDeckButton?.onClick.AddListener(OnAddDeckClicked);
        }

        private void OnDestroy()
        {
            inputManager?.UnregisterClosable(this);
            // 兜底：淡入协程被销毁中断时释放本类持有的锁
            InputLocks.PopAll(this);
            _previewPool?.Clear();
            backdropButton?.onClick.RemoveAllListeners();
            closeButton?.onClick.RemoveAllListeners();
            importButton?.onClick.RemoveAllListeners();
            addDeckButton?.onClick.RemoveAllListeners();
        }

        private void Update()
        {
            // 拖拽实时让位：其他行指数趋近各自让位目标（拖动行由指针/落位协程驱动，不在此列）
            if (!_reorderActive || _letThroughTargets.Count == 0) return;
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * reorderEaseSpeed);
            foreach (var kv in _letThroughTargets)
            {
                var r = kv.Key;
                if (r == null || r == _dragRow) continue;
                ((RectTransform)r.transform).anchoredPosition =
                    Vector2.Lerp(((RectTransform)r.transform).anchoredPosition, kv.Value, k);
            }
        }

        // ==================== 开 / 关 ====================

        public void Open()
        {
            if (_open) return;
            _open = true;

            gameObject.SetActive(true);
            panelFit?.Apply(); // 分辨率可能变化：每次打开按当前画布空间收敛尺寸
            RefreshAllRows();
            PlayRowsEntrance();

            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;

            inputManager?.RegisterClosable(this);
            InputLocks.Push(this, InputLockReason.PopupEntering);

            if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = StartCoroutine(FadeCoroutine(true));
        }

        private void ClosePanel()
        {
            if (!_open) return;
            _open = false;

            SettleDragOnClose(); // 拖拽/落位飞行中关闭：收口防行残留在 dragLayer（下次打开错位）
            StopEntrance();

            inputManager?.UnregisterClosable(this);
            // 淡入被打断时补释放（Pop 幂等，正常路径重复调用无害）
            InputLocks.Pop(this, InputLockReason.PopupEntering);

            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = StartCoroutine(FadeCoroutine(false));
        }

        private IEnumerator FadeCoroutine(bool fadeIn)
        {
            float duration = fadeIn ? fadeInDuration : fadeOutDuration;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                canvasGroup.alpha = fadeIn ? elapsed / duration : 1f - (elapsed / duration);
                yield return null;
            }
            canvasGroup.alpha = fadeIn ? 1f : 0f;
            _fadeCoroutine = null;

            if (fadeIn)
                InputLocks.Pop(this, InputLockReason.PopupEntering);
            else
                gameObject.SetActive(false);
        }

        // ==================== 行入场（逐个淡入+上滑，参考背包卡片节奏） ====================

        private void PlayRowsEntrance()
        {
            if (_entranceCoroutine != null) StopCoroutine(_entranceCoroutine);

            // 快照布局位 → 全部压到底部半透明 → 依次浮回
            _entranceStates.Clear();
            foreach (var row in _rows)
            {
                var cg = row.GetComponent<CanvasGroup>();
                if (cg == null) cg = row.gameObject.AddComponent<CanvasGroup>();
                var rt = (RectTransform)row.transform;
                _entranceStates.Add((row, rt, cg, rt.anchoredPosition));
                cg.alpha = 0f;
                rt.anchoredPosition = rt.anchoredPosition + Vector2.down * rowEntranceOffsetY;
            }
            if (_entranceStates.Count == 0) return;

            _entranceCoroutine = StartCoroutine(EntranceCoroutine());
        }

        private IEnumerator EntranceCoroutine()
        {
            int n = _entranceStates.Count;
            float total = rowEntranceInterval * (n - 1) + rowEntranceDuration;
            float t = 0f;
            while (t < total)
            {
                t += Time.deltaTime;
                for (int i = 0; i < n; i++)
                {
                    var s = _entranceStates[i];
                    if (s.row == null) continue; // 入场中被销毁的行（增删重建）跳过
                    float local = Mathf.Clamp01((t - i * rowEntranceInterval) / rowEntranceDuration);
                    s.cg.alpha = local;
                    s.rt.anchoredPosition = Vector2.Lerp(s.target + Vector2.down * rowEntranceOffsetY, s.target, local);
                }
                yield return null;
            }
            FinishEntrance();
        }

        /// <summary>入场收尾：全部行精确复位（也供拖拽/关闭时立即打断用）</summary>
        private void StopEntrance()
        {
            if (_entranceCoroutine != null)
            {
                StopCoroutine(_entranceCoroutine);
                _entranceCoroutine = null;
            }
            FinishEntrance();
        }

        private void FinishEntrance()
        {
            foreach (var s in _entranceStates)
            {
                if (s.row == null) continue;
                s.cg.alpha = 1f;
                s.rt.anchoredPosition = s.target;
            }
            _entranceStates.Clear();
            _entranceCoroutine = null;
        }

        // ==================== 行构建与刷新 ====================

        /// <summary>全量刷新：卡组数量变化时重建行，随后逐行编号/名称/高亮/预览/滚动条</summary>
        public void RefreshAllRows()
        {
            if (_rows.Count != cardManager.DeckCount)
                RebuildRows();

            // 行身份按"容器下标=显示顺序"解析，拖拽换位后无需回写
            _rows.Sort((a, b) => a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));

            var order = cardManager.DeckOrder;
            int current = saveManager.CurrentSave.progress.currentDeck;
            int count = Mathf.Min(_rows.Count, order.Count);
            for (int pos = 0; pos < count; pos++)
            {
                var row = _rows[pos];
                int deckId = order[pos];
                row.SetNumber(pos + 1);
                row.SetName(cardManager.GetDeckName(deckId), DefaultDeckName(pos + 1));
                row.SetCurrent(deckId == current);
                FillRowPreview(row, deckId);
            }

            SyncScrollbarSize();
            screen?.UpdateDeckBar();
        }

        /// <summary>按当前卡组数量整表重建行（先回收各行预览卡牌再销毁）</summary>
        private void RebuildRows()
        {
            foreach (var row in _rows)
            {
                ReleaseRowPreview(row);
                row.transform.SetParent(null); // 先脱父：Destroy 延迟到帧尾，留在容器里会与新行挤一帧布局
                Destroy(row.gameObject);
            }
            _rows.Clear();

            if (rowsRoot == null || rowPrefab == null) return;

            for (int i = 0; i < cardManager.DeckCount; i++)
            {
                var row = Instantiate(rowPrefab, rowsRoot);
                row.Init(this);
                _rows.Add(row);
            }
        }

        // ==================== 卡牌预览（完整 Card，共享池） ====================

        private CardPool PreviewPool
        {
            get
            {
                if (_previewPool == null)
                {
                    var poolGo = new GameObject("PreviewPool", typeof(RectTransform));
                    poolGo.transform.SetParent(transform, false);
                    poolGo.SetActive(false); // 池根不激活：归池卡牌离屏不渲染
                    _poolRoot = poolGo.transform;
                    _previewPool = new CardPool(screen != null ? screen.cardPrefab : null, _poolRoot);
                }
                return _previewPool;
            }
        }

        /// <summary>行预览填充：完整卡牌（OnlyDisplay）+ 等距排列（视觉宽 96 + 间距 8）</summary>
        private void FillRowPreview(DeckRowView row, int deckId)
        {
            ReleaseRowPreview(row);
            if (deckId < 0 || deckId >= cardManager.decks.Length) return;

            var cards = cardManager.decks[deckId].Cards;
            for (int i = 0; i < cards.Count && i < CardManager.MaxDeckSize; i++)
            {
                var card = PreviewPool.Get(cards[i], null);
                if (card == null) continue;
                card.SetViewType(ViewType.OnlyDisplay);
                card.transform.SetParent(row.previewRoot, false);
                card.transform.localScale = Vector3.one * previewCardScale;
                var crt = (RectTransform)card.transform;
                crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0f, 0.5f);
                // 视觉宽 = 160*scale + 8 间距（RectTransform 尺寸不变、缩放只影响渲染）
                crt.anchoredPosition = new Vector2(i * (160f * previewCardScale + 8f), 0f);
            }
        }

        private void ReleaseRowPreview(DeckRowView row)
        {
            if (row?.previewRoot == null || _previewPool == null) return;
            foreach (var card in row.previewRoot.GetComponentsInChildren<Card>(true))
                _previewPool.Release(card);
        }

        private void SyncScrollbarSize()
        {
            if (scrollbar == null || viewportRect == null || rowsRoot == null) return;
            var contentRt = (RectTransform)rowsRoot;
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(contentRt); // ContentSizeFitter 尺寸本帧即得，不读旧值
            float contentH = contentRt.rect.height;
            float viewH = viewportRect.rect.height;
            scrollbar.size = contentH > 1f ? Mathf.Clamp01(viewH / contentH) : 1f;
        }

        // ==================== 名称与身份解析 ====================

        /// <summary>未命名卡组的本地化默认名"卡组N"（编号为显示顺序位）</summary>
        public static LocalizedString DefaultDeckName(int number)
        {
            var ls = new LocalizedString(TableName.UIText.ToString(), "Deck_DefaultName");
            ls.Arguments = new object[] { number };
            return ls;
        }

        /// <summary>解析显示名（toast 参数用，需同步字符串）</summary>
        private string ResolveDisplayName(int deckId, int number)
        {
            string custom = cardManager.GetDeckName(deckId);
            if (!string.IsNullOrEmpty(custom)) return custom;
            return DefaultDeckName(number).GetLocalizedString();
        }

        private int DeckIdOfRow(DeckRowView row)
        {
            var order = cardManager.DeckOrder;
            int pos = row.DisplayIndex;
            return (pos >= 0 && pos < order.Count) ? order[pos] : -1;
        }

        // ==================== 行事件（行视图/把手转发） ====================

        /// <summary>行背景点击：切换当前卡组（面板保持打开便于继续管理）</summary>
        public void OnRowSelected(DeckRowView row)
        {
            if (Time.unscaledTime < _suppressClickUntil) return;
            int deckId = DeckIdOfRow(row);
            if (deckId < 0) return;
            screen?.SwitchDeck(deckId);
            RefreshAllRows();
        }

        // ── 拖拽排序（实时让位：拖动行挂 dragLayer 置顶跟随指针，其他行按目标插入位平滑让出/回填，松手飞行落位） ──

        public void OnRowBeginDrag(DeckRowView row, Vector2 pointerPos)
        {
            StopEntrance(); // 入场动画与手动拖拽互斥：立即收尾复位

            // 上一行还在落位飞行中（连续快速操作）：瞬移完成它，再开始新一轮拖拽
            if (_dropInCoroutine != null)
            {
                StopCoroutine(_dropInCoroutine);
                _dropInCoroutine = null;
                FinishDropIn(_dropInRow, _dropInTarget);
            }

            _dragFromPos = row.DisplayIndex; // 脱离容器前捕获原始显示位
            _dragTargetPos = _dragFromPos;
            _suppressClickUntil = Time.unscaledTime + 0.25f;

            _dragRow = row;
            _dragRowRt = (RectTransform)row.transform;

            // 实时让位接管布局：先快照基准布局位再禁用行布局（快照须在行挂 dragLayer 前，拖动行也要有基准槽）
            BeginReorderLayout();

            // 挂到拖拽层（保持世界位置）：拖动行脱离滚动内容/遮罩——置顶显示与位置换算都不受滚动影响
            if (dragLayer != null)
                row.transform.SetParent(dragLayer, true);
            row.SetDragState(true);

            _dragStartPointer = pointerPos;
            _dragStartAnchoredPos = _dragRowRt.anchoredPosition;
        }

        /// <summary>拖拽中：行位置跟随指针（屏幕像素→画布单位）；目标插入位按基准槽中心判定，其他行实时让位，近边缘自动滚动</summary>
        public void OnRowDrag(DeckRowView row, Vector2 pointerPos)
        {
            if (_dragRow == null || row != _dragRow) return;

            float scaleFactor = _canvas != null ? _canvas.scaleFactor : 1f;
            if (scaleFactor <= 0f) scaleFactor = 1f;
            float dy = (pointerPos.y - _dragStartPointer.y) / scaleFactor;
            _dragRowRt.anchoredPosition = new Vector2(_dragStartAnchoredPos.x, _dragStartAnchoredPos.y + dy);

            // 目标位 = 基准槽中心在指针之上的其他行数。刻意用拖拽起始快照而非实时位置：
            // 其他行正在让位中，用实时位置会让判定随动画来回振荡；指针换算到 rowsRoot 局部则天然感知列表滚动。
            // 行序用 _rows 列表索引（拖动行挂 dragLayer 后其余行 siblingIndex 前移，DisplayIndex 与快照索引错位）
            int target = 0;
            if (_baseAnchoredPos != null &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_rowsRt, pointerPos, _uiCamera, out var pointerLocal))
            {
                foreach (var other in _rows)
                {
                    if (other == row) continue;
                    int p = _rows.IndexOf(other);
                    if (p < 0 || p >= _baseAnchoredPos.Length) continue;
                    if (_rowsAnchorRef.y + _baseAnchoredPos[p].y > pointerLocal.y) target++;
                }
            }
            if (target != _dragTargetPos)
            {
                _dragTargetPos = target;
                UpdateLetThroughTargets();
            }

            AutoScrollWhileDrag(pointerPos);
        }

        public void OnRowEndDrag(DeckRowView row)
        {
            if (_dragRow == null)
            {
                // 状态丢失的兜底：仍提交一次顺序
                CommitDragOrder(row, row.DisplayIndex);
                return;
            }

            _dragRow = null;
            _dragRowRt = null;
            _suppressClickUntil = Time.unscaledTime + 0.25f; // 飞行期行还在 dragLayer（DisplayIndex 失真），先挡行点击与按钮误触

            // 松手不直接落容器：留在 dragLayer 平滑飞向目标槽（顶层不受视口裁剪、保持拖拽浮层），到位才回容器提交
            int target = Mathf.Clamp(_dragTargetPos, 0, Mathf.Max(0, _rows.Count - 1));
            _dropInRow = row;
            _dropInTarget = target;
            if (_dropInCoroutine != null) StopCoroutine(_dropInCoroutine);
            _dropInCoroutine = StartCoroutine(DropInCoroutine(row, target));
        }

        /// <summary>实时让位布局接管：快照各显示位基准布局位与行 anchor 参考点，再禁用行布局组件（行位置改由让位动画驱动）</summary>
        private void BeginReorderLayout()
        {
            if (_reorderActive) return;

            _rowsRt = (RectTransform)rowsRoot;
            if (_rowsRt == null) return;
            if (_rowsLayout == null) _rowsLayout = _rowsRt.GetComponent<VerticalLayoutGroup>();

            _baseAnchoredPos = new Vector2[_rows.Count];
            foreach (var r in _rows)
            {
                int p = _rows.IndexOf(r); // 列表序=快照序（拖拽中恒定）；勿用 DisplayIndex——挂 dragLayer 后会前移
                if (p >= 0 && p < _baseAnchoredPos.Length)
                    _baseAnchoredPos[p] = ((RectTransform)r.transform).anchoredPosition;
            }

            // 行 anchor 参考点在 rowsRoot 局部空间的位置。必须用拖动行的实际 anchor 计算：
            // 实测运行时行 anchor 与 rowPrefab 资产可能不一致（场景实例字段覆写等），按资产算参考点会整体错位
            // （本批实测错位 (半宽,-半高) → 目标判定恒 0 无让位 + 落位飞向右下角）
            if (_dragRow != null)
                _rowsAnchorRef = CalcAnchorRefLocal((RectTransform)_dragRow.transform);

            _letThroughTargets.Clear();
            foreach (var r in _rows)
                if (r != _dragRow)
                    _letThroughTargets[r] = ((RectTransform)r.transform).anchoredPosition;

            if (_rowsLayout != null) _rowsLayout.enabled = false;
            // CSF 必须一并禁用：VLG 禁用后 CSF 的 PreferredSize 高度来源消失，content 高度会坍缩为 0——
            // ScrollRect 检测 content 尺寸骤变把滚动位置钳回顶=「拖底行瞬间跳顶」根因（2026-10-07 实测报障）
            if (_rowsCsf == null) _rowsCsf = _rowsRt.GetComponent<ContentSizeFitter>();
            if (_rowsCsf != null) _rowsCsf.enabled = false;
            _reorderActive = true;
        }

        /// <summary>行 anchor 参考点在 rowsRoot 局部空间的位置（局部空间以 pivot 为原点，与 anchoredPosition 同基准）</summary>
        private Vector2 CalcAnchorRefLocal(RectTransform rowRt)
        {
            Rect parent = _rowsRt.rect;
            Vector2 aMin = new Vector2(
                Mathf.Lerp(parent.xMin, parent.xMax, rowRt.anchorMin.x),
                Mathf.Lerp(parent.yMin, parent.yMax, rowRt.anchorMin.y));
            Vector2 aMax = new Vector2(
                Mathf.Lerp(parent.xMin, parent.xMax, rowRt.anchorMax.x),
                Mathf.Lerp(parent.yMin, parent.yMax, rowRt.anchorMax.y));
            return (aMin + aMax) * 0.5f;
        }

        /// <summary>按当前目标插入位刷新其他行的让位目标：每行移动到"拖动行插到目标位后"自己所在槽的基准布局位</summary>
        private void UpdateLetThroughTargets()
        {
            if (!_reorderActive || _dragRow == null || _baseAnchoredPos == null) return;
            int from = _dragFromPos, to = _dragTargetPos;
            foreach (var other in _rows)
            {
                if (other == _dragRow) continue;
                int p = _rows.IndexOf(other); // 列表序=快照序，与判定同源（DisplayIndex 挂 dragLayer 后错位）
                if (p < 0 || p >= _baseAnchoredPos.Length) continue;
                int after = p - (p > from ? 1 : 0);     // 先移除拖动行后的序
                int slot = after + (after >= to ? 1 : 0); // 再在目标位插回后的序 = 该行应让到的槽
                if (slot >= 0 && slot < _baseAnchoredPos.Length)
                    _letThroughTargets[other] = _baseAnchoredPos[slot];
            }
        }

        /// <summary>松手落位飞行：从当前位置 EaseOutCubic 飞向目标槽世界位（快出慢收），到位回调 FinishDropIn</summary>
        private IEnumerator DropInCoroutine(DeckRowView row, int target)
        {
            var rt = (RectTransform)row.transform;
            Vector3 from = rt.position;
            Vector3 to = _rowsRt != null && _baseAnchoredPos != null && target < _baseAnchoredPos.Length
                ? _rowsRt.TransformPoint((Vector3)(_rowsAnchorRef + _baseAnchoredPos[target]))
                : from;
            float t = 0f;
            while (t < dropInDuration)
            {
                t += Time.unscaledDeltaTime;
                float x = Mathf.Clamp01(t / dropInDuration);
                float ease = 1f - (1f - x) * (1f - x) * (1f - x); // EaseOutCubic
                rt.position = Vector3.LerpUnclamped(from, to, ease);
                yield return null;
            }
            _dropInCoroutine = null;
            FinishDropIn(row, target);
        }

        /// <summary>落位收尾：行回容器精确落槽（与飞行终点零跳变）→ 恢复行布局 → 提交顺序并刷新</summary>
        private void FinishDropIn(DeckRowView row, int target)
        {
            if (row == null) return;
            target = Mathf.Clamp(target, 0, Mathf.Max(0, _rows.Count - 1));

            row.transform.SetParent(rowsRoot, false);
            if (_baseAnchoredPos != null && target < _baseAnchoredPos.Length)
                ((RectTransform)row.transform).anchoredPosition = _baseAnchoredPos[target];
            row.transform.SetSiblingIndex(target);
            row.SetDragState(false);

            _dropInRow = null;
            _dropInTarget = -1;

            RestoreReorderLayout();
            CommitDragOrder(row, target);
        }

        /// <summary>恢复行布局组件并清让位状态（恢复后布局重排结果与让位终点一致，零跳变）</summary>
        private void RestoreReorderLayout()
        {
            if (_rowsLayout != null) _rowsLayout.enabled = true;
            if (_rowsCsf != null) _rowsCsf.enabled = true;
            _reorderActive = false;
            _letThroughTargets.Clear();
            _baseAnchoredPos = null;
        }

        /// <summary>面板关闭时收口进行中的拖拽：飞行中的行瞬移完成提交；拖拽中的行取消回原位（ESC=取消，不提交换位）</summary>
        private void SettleDragOnClose()
        {
            if (_dropInCoroutine != null)
            {
                StopCoroutine(_dropInCoroutine);
                _dropInCoroutine = null;
                FinishDropIn(_dropInRow, _dropInTarget); // 用户已松手=意图已定：瞬移落位并提交
            }
            else if (_dragRow != null)
            {
                var row = _dragRow;
                _dragRow = null;
                _dragRowRt = null;
                int from = Mathf.Clamp(_dragFromPos, 0, Mathf.Max(0, _rows.Count - 1));
                row.transform.SetParent(rowsRoot, false);
                if (_baseAnchoredPos != null && from < _baseAnchoredPos.Length)
                    ((RectTransform)row.transform).anchoredPosition = _baseAnchoredPos[from];
                row.transform.SetSiblingIndex(from);
                row.SetDragState(false);
                RestoreReorderLayout(); // 不提交 MoveDeckOrder：行序原样
                _dragFromPos = -1;
                _dragTargetPos = -1;
            }
        }

        private void CommitDragOrder(DeckRowView row, int toPos)
        {
            if (_dragFromPos >= 0 && _dragFromPos != toPos)
                cardManager.MoveDeckOrder(_dragFromPos, toPos);
            _dragFromPos = -1;
            _dragTargetPos = -1;
            _suppressClickUntil = Time.unscaledTime + 0.25f;
            RefreshAllRows(); // 重新编号 + 刷新行内容
        }

        /// <summary>拖拽中指针接近视口上下缘时自动滚动列表（把远处槽位拖进来）</summary>
        private void AutoScrollWhileDrag(Vector2 pointerPos)
        {
            if (scrollRect == null || viewportRect == null) return;
            Vector3[] corners = new Vector3[4];
            viewportRect.GetWorldCorners(corners);
            float top = float.MinValue, bottom = float.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                Vector2 s = RectTransformUtility.WorldToScreenPoint(_uiCamera, corners[i]);
                if (s.y > top) top = s.y;
                if (s.y < bottom) bottom = s.y;
            }
            if (pointerPos.y > top - dragAutoScrollMargin)
                scrollRect.velocity = new Vector2(0f, -dragAutoScrollSpeed); // 视口上缘 → 内容上移（看更靠后的行）
            else if (pointerPos.y < bottom + dragAutoScrollMargin)
                scrollRect.velocity = new Vector2(0f, dragAutoScrollSpeed);
        }

        // ==================== 行内按钮操作（行视图转发入口，须 public） ====================

        public void OnRenameClicked(DeckRowView row)
        {
            if (Time.unscaledTime < _suppressClickUntil) return;
            int deckId = DeckIdOfRow(row);
            if (deckId < 0) return;
            // 预填当前显示名（自定义名，或默认"卡组N"）——在现有名字基础上编辑；输入限 10 字
            string currentName = ResolveDisplayName(deckId, row.DisplayIndex + 1);
            ShowInputDialog("Deck_RenameTitle", currentName, newName =>
            {
                cardManager.SetDeckName(deckId, newName);
                RefreshAllRows();
            }, maxChars: CardManager.MaxDeckNameLength);
        }

        public void OnCopyClicked(DeckRowView row)
        {
            if (Time.unscaledTime < _suppressClickUntil) return;
            int deckId = DeckIdOfRow(row);
            if (deckId < 0) return;
            (_clipboardName, _clipboardCards) = cardManager.CopyDeck(deckId);
            _hasClipboard = _clipboardCards != null;
            Toast("Deck_Copied");
        }

        public void OnPasteClicked(DeckRowView row)
        {
            if (Time.unscaledTime < _suppressClickUntil) return;
            if (!_hasClipboard)
            {
                Toast("Deck_EmptyClipboard");
                return;
            }
            int deckId = DeckIdOfRow(row);
            if (deckId < 0) return;
            int missing = cardManager.ApplyDeckContent(deckId, _clipboardName, _clipboardCards);
            if (missing > 0) Toast("Deck_MissingCards", missing);
            else Toast("Deck_Pasted");
            screen?.OnDeckContentChanged(deckId);
            RefreshAllRows();
        }

        public void OnExportClicked(DeckRowView row)
        {
            if (Time.unscaledTime < _suppressClickUntil) return;
            int deckId = DeckIdOfRow(row);
            if (deckId < 0) return;
            GUIUtility.systemCopyBuffer = cardManager.ExportDeckCode(deckId);
            Toast("Deck_ExportCopied");
        }

        public void OnDeleteClicked(DeckRowView row)
        {
            if (Time.unscaledTime < _suppressClickUntil) return;
            if (cardManager.DeckCount <= CardManager.MinDeckCount)
            {
                Toast("Deck_MinReached");
                return;
            }
            int deckId = DeckIdOfRow(row);
            if (deckId < 0) return;

            string displayName = ResolveDisplayName(deckId, row.DisplayIndex + 1);
            if (!cardManager.RemoveDeck(deckId)) return;
            Toast("Deck_Deleted", displayName);

            // 删除可能改写 currentDeck（删中当前卡组时切到后继）——同步界面
            int newCurrent = saveManager.CurrentSave.progress.currentDeck;
            if (screen != null && newCurrent != screen.CurrentDeckId)
                screen.SwitchDeck(newCurrent);
            else if (screen != null)
                screen.OnDeckContentChanged(newCurrent); // 编号全部变化，长条按钮需刷新
            RefreshAllRows();
        }

        /// <summary>新增卡组（尾部追加；达上限 toast）</summary>
        private void OnAddDeckClicked()
        {
            if (cardManager.DeckCount >= CardManager.MaxDeckCount)
            {
                Toast("Deck_MaxReached", CardManager.MaxDeckCount);
                return;
            }
            cardManager.AddDeck();
            RefreshAllRows();
        }

        /// <summary>导入密语：解码后一键配置到当前卡组（名称+内容整卡组覆盖）；剪贴板已有密语时自动预填</summary>
        private void OnImportClicked()
        {
            string prefill = "";
            string clip = GUIUtility.systemCopyBuffer;
            if (!string.IsNullOrEmpty(clip) && clip.Trim().StartsWith(DeckCodeCodec.Prefix, StringComparison.Ordinal))
                prefill = clip.Trim();

            ShowInputDialog("Deck_ImportTitle", prefill, code =>
            {
                if (!DeckCodeCodec.TryDecode(code.Trim(), out var name, out var cards))
                {
                    Toast("Deck_ImportFailed");
                    return;
                }
                int deckId = saveManager.CurrentSave.progress.currentDeck;
                int missing = cardManager.ApplyDeckContent(deckId, name, cards);
                if (missing > 0) Toast("Deck_MissingCards", missing);
                else Toast("Deck_ImportSuccess");
                screen?.OnDeckContentChanged(deckId);
                RefreshAllRows();
            }, maxChars: 256);
        }

        // ==================== 工具 ====================

        /// <summary>
        /// 输入弹窗必须实例化在 screen 根（非 UI 物体）下：InputPopupDialog 预制体根是自带 Canvas 的
        /// scale=0 占位节点——只有挂到无 Canvas 的父级（如 SettingsScreen 同款用法），它的 Canvas 才会
        /// 成为独立顶层画布并被 Canvas 系统接管缩放；挂在面板（主 Canvas 层级内）下会保持 scale=0 永不可见。
        /// </summary>
        private void ShowInputDialog(string titleKey, string current, Action<string> onConfirm, int maxChars = 0)
        {
            if (inputPopupPrefab == null) return;
            if (_inputPopupInstance == null)
                _inputPopupInstance = Instantiate(inputPopupPrefab, screen != null ? screen.transform : transform);
            _inputPopupInstance.transform.SetAsLastSibling();
            _inputPopupInstance.Show(titleKey, current, onConfirm, maxChars);
        }

        private static void Toast(string popupKey, params object[] args)
        {
            var ls = new LocalizedString(TableName.PopupText.ToString(), popupKey);
            if (args != null && args.Length > 0)
                ls.Arguments = args;
            PopupManager.Instance?.ShowToast(ls);
        }
    }
}
