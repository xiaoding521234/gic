using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Localization;
using GIC.Framework;
using GIC.Data;
using GIC.Tool;
using GIC.UI;

namespace GIC.Battle
{
    /// <summary>
    /// 正式战斗 HUD（B6 提前启动；docs/18 决策六 + docs/active/22 §13 + docs/designs/battle-hud-v1.html）。
    /// 2026-09-22 全项目统一批次：结构装配=Resources/Prefabs/Battle/BattleHud.prefab（编辑器维护，
    /// 一次性迁移工具 BattleHudPrefabMigration 从旧程序化构建烘焙）；运行时按契约名寻址+接线+Palette 活色。
    /// partial 分件：本文件=字段/数据回调/状态机/瞄准/技能按钮交互/数据链/高亮；
    /// BattleHud.TopBar.cs=顶栏寻址+刷新；BattleHud.Build.cs=寻址接线总装；BattleHud.Layout.cs=布局系统。
    /// 技能盘四键表驱动（SkillButtonDef，2026-09-18 A 案不变）：加技能键=prefab 加槽节点+Build 分件表加一行。
    /// 布局 = MOBA 范式（移动左下、爆发右下盘心、战技/延奏围绕——默认位即 prefab 摆放，编辑器所见即所得）；
    /// 2026-09-21 自定义布局系统：HUD 控件全量可拖可缩（LayoutSlot 归一锚点），方案存主存档 settings 分区。
    /// 单位选择交互（2026-09-18 拍板）：点立牌选中 → 技能盘现+手牌藏（选中态/手牌态互斥），
    /// 点空白取消选中；技能瞄准 = 可选格高亮 + 右上取消按钮 + 选中单位脚下金色标记。
    /// 交互状态机：Idle（手牌态）→ UnitSelected（行动态）→ Aiming（瞄准态）+ LayoutEditing（编辑态门控）。
    /// 输入 = BattleCameraController.OnBoardTap（Drag 短点击复合发射，docs/24 §7.10 tap+pan 同体）。
    /// 文案 = TextCombiner 本地化（docs/20 §2；UIText 12000 战斗段）；素材全部复用项目内资产。
    /// 拖动式瞄准/手牌卡列表/协议核心血条 = B4/B8 接线。
    /// </summary>
    public partial class BattleHud : MonoBehaviour
    {
        // ==================== 可调参数（编辑器直改；位置/尺寸类随 2026-09-22 prefab 化退役进 prefab） ====================

        // 配色统一走 BattlePalette 配置资产（2026-09-18 统一化批次；接线时活色覆盖烘焙兜底色）
        private static BattlePalette Palette => BattlePalette.Instance;

        [Header("文字（运行时动态件用）")]
        [SerializeField] private float 顶栏字号 = 34f;

        [Header("攻速队列条（运行时重建）")]
        [SerializeField] private float 队列槽边长 = 64f;
        [SerializeField] private float 队列槽间距 = 14f;
        [SerializeField] private int 队列槽位数 = 6;

        // 瞄准常量（运行时计算用）
        private const int 移动最大步数 = 3;
        private const int 方向瞄准显示距离 = 8; // 十字瞄准高亮格数（Host 投射物实际扫描 24 格）

        // ==================== 运行引用 ====================

        private BattleSession _session;
        private BattleBoard _board;
        private BattleCameraController _camera;
        private Action _onCloseBattle;
        private string _myPlayerId;

        private Canvas _canvas;

        // 提示条
        private TMP_Text _tipText;
        private TextCombiner _tipCombiner;

        // 技能详情（现有体系复用：Resources/Prefabs/UI/Skill/SkillDetailPanel.prefab）
        private SkillDetailView _skillDetailView;

        // 行动区（技能盘三键/移动/手牌/取消均为布局件——显隐走 ApplyStateVisibility，_skillZone 容器 2026-09-21 退役）
        private RectTransform _handZone;
        private RectTransform _cancelButton;
        private TextCombiner _handTextCombiner;

        // 技能盘按钮（表驱动四键；现成 Skill.prefab/SkillIconView 视觉填充走 InitWithData 现有链。
        // 移动=特殊技能同款建法+同款交互（2026-09-18 拍板"技能按钮统一，移动是特殊的技能"）：
        // 专位左下，点击式三情况与其余三键全同）
        private readonly List<SkillButtonDef> _skillButtons = new List<SkillButtonDef>();
        private SkillButtonDef _moveDef; // 移动键（SelectUnit/DeselectUnit 的专位显示控制）

        /// <summary>技能盘按钮定义（表驱动，2026-09-18 统一化 A 案）：key=日志标识，type=数据分拣
        /// （UnitConfig.skills 按 SkillType）+ 瞄准语义（IsMove=移动瞄准，其余按 SkillData 分档）</summary>
        private class SkillButtonDef
        {
            public string key;
            public SkillType type;
            public RectTransform rect;
            public SkillIconView view;
            public TextCombiner nameText;
            public bool IsMove => type == SkillType.Move;
        }

        // 高亮（世界层）+ 选中标记
        private Transform _highlightRoot;
        private GameObject _selectMarker;
        private readonly List<GameObject> _highlightQuads = new List<GameObject>();
        // 世界层运行时材质（单实例缓存，OnDestroy 释放——Destroy 物体不销材质，逐次 new 会累积泄漏）
        private Material _aimHighlightMaterial;
        private Material _selectMarkerMaterial;

        // 状态机（AimMode 枚举已并表——瞄准语义由 _aimDef.type 承载，2026-09-18 A 案）
        private enum HudState { Idle, UnitSelected, Aiming }
        private HudState _state = HudState.Idle;

        // ==================== 手牌（B6c：初始=玩家当前卡组投影；点卡→部署瞄准→点格出战） ====================

        /// <summary>部署瞄准中的角色（UnitName 枚举值；0=非部署瞄准态）</summary>
        private int _deployAimUnit;

        /// <summary>手牌卡按钮容器（hand 布局件内；RebuildHandCards 重建。
        /// 2026-09-23 审查 R1：滚动壳四层结构上移 prefab hand 槽，代码只寻址 _handScroll/_handContent+建卡条目）</summary>
        private readonly List<UnityEngine.UI.Button> _handCardButtons = new List<UnityEngine.UI.Button>();

        /// <summary>手牌滚动（B6c-2：卡多时横向滑动，同背包滚动视图结构——ScrollRect+Viewport 裁剪+Content）</summary>
        private UnityEngine.UI.ScrollRect _handScroll;
        private RectTransform _handContent;

        /// <summary>手牌卡 prefab（项目唯一卡牌形态 Card.prefab=Resources/Prefabs/Backpack/；懒加载）</summary>
        private GameObject _handCardPrefab;

        /// <summary>手牌签名（卡列表恒定时跳过重建——手牌不消耗，回合间签名不变，免每回合 Instantiate GC）</summary>
        private string _handSignature;

        private string _selectedUnitId;
        private SkillButtonDef _popupDef;  // 详情面板当前展示的键
        private SkillButtonDef _aimDef;    // 瞄准中的键
        private readonly HashSet<BattleCell> _aimCells = new HashSet<BattleCell>();

        /// <summary>详情面板当前是否开着（以现有面板 activeSelf 为准——关闭只走 BattleHud 显式路径，
        /// 自带点外关闭已在 BuildSkillPopup 关闭，docs/14 §64b）</summary>
        private bool PopupOpen => _skillDetailView != null && _skillDetailView.skillDetailPanel != null
            && _skillDetailView.skillDetailPanel.activeSelf;

        // ==================== 绑定 ====================

        public void Bind(BattleSession session, BattleBoard board, BattleCameraController camera, Action onCloseBattle)
        {
            _session = session;
            _board = board;
            _camera = camera;
            _onCloseBattle = onCloseBattle;
            _myPlayerId = BattleDebugPlayerIds.P1; // 本地双开：真人 P1（B7 联机换本机玩家 id）

            ResolveHudReferences(); // prefab 寻址接线（结构=BattleHud.prefab，2026-09-22 prefab 化）

            // 布局系统（2026-09-21）：运行时 AddComponent 不走场景注入扫描——手动注入取 SaveManager；
            // 建盘完成后按存档激活方案应用布局（-1/空槽=默认）
            Wargame.Instance.Context.Inject(this);
            ApplySavedLayoutOnStart();

            _session.Player.SnapshotUpdated += OnSnapshotUpdated;
            _session.Player.OnSegmentPlaying += OnSegmentPlayingHandler;
            _session.Flow.OnPhaseChanged += OnPhaseChanged;
            if (_camera != null)
                _camera.OnBoardTap += OnBoardTap;

            RefreshFromSnapshot(_session.Player.LatestSnapshot);
        }

        private void OnDestroy()
        {
            if (_session != null)
            {
                if (_session.Player != null)
                {
                    _session.Player.SnapshotUpdated -= OnSnapshotUpdated;
                    _session.Player.OnSegmentPlaying -= OnSegmentPlayingHandler;
                }
                if (_session.Flow != null) _session.Flow.OnPhaseChanged -= OnPhaseChanged;
            }
            if (_camera != null)
                _camera.OnBoardTap -= OnBoardTap;

            // 世界层运行时材质释放（Destroy 物体不销材质，不释放则跨战斗累积）
            if (_aimHighlightMaterial != null) Destroy(_aimHighlightMaterial);
            if (_selectMarkerMaterial != null) Destroy(_selectMarkerMaterial);
        }

        // ==================== 数据回调 ====================

        private void OnSnapshotUpdated(BattleSnapshot snapshot) => RefreshFromSnapshot(snapshot);

        private void OnPhaseChanged(BattlePhase phase, int turn)
        {
            if (phase != BattlePhase.Selecting)
            {
                // 执行阶段：清瞄准/清选中回手牌态（技能盘收起=决策六执行阶段变化首版）
                ExitAiming();
                DeselectUnit();
                if (!_layoutEditing) SetTip("Battle_TipResolving"); // 编辑期提示条保持编辑提示不抢写
            }
            else if (!_layoutEditing)
            {
                SetTip("Battle_TipSelect");
            }
            RefreshFromSnapshot(_session.Player.LatestSnapshot);
        }

        /// <summary>执行阶段：高亮当前攻速片的行动者（其余降透明）</summary>
        private void OnSegmentPlayingHandler(int sliceAttackSpeed)
        {
            foreach (var slot in _queueSlots)
            {
                bool active = slot.attackSpeed == sliceAttackSpeed;
                var baseColor = slot.baseColor;
                slot.frame.color = active
                    ? Palette.高亮金
                    : new Color(baseColor.r, baseColor.g, baseColor.b, 0.30f);
            }
        }

        private void Update()
        {
            // 选择倒计时（docs/04 §4.2；每秒级刷新，静态数字条目）
            if (_session == null || _session.Flow == null || _countdownText == null) return;
            var flow = _session.Flow;
            bool selecting = flow.Phase == BattlePhase.Selecting;
            bool show = selecting && flow.SelectRemainingSeconds >= 0f;
            SetLayoutWidgetActive("countdown", show); // 编辑态强制可见由统一口处理（数字冻结展示）
            if (!show && !_layoutEditing) return;

            string seconds = Mathf.CeilToInt(Mathf.Max(0f, flow.SelectRemainingSeconds)).ToString();
            if (_countdownCombiner.GetCombinedText() != seconds)
                _countdownCombiner.SetSingleEntry(seconds);
        }

        private void RefreshFromSnapshot(BattleSnapshot snapshot)
        {
            if (snapshot == null || _turnPhaseText == null) return;

            // 回合中枢：回合 label + 数字 + 阶段徽章（动态数字 = 静态条目拼接，语言切换随下次刷新）
            _turnPhaseCombiner.ClearAllEntries();
            _turnPhaseCombiner.AddEntry(new LocalizedString("UIText", "Battle_Turn"));
            _turnPhaseCombiner.AddStaticEntry(" " + snapshot.turnNumber + " · ");
            string phaseKey = PhaseKey(_session.Flow.Phase);
            if (phaseKey != null)
                _turnPhaseCombiner.AddEntry(new LocalizedString("UIText", phaseKey));

            // 战斗时钟（独立时钟，TurnFlowController）
            int minutes = _session.Flow.BattleTimeMinutes;
            _clockCombiner.SetSingleEntry($"{minutes / 60:D2}:{minutes % 60:D2}");

            // 双方资源 chips
            UpdatePlayerBlock(_myBlock, snapshot, _myPlayerId);
            var enemyId = snapshot.resources.Where(r => r.playerId != _myPlayerId)
                .Select(r => r.playerId).FirstOrDefault();
            UpdatePlayerBlock(_enemyBlock, snapshot, enemyId);

            // 攻速队列条
            RebuildQueue(snapshot);
            UpdateQueueLabel();

            // 手牌（数量；卡列表 B8 接入）
            // 手牌（B6c：卡列表=当前卡组完整投影，含物品卡；数量文本与卡列表并存——文本做标签）
            var myRes = snapshot.resources.FirstOrDefault(r => r.playerId == _myPlayerId);
            _handTextCombiner.ClearAllEntries();
            _handTextCombiner.AddEntry(new LocalizedString("UIText", "Battle_Hand"));
            if (myRes != null)
                _handTextCombiner.AddStaticEntry(" ×" + myRes.handCardCount);
            RebuildHandCards(myRes);

            // 选中单位若已死亡（对局中不可能复苏），清选中
            if (!string.IsNullOrEmpty(_selectedUnitId))
            {
                var sel = snapshot.units.FirstOrDefault(u => u.unitId == _selectedUnitId);
                if (sel == null || sel.isCorpse != 0)
                {
                    ExitAiming();
                    DeselectUnit();
                }
                else
                {
                    // 存续：重刷技能盘置灰态（B6a 爆发/延奏键的元能门槛随快照刷新——上回合攒满本回合即亮起）
                    RefreshSkillButtons();
                }
            }
        }

        private static string PhaseKey(BattlePhase phase)
        {
            switch (phase)
            {
                case BattlePhase.Selecting: return "Battle_SelectingPhase";
                case BattlePhase.Resolving: return "Battle_ResolvingPhase";
                case BattlePhase.Finished: return "Battle_Finished";
                default: return null; // Idle：HUD 未开战不显示
            }
        }

        // ==================== 手牌与部署瞄准（B6c） ====================

        /// <summary>重建手牌卡（B6c：复用项目唯一卡牌形态 Card.prefab——策略链渲染卡面/名/星；
        /// 外层 wrapper 承点击（卡内 raycast 全关防拦截），费用角标为手牌语义叠加层。
        /// 每选择阶段头随快照重建（Card 淡入被 OnlyDisplay 跳过，无闪烁）。
        /// 2026-09-23 审查 R1：滚动壳（HandCards→HandScroll→HandViewport→HandContent 四层）上移
        /// BattleHud.prefab 的 hand 槽内——本方法只建卡条目；壳寻址=ResolveMiscWidgets，缺失已 Warn</summary>
        private void RebuildHandCards(PlayerResourceState myRes)
        {
            if (_handContent == null) return; // 壳未寻到（prefab 缺 HandCards），手牌不显示

            // 卡列表签名比对：手牌不消耗（回合间恒定），签名未变只重置滚动位置不重建（免每回合 Instantiate/Destroy GC 尖峰）
            var signature = myRes == null ? "" : string.Join(",", myRes.handCards);
            if (signature == _handSignature) return;
            _handSignature = signature;

            foreach (var btn in _handCardButtons)
                if (btn != null) Destroy(btn.gameObject);
            _handCardButtons.Clear();
            if (_handScroll != null) _handScroll.normalizedPosition = Vector2.zero;
            if (myRes == null || myRes.handCards.Count == 0) return;

            if (_handCardPrefab == null)
                _handCardPrefab = Resources.Load<GameObject>("Prefabs/Backpack/Card");
            if (_handCardPrefab == null)
            {
                GICLog.Warn("[BattleHud] Card.prefab 未找到（Resources/Prefabs/Backpack/Card），手牌不显示");
                return;
            }

            // 单位配置=[Autowired] 注入（Y10），不再 Resources.Load
            // 手牌规格=Card.prefab 原生 160×240（保持收藏卡原比例，与背包同款）
            float cardWidth = 160f, gap = 18f;
            int count = myRes.handCards.Count;
            float rowWidth = count * cardWidth + (count - 1) * gap;
            // content 宽恒=行宽+左右边距 60（**勿夹到视口宽**——窄于视口才有 Elastic 拖程，
            // "1 张卡也能滑动"；宽于视口=正常滚动），卡排相对 content 中心对称排
            _handContent.sizeDelta = new Vector2(rowWidth + 120f, 260f);
            for (int i = 0; i < count; i++)
            {
                var cardId = myRes.handCards[i];
                bool isUnit = cardId.cardType == CardType.Unit;
                // 配置校验：角色查 UnitConfig、物品查 ItemConfig——卡组条目无配置跳过并告警；
                // 物品备战数=min(存档持有, maxPrepareCount 备战上限)——带入战斗的量
                // （2026-09-22 拍板：如背包含 100 体力、备战上限 60 → 手牌显示 60；maxPrepareCount=0 的货币卡不可入组）
                int prepareCount = 0;
                if (isUnit)
                {
                    if (_unitConfig?.GetUnitData(cardId.AsUnitName()) == null)
                    {
                        GICLog.Warn($"[BattleHud] 手牌卡 {cardId} 无 UnitConfig 配置，跳过");
                        continue;
                    }
                }
                else
                {
                    var itemData = CardConfigResolver.Instance?.ItemConfig?.GetItemData(cardId.AsItemName());
                    if (itemData == null)
                    {
                        GICLog.Warn($"[BattleHud] 手牌卡 {cardId} 无 ItemConfig 配置，跳过");
                        continue;
                    }
                    prepareCount = Mathf.Min(
                        _saveManager?.CurrentSave?.GetItemCount(cardId.AsItemName()) ?? 0,
                        itemData.maxPrepareCount);
                }

                // 外层 wrapper=点击接收层（Button）；pivot=顶边中点，卡排相对 content 中心对称
                var wrapperGo = new GameObject($"Hand_{cardId}");
                wrapperGo.transform.SetParent(_handContent, false);
                var wrapperRt = wrapperGo.AddComponent<RectTransform>();
                wrapperRt.pivot = new Vector2(0.5f, 1f);
                wrapperRt.anchorMin = wrapperRt.anchorMax = new Vector2(0.5f, 1f);
                wrapperRt.anchoredPosition = new Vector2((i - (count - 1) * 0.5f) * (cardWidth + gap), -10f);
                wrapperRt.sizeDelta = new Vector2(cardWidth, 240f);
                // 命中层（透明 Image）：wrapper 需 raycast 目标才可点击/拖动（卡内 raycast 已全关防拦截）
                var hit = wrapperGo.AddComponent<UnityEngine.UI.Image>();
                hit.color = Color.clear;
                hit.raycastTarget = true;

                // 卡牌本体（唯一形态复用；保持 prefab 原生 160×240 居中——勿 stretch 压扁，2026-09-22 目检实证）
                var cardGo = Instantiate(_handCardPrefab, wrapperGo.transform, false);
                var cardRt = cardGo.GetComponent<RectTransform>();
                cardRt.anchorMin = cardRt.anchorMax = new Vector2(0.5f, 0.5f);
                cardRt.anchoredPosition = Vector2.zero;
                cardRt.sizeDelta = new Vector2(160f, 240f);

                // 卡内全部 Graphic 关 raycast——防拦截 wrapper 点击；OnlyDisplay 关 toggle 与淡入
                foreach (var graphic in cardGo.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                    graphic.raycastTarget = false;
                var card = cardGo.GetComponent<Card>();
                if (card != null)
                {
                    var saveData = new GIC.Framework.SaveCardData();
                    if (isUnit) saveData.SaveUnit(cardId.AsUnitName(), 1);
                    else saveData.SaveItem(cardId.AsItemName(), prepareCount);
                    card.SetViewType(ViewType.OnlyDisplay);
                    card.Init(saveData, null); // 手牌不挂详情面板（点卡=角色进部署瞄准/物品提示）
                }

                // 部署费角标（手牌语义叠加层，右下；仅角色卡——物品卡无部署费，使用链后续批次）
                if (isUnit)
                {
                    var costGo = new GameObject("DeployCost");
                    costGo.transform.SetParent(wrapperGo.transform, false);
                    var costRt = costGo.AddComponent<RectTransform>();
                    costRt.anchorMin = costRt.anchorMax = new Vector2(1f, 0f);
                    costRt.anchoredPosition = new Vector2(-16f, 16f);
                    costRt.sizeDelta = new Vector2(56f, 26f);
                    var costText = costGo.AddComponent<TextMeshProUGUI>();
                    costText.font = BattleViewFactory.WorldTextFont;
                    costText.fontSize = 22;
                    costText.alignment = TextAlignmentOptions.Center;
                    costText.color = Palette.高亮金;
                    costText.text = _unitConfig.GetUnitData(cardId.AsUnitName()).GetEffectiveDeployCost().ToString();
                }

                var btn = wrapperGo.AddComponent<UnityEngine.UI.Button>();
                btn.targetGraphic = hit;
                var captured = cardId;
                btn.onClick.AddListener(() =>
                {
                    if (captured.cardType == CardType.Unit) EnterDeployAim(captured.value);
                    else SetTip("Battle_TipItemCardPending"); // 物品卡使用后续批次接入，不进部署链
                });
                _handCardButtons.Add(btn);
            }
        }

        /// <summary>进入部署瞄准（点手牌卡）：可选格=核心半径 2（客户端粗筛，Host IsDeployCellValid 兜底）</summary>
        private void EnterDeployAim(int unitNameValue)
        {
            if (_state != HudState.Idle) return; // 仅手牌态可起（选中单位时手牌已隐藏）
            _deployAimUnit = unitNameValue;
            _state = HudState.Aiming;
            _aimDef = null;
            ClosePopup();

            _aimCells.Clear();
            var snapshot = _session.Player.LatestSnapshot;
            var core = FindMyCorePosition(snapshot);
            for (int dx = -DeployUnitExecutor.DeployRadiusFromCore; dx <= DeployUnitExecutor.DeployRadiusFromCore; dx++)
            for (int dy = -DeployUnitExecutor.DeployRadiusFromCore; dy <= DeployUnitExecutor.DeployRadiusFromCore; dy++)
            {
                var c = new BattleCell(core.x + dx, core.y + dy);
                if (_board.Map.HasTile(c.x, c.y))
                    _aimCells.Add(c);
            }
            ShowAimHighlights();
            ApplyStateVisibility(); // Aiming 态：取消钮现、手牌藏
            SetTip("Battle_TipAimDirection"); // v1 复用方向瞄准提示；专属提示键随 B6c-2 卡面 polish
        }

        /// <summary>我方核心位置（部署半径圆心；v1=出生区中心=spawnCenters 第一个——双端 PlayerIds 同序）</summary>
        private BattleCell FindMyCorePosition(BattleSnapshot snapshot)
        {
            var centers = _board.Map.spawnCenters;
            if (centers != null && centers.Count > 0)
            {
                // 我方=P1（本地 1v1 惯例，BattleScreen 装配 i==0→TeamType.A 同序）
                return centers[0];
            }
            return snapshot?.units.FirstOrDefault(u => u.playerId == _myPlayerId)?.position ?? BattleCell.zero;
        }

        // ==================== 棋盘点击（拾取/瞄准） ====================

        private void OnBoardTap(Vector2 screenPos)
        {
            if (_layoutEditing) return; // 布局编辑期棋盘交互全静默
            if (_session == null || _session.Flow.Phase != BattlePhase.Selecting) return;
            if (_camera == null || _board == null || _board.Map == null) return;
            if (!_camera.TryGetBoardPoint(screenPos, out Vector3 world)) return;

            var cell = _board.WorldToCell(world);
            bool inBounds = _board.Map.HasTile(cell.x, cell.y);
            var snapshot = _session.Player.LatestSnapshot;
            if (snapshot == null) return;

            var myUnit = FindUnitAt(snapshot, cell);
            var enemyUnit = FindUnitAt(snapshot, cell, enemiesOnly: true);

            switch (_state)
            {
                case HudState.Aiming:
                    if (inBounds && _aimCells.Contains(cell)) { SubmitAim(cell, enemyUnit); return; }
                    // 瞄准态点非可选格 = 退回选中态（不算"点空白取消选中"）
                    ExitAiming();
                    return;

                case HudState.UnitSelected:
                    if (PopupOpen) { ClosePopup(); return; }          // 情况②：面板开着点外部=收面板（选中保持）
                    if (myUnit != null) { SelectUnit(myUnit.unitId); return; } // 换选中
                    if (enemyUnit != null) return;                    // 点敌方：无操作
                    DeselectUnit();                                   // 点空白=取消选中
                    return;

                case HudState.Idle:
                    if (myUnit != null) SelectUnit(myUnit.unitId);
                    return;
            }
        }

        private UnitState FindUnitAt(BattleSnapshot snapshot, BattleCell cell, bool enemiesOnly = false)
        {
            foreach (var u in snapshot.units)
            {
                if (u.isCorpse != 0) continue;
                if (u.position.x != cell.x || u.position.y != cell.y) continue;
                bool mine = u.playerId == _myPlayerId;
                if (enemiesOnly && mine) continue;
                if (!enemiesOnly && !mine) continue;
                return u;
            }
            return null;
        }

        // ==================== 状态机 ====================

        private void SelectUnit(string unitId)
        {
            ExitAiming();
            _selectedUnitId = unitId;
            _state = HudState.UnitSelected;
            ApplyStateVisibility(); // 先态显隐、后数据刷新——无数据技能键的隐藏由 RefreshSkillButtons 终态定
            RefreshSkillButtons();
            ShowSelectMarker(unitId);
            SetTip("Battle_TipUnitSelected");
        }

        private void DeselectUnit()
        {
            _selectedUnitId = null;
            _state = HudState.Idle;
            ClosePopup();
            HideSelectMarker();
            ApplyStateVisibility();
            SetTip("Battle_TipSelect");
        }

        private void EnterAiming(SkillButtonDef def)
        {
            if (def == null) return;
            _aimDef = def;
            _state = HudState.Aiming;
            SetAimSelectRing(def, true);
            ClosePopup();
            ComputeAimCells();
            ShowAimHighlights();
            ApplyStateVisibility(); // Aiming 态：技能盘+移动+取消可见、手牌藏
            SetTip(def.IsMove
                ? "Battle_TipAimMove"
                : IsLineSkill(def) ? "Battle_TipAimDirection" : "Battle_TipAimSkill");
        }

        /// <summary>该按钮技能是否直线型（战技/爆发=十字方向瞄准；延奏/契约=单位指向）</summary>
        private bool IsLineSkill(SkillButtonDef def)
        {
            var data = GetSelectedSkillData(def);
            return data != null
                && data.skillType != SkillType.Enso
                && data.skillType != SkillType.Contract
                && data.skillType != SkillType.Interact;
        }

        private void ExitAiming()
        {
            if (_state != HudState.Aiming) return;
            // 部署瞄准：回手牌态（无选中单位；_aimDef=null 时 SetAimSelectRing 安全跳过）
            bool wasDeployAim = _deployAimUnit != 0;
            _deployAimUnit = 0;
            if (wasDeployAim)
            {
                _state = HudState.Idle;
                _aimDef = null;
                _aimCells.Clear();
                ClearHighlights();
                ApplyStateVisibility();
                SetTip("Battle_TipSelect");
                return;
            }
            _state = HudState.UnitSelected;
            SetAimSelectRing(_aimDef, false);
            _aimDef = null;
            _aimCells.Clear();
            ClearHighlights();
            ApplyStateVisibility();
            SetTip("Battle_TipUnitSelected");
        }

        /// <summary>瞄准态视觉反馈：亮/灭对应技能按钮的选中环（prefab 自带 skillSelect）</summary>
        private static void SetAimSelectRing(SkillButtonDef def, bool on)
        {
            if (def?.view?.skillSelect != null)
                def.view.skillSelect.gameObject.SetActive(on);
        }

        /// <summary>瞄准可选格：移动 = 8 方向直线 1..3 步；战技/爆发（直线型）= 十字方向瞄准；
        /// 延奏/契约（单位指向型）= 敌方存活单位所在格（B4：投放形态由技能类型分档，docs/18 决策二）</summary>
        private void ComputeAimCells()
        {
            _aimCells.Clear();
            var snapshot = _session.Player.LatestSnapshot;
            var sel = snapshot?.units.FirstOrDefault(u => u.unitId == _selectedUnitId);
            if (sel == null) return;

            if (_aimDef.IsMove)
            {
                // 移动：8 方向 × 1..3 步（客户端只做地块粗筛；体积/阻挡由 Host 结算兜底）
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    for (int step = 1; step <= 移动最大步数; step++)
                    {
                        var c = new BattleCell(sel.position.x + dx * step, sel.position.y + dy * step);
                        if (!_board.Map.HasTile(c.x, c.y)) break;
                        _aimCells.Add(c);
                    }
                }
                return;
            }

            var skillData = GetSelectedSkillData(_aimDef);
            if (skillData == null) return;

            // 单位指向型（延奏/契约）：敌方存活单位所在格（目标判定不经格子，docs/05 §5.3）
            if (skillData.skillType == SkillType.Enso || skillData.skillType == SkillType.Contract)
            {
                foreach (var u in snapshot.units)
                {
                    if (u.isCorpse != 0 || u.playerId == _myPlayerId) continue;
                    if (_board.Map.HasTile(u.position.x, u.position.y))
                        _aimCells.Add(new BattleCell(u.position.x, u.position.y));
                }
                return;
            }

            // 直线型（战技/爆发）：十字 4 方向瞄准格（点方向格提交 direction；投射物路径 Host 即定）
            for (int dir = 0; dir < 4; dir++)
            {
                int dx = dir == 0 ? 1 : dir == 1 ? -1 : 0;
                int dy = dir == 2 ? 1 : dir == 3 ? -1 : 0;
                for (int step = 1; step <= 方向瞄准显示距离; step++)
                {
                    var c = new BattleCell(sel.position.x + dx * step, sel.position.y + dy * step);
                    if (!_board.Map.HasTile(c.x, c.y)) break;
                    _aimCells.Add(c);
                }
            }
        }

        private void SubmitAim(BattleCell cell, UnitState enemyAtCell)
        {
            // 部署瞄准分支（B6c）：点可选格=出战上交（玩家级行动，占本回合行动配额）
            if (_deployAimUnit != 0)
            {
                var deployAction = new ActionData
                {
                    playerId = _myPlayerId,
                    actionType = ActionType.DeployUnit,
                    deployUnitName = _deployAimUnit,
                    deployCell = cell,
                };
                _session.SubmitAction(deployAction);
                GICLog.Info($"[BattleHud] {_myPlayerId} 上交：出战 {(UnitName)_deployAimUnit} @ {cell}");
                ExitAiming();
                return;
            }

            var snapshot = _session.Player.LatestSnapshot;
            var sel = snapshot?.units.FirstOrDefault(u => u.unitId == _selectedUnitId);
            if (sel == null) return;
            bool isMove = _aimDef.IsMove;

            var action = new ActionData
            {
                playerId = _myPlayerId,
                unitId = _selectedUnitId,
                actionType = isMove ? ActionType.Move : ActionType.Skill,
                skillIndex = isMove ? 0 : GetSelectedSkillIndex(_aimDef.type),
                targetUnitId = "",
                moveMagnitude = 1,
                direction = Direction2D.Up,
            };

            if (isMove)
            {
                int dx = cell.x - sel.position.x;
                int dy = cell.y - sel.position.y;
                action.direction = DeltaToDirection(dx, dy);
                action.moveMagnitude = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
            }
            else if (IsLineSkill(_aimDef))
            {
                // 直线型：点方向格 → 归一十字方向（目标由 Host 投射物扫描即定，无需指定单位）
                int dx = cell.x - sel.position.x;
                int dy = cell.y - sel.position.y;
                action.direction = SnapToCardinal(dx, dy);
            }
            else
            {
                // 单位指向型（延奏/契约）：目标=点中格上的敌方单位
                action.targetUnitId = enemyAtCell != null ? enemyAtCell.unitId : "";
            }

            _session.SubmitAction(action);
            GICLog.Info($"[BattleHud] {_myPlayerId} 上交：{action.actionType} by {sel.unitName}" +
                        (isMove
                            ? $" {action.direction} ×{action.moveMagnitude}"
                            : (string.IsNullOrEmpty(action.targetUnitId)
                                ? $" → 方向 {action.direction}"
                                : $" → {action.targetUnitId}")));

            ExitAiming();
            DeselectUnit();
            SetTip("Battle_TipSubmitted");
        }

        /// <summary>格差归一到十字方向（|dx|≥|dy| 取横轴，否则取纵轴；0,0 防御回 Right）</summary>
        private static Direction2D SnapToCardinal(int dx, int dy)
        {
            if (Mathf.Abs(dx) >= Mathf.Abs(dy))
                return dx >= 0 ? Direction2D.Right : Direction2D.Left;
            return dy >= 0 ? Direction2D.Up : Direction2D.Down;
        }

        private static Direction2D DeltaToDirection(int dx, int dy)
        {
            if (dx > 0 && dy == 0) return Direction2D.Right;
            if (dx < 0 && dy == 0) return Direction2D.Left;
            if (dx == 0 && dy > 0) return Direction2D.Up;
            if (dx == 0 && dy < 0) return Direction2D.Down;
            if (dx > 0 && dy > 0) return Direction2D.UpRight;
            if (dx < 0 && dy > 0) return Direction2D.UpLeft;
            if (dx > 0 && dy < 0) return Direction2D.DownRight;
            return Direction2D.DownLeft;
        }

        // ==================== 技能按钮（点击式三情况） ====================

        /// <summary>点击式三情况（四键全统一，含移动——2026-09-18 拍板）：①面板开着再点同键=隐藏面板进入瞄准
        /// ②面板没开第一次点=开面板 ③换点其它技能=切内容。移动与其余三键唯一差异=瞄准语义（def.IsMove）；
        /// 拖动式=B4 接线；瞄准态点按钮=无操作（退出走取消按钮/点非可选格）。</summary>
        private void OnSkillButtonClicked(SkillButtonDef def)
        {
            if (def == null || _state != HudState.UnitSelected) return;
            if (_layoutEditing) return; // 编辑期点击让位给拖拽/选框（拖拽板在控件之上）
            // 置灰防线收口在处理端：SkillClickForwarder=IPointerClickHandler 不受 Toggle.interactable 拦截
            //（UGUI 对同物体全部兼容 handler 执行，interactable 只拦 Selectable 自身，docs/14 §63）
            var toggle = def.view != null ? def.view.GetComponent<Toggle>() : null;
            if (toggle != null && !toggle.interactable) return;
            if (PopupOpen)
            {
                if (_popupDef == def)
                    EnterAiming(def);
                else
                {
                    ClosePopup();
                    ShowSkillPopup(def);
                }
                return;
            }
            ShowSkillPopup(def);
        }

        private void ShowSkillPopup(SkillButtonDef def)
        {
            _popupDef = def;
            if (_skillDetailView == null) return;

            // 走现有技能详情体系：UnitData.skills 的 SkillData → SkillDetailView（图标/类型/名称/描述/参数全本地化）
            var skillData = GetSelectedSkillData(def);
            var unitData = GetSelectedUnitData();
            if (skillData == null || unitData == null) return;

            _skillDetailView.skillDetailPanel.SetActive(true);
            _skillDetailView.InitWithData(skillData, unitData, null);
            _skillDetailView.OpenPanel();
        }

        private void ClosePopup()
        {
            if (_skillDetailView != null)
                _skillDetailView.ClosePanel();
        }

        private void OnCancelButtonClicked()
        {
            if (_layoutEditing) return;
            if (_state == HudState.Aiming) ExitAiming();
        }

        // ==================== 可选格高亮 + 选中标记（世界层） ====================

        private void ShowAimHighlights()
        {
            ClearHighlights();
            if (_highlightRoot == null) return;

            foreach (var cell in _aimCells)
            {
                var quad = BattleViewFactory.CreateQuad(_highlightRoot,
                    $"AimHighlight_{cell.x}_{cell.y}", GetAimHighlightMaterial());
                quad.transform.position = new Vector3(
                    _board.CellToWorld(cell).x,
                    _board.GetSurfaceHeight(cell) + 0.03f,
                    _board.CellToWorld(cell).z);
                quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                quad.transform.localScale = new Vector3(0.92f, 0.92f, 1f);
                _highlightQuads.Add(quad);
            }
        }

        /// <summary>瞄准高亮共享材质（懒建单实例复用——曾每格 new Material 且清理只销 quad 不销材质，
        /// 反复进出瞄准态无限累积已修，docs/14 §63；高亮色走 palette 可调，每次进瞄准态刷新）</summary>
        private Material GetAimHighlightMaterial()
        {
            if (_aimHighlightMaterial == null)
                _aimHighlightMaterial = BattleViewFactory.CreateUnlitMaterial(Palette.高亮金);
            _aimHighlightMaterial.color = Palette.高亮金;
            return _aimHighlightMaterial;
        }

        private void ClearHighlights()
        {
            foreach (var quad in _highlightQuads)
                if (quad != null) Destroy(quad);
            _highlightQuads.Clear();
        }

        /// <summary>选中单位脚下金色圆盘标记（选中无棋盘反馈的补全；选中态/瞄准态常显）</summary>
        private void ShowSelectMarker(string unitId)
        {
            if (_selectMarker == null) return;
            var snapshot = _session.Player.LatestSnapshot;
            var unit = snapshot?.units.FirstOrDefault(u => u.unitId == unitId);
            if (unit == null) { HideSelectMarker(); return; }

            var cellWorld = _board.CellToWorld(unit.position);
            _selectMarker.transform.position = new Vector3(
                cellWorld.x, _board.GetSurfaceHeight(unit.position) + 0.024f, cellWorld.z);
            _selectMarker.SetActive(true);
        }

        private void HideSelectMarker()
        {
            if (_selectMarker != null) _selectMarker.SetActive(false);
        }

        // ==================== 技能数据链（现有体系：UnitConfig.skills → SkillData；2026-09-18 复用拍板） ====================

        /// <summary>单位配置（DI 容器 [Bean] 缓存——ConfigManager 产出；Bind 时 Inject 注入。
        /// 2026-09-23 审查 Y10 收口：全 HUD 分件共用此字段，勿再 Resources.Load 旁路）</summary>
        [Autowired] private UnitConfig _unitConfig;

        /// <summary>选中角色的配置数据（头像/技能表/元素全在；BattlePlayer 同款注入）</summary>
        private UnitConfig.UnitData GetSelectedUnitData()
        {
            var snapshot = _session.Player.LatestSnapshot;
            var unit = snapshot?.units.FirstOrDefault(u => u.unitId == _selectedUnitId);
            if (unit == null) return null;
            return Enum.TryParse(unit.unitName, out UnitName name) && _unitConfig != null && _unitConfig.TryGetUnitData(name, out var data)
                ? data : null;
        }

        /// <summary>该技能类型在 UnitData.skills 数组的索引（ActionData.skillIndex 的 Host 侧语义）</summary>
        private int GetSelectedSkillIndex(SkillType want)
        {
            var unitData = GetSelectedUnitData();
            if (unitData?.skills == null) return 0;
            for (int i = 0; i < unitData.skills.Length; i++)
                if (unitData.skills[i].skillType == want) return i;
            return 0;
        }

        /// <summary>按钮 def → 该角色的 SkillData（UnitData.skills 按 def.type 分拣）；
        /// fallback 到首技能是预期语义——3004 号角色设计即无战技、主要靠移动，勿当 bug 修（docs/14 §63④）</summary>
        private SkillConfig.SkillData GetSelectedSkillData(SkillButtonDef def)
        {
            if (def == null) return null;
            var unitData = GetSelectedUnitData();
            if (unitData?.skills == null) return null;
            return unitData.skills.FirstOrDefault(s => s.skillType == def.type)
                ?? unitData.skills.FirstOrDefault();
        }

        /// <summary>选中单位时刷新技能盘：表驱动全键统一（移动走 ApplyMoveButton，其余走 ApplySkillButton）；
        /// 图标/元素色环/主动被动色全走 SkillIconView.InitWithData 现有链</summary>
        private void RefreshSkillButtons()
        {
            var unitData = GetSelectedUnitData();
            if (unitData == null) return;

            foreach (var def in _skillButtons)
            {
                if (def.IsMove) ApplyMoveButton(def, unitData);
                else ApplySkillButton(def, unitData);
            }
        }

        /// <summary>技能按钮刷新（非移动键）：数据分拣→InitWithData 现有链（图标白底不染+底图染亮元素色+
        /// 主动/被动色环，2026-09-10 拍板规则全在 SkillIconView 内）；无数据=隐藏+置灰（原延奏特例泛化全键）；
        /// 元能不足=置灰（B6a：爆发/延奏等 EnergyCost>0 的技能，门槛=技能消耗值，门槛随快照刷新）</summary>
        private void ApplySkillButton(SkillButtonDef def, UnitConfig.UnitData unitData)
        {
            if (def?.view == null) return;
            var data = GetSelectedSkillData(def);
            def.view.gameObject.SetActive(data != null);
            var toggle = def.view.GetComponent<Toggle>();
            if (toggle != null)
                toggle.interactable = data != null && HasEnergyForSkill(data);
            if (data == null) return;

            def.view.InitWithData(data, unitData, ViewType.OnlyDisplay, _skillDetailView);

            if (def.nameText != null)
            {
                def.nameText.ClearAllEntries();
                def.nameText.AddEntry(data.skillID.GetEntry());
            }
        }

        /// <summary>选中单位的元能是否够放此技能（EnergyCost=0 恒可；读快照运行态，选择阶段头权威刷新）</summary>
        private bool HasEnergyForSkill(SkillConfig.SkillData skillData)
        {
            int cost = BattleSimState.GetEnergyCost(skillData);
            if (cost <= 0) return true;
            var snapshot = _session?.Player?.LatestSnapshot;
            if (snapshot == null || string.IsNullOrEmpty(_selectedUnitId)) return false;
            var sel = snapshot.units.FirstOrDefault(u => u.unitId == _selectedUnitId);
            return sel != null && sel.energy >= cost;
        }

        /// <summary>移动按钮刷新（特殊技能）：数据链走 skills[Move]（InitWithData 染角色元素色底+主动环；
        /// 无 Move 条目单位不隐藏——移动人人可用，仅跳过染色）。图标+名称随单位常态切换
        /// （walk/fly/amphibious → 步行/飞行/两栖，docs/18 决策六"随单位切换与否"待拍板项的数据驱动落地）</summary>
        private void ApplyMoveButton(SkillButtonDef def, UnitConfig.UnitData unitData)
        {
            if (def?.view == null || unitData == null) return;

            var move = unitData.skills?.FirstOrDefault(s => s.skillType == SkillType.Move);
            if (move != null)
                def.view.InitWithData(move, unitData, ViewType.OnlyDisplay, null);

            // 图标+名称随常态切换（InitWithData 已填配置的 walk.png，飞行/两栖覆盖为对应图）
            SkillName nameId = SkillName.Common_Walk;
            string iconPath = "UI/Skills/walk";
            if (unitData.normalMoveType == ForceType.Fly)
            {
                nameId = SkillName.Common_Fly;
                iconPath = "UI/Skills/fly";
            }
            else if (unitData.normalMoveType == ForceType.Amphibious)
            {
                nameId = SkillName.Common_Amphibious;
                iconPath = "UI/Skills/amphibious";
            }
            if (def.view.skillIcon != null)
                def.view.skillIcon.sprite = Resources.Load<Sprite>(iconPath);
            if (def.nameText != null)
            {
                def.nameText.ClearAllEntries();
                def.nameText.AddEntry(nameId.GetEntry());
            }
        }

        /// <summary>提示条文案切换（UIText 战斗段键；null/空 = 清空）</summary>
        private void SetTip(string key)
        {
            if (_tipCombiner == null) return;
            if (string.IsNullOrEmpty(key))
            {
                _tipCombiner.SetSingleEntry(string.Empty);
                return;
            }
            _tipCombiner.SetSingleEntry(new LocalizedString("UIText", key));
        }
    }
}
