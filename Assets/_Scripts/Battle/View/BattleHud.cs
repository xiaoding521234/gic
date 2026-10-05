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
    /// 该格实际能走到；不可选=无提示）+ 右上取消按钮 + 选中单位脚下金色标记与两束队伍色环绕光束。
    /// 选中/瞄准提示特效（2026-09-27 拍板）：技能按钮瞄准态=两束元素色光环绕飞行（旧打钩图
    /// skillSelect 退役不再点亮）；立牌选中=底座圆盘两束队伍主色光环绕（OrbitBeams.cs 两驱动）。
    /// 瞄准提交制（2026-09-26 拍板）：点可选格=金色待定（BattlePalette.瞄准已选色，可点其它格变更、
    /// 点空白取消技能回选中态），确认=顶部「完成选择」按钮——待定格提交行动/无待定空过完成选择
    /// （多人提前开演=各真人交齐一份，AI 由脑自动上交）；倒计时归零=自动按下该按钮（统一复用链路）。
    /// 技能键点击循环（2026-09-27 拍板）：首点=瞄准、同键再点=详情模式（面板自适应摆在键旁）、
    /// 再点=切回瞄准（循环切换）；选中技能后再点其它键=切换到新技能重新瞄准。
    /// 交互状态机：Idle（手牌态）→ UnitSelected（行动态）→ Aiming（瞄准态）+ LayoutEditing（编辑态门控）。
    /// 输入 = BattleCameraController.OnBoardTap（Drag 短点击复合发射，docs/24 §7.10 tap+pan 同体）。
    /// 文案 = TextCombiner 本地化（docs/20 §2；UIText 12000 战斗段）；素材全部复用项目内资产。
    /// 拖动式瞄准已落地（B4 2026-09-26：王者荣耀式手势+待定制——技能键按下拖出→**拖向=瞄准方向**
    /// （轮心→小圆盘位移定方向与距离，与指针落点无关）→金色待定**单格**实时跟随→松手=留待定
    /// （不提交，确认=完成选择按钮），拖回技能盘/取消钮松手=取消；拖动时键上现**圆角矩形大盘**
    /// （距离转盘——盘缘=最远格；2026-09-28 拍板「大圆盘改圆角矩形，贴合格子战场」）、手指处
    /// **小圆盘**（不超大盘、不出屏幕）+金格出屏时相机丝滑移过去
    /// ——见 OnSkillButtonDragBegin/ShowDragWheel）；拖动时被拖技能键**临时挪到盘心**作摇杆底座、
    /// 松手/会话收口还原回槽（2026-09-27 拍板）；协议核心=场上单位（2026-09-29 拍板「核心不需要
    /// 额外显示」——顶栏不加核心血条、myinfo 徽标移除，核心血量读场上头顶条 BattleOverheadBars）。
    /// </summary>
    public partial class BattleHud : MonoBehaviour
    {
        // ==================== 可调参数（编辑器直改；位置/尺寸类随 2026-09-22 prefab 化退役进 prefab） ====================

        // 配色统一走 BattlePalette 配置资产（2026-09-18 统一化批次；接线时活色覆盖烘焙兜底色）
        private static BattlePalette Palette => BattlePalette.Instance;

        [Header("手牌下沉（2026-09-26 拍板：默认沉半张避让视野，鼠标接近热区才上移；2026-10-04 拍板「不需要自动降下了，暂时移除这个功能」=总开关默认关）")]
        [Tooltip("手牌下沉热区总开关——false=手牌恒升起态完全可见（2026-10-04 拍板「暂时移除」）；true=恢复自动下沉/接近上移（下列热区/升降参数随之生效，链路全保留）")]
        [SerializeField] private bool 手牌自动下沉 = false;
        [Tooltip("默认下沉藏量=半张卡（卡高 240 之半，按手牌槽缩放×手牌壳缩放自动换算画布量）")]
        [SerializeField] private float 手牌下沉半卡 = 120f;
        [Tooltip("接近热区：手牌矩形左右外扩余量（画布单位）——2026-10-04 拍板「再收：都改为30」（原 60）")]
        [SerializeField] private float 手牌热区侧探 = 30f;
        [Tooltip("接近热区：手牌矩形向上外扩余量（画布单位）——接近主方向探测带；2026-10-04 拍板「再收：都改为30」（原 100）")]
        [SerializeField] private float 手牌热区上探 = 30f;
        [Tooltip("升/降指数趋近系数（1/s，12≈0.25s 到位）")]
        [SerializeField] private float 手牌升降速度 = 12f;

        [Header("倒计时（2026-09-26 拍板：3 倍字号+黑描边；小于告急秒数=红色+字号持续脉动）")]
        [Tooltip("告急阈值（秒）——剩余时间低于此值进入红色脉动")]
        [SerializeField] private float 倒计时告急秒数 = 5f;
        [Tooltip("告急字号呼吸幅度（基准字号的比例，0.12=±12%）")]
        [SerializeField] private float 倒计时脉动幅度 = 0.12f;
        [Tooltip("告急字号呼吸频率（次/秒）")]
        [SerializeField] private float 倒计时脉动频率 = 1.5f;
        [Tooltip("SDF 原生描边宽度（0~1 相对字形，随视口缩放恒定；迭代链 0.3→0.22→0.15→0.08（2026-09-26 三拍「调细」）；首版 UGUI Outline 固定像素描边在缩放视口下不可见已弃用）")]
        [SerializeField] private float 倒计时描边宽度 = 0.08f;

        [Header("拖动式瞄准（B4，2026-09-26 落地：王者荣耀式手势+待定制——按下拖出，拖向=瞄准方向，松手=留金色待定单格）")]
        [Tooltip("指向型瞄准（延奏/契约）拖向锁定锥角（度）：候选目标屏幕方向与「轮心→小盘」拖向的夹角不超过此值才锁定（轮盘化后小盘无法位移到目标——以拖向选目标，夹角最小者胜；精确选择仍可点击式点格）")]
        [SerializeField] private float 拖动瞄准指向锥角 = 60f;

        [Header("拖动瞄准圆盘（2026-09-26 三拍：大盘=键上锚点+距离转盘、小圆盘不超大盘不出屏幕；选中格精确性全在大盘内——盘缘=最远格；2026-09-28 拍板：大盘=圆角矩形（方形），贴合格子战场）")]
        [Tooltip("大盘半边距（画布单位）——圆角矩形盘中心到边的距离（两轴同值=方形盘）；锚在被拖技能键圆心；方向型步距转盘=盘缘对应该方向最远可选格（半边越大选格越精细）")]
        [UnityEngine.Serialization.FormerlySerializedAs("拖动瞄准大圆盘半径")]
        [SerializeField] private float 拖动瞄准大盘半边 = 238f; // 2026-10-04 拍板「矩形盘缩小30%」：340→238
        [Tooltip("小圆盘半径（画布单位）——手指跟随盘（盘心两轴不超大盘半边、盘缘不出屏幕）；兼作键心死区半径：拖动瞄准中小盘未拖出此半径=未真离键，无瞄准、松手取消（防微拖误触/拖回取消目标）")]
        [SerializeField] private float 拖动瞄准小圆盘半径 = 56f;
        [Tooltip("小圆盘屏幕边距（画布单位）——盘缘距屏幕边缘的最小留白（轮盘靠屏角时屏幕边界优先于轮盘界）")]
        [SerializeField] private float 拖动瞄准圆盘屏幕边距 = 16f;

        [Header("技能详情面板（2026-09-27 拍板：同键再点=详情模式——面板自适应摆在技能键旁，不遮挡该键、不出屏）")]
        [Tooltip("面板与技能键的间隙（画布单位）")]
        [SerializeField] private float 详情面板与按钮间距 = 24f;
        [Tooltip("面板距屏幕边缘的最小留白（画布单位）")]
        [SerializeField] private float 详情面板屏幕边距 = 16f;

        [Header("技能键置灰（2026-10-04 拍板：AI 自主键不透明度 45%；使用条件不足整键变暗——不止图标，含底面/圆环；两源同键可叠加）")]
        [Tooltip("使用条件不足（资源不够等）时整键置暗系数——图标/底板/色环基准色统一乘该值（RGB 乘、alpha 不动=变暗非变透明）；与 AI 自主键 45% 半透明为正交机制，同键命中时视觉叠加")]
        [SerializeField, Range(0f, 1f)] private float 资源不足变暗系数 = 0.6f; // 首版 0.45 目检「太暗」调亮（2026-10-04）

        /// <summary>disc.png 实心盘可见缘只占纹理半宽 0.830（四周透明边距大）——纹理放大 0.998/0.830≈1.202
        /// 补偿：小圆盘可见缘贴齐名义半径。2026-09-28 大盘圆角矩形化改版后**仅小圆盘消费本常量**
        /// （大盘=DragWheelFill/DragWheelRing 裁剪到内容框的圆角矩形素材，补偿恒 1.0）</summary>
        private const float 实心盘贴图补偿 = 1.202f;

        /// <summary>大盘填充内缩比例（相对半边）：填充件与描环两素材角弧不同（对角向有效半径@680 盘
        /// 填充≈56px、描环带≈88px），平齐绘制时四角填充缘会突出金框线外 ~9px（2026-09-28 像素
        /// 探针实测）；680 盘内缩 11 后填充缘全程落在金环带内（唇口/空洞双零——全角度 0.1° 步进
        /// 扫描，干净窗口 10~14 取中）——v7「阴影贴齐描环」契约的圆角矩形版。**素材随盘径等比拉伸**
        /// ——角弧差随盘径线性缩放，内缩量亦须随半边等比（=11/340；2026-10-04 盘径缩小 30% 批
        /// 由常量改比例，盘径再调零漂移）。换美术素材须重扫重定（扫描脚本=.codely-cli/tmp/wheel_rect/sweep.py）</summary>
        private const float 大盘填充内缩比例 = 11f / 340f;

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
            public OrbitBeamsUi orbitFx; // 瞄准态环绕光束（2026-09-27 选中提示特效，懒建随键存留）
            public CanvasGroup tierGateGroup; // 层级门控置灰组（D-5：眷属/伙伴 AI 域键半透明——懒建）
            public bool IsMove => type == SkillType.Move;
            /// <summary>势力技能键（号令入口，D-5 层级门控豁免——伙伴选中可点=号令；docs/active/32 §8）</summary>
            public bool IsFaction => type == SkillType.Enso || type == SkillType.Contract;
        }

        // 高亮（世界层）+ 选中标记
        private Transform _highlightRoot;
        private OrbitBeamsWorld _discOrbit; // 底座圆盘环绕光束（2026-09-27 选中提示特效；金盘同日退役，选中仅此弧光）
        private readonly List<GameObject> _highlightQuads = new List<GameObject>();
        // 世界层运行时材质（单实例缓存，OnDestroy 释放——Destroy 物体不销材质，逐次 new 会累积泄漏）
        private Material _aimRecommendedMaterial;    // 可选且推荐（色=BattlePalette.瞄准推荐色）
        private Material _aimNotRecommendedMaterial; // 可选但不推荐（色=BattlePalette.瞄准不推荐色）
        private Material _aimPendingMaterial;       // 待定金格（色=BattlePalette.瞄准已选色，2026-09-26）
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

        /// <summary>手牌普通物品牌卡引用（统一消耗模型 C-1：ItemName→Card——技能吃物品（酒/苹果）时
        /// OnItemConsumed 事件即时重 Init 刷新对应卡数量角标；货币走 _moraHandCard/_staminaHandCard 既有链）</summary>
        private readonly Dictionary<ItemName, Card> _handItemCards = new Dictionary<ItemName, Card>();

        // 手牌下沉（2026-09-26 拍板：默认沉半张避让视野，鼠标接近热区才上移）：
        // 只动 HandCards.anchoredPosition（槽内件）——hand 槽锚点/布局方案数据零接触
        private RectTransform _handCardsRect;
        /// <summary>当前下沉量（画布单位；-1=未初始化，首帧直接落沉态不播动画）</summary>
        private float _handSinkCanvas = -1f;
        private readonly Vector3[] _handCornersBuffer = new Vector3[4];

        private string _selectedUnitId;
        private SkillButtonDef _aimDef;    // 瞄准中的键（同键点击循环/异键换技能的判定源，2026-09-27）
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

        /// <summary>拖动瞄准松手帧号（松手尾巴点击防线，2026-09-30 报障「很近的地方松手→落格
        /// 变打开详情面板」）：UGUI 抬起时「按压/抬起命中同一 IPointerClickHandler」即补发一次点击
        /// ——被拖键已挪到盘心，近距松手指针常仍压在键矩形内（键半宽 110>盘心死区 56），该尾巴点击
        /// 被 OnSkillButtonClicked 当「同键再点」误开详情面板；与 _dragAiming 双保险覆盖
        /// click/endDrag 执行顺序差异（§103）</summary>
        private int _dragAimEndFrame = -1;

        // 拖动瞄准圆盘（2026-09-26 拍板「和王者一样，技能上显示一个大圆盘，并且手指拖拽位置还有小圆盘；
        // 圆盘不可超出屏幕边缘」+同日三拍「大圆盘太小/小圆盘不超大圆盘/选中格精确性全在大圆盘内」；
        // 2026-09-28 拍板「大盘改圆角矩形，贴合格子战场」）：运行时建在 HUD 画布（非布局件、
        // Image.raycastTarget 全关勿拦截；换美术素材改 EnsureDragWheel 的 sprite 加载即可——
        // 大盘=DragWheelFill/DragWheelRing 圆角矩形素材+小盘=disc.png 实心圆盘）
        private GameObject _dragWheelRoot;
        private RectTransform _dragWheelBigFill;  // 大盘填充（DragWheelFill 圆角矩形×底盘半透明）
        private RectTransform _dragWheelBigRing;  // 大盘描环（DragWheelRing 圆角矩形细环×高亮金）
        private RectTransform _dragWheelSmall;    // 小圆盘（disc.png×瞄准已选色——与金色待定格同色系联动）
        private Vector2 _dragWheelCenterLocal;    // 大盘中心（自适应位：键心沿两轴夹进画布内，画布局部）——格子判定基准/转盘原点
        private Vector2 _dragDiscLocal;           // 小圆盘画布局部（指针贴身、夹在盘内）——瞄准解析唯一输入（对盘心取差=盘上位置）

        // 拖动时临时挪到盘心的技能键（2026-09-27 拍板「拖动式使用技能时，临时把技能按钮移动到新出现的
        // 大圆盘中间位置」）：键心≠盘心（盘心自适应夹取后偏移可达百像素）——拖动会话中把控件临时挪到
        // 盘心作摇杆底座，松手/会话收口经 HideDragWheel 单点还原回槽（还原守卫幂等，只动控件不动槽——
        // 布局方案数据/槽锚点零接触）
        private RectTransform _dragMovedRect;
        private Vector2 _dragMovedOriginalPos;

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
            if (_session != null) return; // 幂等守卫（批7③）：二次 Bind 会双订阅/双 AddListener/双预览实例
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
            _session.Player.OnResourceDelta += OnResourceDeltaHandler; // B6d：摩拉/体力命令增量（快照权威外的即时刷新）
            _session.Player.OnItemConsumed += OnItemConsumedHandler;  // C-1：技能吃物品（酒/苹果）手牌角标即时刷新
            _session.Player.BattleOver += OnBattleOverHandler;         // S10 全灭软停：胜负 Tip
            _session.Flow.OnPhaseChanged += OnPhaseChanged;
            _session.Flow.OnSelectTimerExpired += OnSelectTimerExpiredHandler; // 超时=自动完成选择（统一链路）
            InitExecutionPreview(); // 执行预览（2026-09-29）：TopBar 攻速队列退役后的执行阶段多行行动预览
            if (_camera != null)
                _camera.OnBoardTap += OnBoardTap;
            if (_inputManager != null)
            {
                _inputManager.OnActionTriggered -= OnInputAction; // 幂等：重复 Bind 防重订阅
                _inputManager.OnActionTriggered += OnInputAction; // 快捷键：KeyAction.Confirm（默认空格/回车）→完成选择
            }

            RefreshFromSnapshot(_session.Player.LatestSnapshot);
        }

        /// <summary>输入系统快捷键派发（[Autowired] 注入；Bind 注入后订阅，docs/14 §78 寻址/注入分离）</summary>
        [Autowired] private InputManager _inputManager;

        /// <summary>快捷键动作（2026-09-26 拍板「按下空格=快速按下完成选择按钮」）：
        /// 复用 KeyAction.Confirm 通用确认（默认绑定 空格/回车，设置页可重绑）——战斗场景订阅触发，
        /// 语义全在 OnConfirmButtonClicked（阶段门/布局编辑守卫/定死/待定金格提交/无待定空过，与按钮零差异）</summary>
        private void OnInputAction(GIC.Framework.KeyAction action)
        {
            if (action == KeyAction.Confirm) OnConfirmButtonClicked();
        }

        private void OnDestroy()
        {
            if (_session != null)
            {
                if (_session.Player != null)
                {
                    _session.Player.SnapshotUpdated -= OnSnapshotUpdated;
                    _session.Player.OnResourceDelta -= OnResourceDeltaHandler;
                    _session.Player.OnItemConsumed -= OnItemConsumedHandler;
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
            if (_inputManager != null)
                _inputManager.OnActionTriggered -= OnInputAction;

            // 世界层运行时材质释放（Destroy 物体不销材质，不释放则跨战斗累积）
            if (_aimRecommendedMaterial != null) Destroy(_aimRecommendedMaterial);
            if (_aimNotRecommendedMaterial != null) Destroy(_aimNotRecommendedMaterial);
            if (_aimPendingMaterial != null) Destroy(_aimPendingMaterial);
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

        /// <summary>物品消耗命令（统一消耗模型 C-1）：我方技能吃物品（酒/苹果等）——按 BattlePlayer 已扣的
        /// 本地 handCards 镜像重 Init 对应卡（数量角标即时刷新；条目减尽=下回合快照重建移除，
        /// 本帧卡残留一回合属可接受——Host 权威镜像已在，签名比对到点自然重建）</summary>
        private void OnItemConsumedHandler(string playerId, int itemName, int amount)
        {
            if (playerId != _myPlayerId) return;
            if (!_handItemCards.TryGetValue((ItemName)itemName, out var card) || card == null) return;
            // 镜像数量（BattlePlayer 已扣）：非货币物品牌 count=条目真源——从本地快照读当前值
            var snapshot = _session?.Player?.LatestSnapshot;
            PlayerResourceState myRes = null;
            if (snapshot != null)
            {
                foreach (var r in snapshot.resources)
                {
                    if (r.playerId == _myPlayerId) { myRes = r; break; }
                }
            }
            int count = 0;
            if (myRes != null)
            {
                foreach (var entry in myRes.handCards)
                {
                    if ((CardType)entry.cardType == CardType.Item && entry.value == itemName)
                    {
                        count = entry.count;
                        break;
                    }
                }
            }
            RefreshCurrencyCard(card, (ItemName)itemName, count); // 复用重 Init 链（数量渲染=ItemCardViewStrategy）
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
                // 2026-09-29 执行预览拍板「隐藏掉玩家之前打开的技能或手牌」：技能盘已随 DeselectUnit
                // 收起，手牌随相位隐藏让位中下方预览（CanvasGroup 渐隐勿 SetActive——滚动壳/热区
                // 探测逻辑常驻零中断）；回选择阶段恢复
                SetHandVisible(false);
            }
            else
            {
                _actionConfirmed = false; // 新回合选择阶段开：完成选择定死复位（编辑期也要复位——编辑不挡阶段推进）
                if (!_layoutEditing)
                    SetTip("Battle_TipSelect");
                SetHandVisible(true);
            }
            RefreshFromSnapshot(_session.Player.LatestSnapshot);
        }

        /// <summary>手牌区相位显隐（2026-09-29 执行预览批）：执行/结束阶段隐藏、选择阶段恢复。
        /// CanvasGroup 渐隐（0.2s）+ 射线关闭——手牌滚动壳与 UpdateHandHover 常驻逻辑零中断</summary>
        private void SetHandVisible(bool visible)
        {
            if (_handZone == null) return;
            var group = _handZone.GetComponent<CanvasGroup>();
            if (group == null) group = _handZone.gameObject.AddComponent<CanvasGroup>();
            group.interactable = visible;
            group.blocksRaycasts = visible;
            if (_handFadeRoutine != null) StopCoroutine(_handFadeRoutine);
            var from = group.alpha;
            var target = visible ? 1f : 0f;
            _handFadeRoutine = StartCoroutine(BattleViewTween.Over(0.2f, t =>
            {
                if (group != null) group.alpha = Mathf.Lerp(from, target, t);
            }));
        }

        private Coroutine _handFadeRoutine;

        /// <summary>执行预览组件（2026-10-04 下移贴底+自定义布局批起=Slots/preview 槽内烘焙实例；
        /// 布局编辑模式占位行=BattleExecutionPreview.Show/HideLayoutPlaceholder）</summary>
        private BattleExecutionPreview _executionPreview;

        /// <summary>执行预览装配（2026-09-29 拍板：TopBar 攻速队列退役——执行阶段中下方多行行动预览；
        /// 2026-10-04 下移贴底+自定义布局批：实例烘焙进 BattleHud.prefab 的 Slots/preview 槽〔首子级=
        /// ExecutionPreview 实例〕，运行时只寻址接线，摆位/缩放随布局系统槽锚点走）</summary>
        private void InitExecutionPreview()
        {
            var content = _canvas.transform.Find("Slots/preview")?.GetChild(0);
            var preview = content?.GetComponent<BattleExecutionPreview>();
            if (preview == null)
            {
                GICLog.Warn("[BattleHud] 布局槽 preview 缺执行预览（BattleHud.prefab Slots/preview 首子级应为 ExecutionPreview 实例）——执行预览不显示");
                return;
            }
            _executionPreview = preview;
            preview.Init(_session, _myPlayerId);
        }

        private void Update()
        {
            // 防快捷键双触发：UGUI 选中件把 Space/Enter 当 Submit 重发 onClick（KeyBindingSettingItem 同款坑）——
            // 战斗 HUD 无键盘导航、选中态无用途，每帧清空（2026-09-26 空格快捷键接线）
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
                EventSystem.current.SetSelectedGameObject(null);

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

            // （攻速队列条已退役 2026-09-29——执行阶段改由 BattleExecutionPreview 多行预览接管）

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
            _handItemCards.Clear();
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
                    else if (!isUnit)
                    {
                        // 普通物品牌持有引用（C-1 统一消耗模型）：技能吃物品时即时刷新数量角标
                        _handItemCards[cardId.AsItemName()] = card;
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
                    if (captured.cardType == CardType.Unit) TryDeployOrUpgrade(captured.value);
                    else SetTip("Battle_TipItemCardPending"); // 物品卡使用后续批次接入，不进部署链（货币物品牌同款）
                });
                _handCardButtons.Add(btn);
            }
        }

        /// <summary>出战/升命路由（B8 命座批，docs/05 §5.2+docs/09）：3★+ 同名已在场（含尸体）→
        /// 重复出战=提升命座——直接上交免落点瞄准（不生成新单位）；否则走部署瞄准（1~2★ 重复出战=
        /// 加单位、首战=选格落地，均不变）。满命上交被 Host 拒绝，本端先行轻提示省一次落空。</summary>
        private void TryDeployOrUpgrade(int unitNameValue)
        {
            if (IsConstellationUpgrade(unitNameValue))
            {
                if (UpgradeTargetAtMax(unitNameValue))
                {
                    ShowBattleToast("Battle_ConstellationMax");
                    GICLog.Info($"[BattleHud] {(UnitName)unitNameValue} 已满命，升命拦截");
                    return;
                }
                var action = new ActionData
                {
                    playerId = _myPlayerId,
                    actionType = ActionType.DeployUnit,
                    deployUnitName = unitNameValue,
                };
                _session.SubmitAction(action);
                GICLog.Info($"[BattleHud] {_myPlayerId} 上交：升命 {(UnitName)unitNameValue}");
                _actionConfirmed = true; // 确认即定死（与 SubmitAim 部署同款——Host 已交忽略双保险）
                SetTip("Battle_TipSubmitted");
                DeselectUnit();
                return;
            }
            EnterDeployAim(unitNameValue);
        }

        /// <summary>该卡是否升命目标：3★+ 非建筑、我方（本玩家）场上已有同名单位（含尸体）</summary>
        private bool IsConstellationUpgrade(int unitNameValue)
        {
            var data = _unitConfig != null ? _unitConfig.GetUnitData((UnitName)unitNameValue) : null;
            if (data == null || data.starLevel < 3 || data.unitType == UnitType.Building) return false;
            var snapshot = _session?.Player?.LatestSnapshot;
            if (snapshot == null) return false;
            foreach (var u in snapshot.units)
                if (u.playerId == _myPlayerId && u.unitName == ((UnitName)unitNameValue).ToString())
                    return true;
            return false;
        }

        /// <summary>升命目标是否已满命（本端拦截省落空；Host 侧 ConstellationApplier.MaxConstellation 权威兜底）</summary>
        private bool UpgradeTargetAtMax(int unitNameValue)
        {
            var snapshot = _session?.Player?.LatestSnapshot;
            if (snapshot == null) return false;
            foreach (var u in snapshot.units)
                if (u.playerId == _myPlayerId && u.unitName == ((UnitName)unitNameValue).ToString())
                    return u.constellation >= ConstellationApplier.MaxConstellation;
            return false;
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

        /// <summary>手牌下沉热区轮询+升降动画（每帧 Update 顶部调用）——**2026-10-04 拍板「不需要
        /// 自动降下了，暂时移除这个功能」：手牌自动下沉开关默认关=手牌恒升起态**（开关在「手牌下沉」
        /// 段首位，恢复勾选即复原，本方法其余链路原样保留）：
        /// 指针入热区（HandCards 矩形四向外扩）=全升，出区=沉「半张卡到画布底缘下」；
        /// 下沉量按升起态底缘动态测量——视口比例变化/布局槽拖动缩放全自适应；
        /// 升起态底缘用「现底缘+当前下沉量」恒等重建（防随动画回环漂移）；手牌隐藏期间照常趋沉
        /// （不可见），复显即默认沉态。布局系统安全：只写 HandCards.anchoredPosition，槽锚点不动</summary>
        private void UpdateHandHover()
        {
            if (_handCardsRect == null || _canvas == null) return;
            if (!手牌自动下沉)
            {
                // 功能暂停（2026-10-04 拍板「暂时移除」）：手牌恒升起态——归零一次暂停前写入的
                // 下沉偏移/进度（编辑器热切换守卫；恒等式随之成立，开关恢复 true 从升起位平滑沉下）
                if (_handCardsRect.anchoredPosition != Vector2.zero)
                {
                    _handCardsRect.anchoredPosition = Vector2.zero;
                    _handSinkCanvas = 0f;
                }
                return;
            }
            var canvasRt = (RectTransform)_canvas.transform;

            // 手牌槽缩放（画布量→HandCards 局部量换算；布局缩放 0.6~1.6，防 0 除）
            float slotScale = 1f;
            if (_layoutByKey.TryGetValue("hand", out var handDef) && handDef.slot != null)
                slotScale = Mathf.Max(0.01f, Mathf.Abs(handDef.slot.localScale.y));

            // 手牌壳缩放（2026-10-04 拍板「手牌的大小，缩小30%」：HandCards 烘焙 0.7——卡牌/间距/
            // 字体/点击区整壳等比缩小，pivot 底边=卡底线不动；下沉换算须乘壳缩放，半卡=局部量）
            float handScale = Mathf.Max(0.01f, Mathf.Abs(_handCardsRect.localScale.y));

            // 升起态底缘（距画布底缘，画布单位）：现底缘＋已应用的下沉量（首帧 anchoredPosition 尚为 0
            // 即升起位，恒等式自然成立）；半卡视觉量=120×槽缩放×壳缩放（HandCards 壳 0.7，2026-10-04 手牌缩小批）
            float currentBottom = HandCardsBottomFromCanvasBottom(canvasRt);
            float risenBottom = currentBottom + Mathf.Max(0f, _handSinkCanvas);

            // 热区检测：指针（鼠标/末次触点）在手牌矩形+余量内=接近
            bool risen = _handCardsRect.gameObject.activeInHierarchy && IsPointerNearHand(canvasRt);
            float targetSink = risen ? 0f : risenBottom + 手牌下沉半卡 * slotScale * handScale;

            if (_handSinkCanvas < 0f)
                _handSinkCanvas = targetSink; // 首帧直接落沉态（无升起闪现）
            else
                _handSinkCanvas = Mathf.Lerp(_handSinkCanvas, targetSink,
                    1f - Mathf.Exp(-手牌升降速度 * Time.deltaTime));

            // 应用：局部偏移=画布下沉量/（槽缩放×壳缩放）（双层缩放下局部单位视觉量随缩放）
            _handCardsRect.anchoredPosition = new Vector2(0f, -_handSinkCanvas / (slotScale * handScale));
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
            SetTip("Battle_TipAimDeploy"); // 部署专属提示（批7⑥：原复用方向瞄准键语义不符）
        }

        /// <summary>我方核心位置（部署半径圆心）。协议核心 Unit 化（2026-09-29 拍板）：真源=快照里的
        /// 我方核心单位位置（与 Host GetCorePosition 同源语义）；防御回落=出生区中心代理→我方任一单位位</summary>
        private BattleCell FindMyCorePosition(BattleSnapshot snapshot)
        {
            // 我方协议核心（快照真源；核心免疫位移恒=出生区中心，换的是数据源正确性）
            var core = snapshot?.units.FirstOrDefault(u =>
                u.unitName == UnitName.ProtocolCore.ToString() && u.playerId == _myPlayerId);
            if (core != null) return core.position;

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
            if (!TryPickBoardCell(screenPos, out var cell, out bool inBounds)) return;

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
                    if (inBounds && _aimCells.Contains(cell))
                    {
                        SetPendingAimCell(cell);
                        // 落格轻提示（2026-09-29 追拍）：不可操作技能（敌方/眷属/伙伴非势力）或
                        // 已定死时立即提示「不会执行」，防玩家选了却不知为何没被执行；金格照常显示
                        NotifySelectionBlockedOrConfirmed();
                        return;
                    }
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

        /// <summary>板面点击拾取（视差修正，2026-09-26 报障根因，docs/14 §86）：玩家视觉点击面=
        /// 地块顶面（草顶 TileTopHeight=0.5/水顶更低，瞄准高亮 quad 再抬 0.03），若按 y=0 底面
        /// 平面交点取格，交点沿视线向远端漂 h/tan(射线俯角)≈0.2~0.6 格（越靠屏幕上方射线越平
        /// 漂得越多）——点在推荐格（尤其远处格）上半部会被解析进屏幕上方邻格，邻格不在可选集=
        /// 「点了空白」取消瞄准。修正：先按顶面高度平面交射线初判格；水面格再按其真实表面
        /// 高度交一次收敛（表面高度仅两档，二次即稳）。相机平移抓取仍走 y=0 平面（同面差值恒定）。</summary>
        private bool TryPickBoardCell(Vector2 screenPos, out BattleCell cell, out bool inBounds)
        {
            cell = default;
            inBounds = false;
            if (!_camera.TryGetPlanePoint(screenPos, _board.TileTopHeight, out var world)) return false;
            var first = _board.WorldToCell(world);
            if (_board.Map.HasTile(first.x, first.y))
            {
                var surface = _board.GetSurfaceHeight(first);
                if (surface < _board.TileTopHeight &&
                    _camera.TryGetPlanePoint(screenPos, surface, out var refined))
                    world = refined;
            }
            cell = _board.WorldToCell(world);
            inBounds = _board.Map.HasTile(cell.x, cell.y);
            return true;
        }

        /// <summary>单位侧向（2026-09-25 三轮审查 C2）：Mine=操控权归属（playerId）、
        /// MyTeam/Enemy=阵营判定（TeamType）——三个语义勿再用 playerId 比较敌我</summary>
        private enum UnitSide { Mine, MyTeam, Enemy }

        private UnitState FindUnitAt(BattleSnapshot snapshot, BattleCell cell, UnitSide side,
            bool includeCorpses = false)
        {
            foreach (var u in snapshot.units)
            {
                if (u.isCorpse != 0 && !includeCorpses) continue; // 尸体默认不可选（单位指向型爆发例外——复苏目标）
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

        /// <summary>该按钮技能是否直线型（战技/爆发=十字方向瞄准；延奏/契约/单位指向型爆发=单位指向；
        /// 无目标自施放爆发〔aimMode=None，凛冽轮舞〕=自身格瞄准非直线）</summary>
        private bool IsLineSkill(SkillButtonDef def)
        {
            var data = GetSelectedSkillData(def);
            return data != null
                && data.skillType != SkillType.Interact
                && !data.IsUnitTargeted()
                && !data.IsSelfCast();
        }

        private void ExitAiming()
        {
            if (_state != HudState.Aiming) return;
            _dragAiming = false; // 拖动会话统一收口（松手取消/确认提交/超时/阶段切换同一处清零）
            HideDragWheel();      // 圆盘随会话收口（EndDrag 已隐藏，此处=外部退出安全网，幂等）
            ClosePopup();         // 详情面板随会话收口（2026-09-27 点击循环：详情模式=瞄准+面板并开，
                                  // 任何瞄准退出路径——点非可选格/取消钮/确认提交/阶段切换——面板一并收）
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

        /// <summary>瞄准态视觉反馈：亮/灭对应技能按钮的两束环绕光束（2026-09-27 拍板「让两束光，
        /// 环绕飞行选中的技能按钮」——色=选中单位元素色；旧打钩图 skillSelect 不再点亮，
        /// prefab 节点保留仅供非战斗屏复用）</summary>
        private void SetAimSelectRing(SkillButtonDef def, bool on)
        {
            if (def == null) return;
            if (!on)
            {
                if (def.orbitFx != null) def.orbitFx.gameObject.SetActive(false);
                return;
            }
            if (def.orbitFx == null) def.orbitFx = OrbitBeamsUi.Create(def.rect);
            def.orbitFx.SetColor(SelectedElementColor());
            def.orbitFx.gameObject.SetActive(true);
        }

        /// <summary>选中单位的元素色（技能按钮环绕光束用；与 SkillIconView.InitWithData 底图
        /// 染色同源=ElementFactionConfig.GetElementColor(selfElement)，无配置回退物理灰）</summary>
        private Color SelectedElementColor()
        {
            var unitData = GetSelectedUnitData();
            return ElementFactionConfig.Instance != null
                ? ElementFactionConfig.Instance.GetElementColor(
                    unitData != null ? unitData.selfElement : ElementType.Physical)
                : Palette.高亮金;
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

            // 单位指向型爆发（B-3 ②，芭芭拉闪耀奇迹——时轮 aimMode=TargetUnit 声明）：全图我方任意
            // 单位格**含尸体**（复苏目标——docs/05 §5.4 例外条款：唯一能让尸体站起来的通道）；
            // 全推荐 v1（同延奏/契约口径：目标格即语义本身）
            if (skillData.IsUnitTargeted())
            {
                foreach (var u in snapshot.units)
                {
                    if ((TeamType)u.team != (TeamType)sel.team) continue;
                    if (_board.Map.HasTile(u.position.x, u.position.y))
                    {
                        var c = new BattleCell(u.position.x, u.position.y);
                        _aimCells.Add(c);
                        _aimRecommendedCells.Add(c);
                    }
                }
                return;
            }

            // 无目标自施放爆发（aimMode=None——凛冽轮舞）：目标域=自身格（Buff 施加于自身；
            // 全推荐同单位指向口径——目标格即语义本身；伙伴层级提交时被操控防线拦截，瞄准域仅供查看）
            if (skillData.IsSelfCast())
            {
                var selfCell = new BattleCell(sel.position.x, sel.position.y);
                if (_board.Map.HasTile(selfCell.x, selfCell.y))
                {
                    _aimCells.Add(selfCell);
                    _aimRecommendedCells.Add(selfCell);
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
                // 与友方互不阻挡（UnitConfig.与友方互不阻挡 单字段双向——配置驱动，Host MovementResolver 同口径）
                if (sameTeam && occupantData.blockAllies
                    && !occupantData.与友方互不阻挡
                    && (selfData == null || !selfData.与友方互不阻挡)) return false;
                if (!sameTeam && occupantData.blockEnemies && selfData != null && selfData.blockedByEnemies)
                    return false;
            }
            return true;
        }

        /// <summary>部署落点推荐预判（镜像 DeployUnitExecutor.IsDeployCellValid 碰撞判定链的客户端静态版，
        /// 2026-09-25 拍板「根据碰撞决定」）：地形层（按部署单位常态移动类型）→ 体积绝对层（现有+自身 ≤3，
        /// 最高级不可绕过）→ 阻挡规则层（与格内全部单位互不阻挡才推荐）；碰撞配置读 UnitData——运行时
        /// Buff 修改不可见，属提示非校验，Host 结算兜底。含尸体——尸体保留碰撞。部署单位尚未登场无运行时
        /// 组件——飞行互不阻挡豁免按 UnitData.normalMoveType 配置口径判（2026-09-26 拍板）。</summary>
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
                // 与友方互不阻挡（UnitConfig.与友方互不阻挡 单字段双向——配置驱动，Host DeployUnitExecutor 同口径）
                if (sameTeam && occupantData.blockAllies
                    && !occupantData.与友方互不阻挡
                    && (deployData == null || !deployData.与友方互不阻挡)) return false;
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
            // 行动只能由**自己的高级单位**执行——非己方/眷属/伙伴非势力在此弹 toast 轻提示、保持瞄准态继续查看。
            // 轻提示走 PopupManager.ShowToast（2026-09-26 报障修正：首版用提示条 SetTip 文字切换太隐晦
            // 玩家看不见——顶部滑入 toast 才是项目轻弹窗正主，Wish_NoPrimogem/Deck_Full 同款）；
            // 判定单出口=GetSelectionBlockToastKey（2026-09-29 追拍：点格/拖拽落格时同判定提前提示——
            // 提交时防线保留=双保险）；Host 侧 OnSubmitAction 权威校验仍为第二道，B7 LAN 客户端绕 UI 也进不来）
            var blockKey = GetSelectionBlockToastKey(sel);
            if (blockKey != null)
            {
                GICLog.Info($"[BattleHud] 提交拦截：{sel.unitName} → {blockKey}");
                ShowBattleToast(blockKey);
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
                // 单位指向型爆发=点中格上的我方角色**含尸体**（B-3 ② 复苏目标——芭芭拉闪耀奇迹）；
                // 契约=点中格上的敌方单位（docs/05 §5.3）
                var skillData = GetSelectedSkillData(_aimDef);
                bool enemyTargeting = skillData != null && skillData.skillType == SkillType.Contract;
                bool includeCorpses = skillData != null && skillData.skillType == SkillType.Burst;
                var unitAtCell = enemyTargeting ? enemyAtCell : FindUnitAt(snapshot, cell, UnitSide.MyTeam, includeCorpses);
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

        /// <summary>操控防线判定单出口（2026-09-29 追拍：点格/拖拽落格/提交三触点同判定同文案）：
        /// 返回 null=玩家可操作上交（己方魔神全键/己方伙伴势力技能键=号令入口）；否则返回拦截
        /// toast 键——敌方（含队友）Battle_NotYourUnit / 眷属 Battle_MinorUnit / 伙伴非势力技能
        /// Battle_Autonomous（键均在 PopupText 表）</summary>
        private string GetSelectionBlockToastKey(UnitState sel)
        {
            if (sel.playerId != _myPlayerId) return "Battle_NotYourUnit";
            // 层级=快照 tier 优先（TierOverrideStars 覆盖同源——沙盒层级覆盖经此生效，2026-10-05）；
            // 0=未填回落原星级换算
            var selTier = sel.tier != 0 ? (UnitTier)sel.tier
                : UnitTierHelper.FromStars(TryGetUnitData(sel.unitName)?.starLevel ?? 1);
            if (selTier == UnitTier.Familiar) return "Battle_MinorUnit";
            if (selTier == UnitTier.Companion && (_aimDef == null || !_aimDef.IsFaction)) return "Battle_Autonomous";
            return null;
        }

        /// <summary>待定格产生时刻轻提示（2026-09-29 追拍：玩家不可操作的技能（敌方/眷属/伙伴非势力）
        /// 与已确认定死时，点可选格/拖拽松手落格即提示——不再等到按「完成选择」才弹，防玩家不知道
        /// 自己选了但为何后面没执行。防线提示优先于定死提示（不可操作更根本）；金格照常显示
        /// （视觉选择反馈保留）；提交侧防线不变=双保险。部署瞄准=玩家级行动无防线、仅定死提示</summary>
        private void NotifySelectionBlockedOrConfirmed()
        {
            if (_deployAimUnit != 0)
            {
                if (_actionConfirmed) ShowBattleToast("Battle_ActionConfirmed");
                return;
            }
            var snapshot = _session.Player.LatestSnapshot;
            var sel = snapshot?.units.FirstOrDefault(u => u.unitId == _selectedUnitId);
            if (sel == null) return;
            var blockKey = GetSelectionBlockToastKey(sel);
            if (blockKey != null) { ShowBattleToast(blockKey); return; }
            if (_actionConfirmed) ShowBattleToast("Battle_ActionConfirmed");
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

        // ==================== 技能按钮（点击循环 + 拖动式瞄准） ====================

        /// <summary>技能键点击循环（2026-09-27 拍板「第一次点击为瞄准模式，第二次点击为详情模式，
        /// 再点击则又再次切换回瞄准模式（循环切换）；当选中了一个技能时，再点击其它技能，则切换到
        /// 新点击的技能」——原「首点开详情/再点同键进瞄准」顺序反转）：①UnitSelected 首点=进瞄准
        /// ②Aiming 同键=瞄准↔详情循环（瞄准态保持——高亮/待定金格不动，仅开/收详情面板，面板自适应
        /// 摆键旁）③Aiming 异键=切换到新技能重新瞄准（详情模式下面板随收口关闭）。置灰防线收口在
        /// Button.Press 的 IsInteractable 门（Toggle→SelectButton 改版 2026-09-27：onClick 直连后拦截
        /// 天然生效，docs/14 §63）；移动与其余三键唯一差异=瞄准语义（def.IsMove）。</summary>
        private void OnSkillButtonClicked(SkillButtonDef def)
        {
            if (def == null || _layoutEditing) return; // 编辑期点击让位给拖拽/选框（拖拽板在控件之上）

            // 拖动瞄准松手尾巴（§103）：UGUI 抬起判定=按压/抬起命中同一 IPointerClickHandler 即补发
            // 点击（StandaloneInputModule，且点击先于 endDrag 执行）——被拖键挪到盘心后近距松手指针
            // 仍在键矩形内，尾巴会被本方法当「同键再点」误开详情面板。拖动会话中不可能有真按压
            // （指针全程被按住）——吞掉，落格/取消交 OnSkillButtonDragEnd 松手链处理
            if (_dragAiming || Time.frameCount == _dragAimEndFrame) return;

            // 层级门控键（D-5 置灰+「AI 自主」角标=视觉提示）：**点击/拖拽照常进瞄准**——显示瞄准格
            // =直观看到技能范围、可留待定金格（2026-09-29 用户拍板）；操控权拦截在提交时轻弹窗
            // （SubmitAim 防线：眷属 Battle_MinorUnit / 伙伴非势力技能 Battle_Autonomous / 非己方
            // Battle_NotYourUnit，拦截后保持瞄准态继续查看——C1 同款交互模式）
            if (_state == HudState.Aiming)
            {
                if (_aimDef == def)
                {
                    // 同键循环：瞄准 ↔ 详情（详情模式=瞄准+面板并开，回瞄准=仅收面板）
                    if (PopupOpen) ClosePopup();
                    else ShowSkillPopup(def);
                    return;
                }
                // 异键：切换到新点击的技能（瞄准模式重开，旧详情随收口关闭）
                ExitAiming();
                EnterAiming(def);
                return;
            }

            if (_state == HudState.UnitSelected)
                EnterAiming(def); // 第一次点击=瞄准模式
        }

        // ==================== 拖动式瞄准（B4，2026-09-26 落地；王者荣耀式手势+待定制：docs/18 决策六两方式之拖动） ====================

        /// <summary>拖动式起手（SkillDragForwarder 转发，UGUI 拖拽阈值即起）：按住技能键拖出 → 进瞄准态
        /// 高亮可选格（与点击式共用 EnterAiming/高亮/待定/取消全链）→ 拖动全程金色待定**单格**实时跟随 →
        /// 松手=留待定（**不立即提交**——2026-09-26 拍板「一次选择 1 个格子、松手后不应立即完成选择」，
        /// 与点击式同款，确认唯一入口=「完成选择」按钮）。瞄准=拖向（王者荣耀手势，判定基准=大盘中心
        /// ——盘=标尺，与指针落在棋盘哪里无关、手指不必离开按键区）。其余口径：①详情模式（面板开着）
        /// 直接拖=收面板无缝切换拖动式（2026-09-27 点击循环后 EnterAiming/ExitAiming 统一收口）
        /// ②瞄准中拖另一键=换技能重瞄准（ExitAiming 收口旧选中环/待定后重进）③置灰键（无数据/元能/
        /// 体力门槛）同点击式不可起手 ④部署瞄准（点手牌卡）无选中单位不接管 ⑤相机平移不串扰——
        /// 拖拽起手在 UI 上，GestureHub 门2 拦下，BattleCameraController 的 Drag 识别器全程不见此指针序列。</summary>
        private void OnSkillButtonDragBegin(SkillButtonDef def, PointerEventData eventData)
        {
            if (def == null || _layoutEditing) return;
            if (_session == null || _session.Flow == null) return;
            if (_session.Flow.Phase != BattlePhase.Selecting) return;
            if (_deployAimUnit != 0) return;
            // 置灰防线收口在处理端（拖拽转发件不受 interactable 拦截——Button.interactable 只拦
            // Selectable 自身处理器，转发件须手动检查，docs/14 §63）
            var selectButton = def.view != null ? def.view.GetComponent<SelectButton>() : null;
            if (selectButton != null && !selectButton.interactable) return;
            // 层级门控键（眷属/伙伴 AI 域）可起手拖动瞄准——显示范围+留待定，拦截在提交时轻弹窗
            // （2026-09-29 用户拍板，同点击式口径）

            if (_state == HudState.Aiming) ExitAiming(); // 换技能重瞄准（_dragAiming 随收口清零）
            if (_state != HudState.UnitSelected) return;  // Idle（无选中单位）不响应
            _dragAiming = true;
            EnterAiming(def);
            ShowDragWheel(def, eventData.position);   // 王者荣耀式圆盘（大=键心自适应锚位、小=指针贴身不出盘）
            UpdateDragAimPreview(eventData); // 起手即按盘上位置刷待定（键位≠盘心时起手即有初始待定——判定相对盘心的直接后果）
        }

        /// <summary>拖动中：金色待定单格实时跟随瞄准结果（方向型=拖向臂上第 k 格；指向型=指针附近
        /// 锁定的目标格）；无有效瞄准=清待定（松手取消）</summary>
        private void OnSkillButtonDrag(SkillButtonDef def, PointerEventData eventData)
        {
            if (!_dragAiming) return;
            UpdateDragAimPreview(eventData);
        }

        /// <summary>松手收束（与点击式同款待定制，2026-09-26 拍板「松手后不应该立即完成选择」）：
        /// 有效瞄准=保持金色待定单格与瞄准态，确认走「完成选择」按钮；**拖回键心死区（小圆盘半径内
        /// ——盘拖回键心金色即隐）/取消钮上松手=取消回选中态**（2026-09-26 报障返修：原「技能盘任一
        /// 键矩形」取消区几何上盖住「第 1 格」整条盘距带，短拖松手必判空放——取消区收窄，键矩形不再
        /// 作松手取消判定）；无有效瞄准（键心死区/方向臂空/指向无锁定）松手=取消。终帧以松手位校准
        /// （快甩时松手位与末帧 drag 位差一拍）。
        /// 会话中途被外部收口（超时自动确认/阶段切换经 ExitAiming）时 _dragAiming 已清，此处无为。</summary>
        private void OnSkillButtonDragEnd(SkillButtonDef def, PointerEventData eventData)
        {
            if (!_dragAiming) return;
            _dragAiming = false;
            _dragAimEndFrame = Time.frameCount; // 松手尾巴点击防线同帧戳（_dragAiming 已清，此戳兜底 click/endDrag 异序）
            if (_state != HudState.Aiming) { HideDragWheel(); return; }
            UpdateDragAimPreview(eventData); // 圆盘未收——终帧校准与拖动中同用夹取指针（屏缘一致）
            HideDragWheel(); // 手指已离键——圆盘随会话收（留待定路径也隐藏）
            if (ReleaseOverCancelButton(eventData.position) || !_pendingAimCell.HasValue)
                ExitAiming();
            // 有效待定：保持金色待定+瞄准态——提交唯一入口=完成选择按钮
            // 落格轻提示（2026-09-29 追拍）：不可操作技能（敌方/眷属/伙伴非势力）或已定死时
            // 立即提示「不会执行」——与点击式同判定同文案（NotifySelectionBlockedOrConfirmed 单出口）
            else
                NotifySelectionBlockedOrConfirmed();
        }

        /// <summary>拖动瞄准实时解析（单格）：判定基准=**大盘中心**——方向型（移动/直线）=盘心→小盘
        /// 位移定十字方向+盘距比例定步数（盘缘=该方向最远可选格、死区缘=第 1 格）；指向型（延奏/契约）=
        /// 拖向选目标（候选屏幕方向（相对盘心）与拖向夹角最小且≤锥角者锁定）。输入=_dragDiscLocal
        /// （小盘位=盘上位置）。无有效瞄准=清待定</summary>
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

        /// <summary>方向型拖动瞄准解析（轮盘内单格）：**判定基准=大盘中心**（2026-09-26 三拍
        /// 「拖动的格子判定应当是相对于大圆盘中心」）——小盘在盘上的位置=瞄准真值，盘=标尺：
        /// 十字方向=盘心→小盘位移的轴主导量化（相机 yaw 恒 0，画布轴向=世界轴向——不再反投影）；
        /// 步数=盘距越过盘心死区后的比例×臂长（**盘缘=该方向最远可选格、死区缘=第 1 格**，四舍五入
        /// 钳 1..臂长）——大盘=距离转盘（2026-09-28 起为圆角矩形），盘越大选格越精细；小盘被夹在盘内，拖到盘缘=该方向拖满
        /// （盘已自适应入屏，盘上任一位置鼠标可达）。臂步 1..maxStep 连续由 ComputeAimCells 保证
        /// （遇虚空截断）。盘心死区内（小盘未拖出盘心圈）/无臂=null。
        /// 键心死区带拍板=2026-09-26 报障返修「只拖最近的1格松开判定我空放」（docs/14 §87）。</summary>
        private BattleCell? ComputeDragAimCellFromWheel(Vector2 discLocal)
        {
            Vector2 d = discLocal - _dragWheelCenterLocal;
            if (d.sqrMagnitude < 拖动瞄准小圆盘半径 * 拖动瞄准小圆盘半径) return null; // 盘心死区
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

            // 盘距→步数：死区缘=第 1 格、盘缘=最远格（大盘半边=全臂程——方形盘两轴同值；
            // 小盘夹在盘内，拖到盘缘=该方向拖满——盘已自适应入屏）
            float axisCanvas = horizontal ? Mathf.Abs(d.x) : Mathf.Abs(d.y);
            float span = Mathf.Max(1f, 拖动瞄准大盘半边 - 拖动瞄准小圆盘半径);
            float norm = Mathf.Clamp01((axisCanvas - 拖动瞄准小圆盘半径) / span);
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

        /// <summary>指向型拖动锁定：候选目标格的屏幕方向（相对**盘心**——与拖向同基准）与
        /// 「盘心→小盘」拖向夹角最小者锁定，夹角须≤「拖动瞄准指向锥角」（默认 60°）；
        /// 精确选择仍可点击式点格。无匹配=无锁定（松手取消）</summary>
        private BattleCell? FindNearestAimCellByWheelDirection(Vector2 discLocal)
        {
            var canvasRt = CanvasRect;
            if (canvasRt == null || _camera == null) return null;
            Vector2 dir = discLocal - _dragWheelCenterLocal;
            if (dir.sqrMagnitude < 拖动瞄准小圆盘半径 * 拖动瞄准小圆盘半径) return null; // 盘心死区（同方向型，2026-09-26 返修）
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

        /// <summary>松手是否落在取消钮上（2026-09-26 报障返修：原「技能盘任一键矩形」取消区已删——
        /// 键槽 220×220 矩形（半宽 110）几何上盖住「第 1 格」整条盘距带（1 格带 0~102px），右侧键簇
        /// （skill↔burst 仅隔 39.6px）连 2 格带也被盖，短拖松手必判「拖回键区」空放。「拖回取消」
        /// 语义收窄为：键心死区（小圆盘半径内=解析返回 null 走 !pending 取消）+ 取消钮。
        /// 批7 复审改名 ReleaseOverDiscOrCancel→ReleaseOverCancelButton（名实对齐——盘不再参与判定）</summary>
        private bool ReleaseOverCancelButton(Vector2 screenPos)
        {
            return _cancelButton != null
                && RectTransformUtility.RectangleContainsScreenPoint(_cancelButton, screenPos, null);
        }

        // ==================== 拖动瞄准圆盘（2026-09-26 拍板：王者荣耀式大圆盘+小圆盘） ====================

        /// <summary>圆盘显示：大盘中心=被拖技能键圆心**沿两轴夹进画布内**（自适应位，
        /// 2026-09-26 拍板「大圆盘应当自适应位置，让自己不会超出屏幕」——盘+小盘屏幕余量全入屏，
        /// 靠边键的盘不再挂出屏外；**格子判定基准=盘心**（三拍「拖动的格子判定应当是相对于大圆盘中心」
        /// ——盘=标尺，小盘在盘上的位置=瞄准真值）、小圆盘=指针贴身且不出盘（三拍「小圆盘不可超出
        /// 大圆盘」）。素材（2026-09-28 大盘圆角矩形化拍板「贴合格子战场」）：大盘=DragWheelFill/
        /// DragWheelRing 圆角矩形 AI 素材（裁剪到内容框，贴图补偿恒 1.0）+小盘=disc.png 实心圆盘
        /// （瞄准已选色金，与金色待定格同色系联动）；换美术只改 EnsureDragWheel 的 sprite 加载。
        /// Image.raycastTarget 全关——盘覆盖技能盘区域不拦点击/拖拽</summary>
        private void ShowDragWheel(SkillButtonDef def, Vector2 pointerScreen)
        {
            EnsureDragWheel();
            if (_dragWheelRoot == null) return;
            _dragWheelRoot.SetActive(true);

            // 大盘中心=技能键圆心（rect 世界角→画布局部；Overlay 画布世界坐标=屏幕像素）→
            // 自适应夹取：盘心沿两轴夹进 [盘半边+小盘半径+屏幕边距] 画布内（方形盘两轴同值，
            // 盘上任一点皆在屏内可拖到）
            if (def?.rect != null)
            {
                def.rect.GetWorldCorners(_handCornersBuffer);
                var centerWorld = (_handCornersBuffer[0] + _handCornersBuffer[2]) * 0.5f;
                var keyCenter = (Vector2)CanvasRect.InverseTransformPoint(centerWorld);
                var rect = CanvasRect.rect;
                float fit = 拖动瞄准大盘半边 + 拖动瞄准小圆盘半径 + 拖动瞄准圆盘屏幕边距;
                float loX = rect.xMin + fit, hiX = rect.xMax - fit;
                float loY = rect.yMin + fit, hiY = rect.yMax - fit;
                // 画布过小（fit 装不下）时回退画布中心，勿让 Clamp 反转
                var local = new Vector2(
                    hiX > loX ? Mathf.Clamp(keyCenter.x, loX, hiX) : (rect.xMin + rect.xMax) * 0.5f,
                    hiY > loY ? Mathf.Clamp(keyCenter.y, loY, hiY) : (rect.yMin + rect.yMax) * 0.5f);
                _dragWheelCenterLocal = local;
                _dragWheelBigFill.anchoredPosition = local;
                _dragWheelBigRing.anchoredPosition = local;
                MoveDragButtonToWheelCenter(def); // 技能键临时挪到盘心作摇杆底座（2026-09-27 拍板）
            }
            else _dragWheelCenterLocal = Vector2.zero;
            UpdateDragWheel(pointerScreen);
        }

        /// <summary>被拖技能键临时挪到大盘中心（2026-09-27 拍板「临时把技能按钮移动到新出现的大圆盘
        /// 中间位置」）：盘心先按键心算好并夹进画布（ShowDragWheel 主体），再把键控件中心对齐盘心——
        /// 键与盘同心=王者式摇杆底座（环绕光束挂键上随动、小盘贴指针绕键转）。只写控件 anchoredPosition
        /// （父级=布局槽，位移经坐标系两跳换算，槽缩放无关）；还原守卫=HideDragWheel 单点收口</summary>
        private void MoveDragButtonToWheelCenter(SkillButtonDef def)
        {
            RestoreDragMovedButton(); // 幂等：上一会话残位先还原（正常路径已随 HideDragWheel 还原）
            if (def?.rect == null) return;
            var canvasRt = CanvasRect;
            if (canvasRt == null) return;
            def.rect.GetWorldCorners(_handCornersBuffer);
            var centerWorld = (_handCornersBuffer[0] + _handCornersBuffer[2]) * 0.5f;
            var targetWorld = canvasRt.TransformPoint((Vector3)_dragWheelCenterLocal);
            var deltaLocal = def.rect.parent.InverseTransformVector(targetWorld - centerWorld);
            _dragMovedRect = def.rect;
            _dragMovedOriginalPos = def.rect.anchoredPosition;
            def.rect.anchoredPosition = _dragMovedOriginalPos + (Vector2)deltaLocal;
        }

        /// <summary>还原临时挪位的技能键（幂等；盘隐藏随会话收口同点调用）</summary>
        private void RestoreDragMovedButton()
        {
            if (_dragMovedRect == null) return;
            _dragMovedRect.anchoredPosition = _dragMovedOriginalPos;
            _dragMovedRect = null;
        }

        /// <summary>小圆盘=指针贴身且**不出大盘**（2026-09-26 三拍「小圆盘不可超出大圆盘」；2026-09-28
        /// 大盘圆角矩形化后改两轴夹取）：盘位=指针画布局部，小盘中心两轴位移各夹到≤盘半边——
        /// 拖出盘范围时小盘贴矩形盘缘（盘缘=最远格，拖到头即满）。
        /// 判定基准=大盘中心（解析函数对 _dragWheelCenterLocal 取差）——盘=标尺，盘上位置=瞄准真值。
        /// 屏幕界夹取不需要（盘已自适应入屏，小盘在盘内必在屏内）。</summary>
        private void UpdateDragWheel(Vector2 pointerScreen)
        {
            var canvasRt = CanvasRect;
            if (_dragWheelRoot == null || !_dragWheelRoot.activeSelf || canvasRt == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, pointerScreen, null, out var local))
                return;

            // 轮盘界：小盘不超大盘（拍板「小圆盘不可超出大圆盘」——大盘=圆角矩形，按两轴半边夹取，
            // 圆形径向夹取随 2026-09-28 矩形化退役）：小盘中心贴到矩形边界=该轴向拖满
            Vector2 d = local - _dragWheelCenterLocal;
            d.x = Mathf.Clamp(d.x, -拖动瞄准大盘半边, 拖动瞄准大盘半边);
            d.y = Mathf.Clamp(d.y, -拖动瞄准大盘半边, 拖动瞄准大盘半边);
            local = _dragWheelCenterLocal + d;

            _dragDiscLocal = local;
            _dragWheelSmall.anchoredPosition = local;
        }

        /// <summary>圆盘隐藏（松手/会话收口；幂等）——临时挪到盘心的技能键随盘一并还原回槽</summary>
        private void HideDragWheel()
        {
            if (_dragWheelRoot != null) _dragWheelRoot.SetActive(false);
            RestoreDragMovedButton();
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

            var disc = Resources.Load<Sprite>("UI/Backpack/TabGlyphs/disc");   // 实心白圆盘（小盘）
            var fill = Resources.Load<Sprite>("UI/Battle/DragWheelFill");      // 圆角矩形实心盘（大盘填充）
            var ring = Resources.Load<Sprite>("UI/Battle/DragWheelRing");      // 圆角矩形细环（大盘描边）
            if (disc == null) GICLog.Warn("[BattleHud] disc.png（TabGlyphs）未找到——小圆盘不显示");
            if (fill == null) GICLog.Warn("[BattleHud] DragWheelFill.png（UI/Battle）未找到——大盘填充不显示");
            if (ring == null) GICLog.Warn("[BattleHud] DragWheelRing.png（UI/Battle）未找到——大盘描环不显示");
            // 大盘两件=裁剪到内容框的圆角矩形素材（补偿恒 1.0——实心盘贴图补偿仅小盘消费）；
            // 填充件按比例内缩见「大盘填充内缩比例」常量注（两素材角弧不同，平齐绘制四角有暗唇）
            _dragWheelBigFill = MakeWheelDisc("BigFill", rootRt, fill, 拖动瞄准大盘半边 * (1f - 大盘填充内缩比例),
                new Color(Palette.按钮底盘.r, Palette.按钮底盘.g, Palette.按钮底盘.b, 0.45f));
            _dragWheelBigRing = MakeWheelDisc("BigRing", rootRt, ring, 拖动瞄准大盘半边,
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

        /// <summary>开详情面板（详情模式，2026-09-27 点击循环二击）：InitWithData 先行（RefreshLayout
        /// 后面板尺寸=内容终态）→ 自适应摆到技能键旁 → OpenPanel 滑入（滑入目标已随摆位重定）</summary>
        private void ShowSkillPopup(SkillButtonDef def)
        {
            if (_skillDetailView == null) return;

            // 走现有技能详情体系：UnitData.skills 的 SkillData → SkillDetailView（图标/类型/名称/描述/参数全本地化）
            var skillData = GetSelectedSkillData(def);
            var unitData = GetSelectedUnitData();
            if (skillData == null || unitData == null) return;

            _skillDetailView.skillDetailPanel.SetActive(true);
            _skillDetailView.InitWithData(skillData, unitData, null);
            PositionSkillPopupBesideButton(def);
            _skillDetailView.OpenPanel();
        }

        /// <summary>详情面板自适应摆位（2026-09-27 拍板「弹出的详情面板应当出现在该技能的旁边——
        /// 不得遮挡该技能按钮、不得超出屏幕、需要灵活的自适应位置」）：候选=键四侧（上下左右按键位
        /// 定偏好序：键在下半=上侧优先、在右半=左侧优先）×三种横轴对齐（键心/键近缘/键远缘），
        /// 逐候选夹进画布 → 源键交叠=硬否决（键必须保持可点——点击循环依赖它）、其余可见件
        /// （技能键/取消/完成选择）计软交叠；取零交叠的偏好序最早候选，全候选取软交叠最少者，
        /// 再无则键位夹画布兜底。落位=面板中心位移（对锚点体系无关，RepositionPanel 同步滑入目标，
        /// RelatedPanel 为面板子件随动无需另摆）</summary>
        private void PositionSkillPopupBesideButton(SkillButtonDef def)
        {
            var canvasRt = CanvasRect;
            var panelRect = _skillDetailView != null && _skillDetailView.skillDetailPanel != null
                ? _skillDetailView.skillDetailPanel.GetComponent<RectTransform>()
                : null;
            if (canvasRt == null || panelRect == null || def == null || def.rect == null) return;
            if (!RectToCanvasAabb(def.rect, out var keyMin, out var keyMax)) return;
            if (!RectToCanvasAabb(panelRect, out var panelMin, out var panelMax)) return;

            var rect = canvasRt.rect;
            Vector2 keyCenter = (keyMin + keyMax) * 0.5f;
            Vector2 half = (panelMax - panelMin) * 0.5f;
            Vector2 panelCenter = (panelMin + panelMax) * 0.5f;

            // 软避让件：其余可见技能键+取消钮+完成选择（能躲则躲，躲不开允许盖；源键=硬避让）
            var softRects = new List<(Vector2 min, Vector2 max)>();
            foreach (var b in _skillButtons)
            {
                if (b == def || b.rect == null || !b.rect.gameObject.activeInHierarchy) continue;
                if (RectToCanvasAabb(b.rect, out var bMin, out var bMax)) softRects.Add((bMin, bMax));
            }
            if (_cancelButton != null && _cancelButton.gameObject.activeInHierarchy
                && RectToCanvasAabb(_cancelButton, out var cMin, out var cMax))
                softRects.Add((cMin, cMax));
            if (_confirmButton != null && _confirmButton.gameObject.activeInHierarchy
                && RectToCanvasAabb((RectTransform)_confirmButton.transform, out var fMin, out var fMax))
                softRects.Add((fMin, fMax));

            bool upFirst = keyCenter.y < rect.center.y;
            bool leftFirst = keyCenter.x >= rect.center.x;
            var sides = new[]
            {
                upFirst ? Vector2.up : Vector2.down,
                leftFirst ? Vector2.left : Vector2.right,
                leftFirst ? Vector2.right : Vector2.left,
                upFirst ? Vector2.down : Vector2.up,
            };

            Vector2? chosen = null;      // 零交叠候选（偏好序最早）
            Vector2? softChoice = null;  // 源键安全但盖了软件的候选（软交叠最少）
            int softBest = int.MaxValue;
            foreach (var side in sides)
            {
                bool vertical = side.y != 0f;
                for (int align = 0; align < 3 && chosen == null; align++)
                {
                    // 侧向：近缘+间距+面板半尺寸；横轴：键心/键近缘/键远缘三对齐
                    Vector2 center = keyCenter;
                    if (vertical)
                    {
                        center.y = side.y > 0f ? keyMax.y + 详情面板与按钮间距 + half.y
                                                : keyMin.y - 详情面板与按钮间距 - half.y;
                        center.x = align == 0 ? keyCenter.x
                                 : align == 1 ? keyMin.x + half.x
                                 : keyMax.x - half.x;
                    }
                    else
                    {
                        center.x = side.x > 0f ? keyMax.x + 详情面板与按钮间距 + half.x
                                               : keyMin.x - 详情面板与按钮间距 - half.x;
                        center.y = align == 0 ? keyCenter.y
                                 : align == 1 ? keyMin.y + half.y
                                 : keyMax.y - half.y;
                    }
                    center = ClampPanelCenter(center, half, rect);

                    if (OverlapsRect(center, half, keyMin, keyMax)) continue; // 硬：不遮挡该技能按钮
                    int softCount = 0;
                    foreach (var s in softRects)
                        if (OverlapsRect(center, half, s.min, s.max)) softCount++;
                    if (softCount == 0) { chosen = center; break; }
                    if (softCount < softBest) { softBest = softCount; softChoice = center; }
                }
                if (chosen != null) break;
            }

            var target = chosen ?? softChoice ?? ClampPanelCenter(keyCenter, half, rect); // 兜底=键位夹画布

            // 中心位移 → anchoredPosition 位移（TransformVector 两跳换父级坐标系，锚点/缩放无关）
            var worldDelta = canvasRt.TransformVector((Vector3)(target - panelCenter));
            var parentDelta = (Vector2)panelRect.parent.InverseTransformVector(worldDelta);
            _skillDetailView.RepositionPanel(panelRect.anchoredPosition + parentDelta);
        }

        /// <summary>面板中心夹进画布（留屏幕边距；画布装不下整面板的轴回退画布中心）</summary>
        private Vector2 ClampPanelCenter(Vector2 center, Vector2 half, Rect canvas)
        {
            float loX = canvas.xMin + 详情面板屏幕边距 + half.x, hiX = canvas.xMax - 详情面板屏幕边距 - half.x;
            float loY = canvas.yMin + 详情面板屏幕边距 + half.y, hiY = canvas.yMax - 详情面板屏幕边距 - half.y;
            return new Vector2(
                hiX > loX ? Mathf.Clamp(center.x, loX, hiX) : canvas.center.x,
                hiY > loY ? Mathf.Clamp(center.y, loY, hiY) : canvas.center.y);
        }

        private static bool OverlapsRect(Vector2 center, Vector2 half, Vector2 min, Vector2 max)
        {
            return center.x + half.x > min.x && center.x - half.x < max.x
                && center.y + half.y > min.y && center.y - half.y < max.y;
        }

        /// <summary>rt 当前世界矩形 → 画布局部 AABB（Overlay 画布世界坐标=屏幕像素、局部原点=画布中心
        /// ——与拖动圆盘同一坐标系；角序 [0]=左下 [2]=右上，UI 无旋转恒成立）</summary>
        private bool RectToCanvasAabb(RectTransform rt, out Vector2 min, out Vector2 max)
        {
            min = max = Vector2.zero;
            var canvasRt = CanvasRect;
            if (rt == null || canvasRt == null) return false;
            rt.GetWorldCorners(_handCornersBuffer);
            min = (Vector2)canvasRt.InverseTransformPoint(_handCornersBuffer[0]);
            max = (Vector2)canvasRt.InverseTransformPoint(_handCornersBuffer[2]);
            return true;
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
                    _board.GetDecalHeight(cell, 0.03f), // 水格含波峰带防高亮被波峰盖过（docs/14 §89 第四轮）
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

        /// <summary>选中单位脚下两束队伍色环绕彗尾弧光（2026-09-27 拍板「底座圆盘也要」；
        /// 同日验证后追加拍板：金盘退役——底座圆盘本体不替换，选中仅额外加弧光。
        /// 选中态/瞄准态常显，色=该单位所属玩家队伍主色（与底座圆盘同源 BattlePlayer 口径）</summary>
        private void ShowSelectMarker(string unitId)
        {
            var snapshot = _session.Player.LatestSnapshot;
            var unit = snapshot?.units.FirstOrDefault(u => u.unitId == unitId);
            if (unit == null) { HideSelectMarker(); return; }

            var cellWorld = _board.CellToWorld(unit.position);
            // 环绕光束：贴片高度略高于瞄准格（波峰带之上同链），色=队伍主色（TeamType.A=我方/B=敌方）
            if (_discOrbit == null && _highlightRoot != null)
                _discOrbit = OrbitBeamsWorld.Create(_highlightRoot);
            if (_discOrbit != null)
            {
                _discOrbit.Setup(
                    new Vector3(cellWorld.x, _board.GetDecalHeight(unit.position, 0.045f), cellWorld.z),
                    (TeamType)unit.team == TeamType.B ? Palette.敌方主色 : Palette.我方主色,
                    unit.cylinderDiameter); // per-unit 盘径贴紧（协议核心批：0.8 大盘弧光同步外扩）
                _discOrbit.gameObject.SetActive(true); // 复用件重显（2026-09-27 报障返修：件缓存战斗期复用，
                                                       // HideSelectMarker 收起后再次选中须重激活——原版漏此行，
                                                       // 首次选中（Create 即 active）可见、第二次起永远隐形）
            }
        }

        private void HideSelectMarker()
        {
            if (_discOrbit != null) _discOrbit.gameObject.SetActive(false);
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
        /// 图标/元素色环/主动被动色全走 SkillIconView.InitWithData 现有链。
        /// 建筑例外（协议核心批 2026-09-29 拍板「选中=纯查看」）：建筑无任何行动——全部键隐藏
        /// （含移动键——ApplyMoveButton 对无 Move 条目单位恒显示，建筑勿走该兜底）</summary>
        private void RefreshSkillButtons()
        {
            var unitData = GetSelectedUnitData();
            if (unitData == null) return;

            if (unitData.unitType == UnitType.Building)
            {
                foreach (var def in _skillButtons)
                    if (def.view != null) def.view.gameObject.SetActive(false);
                return;
            }

            foreach (var def in _skillButtons)
            {
                if (def.IsMove) ApplyMoveButton(def, unitData);
                else ApplySkillButton(def, unitData);
            }
        }

        /// <summary>技能按钮刷新（非移动键）：数据分拣→InitWithData 现有链（图标白底不染+底图染亮元素色+
        /// 主动/被动色环，2026-09-10 拍板规则全在 SkillIconView 内）；无数据=隐藏+置灰（原延奏特例泛化全键）。
        /// 置灰两源（D-5 置灰单源 docs/active/32 §8；2026-10-04 拍板两源视觉独立可叠加）：
        /// ①「AI 自主」层级门控——眷属/伙伴（任意归属，含敌方——其行动域同属 AI）的移动/战技/爆发键=
        ///   AI 域：45% 半透明（CanvasGroup）；**interactable 保持 true**（点击/拖拽照常进瞄准——显示
        ///   瞄准格直观看到技能范围、可留待定金格；操控权拦截在提交时轻弹窗=SubmitAim 防线，
        ///   2026-09-29 拍板）；
        /// ②「资源不足」（HasSkillResources 门槛，元能/体力=消耗值随快照刷新，体力按层级换算镜像）——
        ///   玩家域键（己方魔神全键/己方伙伴势力键）=interactable 拦截+**整键变暗**（图标/底板/圆环
        ///   统一乘暗系数——2026-10-04 拍板「不止图标」）；**己方眷属/伙伴的 AI 域键同样整键置暗**
        ///   （AI 当前也用不了=信息层，不拦 interactable 保持可查看）——与 45% 半透明同键叠加；
        ///   查看态（敌方单位）无消耗语义不灰（拍板不变）</summary>
        private void ApplySkillButton(SkillButtonDef def, UnitConfig.UnitData unitData)
        {
            if (def?.view == null) return;
            var data = GetSelectedSkillData(def);
            def.view.gameObject.SetActive(data != null);
            bool tierGated = data != null && IsTierGatedButton(def);
            ApplyTierGateVisual(def, tierGated);
            bool conditionDimmed = false;
            var selectButton = def.view.GetComponent<SelectButton>();
            if (selectButton != null)
            {
                if (tierGated)
                {
                    selectButton.interactable = true; // 门控置灰=纯视觉提示——点击/拖拽照常进瞄准查看（拦截在提交时）
                    conditionDimmed = IsSelectedOwnUnit() && !HasSkillResources(data); // 己方 AI 域键资源不足=同置暗（信息层，可叠加）
                }
                else
                {
                    bool playerDomain = IsSelectedPlayerDomain(def.type);
                    bool resourcesOk = !playerDomain || HasSkillResources(data);
                    selectButton.interactable = data != null && resourcesOk;
                    conditionDimmed = data != null && !resourcesOk;
                }
            }
            if (data == null) return;

            def.view.InitWithData(data, unitData, ViewType.OnlyDisplay, _skillDetailView);
            def.view.SetConditionDimmed(conditionDimmed, 资源不足变暗系数); // 须在 InitWithData 后（基准色随刷新重写入）

            if (def.nameText != null)
            {
                def.nameText.ClearAllEntries();
                def.nameText.AddEntry(data.skillID.GetEntry());
            }
        }

        /// <summary>该技能键是否受层级门控（D-5，docs/active/32 §8）：眷属/伙伴的 移动/战技/爆发 键=
        /// AI 域（置灰+角标）；势力技能键（延奏/契约）豁免=号令入口对伙伴开放；魔神选中恒不门控（全亮）</summary>
        private bool IsTierGatedButton(SkillButtonDef def)
        {
            if (def == null || def.IsFaction) return false;
            var tier = SelectedUnitTier();
            return tier == UnitTier.Familiar || tier == UnitTier.Companion;
        }

        /// <summary>层级门控视觉：半透明置灰（懒建 CanvasGroup；不透明度 45%——2026-10-04 拍板
        /// 「AI自主的技能，不透明度为45」（迭代链 0.55→0.5→0.45）；「AI 自主」文字角标已按用户拍板
        /// 2026-09-29 移除——置灰本身+提交时轻弹窗已足够传达，prefab AutoBadge 节点同步删除）</summary>
        private void ApplyTierGateVisual(SkillButtonDef def, bool gated)
        {
            if (def?.view == null) return;
            if (def.tierGateGroup == null)
                def.tierGateGroup = def.view.GetComponent<CanvasGroup>();
            if (def.tierGateGroup == null)
                def.tierGateGroup = def.view.gameObject.AddComponent<CanvasGroup>();
            def.tierGateGroup.alpha = gated ? 0.45f : 1f;
        }

        /// <summary>选中单位层级（D 批次操控分层，docs/active/32 §2；无配置数据按眷属档=保守全门控）。
        /// 快照 tier 优先（2026-10-05 试招沙盒：Unit.TierOverrideStars 覆盖经 BuildUnitState 进快照——
        /// 读原星会让沙盒的层级覆盖在客户端门控失效=战技被误拦「伙伴技能自主」）；0=未填回落原星级换算</summary>
        private UnitTier SelectedUnitTier()
        {
            var sel = _session?.Player?.LatestSnapshot?.units.FirstOrDefault(u => u.unitId == _selectedUnitId);
            if (sel != null && sel.tier != 0) return (UnitTier)sel.tier;
            var data = GetSelectedUnitData();
            return data != null ? UnitTierHelper.FromStars(data.starLevel) : UnitTier.Familiar;
        }

        /// <summary>玩家域判定（D-5 资源门槛的前提，取代旧 IsSelectedControllable=己方≥3星口径）：
        /// 己方魔神=全键玩家域（全手操）；己方伙伴=仅势力技能键（号令入口）；眷属/敌方单位=
        /// 无操控权（查看态恒可点，提交被防线轻提示拦截）。层级读快照 tier（TierOverrideStars
        /// 覆盖经快照同源——沙盒层级覆盖在客户端生效，2026-10-05）；0=未填回落原星级换算</summary>
        private bool IsSelectedPlayerDomain(SkillType buttonType)
        {
            var snapshot = _session?.Player?.LatestSnapshot;
            var sel = snapshot?.units.FirstOrDefault(u => u.unitId == _selectedUnitId);
            if (sel == null || sel.playerId != _myPlayerId) return false;
            UnitTier tier;
            if (sel.tier != 0) tier = (UnitTier)sel.tier;
            else
            {
                var data = TryGetUnitData(sel.unitName);
                if (data == null) return false;
                tier = UnitTierHelper.FromStars(data.starLevel);
            }
            if (tier == UnitTier.Archon) return true;
            return tier == UnitTier.Companion
                && (buttonType == SkillType.Enso || buttonType == SkillType.Contract);
        }

        /// <summary>选中单位是否己方（AI 域键资源置暗的前提——2026-10-04 拍板「资源不足整键变暗」扩展到
        /// 己方眷属/伙伴的 AI 域键：玩家不可提交但 AI 同样用不了=信息层；敌方=查看态无消耗语义不灰）</summary>
        private bool IsSelectedOwnUnit()
        {
            var snapshot = _session?.Player?.LatestSnapshot;
            var sel = snapshot?.units.FirstOrDefault(u => u.unitId == _selectedUnitId);
            return sel != null && sel.playerId == _myPlayerId;
        }

        /// <summary>技能资源门槛单源（统一消耗模型，docs/active/30 §2.3——预判/结算同形纪律）：
        /// 逐条镜像 ResourceGate.Has（元能=选中单位快照 energy / 体力·摩拉=本端缓存 /
        /// 物品=本地手牌镜像条目 count——Host 侧 HasAll 同口径）。
        /// **C-2 起消耗全量迁移完成：costs 空=免费技能**（无消耗语义，数据即事实）；
        /// 体力条目按**选中单位层级换算**镜像（D 批次操控分层 docs/active/32 §5.2——实际扣值=施法者
        /// 层级表 眷属0/伙伴5/魔神10，声明值=基准/校验值；与 ResourceGate.StaminaAmountOf 同口径）；
        /// 旧 EnergyCost 参数/体力类型分档双查已退役</summary>
        private bool HasSkillResources(SkillConfig.SkillData skillData)
        {
            if (skillData == null || !skillData.HasCosts) return true; // 免费技能/空数据
            var snapshot = _session?.Player?.LatestSnapshot;
            var selUnitData = GetSelectedUnitData();
            foreach (var cost in skillData.costs)
            {
                if (cost == null || cost.amount <= 0) continue;
                switch (cost.kind)
                {
                    case CostKind.Energy:
                        var sel = snapshot?.units.FirstOrDefault(u => u.unitId == _selectedUnitId);
                        if (sel == null || sel.energy < cost.amount) return false;
                        break;
                    case CostKind.Stamina:
                    {
                        int amount = selUnitData != null
                            ? UnitTierHelper.StaminaCostOf(SelectedUnitTier())
                            : cost.amount; // 无配置兜底按声明值（理论不可达）
                        if (amount > 0 && _myStamina < amount) return false;
                        break;
                    }
                    case CostKind.Mora:
                        if (_myMora < cost.amount) return false;
                        break;
                    case CostKind.Item:
                        if (!HasHandItem(cost.item, cost.amount)) return false;
                        break;
                    case CostKind.AnyItem:
                        if (!HasHandAnyItem(cost.subType, cost.amount)) return false;
                        break;
                    default:
                        // 镜像安全网（批7②）：与 Host ResourceGate.Has 同款——新增 CostKind 时此处若漏接
                        // 路由会静默按可支付置灰（镜像偏乐观），Warn 提示补路由；ResourceGate 为权威
                        GICLog.Warn($"[BattleHud] HasSkillResources 未接路由的消耗种类 {cost.kind}——按可支付处理（请补镜像路由）");
                        break;
                }
            }
            return true;
        }

        /// <summary>本地手牌镜像是否持有足量物品（C-1 客户端镜像=LatestSnapshot.resources.handCards 条目；
        /// 物品牌条目 count=局内真源，与 Host LoseCard 判定同源——快照权威）</summary>
        private bool HasHandItem(ItemName item, int amount)
        {
            var snapshot = _session?.Player?.LatestSnapshot;
            if (snapshot == null) return false;
            PlayerResourceState myRes = null;
            foreach (var r in snapshot.resources)
            {
                if (r.playerId == _myPlayerId) { myRes = r; break; }
            }
            if (myRes == null) return false;
            foreach (var entry in myRes.handCards)
            {
                if ((CardType)entry.cardType == CardType.Item && entry.value == (int)item)
                    return entry.count >= amount;
            }
            return false;
        }

        /// <summary>本地手牌镜像是否持有足量**任意同类**物品（C-1 AnyItem 客户端镜像——镜像
        /// ResourceGate.CountAnyItems 同口径：跨同类条目聚合、货币卡不可匹配；ItemConfig 走
        /// CardConfigResolver 与 RebuildHandCards 同链）</summary>
        private bool HasHandAnyItem(ItemSubType subType, int amount)
        {
            var snapshot = _session?.Player?.LatestSnapshot;
            if (snapshot == null) return false;
            PlayerResourceState myRes = null;
            foreach (var r in snapshot.resources)
            {
                if (r.playerId == _myPlayerId) { myRes = r; break; }
            }
            if (myRes == null) return false;
            var itemConfig = CardConfigResolver.Instance?.ItemConfig;
            if (itemConfig == null) return false;
            if (subType == ItemSubType.Currency) return false; // 货币=账户资源不经物品消耗链（Host 同口径）
            int total = 0;
            foreach (var entry in myRes.handCards)
            {
                if ((CardType)entry.cardType != CardType.Item) continue;
                var name = (ItemName)entry.value;
                if (name == ItemName.Mora || name == ItemName.Stamina) continue;
                var data = itemConfig.GetItemData(name);
                if (data != null && data.subType == subType)
                    total += entry.count;
            }
            return total >= amount;
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

            // 置灰两源（同 ApplySkillButton D-5 单源口径+2026-10-04 整键变暗扩展）：层级门控（眷属/伙伴的
            // 移动=AI 域——45% 半透明、interactable 保持 true，点击/拖拽照常进瞄准查看——拦截在提交时轻弹窗）；
            // 资源门槛（移动消耗走移动技能 costs 镜像 HasSkillResources，无 Move 条目单位=常量兜底
            // Host MoveExecutor.GetMoveCosts 同口径，体力按选中单位层级换算镜像 眷属0/伙伴5/魔神10）——
            // 玩家域（己方魔神）拦截+整键变暗；己方眷属/伙伴的 AI 域键资源不足同置暗（可叠加）
            bool tierGated = IsTierGatedButton(def);
            ApplyTierGateVisual(def, tierGated);
            bool conditionDimmed = false;
            if (def.view.selectButton != null)
            {
                var selUnitDataFallback = GetSelectedUnitData();
                int fallbackStamina = selUnitDataFallback != null
                    ? UnitTierHelper.StaminaCostOf(SelectedUnitTier())
                    : BattleMetrics.StaminaCostPerAction;
                bool resourcesOk = move != null ? HasSkillResources(move)
                    : _myStamina >= fallbackStamina;
                if (tierGated)
                {
                    def.view.selectButton.interactable = true; // 门控置灰=纯视觉提示——照常进瞄准查看（拦截在提交时）
                    conditionDimmed = IsSelectedOwnUnit() && !resourcesOk;
                }
                else
                {
                    bool playerDomain = IsSelectedPlayerDomain(SkillType.Move);
                    def.view.selectButton.interactable = !playerDomain || resourcesOk;
                    conditionDimmed = playerDomain && !resourcesOk;
                }
            }
            // 移动键染色在方法头部已刷新（InitWithData 在 gating 前）——置暗/恢复在此收口
            def.view.SetConditionDimmed(conditionDimmed, 资源不足变暗系数);
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
