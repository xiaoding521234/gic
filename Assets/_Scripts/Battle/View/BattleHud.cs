using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
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
    /// 点空白取消选中；技能瞄准 = 可选格推荐分色高亮（推荐/不推荐=BattlePalette 瞄准推荐色/瞄准
    /// 不推荐色，色相勿写死在此处——2026-09-23 拍板分色、09-24 白改金；推荐=该方向能命中敌人/
    /// 该格实际能走到；不可选=无提示）+ 右上取消按钮 + 选中单位脚下金色标记。
    /// 瞄准提交制（2026-09-26 拍板）：点可选格=金色待定（BattlePalette.瞄准已选色，可点其它格变更、
    /// 点空白取消技能回选中态），确认=顶部「完成选择」按钮——待定格提交行动/无待定空过完成选择
    /// （多人提前开演=各真人交齐一份，AI 由脑自动上交）；倒计时归零=自动按下该按钮（统一复用链路）。
    /// 交互状态机：Idle（手牌态）→ UnitSelected（行动态）→ Aiming（瞄准态）+ LayoutEditing（编辑态门控）。
    /// 输入 = BattleCameraController.OnBoardTap（Drag 短点击复合发射，docs/24 §7.10 tap+pan 同体）。
    /// 文案 = TextCombiner 本地化（docs/20 §2；UIText 12000 战斗段）；素材全部复用项目内资产。
    /// 拖动式瞄准已落地（B4 2026-09-26：王者荣耀式手势+待定制——技能键按下拖出→**拖向=瞄准方向**
    /// （轮心→小圆盘位移定方向与距离，与指针落点无关）→金色待定**单格**实时跟随→松手=留待定
    /// （不提交，确认=完成选择按钮），拖回技能盘/取消钮松手=取消；拖动时键上现**大圆盘**（距离转盘
    /// ——盘缘=最远格）、手指处**小圆盘**（不超大圆盘、不出屏幕）+金格出屏时相机丝滑移过去——
    /// 见 OnSkillButtonDragBegin/ShowDragWheel）；协议核心血条 = B8 接线。
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

        [Header("手牌下沉（2026-09-26 拍板：默认沉半张避让视野，鼠标接近热区才上移）")]
        [Tooltip("默认下沉藏量=半张卡（卡高 240 之半，按手牌槽缩放自动换算画布量）")]
        [SerializeField] private float 手牌下沉半卡 = 120f;
        [Tooltip("接近热区：手牌矩形左右外扩余量（画布单位）")]
        [SerializeField] private float 手牌热区侧探 = 60f;
        [Tooltip("接近热区：手牌矩形向上外扩余量（画布单位）——接近主方向探测带")]
        [SerializeField] private float 手牌热区上探 = 100f;
        [Tooltip("升/降指数趋近系数（1/s，12≈0.25s 到位）")]
        [SerializeField] private float 手牌升降速度 = 12f;

        [Header("倒计时（2026-09-26 拍板：3 倍字号+黑描边；小于告急秒数=红色+字号持续脉动）")]
        [Tooltip("告急阈值（秒）——剩余时间低于此值进入红色脉动")]
        [SerializeField] private float 倒计时告急秒数 = 5f;
        [Tooltip("告急字号呼吸幅度（基准字号的比例，0.12=±12%）")]
        [SerializeField] private float 倒计时脉动幅度 = 0.12f;
        [Tooltip("告急字号呼吸频率（次/秒）")]
        [SerializeField] private float 倒计时脉动频率 = 1.5f;
        [Tooltip("SDF 原生描边宽度（0~1 相对字形，随视口缩放恒定；迭代链 0.3→0.22→0.15（2026-09-26 两拍「调细」）；首版 UGUI Outline 固定像素描边在缩放视口下不可见已弃用）")]
        [SerializeField] private float 倒计时描边宽度 = 0.15f;

        [Header("拖动式瞄准（B4，2026-09-26 落地：王者荣耀式手势+待定制——按下拖出，拖向=瞄准方向，松手=留金色待定单格）")]
        [Tooltip("指向型瞄准（延奏/契约）拖向锁定锥角（度）：候选目标屏幕方向与「轮心→小盘」拖向的夹角不超过此值才锁定（轮盘化后小盘无法位移到目标——以拖向选目标，夹角最小者胜；精确选择仍可点击式点格）")]
        [SerializeField] private float 拖动瞄准指向锥角 = 60f;

        [Header("拖动瞄准圆盘（2026-09-26 三拍：大圆盘=键上锚点+距离转盘、小圆盘不超大圆盘不出屏幕；选中格精确性全在大圆盘内——盘缘=最远格）")]
        [Tooltip("大圆盘半径（画布单位）——锚在被拖技能键圆心；方向型步距转盘=盘缘对应该方向最远可选格（半径越大选格越精细）")]
        [SerializeField] private float 拖动瞄准大圆盘半径 = 340f;
        [Tooltip("小圆盘半径（画布单位）——手指跟随盘（盘心不超大圆盘半径、盘缘不出屏幕）")]
        [SerializeField] private float 拖动瞄准小圆盘半径 = 56f;
        [Tooltip("小圆盘屏幕边距（画布单位）——盘缘距屏幕边缘的最小留白（轮盘靠屏角时屏幕边界优先于轮盘界）")]
        [SerializeField] private float 拖动瞄准圆盘屏幕边距 = 16f;

        /// <summary>disc.png 实心盘可见缘只占纹理半宽 0.830（四周透明边距大）、circle.png 描环线贴
        /// 纹理外缘 0.998——同尺寸下阴影可见缘比描环天然内缩约 17%（2026-09-26 用户报障
        /// 「半透明阴影比圆环小一点，这是不对的」的根因，非设计意图）。实心盘（大圆盘阴影+小圆盘）
        /// 纹理放大 0.998/0.830≈1.202 补偿：可见缘贴齐描环线/名义半径，多出的透明边距被描环盖住不可见</summary>
        private const float 实心盘贴图补偿 = 1.202f;

        // 瞄准常量（运行时计算用）
        private const int 方向瞄准显示距离 = 8; // 十字瞄准高亮格数（Host 投射物实际扫描 24 格）
        // 移动步数上限不再用常量——数据驱动=移动技能 MoveDistance 参数（B-S1b），缺参数回落 3

        /// <summary>移动瞄准步数上限=移动技能 MoveDistance 按基准换算（「拼接后为10%移速」2026-09-23
        /// 用户拍板：BasedOnMoveSpeed=10%×移速——安柏 50→5 格、凯亚 30→3 格；读快照 moveSpeed 与 Host 同源）；
        /// 无数据回落按 10% 移速</summary>
        private int 移动技能步数上限()
        {
            var moveData = GetSelectedSkillData(_aimDef); // IsMove 键 def.type=Move → 按 skillType 分拣命中
            var snapshot = _session?.Player?.LatestSnapshot;
            var sel = snapshot?.units.FirstOrDefault(u => u.unitId == _selectedUnitId);
            int moveSpeed = sel != null ? sel.moveSpeed : 30;
            return moveData != null ? moveData.ResolveMoveDistance(moveSpeed) : moveSpeed * 10 / 100;
        }

        // ==================== 运行引用 ====================

        private BattleSession _session;
        private BattleBoard _board;
        private BattleCameraController _camera;
        private Action _onCloseBattle;
        private string _myPlayerId;

        /// <summary>本端玩家队伍（2026-09-25 三轮审查 C2：敌我判定统一 TeamType 口径——
        /// 操控权/资源归属=playerId，阵营判定=TeamType，两个语义勿再混用）</summary>
        private TeamType MyTeam => _session != null && _session.Sim != null
            ? _session.Sim.GetTeamOf(_myPlayerId)
            : TeamType.A;

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
        // 完成选择按钮（2026-09-26：顶部阶段级按钮，样式=祈愿 Marketplace 同款 prefab 烘焙；
        // 瞄准待定确认/无待定 Pass 双语义，显隐随选择阶段——Update 轮询同 countdown）
        private Button _confirmButton;

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
        private Material _aimRecommendedMaterial;    // 可选且推荐（色=BattlePalette.瞄准推荐色）
        private Material _aimNotRecommendedMaterial; // 可选但不推荐（色=BattlePalette.瞄准不推荐色）
        private Material _aimPendingMaterial;       // 待定金格（色=BattlePalette.瞄准已选色，2026-09-26）
        private Material _selectMarkerMaterial;
        // 高亮 quad 按格索引（待定金格材质换装用——sharedMaterial 换装不产副本，还原回共享单实例）
        private readonly Dictionary<BattleCell, MeshRenderer> _aimQuadByCell = new Dictionary<BattleCell, MeshRenderer>();

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

        /// <summary>手牌货币物品牌卡（B6d：体力/摩拉=手牌（物品牌）拍板——恒占卡组投影末两位，
        /// 数量=局内持有非备战数/存档数，随回合发放与行动消耗经资源刷新链动态更新）</summary>
        private Card _moraHandCard;
        private Card _staminaHandCard;

        // 手牌下沉（2026-09-26 拍板：默认沉半张避让视野，鼠标接近热区才上移）：
        // 只动 HandCards.anchoredPosition（槽内件）——hand 槽锚点/布局方案数据零接触
        private RectTransform _handCardsRect;
        /// <summary>当前下沉量（画布单位；-1=未初始化，首帧直接落沉态不播动画）</summary>
        private float _handSinkCanvas = -1f;
        private readonly Vector3[] _handCornersBuffer = new Vector3[4];

        private string _selectedUnitId;
        private SkillButtonDef _popupDef;  // 详情面板当前展示的键
        private SkillButtonDef _aimDef;    // 瞄准中的键
        private readonly HashSet<BattleCell> _aimCells = new HashSet<BattleCell>();

        /// <summary>可选且推荐的格（_aimCells 差集=可选但不推荐；2026-09-23 拍板瞄准分色数据源：
        /// 推荐与否见 BattlePalette 瞄准推荐色/瞄准不推荐色，不可选=无提示。推荐口径=直线技能该方向
        /// 能命中敌人 / 移动该格实际能走到 / 单位指向全推荐 / 部署=碰撞判定链镜像（2026-09-25 拍板）</summary>
        private readonly HashSet<BattleCell> _aimRecommendedCells = new HashSet<BattleCell>();

        /// <summary>瞄准待定格（2026-09-26 拍板：点可选格不再立即提交——格子变金待定、
        /// 可点其它可选格变更、点空白=取消技能回选中态；确认提交=顶部「完成选择」按钮）。
        /// null=无待定</summary>
        private BattleCell? _pendingAimCell;

        /// <summary>拖动式瞄准会话中（B4，2026-09-26 落地）：技能键 OnBeginDrag 起手置位，
        /// OnDrag 逐帧刷金色待定单格、OnEndDrag 松手=留待定（确认走完成选择）；任何瞄准退出
        /// （取消/完成选择确认/超时/阶段切换）经 ExitAiming 统一收口清零</summary>
        private bool _dragAiming;

        // 拖动瞄准圆盘（2026-09-26 拍板「和王者一样，技能上显示一个大圆盘，并且手指拖拽位置还有小圆盘；
        // 圆盘不可超出屏幕边缘」+同日三拍「大圆盘太小/小圆盘不超大圆盘/选中格精确性全在大圆盘内」）：
        // 运行时建在 HUD 画布（非布局件、Image.raycastTarget 全关勿拦截；换美术素材改 EnsureDragWheel
        // 的 sprite 加载即可——现用 disc.png 实心圆盘+circle.png 细环）
        private GameObject _dragWheelRoot;
        private RectTransform _dragWheelBigFill;  // 大圆盘填充（disc.png×底盘半透明）
        private RectTransform _dragWheelBigRing;  // 大圆盘描环（circle.png×高亮金细线）
        private RectTransform _dragWheelSmall;    // 小圆盘（disc.png×瞄准已选色——与金色待定格同色系联动）
        private Vector2 _dragWheelCenterLocal;    // 大圆盘圆心（=被拖技能键圆心，画布局部）——拖向/步距转盘原点
        private Vector2 _dragDiscLocal;           // 小圆盘画布局部（轮盘界+屏幕界双夹取后）——瞄准解析唯一输入

        /// <summary>HUD 画布 RectTransform（盘位换算用；Overlay 画布世界坐标=屏幕像素）</summary>
        private RectTransform CanvasRect => _canvas != null ? (RectTransform)_canvas.transform : null;

        /// <summary>本回合完成选择已定死（2026-09-26 拍板「确认行动后就应当定死了」）：
        /// 点过完成选择（或超时自动按下）并成功上交后为 true——按钮置灰、再按/超时自动按下
        /// 均为无操作（完全统一：按已定死的按钮=无事发生）；新回合选择阶段复位。
        /// 旧「后交覆盖先交、可再瞄准再确认改行动」语义随之废除，Host 侧同拒绝双保险</summary>
        private bool _actionConfirmed;

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
            InitMyResourceChipIcons(); // B6d：chip 图标走 [Autowired] ItemConfig——必须在注入后初始化（寻址阶段为 null）
            ApplySavedLayoutOnStart();

            _session.Player.SnapshotUpdated += OnSnapshotUpdated;
            _session.Player.OnSegmentPlaying += OnSegmentPlayingHandler;
            _session.Player.OnResourceDelta += OnResourceDeltaHandler; // B6d：摩拉/体力命令增量（快照权威外的即时刷新）
            _session.Player.BattleOver += OnBattleOverHandler;         // S10 全灭软停：胜负 Tip
            _session.Flow.OnPhaseChanged += OnPhaseChanged;
            _session.Flow.OnSelectTimerExpired += OnSelectTimerExpiredHandler; // 超时=自动完成选择（统一链路）
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
                    _session.Player.OnResourceDelta -= OnResourceDeltaHandler;
                    _session.Player.BattleOver -= OnBattleOverHandler;
                }
                if (_session.Flow != null)
                {
                    _session.Flow.OnPhaseChanged -= OnPhaseChanged;
                    _session.Flow.OnSelectTimerExpired -= OnSelectTimerExpiredHandler;
                }
            }
            if (_camera != null)
                _camera.OnBoardTap -= OnBoardTap;

            // 世界层运行时材质释放（Destroy 物体不销材质，不释放则跨战斗累积）
            if (_aimRecommendedMaterial != null) Destroy(_aimRecommendedMaterial);
            if (_aimNotRecommendedMaterial != null) Destroy(_aimNotRecommendedMaterial);
            if (_aimPendingMaterial != null) Destroy(_aimPendingMaterial);
            if (_selectMarkerMaterial != null) Destroy(_selectMarkerMaterial);
            if (_countdownOutlineMat != null) // SDF 描边实例（TopBar 分件，TMP 不自销）
            {
                Destroy(_countdownOutlineMat);
                _countdownOutlineMat = null;
            }
        }

        // ==================== 数据回调 ====================

        private void OnSnapshotUpdated(BattleSnapshot snapshot) => RefreshFromSnapshot(snapshot);

        /// <summary>摩拉/体力命令增量（B6d）：仅我方驱动左上角 chip 即时刷新（敌方资源无即时表现需求，
        /// 快照权威兜底）；实现落 TopBar 分件 ApplyMyResourceDelta</summary>
        private void OnResourceDeltaHandler(string playerId, int statKind, int delta)
        {
            if (playerId != _myPlayerId) return;
            ApplyMyResourceDelta(statKind, delta);
        }

        /// <summary>战斗结束（S10 轻量全灭软停）：胜负 Tip 常驻，退出走既有设置钮确认流程；
        /// 正式胜负演出/结算画面=B8（协议 BattleOverMessage.winnerTeam 已按队伍下发，B7 多人分端复用）</summary>
        private void OnBattleOverHandler(BattleOverMessage message)
        {
            if (_layoutEditing) return; // 编辑期提示条保持编辑提示不抢写
            bool victory = (TeamType)message.winnerTeam == MyTeam;
            SetTip(victory ? "Battle_Victory" : "Battle_Defeat");
        }

        private void OnPhaseChanged(BattlePhase phase, int turn)
        {
            if (phase != BattlePhase.Selecting)
            {
                // 执行阶段：清瞄准/清选中回手牌态（技能盘收起=决策六执行阶段变化首版）
                ExitAiming();
                DeselectUnit();
                if (!_layoutEditing) SetTip("Battle_TipResolving"); // 编辑期提示条保持编辑提示不抢写
            }
            else
            {
                _actionConfirmed = false; // 新回合选择阶段开：完成选择定死复位（编辑期也要复位——编辑不挡阶段推进）
                if (!_layoutEditing)
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
            UpdateHandHover(); // 手牌下沉/接近上移（独立于阶段轮询，自带空守卫）

            // 拖动瞄准屏幕跟随喂点（2026-09-26 拍板「当拖拽的金格在屏幕外时，屏幕会丝滑的移动过去」）：
            // 仅拖动会话中且有金色待定格才喂；相机侧判出/入屏并指数趋近（金格居中即入屏，入屏即停）
            if (_camera != null)
            {
                Vector3? followWorld = null;
                if (_dragAiming && _pendingAimCell.HasValue && _board != null && _board.Map != null)
                    followWorld = _board.CellToWorld(_pendingAimCell.Value);
                _camera.SetDragFollowTarget(followWorld);
            }

            // 选择倒计时（docs/04 §4.2；每秒级刷新，静态数字条目）
            if (_session == null || _session.Flow == null || _countdownText == null) return;
            var flow = _session.Flow;
            bool selecting = flow.Phase == BattlePhase.Selecting;
            bool show = selecting && flow.SelectRemainingSeconds >= 0f;
            SetLayoutWidgetActive("countdown", show); // 编辑态强制可见由统一口处理（数字冻结展示）
            SetLayoutWidgetActive("confirm", show);  // 完成选择按钮=选择阶段常显（2026-09-26）
            if (_confirmButton != null)
                _confirmButton.interactable = !_actionConfirmed; // 已定死置灰——再按=无操作（超时自动按下同款）
            UpdateCountdownUrgency(show && flow.SelectRemainingSeconds < 倒计时告急秒数); // 告急红+脉动（早退前调保还原）
            if (!show && !_layoutEditing) return;

            string seconds = Mathf.CeilToInt(Mathf.Max(0f, flow.SelectRemainingSeconds)).ToString();
            if (_countdownCombiner.GetCombinedText() != seconds)
                _countdownCombiner.SetSingleEntry(seconds);
        }

        /// <summary>倒计时告急态（2026-09-26 拍板「当时间小于5秒时，数字大小持续循环变化，颜色变为红色」）：
        /// 剩余<告急秒数=伤害红+基准字号呼吸脉动（持续循环，Cos 0→1→0 无跳变）；离告急/离选择阶段
        /// 还原暖金与基准字号一次（等值守卫免每帧重写）。描边/字号基准接线在 TopBar 分件 ResolveTopBar</summary>
        private void UpdateCountdownUrgency(bool urgent)
        {
            if (_countdownText == null) return;
            if (urgent)
            {
                _countdownText.color = Palette.伤害红;
                float pulse = 1f + 倒计时脉动幅度 * (0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * 倒计时脉动频率 * Time.time));
                _countdownText.fontSize = _countdownBaseFontSize * pulse;
            }
            else if (!Mathf.Approximately(_countdownText.fontSize, _countdownBaseFontSize)
                     || _countdownText.color != Palette.暖金)
            {
                _countdownText.fontSize = _countdownBaseFontSize;
                _countdownText.color = Palette.暖金;
            }
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

            // 我方资源 chips（左上角摩拉/体力物品牌计数，B6d；敌方资源不显示——2026-09-25 拍板）
            UpdateMyResources(snapshot);

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

            // 卡列表签名比对（卡 id 列表，不含数量——货币数量每回合变化不触发重建，
            // 角标经 RefreshHandCurrencyCards 随资源链动态刷；条目存亡变化（货币空堆移除/复活）自然反映）
            var signature = myRes == null ? ""
                : string.Join(",", myRes.handCards.ConvertAll(h => h.cardType + ":" + h.value));
            if (signature == _handSignature) return;
            _handSignature = signature;

            foreach (var btn in _handCardButtons)
                if (btn != null) Destroy(btn.gameObject);
            _handCardButtons.Clear();
            _moraHandCard = null;
            _staminaHandCard = null;
            if (_handScroll != null) _handScroll.normalizedPosition = Vector2.zero;
            if (myRes == null) return;

            int count = myRes.handCards.Count;

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
            float rowWidth = count * cardWidth + (count - 1) * gap;
            // content 宽恒=行宽+左右边距 60（**勿夹到视口宽**——窄于视口才有 Elastic 拖程，
            // "1 张卡也能滑动"；宽于视口=正常滚动），卡排相对 content 中心对称排
            _handContent.sizeDelta = new Vector2(rowWidth + 120f, 260f);
            for (int i = 0; i < count; i++)
            {
                // 手牌条目（2026-09-25 拍板「获得卡片」统一）：卡+持有数量一等属性——
                // 普通/角色卡 count=局内真源（开局=备战数）；货币物品牌 count=资源池镜像
                // （发放/消耗经资源命令链即刷角标，RefreshHandCurrencyCards）
                var handEntry = myRes.handCards[i];
                var cardId = handEntry.AsCardId();
                bool isUnit = handEntry.IsUnit;
                bool isCurrency = handEntry.IsCurrency;

                // 配置校验：角色查 UnitConfig、物品查 ItemConfig——条目无配置跳过并告警
                int prepareCount = 0;
                if (isUnit)
                {
                    if (_unitConfig?.GetUnitData(cardId.AsUnitName()) == null)
                    {
                        GICLog.Warn($"[BattleHud] 手牌卡 {cardId} 无 UnitConfig 配置，跳过");
                        continue;
                    }
                    prepareCount = handEntry.count;
                }
                else
                {
                    var itemData = CardConfigResolver.Instance?.ItemConfig?.GetItemData(cardId.AsItemName());
                    if (itemData == null)
                    {
                        GICLog.Warn($"[BattleHud] 手牌卡 {cardId} 无 ItemConfig 配置，跳过");
                        continue;
                    }
                    prepareCount = handEntry.count;
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

                    // 货币物品牌持有引用（B6d）：数量随局内 resources 动态，资源刷新链重 Init 更新
                    if (isCurrency)
                    {
                        if (cardId.AsItemName() == ItemName.Mora) _moraHandCard = card;
                        else _staminaHandCard = card;
                    }
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
                    else SetTip("Battle_TipItemCardPending"); // 物品卡使用后续批次接入，不进部署链（货币物品牌同款）
                });
                _handCardButtons.Add(btn);
            }
        }

        /// <summary>手牌货币物品牌卡数量刷新（B6d：体力/摩拉数量=局内持有，快照/命令增量时随资源链调用；
        /// 重 Init 复用 ItemCardViewStrategy 数量渲染=池化重复 Init 既有用法）</summary>
        private void RefreshHandCurrencyCards()
        {
            RefreshCurrencyCard(_moraHandCard, ItemName.Mora, _myMora);
            RefreshCurrencyCard(_staminaHandCard, ItemName.Stamina, _myStamina);
        }

        private void RefreshCurrencyCard(Card card, ItemName item, int count)
        {
            if (card == null) return;
            var saveData = new GIC.Framework.SaveCardData();
            saveData.SaveItem(item, count);
            card.SetViewType(ViewType.OnlyDisplay);
            card.Init(saveData, null);
        }

        // ==================== 手牌下沉（2026-09-26 拍板：默认沉半张避让视野，鼠标接近热区才上移） ====================

        /// <summary>手牌下沉热区轮询+升降动画（每帧 Update 顶部调用）：
        /// 指针入热区（HandCards 矩形四向外扩）=全升，出区=沉「半张卡到画布底缘下」；
        /// 下沉量按升起态底缘动态测量——视口比例变化/布局槽拖动缩放全自适应；
        /// 升起态底缘用「现底缘+当前下沉量」恒等重建（防随动画回环漂移）；手牌隐藏期间照常趋沉
        /// （不可见），复显即默认沉态。布局系统安全：只写 HandCards.anchoredPosition，槽锚点不动</summary>
        private void UpdateHandHover()
        {
            if (_handCardsRect == null || _canvas == null) return;
            var canvasRt = (RectTransform)_canvas.transform;

            // 手牌槽缩放（画布量→HandCards 局部量换算；布局缩放 0.6~1.6，防 0 除）
            float slotScale = 1f;
            if (_layoutByKey.TryGetValue("hand", out var handDef) && handDef.slot != null)
                slotScale = Mathf.Max(0.01f, Mathf.Abs(handDef.slot.localScale.y));

            // 升起态底缘（距画布底缘，画布单位）：现底缘＋已应用的下沉量（首帧 anchoredPosition 尚为 0
            // 即升起位，恒等式自然成立）；半卡视觉量=120×槽缩放
            float currentBottom = HandCardsBottomFromCanvasBottom(canvasRt);
            float risenBottom = currentBottom + Mathf.Max(0f, _handSinkCanvas);

            // 热区检测：指针（鼠标/末次触点）在手牌矩形+余量内=接近
            bool risen = _handCardsRect.gameObject.activeInHierarchy && IsPointerNearHand(canvasRt);
            float targetSink = risen ? 0f : risenBottom + 手牌下沉半卡 * slotScale;

            if (_handSinkCanvas < 0f)
                _handSinkCanvas = targetSink; // 首帧直接落沉态（无升起闪现）
            else
                _handSinkCanvas = Mathf.Lerp(_handSinkCanvas, targetSink,
                    1f - Mathf.Exp(-手牌升降速度 * Time.deltaTime));

            // 应用：局部偏移=画布下沉量/槽缩放（槽缩放下局部单位视觉量随缩放）
            _handCardsRect.anchoredPosition = new Vector2(0f, -_handSinkCanvas / slotScale);
        }

        /// <summary>HandCards 底缘距画布底缘的距离（画布单位；Overlay 画布世界角=屏幕像素，
        /// 经画布逆变换取局部 y 再平移半高）</summary>
        private float HandCardsBottomFromCanvasBottom(RectTransform canvasRt)
        {
            _handCardsRect.GetWorldCorners(_handCornersBuffer);
            float bottomY = canvasRt.InverseTransformPoint(_handCornersBuffer[0]).y;
            return bottomY + canvasRt.rect.height * 0.5f;
        }

        /// <summary>指针接近判定：画布局部指针点 ∈ HandCards 当前矩形外扩（上探+侧探，下方不外扩——
        /// 屏幕底缘无从自下接近）；触屏取末次触点（Input.mousePosition 同源）</summary>
        private bool IsPointerNearHand(RectTransform canvasRt)
        {
            _handCardsRect.GetWorldCorners(_handCornersBuffer);
            Vector2 min = canvasRt.InverseTransformPoint(_handCornersBuffer[0]);
            Vector2 max = canvasRt.InverseTransformPoint(_handCornersBuffer[2]);
            min.x -= 手牌热区侧探;
            max.x += 手牌热区侧探;
            max.y += 手牌热区上探;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRt, Input.mousePosition, null, out var local)) return false;
            return local.x >= min.x && local.x <= max.x && local.y >= min.y && local.y <= max.y;
        }

        /// <summary>进入部署瞄准（点手牌卡）：可选格=核心半径 2（客户端粗筛，Host IsDeployCellValid 兜底）</summary>
        private void EnterDeployAim(int unitNameValue)
        {
            if (_state != HudState.Idle) return; // 仅手牌态可起（选中单位时手牌已隐藏）
            _deployAimUnit = unitNameValue;
            _state = HudState.Aiming;
            _aimDef = null;
            _pendingAimCell = null; // 新瞄准会话待定清零（高亮 quad 重建，材质无残留）
            ClosePopup();

            _aimCells.Clear();
            _aimRecommendedCells.Clear();
            var snapshot = _session.Player.LatestSnapshot;
            var deployData = _unitConfig != null ? _unitConfig.GetUnitData((UnitName)unitNameValue) : null;
            var core = FindMyCorePosition(snapshot);
            for (int dx = -DeployUnitExecutor.DeployRadiusFromCore; dx <= DeployUnitExecutor.DeployRadiusFromCore; dx++)
            for (int dy = -DeployUnitExecutor.DeployRadiusFromCore; dy <= DeployUnitExecutor.DeployRadiusFromCore; dy++)
            {
                var c = new BattleCell(core.x + dx, core.y + dy);
                if (!_board.Map.HasTile(c.x, c.y)) continue;
                _aimCells.Add(c);
                // 部署推荐分色（2026-09-25 拍板「根据碰撞决定」）：镜像 Host 碰撞判定链——
                // 不推荐格仍可点击（Host 校验兜底，同移动瞄准「水面红格仍可点」语义）
                if (CanDeployEnterPreview(c, deployData, snapshot))
                    _aimRecommendedCells.Add(c);
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

            // 选中开放任意单位（2026-09-26 拍板改版：含敌人/低级单位——可看技能盘/进瞄准查攻击范围；
            // 行动拦截移到提交时轻提示，SubmitAim 处把关；Host 侧 OnSubmitAction 权威校验不变）。
            // 同格多单位优先选中己方（自己的单位先被点中，敌方需点到无己方的格）
            var myUnit = FindUnitAt(snapshot, cell, UnitSide.Mine);
            var anyUnit = myUnit != null ? myUnit : FindUnitAt(snapshot, cell, UnitSide.Enemy);

            switch (_state)
            {
                case HudState.Aiming:
                    // 点可选格=金色待定（2026-09-26 拍板：不立即提交，可反复点其它格变更，
                    // 确认走「完成选择」按钮；目标单位由确认时再取快照）
                    if (inBounds && _aimCells.Contains(cell)) { SetPendingAimCell(cell); return; }
                    // 瞄准态点非可选格 = 退回选中态（不算"点空白取消选中"）
                    ExitAiming();
                    return;

                case HudState.UnitSelected:
                    if (PopupOpen) { ClosePopup(); return; }          // 情况②：面板开着点外部=收面板（选中保持）
                    if (anyUnit != null) { SelectUnit(anyUnit.unitId); return; } // 换选中（任意单位，己方优先）
                    DeselectUnit();                                   // 点空白=取消选中
                    return;

                case HudState.Idle:
                    if (anyUnit != null) SelectUnit(anyUnit.unitId);
                    return;
            }
        }

        /// <summary>单位侧向（2026-09-25 三轮审查 C2）：Mine=操控权归属（playerId）、
        /// MyTeam/Enemy=阵营判定（TeamType）——三个语义勿再用 playerId 比较敌我</summary>
        private enum UnitSide { Mine, MyTeam, Enemy }

        private UnitState FindUnitAt(BattleSnapshot snapshot, BattleCell cell, UnitSide side)
        {
            foreach (var u in snapshot.units)
            {
                if (u.isCorpse != 0) continue;
                if (u.position.x != cell.x || u.position.y != cell.y) continue;
                bool pick;
                if (side == UnitSide.Mine) pick = u.playerId == _myPlayerId;          // 操控权（B7 分端=本机玩家）
                else if (side == UnitSide.MyTeam) pick = (TeamType)u.team == MyTeam;  // 我军（2v2 含队友单位）
                else pick = (TeamType)u.team != MyTeam;                               // 敌军
                if (!pick) continue;
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
            _pendingAimCell = null; // 新瞄准会话待定清零
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
            _dragAiming = false; // 拖动会话统一收口（松手取消/确认提交/超时/阶段切换同一处清零）
            HideDragWheel();      // 圆盘随会话收口（EndDrag 已隐藏，此处=外部退出安全网，幂等）
            // 待定金格随高亮 quad 一并消失（ClearHighlights 销 quad），字段清零防陈旧提交
            _pendingAimCell = null;
            // 部署瞄准：回手牌态（无选中单位；_aimDef=null 时 SetAimSelectRing 安全跳过）
            bool wasDeployAim = _deployAimUnit != 0;
            _deployAimUnit = 0;
            if (wasDeployAim)
            {
                _state = HudState.Idle;
                _aimDef = null;
                _aimCells.Clear();
                _aimRecommendedCells.Clear();
                ClearHighlights();
                ApplyStateVisibility();
                SetTip("Battle_TipSelect");
                return;
            }
            _state = HudState.UnitSelected;
            SetAimSelectRing(_aimDef, false);
            _aimDef = null;
            _aimCells.Clear();
            _aimRecommendedCells.Clear();
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

        /// <summary>瞄准可选格：移动 = 十字四向 1..N 步；战技/爆发（直线型）= 十字方向瞄准；
        /// 延奏/契约（单位指向型）= 存活单位所在格（B4：投放形态由技能类型分档，docs/18 决策二）。
        /// 同步填 _aimRecommendedCells（推荐分色，2026-09-23 拍板）：直线技能=该方向能命中敌人
        /// （技能实例 WouldHitEnemyInDirection 静态预判，与 Host 判定形态同语义）；移动=该格实际
        /// 能走到（CanMoveEnterPreview 步进预判，被挡后该向余下格全不推荐——移动停在格前）；
        /// 单位指向/部署 v1 全推荐</summary>
        private void ComputeAimCells()
        {
            _aimCells.Clear();
            _aimRecommendedCells.Clear();
            var snapshot = _session.Player.LatestSnapshot;
            var sel = snapshot?.units.FirstOrDefault(u => u.unitId == _selectedUnitId);
            if (sel == null) return;

            if (_aimDef.IsMove)
            {
                // 移动：十字四向 × 1..N 步（全员移动技能描述=「选择十字方向其一」，2026-09-23 修正——
                // 首版误做成米字 8 向；上限=移动技能 MoveDistance 参数——移动是特殊技能、距离数据驱动，
                // B-S1b；客户端只做地块粗筛，体积/阻挡由 Host 结算兜底）
                // 推荐=步进可达（如凯亚步行不可入水——水面及其后全不推荐，Host 同款停在格前）
                int maxSteps = 移动技能步数上限();
                var selfData = GetSelectedUnitData();
                for (int dir = 0; dir < 4; dir++)
                {
                    int dx = dir == 0 ? 1 : dir == 1 ? -1 : 0;
                    int dy = dir == 2 ? 1 : dir == 3 ? -1 : 0;
                    bool reachable = true; // 步进可达链（被挡即断，该向余下格全不推荐）
                    for (int step = 1; step <= maxSteps; step++)
                    {
                        var c = new BattleCell(sel.position.x + dx * step, sel.position.y + dy * step);
                        if (!_board.Map.HasTile(c.x, c.y)) break; // 虚空=可选区截止（不可选无提示）
                        _aimCells.Add(c);
                        if (reachable && CanMoveEnterPreview(c, sel, selfData, snapshot))
                            _aimRecommendedCells.Add(c);
                        else
                            reachable = false;
                    }
                }
                return;
            }

            var skillData = GetSelectedSkillData(_aimDef);
            if (skillData == null) return;

            // 单位指向型：延奏=全图我方存活角色（含施法者自身——协奏语义，docs/07 蒙德；B-S1b 修正，
            // 此前误按敌方指向）；契约=敌方存活单位（docs/05 §5.3 目标判定不经格子）。
            // 推荐分色 v1：单位指向全推荐（目标格即语义本身，无优劣数据可分）。
            // 队伍口径=**选中单位**的队伍（2026-09-26 选中开放任意单位：查看敌方延奏/契约时
            // 目标域随施法者视角——敌方延奏高亮敌方全体、契约高亮我方全体）
            if (skillData.skillType == SkillType.Enso)
            {
                foreach (var u in snapshot.units)
                {
                    if (u.isCorpse != 0 || (TeamType)u.team != (TeamType)sel.team) continue;
                    if (_board.Map.HasTile(u.position.x, u.position.y))
                    {
                        var c = new BattleCell(u.position.x, u.position.y);
                        _aimCells.Add(c);
                        _aimRecommendedCells.Add(c);
                    }
                }
                return;
            }
            if (skillData.skillType == SkillType.Contract)
            {
                foreach (var u in snapshot.units)
                {
                    if (u.isCorpse != 0 || (TeamType)u.team == (TeamType)sel.team) continue;
                    if (_board.Map.HasTile(u.position.x, u.position.y))
                    {
                        var c = new BattleCell(u.position.x, u.position.y);
                        _aimCells.Add(c);
                        _aimRecommendedCells.Add(c);
                    }
                }
                return;
            }

            // 直线型（战技/爆发）：十字 4 方向瞄准格（点方向格提交 direction；投射物路径 Host 即定）。
            // 推荐=该方向能命中敌人（含尸体，Host 同语义）：预判走技能实例 WouldHitEnemyInDirection
            // （SkillFactory 按 skillID 建实例，判定形态与各技能 Host 结算同源）
            var previewSkill = SkillFactory.CreateWithData(skillData);
            for (int dir = 0; dir < 4; dir++)
            {
                int dx = dir == 0 ? 1 : dir == 1 ? -1 : 0;
                int dy = dir == 2 ? 1 : dir == 3 ? -1 : 0;
                var direction = dir == 0 ? Direction2D.Right : dir == 1 ? Direction2D.Left
                    : dir == 2 ? Direction2D.Up : Direction2D.Down;
                bool recommended = previewSkill.WouldHitEnemyInDirection(
                    _board.Map, snapshot, (TeamType)sel.team, sel.position, direction);
                for (int step = 1; step <= 方向瞄准显示距离; step++)
                {
                    var c = new BattleCell(sel.position.x + dx * step, sel.position.y + dy * step);
                    if (!_board.Map.HasTile(c.x, c.y)) break;
                    _aimCells.Add(c);
                    if (recommended) _aimRecommendedCells.Add(c);
                }
            }
        }

        /// <summary>单位名 → UnitData（快照 unitName 为枚举名字符串；查不到返回 null——
        /// 单位名缺配置时预判按"无碰撞规则"处理，Host 结算仍是权威）</summary>
        private UnitConfig.UnitData TryGetUnitData(string unitName)
        {
            return Enum.TryParse(unitName, out UnitName name) && _unitConfig != null
                ? _unitConfig.GetUnitData(name)
                : null;
        }

        /// <summary>移动推荐预判的单格进入判定（镜像 MovementResolver.CanEnter 判定链的客户端静态版）：
        /// 地形层（IsPassable 按 ForceType——步行不可入水/沼泽）→ 体积绝对层（格内体积+自身体积≤3）
        /// → 阻挡规则层（blockAllies/blockEnemies/blockedByEnemies 配置值——运行时 Buff 修改不可见）。
        /// 尸体保留碰撞（Host 同语义）；属提示非校验——Host 结算兜底不变。</summary>
        private bool CanMoveEnterPreview(BattleCell cell, UnitState self, UnitConfig.UnitData selfData,
            BattleSnapshot snapshot)
        {
            var forceType = selfData != null ? selfData.normalMoveType : ForceType.Walk;
            if (!_board.Map.IsPassable(cell.x, cell.y, forceType)) return false;

            int selfVolume = self.volume > 0 ? self.volume : 1;
            int existingVolume = 0;
            foreach (var u in snapshot.units)
            {
                if (u.unitId == self.unitId) continue;
                if (u.position.x != cell.x || u.position.y != cell.y) continue; // 含尸体——尸体保留碰撞
                existingVolume += u.volume > 0 ? u.volume : 1;
                if (existingVolume + selfVolume > 3) return false; // 体积绝对层（无视阻挡能力不可绕过）

                var occupantData = TryGetUnitData(u.unitName);
                if (occupantData == null) continue;
                bool sameTeam = (TeamType)u.team == (TeamType)self.team; // 阵营判定（TeamType 口径，2026-09-25 三轮审查 C2）
                if (sameTeam && occupantData.blockAllies) return false;
                if (!sameTeam && occupantData.blockEnemies && selfData != null && selfData.blockedByEnemies)
                    return false;
            }
            return true;
        }

        /// <summary>部署落点推荐预判（镜像 DeployUnitExecutor.IsDeployCellValid 碰撞判定链的客户端静态版，
        /// 2026-09-25 拍板「根据碰撞决定」）：地形层（按部署单位常态移动类型）→ 体积绝对层（现有+自身 ≤3，
        /// 最高级不可绕过）→ 阻挡规则层（与格内全部单位互不阻挡才推荐）；碰撞配置读 UnitData——运行时
        /// Buff 修改不可见，属提示非校验，Host 结算兜底。含尸体——尸体保留碰撞。部署单位尚未登场，无自身豁免。</summary>
        private bool CanDeployEnterPreview(BattleCell cell, UnitConfig.UnitData deployData, BattleSnapshot snapshot)
        {
            var forceType = deployData != null ? deployData.normalMoveType : ForceType.Walk;
            if (!_board.Map.IsPassable(cell.x, cell.y, forceType)) return false;

            int selfVolume = deployData != null && deployData.unitType == UnitType.Building ? 2 : 1;
            int existingVolume = 0;
            foreach (var u in snapshot.units)
            {
                if (u.position.x != cell.x || u.position.y != cell.y) continue; // 含尸体——尸体保留碰撞
                existingVolume += u.volume > 0 ? u.volume : 1;
                if (existingVolume + selfVolume > 3) return false; // 体积绝对层（最高级，不可绕过）

                var occupantData = TryGetUnitData(u.unitName);
                if (occupantData == null) continue;
                bool sameTeam = (TeamType)u.team == MyTeam; // 阵营判定（TeamType 口径，2026-09-25 三轮审查 C2）
                if (sameTeam && occupantData.blockAllies) return false;
                if (!sameTeam && occupantData.blockEnemies && deployData != null && deployData.blockedByEnemies)
                    return false;
            }
            return true;
        }

        /// <summary>提交瞄准行动（单出口：部署/移动/直线/单位指向）。2026-09-26 起唯一调用方=
        /// 「完成选择」按钮的待定确认路径（点格只产待定金格不提交）；防线拦截（非己方/低级单位）
        /// 弹 toast 后保持瞄准态待定不变。
        /// 返回值（完成选择定死拍板）：true=已成功上交（调用方置定死）；false=防线拦截未上交（勿定死）</summary>
        private bool SubmitAim(BattleCell cell, UnitState enemyAtCell)
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
                return true;
            }

            var snapshot = _session.Player.LatestSnapshot;
            var sel = snapshot?.units.FirstOrDefault(u => u.unitId == _selectedUnitId);
            if (sel == null) return false;
            bool isMove = _aimDef.IsMove;

            // 提交时行动防线（2026-09-26 拍板改版：选中/瞄准开放任意单位供查看技能盘与攻击范围，
            // 行动只能由**自己的高级单位**执行——非己方/低级单位在此弹 toast 轻提示、保持瞄准态继续查看。
            // 轻提示走 PopupManager.ShowToast（2026-09-26 报障修正：首版用提示条 SetTip 文字切换太隐晦
            // 玩家看不见——顶部滑入 toast 才是项目轻弹窗正主，Wish_NoPrimogem/Deck_Full 同款）；
            // Host 侧 OnSubmitAction 权威校验仍为双保险，B7 LAN 客户端绕 UI 也进不来）
            if (sel.playerId != _myPlayerId)
            {
                GICLog.Info($"[BattleHud] 提交拦截：{sel.unitName} 不是玩家 {_myPlayerId} 的角色");
                ShowBattleToast("Battle_NotYourUnit");
                return false;
            }
            var selData = TryGetUnitData(sel.unitName);
            if (selData == null || selData.starLevel < 3)
            {
                GICLog.Info($"[BattleHud] 提交拦截：{sel.unitName} 为低级单位（自主行动）");
                ShowBattleToast("Battle_MinorUnit");
                return false;
            }

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
                // 单位指向型：延奏=点中格上的我方角色（协奏，docs/07 蒙德；B-S1b 修正）；
                // 契约=点中格上的敌方单位（docs/05 §5.3）
                var skillData = GetSelectedSkillData(_aimDef);
                bool allyTargeting = skillData != null && skillData.skillType == SkillType.Enso;
                var unitAtCell = allyTargeting ? FindUnitAt(snapshot, cell, UnitSide.MyTeam) : enemyAtCell;
                action.targetUnitId = unitAtCell != null ? unitAtCell.unitId : "";
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
            return true;
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

        // ==================== 技能按钮（点击式三情况 + 拖动式瞄准） ====================

        /// <summary>点击式三情况（四键全统一，含移动——2026-09-18 拍板）：①面板开着再点同键=隐藏面板进入瞄准
        /// ②面板没开第一次点=开面板 ③换点其它技能=切内容。移动与其余三键唯一差异=瞄准语义（def.IsMove）；
        /// 拖动式=同键按下拖出即起瞄准（OnSkillButtonDragBegin）；瞄准态点按钮=无操作（退出走取消按钮/点非可选格）。</summary>
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

        // ==================== 拖动式瞄准（B4，2026-09-26 落地；王者荣耀式手势+待定制：docs/18 决策六两方式之拖动） ====================

        /// <summary>拖动式起手（SkillDragForwarder 转发，UGUI 拖拽阈值即起）：按住技能键拖出 → 进瞄准态
        /// 高亮可选格（与点击式共用 EnterAiming/高亮/待定/取消全链）→ 拖动全程金色待定**单格**实时跟随 →
        /// 松手=留待定（**不立即提交**——2026-09-26 拍板「一次选择 1 个格子、松手后不应立即完成选择」，
        /// 与点击式同款，确认唯一入口=「完成选择」按钮）。瞄准=拖向（王者荣耀手势，同日纠偏拍板
        /// 「不是拖出到格子上」）：方向由「按下点→指针」屏幕位移反投影到棋盘平面决定，**与指针落在
        /// 棋盘哪里无关、手指不必离开按键区**。其余口径：①面板开着直接拖=无缝切换拖动式（决策六点击式③）
        /// ②瞄准中拖另一键=换技能重瞄准（ExitAiming 收口旧选中环/待定后重进）③置灰键（无数据/元能/
        /// 体力门槛）同点击式不可起手 ④部署瞄准（点手牌卡）无选中单位不接管 ⑤相机平移不串扰——
        /// 拖拽起手在 UI 上，GestureHub 门2 拦下，BattleCameraController 的 Drag 识别器全程不见此指针序列。</summary>
        private void OnSkillButtonDragBegin(SkillButtonDef def, PointerEventData eventData)
        {
            if (def == null || _layoutEditing) return;
            if (_session == null || _session.Flow == null) return;
            if (_session.Flow.Phase != BattlePhase.Selecting) return;
            if (_deployAimUnit != 0) return;
            // 置灰防线收口在处理端（同 OnSkillButtonClicked：Toggle.interactable 只拦 Selectable 自身，
            // 转发件不受拦，docs/14 §63）
            var toggle = def.view != null ? def.view.GetComponent<Toggle>() : null;
            if (toggle != null && !toggle.interactable) return;

            if (_state == HudState.Aiming) ExitAiming(); // 换技能重瞄准（_dragAiming 随收口清零）
            if (_state != HudState.UnitSelected) return;  // Idle（无选中单位）不响应
            _dragAiming = true;
            EnterAiming(def);
            ShowDragWheel(def, eventData.position);   // 王者荣耀式圆盘（大=键上锚点、小=手指夹取跟随）
            UpdateDragAimPreview(eventData); // 起手即刷待定（阈值位移已含方向）
        }

        /// <summary>拖动中：金色待定单格实时跟随瞄准结果（方向型=拖向臂上第 k 格；指向型=指针附近
        /// 锁定的目标格）；无有效瞄准=清待定（松手取消）</summary>
        private void OnSkillButtonDrag(SkillButtonDef def, PointerEventData eventData)
        {
            if (!_dragAiming) return;
            UpdateDragAimPreview(eventData);
        }

        /// <summary>松手收束（与点击式同款待定制，2026-09-26 拍板「松手后不应该立即完成选择」）：
        /// 有效瞄准=保持金色待定单格与瞄准态，确认走「完成选择」按钮；**拖回技能盘任一键/取消钮上
        /// 松手=取消回选中态**（王者荣耀"拖回轮盘中心取消"，兼防微拖误触）；无有效瞄准（方向臂空/
        /// 指向无锁定）松手=取消。终帧以松手位校准（快甩时松手位与末帧 drag 位差一拍）。
        /// 会话中途被外部收口（超时自动确认/阶段切换经 ExitAiming）时 _dragAiming 已清，此处无为。</summary>
        private void OnSkillButtonDragEnd(SkillButtonDef def, PointerEventData eventData)
        {
            if (!_dragAiming) return;
            _dragAiming = false;
            if (_state != HudState.Aiming) { HideDragWheel(); return; }
            UpdateDragAimPreview(eventData); // 圆盘未收——终帧校准与拖动中同用夹取指针（屏缘一致）
            HideDragWheel(); // 手指已离键——圆盘随会话收（留待定路径也隐藏）
            if (ReleaseOverDiscOrCancel(eventData.position) || !_pendingAimCell.HasValue)
                ExitAiming();
            // 有效待定：保持金色待定+瞄准态——提交唯一入口=完成选择按钮
        }

        /// <summary>拖动瞄准实时解析（单格）：方向型（移动/直线）=轮心→小盘画布位移定十字方向+盘距
        /// 占大圆盘半径的比例定步数（盘缘=该方向最远可选格）；指向型（延奏/契约）=拖向选目标（候选
        /// 屏幕方向与拖向夹角最小且≤锥角者锁定——小盘限在轮盘内无法位移到目标）。
        /// 输入=_dragDiscLocal（双夹取后盘位）——选中格的精确性全在大圆盘内。无有效瞄准=清待定</summary>
        private void UpdateDragAimPreview(PointerEventData eventData)
        {
            UpdateDragWheel(eventData.position); // 小盘跟手+轮盘/屏幕双夹取（_dragDiscLocal=瞄准唯一输入）
            bool directionSkill = _aimDef != null && (_aimDef.IsMove || IsLineSkill(_aimDef));
            BattleCell? cell = directionSkill
                ? ComputeDragAimCellFromWheel(_dragDiscLocal)
                : FindNearestAimCellByWheelDirection(_dragDiscLocal);
            if (cell.HasValue) SetPendingAimCell(cell.Value);
            else ClearPendingAimCell();
        }

        /// <summary>方向型拖动瞄准解析（轮盘内单格，2026-09-26 三拍「选中格子的精确性应当限制在
        /// 大圆盘范围里」）：轮心→小盘的**画布**位移定十字方向（相机 yaw 恒 0，画布轴向=世界轴向——
        /// 不再反投影，大圆盘即瞄准面）；步数=盘距占大圆盘半径的比例×臂长（**盘缘=该方向最远可选格、
        /// 近心=第 1 格**，四舍五入钳 1..臂长）——大圆盘=距离转盘，盘越大选格越精细。臂步 1..maxStep
        /// 连续由 ComputeAimCells 保证（遇虚空截断）。无位移/无臂=null</summary>
        private BattleCell? ComputeDragAimCellFromWheel(Vector2 discLocal)
        {
            Vector2 d = discLocal - _dragWheelCenterLocal;
            if (d.sqrMagnitude < 1f) return null;
            var snapshot = _session.Player.LatestSnapshot;
            var sel = snapshot?.units.FirstOrDefault(u => u.unitId == _selectedUnitId);
            if (sel == null) return null;

            bool horizontal = Mathf.Abs(d.x) >= Mathf.Abs(d.y);
            int cdx = horizontal ? (d.x >= 0 ? 1 : -1) : 0;
            int cdy = horizontal ? 0 : (d.y >= 0 ? 1 : -1);

            int maxStep = 0;
            foreach (var c in _aimCells)
            {
                int adx = c.x - sel.position.x, ady = c.y - sel.position.y;
                bool onArm = horizontal
                    ? (ady == 0 && adx * cdx > 0)
                    : (adx == 0 && ady * cdy > 0);
                if (onArm) maxStep = Mathf.Max(maxStep, Mathf.Max(Mathf.Abs(adx), Mathf.Abs(ady)));
            }
            if (maxStep == 0) return null; // 该方向无臂（虚空/无格）

            // 盘距→步数：大圆盘半径=全臂程（阴影可见缘已贴齐描环线——实心盘贴图补偿；盘缘=最远格、近心=第 1 格）
            float axisCanvas = horizontal ? Mathf.Abs(d.x) : Mathf.Abs(d.y);
            float norm = Mathf.Clamp01(axisCanvas / Mathf.Max(1f, 拖动瞄准大圆盘半径));
            int k = Mathf.Clamp(Mathf.RoundToInt(norm * maxStep), 1, maxStep);
            foreach (var c in _aimCells)
            {
                int adx = c.x - sel.position.x, ady = c.y - sel.position.y;
                bool onArm = horizontal
                    ? (ady == 0 && adx * cdx == k)
                    : (adx == 0 && ady * cdy == k);
                if (onArm) return c;
            }
            return null;
        }

        /// <summary>指向型拖动锁定（轮盘化推论，2026-09-26 三拍「小圆盘不超大圆盘」——小盘无法位移
        /// 到散布全图的目标，改为**拖向选目标**）：候选目标格的屏幕方向与「轮心→小盘」拖向夹角最小者
        /// 锁定，夹角须≤「拖动瞄准指向锥角」（默认 60°）；精确选择仍可点击式点格。无匹配=无锁定
        /// （松手取消）</summary>
        private BattleCell? FindNearestAimCellByWheelDirection(Vector2 discLocal)
        {
            var canvasRt = CanvasRect;
            if (canvasRt == null || _camera == null) return null;
            Vector2 dir = discLocal - _dragWheelCenterLocal;
            if (dir.sqrMagnitude < 1f) return null;
            dir.Normalize();

            float minCos = Mathf.Cos(拖动瞄准指向锥角 * Mathf.Deg2Rad);
            BattleCell? best = null;
            float bestCos = minCos;
            foreach (var c in _aimCells)
            {
                if (!_camera.TryProjectToScreen(_board.CellToWorld(c), out var screen)) continue;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, screen, null, out var candLocal))
                    continue;
                Vector2 candDir = candLocal - _dragWheelCenterLocal;
                float candLen = candDir.magnitude;
                if (candLen < 1f) continue;
                float cos = Vector2.Dot(dir, candDir / candLen);
                if (cos > bestCos) { bestCos = cos; best = c; }
            }
            return best;
        }

        /// <summary>松手是否落在取消钮/技能盘任一键上（王者荣耀"拖回轮盘中心取消"——拖出后拖回键区
        /// 松手=取消瞄准；同时防微拖误触：不出键区不落待定）</summary>
        private bool ReleaseOverDiscOrCancel(Vector2 screenPos)
        {
            if (_cancelButton != null
                && RectTransformUtility.RectangleContainsScreenPoint(_cancelButton, screenPos, null)) return true;
            foreach (var def in _skillButtons)
                if (def?.rect != null && def.rect.gameObject.activeInHierarchy
                    && RectTransformUtility.RectangleContainsScreenPoint(def.rect, screenPos, null)) return true;
            return false;
        }

        // ==================== 拖动瞄准圆盘（2026-09-26 拍板：王者荣耀式大圆盘+小圆盘） ====================

        /// <summary>圆盘显示：大圆盘锚在被拖技能键圆心（拖动位移的参照原点可视化）、小圆盘随手指。
        /// 素材=项目现成资产复用（拍板纪律）：disc.png 实心圆盘（大=底盘色半透明+小=瞄准已选色金，
        /// 小盘与金色待定格同色系联动）+ circle.png 细环做大圆盘描边（高亮金）；换美术只改
        /// EnsureDragWheel 的 sprite 加载。Image.raycastTarget 全关——盘覆盖技能盘区域不拦点击/拖拽</summary>
        private void ShowDragWheel(SkillButtonDef def, Vector2 pointerScreen)
        {
            EnsureDragWheel();
            if (_dragWheelRoot == null) return;
            _dragWheelRoot.SetActive(true);

            // 大圆盘=技能键圆心（rect 世界角→画布局部；Overlay 画布世界坐标=屏幕像素）——
            // 圆心即拖向/步距转盘原点（拍板三：选中格的精确性全在大圆盘内）
            if (def?.rect != null)
            {
                def.rect.GetWorldCorners(_handCornersBuffer);
                var centerWorld = (_handCornersBuffer[0] + _handCornersBuffer[2]) * 0.5f;
                var local = CanvasRect.InverseTransformPoint(centerWorld);
                _dragWheelCenterLocal = local;
                _dragWheelBigFill.anchoredPosition = local;
                _dragWheelBigRing.anchoredPosition = local;
            }
            else _dragWheelCenterLocal = Vector2.zero;
            UpdateDragWheel(pointerScreen);
        }

        /// <summary>小圆盘跟手（双夹取）：①轮盘界——盘心不超大圆盘半径（拍板「小圆盘不应该超出
        /// 大圆盘范围」）；②屏幕界——盘缘距屏幕边缘至少留「盘半径+边距」（拍板「圆盘不可超出屏幕
        /// 边缘」；轮盘靠屏角时屏幕界优先）。夹取后的盘位 _dragDiscLocal=瞄准解析唯一输入</summary>
        private void UpdateDragWheel(Vector2 pointerScreen)
        {
            var canvasRt = CanvasRect;
            if (_dragWheelRoot == null || !_dragWheelRoot.activeSelf || canvasRt == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, pointerScreen, null, out var local))
                return;

            // 轮盘界：轮心→盘向量夹到盘心距 ≤ 大圆盘半径
            Vector2 d = local - _dragWheelCenterLocal;
            float len = d.magnitude;
            if (len > 拖动瞄准大圆盘半径 && len > 0f)
                local = _dragWheelCenterLocal + d * (拖动瞄准大圆盘半径 / len);

            // 屏幕界：盘缘不出屏（画布 rect=实际屏幕/scaleFactor，画布单位）
            var half = canvasRt.rect.size * 0.5f;
            float margin = 拖动瞄准小圆盘半径 + 拖动瞄准圆盘屏幕边距;
            local.x = Mathf.Clamp(local.x, -half.x + margin, half.x - margin);
            local.y = Mathf.Clamp(local.y, -half.y + margin, half.y - margin);

            _dragWheelSmall.anchoredPosition = local;
            _dragDiscLocal = local;
        }

        /// <summary>圆盘隐藏（松手/会话收口；幂等）</summary>
        private void HideDragWheel()
        {
            if (_dragWheelRoot != null) _dragWheelRoot.SetActive(false);
        }

        /// <summary>圆盘三件懒建（首次拖动起手时建，BattleHud 随战斗实例销毁即回收）</summary>
        private void EnsureDragWheel()
        {
            if (_dragWheelRoot != null || _canvas == null) return;
            var canvasRt = (RectTransform)_canvas.transform;
            _dragWheelRoot = new GameObject("DragAimWheel", typeof(RectTransform));
            var rootRt = (RectTransform)_dragWheelRoot.transform;
            rootRt.SetParent(canvasRt, false);
            rootRt.anchorMin = Vector2.zero; // 全拉伸壳：子件中心锚=画布中心，anchoredPosition 即画布局部坐标
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;
            rootRt.SetAsLastSibling(); // 盘画在 HUD 最上层（仅视觉，无射线）
            _dragWheelRoot.SetActive(false);

            var disc = Resources.Load<Sprite>("UI/Backpack/TabGlyphs/disc");   // 实心白圆盘
            var ring = Resources.Load<Sprite>("UI/Skills/circle");              // 细环
            if (disc == null) GICLog.Warn("[BattleHud] disc.png（TabGlyphs）未找到——拖动圆盘不显示");
            if (ring == null) GICLog.Warn("[BattleHud] circle.png（Skills）未找到——大圆盘描环不显示");
            _dragWheelBigFill = MakeWheelDisc("BigFill", rootRt, disc, 拖动瞄准大圆盘半径 * 实心盘贴图补偿,
                new Color(Palette.按钮底盘.r, Palette.按钮底盘.g, Palette.按钮底盘.b, 0.45f));
            _dragWheelBigRing = MakeWheelDisc("BigRing", rootRt, ring, 拖动瞄准大圆盘半径,
                new Color(Palette.高亮金.r, Palette.高亮金.g, Palette.高亮金.b, 0.8f));
            _dragWheelSmall = MakeWheelDisc("SmallDisc", rootRt, disc, 拖动瞄准小圆盘半径 * 实心盘贴图补偿,
                Palette.瞄准已选色);
        }

        /// <summary>圆盘子件（中心锚；尺寸=半径×2；raycastTarget 恒关）</summary>
        private static RectTransform MakeWheelDisc(string name, RectTransform parent, Sprite sprite,
            float radius, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(radius * 2f, radius * 2f);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false; // 盘覆盖技能盘区，勿拦射线
            return rt;
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

        /// <summary>顶部「完成选择」按钮（2026-09-26 拍板；同日追加拍板「确认行动后就应当定死了」）：
        /// ① 瞄准态有待定金格=确认提交该行动（SubmitAim 内非己方/低级防线拦截时弹 toast 保持瞄准态、
        /// 不算完成选择）；② 其余情况（未选单位/已选未瞄准/瞄准未定格）无论是否已选=以 Pass 完成
        /// 本回合选择。**成功上交即定死**——按钮置灰、再按/超时自动按下均无操作（按已定死的按钮=
        /// 无事发生，超时与手动零差异）；Host 侧同拒绝重复上交双保险。
        /// 多人提前开演口径：真人玩家的「完成选择」=各交一份行动（含 Pass），全员交齐即提前开演
        /// （AI 由脑自动上交，与既有 TryBeginResolve 收齐判定天然契合，无需新协议）</summary>
        private void OnConfirmButtonClicked()
        {
            if (_layoutEditing) return;
            if (_session == null || _session.Flow == null) return;
            if (_session.Flow.Phase != BattlePhase.Selecting) return;
            if (_actionConfirmed) return; // 已定死（按钮置灰，本条=超时自动按下的同款守卫）

            // 瞄准待定确认：提交待定格上的行动（部署/移动/直线/单位指向全在 SubmitAim 单出口）
            if (_state == HudState.Aiming && _pendingAimCell.HasValue)
            {
                var snapshot = _session.Player.LatestSnapshot;
                if (snapshot == null) return;
                var cell = _pendingAimCell.Value;
                var enemyAtCell = FindUnitAt(snapshot, cell, UnitSide.Enemy);
                if (SubmitAim(cell, enemyAtCell)) _actionConfirmed = true; // 防线拦截（false）不定死
                return;
            }

            // 无待定瞄准：完成选择=空过上交——本回合就此定死（再瞄准再确认不再受理）
            _session.SubmitAction(new ActionData
            {
                playerId = _myPlayerId,
                actionType = ActionType.Pass,
            });
            _actionConfirmed = true;
            GICLog.Info($"[BattleHud] {_myPlayerId} 完成选择：空过（已定死）");
            ExitAiming();
            DeselectUnit();
            SetTip("Battle_TipSubmitted");
        }

        /// <summary>倒计时归零=自动按下完成选择（2026-09-26 拍板「统一复用链路」**完全统一版**）：
        /// 无条件复用 OnConfirmButtonClicked，超时与手动按下按钮**零差异**——待定金格确认提交/
        /// 无待定上交空过；**已定死（本回合点过完成选择）则按钮同款无操作**——已确认的行动 A
        /// 天然保留，无需任何特例。事件在 Host 超时 Pass 兜底填充**之前**同步触发——HUD 先交，
        /// Host 兜底只填仍未交的玩家；B7 LAN 分端时 Host 兜底语义不变，客户端迟到上交按超时丢弃。</summary>
        private void OnSelectTimerExpiredHandler(int turn)
        {
            GICLog.Info($"[BattleHud] 倒计时归零：{_myPlayerId} 自动按下完成选择");
            OnConfirmButtonClicked();
        }

        // ==================== 可选格高亮 + 选中标记（世界层） ====================

        private void ShowAimHighlights()
        {
            ClearHighlights();
            if (_highlightRoot == null) return;

            foreach (var cell in _aimCells)
            {
                // 分色（2026-09-23 拍板）：推荐/不推荐（推荐集差集；色值=BattlePalette 两字段）
                var material = _aimRecommendedCells.Contains(cell)
                    ? GetAimRecommendedMaterial()
                    : GetAimNotRecommendedMaterial();
                var quad = BattleViewFactory.CreateQuad(_highlightRoot,
                    $"AimHighlight_{cell.x}_{cell.y}", material);
                quad.transform.position = new Vector3(
                    _board.CellToWorld(cell).x,
                    _board.GetSurfaceHeight(cell) + 0.03f,
                    _board.CellToWorld(cell).z);
                quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                quad.transform.localScale = new Vector3(0.92f, 0.92f, 1f);
                _highlightQuads.Add(quad);
                _aimQuadByCell[cell] = quad.GetComponent<MeshRenderer>(); // 待定金格材质换装索引
            }
        }

        /// <summary>瞄准高亮材质（推荐/不推荐双色；底图=BattleViewFactory.AimCellTexture 白芯+内嵌
        /// 黑边——2026-09-24 拍板加黑边；色值走 palette 可调（2026-09-23 拍板分色），
        /// 每次进瞄准态刷新。懒建单实例复用——曾每格 new Material 且清理只销 quad 不销材质，
        /// 反复进出瞄准态无限累积已修，docs/14 §63）</summary>
        private Material GetAimRecommendedMaterial()
        {
            if (_aimRecommendedMaterial == null)
                _aimRecommendedMaterial = BattleViewFactory.CreateAimCellMaterial(Palette.瞄准推荐色);
            _aimRecommendedMaterial.color = Palette.瞄准推荐色;
            return _aimRecommendedMaterial;
        }

        private Material GetAimNotRecommendedMaterial()
        {
            if (_aimNotRecommendedMaterial == null)
                _aimNotRecommendedMaterial = BattleViewFactory.CreateAimCellMaterial(Palette.瞄准不推荐色);
            _aimNotRecommendedMaterial.color = Palette.瞄准不推荐色;
            return _aimNotRecommendedMaterial;
        }

        // ==================== 瞄准待定金格（2026-09-26 拍板：点格待定+完成选择确认） ====================

        /// <summary>点可选格=金色待定（不立即提交）：旧待定格还原推荐/不推荐色、新格换金色；
        /// 重复点同格=保持不变。换装走 sharedMaterial（换 renderer.material 会产副本泄漏，docs/14 §63）</summary>
        private void SetPendingAimCell(BattleCell cell)
        {
            if (_pendingAimCell.HasValue && _pendingAimCell.Value.Equals(cell)) return;
            RestorePendingCellMaterial();
            _pendingAimCell = cell;
            if (_aimQuadByCell.TryGetValue(cell, out var renderer) && renderer != null)
                renderer.sharedMaterial = GetAimPendingMaterial();
        }

        /// <summary>清待定格并还原材质（拖动瞄准用：拖向移出有效区/无有效瞄准时调用——松手即"无待定=取消"）</summary>
        private void ClearPendingAimCell()
        {
            if (!_pendingAimCell.HasValue) return;
            RestorePendingCellMaterial();
            _pendingAimCell = null;
        }

        /// <summary>待定格还原回推荐/不推荐共享材质（变更待定格/退出瞄准前调用）</summary>
        private void RestorePendingCellMaterial()
        {
            if (!_pendingAimCell.HasValue) return;
            if (_aimQuadByCell.TryGetValue(_pendingAimCell.Value, out var renderer) && renderer != null)
            {
                var material = _aimRecommendedCells.Contains(_pendingAimCell.Value)
                    ? GetAimRecommendedMaterial()
                    : GetAimNotRecommendedMaterial();
                renderer.sharedMaterial = material;
            }
        }

        /// <summary>待定金格材质（色=BattlePalette.瞄准已选色——原神风格金；懒建单实例，
        /// 每次刷新活色，OnDestroy 释放，与推荐/不推荐两材质同生命周期）</summary>
        private Material GetAimPendingMaterial()
        {
            if (_aimPendingMaterial == null)
                _aimPendingMaterial = BattleViewFactory.CreateAimCellMaterial(Palette.瞄准已选色);
            _aimPendingMaterial.color = Palette.瞄准已选色;
            return _aimPendingMaterial;
        }

        private void ClearHighlights()
        {
            foreach (var quad in _highlightQuads)
                if (quad != null) Destroy(quad);
            _highlightQuads.Clear();
            _aimQuadByCell.Clear();
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

        /// <summary>物品配置（B6d：左上角摩拉/体力物品牌计数 chip 的图标数据链——体力/摩拉=物品牌，
        /// 图标走 ItemConfig 单源勿手塞 prefab）</summary>
        [Autowired] private ItemConfig _itemConfig;

        /// <summary>选中角色的配置数据（头像/技能表/元素全在；BattlePlayer 同款注入）</summary>
        private UnitConfig.UnitData GetSelectedUnitData()
        {
            var snapshot = _session.Player.LatestSnapshot;
            var unit = snapshot?.units.FirstOrDefault(u => u.unitId == _selectedUnitId);
            return unit != null ? TryGetUnitData(unit.unitName) : null;
        }

        /// <summary>该技能类型在 UnitData.skills 数组的索引（ActionData.skillIndex 的 Host 侧语义；
        /// 技能独立化后 skills=SkillConfig 引用列表，分拣读 .data）</summary>
        private int GetSelectedSkillIndex(SkillType want)
        {
            var unitData = GetSelectedUnitData();
            if (unitData?.skills == null) return 0;
            for (int i = 0; i < unitData.skills.Count; i++)
                if (unitData.skills[i]?.data.skillType == want) return i;
            return 0;
        }

        /// <summary>按钮 def → 该角色的 SkillData（UnitData.skills 按 def.type 分拣）；
        /// fallback 到首技能是预期语义——3004 号角色设计即无战技、主要靠移动，勿当 bug 修（docs/14 §63④）</summary>
        private SkillConfig.SkillData GetSelectedSkillData(SkillButtonDef def)
        {
            if (def == null) return null;
            var unitData = GetSelectedUnitData();
            if (unitData?.skills == null) return null;
            return unitData.skills.FirstOrDefault(s => s != null && s.data.skillType == def.type)?.data
                ?? unitData.skills.FirstOrDefault(s => s != null)?.data;
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
        /// 元能不足=置灰（B6a：爆发/延奏等 EnergyCost>0 的技能，门槛=技能消耗值，门槛随快照刷新）；
        /// 体力不足=置灰（B6d：战技/爆发消耗 10 体力，docs/05 §5.1——门槛随快照刷新）。
        /// 查看态恒可点（2026-09-26 选中开放任意单位：敌人/低级单位无操控权即无消耗语义，
        /// 元能/体力置灰只约束己方可操控单位——勿把"查看敌人技能盘"也灰掉）</summary>
        private void ApplySkillButton(SkillButtonDef def, UnitConfig.UnitData unitData)
        {
            if (def?.view == null) return;
            var data = GetSelectedSkillData(def);
            def.view.gameObject.SetActive(data != null);
            var toggle = def.view.GetComponent<Toggle>();
            if (toggle != null)
            {
                bool controllable = IsSelectedControllable();
                toggle.interactable = data != null
                    && (!controllable || (HasEnergyForSkill(data) && HasStaminaForSkill(data)));
            }
            if (data == null) return;

            def.view.InitWithData(data, unitData, ViewType.OnlyDisplay, _skillDetailView);

            if (def.nameText != null)
            {
                def.nameText.ClearAllEntries();
                def.nameText.AddEntry(data.skillID.GetEntry());
            }
        }

        /// <summary>选中单位是否可操控=己方高级单位（行动提交门槛，SubmitAim 同口径）；
        /// false=查看态（敌人/低级/无配置）——技能盘与瞄准开放，仅提交被轻提示拦截</summary>
        private bool IsSelectedControllable()
        {
            var snapshot = _session?.Player?.LatestSnapshot;
            var sel = snapshot?.units.FirstOrDefault(u => u.unitId == _selectedUnitId);
            if (sel == null || sel.playerId != _myPlayerId) return false;
            var data = TryGetUnitData(sel.unitName);
            return data != null && data.starLevel >= 3;
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

            var move = unitData.skills?.FirstOrDefault(s => s != null && s.data.skillType == SkillType.Move)?.data;
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

            // 体力置灰（B6d）：移动=配额行动消耗 10 体力，不足置灰——仅约束己方可操控单位
            // （2026-09-26 查看态恒可点，同 ApplySkillButton 口径）
            if (def.view.toggle != null)
                def.view.toggle.interactable = !IsSelectedControllable()
                    || _myStamina >= BattleMetrics.StaminaCostPerAction;
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

        /// <summary>战斗轻弹窗（2026-09-26 报障修正：提交拦截等反馈走 PopupManager toast——
        /// 顶部滑入、可堆叠去重，与 Wish_NoPrimogem/Deck_Full 同款；PopupManager=Boot 建立常驻设施，
        /// 战斗根场景可用。键在 PopupText 表非 UIText，勿写错表）</summary>
        private static void ShowBattleToast(string popupKey)
        {
            if (PopupManager.Instance == null)
            {
                GICLog.Warn("[BattleHud] PopupManager 不在（常驻根未建立？）——轻提示降级不显示");
                return;
            }
            PopupManager.Instance.ShowToast(new LocalizedString(TableName.PopupText.ToString(), popupKey));
        }
    }
}
