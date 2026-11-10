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
    /// 正式战斗 HUD（B6 提前启动；docs/18 决策六 + docs/22 §13 + docs/designs/battle-hud-v1.html）。
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
    /// 该格实际能走到；不可选=无提示）+ 右上取消按钮 + 选中单位脚下金色标记与两束归属玩家色环绕光束。
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

        [Header("拖动瞄准头像盘（2026-10-05 拍板：指定单位型拖动瞄准，盘内出现可选角色头像——复用 QueueSlot 头像牌；从左往右、从上往下每行每列 4 个（4×4），超 16 缩头像变 5×5 以此类推；头像上松手=指定该角色，悬停保持该角色所在格高亮）")]
        [Tooltip("头像阵列区自盘缘的内缩边距（画布单位）——头像不压盘描环")]
        [SerializeField] private float 拖动瞄准头像盘边距 = 24f;
        [Tooltip("悬停头像放大倍率（悬停反馈——格金高亮与目标描边之外的轻量确认）")]
        [SerializeField] private float 拖动瞄准头像悬停放大 = 1.12f;

        [Header("技能详情面板（2026-09-27 拍板：同键再点=详情模式——面板自适应摆在技能键旁，不遮挡该键、不出屏）")]
        [Tooltip("面板与技能键的间隙（画布单位）")]
        [SerializeField] private float 详情面板与按钮间距 = 24f;
        [Tooltip("面板距屏幕边缘的最小留白（画布单位）")]
        [SerializeField] private float 详情面板屏幕边距 = 16f;

        [Header("技能键置灰（2026-10-04 拍板：AI 自主键不透明度 45%；使用条件不足整键变暗——不止图标，含底面/圆环；两源同键可叠加）")]
        [Tooltip("使用条件不足（资源不够等）时整键置暗系数——图标/底板/色环基准色统一乘该值（RGB 乘、alpha 不动=变暗非变透明）；与 AI 自主键 45% 半透明为正交机制，同键命中时视觉叠加")]
        [SerializeField, Range(0f, 1f)] private float 资源不足变暗系数 = 0.6f; // 首版 0.45 目检「太暗」调亮（2026-10-04）

        [Header("瞄准预览增强（2026-10-15 拍板：预计命中单位描边+每命中单位一条玩家色竖线+方向技能箭头显示；同日二轮拍板：箭头铺满整格加黑描边、竖线逐命中单位各一条）")]
        [Tooltip("预计命中竖线显示总开关——2026-10-15 四轮拍板「暂时移除竖线（不删除）」：false=不建竖线（链路/材质/重建逻辑全保留，勾选即复原）")]
        [SerializeField] private bool 显示命中竖线 = false;
        [Tooltip("预计命中竖线宽度（世界单位）——每个预判命中单位各一条的软渐变竖线（实心亮芯+光肩，非生硬直线）")]
        [SerializeField] private float 命中竖线宽度 = 0.2f;
        [Tooltip("预计命中竖线高度（世界单位）——高出立牌顶让标记从单位头上读得出")]
        [SerializeField] private float 命中竖线高度 = 1.8f;
        [Tooltip("命中竖线亮度呼吸频率（次/秒）")]
        [SerializeField] private float 命中竖线脉动频率 = 1.2f;
        [Tooltip("方向技能箭头带宽（世界单位；2026-10-15 二轮拍板「铺满整个格子」=与逐格高亮同足印；纹理设计面 1 格×1 格、tiling=(带长,1)）")]
        [SerializeField] private float 瞄准箭头带宽 = 0.92f;
        [Tooltip("箭头 chevron 滚动速度（格/秒）——悬停或已落地（金色待定）方向持续循环滚动")]
        [SerializeField] private float 瞄准箭头滚动速度 = 1.1f;

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
        // 移动步数上限不再用常量——数据驱动=移动技能 MoveDistance 参数（B-S1b），缺参数回落 3
        // 直线技能瞄准臂长不再用常量——数据驱动=EffectCompiler.DirectionRangeOf 单源
        // （2026-10-15 拍板「瞄准显示的距离应当和射出飞行物距离一致」，旧常量 8 对全员失真）

        /// <summary>移动瞄准步数上限=移动技能 MoveDistance 按基准换算（「拼接后为10%移速」2026-09-23
        /// 用户拍板：BasedOnMoveSpeed=10%×移速——安柏 60→6 格、其余 50→5 格；读快照 moveSpeed 与 Host 同源）；
        /// 无数据回落按 10% 移速</summary>
        private int 移动技能步数上限()
        {
            var moveData = GetSelectedSkillData(_aimDef); // IsMove 键 def.type=Move → 按 skillType 分拣命中
            var snapshot = _session?.Player?.LatestSnapshot;
            var sel = snapshot?.units.FirstOrDefault(u => u.unitId == _selectedUnitId);
            int moveSpeed = sel != null ? sel.moveSpeed : BattleMetrics.DefaultMoveSpeedCm;
            return moveData != null ? moveData.ResolveMoveDistance(moveSpeed) : moveSpeed * 10 / 100;
        }

        // ==================== 运行引用 ====================

        private BattleSession _session;
        private BattleBoard _board;
        private BattleCameraController _camera;
        private Action _onCloseBattle;
        private string _myPlayerId;

        /// <summary>本端玩家队伍（2026-09-25 三轮审查 C2：敌我判定统一 TeamType 口径——
        /// 操控权/资源归属=playerId，阵营判定=TeamType，两个语义勿再混用）。
        /// 2026-10-11 联机批：改走 BattleSession 门面（Roster 优先——LAN 客户端无 Sim 也成立；
        /// 调试直开无 Roster 回落 Sim）</summary>
        private TeamType MyTeam => _session != null
            ? _session.GetTeamOf(_myPlayerId)
            : TeamType.A;

        private Canvas _canvas;

        // 提示条
        private TMP_Text _tipText;
        private TextCombiner _tipCombiner;

        // 技能详情（现有体系复用：Resources/Prefabs/UI/Skill/SkillDetailPanel.prefab）
        private SkillDetailView _skillDetailView;

        /// <summary>技能详情面板烘焙停泊位（右侧，ResolveSkillPopup 读回）：非摆位开面板
        /// （卡牌详情内点技能图标——SkillIconView 直连 OpenPanel 无摆位链）的确定性落点，
        /// ClosePopup 收口复位——技能键/被动盘路径每次开面板都显式重摆不受影响
        /// （2026-10-10 追拍「和背包里一样可以点击」）</summary>
        private Vector2 _skillPopupDockedPos;

        // 行动区（技能盘三键/移动/手牌/取消均为布局件——显隐走 ApplyStateVisibility，_skillZone 容器 2026-09-21 退役）
        private RectTransform _handZone;
        private RectTransform _cancelButton;
        private TextCombiner _handTextCombiner;
        // 完成选择按钮（2026-09-26：顶部阶段级按钮，样式=祈愿 Marketplace 同款 prefab 烘焙；
        // 瞄准待定确认/无待定 Pass 双语义，显隐随选择阶段——Update 轮询同进度线/confirm）
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
            /// <summary>势力技能键（号令入口，D-5 层级门控豁免——伙伴选中可点=号令；docs/32 §8）</summary>
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

        /// <summary>使用物品瞄准会话载体（ItemName 枚举值；0=非会话）——2026-10-16 首个可打出物品批，
        /// 部署会话同构（打出态悬浮+待定金格+完成选择确认；ExitAiming 统一收口飞回打出卡）</summary>
        private int _itemUseAimItem;

        /// <summary>装备物品瞄准会话载体（ItemName 枚举值；0=非会话）——装备批 2026-10-18：武器/配件
        /// 打出=使用物品同构精确选人（目标=有空槽∨已装备该物品且耐久未满的我方存活非建筑单位
        /// 〔决策八十一补充耐久语义〕；ExitAiming 统一收口飞回）</summary>
        private int _equipAimItem;

        /// <summary>装备瞄准会话的实例耐久（决策八十一：装备条目按耐久分实例——拖出哪张卡就消耗
        /// 哪个实例，提交载荷 equipDurability 由此携带）</summary>
        private int _equipAimDurability;

        /// <summary>手牌卡按钮容器（hand 布局件内；RebuildHandCards 重建。
        /// 2026-09-23 审查 R1：滚动壳四层结构上移 prefab hand 槽，代码只寻址 _handScroll/_handContent+建卡条目）</summary>
        private readonly List<UnityEngine.UI.Button> _handCardButtons = new List<UnityEngine.UI.Button>();

        /// <summary>手牌滚动（B6c-2：卡多时横向滑动，同背包滚动视图结构——ScrollRect+Viewport 裁剪+Content）</summary>
        private UnityEngine.UI.ScrollRect _handScroll;
        private RectTransform _handContent;

        /// <summary>手牌卡 prefab（项目唯一卡牌形态 Card.prefab=Resources/Prefabs/Backpack/；懒加载）</summary>
        private GameObject _handCardPrefab;

        /// <summary>手牌签名（卡列表恒定时跳过重建——手牌不消耗，跨拍签名不变，免每拍重建 Instantiate GC）</summary>
        private string _handSignature;

        /// <summary>手牌货币物品牌卡（B6d：体力/摩拉=手牌（物品牌）拍板——恒占卡组投影末两位，
        /// 数量=局内持有非备战数/存档数，随秒拍发放与行动消耗经资源刷新链动态更新）</summary>
        private Card _moraHandCard;
        private Card _staminaHandCard;

        /// <summary>手牌普通物品牌卡引用（统一消耗模型 C-1：(ItemName,实例耐久)→Card——技能吃物品
        ///（酒/苹果）时 OnItemConsumed 事件即时重 Init 刷新对应卡数量角标；货币走
        /// _moraHandCard/_staminaHandCard 既有链。**键含实例耐久**（决策八十一——装备条目按耐久
        /// 分实例，同物异耐久=不同卡）</summary>
        private readonly Dictionary<(ItemName item, int durability), Card> _handItemCards
            = new Dictionary<(ItemName, int), Card>();

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

        // ==================== 瞄准预览增强（2026-10-15 拍板：预计命中单位描边+落点玩家色光柱+方向技能箭头） ====================

        /// <summary>P4-C 自由角瞄准指示器（决策九十五蓝图 §4.1「真圆/线段/落点指示器」——取代四方向
        /// 箭头与方向型逐格贴片）：射程环（技能=DirectionRangeCmOf cm 单源；移动=不限距无环）+方向线
        ///（chevron 滚动带复用，自由角 yaw）+落点金圈（AoERadius 真圆/单体=受击圆柱半径；黑描边环=
        /// 底座盘同语言）。BuildAimIndicators 建会话实例、ClearHighlights 销、RefreshAimIndicators 驱动
        ///（着色：拖拽=待定金、点击悬停=白、静止=推荐/不推荐）</summary>
        private GameObject _aimRangeRing;
        private GameObject _aimBandLine;
        private GameObject _aimLandingDisc;
        private GameObject _aimLandingOutline;
        private Material _aimRingMaterial;      // 射程环（瞄准推荐色半透；OnDestroy 释放）
        private Material _aimBandMaterial;      // 方向线（chevron 纹理；tiling/滚动/着色单实例）
        private Material _aimLandingMaterial;   // 落点金圈（瞄准已选色半透）
        private float _aimBandScrollPhase;
        private bool _aimBandScroll;

        /// <summary>当前瞄准会话射程（cm；0=移动不限距/无射程）——射程环半径/点击钳边共用</summary>
        private int _aimRangeCm;

        /// <summary>落点金圈半径（世界单位）——AoE 技能=AoERadius 真圆半径、单体=受击圆柱半径 0.25</summary>
        private float _aimLandingRadiusWorld = 0.25f;

        /// <summary>当前瞄准会话是否 LineBurst（整线迸发——伤害域=线段，无落点金圈）</summary>
        private bool _aimIsLineBurst;

        /// <summary>射程环带宽（世界单位=8cm——固定带宽不随半径缩放，CreateRing 比例带在大半径下过粗）</summary>
        private const float 瞄准指示环带宽 = 0.08f;

        /// <summary>P4-C 待定落点连续位置（cm）——拖拽=盘解析落点、点击=射程钳边落点；
        /// FillAimVector/指示器/相机跟随优先消费（格=其含格近似）</summary>
        private Vector2Int? _pendingAimCm;

        /// <summary>P4-C 点击式方向型悬停落点（cm）——方向线/落点圈跟随鼠标（钳边后）</summary>
        private Vector2Int? _hoverAimCm;

        /// <summary>P4-C 投射物沿瞄准向首接触距离（格=世界单位；-1=无接触/非投射物——落点金圈
        /// 骑首接触点，无接触回落射程端；迷雾过滤后不可见=回落同 -1）</summary>
        private float _aimFirstEntryCells = -1f;

        /// <summary>预计命中竖线（2026-10-15 二轮拍板「每个预计命中单位各一条竖线」取代首版单条落点
        /// 光柱）：每命中单位一条软渐变竖光，色=该单位归属玩家色（与描边成对同色）；
        /// 集合变化时重建（RebuildAimHitLines）、ClearHighlights 销毁</summary>
        private readonly List<GameObject> _aimHitLines = new List<GameObject>();
        private readonly Dictionary<string, Material> _aimHitLineMats = new Dictionary<string, Material>(); // 按单位归属 playerId 缓存（OnDestroy 释放）

        /// <summary>预计命中单位集（直线技能活动方向预判——描边刷新数据源；空集=无预览）</summary>
        private readonly HashSet<string> _aimHitUnitIds = new HashSet<string>();
        private readonly List<UnitState> _aimHitScratch = new List<UnitState>(); // 预判复用缓冲（免逐帧分配）

        /// <summary>瞄准范围提示整块（2026-10-15 拍板「额外加上范围提示……完整的一块，可以参考
        /// 滚动带」）：自施放光环技能瞄准时以自身为中心铺 (2R+1)² 格单 quad（纹理=BattleViewFactory
        /// 按半径程序生成缓存，实心底+整块外缘黑描边——格子高亮同语言）；层=AimRadiusBlock(-1)
        /// 恒在待定金格贴片(0)之下、盘面之上。**复用原语**：未来「选格降闪电攻击半径内敌人」类技能
        /// 的范围提示走同 ShowAimRadiusBlock(cell, radius)——提示中心跟随待定格；半径智能随命座
        /// （寒冰之棱 3命 +C3Radius）</summary>
        private GameObject _aimRadiusBlock;
        private Material _aimRadiusBlockMaterial; // 单实例复用（半径变=换纹理；OnDestroy 释放）

        /// <summary>方向预判技能实例缓存（按 SkillData 引用失效重建——拖动逐帧调用免分配）</summary>
        private BaseSkill _aimPreviewSkill;
        private SkillConfig.SkillData _aimPreviewSkillData;

        /// <summary>瞄准待定格（2026-09-26 拍板：点可选格不再立即提交——格子变金待定、
        /// 可点其它可选格变更、点空白=取消技能回选中态；确认提交=顶部「完成选择」按钮）。
        /// null=无待定</summary>
        private BattleCell? _pendingAimCell;

        /// <summary>瞄准待定目标单位（2026-10-05 拍板「指定单位型瞄准同格多候选重复点击轮换」+
        /// 拖动瞄准头像盘）：指定单位型技能（延奏/契约/单位指向型爆发）点格/拖动时记录具体目标，
        /// 重复点击同格轮换、头像盘悬停/松手指定；提交时优先消费（目标消失/换格残留回落
        /// FindUnitAt 旧行为）。非指定单位型恒 null（方向语义）</summary>
        private string _pendingTargetUnitId;

        /// <summary>拖动式瞄准会话中（B4，2026-09-26 落地）：技能键 OnBeginDrag 起手置位，
        /// OnDrag 逐帧刷金色待定单格、OnEndDrag 松手=**松手即放**（决策九十五 P4，蓝图 §4.1「拖拽=
        /// 王者荣耀一致」——推翻 2026-09-26「留待定」拍板）；任何瞄准退出
        /// （取消/阶段切换）经 ExitAiming 统一收口清零</summary>
        private bool _dragAiming;

        // ==================== 格线可读性（2026-11-10 画面增强批 A 组） ====================

        /// <summary>格线强调过渡时长（秒）：瞄准/拖动/悬停/出牌进入与退出操作态时的平滑时间。
        /// prefab 未序列化=脚本默认生效（Inspector 可调）</summary>
        [Tooltip("格线强调过渡时长（秒；0.18≈一眨眼，太长会显得拖沓）")]
        [SerializeField] private float 格线强调过渡秒 = 0.18f;

        /// <summary>当前格线强调度 0..1（逐帧趋近目标后写入 BattleBoard；0=常态、1=操作态）</summary>
        private float _gridEmphasis01;



        /// <summary>拖拽自由角瞄准向量 X（千分定点——盘心→小盘位移归一；决策九十五 P4 蓝图 §4.1
        /// 「任意角度」：取代旧十字轴量化；0=非拖拽会话/点击式——上交时 FillAimVector 回落
        /// 施法者→落点推导）</summary>
        private int _dragAimVecX;

        /// <summary>拖拽自由角瞄准向量 Z（千分定点，同上）</summary>
        private int _dragAimVecZ;

        /// <summary>拖动瞄准松手帧号（松手尾巴点击防线，2026-09-30 报障「很近的地方松手→落格
        /// 变打开详情面板」）：UGUI 抬起时「按压/抬起命中同一 IPointerClickHandler」即补发一次点击
        /// ——被拖键已挪到盘心，近距松手指针常仍压在键矩形内（键半宽 110>盘心死区 56），该尾巴点击
        /// 被 OnSkillButtonClicked 当「同键再点」误开详情面板；与 _dragAiming 双保险覆盖
        /// click/endDrag 执行顺序差异（§103）</summary>
        private int _dragAimEndFrame = -1;

        // ==================== P6 真摇杆持续态（蓝图 §3.3 移动命令两形态之二「方向持续」） ====================

        /// <summary>摇杆行进中（已提交远距目标、mover 在途）：盘心死区/松手=停止信号判据；
        /// 回中→再推出=重新行进（王者荣耀摇杆语义：按住期间朝向移动，松手停）</summary>
        private bool _joyMoveSubmitted;

        /// <summary>上次提交的摇杆向量（千分）——持稳不刷命令（变向超阈值才重提交，与 AI 0.5s
        /// 重决策替换同路；在途 mover 由替换链自然接管新方向）</summary>
        private int _joyLastVecX;
        private int _joyLastVecZ;

        /// <summary>摇杆行进目标距离（cm）：快照位+方向×该距离=远距目标点（RTSPathfinder
        /// ClampToNearestPassable 钳最近可走点兜底——按住期间行进只受阻挡/边界约束）</summary>
        private const int 摇杆行进距离Cm = 2000;

        /// <summary>摇杆变向重提阈值（千分点积：两向量各≈1000 千分长，dot&gt;该值=变向小于 ~15°
        /// 不重提交——在途 mover 沿旧方向继续，待下次显著变向/回中纠正）</summary>
        private const long 摇杆变向阈值Dot = 966_000;

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

        // ==================== 拖动瞄准头像盘（2026-10-05 拍板：指定单位型拖动瞄准，盘内可选角色头像阵列） ====================
        // 候选目标头像=复用 QueueSlot 头像牌 prefab（Plate/AvatarMask/Ring 结构、raycastTarget 全关——
        // 命中测试走手动 rect 包含：拖拽中指针被 UGUI 捕获，不派发 Enter/Exit 事件）。从左往右、
        // 从上往下排列，每行每列 4 个（4×4=16）；候选超 16 缩头像变 5×5（cols=4 起、cols*cols&lt;n 递增）。
        // 悬停头像=该角色所在格金格高亮+待定目标描边+头像轻放大；头像上松手=指定该角色
        // （UpdateDragAimPreview 头像命中优先，未命中回落锥角锁定旧行为）
        private GameObject _dragAvatarGrid;
        private readonly List<RectTransform> _dragAvatarRects = new List<RectTransform>();
        private readonly List<UnitState> _dragAvatarUnits = new List<UnitState>();
        private int _dragAvatarHover = -1;
        private float _dragAvatarBaseScale = 1f; // 阵列基准缩放（悬停放大除数/复位基准）
        private GameObject _avatarTilePrefab;    // QueueSlot（Resources/Prefabs/Battle/，懒加载）

        // 立牌点击拾取缓冲（2026-10-05 拍板「点击立牌也能选中」——屏幕空间面片命中）
        private readonly Vector3[] _paperdollCorners = new Vector3[4];
        private readonly Vector2[] _paperdollScreen = new Vector2[4];

        /// <summary>HUD 画布 RectTransform（盘位换算用；Overlay 画布世界坐标=屏幕像素）</summary>
        private RectTransform CanvasRect => _canvas != null ? (RectTransform)_canvas.transform : null;

        /// <summary>本秒拍完成选择已定死（2026-09-26 拍板「确认行动后就应当定死了」）：
        /// 点过完成选择并成功上交后为 true——按钮置灰、再按
        /// 均为无操作（完全统一：按已定死的按钮=无事发生）；新秒拍复位。
        /// 旧「后交覆盖先交、可再瞄准再确认改行动」语义随之废除，Host 侧同拒绝双保险</summary>
        private bool _actionConfirmed;
        /// <summary>P6 残留审计（docs/active/49 A1）：即时制下 RTSFlowController 每 0.5 秒广播
        /// OnPhaseChanged(Selecting, second) 作 AI 节拍兼容事件——HUD 只在相位**真变化**时执行
        /// 「进入新阶段」副作用（复位定死/提示/手牌显隐），避免每 0.5 秒重置一次。</summary>
        private BattlePhase _lastHudPhase = BattlePhase.Idle;

        /// <summary>详情面板当前是否开着（以现有面板 activeSelf 为准——关闭只走 BattleHud 显式路径，
        /// 自带点外关闭已在 BuildSkillPopup 关闭，docs/14 §64b）</summary>
        private bool PopupOpen => _skillDetailView != null && _skillDetailView.skillDetailPanel != null
            && _skillDetailView.skillDetailPanel.activeSelf;

        // ==================== N0 单人局浏览 UI 自动暂停（2026-10-13 决策六十七，docs/active/43 §10）====================

        // 宿主（BattleScreen）登记的外部浏览源——战斗内「设置/退出」确认弹窗由宿主持有实例，开态经回调供本件聚合
        private Func<bool> _externalBrowseOpen;

        /// <summary>登记宿主侧浏览源开态（BattleScreen.BindHud 接线：退出确认弹窗）</summary>
        internal void RegisterExternalBrowseSource(Func<bool> isOpen) => _externalBrowseOpen = isOpen;

        /// <summary>浏览型 UI 聚合开态（自动暂停源）：布局编辑/退出确认（退出）/设置界面（2026-10-15
        /// 战斗内设置钮批——UIManager 池化面板开态）/手牌卡详情（含全部
        /// 数据面板）/技能详情弹窗——Update 每帧重估写入 session.SelectTimerPaused，打开期间持续保持
        /// （跨阶段边界自洽：设置开着进选择阶段=倒计时冻结不偷跑）；单人局门控在 BattleSession 侧</summary>
        private bool BrowseUiOpen => _layoutEditing
            || HandCardDetailOpen
            || (_handStatsPanel != null && _handStatsPanel.gameObject.activeInHierarchy)
            || PopupOpen
            || BuildingShopOpen
            || (UIManager.Instance != null && UIManager.Instance.IsOpen(Screens.Settings))
            || (_externalBrowseOpen != null && _externalBrowseOpen());

        // ==================== 绑定 ====================

        public void Bind(BattleSession session, BattleBoard board, BattleCameraController camera, Action onCloseBattle)
        {
            if (_session != null) return; // 幂等守卫（批7③）：二次 Bind 会双订阅/双 AddListener/双预览实例
            _session = session;
            _board = board;
            _camera = camera;
            _onCloseBattle = onCloseBattle;
            // 本端玩家身份（2026-10-11 联机批）：联机=房间分配的 PlayerID（connectionId）；
            // 本地双开/调试直开（无房间名册）回落 P1
            _myPlayerId = ResolveLocalPlayerId();

            ResolveHudReferences(); // prefab 寻址接线（结构=BattleHud.prefab，2026-09-22 prefab 化）

            // 布局系统（2026-09-21）：运行时 AddComponent 不走场景注入扫描——手动注入取 SaveManager；
            // 建盘完成后按存档激活方案应用布局（-1/空槽=默认）
            Wargame.Instance.Context.Inject(this);
            InitMyResourceChipIcons(); // B6d：chip 图标走 [Autowired] ItemConfig——必须在注入后初始化（寻址阶段为 null）
            ApplySavedLayoutOnStart();
            _myPlayerId = ResolveLocalPlayerId(); // 注入后再解析一次（SelfPlayerID 经容器可达；两次同值幂等）

            _session.Player.SnapshotUpdated += OnSnapshotUpdated;
            _session.Player.OnResourceDelta += OnResourceDeltaHandler; // B6d：摩拉/体力命令增量（快照权威外的即时刷新）
            _session.Player.OnItemConsumed += OnItemConsumedHandler;  // C-1：技能吃物品（酒/苹果）手牌角标即时刷新
            _session.Player.OnItemGained += OnItemGainedHandler;      // 装备批：命座授予物品（兔兔伯爵）手牌即时并入
            _session.Player.OnItemGained += OnShopPurchaseRefreshHandler; // 购买批：物品卡购买后面板灰显/摩拉态刷新
            _session.Player.OnUnitCardGained += OnShopPurchaseRefreshHandler; // 购买批：角色/建筑卡购买后面板刷新
            _session.Player.BattleOver += OnBattleOverHandler;         // S10 全灭软停：胜负 Tip
            _session.Player.SkillCasted += OnSkillCastedHandler;        // P4-D：新施放=CD 转圈即时起点
            // 单位显示色解析器注入（2026-10-12 拍板「单位显示的颜色应当是玩家色」）：立牌底座/选中描边/
            // 血条填充全链改走房间玩家色单源（roster 命中=房间选色——同队队友异色可辨识归属；
            // 无 roster 调试直开回落 BattlePalette 两表口径）——先于 BattleStart 建场注入
            _session.Player.SetPlayerColorOf(pid => BattlePlayerColors.Resolve(_session, _myPlayerId, pid));
            // 血条填色解析器注入（2026-10-15 拍板「设置·战斗·血条颜色」玩家色/三色双模式——**只影响血条
            // 填充色**，底座/描边/弧光链路不动）：先于建场注入（OverheadBars 懒建时透传）
            _session.Player.SetHpBarColorOf(HpBarColorOf);
            // 相位/计时经 BattleSession 门面（2026-10-11 联机批）：host/本地=RTSFlowController、
            // LAN client=消息派生——HUD 不再直接触 Flow（客户端 Flow=null）。
            // P4 即时制（2026-11-04）：选择倒计时已退役（RTSFlowController 无倒计时语义）——
            // OnSelectTimerExpired 订阅随超时自动确认链一并退役
            _session.OnPhaseChanged += OnPhaseChanged;

            // 头顶 Buff 图标批（2026-10-06）接线：来源玩家色解析器（本端视角单源）+ 相机缩放源
            // （放大=半速率跟随、缩小=下限不缩）——层侧每帧/重建时消费；Bind 先于建场亦安全（懒取值）
            var bars = _session.Player.OverheadBars;
            if (bars != null)
            {
                bars.PlayerColorOf = OverheadBuffSourceColor;
                bars.CameraController = camera;
            }
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

        /// <summary>本端玩家身份解析（2026-10-11 联机批）：联机对局=房间分配的 PlayerID
        /// （PlayerManager.SelfPlayerID——connectionId/AI 恒不指向真人本机）；本地双开/调试直开
        /// （未连房间=Offline）回落 P1。Bind 注入前后各解析一次（首次兜注入序）</summary>
        private string ResolveLocalPlayerId()
        {
            var pm = Wargame.Instance?.Context?.Get<PlayerManager>();
            var id = pm?.SelfPlayerID;
            if (!string.IsNullOrEmpty(id) && id != PlayerID.Offline) return id;
            return BattleDebugPlayerIds.P1;
        }

        /// <summary>血条填色解析（2026-10-15 拍板「设置·战斗·血条颜色」双模式——**只影响血条填充色**，
        /// 底座圆盘/选中描边/弧光等其余玩家色链路不动）：玩家色=归属玩家色（2026-10-12 现行口径）；
        /// 三色（默认，参照王者荣耀）=自己的单位绿/队友全蓝/敌人全红——玩家不用记忆哪些颜色是队友。
        /// 模式真源=SaveSettings.hpBarColorMode（设置界面战斗栏）；设置界面不可在战斗中打开
        /// （战斗内设置入口=退出确认弹窗），改动下一场生效；存档缺读=回落默认三色档</summary>
        private Color HpBarColorOf(UnitView view)
        {
            int mode = _saveManager != null && _saveManager.CurrentSave != null
                ? _saveManager.CurrentSave.settings.hpBarColorMode
                : (int)HpBarColorMode.ThreeColor;
            if (mode == (int)HpBarColorMode.PlayerColor) return view.OwnerColor;
            if (view.OwnerPlayerId == _myPlayerId) return Palette.血条三色_自己;
            return view.OwnerTeam == MyTeam ? Palette.血条三色_队友 : Palette.血条三色_敌方;
        }

        /// <summary>头顶 Buff 图标描环=来源单位所属玩家色（2026-10-06 拍板「框的颜色采用来源单位所属
        /// 玩家色」）：unitId→快照 playerId→BattlePlayerColors 本端视角解析——与快捷面板/执行预览
        /// 头像描环同口径单源；来源单位不在快照（理论不可达）回落敌方主色（Resolve 空参语义）</summary>
        private Color OverheadBuffSourceColor(string unitId)
        {
            string playerId = null;
            var snapshot = _session?.Player?.LatestSnapshot;
            if (snapshot?.units != null)
            {
                foreach (var u in snapshot.units)
                {
                    if (u != null && u.unitId == unitId) { playerId = u.playerId; break; }
                }
            }
            return BattlePlayerColors.Resolve(_session, _myPlayerId, playerId);
        }

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
                    _session.Player.OnItemGained -= OnItemGainedHandler;
                    _session.Player.OnItemGained -= OnShopPurchaseRefreshHandler;
                    _session.Player.OnUnitCardGained -= OnShopPurchaseRefreshHandler;
                    _session.Player.BattleOver -= OnBattleOverHandler;
                    _session.Player.SkillCasted -= OnSkillCastedHandler;       // P4-D CD 转圈
                }
                _session.OnPhaseChanged -= OnPhaseChanged;
            }
            if (_camera != null)
                _camera.OnBoardTap -= OnBoardTap;
            if (_inputManager != null)
                _inputManager.OnActionTriggered -= OnInputAction;

            // 世界层运行时材质释放（Destroy 物体不销材质，不释放则跨战斗累积）
            if (_aimRecommendedMaterial != null) Destroy(_aimRecommendedMaterial);
            if (_aimNotRecommendedMaterial != null) Destroy(_aimNotRecommendedMaterial);
            if (_aimPendingMaterial != null) Destroy(_aimPendingMaterial);
            if (_aimHoverMaterial != null) Destroy(_aimHoverMaterial); // 悬停提亮材质（2026-10-06）
            foreach (var kv in _aimHitLineMats)                        // 命中竖线材质（2026-10-15 二轮；单光柱方案已废随字段删除）
                if (kv.Value != null) Destroy(kv.Value);
            _aimHitLineMats.Clear();
            if (_aimRingMaterial != null) Destroy(_aimRingMaterial);       // P4-C 指示器材质（射程环/方向线/落点圈）
            if (_aimBandMaterial != null) Destroy(_aimBandMaterial);
            if (_aimLandingMaterial != null) Destroy(_aimLandingMaterial);
            if (_aimRadiusBlockMaterial != null)                       // 范围提示整块材质（2026-10-15）
                Destroy(_aimRadiusBlockMaterial);
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
        /// 本地 handCards 镜像重 Init 对应卡（数量角标即时刷新；条目减尽=下一拍快照重建移除，
        /// 本帧卡残留一拍属可接受——Host 权威镜像已在，签名比对到点自然重建）。
        /// durability=装备实例耐久（决策八十一——装备条目按耐久分实例，同物异耐久=不同卡）</summary>
        private void OnItemConsumedHandler(string playerId, int itemName, int amount, int durability)
        {
            if (playerId != _myPlayerId) return;
            if (!_handItemCards.TryGetValue(((ItemName)itemName, durability), out var card) || card == null) return;
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
                    if ((CardType)entry.cardType == CardType.Item && entry.value == itemName
                        && entry.durability == durability)
                    {
                        count = entry.count;
                        break;
                    }
                }
            }
            RefreshCurrencyCard(card, (ItemName)itemName, count); // 复用重 Init 链（数量渲染=ItemCardViewStrategy）
        }

        /// <summary>获得物品卡命令（命座授予/死亡掠夺）：已有实例卡=刷数量角标（镜像已并，同
        /// OnItemConsumedHandler 模式读本地快照条目——按 (物品,实例耐久) 匹配，掠夺物新耐久=新实例）；
        /// **新实例=全量重建**（卡列表签名已变——新槽位长出来，下一拍快照自然对账）</summary>
        private void OnItemGainedHandler(string playerId, int itemName, int amount, int durability)
        {
            if (playerId != _myPlayerId) return;
            var snapshot = _session?.Player?.LatestSnapshot;
            PlayerResourceState myRes = null;
            if (snapshot != null)
            {
                foreach (var r in snapshot.resources)
                {
                    if (r.playerId == _myPlayerId) { myRes = r; break; }
                }
            }
            if (myRes == null) return;
            if (_handItemCards.TryGetValue(((ItemName)itemName, durability), out var card) && card != null)
            {
                int count = 0;
                foreach (var entry in myRes.handCards)
                {
                    if ((CardType)entry.cardType == CardType.Item && entry.value == itemName
                        && entry.durability == durability)
                    {
                        count = entry.count;
                        break;
                    }
                }
                RefreshCurrencyCard(card, (ItemName)itemName, count);
                return;
            }
            RebuildHandCards(myRes); // 新实例：重建（签名变化触发；执行期手牌隐藏中也不碍——恢复期即见）
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
            // P6 残留审计（docs/active/49 A1）：即时制复用相位事件承载 AI 节拍（每 0.5 秒一次），
            // 同一相位会被重复广播——副作用只在相位真变化时执行一次。
            if (phase == _lastHudPhase) return;
            _lastHudPhase = phase;

            if (phase != BattlePhase.Selecting)
            {
                // 终局收口（原执行阶段分支——即时制 Selecting=Running 兼容常驻，本分支仅 Finished 走到一次）：
                // 清瞄准/清选中回手牌态（技能盘收起=决策六执行阶段变化首版）
                ExitAiming();
                DeselectUnit();
                CloseBuildingShop(); // 购买面板随阶段收口（选择阶段专属交互，docs/45 §7.4）
                CancelHandCardDrag(); // 手牌拖拽中断收口（决策五十四：卡瞬回原槽防悬空在拖拽层）
                CloseHandCardDetail(); // 手牌卡详情随阶段收口（原执行阶段棋盘视线让位给预览；统计面板随卡详情联动收）
                if (_camera != null) _camera.EndDragFollowSession(); // 阶段流转=会话正常终结（相机留位、清快照防误重置）
                if (!_layoutEditing) SetTip("Battle_Finished"); // 原 Battle_TipResolving 随执行相位退役改指终局（2026-11-09 残留清理批）；编辑期提示条保持编辑提示不抢写
                // 2026-09-29 执行预览拍板「隐藏掉玩家之前打开的技能或手牌」：技能盘已随 DeselectUnit
                // 收起，手牌随相位隐藏让位中下方预览（CanvasGroup 渐隐勿 SetActive——滚动壳/热区
                // 探测逻辑常驻零中断）；回选择阶段恢复
                SetHandVisible(false);
            }
            else
            {
                _actionConfirmed = false; // 新秒拍开：完成选择定死复位（编辑期也要复位——编辑不挡节拍推进）
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
        private void Update()
        {
            // 防快捷键双触发：UGUI 选中件把 Space/Enter 当 Submit 重发 onClick（KeyBindingSettingItem 同款坑）——
            // 战斗 HUD 无键盘导航、选中态无用途，每帧清空（2026-09-26 空格快捷键接线）
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
                EventSystem.current.SetSelectedGameObject(null);

            // N0 单人局浏览 UI 自动暂停（决策六十七）：每帧重估写入（浏览源开着即保持；单人局门控在
            // BattleSession.SelectTimerPaused——host 权威计时仅单人局真停，多人局打开不冻全场；
            // 客户端本地显示冻结语义不变）
            if (_session != null)
                _session.SelectTimerPaused = BrowseUiOpen;

            UpdateHandHover(); // 手牌下沉/接近上移（独立于阶段轮询，自带空守卫）
            UpdateAimHover(); // 点击式瞄准悬停格提亮（2026-10-06 拍板；Aiming+非拖动才有悬停）
            UpdateGridEmphasis(); // 2026-11-10 画面增强批：格线常态极弱、操作期提亮
            UpdateAimFxAnimation(); // 瞄准增强动画推进（2026-10-15：箭头 chevron 滚动+光柱呼吸）
            UpdateHandCardDrag(); // 手牌拖拽手感主循环（决策五十四：跟随/抖动/让位/出牌区蒙层）
            UpdateSelectMarkerFog(); // 迷雾批 2026-10-17：选中的敌方单位走进迷雾——选中弧光随隐

            // P6 真摇杆持续态（蓝图 §3.3「方向持续：按住期间朝向移动，松手停」）：移动瞄准拖拽会话=
            // 摇杆——方向首提/显著变向→提交远距目标点维持行进（mover 替换链）；盘心死区=回中停止
            if (_dragAiming && _aimDef != null && _aimDef.IsMove)
                DriveMoveJoystick();

            // 拖动瞄准屏幕跟随喂点（2026-09-26 拍板「当拖拽的金格在屏幕外时，屏幕会丝滑的移动过去」
            // +2026-10-06 三段改造）：仅拖动会话中且有金色待定格才喂；单位指向型喂 centerMode=true
            // （选中单位恒居中、出屏瞬移——MOBA 式）；方向型 centerMode=false（出屏+1 格余量才补间居中）
            if (_camera != null)
            {
                Vector3? followWorld = null;
                bool followCenter = false;
                if (_dragAiming && _pendingAimCell.HasValue && _board != null && _board.Map != null)
                {
                    // P6 摇杆行进中改跟**单位**（拖点恒在盘内、行进目标在远方——跟拖点=单位走出屏；
                    // 跟视图实时位=跟人）；未行进/技能瞄准=旧口径
                    var player = _session != null ? _session.Player : null;
                    if (_aimDef != null && _aimDef.IsMove && _joyMoveSubmitted
                        && player != null
                        && player.Views.TryGetValue(_selectedUnitId, out var joyView)
                        && joyView != null)
                    {
                        followWorld = joyView.transform.position;
                    }
                    if (!followWorld.HasValue)
                    {
                        // P4-C：方向型拖拽跟随连续落点（cm 任意角精确）；其余（指向型）=待定格
                        followWorld = _pendingAimCm.HasValue
                            ? CmToWorld(_pendingAimCm.Value.x, _pendingAimCm.Value.y)
                            : _board.CellToWorld(_pendingAimCell.Value);
                        followCenter = IsCurrentAimUnitTargeted();
                    }
                }
                _camera.SetDragFollowTarget(followWorld, followCenter);
            }

            // P4 即时制（决策九十五蓝图 §2.4）：会话级收尾驱动——完成选择按钮显隐收窄=升命待确认态
            //（EnterUpgradePending 专用确认载体——选择阶段常显/瞄准待定确认语义已随即放退役）；
            // 技能键 CD 转圈+剩余秒数（P4-D，选中单位战技/爆发键逐帧驱动）
            if (_session == null) return;
            bool confirmShow = _state == HudState.Aiming && _upgradePending;
            SetLayoutWidgetActive("confirm", confirmShow);
            if (_confirmButton != null)
                _confirmButton.interactable = !_actionConfirmed; // 已定死置灰——再按=无操作（超时自动按下同款）
            UpdateSkillCooldowns(); // P4-D：技能键 CD 转圈+秒数（转圈数据源=SkillCasted 事件+快照校准）
        }

        // ==================== P4-D 技能 CD 转圈（决策九十五蓝图 §7「CD 转圈+剩余秒数」） ====================

        /// <summary>每单位 CD 就绪时刻（realtimeSinceStartup 秒；[0]=战技/普攻槽、[1]=爆发槽）——
        /// SkillCasted 事件即时起点+快照 attackCdRemainTicks/burstCdRemainTicks 每秒权威校准
        ///（校准=整表重写，单位离场残留条目无消费方=无害）</summary>
        private readonly Dictionary<string, float[]> _cdReadyAtByUnit = new Dictionary<string, float[]>();

        /// <summary>技能施放事件（BattlePlayer.SkillCasted）：新施放=CD 即时起点（战技/爆发两槽；
        /// cdTicks=施放起该技能类 CD 剩余 tick——转圈总时长=BattleMetrics 秒常量）</summary>
        private void OnSkillCastedHandler(string unitId, SkillType skillType, int cdTicks)
        {
            if (cdTicks <= 0) return;
            if (skillType != SkillType.Normal && skillType != SkillType.Burst) return;
            if (!_cdReadyAtByUnit.TryGetValue(unitId, out var slots))
                _cdReadyAtByUnit[unitId] = slots = new float[2];
            slots[skillType == SkillType.Burst ? 1 : 0] =
                Time.realtimeSinceStartup + cdTicks / (float)BattleMetrics.TicksPerSecond;
        }

        /// <summary>快照 CD 校准（每秒权威）：全单位战技/爆发剩余 tick→就绪时刻（remain=0=即刻就绪）——
        /// 事件起点与快照校准双通道，快照恒覆盖修正漂移</summary>
        private void RecalibrateCooldownsFromSnapshot(BattleSnapshot snapshot)
        {
            if (snapshot?.units == null) return;
            float now = Time.realtimeSinceStartup;
            foreach (var u in snapshot.units)
            {
                if (u == null) continue;
                if (!_cdReadyAtByUnit.TryGetValue(u.unitId, out var slots))
                    _cdReadyAtByUnit[u.unitId] = slots = new float[2];
                slots[0] = now + u.attackCdRemainTicks / (float)BattleMetrics.TicksPerSecond;
                slots[1] = now + u.burstCdRemainTicks / (float)BattleMetrics.TicksPerSecond;
            }
        }

        /// <summary>选中单位该技能槽 CD 是否未就绪（P4-D 拦截判据——与转圈数据同源；50ms 容差防
        /// 「显示 0 秒仍拦」秒末抖动；无记录=就绪）</summary>
        private bool SelectedUnitSkillOnCooldown(SkillType type)
        {
            if (_selectedUnitId == null) return false;
            if (!_cdReadyAtByUnit.TryGetValue(_selectedUnitId, out var slots)) return false;
            return slots[type == SkillType.Burst ? 1 : 0] > Time.realtimeSinceStartup + 0.05f;
        }

        /// <summary>技能键 CD 转圈驱动（Update 每帧）：选中单位战技/爆发键=径向遮罩+剩余秒数；
        /// 快捷面板行爆发键（P6 挂账销账）同数据源逐帧驱动——_cdReadyAtByUnit 全单位表
        ///（SkillCasted 事件起点+快照每秒校准）天然覆盖面板单位；
        /// 其余键（移动/延奏/契约/势力）无 CD 不参与——HasCooldownVisual 防为无 CD 键空建视觉件</summary>
        private void UpdateSkillCooldowns()
        {
            float now = Time.realtimeSinceStartup;

            // 快捷面板行（P6）：爆发键转圈+秒数；势力技能无 CD 不参与；无爆发技能行已隐藏跳过
            for (int i = 0; i < _quickRows.Count; i++)
            {
                var row = _quickRows[i];
                if (row.burstView == null || row.burstHost == null || !row.burstHost.activeSelf) continue;
                float remain = _cdReadyAtByUnit.TryGetValue(row.unitId, out var rowSlots)
                    ? rowSlots[1] - now : 0f;
                if (remain > 0f || row.burstView.HasCooldownVisual)
                    row.burstView.SetCooldown(remain, BattleMetrics.BurstCooldownSeconds);
            }

            if (_selectedUnitId == null) return;
            if (!_cdReadyAtByUnit.TryGetValue(_selectedUnitId, out var slots)) return;
            foreach (var def in _skillButtons)
            {
                if (def?.view == null) continue;
                int slot = def.type == SkillType.Normal ? 0 : def.type == SkillType.Burst ? 1 : -1;
                if (slot < 0) continue;
                float remain = slots[slot] - now;
                if (remain > 0f || def.view.HasCooldownVisual)
                    def.view.SetCooldown(remain, slot == 0
                        ? BattleMetrics.CasterCooldownSeconds : BattleMetrics.BurstCooldownSeconds);
            }
        }

        private void RefreshFromSnapshot(BattleSnapshot snapshot)
        {
            if (snapshot == null || _clockCombiner == null) return;

            // 战斗时钟（独立时钟；host=RTSFlowController 权威、LAN client=按快照逻辑秒推导——session 门面）
            // P4（2026-11-04）：回合数显示退役=时钟为主显示（蓝图 §2.3「回合数显示退役」）——
            // 回合中枢文本件随 turn 布局槽退役（时钟件存在性=本方法守卫）
            int minutes = _session.BattleTimeMinutes;
            _clockCombiner.SetSingleEntry($"{minutes / 60:D2}:{minutes % 60:D2}");

            // 我方资源 chips（左上角摩拉/体力物品牌计数，B6d；敌方资源不显示——2026-09-25 拍板）
            UpdateMyResources(snapshot);
            RecalibrateCooldownsFromSnapshot(snapshot); // P4-D：CD 就绪时刻每秒权威校准（全单位战技/爆发）

            // （攻速队列条已退役 2026-09-29——执行阶段改由 BattleExecutionPreview 多行预览接管）

            // 手牌（数量；卡列表 B8 接入）
            // 手牌（B6c：卡列表=当前卡组完整投影，含物品卡；数量文本与卡列表并存——文本做标签）
            var myRes = snapshot.resources.FirstOrDefault(r => r.playerId == _myPlayerId);
            _handTextCombiner.ClearAllEntries();
            _handTextCombiner.AddEntry(new LocalizedString("UIText", "Battle_Hand"));
            if (myRes != null)
                _handTextCombiner.AddStaticEntry(" ×" + myRes.handCardCount);
            RebuildHandCards(myRes);

            // 快捷面板（2026-10-06 拍板：左侧竖条，每行=头像+爆发+势力技能，点击=选中+相机居中）：
            // 成员/存亡变化重建行、元能水位/置暗/选中高亮就地刷新
            RefreshQuickPanel(snapshot);

            // 选中单位若已从快照消失（被移除/对局结束清场）清选中；尸体不清——2026-10-06 追拍
            // 「尸体也能点选查看」（旧防线会把刚选中的尸体在下次快照刷新时清掉）；尸体选中态
            // 技能盘照常刷新（查看语义：技能键/详情开放，提交防线+Host 权威校验兜底同敌方查看态）
            if (!string.IsNullOrEmpty(_selectedUnitId))
            {
                var sel = snapshot.units.FirstOrDefault(u => u.unitId == _selectedUnitId);
                if (sel == null)
                {
                    ExitAiming();
                    DeselectUnit();
                }
                else
                {
                    // 存续：重刷技能盘置灰态（B6a 爆发/延奏键的元能门槛随快照刷新——上拍攒满本拍即亮起）
                    RefreshSkillButtons();
                }
            }
        }

        // PhaseKey 已退役（2026-11-04 决策九十五 P4：回合中枢/阶段徽章随回合数显示退役）

        // ==================== 手牌与部署瞄准（B6c） ====================

        /// <summary>重建手牌卡（B6c：复用项目唯一卡牌形态 Card.prefab——策略链渲染卡面/名/星；
        /// 外层 wrapper 承点击（卡内 raycast 全关防拦截），费用角标为手牌语义叠加层。
        /// 每选择阶段头随快照重建（Card 淡入被 OnlyDisplay 跳过，无闪烁）。
        /// 2026-09-23 审查 R1：滚动壳（HandCards→HandScroll→HandViewport→HandContent 四层）上移
        /// BattleHud.prefab 的 hand 槽内——本方法只建卡条目；壳寻址=ResolveMiscWidgets，缺失已 Warn</summary>
        private void RebuildHandCards(PlayerResourceState myRes)
        {
            if (_handContent == null) return; // 壳未寻到（prefab 缺 HandCards），手牌不显示

            // 卡列表签名比对（卡 id 列表，不含数量——货币数量每秒拍变化不触发重建，
            // 角标经 RefreshHandCurrencyCards 随资源链动态刷；条目存亡变化（货币空堆移除/复活）自然反映。
            // **键含装备实例耐久**（决策八十一）——同物异耐久条目=不同卡，掠夺新实例触发重建）
            var signature = myRes == null ? ""
                : string.Join(",", myRes.handCards.ConvertAll(HandEntryKeyOf));
            if (signature == _handSignature) return;
            _handSignature = signature;

            SettleHandCardsOnRebuild(); // 拖拽/飞行/打出态收口（决策五十四：重建将销毁全部 wrapper）

            foreach (var btn in _handCardButtons)
                if (btn != null) Destroy(btn.gameObject);
            _handCardButtons.Clear();
            _handCardSlots.Clear(); // 手牌拖拽分件：槽列表随重建重铺（显示序=列表序）
            _moraHandCard = null;
            _staminaHandCard = null;
            _handItemCards.Clear();
            if (_handScroll != null) _handScroll.normalizedPosition = Vector2.zero;
            if (myRes == null) return;

            // 显示序（决策五十四拖拽重排缓存）：纯本地显示层——协议 handCards 顺序不动；
            // 缓存命中的键按缓存序在前，新获得条目按协议序殿后
            var orderedEntries = OrderHandEntriesForDisplay(myRes.handCards);
            // 无配置条目前置滤除（原循环内跳过改前置——决策五十四拖拽槽位公式要求 count=实铺数，
            // 循环内跳过会让重建摆位与拖拽让位两套 count 错位；循环内同名校验保留作防御）
            for (int i = orderedEntries.Count - 1; i >= 0; i--)
            {
                var h = orderedEntries[i];
                var id = h.AsCardId();
                if (h.IsUnit)
                {
                    if (_unitConfig?.GetUnitData(id.AsUnitName()) == null)
                    {
                        GICLog.Warn($"[BattleHud] 手牌卡 {id} 无 UnitConfig 配置，跳过");
                        orderedEntries.RemoveAt(i);
                    }
                }
                else if (CardConfigResolver.Instance?.ItemConfig?.GetItemData(id.AsItemName()) == null)
                {
                    GICLog.Warn($"[BattleHud] 手牌卡 {id} 无 ItemConfig 配置，跳过");
                    orderedEntries.RemoveAt(i);
                }
            }
            int count = orderedEntries.Count;

            if (_handCardPrefab == null)
                _handCardPrefab = Resources.Load<GameObject>("Prefabs/Backpack/Card");
            if (_handCardPrefab == null)
            {
                GICLog.Warn("[BattleHud] Card.prefab 未找到（Resources/Prefabs/Backpack/Card），手牌不显示");
                return;
            }

            // 单位配置=[Autowired] 注入（Y10），不再 Resources.Load
            // 手牌规格=Card.prefab 原生 160×240（保持收藏卡原比例，与背包同款）
            float cardWidth = 手牌卡宽, gap = 手牌卡间距; // 规格常量归手牌拖拽分件单源（决策五十四）
            float rowWidth = count * cardWidth + (count - 1) * gap;
            // content 宽恒=行宽+左右边距 60（**勿夹到视口宽**——窄于视口才有 Elastic 拖程，
            // "1 张卡也能滑动"；宽于视口=正常滚动），卡排相对 content 中心对称排；
            // 高=手牌窗口高+底裕（2026-10-19 窗口顶加高防裁角标，行顶距=手牌行顶距单源）
            _handContent.sizeDelta = new Vector2(rowWidth + 120f, 手牌窗口高 + 手牌内容底裕量);
            int builtIndex = 0; // 实铺序（无配置条目跳过不留洞——拖拽槽位公式同源，决策五十四）
            for (int i = 0; i < count; i++)
            {
                // 手牌条目（2026-09-25 拍板「获得卡片」统一）：卡+持有数量一等属性——
                // 普通/角色卡 count=局内真源（开局=备战数）；货币物品牌 count=资源池镜像
                // （发放/消耗经资源命令链即刷角标，RefreshHandCurrencyCards）
                var handEntry = orderedEntries[i];
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
                wrapperRt.anchoredPosition = new Vector2(HandSlotX(builtIndex, count), -手牌行顶距);
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
                bool isEquipmentCard = false;
                if (!isUnit && !isCurrency)
                {
                    var equipCheck = _itemConfig?.GetItemData(cardId.AsItemName());
                    isEquipmentCard = equipCheck != null
                        && (equipCheck.subType == ItemSubType.Weapon || equipCheck.subType == ItemSubType.Accessory);
                }
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
                        //（**键含实例耐久**——决策八十一：装备条目按耐久分实例）
                        _handItemCards[(cardId.AsItemName(), handEntry.durability)] = card;
                    }
                }

                // 装备实例耐久角标（决策八十一——手牌语义叠加层，左下；装备卡显示实例耐久
                //（新鲜卡=配置满值/掠夺卡=保留值；无耐久概念〔纯词条武器〕0 不显）；与数量角标分立：
                // 数量=同实例张数、耐久=实例可补充量）
                if (isEquipmentCard && handEntry.durability > 0)
                {
                    var durGo = new GameObject("EquipDurability");
                    durGo.transform.SetParent(wrapperGo.transform, false);
                    var durRt = durGo.AddComponent<RectTransform>();
                    durRt.anchorMin = durRt.anchorMax = new Vector2(0f, 0f);
                    durRt.anchoredPosition = new Vector2(30f, 16f);
                    durRt.sizeDelta = new Vector2(44f, 24f);
                    var durText = durGo.AddComponent<TextMeshProUGUI>();
                    durText.font = BattleViewFactory.WorldTextFont;
                    durText.fontSize = 20;
                    durText.alignment = TextAlignmentOptions.Center;
                    durText.color = Palette.高亮金;
                    durText.raycastTarget = false;
                    durText.text = handEntry.durability.ToString();
                }

                // 部署费角标已并入 Card.prefab 卡面（2026-10-19：左上角摩拉币外探+白字，
                // UnitCardViewStrategy 统一填值——手牌不再叠加运行时右下金数字，三端观感一致）

                var btn = wrapperGo.AddComponent<UnityEngine.UI.Button>();
                btn.targetGraphic = hit;
                // 打出=拖拽专属（决策五十四，拍板「当前只是点一下就算打出，非常low」）：点击=开卡牌详情
                // 面板（2026-10-10 拍板「点击手牌时，应当弹出卡牌详情面板」，分件 OnHandCardClicked）
                var capturedKey = HandEntryKey(handEntry); // 装备条目键含实例耐久（决策八十一）
                btn.onClick.AddListener(() => OnHandCardClicked(capturedKey));
                _handCardButtons.Add(btn);
                AttachHandCardInteraction(wrapperGo, capturedKey, isUnit, handEntry.value, handEntry.durability);
                builtIndex++;
            }
            WriteBackHandDisplayOrder(); // 显示序缓存=本次实铺序（剪除已不存在的键；拖拽重排经此跨重建保持）
            RefreshHandCardPlayStates(); // 打出条件变暗初刷（后续随资源链/快照刷新，2026-10-23）
        }

        /// <summary>出战/升命路由（B8 命座批，docs/05 §5.2+docs/09）：3★+ 同名已在场（含尸体）→
        /// 重复出战=提升命座——直接上交免落点瞄准（不生成新单位）；否则走部署瞄准（1~2★ 重复出战=
        /// 加单位、首战=选格落地，均不变）。满命上交被 Host 拒绝，本端先行轻提示省一次落空。
        /// 决策五十五起唯一入口=拖拽打出（slot=打出卡上下文）：满命拦截=弹回手牌槽、
        /// 升命卡=EnterUpgradePending 待确认态（2026-10-07 返拍「拖入出牌区，不应该直接上交」——
        /// 卡悬浮待「完成选择」确认上交，拖回/点空白/取消钮=反悔弹回）、部署=登记打出态进瞄准。
        /// 建筑拦截（决策八十六批C，2026-10-23 追记二改星级链，docs/45 §2）：星级链前置/上限
        /// 不满足=toast+弹回手牌槽（Host PassesBuildingStarGate/PassesBuildingLimit 权威兜底）</summary>
        private void TryDeployOrUpgrade(int unitNameValue, HandCardSlot slot)
        {
            var deployData = _unitConfig != null ? _unitConfig.GetUnitData((UnitName)unitNameValue) : null;
            if (deployData != null && deployData.unitType == UnitType.Building)
            {
                var snapshot = _session?.Player?.LatestSnapshot;
                if (!PassesBuildingStarPreview(deployData, snapshot))
                {
                    ShowBattleToast("Battle_TechRequired"); // 键名 Tech=内部标识符沿用（Rule 键先例），文案已改星级链口径
                    GICLog.Info($"[BattleHud] 建筑部署拦截（星级链）：{(UnitName)unitNameValue}");
                    if (slot != null)
                        FlyHandCardToSlot(slot, slot.baseScale,
                            HandSlotX(_handCardSlots.IndexOf(slot), _handCardSlots.Count));
                    return;
                }
                if (!PassesBuildingLimitPreview(deployData, snapshot))
                {
                    ShowBattleToast("Battle_BuildingLimit");
                    GICLog.Info($"[BattleHud] 建筑部署拦截（上限）：{(UnitName)unitNameValue}");
                    if (slot != null)
                        FlyHandCardToSlot(slot, slot.baseScale,
                            HandSlotX(_handCardSlots.IndexOf(slot), _handCardSlots.Count));
                    return;
                }
            }
            if (IsConstellationUpgrade(unitNameValue))
            {
                if (UpgradeTargetAtMax(unitNameValue))
                {
                    ShowBattleToast("Battle_ConstellationMax");
                    GICLog.Info($"[BattleHud] {(UnitName)unitNameValue} 已满命，升命拦截");
                    if (slot != null)
                        FlyHandCardToSlot(slot, slot.baseScale,
                            HandSlotX(_handCardSlots.IndexOf(slot), _handCardSlots.Count));
                    return;
                }
                EnterUpgradePending(unitNameValue, slot); // 返拍：不直接上交——待确认态（完成选择=确认）
                return;
            }
            if (slot != null) SetPlayedHandCard(slot); // 打出态：卡留在松手位悬浮（部署瞄准期随取随拖）
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

        /// <summary>进入部署瞄准（点手牌卡）：可选格=自己存活建筑锚并集（2026-10-23 拍板——
        /// 核心锚建筑 3/角色 2、其它建筑锚建筑 2/角色 1；客户端粗筛，Host IsDeployCellValid 兜底）</summary>
        private void EnterDeployAim(int unitNameValue)
        {
            if (_state != HudState.Idle) return; // 仅手牌态可起（选中单位时手牌已隐藏）
            ReturnToMainWorldForAim(); // 地脉视角=纯观察（决策九十一）：起部署瞄准自动切回主世界
            _deployAimUnit = unitNameValue;
            _state = HudState.Aiming;
            _aimDef = null;
            _pendingAimCell = null; // 新会话待定清零（高亮 quad 重建，材质无残留）
            ClosePopup();
            CloseHandCardDetail(); // 部署瞄准视线让位（点非可选格/拖回=反悔弹回）

            _aimCells.Clear();
            _aimRecommendedCells.Clear();
            var snapshot = _session.Player.LatestSnapshot;
            var deployData = _unitConfig != null ? _unitConfig.GetUnitData((UnitName)unitNameValue) : null;
            bool deployIsBuilding = deployData != null && deployData.unitType == UnitType.Building;
            // 迷雾批（2026-10-17 拍板 D4）：部署碰撞预判吃迷雾滤后快照——迷雾内敌方按不存在
            //（红格不得泄露隐藏敌人占位；地形不可行仍如实=允许泄露）
            var fogSnapshot = FogFilteredSnapshot(snapshot);
            // 部署锚点并集（2026-10-23 拍板「任何自己的建筑即可为锚」）：CollectDeployAnchorsFromSnapshot
            // 镜像 Host 单源（DeployUnitExecutor.CollectDeployAnchors）——核心锚建筑 3/角色 2、
            // 其它建筑锚建筑 2/角色 1，多锚重叠去重
            var seen = new HashSet<BattleCell>();
            foreach (var anchor in CollectDeployAnchorsFromSnapshot(snapshot))
            {
                int deployRadius = DeployUnitExecutor.DeployRadiusOf(anchor.isCore, deployIsBuilding)
                    / CellMetrics.CellSizeCm; // P3 cm 制→格环扫（Host 欧氏 cm 域判定的格域近似镜像——P4 HUD 真圆指示器批精确化）
                for (int dx = -deployRadius; dx <= deployRadius; dx++)
                for (int dy = -deployRadius; dy <= deployRadius; dy++)
                {
                    var c = new BattleCell(anchor.cell.x + dx, anchor.cell.y + dy);
                    if (!_board.Map.HasTile(c.x, c.y)) continue;
                    if (!seen.Add(c)) continue; // 多锚半径重叠去重
                    _aimCells.Add(c);
                    // 部署推荐分色（2026-09-25 拍板「根据碰撞决定」）：镜像 Host 碰撞判定链——
                    // 不推荐格仍可点击（Host 校验兜底，同移动瞄准「水面红格仍可点」语义）
                    if (CanDeployEnterPreview(c, deployData, fogSnapshot))
                        _aimRecommendedCells.Add(c);
                }
            }
            ShowAimHighlights();
            ApplyStateVisibility(); // Aiming 态：取消钮现、手牌藏
            SetTip("Battle_TipAimDeploy"); // 部署专属提示（批7⑥：原复用方向瞄准键语义不符）
        }

        /// <summary>进入使用物品瞄准（拖出可打出物品牌，2026-10-16 首个可打出物品批）：可选格=我方
        /// 存活**非建筑**单位所在格（TeamType 口径同延奏域与 Host UseItemExecutor 校验——2v2 含队友
        /// 单位、尸体不可饮用、建筑不可饮用——2026-10-16 报障修复「不可为建筑单位」；同格建筑+角色
        /// 共存时该格仍可选，域按单位收非按格收）。交互复用延奏精确选人（IsCurrentAimUnitTargeted
        /// 物品分支）：点格=同格多候选轮换+precise 立牌直选、待定目标描边、「完成选择」=确认提交
        /// （SubmitAim 物品分支单出口）、拖回手牌区/点非可选格/取消钮/阶段切换=反悔弹回（ExitAiming
        /// 统一收口）。</summary>
        private void EnterItemUseAim(int itemNameValue)
        {
            if (_state != HudState.Idle) return; // 仅手牌态可起（同部署）
            ReturnToMainWorldForAim(); // 地脉视角=纯观察（决策九十一）：起物品瞄准自动切回主世界
            _itemUseAimItem = itemNameValue;
            _state = HudState.Aiming;
            _aimDef = null; // 玩家级会话（无技能按钮定义——同部署/升命）
            _pendingAimCell = null; // 新会话待定清零（防陈旧提交）
            ClosePopup();
            CloseHandCardDetail(); // 瞄准视线让位（点非可选格/拖回=反悔弹回）

            _aimCells.Clear();
            _aimRecommendedCells.Clear();
            var snapshot = _session.Player.LatestSnapshot;
            if (snapshot != null)
            {
                // 目标域与 CollectAimTargets 物品分支同口径（单源语义：我方存活非建筑单位所在格）
                var candidates = new List<UnitState>();
                CollectAimTargets(snapshot, null, candidates);
                var seen = new HashSet<BattleCell>();
                foreach (var u in candidates)
                {
                    if (!seen.Add(u.position)) continue; // 同格多单位去重
                    _aimCells.Add(u.position);
                    _aimRecommendedCells.Add(u.position); // 单位指向域全推荐（目标格即语义本身，延奏同口径）
                }
            }
            ShowAimHighlights();
            ApplyStateVisibility(); // Aiming 态：取消钮现、手牌藏
            SetTip("Battle_TipAimItemUse"); // 饮用瞄准专属提示
        }

        /// <summary>进入装备物品瞄准（装备批 2026-10-18 拍板「和打出食物一样，需要精确指定」）：武器/
        /// 配件拖出出牌区进本会话。可选格=**可装备**的我方存活非建筑单位所在格（**决策八十一补充耐久
        /// 语义**：有空槽〔武器=weaponItem==0、配件=槽位表含零位〕∨ 已装备该物品且耐久未满〔补充
        /// 耐久目标——满耐久持有者不入选〕；快照槽位表单源；TeamType 口径同饮用域〔2v2 含队友单位；
        /// 尸体/建筑不可装备〕）。交互全复用饮用精确选人（IsCurrentAimUnitTargeted 装备分支：点格=
        /// 同格多候选轮换+precise 立牌直选+待定目标描边、「完成选择」=确认提交 SubmitAim 装备分支
        /// 单出口、拖回/点非可选格/取消钮/阶段切换=反悔弹回）。durability=拖出卡的实例耐久（提交
        /// 载荷 equipDurability——决策八十一：装备条目按耐久分实例）</summary>
        private void EnterEquipAim(int itemNameValue, int durability)
        {
            if (_state != HudState.Idle) return; // 仅手牌态可起（同部署/饮用）
            ReturnToMainWorldForAim(); // 地脉视角=纯观察（决策九十一）：起装备瞄准自动切回主世界
            _equipAimItem = itemNameValue;
            _equipAimDurability = durability;
            _state = HudState.Aiming;
            _aimDef = null; // 玩家级会话（无技能按钮定义——同部署/饮用/升命）
            _pendingAimCell = null; // 新会话待定清零（防陈旧提交）
            ClosePopup();
            CloseHandCardDetail(); // 瞄准视线让位（点非可选格/拖回=反悔弹回）

            _aimCells.Clear();
            _aimRecommendedCells.Clear();
            var snapshot = _session.Player.LatestSnapshot;
            if (snapshot != null)
            {
                // 目标域与 CollectAimTargets 装备分支同口径（单源语义：有空槽的我方存活非建筑单位）
                var candidates = new List<UnitState>();
                CollectAimTargets(snapshot, null, candidates);
                var seen = new HashSet<BattleCell>();
                foreach (var u in candidates)
                {
                    if (!seen.Add(u.position)) continue; // 同格多单位去重
                    _aimCells.Add(u.position);
                    _aimRecommendedCells.Add(u.position); // 单位指向域全推荐（目标格即语义本身，饮用同口径）
                }
            }
            ShowAimHighlights();
            ApplyStateVisibility(); // Aiming 态：取消钮现、手牌藏
            SetTip("Battle_TipAimEquip"); // 装备瞄准专属提示
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
            if (_session == null || _session.Phase != BattlePhase.Selecting) return;
            if (_camera == null || _board == null || _board.Map == null) return;
            if (HandCardDetailOpen)
            {
                // 点棋盘=按层收顶（同技能详情面板点外收口口径）：先收卡详情内点技能图标开的
                // 技能详情（叠在上层），再点才收卡牌详情——逐层退栈
                if (PopupOpen) { ClosePopup(); return; }
                CloseHandCardDetail();
                return;
            }

            if (!TryPickBoardCell(screenPos, out var cell, out bool inBounds)) return;

            var snapshot = _session.Player.LatestSnapshot;
            if (snapshot == null) return;

            // 点击拾取升级（2026-10-05 拍板「点立牌也能选中，点击立牌/所在地面/底座圆盘同规」）：
            // 立牌=55° 后仰的悬空面片，平面取格会把立牌上的点击解析到身后格（视差 §86 同族）——
            // 先做屏幕空间面片命中（多立牌重叠取离相机最近者），命中则点击目标=该单位及其所在格；
            // 未命中回落地面取格（底座圆盘/地面=原路径，选中开放任意单位语义不变——2026-09-26 拍板：
            // 含敌人/低级单位，行动拦截在提交时轻提示；同格多单位己方优先）。
            // 尸体立牌同样参与命中（悬空面片必须命中其格——复苏瞄准点尸体立牌的精确指定）。
            // 2026-10-06 追拍「尸体也能点选查看」：选中态尸体 precise 不再落空——尸体可选中
            // （技能盘/详情/描边全开=查看语义，提交防线 toast+Host 权威校验照旧兜底，同敌方查看态）；
            // 瞄准态 precise 由 HandleAimTap 候选域校验收编（复苏型爆发含尸体、延奏/契约域外 precise
            // 视同未点）
            var paperdollUnit = PickPaperdollUnit(screenPos, snapshot);
            // 部署瞄准=纯选格语义（2026-10-15 报障修复「部署点协议核心上面格子却被判成核心格」）：
            // 部署的悬停高亮走板面取格，点击若被立牌命中截胡重定向到立牌单位所在格（协议核心等高立牌
            // 画面伸入上方格），悬停与点击两口径不一致=点上面格落成核心格；部署格上立牌无选择语义，
            // 一律按板面取格。技能瞄准保留立牌优先（方向/移动=点敌立牌选该向同向无害、
            // 单位指向=点立牌直选、复苏=点尸体立牌直选）。
            bool deployPickByBoard = _state == HudState.Aiming && _deployAimUnit != 0;
            var precise = deployPickByBoard ? null : paperdollUnit;
            var targetCell = !deployPickByBoard && paperdollUnit != null ? paperdollUnit.position : cell;
            bool targetInBounds = (!deployPickByBoard && paperdollUnit != null) || inBounds;

            switch (_state)
            {
                case HudState.Aiming:
                    // P4-C 自由角域放开（决策九十五蓝图 §4.1）：方向型/移动=点板内任意格即放
                    //（技能射程外钳到射程圆边、移动不限距走点）；其余域=原可选格判定
                    //（单位指向/自施放/部署/物品/装备）
                    bool freeDomain = IsFreeAngleAim();
                    if (targetInBounds && (freeDomain || _aimCells.Contains(targetCell)))
                    {
                        // P4-C 自由角：方向型/移动的点击落点=板面精确 cm（射程钳边；移动不限距）——
                        // 立牌命中（precise）保持格语义（点敌立牌=朝该向）；钳边落点含格无 tile=回落原格
                        if (freeDomain && precise == null
                            && TryPickBoardCm(screenPos, out var tapCm, out bool cmOk) && cmOk)
                        {
                            var landing = ClampAimLanding(tapCm);
                            var landingCell = BattleCell.FromCm(landing.x, landing.y);
                            if (_board.Map.HasTile(landingCell.x, landingCell.y))
                            {
                                targetCell = landingCell;
                                _pendingAimCm = landing;
                            }
                            else _pendingAimCm = null;
                        }
                        HandleAimTap(targetCell, precise, snapshot);
                        // 落格轻提示（2026-09-29 追拍）：不可操作技能（敌方/眷属）、已定死或资源
                        // 不足时立即提示「不会执行」，防玩家选了却不知为何没被执行；金格照常显示
                        NotifySelectionBlockedOrConfirmed();
                        return;
                    }
                    // 瞄准态点非可选格 = 退回选中态（不算"点空白取消选中"）——取消语义：拖动跟随过的
                    // 相机重置回瞄准开始位（2026-10-06 拍板，覆盖松手留待定后再点空白取消的路径）
                    ExitAiming();
                    if (_camera != null) _camera.CancelDragFollow();
                    return;

                case HudState.UnitSelected:
                    if (PopupOpen) { ClosePopup(); return; }          // 情况②：面板开着点外部=收面板（选中保持）
                    // 2026-10-05 拍板：同格多单位重复点击轮换下一个；精确点到非当前选中的立牌直接选它；
                    // 点空白=取消选中
                    HandleSelectionTap(targetCell, precise, snapshot, deselectOnEmpty: true);
                    return;

                case HudState.Idle:
                    HandleSelectionTap(targetCell, precise, snapshot, deselectOnEmpty: false);
                    return;
            }
        }

        /// <summary>屏幕空间立牌命中（2026-10-05 拍板「点击立牌也能选中」）：遍历全部立牌视图，
        /// 当前激活面片四角投影成屏幕四边形、指针在内=命中；多立牌重叠取离相机最近者
        /// （前排先被点中）。**尸体立牌同参与命中**（悬空面片必须命中其格——复苏瞄准点尸体立牌
        /// 的精确指定；尸体可不可选由消费方按状态分流，见 OnBoardTap）</summary>
        private UnitState PickPaperdollUnit(Vector2 screenPos, BattleSnapshot snapshot)
        {
            var views = _session.Player.Views;
            if (views == null || views.Count == 0) return null;
            UnitState best = null;
            float bestDist = float.MaxValue;
            var camPos = _camera.transform.position;
            foreach (var kv in views)
            {
                var view = kv.Value;
                if (view == null) continue;
                if (view.IsSoulView) continue; // 灵魂不可点选（决策九十一：v1 灵魂无详情无瞄准交互——地脉视角纯观察）
                UnitState unit = null;
                foreach (var u in snapshot.units)
                {
                    if (u.unitId == kv.Key) { unit = u; break; }
                }
                if (unit == null) continue; // 尸体不排除——立牌仍在场（灰显），点击须命中其格
                if (!view.TryGetPaperdollWorldCorners(_paperdollCorners)) continue;

                // 四角投影 + 凸四边形点内测试（任一 winding；±1px 容差含线上）
                var q = _paperdollScreen;
                bool projected = true;
                for (int i = 0; i < 4; i++)
                {
                    if (!_camera.TryProjectToScreen(_paperdollCorners[i], out var p)) { projected = false; break; }
                    q[i] = p;
                }
                if (!projected) continue; // 任一角投影失败（相机背后）=该立牌不可点
                if (!PointInQuad(screenPos, q)) continue;

                float dist = Vector3.Distance(camPos, (_paperdollCorners[0] + _paperdollCorners[2]) * 0.5f);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = unit;
                }
            }
            return best;
        }

        /// <summary>点在凸四边形内（全 cross 同侧即命中；±1px 容差——线上/贴边算命中）</summary>
        private static bool PointInQuad(Vector2 p, Vector2[] q)
        {
            float minC = float.MaxValue, maxC = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                var a = q[i];
                var b = q[(i + 1) & 3];
                float cross = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
                if (cross < minC) minC = cross;
                if (cross > maxC) maxC = cross;
            }
            return minC >= -1f || maxC <= 1f;
        }

        /// <summary>选中态点击（2026-10-05 拍板：立牌/地面/底座圆盘同规+同格轮换；2026-10-06 追拍
        /// 「尸体也能点选查看」）。precise=立牌精确指到的单位（可空，含尸体）。序=己方活体 → 队友
        /// 活体 → 敌方活体 → 尸体垫底（己方优先与旧 FindUnitAt(Mine) 兼容，队友插入使 2v2 队友
        /// 单位可选中——2026-09-26 拍板「任意单位可选中」补全）：
        /// ① 该格已有当前选中单位（重复点击）→ 轮换到该格下一个单位（活体轮尽轮到尸体）；
        /// ② 精确点到非当前选中的立牌（含尸体立牌）→ 直接选它；
        /// ③ 其余 → 按序选首个。格上无单位：deselectOnEmpty 决定点空白=取消选中（UnitSelected）或无操作（Idle）</summary>
        private void HandleSelectionTap(BattleCell cell, UnitState precise, BattleSnapshot snapshot, bool deselectOnEmpty)
        {
            var units = UnitsAtCellOrdered(snapshot, cell);
            if (units.Count == 0)
            {
                if (deselectOnEmpty) DeselectUnit();
                return;
            }
            int selIdx = units.FindIndex(u => u.unitId == _selectedUnitId);
            if (selIdx >= 0)
            {
                SelectUnit(units[(selIdx + 1) % units.Count].unitId); // 重复点击该格 → 轮换
                return;
            }
            var target = precise != null && precise.unitId != _selectedUnitId ? precise : units[0];
            SelectUnit(target.unitId);
        }

        /// <summary>该格的可选中单位，序=己方活体 → 队友活体 → 敌方活体 → 尸体垫底（各段快照序内
        /// 保序）。2026-10-06 追拍「尸体也能点选查看」：尸体入轮换序（点尸体格不再=取消选中）；
        /// 垫底=活体优先（默认选中目标仍是活体），精确点尸体立牌则直接选尸体（HandleSelectionTap
        /// precise 优先）。瞄准候选域（CollectAimTargets）与本序独立——延奏/契约仍只活体</summary>
        private List<UnitState> UnitsAtCellOrdered(BattleSnapshot snapshot, BattleCell cell)
        {
            var result = new List<UnitState>();
            foreach (var side in new[] { UnitSide.Mine, UnitSide.MyTeam, UnitSide.Enemy })
            {
                foreach (var u in snapshot.MainWorldUnits()) // 主世界域单源（②④迁移）
                {
                    if (u.isCorpse != 0) continue; // 灵魂不可选不可查看（决策九十一：已随 MainWorldUnits 收口）
                    if (u.position.x != cell.x || u.position.y != cell.y) continue;
                    bool pick = side == UnitSide.Mine ? u.playerId == _myPlayerId
                        : side == UnitSide.MyTeam ? ((TeamType)u.team == MyTeam && u.playerId != _myPlayerId)
                        : (TeamType)u.team != MyTeam;
                    if (!pick) continue;
                    // 迷雾批（2026-10-17）：同格查看列表滤迷雾内敌方（立牌已隐，点格不应还能翻到）
                    if (!FogVisible(u)) continue;
                    result.Add(u);
                }
            }
            foreach (var u in snapshot.MainWorldUnits()) // 尸体垫底段（不分敌我——查看语义无优先级需求；②④迁移）
            {
                if (u.isCorpse == 0) continue; // 灵魂永不入查看域=维度隔离收口（决策九十一）
                if (u.position.x != cell.x || u.position.y != cell.y) continue;
                if (!FogVisible(u)) continue;
                result.Add(u);
            }
            return result;
        }

        // ==================== 指定单位型瞄准目标（2026-10-05 拍板：同格多候选重复点击轮换+拖动瞄准头像盘） ====================

        /// <summary>当前瞄准是否指定单位型（延奏/契约/单位指向型爆发——IsUnitTargeted 单源；
        /// 使用物品会话=同构精确选人——2026-10-16 报障修复「打出物品需要和延奏一样精确的选择某个
        /// 角色」：同格多候选点击轮换+立牌 precise 直选+待定目标描边全链复用，域=CollectAimTargets
        /// 物品分支）；部署瞄准（_aimDef=null 且 _itemUseAimItem=0）与方向型/自施放恒 false</summary>
        private bool IsCurrentAimUnitTargeted()
        {
            if (_itemUseAimItem != 0) return true; // 使用物品=指定单位型（延奏同构精确选人）
            if (_equipAimItem != 0) return true;   // 装备物品=指定单位型（装备批：饮用同构精确选人）
            if (_aimDef == null) return false;
            var data = GetSelectedSkillData(_aimDef);
            return data != null && data.IsUnitTargeted();
        }

        /// <summary>瞄准目标域枚举（与 ComputeAimCells 同域同序单源——caster 队伍口径；查看敌方单位时
        /// 目标域随施法者视角，2026-09-26 拍板）：延奏=施法者队伍存活；契约=敌队存活；
        /// 单位指向型爆发=施法者队伍**含尸体**（复苏目标，docs/05 §5.4 例外条款）。
        /// cell=null 全域（头像盘/高亮格生成）；非空=该格候选（点格轮换）。序=快照序（轮换确定性）</summary>
        private void CollectAimTargets(BattleSnapshot snapshot, BattleCell? cell, List<UnitState> into)
        {
            // 使用物品分支（2026-10-16 报障修复）：目标域=我方存活**非建筑**单位（用户拍板「不可为
            // 建筑单位」——给建筑喂物品无意义；TeamType 口径同延奏域——2v2 含队友单位；尸体不可饮用）。
            // 独立于下方技能 caster 域（物品无施法者）。precise 域校验随域自动排除建筑——点建筑立牌
            // 视同未点，同延奏点尸体立牌先例。
            if (_itemUseAimItem != 0)
            {
                foreach (var u in snapshot.MainWorldUnits()) // 主世界域单源（②④迁移）
                {
                    if (u.isCorpse != 0) continue; // 灵魂不可饮用（决策九十一：已随 MainWorldUnits 收口）
                    if ((TeamType)u.team != MyTeam) continue;
                    if (IsBuildingUnitState(u)) continue;
                    if (cell.HasValue && (u.position.x != cell.Value.x || u.position.y != cell.Value.y)) continue;
                    if (!_board.Map.HasTile(u.position.x, u.position.y)) continue;
                    into.Add(u);
                }
                return;
            }
            // 装备物品分支（装备批 2026-10-18；决策八十一补充耐久域）：目标域=**可装备**的我方存活
            // 非建筑单位——有空槽 ∨ 已装备该物品且耐久未满（补充耐久目标；同物永不双槽=持有恒走
            // 补充路径，满耐久/持有他物槽满不入选=不可点的空反馈，免提交后被 Host 拒）。物品种类经
            // _itemConfig 单源判定（EquipTargetQualifies 单出口）
            if (_equipAimItem != 0)
            {
                foreach (var u in snapshot.MainWorldUnits()) // 主世界域单源（②④迁移）
                {
                    if (u.isCorpse != 0) continue; // 灵魂不可装备（决策九十一：已随 MainWorldUnits 收口+无装备拍板）
                    if ((TeamType)u.team != MyTeam) continue;
                    if (IsBuildingUnitState(u)) continue;
                    if (!EquipTargetQualifies(u, _equipAimItem)) continue;
                    if (cell.HasValue && (u.position.x != cell.Value.x || u.position.y != cell.Value.y)) continue;
                    if (!_board.Map.HasTile(u.position.x, u.position.y)) continue;
                    into.Add(u);
                }
                return;
            }
            var skillData = _aimDef != null ? GetSelectedSkillData(_aimDef) : null;
            if (skillData == null || !skillData.IsUnitTargeted()) return;
            var sel = snapshot.units.FirstOrDefault(u => u.unitId == _selectedUnitId);
            if (sel == null) return;
            bool contract = skillData.skillType == SkillType.Contract;
            bool burstUnit = skillData.skillType == SkillType.Burst; // 单位指向型爆发（IsUnitTargeted 已保证）
            foreach (var u in snapshot.MainWorldUnits()) // 主世界域单源（②④迁移：灵魂排除=维度隔离，延奏/契约全拒）
            {
                if (cell.HasValue && (u.position.x != cell.Value.x || u.position.y != cell.Value.y)) continue;
                if ((TeamType)u.team == (TeamType)sel.team)
                {
                    if (contract) continue;
                    if (u.isCorpse != 0 && !burstUnit) continue; // 延奏只活体；爆发单位指向含尸体
                }
                else
                {
                    if (!contract) continue;    // 延奏/爆发只我方侧
                    if (u.isCorpse != 0) continue; // 契约只活体
                    // 迷雾批（2026-10-17，拍板 D4 单位类零泄露）：契约目标域=敌队——迷雾内敌方不可选
                    if (!FogVisible(u)) continue;
                }
                if (!_board.Map.HasTile(u.position.x, u.position.y)) continue;
                into.Add(u);
            }
        }

        /// <summary>瞄准态点击可选格——**P4 点击即放**（决策九十五，蓝图 §4.1「点击式=英雄联盟：点
        /// 目标点/目标单位即放」——取代 2026-09-26 待定制；**同格多候选轮换交互退役**，精确指定=
        /// 点立牌 precise 直选〔复苏爆发点尸体立牌=直选尸体〕；防线/资源拦截=SubmitAim false 保持
        /// 瞄准态可再点）。装备瞄准点空域反馈（决策八十一追拍）保留在拦截路径</summary>
        private void HandleAimTap(BattleCell cell, UnitState precise, BattleSnapshot snapshot)
        {
            if (IsCurrentAimUnitTargeted())
            {
                var candidates = new List<UnitState>();
                CollectAimTargets(snapshot, cell, candidates);
                if (candidates.Count > 0)
                {
                    // 精确候选优先（立牌直选）；无 precise=首个候选
                    UnitState target = candidates[0];
                    if (precise != null)
                    {
                        foreach (var c in candidates)
                            if (c.unitId == precise.unitId) { target = c; break; }
                    }
                    _pendingTargetUnitId = target.unitId;
                    SetPendingAimCell(cell);
                    RefreshOutlines(); // 目标描边反馈（提交成功随 ExitAiming 收口）
                    TrySubmitPendingAim();
                    return;
                }
                // 点格无候选（域外）——该格有我方存活非建筑单位且持有该物品但已满（耐久满/层满）
                // → toast 提示（耐件/堆件分流键）；其余空格静默（原行为）
                if (_equipAimItem != 0)
                {
                    var tapped = FindUnitAt(snapshot, cell, UnitSide.MyTeam) ?? precise;
                    if (tapped != null && !IsBuildingUnitState(tapped)
                        && UnitHoldsItem(tapped, _equipAimItem) && !EquipTargetQualifies(tapped, _equipAimItem))
                        ShowBattleToast(EquipFullToastKey(_equipAimItem));
                }
            }
            SetPendingAimCell(cell); // 方向型/部署：金格反馈（拦截=保持待定可视化）
            if (_pendingTargetUnitId != null)
            {
                _pendingTargetUnitId = null;
                RefreshOutlines();
            }
            TrySubmitPendingAim();
        }

        /// <summary>待定格+目标单位（拖动瞄准路径消费）：头像命中=直接指定该单位；无精确目标=该格首个
        /// 候选（锥角锁定回落同口径）。同格幂等快路径——锥角锁定连续命中同格不重算候选（免逐帧分配）</summary>
        private void SetPendingAimCellWithTarget(BattleCell cell, UnitState unit)
        {
            bool sameCell = _pendingAimCell.HasValue && _pendingAimCell.Value.Equals(cell);
            if (unit != null && unit.position.Equals(cell))
            {
                if (sameCell && _pendingTargetUnitId == unit.unitId) return; // 同格同目标幂等
                SetPendingAimCell(cell);
                _pendingTargetUnitId = unit.unitId;
                RefreshOutlines();
                return;
            }
            // 无精确目标：同格且目标已定（或方向型无目标语义）→幂等直通（金格本就同格无操作）
            if (sameCell && (!IsCurrentAimUnitTargeted() || !string.IsNullOrEmpty(_pendingTargetUnitId)))
                return;
            string targetId = null;
            if (IsCurrentAimUnitTargeted())
            {
                var candidates = new List<UnitState>();
                CollectAimTargets(_session.Player.LatestSnapshot, cell, candidates);
                if (candidates.Count > 0) targetId = candidates[0].unitId;
            }
            SetPendingAimCell(cell);
            _pendingTargetUnitId = targetId;
            RefreshOutlines();
        }

        /// <summary>提交时刻待定目标解析（2026-10-05）：_pendingTargetUnitId 仍在待定格上才有效
        /// （目标当拍死亡/换格残留由快照对账拦下），有效则优先于格上按域找（FindUnitAt 旧行为回落）</summary>
        private UnitState ResolvePendingTarget(BattleSnapshot snapshot, BattleCell cell)
        {
            if (string.IsNullOrEmpty(_pendingTargetUnitId) || !IsCurrentAimUnitTargeted()) return null;
            UnitState u = null;
            foreach (var x in snapshot.units)
            {
                if (x.unitId == _pendingTargetUnitId) { u = x; break; }
            }
            if (u == null || u.position.x != cell.x || u.position.y != cell.y) return null;
            return u;
        }

        // ==================== 选中/待定目标描边（2026-10-05 拍板「选中时立牌加描边，颜色=所属玩家色」） ====================

        /// <summary>描边统一收口（全量遍历按幂等开关写，≤70 单位低频调用）：三个槽各至多一组——
        /// ① 选中单位（瞄准态选中仍在=描边保持）；② 指定单位型瞄准的待定目标（点击轮换/头像盘悬停
        /// 的目标反馈）；③ 直线技能活动方向的预计命中单位（2026-10-15 拍板「对预计有效单位高亮
        /// 描边，复用现有描边」——数据源=_aimHitUnitIds，UpdateAimHitPreview 单点维护）。
        /// 色=归属玩家色（与底座圆盘/弧光同源 BattlePlayer 建盘口径）</summary>
        private void RefreshOutlines()
        {
            var views = _session?.Player?.Views;
            if (views == null) return;
            string selected = _selectedUnitId;
            string target = _state == HudState.Aiming && !string.IsNullOrEmpty(_pendingTargetUnitId)
                ? _pendingTargetUnitId
                : null;
            bool hitPreview = _state == HudState.Aiming && _aimHitUnitIds.Count > 0;
            foreach (var kv in views)
            {
                var view = kv.Value;
                if (view == null) continue;
                view.SetSelectedOutline(kv.Key == selected || kv.Key == target
                    || (hitPreview && _aimHitUnitIds.Contains(kv.Key)));
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
            foreach (var u in snapshot.MainWorldUnits()) // 主世界域单源（②④迁移）
            {
                if (u.isCorpse != 0 && !includeCorpses) continue; // 尸体默认不可选（单位指向型爆发例外——复苏目标）；灵魂排除=维度隔离收口
                if (u.position.x != cell.x || u.position.y != cell.y) continue;
                bool pick;
                if (side == UnitSide.Mine) pick = u.playerId == _myPlayerId;          // 操控权（B7 分端=本机玩家）
                else if (side == UnitSide.MyTeam) pick = (TeamType)u.team == MyTeam;  // 我军（2v2 含队友单位）
                else pick = (TeamType)u.team != MyTeam;                               // 敌军
                if (!pick) continue;
                // 迷雾批（2026-10-17，拍板 D4 单位类零泄露）：点格选敌——迷雾内敌方不可中
                //（渲染器已禁=立牌 precise 直选天然失效，此处补点格路径）
                if (!FogVisible(u)) continue;
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
            RefreshOutlines(); // 选中描边（2026-10-05 拍板：立牌描边=归属玩家色；ExitAiming 已随收口刷过一次）
            UpdateQuickSelectionHighlight(); // 快捷面板行高亮随选中即时刷新（2026-10-06）
            SetTip("Battle_TipUnitSelected");
            TryOpenBuildingShop(unitId); // 购买面板（决策八十六批E：我方存活建筑选中态专属面板；敌方=纯查看不弹）
        }

        private void DeselectUnit()
        {
            _selectedUnitId = null;
            _state = HudState.Idle;
            ClosePopup();
            CloseBuildingShop(); // 购买面板随取消选中收口（换选/点空白同路，docs/45 §7.4）
            HideSelectMarker();
            ApplyStateVisibility();
            RefreshOutlines(); // 选中/目标描边随选中一并清（2026-10-05）
            UpdateQuickSelectionHighlight(); // 快捷面板行高亮随取消选中即时清（2026-10-06）
            SetTip("Battle_TipSelect");
        }

        private void EnterAiming(SkillButtonDef def)
        {
            if (def == null) return;
            ReturnToMainWorldForAim(); // 地脉视角=纯观察（决策九十一）：起瞄准自动切回主世界（主世界单位在地脉视角隐藏——不切回=瞄准空板）
            _aimDef = def;
            _state = HudState.Aiming;
            _joyMoveSubmitted = false; // P6 摇杆态随会话清（新会话未行进）
            _pendingAimCell = null; // 新瞄准会话待定清零
            _pendingTargetUnitId = null; // 待定目标随会话清零（2026-10-05 指定单位型轮换）
            _pendingAimCm = null; // P4-C 连续落点随会话清（新会话无落点）
            SetAimSelectRing(def, true);
            ClosePopup();
            CloseHandCardDetail(); // 手牌卡详情让位瞄准视线
            ComputeAimCells();
            ShowAimHighlights();
            ApplyStateVisibility(); // Aiming 态：技能盘+移动+取消可见、手牌藏
            RefreshOutlines();      // 瞄准态选中描边保持（目标未定=null 槽）
            SetTip(def.IsMove
                ? "Battle_TipAimMove"
                : IsLineSkill(def) ? "Battle_TipAimDirection" : "Battle_TipAimSkill");
        }

        /// <summary>该按钮技能是否直线型（战技/爆发=十字方向瞄准；延奏/契约/单位指向型爆发=单位指向；
        /// 无目标自施放爆发〔aimMode=None，凛冽轮舞〕=自身格瞄准非直线）。
        /// **移动恒 false**（2026-10-15 箭头显示批勘正：移动=具体格瞄准〔拍板「保留不变」〕，
        /// SkillType.Move 数据过三条件——旧消费点全有 IsMove 前置从未暴露，箭头显示批新消费点
        /// 须防移动误入；null〔部署〕恒 false）</summary>
        private bool IsLineSkill(SkillButtonDef def)
        {
            if (def == null || def.IsMove) return false;
            var data = GetSelectedSkillData(def);
            return data != null
                && data.skillType != SkillType.Interact
                && !data.IsUnitTargeted()
                && !data.IsSelfCast();
        }

        /// <summary>地脉视角=纯观察（决策九十一，2026-10-25）：一切瞄准/打出交互入口先自动切回
        /// 主世界视角（主世界单位在地脉视角隐藏——不切回=瞄空板）。与维度切换钮的「先收瞄准再切」
        /// 互不冲突：本守卫先行时无在途瞄准</summary>
        private void ReturnToMainWorldForAim()
        {
            var player = _session?.Player;
            if (player != null && player.CurrentDimension != BattleDimension.MainWorld)
                player.SetDimensionView(BattleDimension.MainWorld);
        }

        /// <summary>维度切换钮（决策九十一）：主世界↔地脉观察视角循环（纯视觉——战斗逻辑/协议/
        /// Host 零感知，LAN 各端独立）。瞄准/打出会话先收（地脉=纯观察）；地脉视角下按技能/打出键
        /// 经 ReturnToMainWorldForAim 自动切回。音效=开关切换咔哒（UiSfx 备用库存位）</summary>
        private void OnDimensionButtonClicked()
        {
            if (_layoutEditing) return;
            if (_state == HudState.Aiming) ExitAiming(); // 地脉纯观察：先收瞄准会话（拖拽打出/部署/物品/装备同收）
            _session?.Player?.ToggleDimensionView();
            UiSfx.Play(UiSfxKind.Toggle);
        }

        private void ExitAiming()
        {
            if (_state != HudState.Aiming) return;
            // P6 真摇杆持续态：拖拽仍按住时会话被外部收口（单位死亡/阶段切换/换技能重瞄/切维度）→
            // 停止信号（远距目标在途不收=单位走穿棋盘）。已松手的常规退出（提交成功/点击路径）此处
            // _dragAiming=false 不触发——移动提交本身即新 mover。已停/终局态被 Host 拒绝均幂等无害
            if (_dragAiming && _joyMoveSubmitted) SubmitJoystickMove(0, 0, 0, 0);
            _dragAiming = false; // 拖动会话统一收口（松手取消/确认提交/超时/阶段切换同一处清零）
            _dragAimVecX = 0; // P4 自由角向量随会话清（防陈旧向量污染下一次上交）
            _dragAimVecZ = 0;
            _pendingAimCm = null; // P4-C 连续落点随会话清（防陈旧落点污染下一次上交/指示器）
            _hoverAimCm = null;
            _upgradePending = false; // 升命待确认态随会话收口（决策五十五返拍）
            HideDragWheel();      // 圆盘随会话收口（EndDrag 已隐藏，此处=外部退出安全网，幂等）
            ClosePopup();         // 详情面板随会话收口（2026-09-27 点击循环：详情模式=瞄准+面板并开，
                                  // 任何瞄准退出路径——点非可选格/取消钮/确认提交/阶段切换——面板一并收）
            // 待定金格随高亮 quad 一并消失（ClearHighlights 销 quad），字段清零防陈旧提交
            _pendingAimCell = null;
            _pendingTargetUnitId = null; // 待定目标随会话收口（2026-10-05）
            _hoverAimCell = null; // 悬停态随高亮层同清（quad 已销，残留引用无意义）
            // 部署/使用物品瞄准：回手牌态（无选中单位；_aimDef=null 时 SetAimSelectRing 安全跳过）——
            // 物品会话同构收口（2026-10-16 首个可打出物品批：提交成功/反悔弹回后，数量扣减由
            // ItemConsume 命令镜像驱动——卡本体恒飞回不销毁）
            bool wasDeployAim = _deployAimUnit != 0;
            bool wasItemUseAim = _itemUseAimItem != 0;
            bool wasEquipAim = _equipAimItem != 0; // 装备会话同构收口（装备批 2026-10-18）
            _deployAimUnit = 0;
            _itemUseAimItem = 0;
            _equipAimItem = 0;
            _equipAimDurability = 0; // 装备实例耐久随会话清（决策八十一）
            if (wasDeployAim || wasItemUseAim || wasEquipAim)
            {
                _state = HudState.Idle;
                _aimDef = null;
                _aimCells.Clear();
                _aimRecommendedCells.Clear();
                ClearHighlights();
                ApplyStateVisibility();
                RefreshOutlines(); // 目标描边随会话收口（2026-10-05；部署路径无选中=全灭）
                // 打出卡飞回手牌槽（2026-10-07 返拍「不应该把我的卡销毁」：卡不消耗可重复出战——
                // 提交成功/拖回反悔/取消钮/超时/阶段切换全部同一收口飞回，无消耗销毁路径）
                if (_playedHandCard != null)
                {
                    var played = _playedHandCard;
                    int playedIdx = _handCardSlots.IndexOf(played);
                    if (playedIdx >= 0)
                        FlyHandCardToSlot(played, played.baseScale, HandSlotX(playedIdx, _handCardSlots.Count));
                    else _playedHandCard = null;
                }
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
            RefreshOutlines(); // 目标描边随会话收口、选中描边保持（2026-10-05）
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
                // P4-C 移动域放开（决策九十五蓝图 §4.1「移动=点地走点不限距」）：不再产十字臂格域——
                // 点击=任意板内格直达（Host A* 寻路权威）、拖拽=盘距比例×移动距离上限（ResolveMoveDistance
                // 数据驱动）。旧臂格推荐分色（步进可达链）随臂域退役；落点可走性=Host 落空兜底+快照自愈
                return;
            }

            var skillData = GetSelectedSkillData(_aimDef);
            if (skillData == null) return;

            // 单位指向型（三分支合一，2026-10-05 域单源化：延奏=施法者队伍存活角色〔含施法者自身
            // ——协奏语义，docs/07 蒙德；B-S1b 修正〕；契约=敌方存活〔docs/05 §5.3 目标判定不经格子〕；
            // 单位指向型爆发〔时轮 aimMode=TargetUnit，芭芭拉闪耀奇迹〕=施法者队伍**含尸体**〔复苏目标
            // ——docs/05 §5.4 例外条款：唯一能让尸体站起来的通道〕。队伍口径=**选中单位**的队伍
            // （2026-09-26 选中开放任意单位：查看敌方延奏/契约时目标域随施法者视角）。
            // 推荐分色 v1：单位指向全推荐（目标格即语义本身，无优劣数据可分）。
            // 域枚举=CollectAimTargets 单源（同格轮换/拖动头像盘/高亮格三方同域同序）
            if (skillData.IsUnitTargeted())
            {
                var targets = new List<UnitState>();
                CollectAimTargets(snapshot, null, targets);
                foreach (var u in targets)
                {
                    var c = new BattleCell(u.position.x, u.position.y);
                    _aimCells.Add(c);
                    _aimRecommendedCells.Add(c);
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

            // P4-C 方向型（战技/爆发非指向非自施放）自由角域（决策九十五蓝图 §4.1）：不再产四方向
            // 臂格域——点击=点目标点（射程外钳到射程圆边）、拖拽=盘向任意角+盘距比例×射程。
            // 推荐分色/预计命中=自由角向量预判（PredictDirectionHits 向量重载）随 RefreshAimIndicators
            // 逐帧驱动；射程 cm 真源=DirectionRangeCmOf（DirectionRangeOf 格桥 150cm 会被整除抹尾）
        }

        /// <summary>单位名 → UnitData（快照 unitName 为枚举名字符串；查不到返回 null——
        /// 单位名缺配置时预判按"无碰撞规则"处理，Host 结算仍是权威）</summary>
        private UnitConfig.UnitData TryGetUnitData(string unitName)
        {
            return Enum.TryParse(unitName, out UnitName name) && _unitConfig != null
                ? _unitConfig.GetUnitData(name)
                : null;
        }

        /// <summary>快照单位是否建筑（物品饮用域排除用——UnitConfig.unitType 单源；建筑含协议核心）</summary>
        private bool IsBuildingUnitState(UnitState u)
        {
            var data = u == null ? null : TryGetUnitData(u.unitName);
            return data != null && data.unitType == UnitType.Building;
        }

        /// <summary>快照单位是否有空配件槽（装备批 2026-10-18）：accessoryItems 槽位表（0=空槽）含零位
        /// 即有空——与 Host UnitEquipment.HasFreeAccessorySlot 同构（快照真源，长度=层级槽位数）</summary>
        private static bool HasFreeAccessorySlot(UnitState u)
        {
            if (u?.accessoryItems == null) return false;
            foreach (var slot in u.accessoryItems)
                if (slot == 0) return true;
            return false;
        }

        /// <summary>装备目标合格判定（决策八十一补充耐久域单出口——瞄准域/提交防线两处同源）：
        /// 未持有+有空槽 ∨ 持有该物品且槽位计数器<上限（**同物永不双槽**——持有恒走补充路径，
        /// 满=不入选——「已经满耐久/已叠满，选择单位时要提示」由 HandleAimTap 点选域外满持有者
        /// 给 toast；无耐久概念装备〔Durability 与 EquipStackLimit 均 0〕持有即恒满不入选）。
        /// 上限=SlotCap 统一原语：耐久件=配置 Durability、堆叠件=配置 EquipStackLimit（互斥取大）。
        /// 武器=weaponItem 单槽；配件=槽位表扫描（空位或该物命中即判）</summary>
        private bool EquipTargetQualifies(UnitState u, int itemNameValue)
        {
            var equipData = _itemConfig?.GetItemData((ItemName)itemNameValue);
            if (equipData == null) return false;
            bool isWeapon = equipData.subType == ItemSubType.Weapon;
            int cap = equipData.GetInt(nameof(SkillParamKey.Durability), 0);
            int stack = equipData.GetInt(nameof(SkillParamKey.EquipStackLimit), 0);
            if (stack > cap) cap = stack;
            if (isWeapon)
            {
                if (u.weaponItem == 0) return true; // 未持有+武器槽恒 1（空=有空槽）
                return u.weaponItem == itemNameValue && u.weaponDurability < cap; // 持有→补充域
            }
            if (u?.accessoryItems == null) return false;
            for (int i = 0; i < u.accessoryItems.Count; i++)
            {
                int slot = u.accessoryItems[i];
                if (slot == 0) return true; // 空槽
                if (slot == itemNameValue)
                {
                    int counter = u.accessoryDurability != null && i < u.accessoryDurability.Count
                        ? u.accessoryDurability[i] : 0;
                    return counter < cap; // 持有→补充域（命中即返——同物永不双槽）
                }
            }
            return false;
        }

        /// <summary>装备「已满」提示键分流（决策八十一追记——点选满持有者/提交防线共用）：
        /// 堆叠配件=Battle_EquipStackFull（已叠满）、耐久配件=Battle_EquipDurabilityFull（耐久已满）</summary>
        private string EquipFullToastKey(int itemNameValue)
        {
            var equipData = _itemConfig?.GetItemData((ItemName)itemNameValue);
            return equipData != null && equipData.GetInt(nameof(SkillParamKey.EquipStackLimit), 0) > 0
                ? "Battle_EquipStackFull"
                : "Battle_EquipDurabilityFull";
        }

        /// <summary>快照单位是否持有指定物品（任意槽——武器+配件槽位表扫描；满/未满不判）</summary>
        private static bool UnitHoldsItem(UnitState u, int itemNameValue)
        {
            if (u == null) return false;
            if (u.weaponItem == itemNameValue) return true;
            if (u.accessoryItems == null) return false;
            foreach (var slot in u.accessoryItems)
                if (slot == itemNameValue) return true;
            return false;
        }

        /// <summary>移动推荐预判的落点合法性（P6 残留审计 B4，docs/active/49）：与 Host 连续空间
        /// 同口径——地形可走 + 碰撞圆不重叠（旧「格体积上限 3 + blockAllies 格内判定」已随
        /// 即时制退役）。属提示非校验——Host 结算兜底不变；运行时 Buff 修改不可见。</summary>
        private bool CanMoveEnterPreview(BattleCell cell, UnitState self, UnitConfig.UnitData selfData,
            BattleSnapshot snapshot)
        {
            var forceType = selfData != null ? selfData.normalMoveType : ForceType.Walk;
            if (!_board.Map.IsPassable(cell.x, cell.y, forceType)) return false;

            int selfRadiusCm = Mathf.RoundToInt(
                BattleMetrics.CylinderDiameterOf(selfData) * CellMetrics.CellSizeCm * 0.5f);
            int xCm = cell.CenterXCm, zCm = cell.CenterYCm;
            foreach (var u in snapshot.units)
            {
                if (u.unitId == self.unitId) continue; // 含尸体——尸体保留碰撞
                var occupantData = TryGetUnitData(u.unitName);
                int otherRadiusCm = Mathf.RoundToInt(
                    BattleMetrics.CylinderDiameterOf(occupantData) * CellMetrics.CellSizeCm * 0.5f);
                long touch = selfRadiusCm + otherRadiusCm;
                long dx = u.PosXCm() - xCm;
                long dz = u.PosZCm() - zCm;
                if (dx * dx + dz * dz < touch * touch) return false;
            }
            return true;
        }

        /// <summary>建筑部署星级链前置镜像（决策八十六批C，2026-10-23 追记二改星级链，docs/45 §2
        /// ——Host DeployUnitExecutor.PassesBuildingStarGate 快照同口径）：部署 1★=我方存活 Core tag
        /// 建筑在场；部署 N★(N≥2)=我方存活 N-1★ 建筑在场（高星不抵低星；核心 5★ 恒≠N-1(≤4) 帮不上）。
        /// 不共享队友（playerId 过滤）；核心判据走 UnitConfig tags（单核心时代与 FindMyCorePosition
        /// 等价，世界树批扩 UnitState.tags 时一并迁移）</summary>
        private bool PassesBuildingStarPreview(UnitConfig.UnitData deployData, BattleSnapshot snapshot)
        {
            if (deployData == null || deployData.unitType != UnitType.Building) return true;
            if (snapshot == null) return false;
            bool hasCoreAlive = false, hasRequiredStar = false;
            int requiredStar = deployData.starLevel - 1;
            foreach (var u in snapshot.units)
            {
                if (u.playerId != _myPlayerId || u.isCorpse != 0) continue;
                var data = TryGetUnitData(u.unitName);
                if (data == null || data.unitType != UnitType.Building) continue;
                bool isCore = data.tags != null
                    && System.Array.IndexOf(data.tags, UnitTag.Core) >= 0;
                if (isCore) { hasCoreAlive = true; continue; } // 核心=5★，恒≠N-1(≤4) 不占链位
                if (data.starLevel == requiredStar) hasRequiredStar = true;
            }
            return deployData.starLevel <= 1 ? hasCoreAlive : hasRequiredStar;
        }

        /// <summary>建筑上限镜像（Host PassesBuildingLimit 快照同口径）：我方存活非核心建筑
        /// &lt; DeployUnitExecutor.MaxBuildingsPerPlayer(3)</summary>
        private bool PassesBuildingLimitPreview(UnitConfig.UnitData deployData, BattleSnapshot snapshot)
        {
            if (deployData == null || deployData.unitType != UnitType.Building) return true;
            if (snapshot == null) return false;
            int count = 0;
            string deployName = deployData.unitName.ToString();
            foreach (var u in snapshot.units)
            {
                if (u.playerId != _myPlayerId || u.isCorpse != 0) continue;
                if (u.unitName != deployName) continue; // 同种（同名）计数——拍板 #4 勘正 2026-10-22：每种上限 3
                var data = TryGetUnitData(u.unitName);
                if (data == null || data.unitType != UnitType.Building) continue;
                if (data.tags != null && System.Array.IndexOf(data.tags, UnitTag.Core) >= 0) continue;
                count++;
            }
            return count < DeployUnitExecutor.MaxBuildingsPerPlayer;
        }

        /// <summary>部署锚点集合镜像（Host DeployUnitExecutor.CollectDeployAnchors 快照同口径——
        /// 2026-10-23 拍板「任何自己的建筑即可为锚」）：自己（不共享队友）存活建筑全集（含协议核心）；
        /// 锚序=核心优先+unitId 升序。己方单位对自己恒可见（迷雾不滤己方），快照直读。
        /// 消费方=EnterDeployAim（部署瞄准可选格并集）</summary>
        private List<(BattleCell cell, bool isCore)> CollectDeployAnchorsFromSnapshot(BattleSnapshot snapshot)
        {
            var result = new List<(BattleCell, bool)>();
            if (snapshot == null) return result;
            List<(string unitId, BattleCell cell)> others = null;
            foreach (var u in snapshot.units)
            {
                if (u.playerId != _myPlayerId || u.isCorpse != 0) continue;
                var data = TryGetUnitData(u.unitName);
                if (data == null || data.unitType != UnitType.Building) continue;
                bool isCore = data.tags != null
                    && System.Array.IndexOf(data.tags, UnitTag.Core) >= 0;
                if (isCore)
                {
                    result.Add((u.position, true)); // 核心锚优先登记（锚半径 3/2）
                    continue;
                }
                if (others == null) others = new List<(string, BattleCell)>();
                others.Add((u.unitId, u.position));
            }
            if (others != null)
            {
                others.Sort((a, b) => string.CompareOrdinal(a.unitId, b.unitId));
                foreach (var o in others) result.Add((o.cell, false));
            }
            return result;
        }

        /// <summary>部署落点推荐预判（镜像 DeployUnitExecutor.IsDeployCellValid 碰撞判定链的客户端静态版，
        /// 2026-09-25 拍板「根据碰撞决定」）：星级链/上限全局门（决策八十六批C 追记二——TryDeployOrUpgrade
        /// 拖出拦截为主 UX，此处镜像保持与 Host 校验链同口径）→ 地形层（按部署单位常态移动类型）→ 体积绝对层
        /// （现有+自身 ≤3，最高级不可绕过）→ 阻挡规则层（与格内全部单位互不阻挡才推荐）；碰撞配置读
        /// UnitData——运行时 Buff 修改不可见，属提示非校验，Host 结算兜底。含尸体——尸体保留碰撞。
        /// 部署单位尚未登场无运行时组件——飞行互不阻挡豁免按 UnitData.normalMoveType 配置口径判（2026-09-26 拍板）。</summary>
        private bool CanDeployEnterPreview(BattleCell cell, UnitConfig.UnitData deployData, BattleSnapshot snapshot)
        {
            // 建筑星级链/上限镜像（全局条件——不满足=全红格；正常流已被拖出拦截挡在进入瞄准之前）
            if (deployData != null && deployData.unitType == UnitType.Building
                && (!PassesBuildingStarPreview(deployData, snapshot) || !PassesBuildingLimitPreview(deployData, snapshot)))
                return false;

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
            // 部署瞄准分支（B6c）：点可选格=出战上交（玩家级行动，占本秒拍行动配额）
            if (_deployAimUnit != 0)
            {
                var deployAction = new ActionData
                {
                    playerId = _myPlayerId,
                    actionType = ActionType.DeployUnit,
                    deployUnitName = _deployAimUnit,
                    deployCell = cell,
                };
                // 日志先于 SubmitAction（单机局 SubmitAction 同步触发阶段流转→OnPhaseChanged→ExitAiming
                // 清空会话字段——后打印会把出战单位读成 None，2026-10-16 日志取证勘正）
                GICLog.Info($"[BattleHud] {_myPlayerId} 上交：出战 {(UnitName)_deployAimUnit} @ {cell}");
                _session.SubmitAction(deployAction);
                ExitAiming(); // 打出卡经 ExitAiming 统一飞回手牌槽（卡不消耗可重复出战——2026-10-07 返拍「不应该把我的卡销毁」）
                return true;
            }

            // 使用物品分支（2026-10-16 首个可打出物品批；报障修复精确选人）：点我方单位格=使用上交
            //（玩家级行动占配额、不耗体力——docs/05 §5.0）；目标=延奏同构精确选择——ResolvePendingTarget
            //（点格轮换/precise 立牌直选的待定目标）优先，FindUnitAt 按域找仅作残留回落；建筑不可饮用
            //（兜底防线——域已排除，回落残留态再拦一次）；Host UseItemExecutor 手牌持有/目标/建筑校验兜底
            if (_itemUseAimItem != 0)
            {
                var itemSnapshot = _session.Player.LatestSnapshot;
                var drinker = ResolvePendingTarget(itemSnapshot, cell)
                    ?? FindUnitAt(itemSnapshot, cell, UnitSide.MyTeam); // 存活我方（TeamType 口径；尸体默认排除）
                if (drinker != null && IsBuildingUnitState(drinker))
                    drinker = null; // 建筑防线（域已排除，防回落残留态）
                if (drinker == null)
                {
                    // 待定格在确认时已失效（同拍先手被杀/残留目标已离开）——不提交保持瞄准态，换格/取消
                    GICLog.Info($"[BattleHud] 使用物品目标格 {cell} 无我方存活可饮用单位，未提交");
                    return false;
                }
                var useAction = new ActionData
                {
                    playerId = _myPlayerId,
                    actionType = ActionType.UseItem,
                    useItemId = _itemUseAimItem,
                    targetUnitId = drinker.unitId,
                };
                // 日志先于 SubmitAction（同部署分支勘正——ExitAiming 同步清零会把物品读成 None）
                GICLog.Info($"[BattleHud] {_myPlayerId} 上交：使用 {(ItemName)_itemUseAimItem} → {drinker.unitId}");
                _session.SubmitAction(useAction);
                ExitAiming(); // 打出卡经 ExitAiming 统一飞回手牌槽（数量扣减由 ItemConsume 命令镜像）
                return true;
            }

            // 装备物品分支（装备批 2026-10-18；决策八十一补充耐久）：点可装备的我方单位格=装备/补充
            // 耐久上交（玩家级行动占配额、不耗体力——装备即消耗 docs/01）；目标=饮用同构精确选择
            //（ResolvePendingTarget 优先，FindUnitAt 回落残留态再核合格性——满耐久/槽满/建筑=拦截
            // 不提交保持瞄准态）；**equipDurability=拖出卡的实例耐久**（装备条目按耐久分实例）；
            // Host EquipItemExecutor 手牌实例/目标/槽位/满耐久四重校验兜底
            if (_equipAimItem != 0)
            {
                var equipSnapshot = _session.Player.LatestSnapshot;
                var equipTarget = ResolvePendingTarget(equipSnapshot, cell)
                    ?? FindUnitAt(equipSnapshot, cell, UnitSide.MyTeam); // 存活我方（TeamType 口径；尸体默认排除）
                if (equipTarget != null && IsBuildingUnitState(equipTarget))
                    equipTarget = null; // 建筑防线（域已排除，防回落残留态）
                if (equipTarget != null && !EquipTargetQualifies(equipTarget, _equipAimItem))
                {
                    // 合格性防线（域已排除，防回落残留态——同拍竞态：先手装备把槽占满/补满耐久）。
                    // 提示分流：持有该物品必然满耐久（未满会在合格判定入选）→「耐久已满」；否则=无空槽
                    var rejected = equipTarget;
                    equipTarget = null;
                    // 提示分流：持有该物（必然满——未满会入选）→ 已满键（耐久/堆叠件分流）；否则=无空槽
                    ShowBattleToast(UnitHoldsItem(rejected, _equipAimItem)
                        ? EquipFullToastKey(_equipAimItem) : "Battle_EquipSlotFull");
                }
                if (equipTarget == null)
                {
                    GICLog.Info($"[BattleHud] 装备目标格 {cell} 无可装备的我方存活单位，未提交");
                    return false;
                }
                var equipAction = new ActionData
                {
                    playerId = _myPlayerId,
                    actionType = ActionType.EquipItem,
                    equipItemId = _equipAimItem,
                    equipDurability = _equipAimDurability, // 实例耐久（决策八十一——Host 按此实例消耗）
                    targetUnitId = equipTarget.unitId,
                };
                // 日志先于 SubmitAction（同饮用分支勘正——ExitAiming 同步清零）
                GICLog.Info($"[BattleHud] {_myPlayerId} 上交：装备 {(ItemName)_equipAimItem}" +
                            $"（耐久 {_equipAimDurability}）→ {equipTarget.unitId}");
                _session.SubmitAction(equipAction);
                ExitAiming(); // 打出卡经 ExitAiming 统一飞回（装备段 ItemConsume 镜像扣数量+EquipItem 命令挂图标/刷角标）
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

            // 资源不足拦截（2026-10-12 拍板：技能键始终可点击进瞄准，资源不足在落地时拦截）——
            // 玩家域键（魔神/伙伴）资源不足时弹 toast 并阻止提交；AI 域键（眷属）已被上行防线拦截
            if (!isMove && _aimDef != null)
            {
                var skillData = GetSelectedSkillData(_aimDef);
                if (skillData != null && !HasSkillResources(skillData))
                {
                    GICLog.Info($"[BattleHud] 提交拦截：{sel.unitName} 技能资源不足");
                    ShowBattleToast("Battle_NotEnoughResources");
                    return false;
                }
                // P4-D CD 拦截（决策九十五蓝图 §4.3）：战技/爆发 CD 未就绪=轻 toast 保持瞄准态
                //（与转圈数据同源 _cdReadyAtByUnit；Host SkillExecutor gate「未就绪=落空不扣不播」权威兜底）
                if (skillData != null
                    && (skillData.skillType == SkillType.Normal || skillData.skillType == SkillType.Burst)
                    && SelectedUnitSkillOnCooldown(skillData.skillType))
                {
                    GICLog.Info($"[BattleHud] 提交拦截：{sel.unitName} 技能冷却中");
                    ShowBattleToast("Battle_SkillOnCooldown");
                    return false;
                }
            }
            else if (isMove)
            {
                // 移动消耗=移动技能 costs 或常量兜底（与 ApplyMoveButton 同口径）
                var moveSkill = GetSelectedSkillData(SkillType.Move);
                bool moveResourcesOk = moveSkill != null
                    ? HasSkillResources(moveSkill)
                    : _myStamina >= (SelectedUnitTier() == UnitTier.Companion
                        ? UnitTierHelper.StaminaCostOf(UnitTier.Companion) - 5
                        : UnitTierHelper.StaminaCostOf(SelectedUnitTier()));
                if (!moveResourcesOk)
                {
                    GICLog.Info($"[BattleHud] 提交拦截：{sel.unitName} 移动体力不足");
                    ShowBattleToast("Battle_NotEnoughResources");
                    return false;
                }
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
                action.direction = DeltaToDirection(dx, dy); // 旧客户端立牌朝向镜像（判定真源=aim 向量）
                action.moveMagnitude = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                // P4 即时制（决策九十五）：连续目标点直填新轨（桥兼容——targetX>0 走 tick 移动 1 米/秒）
                action.targetX = cell.CenterXCm;
                action.targetZ = cell.CenterYCm;
            }
            else if (IsLineSkill(_aimDef))
            {
                // 直线型（P4 自由角）：aim 向量=方向真值（Host CompileJudgment 消费）；
                // direction=旧客户端立牌朝向镜像（十字近似——朝向随 aim 优先，见 BattlePlayer SkillCast 分支）
                int dx = cell.x - sel.position.x;
                int dy = cell.y - sel.position.y;
                action.direction = SnapToCardinal(dx, dy);
            }
            else
            {
                // 单位指向型：延奏=点中格上的我方角色（协奏，docs/07 蒙德；B-S1b 修正）；
                // 单位指向型爆发=点中格上的我方角色**含尸体**（B-3 ② 复苏目标——芭芭拉闪耀奇迹）；
                // 契约=点中格上的敌方单位（docs/05 §5.3）。
                // 待定目标优先（2026-10-05：同格多候选点击轮换/拖动头像盘指定的具体目标）；
                // 无待定目标（旧会话/拖动回落）按域找首个——旧行为不变
                var skillData = GetSelectedSkillData(_aimDef);
                bool enemyTargeting = skillData != null && skillData.skillType == SkillType.Contract;
                bool includeCorpses = skillData != null && skillData.skillType == SkillType.Burst;
                var unitAtCell = ResolvePendingTarget(snapshot, cell);
                if (unitAtCell == null)
                    unitAtCell = enemyTargeting ? enemyAtCell : FindUnitAt(snapshot, cell, UnitSide.MyTeam, includeCorpses);
                action.targetUnitId = unitAtCell != null ? unitAtCell.unitId : "";
            }

            // P4 自由角瞄准向量（决策九十五，蓝图 §4.1）：拖拽会话=盘方向真值（任意角）；
            // 点击式=施法者→落点格心。全型统一填装（单位指向型/自施放=朝向参考，判定主载 targetUnitId/自身格）
            FillAimVector(action, sel, cell);
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

        /// <summary>待定瞄准统一提交出口（P4 即放，决策九十五蓝图 §4.1）：拖拽松手/点击可选格两路共用——
        /// 提交成功=SubmitAim 内 ExitAiming+DeselectUnit 收口；此处补相机拖动会话终结（原完成选择
        /// 确认链承载的职责随即放迁移至此）。防线/资源拦截=false 保持瞄准态可再拖/再点
        ///（轻提示已由 NotifySelectionBlockedOrConfirmed 先行）。无待定=无操作</summary>
        private bool TrySubmitPendingAim()
        {
            if (!_pendingAimCell.HasValue) return false;
            var snapshot = _session.Player.LatestSnapshot;
            if (snapshot == null) return false;
            var cell = _pendingAimCell.Value;
            var enemyAtCell = FindUnitAt(snapshot, cell, UnitSide.Enemy);
            if (SubmitAim(cell, enemyAtCell))
            {
                if (_camera != null) _camera.EndDragFollowSession(); // 提交=会话正常终结（相机留位、清快照防误重置）
                return true;
            }
            return false;
        }

        /// <summary>P4 自由角瞄准向量填装（决策九十五，蓝图 §4.1「任意角度」）：拖拽会话向量优先
        ///（_dragAimVecX/Z=盘心→小盘位移归一千分——任意角真值，Host AimVectorOf 直读）；
        /// 点击式=施法者连续位置→落点格心向量（NormalizePerMille 整数归一）。零向量兜底=东
        ///（与 Host AimVectorOf 同向，防御口径）</summary>
        private void FillAimVector(ActionData action, UnitState sel, BattleCell targetCell)
        {
            if (_dragAimVecX != 0 || _dragAimVecZ != 0)
            {
                action.aimX = _dragAimVecX;
                action.aimZ = _dragAimVecZ;
                return;
            }
            // P4-C 点击式自由角：钳边连续落点优先（方向精度=实际点击向）；无落点缓存=落点格心
            int dx, dz;
            if (_pendingAimCm.HasValue)
            {
                dx = _pendingAimCm.Value.x - sel.PosXCm();
                dz = _pendingAimCm.Value.y - sel.PosZCm();
            }
            else
            {
                dx = targetCell.CenterXCm - sel.PosXCm();
                dz = targetCell.CenterYCm - sel.PosZCm();
            }
            if (dx == 0 && dz == 0)
            {
                action.aimX = 1000; // 原地兜底=东（与 Host 同向）
                return;
            }
            var v = SkillHitResolver.NormalizePerMille(dx, dz);
            action.aimX = v.x;
            action.aimZ = v.y;
        }

        /// <summary>操控防线判定单出口（2026-09-29 追拍：点格/拖拽落格/提交三触点同判定同文案；
        /// 2026-10-12 拍板伙伴技能解锁——伙伴不再拦截）：
        /// 返回 null=玩家可操作上交（己方魔神全键/己方伙伴全键/己方伙伴势力技能键=号令入口）；否则返回拦截
        /// toast 键——敌方（含队友）Battle_NotYourUnit / 眷属 Battle_MinorUnit（键均在 PopupText 表）</summary>
        private string GetSelectionBlockToastKey(UnitState sel)
        {
            if (sel.playerId != _myPlayerId) return "Battle_NotYourUnit";
            // 层级=快照 tier 优先（TierOverrideStars 覆盖同源——沙盒层级覆盖经此生效，2026-10-05）；
            // 0=未填回落原星级换算
            var selTier = sel.tier != 0 ? (UnitTier)sel.tier
                : UnitTierHelper.FromStars(TryGetUnitData(sel.unitName)?.starLevel ?? 1);
            if (selTier == UnitTier.Familiar) return "Battle_MinorUnit";
            return null;
        }

        /// <summary>待定格产生时刻轻提示（2026-09-29 追拍：玩家不可操作的技能（敌方/眷属）与已确认
        /// 定死时，点可选格/拖拽松手落格即提示——不再等到按「完成选择」才弹，防玩家不知道自己选了
        /// 但为何后面没执行；2026-10-12 追拍「点格时就提醒，而不是行动提交了才提醒」：**资源不足**
        /// （元能/体力/摩拉/物品——技能键始终可点击进瞄准的代价）同判定同文案提前提示。提示序=
        /// 防线（不可操作最根本）>定死>资源不足；金格照常显示（视觉选择反馈保留）；提交侧防线
        /// 不变=双保险。部署瞄准=玩家级行动无防线、仅定死提示</summary>
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
            if (_actionConfirmed) { ShowBattleToast("Battle_ActionConfirmed"); return; }
            // 资源不足提前提示：与 SubmitAim 提交侧拦截同判定同文案（镜像移动 costs/常量兜底两分支）
            if (_aimDef == null) return;
            if (_aimDef.IsMove)
            {
                var moveSkill = GetSelectedSkillData(SkillType.Move);
                bool moveResourcesOk = moveSkill != null
                    ? HasSkillResources(moveSkill)
                    : _myStamina >= (SelectedUnitTier() == UnitTier.Companion
                        ? UnitTierHelper.StaminaCostOf(UnitTier.Companion) - 5
                        : UnitTierHelper.StaminaCostOf(SelectedUnitTier()));
                if (!moveResourcesOk) ShowBattleToast("Battle_NotEnoughResources");
            }
            else
            {
                var skillData = GetSelectedSkillData(_aimDef);
                if (skillData != null && !HasSkillResources(skillData))
                    ShowBattleToast("Battle_NotEnoughResources");
                // P4-D CD 未就绪提前提示（点格/拖拽落点即报——与提交拦截同源同文案；资源足才轮到 CD 判定）
                else if (skillData != null
                    && (skillData.skillType == SkillType.Normal || skillData.skillType == SkillType.Burst)
                    && SelectedUnitSkillOnCooldown(skillData.skillType))
                    ShowBattleToast("Battle_SkillOnCooldown");
            }
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
            {
                // P4-C 自施放点键直接施放（蓝图 §4.1⑤——无目标自施放爆发〔凛冽轮舞〕点键=立即上交
                // 自身格，复用 SubmitAim 全防线链；拦截=保持瞄准态可再点；拖拽路径维持盘心死区语义）
                var selfData = GetSelectedSkillData(def);
                if (selfData != null && selfData.IsSelfCast())
                {
                    EnterAiming(def);
                    var snap = _session.Player.LatestSnapshot;
                    var sel = snap?.units.FirstOrDefault(u => u.unitId == _selectedUnitId);
                    if (sel != null && _aimCells.Count > 0)
                    {
                        SetPendingAimCell(_aimCells.First());
                        TrySubmitPendingAim();
                    }
                    return;
                }
                EnterAiming(def); // 第一次点击=瞄准模式
            }
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
            if (_session == null) return;
            if (_session.Phase != BattlePhase.Selecting) return;
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
            // P6 真摇杆持续态（蓝图 §3.3「方向持续：松手停」）：移动瞄准拖拽=摇杆语义——松手=停止信号
            //（行进中才发，幂等）+收会话。旧「移动松手即放落点跳跃」随 P6 退役（技能瞄准保持松手即放）；
            // 拖回死区/取消区松手同语义（摇杆回中=停，已在 Update 停过的此处零命令静默）
            if (_aimDef != null && _aimDef.IsMove)
            {
                if (_joyMoveSubmitted) SubmitJoystickMove(0, 0, 0, 0);
                ExitAiming();
                if (_camera != null) _camera.CancelDragFollow(); // 2026-10-06 拍板「取消时重置摄像机位置为瞄准开始时」
                return;
            }
            if (ReleaseOverCancelButton(eventData.position) || !_pendingAimCell.HasValue)
            {
                ExitAiming();
                if (_camera != null) _camera.CancelDragFollow(); // 2026-10-06 拍板「取消时重置摄像机位置为瞄准开始时」——瞬移回拖动起始位
                return;
            }
            // **松手即放**（决策九十五 P4，蓝图 §4.1「拖拽=王者荣耀一致」——推翻 2026-09-26「松手留
            // 待定+完成选择确认」拍板）：有效待定=落格轻提示（防线/资源不足——Notify 内含三段判定）
            // 后立即提交；拦截=轻 toast 保持瞄准态可再拖
            NotifySelectionBlockedOrConfirmed();
            TrySubmitPendingAim();
        }

        /// <summary>拖动瞄准实时解析（单格）：判定基准=**大盘中心**——方向型（移动/直线）=盘心→小盘
        /// 位移定十字方向+盘距比例定步数；指向型（延奏/契约/单位指向型爆发）=**头像阵列命中=唯一
        /// 选择途径**（2026-10-05 拍板「盘内可选角色头像，头像上松手=指定该角色」+2026-10-06 追拍
        /// 「松手没在头像上=视为取消」：悬停头像=该角色所在格金格高亮+待定目标描边——既有格高亮
        /// 行为保留改由头像命中驱动；**未命中头像=无有效瞄准**——拖动中金格即隐、松手取消；
        /// 锥角锁定回落已按拍板移除，头像盘=精确选择器非模糊吸附）。自施放爆发（aimMode=None
        /// 单格）与方向型维持原解析（无头像盘）。输入=_dragDiscLocal（小盘位=盘上位置）。
        /// 无有效瞄准=清待定</summary>
        private void UpdateDragAimPreview(PointerEventData eventData)
        {
            UpdateDragWheel(eventData.position); // 小盘跟手+轮盘/屏幕双夹取（_dragDiscLocal=瞄准唯一输入）
            bool directionSkill = _aimDef != null && (_aimDef.IsMove || IsLineSkill(_aimDef));
            BattleCell? cell;
            UnitState targetUnit = null;
            if (!directionSkill && IsCurrentAimUnitTargeted())
            {
                // 指定单位型：头像阵列命中=唯一选择途径（2026-10-06 追拍「松手没在头像上=视为取消」
                // ——锥角锁定回落已按拍板移除，头像盘=精确选择器非模糊吸附；盘外拖动金格即隐、松手取消）
                int hit = AvatarIndexAtScreenPos(eventData.position);
                SetDragAvatarHover(hit);
                if (hit >= 0)
                {
                    targetUnit = _dragAvatarUnits[hit];
                    cell = targetUnit.position;
                }
                else
                {
                    cell = null;
                }
            }
            else
            {
                // 自施放（2026-10-15 拍板「对于凯亚爆发这样的无需任何瞄准，应当不管拖到矩形盘哪都算，
                // 除了中心那块（已经有）视为取消」）：盘心死区外任意位置=锁定自身格；死区内=null
                // （松手取消走既有链）。取代旧指向锥角锁定——单格候选+锥角语义对自施放别扭
                var selfCastData = _aimDef != null ? GetSelectedSkillData(_aimDef) : null;
                if (selfCastData != null && selfCastData.IsSelfCast())
                {
                    Vector2 d = _dragDiscLocal - _dragWheelCenterLocal;
                    // 自施放瞄准域恒=自身单格（ComputeAimCells 自施放分支），First() 确定性
                    cell = d.sqrMagnitude >= 拖动瞄准小圆盘半径 * 拖动瞄准小圆盘半径 && _aimCells.Count > 0
                        ? (BattleCell?)_aimCells.First()
                        : null;
                }
                else
                {
                    cell = directionSkill
                        ? ComputeDragAimCellFromWheel(_dragDiscLocal)
                        : FindNearestAimCellByWheelDirection(_dragDiscLocal); // 方向型走盘距解析（锥角=非方向非指向形态兜底）
                }
            }
            if (cell.HasValue) SetPendingAimCellWithTarget(cell.Value, targetUnit);
            else ClearPendingAimCell();
            // P4-C：自由角拖拽格内微移（同格异落点）也要驱动指示器三件+向量预判——同格幂等早退护不到
            if (IsFreeAngleAim()) UpdateAimPreviewFx();
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
            _pendingAimCm = null; // P4-C：会话重置（死区/无效路径=无落点）
            // P4 自由角（决策九十五，蓝图 §4.1「拖拽=王者荣耀：方向+距离→松手即放、超圆钳边、拖回取消」
            // ——取代旧十字轴量化）：盘心→小盘位移=瞄准真值——方向=位移归一（任意角）、
            // 距离=盘距比例×臂长（死区缘=最近、盘缘=臂满；比例钳 0..1=超圆钳边）
            Vector2 d = discLocal - _dragWheelCenterLocal;
            if (d.sqrMagnitude < 拖动瞄准小圆盘半径 * 拖动瞄准小圆盘半径) return null; // 盘心死区（拖回取消）
            var snapshot = _session.Player.LatestSnapshot;
            var sel = snapshot?.units.FirstOrDefault(u => u.unitId == _selectedUnitId);
            if (sel == null) return null;

            // 方向=盘位移归一（自由角真值）；向量千分记录随会话（上交消费）
            var dir = d.normalized;
            _dragAimVecX = Mathf.RoundToInt(dir.x * 1000f);
            _dragAimVecZ = Mathf.RoundToInt(dir.y * 1000f);

            // 距离上限（cm，P4-C）：移动=移动技能数据驱动上限（ResolveMoveDistance=10% 移速——拖拽=
            // 快速短距档，「不限距走点」放开的是点击式；蓝图 §4.1 拖拽=王者荣耀方向+距离）；技能=时轮
            // 射程 cm 单源（DirectionRangeCmOf——DirectionRangeOf 格桥会把 150cm 整除抹成 100）
            int rangeCm = _aimDef.IsMove
                ? 移动技能步数上限() * CellMetrics.CellSizeCm
                : EffectCompiler.DirectionRangeCmOf(GetSelectedSkillData(_aimDef)?.timeline);
            if (rangeCm <= 0) return null;

            // 距离=盘距比例×臂长；死区外即拖=最小距离兜底（防零距=原地无效）
            float dead = 拖动瞄准小圆盘半径;
            float span = Mathf.Max(1f, 拖动瞄准大盘半边 - dead);
            float t = Mathf.Clamp01((d.magnitude - dead) / span);
            if (t <= 0f) t = Mathf.Min(0.08f, 1f);

            // 落点=施法者连续位置+方向×距离（cm 域）→格；出棋盘=无效（落点可走性 Host 权威兜底）
            int xCm = Mathf.RoundToInt(sel.PosXCm() + dir.x * (t * rangeCm));
            int zCm = Mathf.RoundToInt(sel.PosZCm() + dir.y * (t * rangeCm));
            var cell = BattleCell.FromCm(xCm, zCm);
            if (!_board.Map.HasTile(cell.x, cell.y)) return null;
            _pendingAimCm = new Vector2Int(xCm, zCm); // P4-C 连续落点缓存（指示器/FillAimVector/相机跟随消费）
            return cell;
        }

        // ==================== P6 真摇杆持续态驱动（蓝图 §3.3 移动命令两形态之二「方向持续」） ====================

        /// <summary>摇杆驱动（Update 每帧；移动瞄准拖拽会话=王者荣耀摇杆语义——按住期间朝向移动，
        /// 松手停）：盘心死区（摇杆回中）→ 停止信号（行进中才发；Host 移除在途 mover+位置校正落位，
        /// 重复停幂等零命令）；方向有效 → 首提/变向超阈值才提交远距目标点（快照位+方向×2000cm，
        /// RTSPathfinder 钳最近可走点兜底；mover 替换链维持行进）。持稳不刷命令——与 AI 0.5s
        /// 重决策替换同路。速度恒全速（盘距比例不映射步长——MOBA 精确速度档后续调参批再评）</summary>
        private void DriveMoveJoystick()
        {
            if (_state != HudState.Aiming || _selectedUnitId == null || _session == null) return;
            bool hasDir = _pendingAimCm.HasValue && (_dragAimVecX != 0 || _dragAimVecZ != 0);
            if (!hasDir)
            {
                if (_joyMoveSubmitted) SubmitJoystickMove(0, 0, 0, 0); // 摇杆回中=停
                return;
            }
            if (_joyMoveSubmitted)
            {
                long dot = (long)_dragAimVecX * _joyLastVecX + (long)_dragAimVecZ * _joyLastVecZ;
                if (dot > 摇杆变向阈值Dot) return; // 变向 <~15°：在途 mover 沿旧向继续，待显著变向再纠正
            }
            var sel = _session?.Player?.LatestSnapshot?.units.FirstOrDefault(u => u.unitId == _selectedUnitId);
            if (sel == null) return;
            float len = Mathf.Sqrt(_dragAimVecX * (float)_dragAimVecX + _dragAimVecZ * (float)_dragAimVecZ);
            if (len < 1f) return;
            int targetX = Mathf.RoundToInt(sel.PosXCm() + _dragAimVecX / len * 摇杆行进距离Cm);
            int targetZ = Mathf.RoundToInt(sel.PosZCm() + _dragAimVecZ / len * 摇杆行进距离Cm);
            SubmitJoystickMove(targetX, targetZ, _dragAimVecX, _dragAimVecZ);
        }

        /// <summary>摇杆行动裸上交：**不走 SubmitAim 出口**（保持瞄准会话——选中/轮盘/相机拖拽会话
        /// 不被提交终结；松手 OnSkillButtonDragEnd 才收会话）。targetX/Z 双零+aim 双零=停止信号
        /// （Host 移除在途 mover+位置校正命令落位——moveContinuous 标记承载语义，P4-E 校正同协议形态）。
        /// turnNumber/submitTick 由 BattleSession.SubmitAction 盖章；归属/眷属/建筑防线 Host 权威校验
        ///（回放 v2：随 SubmitAndResolve 钩入 submit 行，重演走同分支）</summary>
        private void SubmitJoystickMove(int targetX, int targetZ, int aimX, int aimZ)
        {
            if (_session == null || string.IsNullOrEmpty(_selectedUnitId)) return;
            var action = new ActionData
            {
                playerId = _myPlayerId,
                unitId = _selectedUnitId,
                actionType = ActionType.Move,
                moveContinuous = true,
                targetX = targetX,
                targetZ = targetZ,
                aimX = aimX,
                aimZ = aimZ,
            };
            _session.SubmitAction(action);
            _joyMoveSubmitted = targetX != 0 || targetZ != 0;
            if (_joyMoveSubmitted)
            {
                _joyLastVecX = aimX;
                _joyLastVecZ = aimZ;
            }
        }

        /// <summary>指向锥角锁定（兜底保留）：候选目标格的屏幕方向（相对**盘心**——与拖向同基准）与
        /// 「盘心→小盘」拖向夹角最小者锁定，夹角须≤「拖动瞄准指向锥角」（默认 60°）；
        /// 无匹配=无锁定（松手取消）。**自施放已改直锁**（2026-10-15 拍板「不管拖到矩形盘哪都算
        /// 除中心死区」——UpdateDragAimPreview 自施放分支不进此）；现役消费=非方向非指向形态兜底
        /// （Interact 类/未来瞄准形态），预留勿删</summary>
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
            BuildDragAvatarGrid(def); // 指定单位型：盘内候选头像阵列（2026-10-05；其它技能类型直通不建）
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

        /// <summary>圆盘隐藏（松手/会话收口；幂等）——临时挪到盘心的技能键随盘一并还原回槽；
        /// 头像阵列随盘销（2026-10-05）</summary>
        private void HideDragWheel()
        {
            if (_dragWheelRoot != null) _dragWheelRoot.SetActive(false);
            ClearDragAvatarGrid();
            RestoreDragMovedButton();
        }

        // ==================== 拖动瞄准头像阵列盘（2026-10-05 拍板「拖动瞄准矩形盘里出现可选角色的头像，
        // 从左往右从上往下每行每列 4 个；超 16 缩头像变 5×5 以此类推；头像上松手=指定该角色」） ====================
        // 头像牌=复用 QueueSlot.prefab（Plate/AvatarMask/Avatar/Ring 结构，raycastTarget 全关——
        // 命中测试走手动 rect 包含：拖拽中指针被 UGUI 拖拽捕获，不派发 hover 事件）。描环=归属玩家色
        // （BattlePlayerColors 同执行预览口径）；尸体候选（复苏目标）头像灰染。候选=CollectAimTargets 全域。

        /// <summary>构建头像阵列（ShowDragWheel 时调；指定单位型才建，幂等先清）：
        /// cols=4 起、cols*cols&lt;候选数递增（4×4=16 → 5×5=25 以此类推），rows=ceil(count/cols)
        /// 居中排布；头像尺寸=min(横/纵 pitch)×0.86 随 cols 收缩（「缩小每个头像以容纳」）</summary>
        private void BuildDragAvatarGrid(SkillButtonDef def)
        {
            ClearDragAvatarGrid();
            if (_dragWheelRoot == null || def == null) return;
            var skillData = GetSelectedSkillData(def);
            if (skillData == null || !skillData.IsUnitTargeted()) return; // 移动/直线/自施放/部署无头像盘
            var snapshot = _session.Player.LatestSnapshot;
            if (snapshot == null) return;
            var candidates = new List<UnitState>();
            CollectAimTargets(snapshot, null, candidates); // ShowDragWheel 恒在 EnterAiming(def) 之后调（_aimDef==def）
            int count = candidates.Count;
            if (count == 0) return;

            if (_avatarTilePrefab == null)
                _avatarTilePrefab = Resources.Load<GameObject>("Prefabs/Battle/QueueSlot");
            if (_avatarTilePrefab == null)
            {
                GICLog.Warn("[BattleHud] QueueSlot.prefab 未找到（Resources/Prefabs/Battle/QueueSlot）——拖动瞄准头像盘不显示");
                return;
            }

            _dragAvatarGrid = new GameObject("DragAimAvatars", typeof(RectTransform));
            var gridRt = (RectTransform)_dragAvatarGrid.transform;
            gridRt.SetParent(_dragWheelRoot.transform, false);
            gridRt.SetAsLastSibling(); // 阵列画在盘填充/描环之上、小盘之下（小盘随后再抬到最顶）

            int cols = 4;
            while (cols * cols < count) cols++; // 4×4=16 容纳不下 → 5×5（以此类推）
            int rows = Mathf.CeilToInt(count / (float)cols);
            float span = Mathf.Max(1f, 2f * (拖动瞄准大盘半边 - 拖动瞄准头像盘边距));
            float pitchX = span / cols;
            float pitchY = span / Mathf.Max(1, rows);
            const float tileNative = 64f; // QueueSlot 原生 64×64——缩放走 localScale（描环/圆角同比）
            float size = Mathf.Min(pitchX, pitchY) * 0.86f;
            _dragAvatarBaseScale = size / tileNative;

            for (int i = 0; i < count; i++)
            {
                var u = candidates[i];
                int col = i % cols, row = i / cols;
                var tile = Instantiate(_avatarTilePrefab, gridRt, false);
                tile.name = $"Aim_{u.unitId}";
                var rt = (RectTransform)tile.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = _dragWheelCenterLocal + new Vector2(
                    (col - (cols - 1) * 0.5f) * pitchX,
                    -(row - (rows - 1) * 0.5f) * pitchY); // 从左往右、从上往下（画布 y 向上 → 行号取负）
                rt.localScale = Vector3.one * _dragAvatarBaseScale;

                var avatar = rt.Find("AvatarMask/Avatar")?.GetComponent<UnityEngine.UI.Image>();
                var ring = rt.Find("Ring")?.GetComponent<UnityEngine.UI.Image>();
                var data = TryGetUnitData(u.unitName);
                if (avatar != null)
                {
                    MissingImageGuard.Assign(avatar, data != null ? data.avatar : null); // 头像缺失兜底（2026-10-06 全位点接入）
                    avatar.color = u.isCorpse != 0 ? Palette.立牌尸体灰 : Color.white; // 尸体候选（复苏）灰染
                }
                if (ring != null) // 描环=归属玩家色（执行预览同款：直观看出这个单位是谁的）
                {
                    var pc = BattlePlayerColors.Resolve(_session, _myPlayerId, u.playerId);
                    ring.color = new Color(pc.r, pc.g, pc.b, 0.8f);
                }
                _dragAvatarRects.Add(rt);
                _dragAvatarUnits.Add(u);
            }
            if (_dragWheelSmall != null) _dragWheelSmall.SetAsLastSibling(); // 小盘保持最顶（指针盘可见）
        }

        /// <summary>头像阵列销毁（HideDragWheel 单点收口；幂等）</summary>
        private void ClearDragAvatarGrid()
        {
            if (_dragAvatarGrid != null) Destroy(_dragAvatarGrid);
            _dragAvatarGrid = null;
            _dragAvatarRects.Clear();
            _dragAvatarUnits.Clear();
            _dragAvatarHover = -1;
        }

        /// <summary>拖拽中头像命中（手动 rect 包含——指针被拖拽捕获无 UGUI hover 事件；Overlay 画布 camera=null）</summary>
        private int AvatarIndexAtScreenPos(Vector2 screenPos)
        {
            for (int i = 0; i < _dragAvatarRects.Count; i++)
            {
                var rt = _dragAvatarRects[i];
                if (rt == null) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, null)) return i;
            }
            return -1;
        }

        /// <summary>悬停头像放大反馈（金格高亮+目标描边之外的轻量确认；切悬停复位旧格）</summary>
        private void SetDragAvatarHover(int index)
        {
            if (_dragAvatarHover == index) return;
            if (_dragAvatarHover >= 0 && _dragAvatarHover < _dragAvatarRects.Count
                && _dragAvatarRects[_dragAvatarHover] != null)
                _dragAvatarRects[_dragAvatarHover].localScale = Vector3.one * _dragAvatarBaseScale;
            _dragAvatarHover = index;
            if (index >= 0 && index < _dragAvatarRects.Count && _dragAvatarRects[index] != null)
                _dragAvatarRects[index].localScale = Vector3.one * (_dragAvatarBaseScale * 拖动瞄准头像悬停放大);
        }

        /// <summary>圆盘三件懒建（首次拖动起手时建，BattleHud 随战斗实例销毁即回收）</summary>
        private void EnsureDragWheel()
        {
            if (_dragWheelRoot != null || _canvas == null) return;
            // 结构真源=DragAimWheel.prefab（2026-10-26 fix-batch2 ①c 烘焙迁移：全拉伸壳+三盘件
            // 结构/sprite 入 prefab；尺寸随 BattleHud 序列化字段、颜色随 BattlePalette——运行时绑定）
            var prefab = Resources.Load<GameObject>("Prefabs/Battle/DragAimWheel");
            if (prefab == null)
            {
                GICLog.Warn("[BattleHud] DragAimWheel.prefab 未找到（Resources/Prefabs/Battle/）——拖动瞄准盘不显示");
                return;
            }
            var canvasRt = (RectTransform)_canvas.transform;
            _dragWheelRoot = Instantiate(prefab, canvasRt, false);
            var rootRt = (RectTransform)_dragWheelRoot.transform;
            rootRt.SetAsLastSibling(); // 盘画在 HUD 最上层（仅视觉，无射线）
            _dragWheelRoot.SetActive(false); // 初始隐藏（prefab 烘焙 false——保险重申防误存激活态）

            // 大盘两件=裁剪到内容框的圆角矩形素材（补偿恒 1.0——实心盘贴图补偿仅小盘消费）；
            // 填充件按比例内缩见「大盘填充内缩比例」常量注（两素材角弧不同，平齐绘制四角有暗唇）
            _dragWheelBigFill = BindWheelDisc(rootRt, "BigFill", 拖动瞄准大盘半边 * (1f - 大盘填充内缩比例),
                new Color(Palette.按钮底盘.r, Palette.按钮底盘.g, Palette.按钮底盘.b, 0.45f));
            _dragWheelBigRing = BindWheelDisc(rootRt, "BigRing", 拖动瞄准大盘半边,
                new Color(Palette.高亮金.r, Palette.高亮金.g, Palette.高亮金.b, 0.8f));
            _dragWheelSmall = BindWheelDisc(rootRt, "SmallDisc", 拖动瞄准小圆盘半径 * 实心盘贴图补偿,
                Palette.瞄准已选色);
        }

        /// <summary>prefab 盘件绑定（节点/中心锚/sprite=prefab 契约；尺寸=半径×2 与色运行时绑定——
        /// 序列化字段与 Palette 活色单源）</summary>
        private static RectTransform BindWheelDisc(RectTransform root, string name, float radius, Color color)
        {
            var rt = root.Find(name) as RectTransform;
            if (rt == null) return null;
            rt.sizeDelta = new Vector2(radius * 2f, radius * 2f);
            var img = rt.GetComponent<Image>();
            if (img != null) img.color = color;
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

        /// <summary>详情面板自适应摆位（技能键入口——键矩形版核心 PositionSkillPopupBesideKey 共用，
        /// 被动图标详情同链，2026-10-07）</summary>
        private void PositionSkillPopupBesideButton(SkillButtonDef def)
        {
            if (def == null || def.rect == null) return;
            PositionSkillPopupBesideKey(def.rect);
        }

        /// <summary>详情面板自适应摆位核心（2026-09-27 拍板「弹出的详情面板应当出现在该技能的旁边——
        /// 不得遮挡该技能按钮、不得超出屏幕、需要灵活的自适应位置」；2026-10-07 抽出键矩形版：
        /// 主动技能键/被动图标两入口共用）：候选=键四侧（上下左右按键位定偏好序：键在下半=上侧优先、
        /// 在右半=左侧优先）×三种横轴对齐（键心/键近缘/键远缘），逐候选夹进画布 → 源键交叠=硬否决
        /// （键必须保持可点——点击循环依赖它）、其余可见件（技能键/被动图标/取消/完成选择）计软交叠；
        /// 取零交叠的偏好序最早候选，全候选取软交叠最少者，再无则键位夹画布兜底。落位=面板中心位移
        /// （对锚点体系无关，RepositionPanel 同步滑入目标，RelatedPanel 为面板子件随动无需另摆）</summary>
        private void PositionSkillPopupBesideKey(RectTransform keyRect)
        {
            var canvasRt = CanvasRect;
            var panelRect = _skillDetailView != null && _skillDetailView.skillDetailPanel != null
                ? _skillDetailView.skillDetailPanel.GetComponent<RectTransform>()
                : null;
            if (canvasRt == null || panelRect == null || keyRect == null) return;
            if (!RectToCanvasAabb(keyRect, out var keyMin, out var keyMax)) return;
            if (!RectToCanvasAabb(panelRect, out var panelMin, out var panelMax)) return;

            var rect = canvasRt.rect;
            Vector2 keyCenter = (keyMin + keyMax) * 0.5f;
            Vector2 half = (panelMax - panelMin) * 0.5f;
            Vector2 panelCenter = (panelMin + panelMax) * 0.5f;

            // 软避让件：其余可见技能键+被动图标+取消钮+完成选择（能躲则躲，躲不开允许盖；源键=硬避让）
            var softRects = new List<(Vector2 min, Vector2 max)>();
            foreach (var b in _skillButtons)
            {
                if (b.rect == keyRect || b.rect == null || !b.rect.gameObject.activeInHierarchy) continue;
                if (RectToCanvasAabb(b.rect, out var bMin, out var bMax)) softRects.Add((bMin, bMax));
            }
            foreach (var p in _passiveIcons)
            {
                if (p.rt == keyRect || p.rt == null || !p.rt.gameObject.activeInHierarchy) continue;
                if (RectToCanvasAabb(p.rt, out var pMin, out var pMax)) softRects.Add((pMin, pMax));
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

        /// <summary>关联面板摆位（2026-10-10 关联面板半遮主面板修复——SkillDetailView.onRelatedPanelOpening
        /// 宿主回调，link 命中即将滑入时调）：候选=技能面板左侧（同高轴对齐、留按钮间距）；左侧出画布→
        /// 右侧；画布两侧都放不下→夹画布兜底（窄屏允许与面板交叠但保完整可见）。烘焙位（-55 偏移）仅
        /// 兜底，战斗内每次开关联面板都按当前面板实际位重摆——面板本身是自适应摆键旁的，关联面板必须
        /// 跟着面板走。中心位移换算与 PositionSkillPopupBesideKey 收尾同法（父子级两跳 TransformVector）</summary>
        private void PositionRelatedPanelBesideSkillPanel()
        {
            if (_skillDetailView == null) return;
            var canvasRt = CanvasRect;
            var panelRect = _skillDetailView.skillDetailPanel != null
                ? _skillDetailView.skillDetailPanel.GetComponent<RectTransform>() : null;
            var relatedRect = _skillDetailView.relatedPanel != null
                ? _skillDetailView.relatedPanel.GetComponent<RectTransform>() : null;
            if (canvasRt == null || panelRect == null || relatedRect == null) return;
            if (!RectToCanvasAabb(panelRect, out var pMin, out var pMax)) return;
            if (!RectToCanvasAabb(relatedRect, out var rMin, out var rMax)) return;

            var rect = canvasRt.rect;
            Vector2 rHalf = (rMax - rMin) * 0.5f;
            Vector2 rCenter = (rMin + rMax) * 0.5f;
            Vector2 pCenter = (pMin + pMax) * 0.5f;

            // 左侧候选：右缘贴面板左缘-间距；左缘出画布边距 → 换右侧；再夹画布（两侧都窄=允许交叠保可见）
            Vector2 target = new Vector2(pMin.x - 详情面板与按钮间距 - rHalf.x, pCenter.y);
            if (target.x - rHalf.x < rect.xMin + 详情面板屏幕边距)
                target = new Vector2(pMax.x + 详情面板与按钮间距 + rHalf.x, pCenter.y);
            target = ClampPanelCenter(target, rHalf, rect);

            var worldDelta = canvasRt.TransformVector((Vector3)(target - rCenter));
            var parentDelta = (Vector2)relatedRect.parent.InverseTransformVector(worldDelta);
            _skillDetailView.RepositionRelatedPanel(relatedRect.anchoredPosition + parentDelta);
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
            if (_skillDetailView == null) return;
            _skillDetailView.ClosePanel();
            // 收口复位停泊位：非摆位开面板（卡详情点技能图标）下次仍落确定性停泊位；
            // 技能键/被动盘路径开面板前必显式重摆，复位对其零影响（面板收态不可见，写位无副作用）
            _skillDetailView.RepositionPanel(_skillPopupDockedPos);
        }

        /// <summary>技能详情面板打开回调（onPanelOpened 订阅，Build 分件接线）：卡牌详情内点
        /// 技能图标开的详情面板须抬到卡详情之上——BattleCardDetail 为运行时实例挂画布最顶、
        /// 烘焙的 BattleSkillDetail 默认 sibling 在其下，不抬升面板会被卡详情盖住半边
        /// （2026-10-10 追拍「应当和背包里一样可以点击」）；技能键/被动盘路径卡详情必已收，
        /// 抬升为无害空操作</summary>
        private void OnSkillPopupOpened()
        {
            if (!HandCardDetailOpen) return;
            if (_skillDetailView != null && _skillDetailView.skillDetailPanel != null)
                _skillDetailView.skillDetailPanel.transform.SetAsLastSibling();
        }

        private void OnCancelButtonClicked()
        {
            if (_layoutEditing) return;
            if (_state == HudState.Aiming)
            {
                ExitAiming();
                if (_camera != null)
                    _camera.CancelDragFollow(); // 取消重置（2026-10-06 拍板）——含松手留待定后按取消钮；点击式瞄准相机未动过=幂等
            }
        }

        /// <summary>顶部「完成选择」按钮（2026-09-26 拍板；同日追加拍板「确认行动后就应当定死了」；
        /// 2026-11-04 决策九十五 P4 即时制收窄，蓝图 §2.4「Pass 按钮、完成选择按钮退役」——下述旧
        /// ①②/定死/提前开演语义已随之退役）：现役职责=升命待确认（EnterUpgradePending）专用
        /// 确认载体；非升命待确认态=无操作（详见方法体 P4 收窄注记）</summary>
        private void OnConfirmButtonClicked()
        {
            if (_layoutEditing) return;
            if (_session == null) return;
            if (_session.Phase != BattlePhase.Selecting) return;
            if (_actionConfirmed) return; // 已定死守卫（按钮置灰幂等直返——旧超时自动按下链路已随回合机退役）

            // 升命待确认（决策五十五返拍「拖入出牌区，不应该直接上交」）：升命卡打出=卡悬浮待确认态，
            // 「完成选择」=确认上交（超时自动按下同链路=自动确认升命，与部署金格待定同语义）
            if (_state == HudState.Aiming && _upgradePending)
            {
                SubmitPlayedUpgrade();
                return;
            }

            // P4 即时制收窄（决策九十五，蓝图 §2.4「Pass 按钮、完成选择按钮退役；升命保留手动确认/取消」）：
            // 瞄准待定已随「点击/拖拽即放」退役（无待定可确认）；Pass=回合配额制语义即时制无效。
            // 本按钮自此=**升命待确认（EnterUpgradePending）专用确认载体**；非升命待确认态=无操作
            //（按钮显隐收窄挂 P4 视觉批）。提交链（含相机终结）由 TrySubmitPendingAim 统一承载
        }

        // ==================== 可选格高亮 + 选中标记（世界层） ====================

        private void ShowAimHighlights()
        {
            ClearHighlights();
            if (_highlightRoot == null) return;

            // P4-C 自由角指示器（决策九十五蓝图 §4.1「真圆/线段/落点指示器」）：方向型（战技/爆发
            // 非指向非自施放）与移动走射程环+方向线+落点金圈三件，四方向箭头与逐格贴片域退役；
            // 单位指向/自施放/部署/物品/装备维持逐格贴片（目标域即语义本身）
            if (IsFreeAngleAim())
            {
                BuildAimIndicators();
                UpdateAimPreviewFx(); // 会话初始态：无落点=指示器隐藏、预计命中描边清
                return;
            }

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
                // 瞄准层显式化（2026-10-15 四轮拍板：层号入 BattleMetrics 单源，消费方勿写字面量）
                quad.GetComponent<MeshRenderer>().sortingOrder = BattleMetrics.AimOverlaySortingOrder;
                _highlightQuads.Add(quad);
                _aimQuadByCell[cell] = quad.GetComponent<MeshRenderer>(); // 待定金格材质换装索引
            }

            // 自施放光环范围提示（2026-10-15 拍板「额外加上范围提示……完整的一块」）：技能施加带
            // 作用半径的行为族 Buff（寒冰之棱）时，以自身为中心整块提示 (2R+1)² 格——半径智能随命座
            //（3命 +C3Radius：0命 3×3/3命 5×5）
            var selfCastData = _aimDef != null ? GetSelectedSkillData(_aimDef) : null;
            if (selfCastData != null && selfCastData.IsSelfCast() && _aimCells.Count > 0)
            {
                var snap = _session?.Player?.LatestSnapshot;
                var selState = snap != null
                    ? snap.units.FirstOrDefault(u => u.unitId == _selectedUnitId) : null;
                int auraRadius = SelfCastAuraRadiusOf(selfCastData, selState);
                if (auraRadius > 0) ShowAimRadiusBlock(_aimCells.First(), auraRadius);
            }
            UpdateAimPreviewFx(); // 会话初始态：预计命中描边清（悬停/待定起再驱动）
        }

        // ==================== 自施放光环范围提示（2026-10-15 拍板「额外加上范围提示……完整的一块，
        // 可以参考滚动带」——寒冰之棱半径提示；复用原语供未来「选格降闪电攻击半径内敌人」类技能） ====================

        /// <summary>自施放技能的光环提示半径：技能 effects 中 ApplyBuff 的 buff 资产经
        /// BuffConfig.AuraRadius/ConstellationAuraBonus 虚方法单源解析（2026-10-20 审查批#6：
        /// 原 IcicleBuffConfig 硬编码分支退役——任何光环族自动生效），客户端按快照 constellation
        /// 预估与 IcicleBuff.OnEffectBeat 服务端同口径（0命 3×3/3命 5×5）；无光环 buff=0 不显示。
        /// 命座参数载体=该单位 Talent 技能（SourceConstellation 客户端镜像）</summary>
        private int SelfCastAuraRadiusOf(SkillConfig.SkillData skillData, UnitState sel)
        {
            if (skillData == null || !skillData.HasEffects || sel == null) return 0;
            SkillConfig.SkillData talent = null;
            var unitData = TryGetUnitData(sel.unitName);
            if (unitData != null)
                foreach (var s in unitData.skills)
                    if (s != null && s.data != null && s.data.skillType == SkillType.Talent) { talent = s.data; break; }
            foreach (var eff in skillData.effects)
            {
                if (eff == null || eff.kind != SkillEffectKind.ApplyBuff) continue;
                var cfg = eff.buffAsset != null ? eff.buffAsset : BuffConfig.OfType(eff.buffType);
                if (cfg == null || cfg.AuraRadius <= 0) continue;
                return cfg.AuraRadius + cfg.ConstellationAuraBonus(sel.constellation, talent);
            }
            return 0;
        }

        /// <summary>显示范围提示整块：以 centerCell 为中心铺 (2R+1)² 格单 quad（实心白底+整块外缘
        /// 黑描边——格子高亮同语言，色=瞄准推荐色与推荐格一致；层=BattleMetrics.AimRadiusBlockSortingOrder
        /// 恒在待定金格贴片之下）。**复用原语**——未来「选格降闪电」类技能瞄准时以待定格为中心调
        /// 本方法（提示中心随待定更新=重复调用幂等重建）</summary>
        private void ShowAimRadiusBlock(BattleCell centerCell, int radius)
        {
            HideAimRadiusBlock();
            if (radius <= 0 || _highlightRoot == null) return;
            if (_aimRadiusBlockMaterial == null)
                _aimRadiusBlockMaterial = BattleViewFactory.CreateAimRadiusBlockMaterial(Palette.瞄准推荐色, radius);
            _aimRadiusBlockMaterial.mainTexture = BattleViewFactory.AimRadiusBlockTexture(radius); // 半径变=换纹理
            _aimRadiusBlockMaterial.color = Palette.瞄准推荐色;
            var quad = BattleViewFactory.CreateQuad(_highlightRoot, "AimRadiusBlock", _aimRadiusBlockMaterial);
            var world = _board.CellToWorld(centerCell);
            quad.transform.position = new Vector3(world.x, _board.GetDecalHeight(centerCell, 0.03f), world.z);
            quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            // 整块边到边略内缩 0.08（两侧各 0.04=格子高亮 0.92 足印同语言）；块心=中心格
            float span = radius * 2 + 1 - 0.08f;
            quad.transform.localScale = new Vector3(span, span, 1f);
            quad.GetComponent<MeshRenderer>().sortingOrder = BattleMetrics.AimRadiusBlockSortingOrder;
            _aimRadiusBlock = quad;
        }

        /// <summary>收掉范围提示整块（ClearHighlights/重建前调用——quad 销毁材质单实例保留复用）</summary>
        private void HideAimRadiusBlock()
        {
            if (_aimRadiusBlock != null) { Destroy(_aimRadiusBlock); _aimRadiusBlock = null; }
        }

        // ==================== P4-C 自由角瞄准指示器（决策九十五蓝图 §4.1：射程环+方向线+落点金圈） ====================

        /// <summary>方向型/移动瞄准会话是否走自由角指示器（IsLineSkill=战技/爆发非指向非自施放；
        /// 移动=不限距走点）——点击式与拖拽式共用；单位指向/自施放/部署/物品/装备维持逐格贴片域</summary>
        private bool IsFreeAngleAim()
            => _aimDef != null && (_aimDef.IsMove || IsLineSkill(_aimDef));

        /// <summary>技能 AoE 真圆半径（cm；0=单体——仅接触命中者）：SkillParamKey.AoERadius 参数单源
        ///（与 Host EffectCompiler 同键）——P4-C 落点金圈半径数据源</summary>
        private static int GetSkillAoERadiusCm(SkillConfig.SkillData data)
        {
            if (data == null) return 0;
            foreach (var p in data.customParams)
                if (p != null && p.key == SkillParamKey.AoERadius && p.value > 0) return p.value;
            return 0;
        }

        /// <summary>cm 连续位置→世界坐标（与 BattleBoard.CellToWorld 同基准：BattleCell.zero 格心
        /// 为原点——P4-C 指示器/相机跟随的连续落点消费）</summary>
        private Vector3 CmToWorld(int xCm, int zCm)
        {
            var o = _board.CellToWorld(BattleCell.zero);
            float s = 1f / CellMetrics.CellSizeCm;
            return new Vector3(o.x + (xCm - BattleCell.zero.CenterXCm) * s, 0f,
                o.z + (zCm - BattleCell.zero.CenterYCm) * s);
        }

        /// <summary>世界坐标→cm 连续位置（TryPickBoardCm 消费——CellToWorld 逆运算）</summary>
        private Vector2Int WorldToCm(Vector3 world)
        {
            var o = _board.CellToWorld(BattleCell.zero);
            float s = CellMetrics.CellSizeCm;
            return new Vector2Int(Mathf.RoundToInt((world.x - o.x) * s + BattleCell.zero.CenterXCm),
                Mathf.RoundToInt((world.z - o.z) * s + BattleCell.zero.CenterYCm));
        }

        /// <summary>板面拾取 cm 连续点（P4-C 自由角：点击/悬停的精确落点——与 TryPickBoardCell 同
        /// 投影链〔TileTopHeight 平面+水面精化〕，出 cm 域+所在格有效性）</summary>
        private bool TryPickBoardCm(Vector2 screenPos, out Vector2Int cm, out bool inBounds)
        {
            cm = default;
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
            cm = WorldToCm(world);
            var cell = BattleCell.FromCm(cm.x, cm.y);
            inBounds = _board.Map.HasTile(cell.x, cell.y);
            return true;
        }

        /// <summary>自由角落点钳边（蓝图 §4.1「射程外钳到圆边」）：技能=以施法者连续位置为圆心、
        /// 射程为半径的圆钳；移动/无射程=不限距直通。落点含格可走性由调用方判（Host 权威兜底）</summary>
        private Vector2Int ClampAimLanding(Vector2Int cm)
        {
            if (_aimRangeCm <= 0) return cm; // 移动/无射程=不限距
            var snapshot = _session?.Player?.LatestSnapshot;
            var sel = snapshot != null ? snapshot.units.FirstOrDefault(u => u.unitId == _selectedUnitId) : null;
            if (sel == null) return cm;
            int dx = cm.x - sel.PosXCm(), dz = cm.y - sel.PosZCm();
            float distSq = (float)dx * dx + (float)dz * dz;
            float rangeSq = (float)_aimRangeCm * _aimRangeCm;
            if (distSq <= rangeSq) return cm;
            float t = _aimRangeCm / Mathf.Sqrt(distSq);
            return new Vector2Int(Mathf.RoundToInt(sel.PosXCm() + dx * t),
                Mathf.RoundToInt(sel.PosZCm() + dz * t));
        }

        /// <summary>建指示器三件（会话级——ShowAimHighlights 调）：射程环（技能=DirectionRangeCmOf
        /// cm 单源、固定带宽不随半径缩放；移动=不限距无环）+方向线（chevron 带自由角）+落点金圈
        ///（AoERadius 真圆/单体=受击圆柱半径；黑描边环=底座盘同语言）。位置姿态随
        /// RefreshAimIndicators 逐帧驱动</summary>
        private void BuildAimIndicators()
        {
            var snapshot = _session.Player.LatestSnapshot;
            var sel = snapshot?.units.FirstOrDefault(u => u.unitId == _selectedUnitId);
            if (sel == null) return;
            var data = GetSelectedSkillData(_aimDef);
            float y = _board.TileTopHeight + 0.03f; // 与瞄准贴片同高（草顶上方恒高于水面波峰）

            // 射程与落点半径：移动=不限距（无环；落点圈=受击圆柱半径）；技能=时轮射程 cm+AoE 真圆；
            // LineBurst（整线迸发）=伤害域为线段，无落点金圈
            _aimRangeCm = 0;
            _aimLandingRadiusWorld = 0.25f; // 单体档=受击圆柱半径（直径 0.5 格）
            _aimIsLineBurst = false;
            if (!_aimDef.IsMove && data != null)
            {
                _aimRangeCm = EffectCompiler.DirectionRangeCmOf(data.timeline);
                _aimIsLineBurst = SkillTimelineQuery.JudgmentClips(data.timeline, SkillJudgmentKind.LineBurst).Count > 0;
                int aoeCm = GetSkillAoERadiusCm(data);
                if (aoeCm > 0) _aimLandingRadiusWorld = aoeCm / (float)CellMetrics.CellSizeCm;
            }

            var origin = _board.CellToWorld(sel.position);
            if (_aimRangeCm > 0)
            {
                if (_aimRingMaterial == null)
                    _aimRingMaterial = BattleViewFactory.CreateTransparentUnlitMaterial(Palette.瞄准推荐色);
                var ringColor = Palette.瞄准推荐色; ringColor.a *= 0.45f;
                _aimRingMaterial.color = ringColor; // 环体恒半透
                _aimRangeRing = BattleViewFactory.CreateRangeRing(_highlightRoot, "AimRangeRing",
                    _aimRangeCm / (float)CellMetrics.CellSizeCm, 瞄准指示环带宽, _aimRingMaterial);
                _aimRangeRing.transform.position = new Vector3(origin.x, y, origin.z);
                _aimRangeRing.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                _aimRangeRing.GetComponent<MeshRenderer>().sortingOrder = BattleMetrics.AimRadiusBlockSortingOrder;
            }

            if (_aimBandMaterial == null)
            {
                _aimBandMaterial = BattleViewFactory.CreateTransparentUnlitMaterial(Color.white);
                _aimBandMaterial.mainTexture = BattleViewFactory.AimArrowBandTexture; // chevron 带纹理复用
            }
            _aimBandLine = BattleViewFactory.CreateQuad(_highlightRoot, "AimBandLine", _aimBandMaterial);
            _aimBandLine.GetComponent<MeshRenderer>().sortingOrder = BattleMetrics.AimOverlaySortingOrder;
            _aimBandLine.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            if (_aimLandingMaterial == null)
                _aimLandingMaterial = BattleViewFactory.CreateTransparentUnlitMaterial(Palette.瞄准已选色);
            var discColor = Palette.瞄准已选色; discColor.a *= 0.5f;
            _aimLandingMaterial.color = discColor;
            _aimLandingDisc = BattleViewFactory.CreateDisc(_highlightRoot, "AimLandingDisc", _aimLandingMaterial);
            _aimLandingDisc.GetComponent<MeshRenderer>().sortingOrder = BattleMetrics.AimOverlaySortingOrder;
            _aimLandingDisc.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _aimLandingOutline = BattleViewFactory.CreateRing(_highlightRoot, "AimLandingOutline",
                BattleViewFactory.DiscOutlineMaterial);
            _aimLandingOutline.GetComponent<MeshRenderer>().sortingOrder = BattleMetrics.AimOverlayTopSortingOrder;
            _aimLandingOutline.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        /// <summary>活动落点 cm（拖拽=待定落点；点击式=悬停落点〔钳边后〕优先、无悬停回落待定）</summary>
        private Vector2Int? ActiveLandingCm()
        {
            if (!IsFreeAngleAim()) return null;
            if (_dragAiming) return _pendingAimCm;
            return _hoverAimCm ?? _pendingAimCm;
        }

        /// <summary>指示器逐帧驱动（UpdateAimPreviewFx 单点调）：方向线姿态=施法者→落点自由角
        ///（chevron tiling=带长）；落点金圈=AoE 真圆半径（LineProjectile=骑首个预计接触点、无接触=
        /// 射程端〔本类投射物距离不受盘距影响——盘只定向〕；LineBurst=无落点圈〔伤害域=整线〕；
        /// 移动=盘距落点）；无落点=两件隐藏。着色：拖拽=待定金、点击悬停=白、静止=推荐/不推荐</summary>
        private void RefreshAimIndicators()
        {
            if (_aimBandLine == null) return; // 非自由角会话（未建指示器）
            var snapshot = _session?.Player?.LatestSnapshot;
            var sel = snapshot != null ? snapshot.units.FirstOrDefault(u => u.unitId == _selectedUnitId) : null;
            var landing = ActiveLandingCm();
            bool show = sel != null && landing.HasValue;
            _aimBandScroll = show;
            _aimBandLine.SetActive(show);
            bool showDisc = show && !_aimIsLineBurst;
            if (_aimLandingDisc != null) _aimLandingDisc.SetActive(showDisc);
            if (_aimLandingOutline != null) _aimLandingOutline.SetActive(showDisc);
            if (!show) return;

            var from = _board.CellToWorld(sel.position);
            var toWorld = CmToWorld(landing.Value.x, landing.Value.y);
            Vector2 delta = new Vector2(toWorld.x - from.x, toWorld.z - from.z);
            float dist = delta.magnitude;
            if (dist < 0.05f)
            {
                _aimBandLine.SetActive(false);
                _aimBandScroll = false;
                if (_aimLandingDisc != null) _aimLandingDisc.SetActive(false);
                if (_aimLandingOutline != null) _aimLandingOutline.SetActive(false);
                return;
            }
            var dir = delta / dist;

            // 带终点：LineBurst/投射物=铺到射程端（虚空截断——方向线即实际弹道，盘距不缩带）；移动=到落点
            float bandEnd;
            if (_aimDef.IsMove) bandEnd = Mathf.Max(0.8f, dist);
            else if (_aimRangeCm > 0)
            {
                float range = _aimRangeCm / (float)CellMetrics.CellSizeCm;
                bandEnd = range;
                for (float t = 0.5f; t <= range; t += 0.5f) // 沿向 0.5 步进虚空截断（与 Host 同语义）
                {
                    var p = new Vector2(from.x, from.z) + dir * t;
                    var stepCell = _board.WorldToCell(new Vector3(p.x, 0f, p.y));
                    if (!_board.Map.HasTile(stepCell.x, stepCell.y)) { bandEnd = t - 0.25f; break; }
                }
            }
            else bandEnd = dist;

            const float bandStart = 0.5f; // 施法者格缘起（与旧箭头带同锚）
            if (bandEnd <= bandStart + 0.3f) bandEnd = bandStart + 0.3f;
            float bandLen = bandEnd - bandStart;
            float yaw = Mathf.Atan2(-dir.y, dir.x) * Mathf.Rad2Deg;
            var rot = Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(0f, 0f, -yaw); // 平铺后绕法线旋（同旧带）
            float mid = (bandStart + bandEnd) * 0.5f;
            _aimBandMaterial.mainTextureScale = new Vector2(bandLen, 1f); // 1 节 chevron/格
            _aimBandLine.transform.position = new Vector3(from.x + dir.x * mid,
                _board.TileTopHeight + 0.03f, from.z + dir.y * mid);
            _aimBandLine.transform.rotation = rot;
            _aimBandLine.transform.localScale = new Vector3(bandLen, 瞄准箭头带宽, 1f);

            // 落点金圈：LineProjectile=骑首个预计接触点（无接触=射程端）；移动=盘距落点
            Vector2 discPoint;
            if (!_aimDef.IsMove && !_aimIsLineBurst && _aimFirstEntryCells >= 0f)
                discPoint = new Vector2(from.x, from.z) + dir * _aimFirstEntryCells;
            else discPoint = new Vector2(toWorld.x, toWorld.z);
            var discCell = _board.WorldToCell(new Vector3(discPoint.x, 0f, discPoint.y));
            var discPos = new Vector3(discPoint.x, _board.GetDecalHeight(discCell, 0.03f), discPoint.y);
            _aimLandingDisc.transform.position = discPos;
            _aimLandingOutline.transform.position = discPos;
            _aimLandingDisc.transform.localScale = new Vector3(_aimLandingRadiusWorld * 2f, _aimLandingRadiusWorld * 2f, 1f);
            _aimLandingOutline.transform.localScale = new Vector3(_aimLandingRadiusWorld * 2f, _aimLandingRadiusWorld * 2f, 1f);

            // 着色（拖拽=待定金 > 点击悬停白 > 静止推荐/不推荐——推荐=该角度预计命中非空）
            Color c = _dragAimVecX != 0 || _dragAimVecZ != 0 ? Palette.瞄准已选色
                : _dragAiming || _hoverAimCm.HasValue ? Palette.瞄准悬停色
                : _aimHitUnitIds.Count > 0 ? Palette.瞄准推荐色 : Palette.瞄准不推荐色;
            _aimBandMaterial.color = c;
        }

        // ==================== 瞄准预览增强（光柱+预计命中描边，2026-10-15 拍板） ====================

        /// <summary>瞄准预览总刷新（悬停/待定变化单点调）：预计命中单位描边+逐单位竖线+自由角指示器。
        /// 非自由角形态（部署/指向/自施放）指示器自守卫（RefreshAimIndicators 空引用早退）</summary>
        private void UpdateAimPreviewFx()
        {
            if (_session == null) return;
            UpdateAimHitPreview();
            RefreshAimIndicators(); // P4-C 自由角指示器（方向型/移动；其余型内部自守卫）
        }

        /// <summary>预计命中竖线材质（按单位归属 playerId 缓存——同玩家多单位共享一实例；
        /// OnDestroy 释放）</summary>
        private Material GetAimHitLineMaterial(string playerId)
        {
            if (!_aimHitLineMats.TryGetValue(playerId, out var m) || m == null)
            {
                var color = BattlePlayerColors.Resolve(_session, _myPlayerId, playerId); // 与描边同源归属玩家色
                color.a = 1f;
                m = BattleViewFactory.CreateTransparentUnlitMaterial(color);
                m.mainTexture = BattleViewFactory.AimMarkerTexture;
                _aimHitLineMats[playerId] = m;
            }
            return m;
        }

        /// <summary>重建预计命中竖线（每命中单位一条，集合变化时调）。**2026-10-15 三轮拍板「光柱
        /// 应当和立牌面平行，而不是垂直地面」**：竖线姿态/底边锚/面内向上全部取单位立牌倾斜组
        /// （UnitView.TiltGroup=立牌面真源——billboard yaw+55° 后倾+悬浮离地+立牌前移全继承，
        /// 竖线躺进立牌平面像贴着单位的光带）；倾斜组缺失（理论不可达）回落垂直地面+格心</summary>
        private void RebuildAimHitLines()
        {
            foreach (var line in _aimHitLines)
                if (line != null) Destroy(line);
            _aimHitLines.Clear();
            if (!显示命中竖线) return; // 2026-10-15 四轮拍板「暂时移除竖线（不删除）」——链路全保留，勾选即复原
            if (_highlightRoot == null || _aimHitUnitIds.Count == 0) return;
            var snapshot = _session?.Player?.LatestSnapshot;
            if (snapshot == null || _board == null || _board.Map == null) return;
            var views = _session.Player.Views;
            foreach (var unitId in _aimHitUnitIds)
            {
                UnitState u = null;
                foreach (var s in snapshot.units)
                    if (s.unitId == unitId) { u = s; break; }
                if (u == null) continue;
                var line = BattleViewFactory.CreateQuad(_highlightRoot, $"AimHitLine_{unitId}",
                    GetAimHitLineMaterial(u.playerId));
                var view = views != null && views.TryGetValue(unitId, out var v) && v != null ? v : null;
                var tilt = view != null ? view.TiltGroup : null;
                if (tilt != null)
                {
                    // 立牌面平行：竖线=倾斜组同姿态，底边=立牌底边（含悬浮/前移），向上=面内向上
                    line.transform.rotation = tilt.rotation;
                    line.transform.position = tilt.position + tilt.up * (命中竖线高度 * 0.5f);
                }
                else
                {
                    // 回落：垂直地面+格心（视图缺失理论不可达）
                    var w = _board.CellToWorld(u.position);
                    line.transform.position = new Vector3(w.x,
                        _board.GetDecalHeight(u.position, 0.04f) + 命中竖线高度 * 0.5f, w.z);
                    line.transform.rotation = AimFxBillboardRotation();
                }
                line.transform.localScale = new Vector3(命中竖线宽度, 命中竖线高度, 1f);
                // 立牌(10)之上、投射物(12)之下——竖线恒可见不被立牌吞（首版看不见的主因之一）
                line.GetComponent<MeshRenderer>().sortingOrder = BattleMetrics.BuffBadgeSortingOrder;
                _aimHitLines.Add(line);
            }
        }

        /// <summary>增强件布告板朝向（与立牌同口径：相机前向压平）——竖线竖直面朝相机（yaw 恒 0，按相机实算防漂移）</summary>
        private Quaternion AimFxBillboardRotation()
        {
            var cam = _camera != null ? _camera.GetComponent<Camera>() : null;
            if (cam == null) return Quaternion.identity;
            var forward = cam.transform.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.001f ? Quaternion.LookRotation(forward, Vector3.up) : Quaternion.identity;
        }

        /// <summary>预计命中单位描边预览（P4-C 自由角向量版）：沿瞄准向量的弹道/线段预判
        ///（与 Host 判定同形——LineProjectile=射线×受击圆、LineBurst=射程线段×受击圆；含尸体=
        /// 完全算判定同口径）；集合无变化零刷新（拖动逐帧防抖）。非自由角/无落点=清空</summary>
        private void UpdateAimHitPreview()
        {
            _aimHitScratch.Clear();
            _aimFirstEntryCells = -1f;
            if (IsFreeAngleAim())
            {
                var snapshot = _session?.Player?.LatestSnapshot;
                var sel = snapshot != null
                    ? snapshot.units.FirstOrDefault(u => u.unitId == _selectedUnitId) : null;
                var landing = ActiveLandingCm();
                if (sel != null && landing.HasValue && _board != null && _board.Map != null)
                {
                    var skill = AimPreviewSkill();
                    if (skill != null)
                    {
                        var v = SkillHitResolver.NormalizePerMille(
                            landing.Value.x - sel.PosXCm(), landing.Value.y - sel.PosZCm());
                        if (v.x == 0 && v.y == 0) v = new Vector2Int(1000, 0); // 零向量兜底=东（与 Host 同向）
                        EffectCompiler.PredictDirectionHits(skill.Timeline, _board.Map, snapshot,
                            (TeamType)sel.team, sel.position, v.x, v.y, _aimHitScratch, out _aimFirstEntryCells);
                        // 迷雾批（2026-10-17，拍板 D4 原话案例）：迷雾内敌方不进预判集合——预计命中
                        // 描边/命中竖线/落点圈三消费方同源于此，一处后滤全链零泄露；首接触被滤=落点回落射程端
                        for (int i = _aimHitScratch.Count - 1; i >= 0; i--)
                            if (!FogVisible(_aimHitScratch[i]))
                            {
                                _aimHitScratch.RemoveAt(i);
                                _aimFirstEntryCells = -1f;
                            }
                    }
                }
            }
            bool changed = _aimHitScratch.Count != _aimHitUnitIds.Count;
            if (!changed)
                for (int i = 0; i < _aimHitScratch.Count; i++)
                    if (!_aimHitUnitIds.Contains(_aimHitScratch[i].unitId)) { changed = true; break; }
            if (changed)
            {
                _aimHitUnitIds.Clear();
                for (int i = 0; i < _aimHitScratch.Count; i++) _aimHitUnitIds.Add(_aimHitScratch[i].unitId);
                RebuildAimHitLines(); // 每命中单位一条竖线随集合重建（2026-10-15 二轮拍板）
                RefreshOutlines();
            }
        }

        /// <summary>方向预判技能实例缓存（按 SkillData 引用失效重建——拖动逐帧调用免分配）</summary>
        private BaseSkill AimPreviewSkill()
        {
            var data = GetSelectedSkillData(_aimDef);
            if (data == null)
            {
                _aimPreviewSkill = null;
                _aimPreviewSkillData = null;
                return null;
            }
            if (_aimPreviewSkill == null || _aimPreviewSkillData != data)
            {
                _aimPreviewSkill = SkillFactory.CreateWithData(data);
                _aimPreviewSkillData = data;
            }
            return _aimPreviewSkill;
        }

        /// <summary>瞄准增强动画推进（Update 每帧，Aiming 态）：方向线 chevron 持续循环滚动
        ///（P4-C 自由角单带）+命中竖线亮度呼吸</summary>
        private void UpdateAimFxAnimation()
        {
            if (_state != HudState.Aiming) return;
            float dt = Time.deltaTime;
            if (_aimBandScroll && _aimBandMaterial != null)
            {
                _aimBandScrollPhase += dt * 瞄准箭头滚动速度;
                _aimBandMaterial.mainTextureOffset = new Vector2(-_aimBandScrollPhase, 0f); // 负向=沿方向线流出
            }
            if (_aimHitLines.Count > 0)
            {
                float pulse = 0.8f + 0.2f * (0.5f - 0.5f * Mathf.Cos(Mathf.PI * 2f * 命中竖线脉动频率 * Time.time));
                foreach (var kv in _aimHitLineMats)
                {
                    if (kv.Value == null) continue;
                    var c = kv.Value.color; // rgb=归属玩家色不动，呼吸只改 a
                    c.a = pulse;
                    kv.Value.color = c;
                }
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
            var old = _pendingAimCell;
            _pendingAimCell = cell;
            if (old.HasValue) RefreshCellVisual(old.Value); // 旧待定格还原（悬停中则转悬停态）
            RefreshCellVisual(cell);
            UpdateAimPreviewFx(); // 待定变化驱动光柱/预计命中描边/箭头态（2026-10-15；同格幂等早退不进此）
        }

        /// <summary>清待定格并还原材质（拖动瞄准用：拖向移出有效区/无有效瞄准时调用——松手即"无待定=取消"）；
        /// 待定目标随格一并清（2026-10-05）</summary>
        private void ClearPendingAimCell()
        {
            if (!_pendingAimCell.HasValue) return;
            var old = _pendingAimCell;
            _pendingAimCell = null;
            _pendingAimCm = null; // P4-C 连续落点随待定清（方向型指示器随隐）
            RefreshCellVisual(old.Value);
            if (_pendingTargetUnitId != null)
            {
                _pendingTargetUnitId = null;
                RefreshOutlines();
            }
            UpdateAimPreviewFx(); // 待定清零回落悬停驱动（2026-10-15 瞄准预览批；拖动无悬停=光柱随清）
        }

        // ==================== 瞄准格材质三态单点（2026-10-06 拍板「点击式瞄准悬停格高亮」） ====================
        // 优先级：待定金格 > 悬停提亮 > 推荐/不推荐——任何格的材质变化（待定变更/悬停进出/还原）
        // 一律走 RefreshCellVisual 单点重算，勿再散写 sharedMaterial 直赋

        private BattleCell? _hoverAimCell;
        private Material _aimHoverMaterial; // 悬停提亮（色=BattlePalette.瞄准悬停色）

        /// <summary>点击式瞄准悬停轮询（Update 调）：Aiming 态且非拖动会话时鼠标所在可选格=悬停提亮；
        /// 拖动会话中恒无悬停（拖动有金格/头像反馈，指针语义不同）。P4-C 自由角（方向型/移动）：
        /// 悬停=板面精确 cm 落点（射程钳边；移动不限距）——指示器三件跟随鼠标，无格高亮</summary>
        private void UpdateAimHover()
        {
            BattleCell? hover = null;
            Vector2Int? hoverCm = null;
            if (_state == HudState.Aiming && !_dragAiming && _camera != null && _board != null && _board.Map != null)
            {
                if (IsFreeAngleAim())
                {
                    // P4-C 自由角点击式悬停：cm 落点跟随鼠标（钳边）——方向线/落点金圈/预计命中随动
                    if (TryPickBoardCm(Input.mousePosition, out var freeCm, out bool cmInBounds) && cmInBounds)
                        hoverCm = ClampAimLanding(freeCm);
                }
                else if (TryPickBoardCell(Input.mousePosition, out var cell, out bool inBounds)
                    && inBounds && _aimCells.Contains(cell))
                    hover = cell;
            }
            // 悬停未变零操作（格与 cm 双轨比对）
            if (_hoverAimCell.HasValue == hover.HasValue
                && (!hover.HasValue || _hoverAimCell.Value.Equals(hover.Value))
                && _hoverAimCm.HasValue == hoverCm.HasValue
                && (!hoverCm.HasValue || _hoverAimCm.Value.Equals(hoverCm.Value)))
                return;
            var old = _hoverAimCell;
            _hoverAimCell = hover;
            _hoverAimCm = hoverCm;
            if (old.HasValue) RefreshCellVisual(old.Value);
            if (hover.HasValue) RefreshCellVisual(hover.Value);
            UpdateAimPreviewFx(); // 悬停变化驱动预计命中描边/自由角指示器（2026-10-15 瞄准预览批+P4-C）
        }

        /// <summary>
        /// 格线可读性驱动（2026-11-10 画面增强批 A 组）：瞄准/拖动瞄准/手牌拖拽/悬停可选格=操作态，
        /// 其余=常态——逐帧平滑趋近后写进棋盘（BattleBoard.SetGridEmphasis01）。纯表现层：
        /// 不进命令流、不进回放；场上有无格线层均直通（棋盘侧幂等守卫）。
        /// </summary>
        private void UpdateGridEmphasis()
        {
            if (_board == null) return;
            bool interacting = _state == HudState.Aiming
                || _dragAiming
                || _handDrag != null
                || _hoverAimCell.HasValue;
            float target = interacting ? 1f : 0f;
            float step = Time.unscaledDeltaTime / Mathf.Max(0.02f, 格线强调过渡秒);
            _gridEmphasis01 = Mathf.MoveTowards(_gridEmphasis01, target, step);
            _board.SetGridEmphasis01(_gridEmphasis01);
        }

        /// <summary>该格材质按三态重算（待定金 > 悬停提亮 > 推荐/不推荐；quad 缺失安全跳过）</summary>
        private void RefreshCellVisual(BattleCell cell)
        {
            if (!_aimQuadByCell.TryGetValue(cell, out var renderer) || renderer == null) return;
            Material material;
            if (_pendingAimCell.HasValue && _pendingAimCell.Value.Equals(cell))
                material = GetAimPendingMaterial();
            else if (_hoverAimCell.HasValue && _hoverAimCell.Value.Equals(cell))
                material = GetAimHoverMaterial();
            else
                material = _aimRecommendedCells.Contains(cell)
                    ? GetAimRecommendedMaterial()
                    : GetAimNotRecommendedMaterial();
            renderer.sharedMaterial = material;
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

        /// <summary>悬停提亮材质（色=BattlePalette.瞄准悬停色；懒建单实例，同生命周期）</summary>
        private Material GetAimHoverMaterial()
        {
            if (_aimHoverMaterial == null)
                _aimHoverMaterial = BattleViewFactory.CreateAimCellMaterial(Palette.瞄准悬停色);
            _aimHoverMaterial.color = Palette.瞄准悬停色;
            return _aimHoverMaterial;
        }

        private void ClearHighlights()
        {
            foreach (var quad in _highlightQuads)
                if (quad != null) Destroy(quad);
            _highlightQuads.Clear();
            _aimQuadByCell.Clear();
            HideAimRadiusBlock(); // 范围提示整块随会话收口（2026-10-15）
            // 瞄准增强件随会话收口（P4-C）：指示器三件（射程环/方向线/落点圈+描边环）销毁、
            // 命中竖线销毁、预计命中描边清
            if (_aimRangeRing != null) { Destroy(_aimRangeRing); _aimRangeRing = null; }
            if (_aimBandLine != null) { Destroy(_aimBandLine); _aimBandLine = null; }
            if (_aimLandingDisc != null) { Destroy(_aimLandingDisc); _aimLandingDisc = null; }
            if (_aimLandingOutline != null) { Destroy(_aimLandingOutline); _aimLandingOutline = null; }
            _aimBandScroll = false;
            _hoverAimCm = null;
            foreach (var line in _aimHitLines)
                if (line != null) Destroy(line);
            _aimHitLines.Clear();
            if (_aimHitUnitIds.Count > 0)
            {
                _aimHitUnitIds.Clear();
                RefreshOutlines(); // 预计命中描边随收口即时熄灭（ExitAiming 后续自调幂等）
            }
        }

        /// <summary>选中单位脚下两束归属玩家色环绕彗尾弧光（2026-09-27 拍板「底座圆盘也要」；
        /// 同日验证后追加拍板：金盘退役——底座圆盘本体不替换，选中仅额外加弧光。
        /// 选中态/瞄准态常显，色=该单位归属玩家色（2026-10-12 拍板取代旧队伍主色口径——与底座圆盘
        /// 同源 BattlePlayerColors 单源，同队队友异色）</summary>
        private void ShowSelectMarker(string unitId)
        {
            var snapshot = _session.Player.LatestSnapshot;
            var unit = snapshot?.units.FirstOrDefault(u => u.unitId == unitId);
            if (unit == null) { HideSelectMarker(); return; }

            var cellWorld = _board.CellToWorld(unit.position);
            // 环绕光束：贴片高度略高于瞄准格（波峰带之上同链），色=归属玩家色（与底座圆盘同源）
            if (_discOrbit == null && _highlightRoot != null)
                _discOrbit = OrbitBeamsWorld.Create(_highlightRoot);
            if (_discOrbit != null)
            {
                _discOrbit.Setup(
                    new Vector3(cellWorld.x, _board.GetDecalHeight(unit.position, 0.045f), cellWorld.z),
                    BattlePlayerColors.Resolve(_session, _myPlayerId, unit.playerId),
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

        /// <summary>迷雾选中弧光巡检（迷雾批 2026-10-17，Update 每帧）：选中单位为迷雾内敌方
        /// （我方/队友恒可见勿扰）→ 弧光随隐。重新可见不强推重显——重新选中自然恢复</summary>
        private void UpdateSelectMarkerFog()
        {
            if (_discOrbit == null || !_discOrbit.gameObject.activeSelf) return;
            if (string.IsNullOrEmpty(_selectedUnitId)) return;
            var fog = _session?.Player?.Fog;
            if (fog == null || !fog.Active) return;
            var snapshot = _session.Player.LatestSnapshot;
            var unit = snapshot?.units.FirstOrDefault(u => u.unitId == _selectedUnitId);
            if (unit != null && !fog.IsUnitVisible(unit)) HideSelectMarker();
        }

        /// <summary>迷雾单位可见性（迷雾批 2026-10-17，拍板 D4「单位类零泄露」单出口）：敌方单位
        /// 所在格未探索=不可见——不可瞄准/不进预判/不进同格查看列表。地形类信息（移动路径绕行/
        /// 部署落点合法性）允许泄露不在本口径。迷雾未激活恒 true（零回归）</summary>
        private bool FogVisible(UnitState u) => _session?.Player?.Fog?.IsUnitVisible(u) ?? true;

        /// <summary>迷雾滤后快照（迷雾批 2026-10-17，拍板 D4）：迷雾内敌方按「不存在」处理——
        /// 移动/部署占用预判（CanMove/CanDeployEnterPreview）与方向推荐（WouldHitEnemyInDirection）
        /// 等一切单位类预览消费此视图（否则「不推荐格」泄露隐藏敌人占位）。迷雾未激活/无隐藏=原样
        /// 直通零分配。**灵魂恒滤（决策九十一 2026-10-25）**：灵魂在地脉维度不占主世界格——
        /// 移动/部署占用预判按不存在（灵魂悬于尸体格不产生阻挡泄露）</summary>
        private BattleSnapshot FogFilteredSnapshot(BattleSnapshot src)
        {
            if (src?.units == null) return src;
            var fog = _session?.Player?.Fog;
            bool fogActive = fog != null && fog.Active;
            if (!fogActive)
            {
                bool anySoul = false;
                foreach (var u in src.units)
                    if (u != null && u.dimension != 0) { anySoul = true; break; }
                if (!anySoul) return src;
                var soulStripped = new BattleSnapshot { turnNumber = src.turnNumber, resources = src.resources };
                soulStripped.units.AddRange(src.MainWorldUnits()); // 主世界域单源（②④迁移）
                return soulStripped;
            }
            bool anyHidden = false;
            foreach (var u in src.units)
                if (u == null || !fog.IsUnitVisible(u) || u.dimension != 0) { anyHidden = true; break; }
            if (!anyHidden) return src;
            var filtered = new BattleSnapshot { turnNumber = src.turnNumber, resources = src.resources };
            foreach (var u in src.units)
                if (u != null && fog.IsUnitVisible(u) && u.dimension == 0)
                    filtered.units.Add(u);
            return filtered;
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

        /// <summary>SkillType 直取版：按类型精确分拣、不做首技能 fallback——
        /// 无该类型条目返回 null 让调用方走常量兜底（与 ApplyMoveButton 的 move 解析同口径；
        /// def 版的首技能 fallback 语义不适用于按类型直取）</summary>
        private SkillConfig.SkillData GetSelectedSkillData(SkillType type)
        {
            var unitData = GetSelectedUnitData();
            return unitData?.skills?.FirstOrDefault(s => s != null && s.data.skillType == type)?.data;
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
                RefreshPassivePanel(unitData); // 建筑=纯查看拍板：被动图标同样清空（空盘不可见）
                return;
            }

            foreach (var def in _skillButtons)
            {
                if (def.IsMove) ApplyMoveButton(def, unitData);
                else ApplySkillButton(def, unitData);
            }
            RefreshPassivePanel(unitData); // 被动技能盘（2026-10-07）：签名比对防抖，选中单位变化才重建
        }

        /// <summary>技能按钮刷新（非移动键）：数据分拣→InitWithData 现有链（图标白底不染+底图染亮元素色+
        /// 主动/被动色环，2026-09-10 拍板规则全在 SkillIconView 内）；无数据=隐藏+置灰（原延奏特例泛化全键）。
        /// 置灰两源（D-5 置灰单源 docs/32 §8；2026-10-04 拍板两源视觉独立可叠加）：
        /// ①「AI 自主」层级门控——眷属/伙伴（任意归属，含敌方——其行动域同属 AI）的移动/战技/爆发键=
        ///   AI 域：45% 半透明（CanvasGroup）；**interactable 保持 true**（点击/拖拽照常进瞄准——显示
        ///   瞄准格直观看到技能范围、可留待定金格；操控权拦截在提交时轻弹窗=SubmitAim 防线，
        ///   2026-09-29 拍板）；
        /// ②「资源不足」（HasSkillResources 门槛，元能/体力=消耗值随快照刷新，体力按层级换算镜像）——
        ///   玩家域键（己方魔神全键/己方伙伴全键）=**interactable 保持 true**（2026-10-12 拍板：即便技能
        ///   未满足释放条件也允许点击，只在落地时拦截——与 AI 域键交互模式统一）+**整键变暗**（图标/底板/圆环
        ///   统一乘暗系数——2026-10-04 拍板「不止图标」）；**己方眷属的 AI 域键同样整键置暗**
        ///   （AI 当前也用不了=信息层，不拦 interactable 保持可查看）——与 45% 半透明同键叠加；
        ///   查看态（敌方单位）无消耗语义不灰（拍板不变）；
        /// ③「资源进度」底面填充（2026-10-06 拍板）——元能/摩拉/物品消耗键底面按 min(持有/需求)
        ///   自下而上填色、体力不参与（SkillResourceProgress），敌方查看态恒满填（同②口径）</summary>
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
                    // 2026-10-12 拍板：即便技能未满足释放条件也允许点击（进瞄准查看范围），
                    // 只在落地时拦截——与 AI 域键交互模式统一
                    selectButton.interactable = data != null;
                    conditionDimmed = data != null && !resourcesOk;
                }
            }
            if (data == null) return;

            def.view.InitWithData(data, unitData, ViewType.OnlyDisplay, _skillDetailView);
            def.view.SetConditionDimmed(conditionDimmed, 资源不足变暗系数); // 须在 InitWithData 后（基准色随刷新重写入）
            // 底面进度填充（2026-10-06 拍板「从下至上按百分比填色」）：元能/摩拉/物品消耗按
            // min(持有/需求) 填色（体力不参与）；敌方查看态恒满填（查看态无消耗语义，同置灰拍板口径）
            def.view.SetResourceProgress(IsSelectedOwnUnit() ? SkillResourceProgress(data) : 1f);

            if (def.nameText != null)
            {
                def.nameText.ClearAllEntries();
                // 2026-10-11 决策六十四：按钮文本显示技能 tag（无 tag 回落技能名——未配置/占位技能保持旧行为）
                def.nameText.AddEntry(data.技能标签 != SkillTag.None ? data.GetTagEntry() : data.skillID.GetEntry());
            }
        }

        /// <summary>该技能键是否受层级门控（D-5，docs/32 §8；2026-10-12 拍板伙伴技能解锁）：
        /// 眷属的 移动/战技/爆发 键=AI 域（置灰）；**伙伴不再置灰**（玩家可直接操控，体力减免 5）；
        /// 势力技能键（延奏/契约）豁免=号令入口对伙伴开放；魔神选中恒不门控（全亮）</summary>
        private bool IsTierGatedButton(SkillButtonDef def)
        {
            if (def == null || def.IsFaction) return false;
            var tier = SelectedUnitTier();
            return tier == UnitTier.Familiar;
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

        /// <summary>选中单位层级（D 批次操控分层，docs/32 §2；无配置数据按眷属档=保守全门控）。
        /// 快照 tier 优先（2026-10-05 试招沙盒：Unit.TierOverrideStars 覆盖经 BuildUnitState 进快照——
        /// 读原星会让沙盒的层级覆盖在客户端门控失效=战技被误拦「伙伴技能自主」）；0=未填回落原星级换算</summary>
        private UnitTier SelectedUnitTier()
        {
            var snapshot = _session?.Player?.LatestSnapshot;
            return TierOfUnit(snapshot?.units.FirstOrDefault(u => u.unitId == _selectedUnitId));
        }

        /// <summary>单位层级单源（快照 tier 优先——TierOverrideStars 覆盖经快照同源；0=未填回落
        /// UnitConfig 星级换算；null/无配置=眷属档保守值）——技能盘选中口径与快捷面板行共用</summary>
        private UnitTier TierOfUnit(UnitState unitState)
        {
            if (unitState != null && unitState.tier != 0) return (UnitTier)unitState.tier;
            var data = unitState != null ? TryGetUnitData(unitState.unitName) : null;
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

        /// <summary>技能资源门槛单源（统一消耗模型，docs/30 §2.3——预判/结算同形纪律）：
        /// 逐条镜像 ResourceGate.Has（元能=选中单位快照 energy / 体力·摩拉=本端缓存 /
        /// 物品=本地手牌镜像条目 count——Host 侧 HasAll 同口径）。
        /// **C-2 起消耗全量迁移完成：costs 空=免费技能**（无消耗语义，数据即事实）；
        /// 体力条目按**选中单位层级换算**镜像（D 批次操控分层 docs/32 §5.2——实际扣值=施法者
        /// 层级表 眷属0/伙伴5/魔神10，声明值=基准/校验值；与 ResourceGate.StaminaAmountOf 同口径）；
        /// **2026-10-12 拍板伙伴技能解锁：玩家操控伙伴=体力减免 5（可减免为负数=获得体力），
        /// 减免后 ≤0=门槛恒过**；
        /// 非体力条目持有量走 HeldResourceAmount 单源（与底面进度填充 SkillResourceProgress 共用）；
        /// 旧 EnergyCost 参数/体力类型分档双查已退役</summary>
        private bool HasSkillResources(SkillConfig.SkillData skillData)
        {
            if (skillData == null || !skillData.HasCosts) return true; // 免费技能/空数据
            var selUnitData = GetSelectedUnitData();
            var selTier = SelectedUnitTier();
            foreach (var cost in skillData.costs)
            {
                if (cost == null || cost.amount <= 0) continue;
                if (cost.kind == CostKind.Stamina)
                {
                    int amount = selUnitData != null
                        ? UnitTierHelper.StaminaCostOf(selTier)
                        : cost.amount; // 无配置兜底按声明值（理论不可达）
                    // 玩家操控伙伴=体力减免 5（2026-10-12 拍板，可减免为负数=获得体力）
                    if (selTier == UnitTier.Companion) amount -= 5;
                    if (amount > 0 && _myStamina < amount) return false;
                    continue;
                }
                if (HeldResourceAmount(cost) < cost.amount) return false;
            }
            return true;
        }

        /// <summary>非体力消耗条目的持有量读取单源（门槛 HasSkillResources 与底面进度填充
        /// SkillResourceProgress 共用——两消费方同源勿散抄）：元能=选中单位快照 energy /
        /// 摩拉=本端缓存 / 物品=手牌镜像条目计数（同 Host ResourceGate 口径）</summary>
        private int HeldResourceAmount(SkillCostEntry cost)
        {
            if (cost.kind == CostKind.Energy)
            {
                var snapshot = _session?.Player?.LatestSnapshot;
                var sel = snapshot?.units.FirstOrDefault(u => u.unitId == _selectedUnitId);
                return sel?.energy ?? 0;
            }
            return HeldPoolAmount(cost);
        }

        /// <summary>玩家池资源持有量（摩拉/物品/同类任意——元能与体力除外）：选中键与快捷面板行
        /// （UnitHeldAmount）两路径共用单源；含未接路由消耗种类的镜像安全网 Warn（批7②——
        /// Host ResourceGate 为权威，漏接路由按可支付乐观口径并提示补镜像）</summary>
        private int HeldPoolAmount(SkillCostEntry cost)
        {
            switch (cost.kind)
            {
                case CostKind.Mora:
                    return _myMora;
                case CostKind.Item:
                    return CountHandItem(cost.item);
                case CostKind.AnyItem:
                    return CountHandAnyItem(cost.subType);
                default:
                    GICLog.Warn($"[BattleHud] HeldPoolAmount 未接路由的消耗种类 {cost.kind}——按可支付处理（请补镜像路由）");
                    return int.MaxValue;
            }
        }

        /// <summary>技能资源进度（2026-10-06 拍板「消耗键底面按百分比自下而上填色——爆发/延奏/契约等
        /// 让玩家一眼看出进度」）：非体力消耗逐条取 min(持有/需求)、钳 0~1——多条目取最紧一环
        /// （如 元能50+摩拉200 双耗时水位=两者较小者）；**体力不参与**（拍板排除——按层级换算
        /// 的玩家资源无「攒进度」观感语义，移动/战技键恒满填）；无非体力消耗=1（满填视觉恒等旧版）</summary>
        private float SkillResourceProgress(SkillConfig.SkillData skillData)
        {
            if (skillData == null || !skillData.HasCosts) return 1f;
            float progress = 1f;
            bool any = false;
            foreach (var cost in skillData.costs)
            {
                if (cost == null || cost.amount <= 0 || cost.kind == CostKind.Stamina) continue;
                any = true;
                progress = Mathf.Min(progress, HeldResourceAmount(cost) / (float)cost.amount);
            }
            return any ? Mathf.Clamp01(progress) : 1f;
        }

        /// <summary>本地手牌镜像指定物品持有数（C-1 客户端镜像=LatestSnapshot.resources.handCards 条目；
        /// 物品牌条目 count=局内真源，与 Host LoseCard 判定同源——快照权威；资源门槛与底面进度两消费方共用）</summary>
        private int CountHandItem(ItemName item)
        {
            var snapshot = _session?.Player?.LatestSnapshot;
            if (snapshot == null) return 0;
            PlayerResourceState myRes = null;
            foreach (var r in snapshot.resources)
            {
                if (r.playerId == _myPlayerId) { myRes = r; break; }
            }
            if (myRes == null) return 0;
            int total = 0;
            foreach (var entry in myRes.handCards)
            {
                if ((CardType)entry.cardType == CardType.Item && entry.value == (int)item)
                    total += entry.count;
            }
            return total;
        }

        /// <summary>本地手牌镜像**任意同类**物品合计持有数（C-1 AnyItem 客户端镜像——镜像
        /// ResourceGate.CountAnyItems 同口径：跨同类条目聚合、货币卡不可匹配；ItemConfig 走
        /// CardConfigResolver 与 RebuildHandCards 同链；资源门槛与底面进度两消费方共用）</summary>
        private int CountHandAnyItem(ItemSubType subType)
        {
            var snapshot = _session?.Player?.LatestSnapshot;
            if (snapshot == null) return 0;
            PlayerResourceState myRes = null;
            foreach (var r in snapshot.resources)
            {
                if (r.playerId == _myPlayerId) { myRes = r; break; }
            }
            if (myRes == null) return 0;
            var itemConfig = CardConfigResolver.Instance?.ItemConfig;
            if (itemConfig == null) return 0;
            if (subType == ItemSubType.Currency) return 0; // 货币=账户资源不经物品消耗链（Host 同口径）
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
            return total;
        }

        /// <summary>移动按钮刷新（特殊技能）：数据链走 skills[Move]（InitWithData 染角色元素色底+主动环；
        /// 无 Move 条目单位不隐藏——移动人人可用，仅跳过染色）。图标+按钮标签随单位常态切换
        /// （walk/fly/amphibious → 步行/飞行/两栖 tag，docs/18 决策六"随单位切换与否"待拍板项的数据驱动落地；
        /// 2026-10-11 决策六十四起标签文本=tag，同口径由 normalMoveType 驱动）</summary>
        private void ApplyMoveButton(SkillButtonDef def, UnitConfig.UnitData unitData)
        {
            if (def?.view == null || unitData == null) return;

            var move = unitData.skills?.FirstOrDefault(s => s != null && s.data.skillType == SkillType.Move)?.data;
            if (move != null)
                def.view.InitWithData(move, unitData, ViewType.OnlyDisplay, null);

            // 图标随常态切换（InitWithData 已填配置的 walk.png，飞行/两栖覆盖为对应图；
            // 名称已随 2026-10-11 决策六十四改显 tag，走下方 normalMoveType 派生分支）
            string iconPath = "UI/Skills/walk";
            if (unitData.normalMoveType == ForceType.Fly)
            {
                iconPath = "UI/Skills/fly";
            }
            else if (unitData.normalMoveType == ForceType.Amphibious)
            {
                iconPath = "UI/Skills/amphibious";
            }
            if (def.view.skillIcon != null)
                MissingImageGuard.Assign(def.view.skillIcon, Resources.Load<Sprite>(iconPath)); // 图标缺失兜底（2026-10-06 全位点接入）
            if (def.nameText != null)
            {
                def.nameText.ClearAllEntries();
                // 2026-10-11 决策六十四：移动键标签显示 tag；同日追拍「优先以特色当tag」——
                // 移动技能资产标签优先（安柏移动=采集这类特色语义），无标签（占位/通用 Common_Walk）
                // 才回落 normalMoveType 派生（步行/飞行/两栖，与图标同口径兜底）
                SkillTag moveTag = SkillTag.None;
                if (move != null && move.技能标签 != SkillTag.None) moveTag = move.技能标签;
                if (moveTag == SkillTag.None)
                    moveTag = unitData.normalMoveType == ForceType.Fly ? SkillTag.Flight
                        : unitData.normalMoveType == ForceType.Amphibious ? SkillTag.Amphibious
                        : SkillTag.Walk;
                def.nameText.AddEntry(moveTag.GetEntry());
            }

            // 置灰两源（同 ApplySkillButton D-5 单源口径+2026-10-04 整键变暗扩展）：层级门控（眷属的
            // 移动=AI 域——45% 半透明、interactable 保持 true，点击/拖拽照常进瞄准查看——拦截在提交时轻弹窗；
            // 2026-10-12 拍板伙伴技能解锁——伙伴不再置灰）；
            // 资源门槛（移动消耗走移动技能 costs 镜像 HasSkillResources，无 Move 条目单位=常量兜底
            // Host MoveExecutor.GetMoveCosts 同口径，体力按选中单位层级换算镜像 眷属0/伙伴5/魔神10；
            // 伙伴玩家操控减免 5）——
            // 玩家域（己方魔神+己方伙伴）拦截+整键变暗；己方眷属的 AI 域键资源不足同置暗（可叠加）
            bool tierGated = IsTierGatedButton(def);
            ApplyTierGateVisual(def, tierGated);
            bool conditionDimmed = false;
            if (def.view.selectButton != null)
            {
                var selUnitDataFallback = GetSelectedUnitData();
                var selTierFallback = SelectedUnitTier();
                int fallbackStamina = selUnitDataFallback != null
                    ? UnitTierHelper.StaminaCostOf(selTierFallback)
                    : BattleMetrics.StaminaCostPerAction;
                // 玩家操控伙伴=体力减免 5（2026-10-12 拍板，可减免为负数=获得体力）
                if (selTierFallback == UnitTier.Companion) fallbackStamina -= 5;
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
                    // 2026-10-12 拍板：即便技能未满足释放条件也允许点击（进瞄准查看范围），
                    // 只在落地时拦截——与 AI 域键交互模式统一
                    def.view.selectButton.interactable = true;
                    conditionDimmed = playerDomain && !resourcesOk;
                }
            }
            // 移动键染色在方法头部已刷新（InitWithData 在 gating 前）——置暗/恢复在此收口
            def.view.SetConditionDimmed(conditionDimmed, 资源不足变暗系数);
            // 底面进度填充（2026-10-06 拍板）：移动消耗=体力（拍板排除）常态恒满填；
            // 无 Move 条目单位同样满填（本帧未走 InitWithData，防残留上一单位的进度）
            def.view.SetResourceProgress(IsSelectedOwnUnit() && move != null ? SkillResourceProgress(move) : 1f);
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
