using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;
using UnityEngine.Video;
using GIC.Framework;
using GIC.Data;
using GIC.Tool;
using GIC.UI;
namespace GIC.Battle
{


    /// <summary>
    /// 最小战斗播放器（客户端侧；Host 显示侧同构复用）。
    /// Snapshot 建场 → Segment 驱动动画 → 片播放完成回 ack。
    /// 片开始时刻 =（全场最高攻速 − 本片攻速）÷ 10 秒（docs/22 §1）；
    /// 表现层时序与逻辑层解耦：逻辑即时结算，本类只管"何时播"。
    /// </summary>
    public class BattlePlayer : MonoBehaviour
    {
        [Header("棋盘引用")]
        [SerializeField] private BattleBoard _board;
        [Tooltip("立牌朝向相机（兜底走 CameraContext.Resolve()，战斗场景已解析到战斗相机；场景序列化引用仍是主路径）")]
        [SerializeField] private Camera _viewCamera;

        [Header("播放参数")]
        [Tooltip("播放速率倍率（1=按公式实时；压缩片间等待用）")]
        [SerializeField] private float _playbackSpeed = 1f;

        [Header("立牌视觉")]
        [Tooltip("立牌后仰角（饥荒式斜插卡片：倾角=俯角 55° 时立牌面正对视线完全消压扁；0=完全垂直；2026-09-18 两轮目检修正：方向=顶部远离相机后仰）")]
        [SerializeField, Range(0f, 80f)] private float 立牌后倾角 = 55f;

        [Tooltip("全身立牌（UnitData.立牌图）放大倍数：全身立绘人物在图中占比小，放大对齐头像版人物观感（2026-09-21 先试 2.5；2026-09-27 拍板 2.5→2：站立全身图 tight bounds 撑满 1.375 格超格子、且比安柏视频立牌（主体仅占帧 69~87%）高 15~45%，降至 2 后显示高=1.1 格）；血条/名字/Buff 行尺寸不变、随立牌顶抬高；头像版（无立牌图回落 avatar）恒为原尺寸")]
        [SerializeField, Min(0.1f)] private float 全身立牌放大倍数 = 2f;

        [Header("地脉灵魂表现（决策九十一，2026-10-25）")]
        [Tooltip("灵魂立牌不透明度（半透明冰蓝 tint 强度——「认出是谁的灵魂」）")]
        [SerializeField, Range(0.1f, 1f)] private float 灵魂立牌不透明度 = 0.55f;
        [Tooltip("灵魂漂浮幅度（米——倾斜组正弦幽浮）")]
        [SerializeField, Range(0f, 0.2f)] private float 灵魂漂浮幅度 = 0.05f;
        [Tooltip("灵魂漂浮角速度（度/秒——慢速幽浮）")]
        [SerializeField, Range(0f, 90f)] private float 灵魂漂浮速度 = 22f;
        [Tooltip("灵魂光晕不透明度（立牌后的柔光底）")]
        [SerializeField, Range(0f, 1f)] private float 灵魂光晕不透明度 = 0.30f;

        [Header("维度视角档案（2026-10-26 fix-batch2 ②a：per-dimension 滤镜档案）")]
        [Tooltip("维度滤镜档案（按 BattleDimension 枚举值索引：0=主世界〔占位不消费〕、1=地脉；加霜月等新维度=枚举值+本表加一行，相机 mask/滤镜/地形三件自动扩展）")]
        [SerializeField] private List<DimensionViewProfile> 维度滤镜档案 = new List<DimensionViewProfile>
        {
            null,                              // 0=MainWorld：无滤镜（切回=淡出）
            new DimensionViewProfile(),        // 1=LeyLine：默认值=原单字段初始值
        };

        [Header("投射物箭矢")]
        [Tooltip("箭矢屏幕长度（格）——正式素材长轴按 bounds 归一到该长度，高度随素材纵横比；十字四向全长（2026-09-28 拍板方案 A：屏幕平行布告板+屏幕平面内旋转，上下射=屏幕竖直箭；0.7→0.49=同日目检拍板调小 30%）")]
        [SerializeField, Min(0.1f)] private float 箭矢长度 = 0.49f;

        [Header("箭雨天降（arrow_rain cue；时轮特效轨→整线迸发技能视觉）")]
        [Tooltip("每格每段落箭数（「大量箭矢」密度轴）")]
        [SerializeField, Min(1)] private int 箭雨每格每段箭数 = 2;
        [Tooltip("落箭箭矢缩放（相对「箭矢长度」基准；1=与飞行箭同大）")]
        [SerializeField, Range(0.3f, 2f)] private float 箭雨箭矢缩放 = 1f;
        [Tooltip("落箭起点离落点高度（格；读作从天而降）")]
        [SerializeField, Min(0.5f)] private float 箭雨起始高度 = 2.2f;
        [Tooltip("落下倾斜角（度；0=竖直落下；正=沿安柏射向斜落（起点向安柏一侧侧移——东射自西向东落、北射自南向北落，读作安柏射出的箭坠入格内）、负=逆射向；2026-09-28 拍板「斜着落下」+追拍板「方向应当根据安柏的位置来」；侧移=tan(角)×起始高度（世界倾角），落点/落地时刻/节拍不变）")]
        [SerializeField, Range(-60f, 60f)] private float 箭雨落下倾斜角 = 30f;
        [Tooltip("单箭坠落时长（秒；起飞时刻=段时刻-坠落时长，落地与伤害数字同拍）")]
        [SerializeField, Min(0.02f)] private float 箭雨坠落时长 = 0.14f;
        [Tooltip("落点在格内的随机散布半径（格）")]
        [SerializeField, Range(0f, 0.45f)] private float 箭雨格内散布 = 0.3f;
        [Tooltip("段内每支箭的随机起飞延迟上限（秒；散开成阵雨而非整排齐落）")]
        [SerializeField, Range(0f, 0.2f)] private float 箭雨逐箭散布延迟 = 0.06f;
        [Tooltip("落地后沿箭轴向落点内压入的深度（格；尖入土、尾翘起=插在表面观感，入土段被地形深度裁掉；0=不压入）")]
        [SerializeField, Min(0f)] private float 箭雨入土深度 = 0.15f;
        [Tooltip("落地压入时长（秒；0=瞬间到位）")]
        [SerializeField, Min(0f)] private float 箭雨入土时长 = 0.05f;
        [Tooltip("落地插稳后原地滞留时长（秒；2026-09-28 拍板「箭落地后应当插在表面 3 秒」默认 3）")]
        [SerializeField, Min(0f)] private float 箭雨落地滞留 = 3f;
        [Tooltip("滞留后的淡出时长（秒）")]
        [SerializeField, Range(0f, 1f)] private float 箭雨落地淡出 = 0.18f;

        [Header("箭矢落地（决策七十六元素×地形批：消散箭/箭雨落箭共用的沉水与插地摇晃参数）")]
        [Tooltip("插地箭尾部弹性摇晃时长（秒；正弦衰减，两三个来回即收敛；0=不摇）")]
        [SerializeField, Min(0f)] private float 箭落摇晃时长 = 0.8f;
        [Tooltip("摇晃初始摆角（度；绕埋入尖屏幕面内摆动）")]
        [SerializeField, Range(0f, 30f)] private float 箭落摇晃幅度 = 10f;
        [Tooltip("落水沉入深度（格；箭贴水面下潜后淡出=「沉入水下」）")]
        [SerializeField, Min(0f)] private float 箭落沉水深度 = 0.28f;
        [Tooltip("落水沉没时长（秒；下潜+淡出同程）")]
        [SerializeField, Range(0.05f, 2f)] private float 箭落沉水时长 = 0.5f;
        [Tooltip("消散箭插地角（度；箭身从水平飞行姿态绕尖摆到插地姿态——负=尖端朝下）")]
        [SerializeField, Range(-89f, 0f)] private float 箭落插地角 = -70f;
        [Tooltip("消散箭坠地时长（秒；尖自空中落至表面点+摆正到插地角）")]
        [SerializeField, Range(0.02f, 1f)] private float 箭落坠地时长 = 0.12f;

        // 配色统一走 BattlePalette 配置资产（2026-09-18 统一化批次；原 _teamAColor/_teamBColor 场景序列化值
        // 与代码默认一致，迁移零损失——2026-10-12 拍板起单位显示色=归属玩家色〔HUD 注入解析器〕，
        // Palette 我方/敌方主色仅作 HUD 未绑时的兜底口径）
        private static BattlePalette Palette => BattlePalette.Instance;

        /// <summary>最新快照（HUD/血条刷新用；客户端不持逻辑状态）</summary>
        public BattleSnapshot LatestSnapshot { get; private set; }
        public float PlaybackSpeed => _playbackSpeed;
        public BattleBoard Board => _board;

        /// <summary>战场相机（迷雾批 2026-10-17：草簇探索态重建需相机朝向——与建盘同源；
        /// 序列化槽空时按 CameraContext 兜底，L272 同口径）</summary>
        public Camera ViewCamera => _viewCamera != null ? _viewCamera : CameraContext.Resolve();

        /// <summary>迷雾控制器（迷雾批 2026-10-17，docs/active/44；模式 None 时 EnsureFog 不建件——
        /// BattleScreen.ConfigureFog 注入；消费方=HUD 瞄准/预判/预览过滤单出口）</summary>
        public BattleFog Fog => _fog;

        private BattleFog _fog;

        /// <summary>迷雾模式注入（BattleScreen 装配期调用，建盘前）：None=零开销直通（不建件）；
        /// 身份解析器惰性求值（session 在 ConfigureFog 之后才创建——lambda 捕获字段延后取值）</summary>
        public void ConfigureFog(BattleFogMode mode,
            System.Func<string> localPlayerIdResolver, System.Func<string, TeamType> teamOfResolver)
        {
            if (mode == BattleFogMode.None) { if (_fog != null) { Destroy(_fog); _fog = null; } return; }
            if (_fog == null)
            {
                var go = new GameObject("BattleFog");
                go.transform.SetParent(transform, false);
                _fog = go.AddComponent<BattleFog>();
            }
            _fog.Init(mode, _board, this, localPlayerIdResolver, teamOfResolver);
        }

        /// <summary>快照更新通知（选择阶段头 + 开局）</summary>
        public event Action<BattleSnapshot> SnapshotUpdated;

        /// <summary>片开始播放（参数=本片攻速；执行预览推进行用）。2026-09-29 语义收紧：只对
        /// **攻速行动片**发（部署段/回合结束段/即时行动块不发——它们不按攻速排程，行进推进语义
        /// 只属攻速片；旧消费方=攻速队列高亮已随队列退役）</summary>
        public event Action<int> OnSegmentPlaying;

        /// <summary>片到达（2026-10-26 底部进度线批）：**全类型**（部署/攻速/回合末/即时段均含、
        /// 购买迷你段 sliceIndex=-1 亦含——消费方自行过滤），到达即发不等播放——本地 host 视图与
        /// LAN 客户端经 OnSegment 同源；与 OnSegmentPlaying（只攻速片、播到才发）互补勿混用</summary>
        public event Action<Segment> SegmentReceived;

        /// <summary>行动预告到达（2026-09-29 执行预览：Host 分桶后、首片推送前下发——
        /// 订阅方=执行预览建行；载荷自含攻速/归属/技能索引。命名同 SnapshotUpdated 模式）</summary>
        public event Action<TurnPlanMessage> TurnPlanUpdated;

        /// <summary>玩家资源增量（B6d 经济闭环：摩拉/体力 StatChange 命令消费点——
        /// 参数=玩家ID / 属性子类型（StatKindMora/StatKindStamina）/ 变化量；HUD 订阅即时刷新，
        /// 快照权威兜底）</summary>
        public event Action<string, int, int> OnResourceDelta;

        /// <summary>手牌物品牌消耗（统一消耗模型 C-1，docs/30）：技能吃物品（如酒/苹果）的
        /// ItemConsume 命令消费点——参数=玩家ID / 物品名（ItemName）/ 消耗量 / 实例耐久（决策八十一：
        /// 装备条目按耐久分实例，非装备恒 0）。本类先扣本地快照 handCards 镜像（减尽移除条目），
        /// HUD 订阅刷新对应卡角标；快照权威兜底</summary>
        public event Action<string, int, int, int> OnItemConsumed;

        /// <summary>手牌物品牌获得（命座授予/死亡掠夺）：ItemGain 命令消费点——参数=玩家ID /
        /// 物品名（ItemName）/ 获得量 / 实例耐久（决策八十一：掠夺物耐久不变保留、新鲜授予=配置满值；
        /// 客户端按 (物品,耐久) 条目镜像）。本类先并本地快照 handCards 镜像（有则加数量/无则加卡
        /// ——GainCard 对偶），HUD 订阅重建手牌（新实例=新槽位）；快照权威兜底</summary>
        public event Action<string, int, int, int> OnItemGained;

        /// <summary>获得单位卡事件（购买批 2026-10-22：购买角色/建筑卡入手牌——订阅方 HUD 重建手牌）</summary>
        public event Action<string, int, int> OnUnitCardGained;

        /// <summary>战斗结束事件（2026-09-25 三轮审查 S10 轻量全灭软停；订阅方=HUD 胜负提示，
        /// 结算画面=B8）</summary>
        public event Action<BattleOverMessage> BattleOver;

        /// <summary>技能施放命令到达（P4-D CD 转圈即时起点，决策九十五蓝图 §7）：参数=施放单位 /
        /// 技能类型 / CD 剩余 tick。仅 cdTicks>0 且技能数据可载时发（移动施放 cdTicks=0 不发）——
        /// 快照 attackCdRemainTicks/burstCdRemainTicks 每秒权威校准，本事件=两次快照间新施放的即时起点</summary>
        public event Action<string, SkillType, int> SkillCasted;

        // ===== B7 LAN 客户端相位派生（2026-10-11 联机批；_flow==null 的端=LAN 客户端） =====
        // 客户端无 TurnFlowController——阶段/倒计时从消息流派生（Snapshot→Selecting、Segment→Resolving、
        // BattleOver→Finished）；host/本地端 _flow!=null 恒走 Flow 权威相位，本派生段零生效（各入口守卫）。

        /// <summary>客户端派生相位变化（BattleSession 相位门面转发，HUD 订阅单源）</summary>
        public event Action<BattlePhase, int> ClientPhaseChanged;

        /// <summary>客户端本地选择倒数归零（host 仍权威超时兜底；双端重复提交由 host 忽略）</summary>
        // P6 残留审计清理（docs/active/49 A4）：选择倒计时已随回合机退役，旧
        // ClientSelectTimerExpired 事件与本地倒数递减链一并删除（selectLimitSeconds 恒 0）。

        public BattlePhase ClientPhase => _clientPhase;
        public float ClientSelectRemainingSeconds => _clientSelectRemaining;

        /// <summary>布局编辑期冻结本地倒数（host 端=Flow.SelectTimerPaused 真暂停；客户端仅冻本地显示）</summary>
        public bool ClientPhasePaused { get; set; }

        private BattlePhase _clientPhase = BattlePhase.Idle;
        private int _clientTurn;
        private float _clientSelectRemaining = -1f; // 即时制恒 -1（无选择倒计时；保留字段供 BattleSession 门面读取）

        /// <summary>消息流派生相位（客户端专用；**仅相位真变化**时广播+驱动两态立牌模型——
        /// P6 残留审计修复（docs/active/49 A3）：turnNumber 已是逻辑秒、每秒都变，旧实现把
        /// 「换回合」当相位切换，导致每秒全场立牌重排一次）</summary>
        private void DeriveClientPhase(BattlePhase phase, int turn, int selectLimitSeconds = -1)
        {
            _clientTurn = turn;

            if (phase == BattlePhase.Selecting)
                _clientSelectRemaining = selectLimitSeconds > 0 ? selectLimitSeconds : -1f;

            if (_clientPhase == phase) return;
            _clientPhase = phase;
            OnPhaseChangedForFormations(phase, turn); // 两态模型：客户端本地派生驱动散开/收拢
            ClientPhaseChanged?.Invoke(phase, turn);
        }

        private IBattleTransport _transport;
        private Transform _viewRoot;
        private BattleDamageNumbers _damageNumbers;
        private BattleOverheadBars _overheadBars;
        private Quaternion _billboardRotation = Quaternion.identity;
        private readonly Dictionary<string, UnitView> _views = new Dictionary<string, UnitView>();

        // ==================== P4-E 移动实时化（决策九十五蓝图 §3.3/§7——世代补间+位置自愈） ====================

        /// <summary>移动补间世代（unitId→当前世代号）：同单位新移动命令/位置校正=世代递增，旧补间
        /// 逐段+逐帧自检发现失配即自废（防双补间同帧双写 jitter——玩家连点重定目标/AI 0.5s 重决策）</summary>
        private readonly Dictionary<string, int> _moveGeneration = new Dictionary<string, int>();

        /// <summary>移动播放中的单位集（世代开始加入、finally 世代匹配才移除）——OnSnapshot 位置自愈
        /// 的让位表（移动中不回拉：补间与模拟同速推进，1s 滞后快照回拉会抖）</summary>
        private readonly HashSet<string> _activeMovePlayback = new HashSet<string>();

        /// <summary>cm 连续位置→世界坐标（BattleBoard.CellToWorld 同基准：BattleCell.zero 格心为原点
        ///——P4-E 被挡停点/同格移动终点/快照自愈消费）。
        /// Y=该点所在格的视觉表面高度（地块顶面 0.5、水面/薄冰含波峰带）——2026-11-06「立牌全部陷进地下」
        /// 修复：旧实现把 Y 写死 0f（地块底面），快照自愈每秒按 0.5 落差判定漂移并把全场立牌拉回 y=0，
        /// 移动停点同样落到地下；单位站位与 CellToWorld 必须同一表面口径（§176 贴地件起算面教训）</summary>
        private Vector3 BoardCmToWorld(int xCm, int zCm)
        {
            var o = _board.CellToWorld(BattleCell.zero);
            float s = 1f / CellMetrics.CellSizeCm;
            var cell = BattleCell.FromCm(xCm, zCm);
            return new Vector3(o.x + (xCm - BattleCell.zero.CenterXCm) * s,
                _board.GetVisualSurfaceHeight(cell),
                o.z + (zCm - BattleCell.zero.CenterYCm) * s);
        }

        /// <summary>血条填色解析器（2026-10-15 血条三色批，HUD Bind 注入——先于 BattleStart 建场：
        /// 玩家色模式=归属玩家色、三色模式=自己绿/队友蓝/敌红；未注入回落 view.OwnerColor 现行口径）。
        /// 同 SetPlayerColorOf 注入先例：设置只影响血条填充色，底座圆盘/描边/弧光链路不动</summary>
        private Func<UnitView, Color> _hpBarColorOf;
        public void SetHpBarColorOf(Func<UnitView, Color> resolver) => _hpBarColorOf = resolver;

        /// <summary>单位立牌视图表（unitId → UnitView；HUD 点击拾取/选中描边消费，2026-10-05 拍板）</summary>
        public IReadOnlyDictionary<string, UnitView> Views => _views;

        /// <summary>per-unit 专属弹射物登记（UnitData.专属弹射物/缩放，2026-10-05 拍板「每位伙伴角色
        /// 单独定制弹射物，通用染色箭矢保留给眷属」）：CreateView 建场登记、ClearViews 清空；命中箭/
        /// 消散箭/箭雨落箭三路经 actorUnitId 查此表——命中=专属素材+白染（美术即最终色）、
        /// 未登记=通用 ArrowBolt+箭矢色板元素染色（眷属路径零回归）</summary>
        private readonly Dictionary<string, (Sprite sprite, float scale)> _customProjectiles =
            new Dictionary<string, (Sprite, float)>();

        /// <summary>单位配置（DI 容器 [Bean] 缓存——ConfigManager 产出；2026-09-23 审查 Y10 收口，Bind 时注入）</summary>
        [Autowired] private UnitConfig _unitConfig;
        // P6 残留审计 D1（docs/active/49）：TurnFlowController 已删除——客户端相位恒走消息派生。
        private float _windowNightFactor = 1f;

        /// <summary>单位显示色解析器（2026-10-12 用户拍板「单位显示的颜色应当是玩家色，本游戏没有
        /// 队伍色，否则无法区分哪些单位是自己的，哪些是队友的」）：HUD Bind 注入 playerId→Color
        /// （BattlePlayerColors.Resolve=房间玩家色单源，roster 命中=房间选色、无 roster 回落两表口径
        /// ——同队队友与我天然异色〔房间开局门颜色冲突校验保证〕）；null=HUD 未绑兜底（旧队伍主色口径）。
        /// 消费=CreateView 立牌底座/描边/血条填充全链（底座即判定的视觉语义不变）</summary>
        private Func<string, Color> _playerColorOf;

        /// <summary>注入单位显示色解析器（HUD Bind 调用——先于 BattleStart 建场）</summary>
        public void SetPlayerColorOf(Func<string, Color> resolver) => _playerColorOf = resolver;

        private const float MoveStepSeconds = BattleMetrics.MoveStepSeconds;
        // #10 审查批：段播放 pacing 常量收口 BattleMetrics（Host/客户端契约单源——原为客户端内联字面量）
        private const float CommandStaggerSeconds = BattleMetrics.CommandStaggerSeconds;
        private const float TurnEndDelaySeconds = BattleMetrics.TurnEndDelaySeconds; // 回合结束段固定节拍（不按攻速排程）

        /// <summary>同段直击节拍配对状态（§107 追记，2026-10-02 单表化收口：原双表中
        /// _segmentLastDirectDamageDelay 恒为 beats 末项的镜像冗余，合一）。beats=该攻击者本段
        /// 各次 launchMs=0 直击的弹出延迟；consumed=吸血配对游标——**立即响应配对**（2026-10-01
        /// 拍板 B 勘正「不要错峰」）：第 n 笔自疗随第 n 次直击同帧弹（MOBA 式——launchMs=0 的直击
        /// 按命令 stagger 旧节拍弹、Host 无从随命令携带，段内就近绑定；自疗 source==target 消费
        /// 本方直击拍）；游标耗尽回落末次节拍=同帧各弹（多笔伤害并成一条命令等场景）；
        /// 段首由 PlaySegmentCoroutine 清空防跨段污染；多目标同拍多伤时观感同步（0.12s 槽内）</summary>
        private sealed class DirectDamageBeats
        {
            public readonly List<float> beats = new List<float>();
            public int consumed;

            /// <summary>取下一拍：游标未尽=依次配对；耗尽=末次节拍（beats 空=fallback 调用方节拍）</summary>
            public float NextOrLast(float fallback)
                => consumed < beats.Count ? beats[consumed++]
                : (beats.Count > 0 ? beats[beats.Count - 1] : fallback);
        }

        /// <summary>段内吸血节拍配对表（actor → 直击节拍状态）</summary>
        private readonly Dictionary<string, DirectDamageBeats> _segmentDirectDamage = new Dictionary<string, DirectDamageBeats>();

        /// <summary>命令应用时刻（秒，含播放速率换算）——WYSIWYP 时间通道客户端单源
        /// （2026-10-02 复审收口）：launchMs=0 返回 0=立即；Damage/Heal/Reaction/Energy/Mora/
        /// 投射物起飞各到点消费分支统一走此口，勿散抄公式（新增带时刻命令同走此口）</summary>
        private float LaunchDelayOf(BattleCommand command) => command.launchMs / 1000f / _playbackSpeed;

        public void Bind(IBattleTransport transport, BattleMapData map)
        {
            Wargame.Instance?.Context?.Inject(this); // [Autowired] UnitConfig（Y10）
            _transport = transport;
            if (_viewRoot == null)
            {
                var rootGo = new GameObject("UnitViews");
                rootGo.transform.SetParent(transform, false);
                _viewRoot = rootGo.transform;
            }
        }

        private void OnDestroy()
        {
            // 维度视角清理（2026-10-26 返修）：相机可能跨对局存活（场景复用/沙盒重开等路径）——退出时
            // 把 mask 归位主世界视角（幂等重算同式）+还原底色，防脏值流入下一场（「只剩血条」根因的另一半）
            var cam = _viewCamera != null ? _viewCamera : CameraContext.Resolve();
            if (cam != null)
            {
                int mask = cam.cullingMask;
                foreach (BattleDimension dim in Enum.GetValues(typeof(BattleDimension)))
                {
                    int layer = LayerMask.NameToLayer(BattleViewFactory.DimensionLayerName(dim));
                    if (layer >= 0) mask &= ~(1 << layer);
                }
                int mainLayer = LayerMask.NameToLayer(BattleViewFactory.DimensionLayerName(BattleDimension.MainWorld));
                if (mainLayer >= 0) mask |= 1 << mainLayer;
                cam.cullingMask = mask;
            }
            if (_cameraBgSaved && cam != null)
            {
                cam.backgroundColor = _origCameraBg;
                _cameraBgSaved = false;
            }
        }

        private void OnPhaseChangedForFormations(BattlePhase phase, int turn)
        {
            // 2026-11-07 拍板：同格多单位不再散开站队（「自然即可」）——原选择阶段散开/执行阶段收拢的
            // 两态模型退役，任何阶段都贴各自权威站位（重叠显示）。相位变化仍做一次归零重放，
            // 用于收敛历史上可能残留的队形偏移。
            RefreshAllFormations();
        }

        // P6 残留审计 D5（docs/active/49）：SetPlaybackSpeed 无调用方（回放倍率语义随回合机退役），
        // 已删除；_playbackSpeed 字段保留=表现层倍率恒 1（伤害数字/弹道时长换算同源）。

        private void Update()
        {
            // 立牌面向战场相机（固定斜俯视，逐帧保持即可；相机不动时开销可忽略）
            var cam = _viewCamera != null ? _viewCamera : CameraContext.Resolve();
            if (cam != null)
            {
                var forward = cam.transform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.001f)
                    _billboardRotation = Quaternion.LookRotation(forward, Vector3.up);
            }

            // 建筑窗灯只随战斗独立时钟变化。P6 残留审计修复（docs/active/49 C4/D1）：
            // 即时制无选择倒计时（selectLimitSeconds 恒 0），旧 LAN 客户端倒数链已删除；
            // 时钟兜底改按「每秒 +1 分钟」推导——turnNumber 已是逻辑秒，旧回合常量
            // BattleClockMinutesPerTurn=20 会让昼夜窗灯跑成 20 倍速。
            int battleMinutes = RTSFlowController.BattleClockStartMinutes + BattleMetrics.BattleClockMinutesPerSecond
                * Mathf.Max(0, LatestSnapshot?.turnNumber ?? 0);
            float nightTarget = BuildingModelView.NightTargetForMinutes(battleMinutes);
            _windowNightFactor = Mathf.MoveTowards(_windowNightFactor, nightTarget, Time.deltaTime * 2f);
            foreach (var view in _views.Values)
                if (view != null) view.SetNightEmission(_windowNightFactor);

            // 地脉滤镜灵雾漂移（决策九十一）：滤镜激活（alpha>0）才驱动——锚点慢正弦漂+透明度呼吸
            if (_leyLineFilterGroup != null && _leyLineFilterGroup.alpha > 0.01f)
                AnimateLeyLineWisps();
        }

        // ==================== 地脉维度视角（决策九十一，2026-10-25）：切换 + 阴间滤镜 ====================

        /// <summary>当前维度视角（主世界默认；地脉=HUD 维度钮切换——纯客户端观察视角，
        /// 战斗逻辑/协议/Host 零感知；LAN 各端独立切换互不同步）</summary>
        public BattleDimension CurrentDimension => _currentDimension;
        private BattleDimension _currentDimension = BattleDimension.MainWorld;

        /// <summary>维度切换事件（BattleHud 消费：瞄准取消/技能键自动回主世界等交互联动）</summary>
        public event System.Action<BattleDimension> OnDimensionChanged;

        private Canvas _leyLineFilterCanvas;      // 阴间滤镜层（懒建；sorting 37——盖场景/立牌、让位头顶条 38/伤害数字 39/HUD 40）
        private CanvasGroup _leyLineFilterGroup;  // 滤镜整体透明度（切换过渡驱动）
        private readonly List<RectTransform> _leyLineWisps = new List<RectTransform>();
        private readonly List<Vector2> _leyLineWispBases = new List<Vector2>();
        private readonly List<float> _leyLineWispPhases = new List<float>();
        private Color _origCameraBg;              // 切地脉前相机底色（切回还原）
        private bool _cameraBgSaved;
        private Coroutine _filterFadeCo;

        /// <summary>切换维度视角（HUD 维度钮驱动）：主世界↔地脉循环（未来霜月等新维度=追加
        /// 枚举值后循环自然扩展）。切地脉=滤镜淡入+主世界单位渲染件隐藏+灵魂现身+雾面让位
        /// （灵界透视）+相机底色换阴间深蓝；切回反向。交互联动（瞄准取消）由 HUD 侧在调用前处理</summary>
        public void ToggleDimensionView()
        {
            SetDimensionView(_currentDimension == BattleDimension.MainWorld
                ? BattleDimension.LeyLine : BattleDimension.MainWorld);
        }

        /// <summary>维度滤镜档案（2026-10-26 fix-batch2 ②a）：每维度一套滤镜/相机表现——主世界=无滤镜
        /// （档案槽占位不消费）；加新维度=枚举值+档案表加一行</summary>
        [Serializable]
        public class DimensionViewProfile
        {
            [Tooltip("滤镜顶色（该维度全屏渐变上端）")]
            public Color 滤镜顶色 = new Color(0.05f, 0.10f, 0.20f, 0.28f);
            [Tooltip("滤镜底色（全屏渐变下端——下坠感更浓）")]
            public Color 滤镜底色 = new Color(0.02f, 0.14f, 0.22f, 0.50f);
            [Tooltip("滤镜暗角强度（屏幕四缘压暗）")]
            [Range(0f, 1f)] public float 滤镜暗角强度 = 0.55f;
            [Tooltip("切入该维度的滤镜过渡时长（秒）")]
            [Range(0.05f, 1f)] public float 过渡秒 = 0.28f;
            [Tooltip("该维度相机底色（背景换色；切回主世界自动还原）")]
            public Color 相机底色 = new Color(0.015f, 0.03f, 0.06f);
            [Tooltip("灵雾漂片数量（0=无灵雾；prefab 现烘 3 节点——扩量需同步 prefab 加节点）")]
            [Min(0)] public int 灵雾数量 = 3;
        }

        /// <summary>维度档案查询（越界/空槽=null=无滤镜；主世界恒 null——语义由枚举位决定，
        /// 序列化空槽 round-trip 变默认实例也不影响消费）</summary>
        private DimensionViewProfile ProfileOf(BattleDimension dimension)
        {
            if (dimension == BattleDimension.MainWorld) return null;
            int i = (int)dimension;
            if (i >= 0 && i < 维度滤镜档案.Count && 维度滤镜档案[i] != null) return 维度滤镜档案[i];
            return null;
        }

        public void SetDimensionView(BattleDimension dimension)
        {
            if (dimension == _currentDimension) return;
            var previous = _currentDimension;
            _currentDimension = dimension;
            // 单位可见性=维度匹配（②a 泛化：DimensionView 替代 IsSoulView 二元判）
            foreach (var kv in _views)
            {
                var view = kv.Value;
                if (view == null) continue;
                view.SetDimensionVisible(view.DimensionView == dimension);
            }
            _board?.SetFogOverlayVisible(dimension == BattleDimension.MainWorld); // 灵界透视：非主世界视角雾面让位
            _board?.SetDimensionTerrain(dimension); // 地脉地形=开局快照（2026-10-25 拍板：不同步主世界演化）
            EnsureOverheadBars().SetDimensionVisible(dimension); // 头顶条=Overlay 画布不吃相机 culling——逐条目门控
            ApplyDimensionFilter(dimension, previous);
            GICLog.Info($"[BattlePlayer] 维度视角切换 → {dimension}");
            OnDimensionChanged?.Invoke(dimension);
        }

        /// <summary>滤镜+相机落地（②a/②b 泛化：档案驱动+相机 mask 查表——切入某维度=**从当前 mask
        /// 幂等重算**：剥掉全部维度层再点亮当前维度层；2026-10-26 返修：原「一次性捕获基值」在相机跨
        /// 对局存活时把上一场遗留的脏 mask 当原始值锁死（bit 永久丢失=「切回主世界只剩血条」根因），
        /// 重算式天然自愈历史脏值；fade 时长=离开维度的过渡秒，回主世界回落 0.28 默认）</summary>
        private void ApplyDimensionFilter(BattleDimension dimension, BattleDimension previous)
        {
            var profile = ProfileOf(dimension);
            bool filterOn = profile != null;
            if (filterOn) EnsureDimensionFilter(profile);
            var cam = ViewCamera;
            // 维度渲染层隔离（2026-10-25 泄漏修复拍板 A）：箭矢/瞄准贴片等世界特效由工厂统一挂
            // BattleMain 层、灵魂树挂 BattleLeyLine——此处单开关管全部 3D 渲染件（根治「切地脉见
            // 主世界箭矢」）；Overlay 画布（头顶条/伤害数字）不经相机——由 SetDimensionVisible/
            // 弹出点门控分别管。加新维度=枚举+层注册即自动纳入循环（②b 查表）
            if (cam != null)
            {
                // 幂等重算（2026-10-26 返修）：从**当前** mask 剥掉全部维度层再点亮当前维度层——无捕获
                // 基值、无还原依赖，相机携带任何历史脏值（跨对局未还原/异地切换残留）都一并自愈
                int mask = cam.cullingMask;
                foreach (BattleDimension dim in Enum.GetValues(typeof(BattleDimension)))
                {
                    int layer = LayerMask.NameToLayer(BattleViewFactory.DimensionLayerName(dim));
                    if (layer >= 0) mask &= ~(1 << layer);
                }
                int curLayer = LayerMask.NameToLayer(BattleViewFactory.DimensionLayerName(dimension));
                if (curLayer >= 0) mask |= 1 << curLayer;
                cam.cullingMask = mask;
            }
            if (filterOn)
            {
                if (cam != null)
                {
                    if (!_cameraBgSaved) { _origCameraBg = cam.backgroundColor; _cameraBgSaved = true; }
                    cam.backgroundColor = profile.相机底色;
                }
            }
            else if (_cameraBgSaved)
            {
                if (cam != null) cam.backgroundColor = _origCameraBg;
                _cameraBgSaved = false;
            }
            if (_leyLineFilterGroup == null) return;
            if (_filterFadeCo != null) StopCoroutine(_filterFadeCo);
            var fadeDuration = ProfileOf(previous)?.过渡秒 ?? 0.28f;
            _filterFadeCo = StartCoroutine(FadeLeyLineFilterCoroutine(filterOn ? 1f : 0f, fadeDuration));
        }

        private IEnumerator FadeLeyLineFilterCoroutine(float targetAlpha, float duration)
        {
            float from = _leyLineFilterGroup.alpha;
            if (Mathf.Approximately(from, targetAlpha)) { _leyLineFilterGroup.alpha = targetAlpha; yield break; }
            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / Mathf.Max(0.01f, duration);
                _leyLineFilterGroup.alpha = Mathf.Lerp(from, targetAlpha, t);
                yield return null;
            }
            _leyLineFilterGroup.alpha = targetAlpha;
        }

        /// <summary>维度滤镜层懒建（首次切入带档案的维度）：全屏双色渐变+暗角+灵雾漂片。
        /// 纯视觉层——无 GraphicRaycaster（勿补，BattleOverheadBars 同纪律）、blocksRaycasts 恒关；
        /// 挂 BattlePlayer 子级随场同亡。结构真源=LeyLineFilterCanvas.prefab（①b）；
        /// 色/暗角/灵雾数按维度档案绑定（②a）</summary>
        private void EnsureDimensionFilter(DimensionViewProfile profile)
        {
            if (_leyLineFilterCanvas != null) return;
            var prefab = Resources.Load<GameObject>("Prefabs/Battle/LeyLineFilterCanvas");
            if (prefab == null)
            {
                GICLog.Warn("[BattlePlayer] LeyLineFilterCanvas.prefab 缺失（Assets/Resources/Prefabs/Battle/）——维度滤镜不可用");
                return;
            }
            var go = Instantiate(prefab, transform, false); // 随 BattlePlayer 同亡（Overlay 画布渲染与层级无关）
            _leyLineFilterCanvas = go.GetComponent<Canvas>();
            _leyLineFilterCanvas.sortingOrder = BattleMetrics.DamageNumbersCanvasOrder - 2; // 37：盖场景、让位头顶条/数字/HUD（Metrics 单源覆写防 prefab 漂移）
            _leyLineFilterGroup = go.GetComponent<CanvasGroup>();
            var root = (RectTransform)go.transform;

            // 双色垂直渐变：同一 alpha 渐变 sprite（白 RGB、alpha 顶 0→底 1）——顶片 y 翻转承顶色（翻转已烘焙 prefab）
            var gradient = BattleViewFactory.LeyLineGradientSprite();
            BindFilterImage(root, "TintTop", gradient, profile.滤镜顶色);
            BindFilterImage(root, "TintBottom", gradient, profile.滤镜底色);
            // 暗角（径向：中心透明→四缘实）——深蓝黑压暗四缘
            BindFilterImage(root, "Vignette", BattleViewFactory.LeyLineVignetteSprite(),
                new Color(0f, 0.02f, 0.05f, profile.滤镜暗角强度));
            // 灵雾漂片（软圆 sprite=灵魂光晕同源纹理；慢漂+呼吸由 AnimateLeyLineWisps 驱动；
            // 尺寸/基准位随屏幕分辨率——运行时绑定；数量按档案〔prefab 现烘 3 节点，扩量同步 prefab〕）
            int wispCount = Mathf.Max(0, profile.灵雾数量);
            for (int i = 0; i < wispCount; i++)
            {
                var wispRect = root.Find($"LeyLineWisp{i}") as RectTransform;
                if (wispRect == null) continue;
                float size = Screen.height * (0.20f + i * 0.06f);
                wispRect.sizeDelta = new Vector2(size, size);
                var wispColor = Palette.灵魂蓝;
                wispColor.a = 0.06f + i * 0.03f;
                var img = wispRect.GetComponent<Image>();
                img.sprite = BattleViewFactory.SoulGlowSprite();
                img.color = wispColor;
                img.raycastTarget = false;
                // 基准位散开（屏宽 1/4 间隔），初始随机相位错开
                _leyLineWisps.Add(wispRect);
                _leyLineWispBases.Add(new Vector2(
                    (i - 1) * Screen.width * 0.22f, (i - 1) * Screen.height * 0.13f));
                _leyLineWispPhases.Add(UnityEngine.Random.Range(0f, Mathf.PI * 2f));
            }
        }

        /// <summary>prefab 贴图件绑定（节点/锚点/翻转=prefab 契约；本方法只绑 sprite/色/射线——程序生成
        /// sprite 无法序列化进 prefab，运行时绑定属合法接线）</summary>
        private static void BindFilterImage(RectTransform root, string name, Sprite sprite, Color color)
        {
            var rect = root.Find(name) as RectTransform;
            if (rect == null) return;
            var image = rect.GetComponent<Image>();
            if (image == null) return;
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
        }

        /// <summary>灵雾漂移（Update 驱动，滤镜激活时）：锚点慢正弦漂 + 透明度呼吸（unscaled——
        /// 暂停时滤镜仍活，纯观感同命座满命渐变先例）</summary>
        private void AnimateLeyLineWisps()
        {
            float t = Time.unscaledTime;
            for (int i = 0; i < _leyLineWisps.Count; i++)
            {
                var wisp = _leyLineWisps[i];
                if (wisp == null) continue;
                float phase = _leyLineWispPhases[i];
                float x = Mathf.Sin(t * (0.05f + i * 0.021f) + phase) * Screen.width * 0.06f;
                float y = Mathf.Sin(t * (0.03f + i * 0.017f) + phase * 1.7f) * Screen.height * 0.05f;
                wisp.anchoredPosition = _leyLineWispBases[i] + new Vector2(x, y);
                var img = wisp.GetComponent<Image>();
                if (img != null)
                {
                    var c = img.color;
                    c.a = (0.06f + i * 0.03f) * (0.7f + 0.3f * Mathf.Sin(t * (0.11f + i * 0.05f) + phase)); // 呼吸
                    img.color = c;
                }
            }
        }

        /// <summary>灵魂衰减数字（决策九十一）：灵魂蓝 N（伤害不带负号先例）——血条同步下降的
        /// 衰减读数；仅灵魂在地脉视角可见时弹（主世界视角灵魂隐藏不弹）</summary>
        private void SpawnSoulDecayNumber(UnitView view, int delta)
        {
            if (view == null || delta >= 0) return;
            int value = Mathf.Abs(delta);
            var randomOffset = new Vector3(
                UnityEngine.Random.Range(-0.42f, 0.42f), UnityEngine.Random.Range(0.3f, 0.65f),
                UnityEngine.Random.Range(-0.15f, 0.15f));
            EnsureDamageNumbers().Spawn(view.transform.position + randomOffset, value.ToString(),
                Palette.灵魂蓝, value, _playbackSpeed);
        }

        /// <summary>采集数字（docs/47 资源点批）：资源点格上弹「+N」摩拉金飘字——锚=资源点世界位
        ///（摩拉从堆里出来，比锚在采集者身上直观）；采集者归属玩家与采集路径（站立/途经）不区分
        /// 弹字（同堆双路径弹同款）。抬升 0.55=堆顶之上（2026-10-28 返修堆叠 55° 平面化后堆顶
        /// ≈表面+0.42，旧 0.35 会扎进顶件）</summary>
        private void SpawnMoraHarvestNumber(BattleCell cell, int moraGain)
        {
            if (_board == null || moraGain <= 0) return;
            var palette = Palette != null ? Palette : BattlePalette.Instance;
            if (palette == null) return;
            var pos = _board.CellToWorld(cell) + new Vector3(0f, 0.55f, 0f);
            EnsureDamageNumbers().Spawn(pos, $"+{moraGain}", palette.采集摩拉色, moraGain, _playbackSpeed);
        }

        // ==================== 消息处理（客户端侧） ====================

        public void OnBattleStart(BattleStartMessage message)
        {
            if (_board == null)
            {
                GICLog.Error("[BattlePlayer] 未绑定 BattleBoard，无法建盘");
                return;
            }

            _board.Build(message.map);
            _board.BuildGrassDecor(_viewCamera); // 草簇装饰（2026-09-27 地形批次：立牌式草簇随机铺满草地格）
            ClearViews();

            foreach (var state in message.initialSnapshot.units)
                CreateView(state);

            RefreshAllFormations(); // 开局归位（同格重叠=预期，2026-11-07 拍板）
            LatestSnapshot = message.initialSnapshot;
            SnapshotUpdated?.Invoke(LatestSnapshot);
            _fog?.OnBattleBuilt(); // 迷雾批 2026-10-17：新战斗探索集归零+初始揭雾（模式 None 时 _fog=null 直通）
            GICLog.Info($"[BattlePlayer] 建盘完成：{_views.Count} 个单位立牌");
        }

        public void OnSnapshot(SnapshotMessage message)
        {
            LatestSnapshot = message.snapshot;

            // 即时制（docs/active/49 D1/A3）：相位恒由快照派生（Selecting=Running 常驻）——
            // TurnFlowController 已删除，不再有 host/客户端双源分支。
            DeriveClientPhase(BattlePhase.Selecting, message.snapshot.turnNumber, message.selectLimitSeconds);

            // 尸体态 + 头顶血量 + 冻结可视同步（快照为准）
            foreach (var state in message.snapshot.units)
            {
                if (_views.TryGetValue(state.unitId, out var view))
                {
                    view.SetCorpseVisual(state.isCorpse != 0);
                    view.SetFrozenVisual(state.isFrozen != 0);
                    view.SetAttachedElement((ElementType)state.dyedElement); // 附着元素（权威态）
                    view.SetHp(state.hp, state.maxHp);
                    view.SetEnergy(state.energy, state.maxEnergy); // 元能（权威态；B6a）
                    view.SetSanity(state.sanity); // 理智（权威态；2026-09-30 歌声之环批）
                    view.SetConstellation(state.constellation, ResolveIsFamiliar(state)); // 命座+眷属（2026-10-06 命座徽章批）
                    view.SetBuffs(state.buffs); // 头顶 Buff 状态（权威态；含来源——图标/描环解析数据源）
                    view.SetEquipment(state.weaponItem, state.weaponDurability, state.accessoryItems,
                        state.accessoryDurability); // 装备态（权威态；装备批 2026-10-18）
                }
            }

            // 资源点权威态自愈（docs/47）：回合头快照整表覆盖（采集时刻变化走 ResourceHarvest 命令
            // 增量；重连/漏命令场景由本口兜底对齐）
            if (_board != null)
                _board.RebuildResourceNodes(message.snapshot.resourceNodes);

            // P4-E 位置自愈（每秒快照）：非移动播放中的单位，视图位与快照连续位漂移>15cm=拉回
            //（被挡停点校正/死亡中断等残留纠偏；移动播放中让位——补间与模拟同速，回拉 1s 滞后值会抖）
            if (_board != null)
            {
                foreach (var state in message.snapshot.units)
                {
                    if (!_views.TryGetValue(state.unitId, out var healView) || healView == null) continue;
                    if (_activeMovePlayback.Contains(state.unitId)) continue;
                    var snapWorld = BoardCmToWorld(state.PosXCm(), state.PosZCm());
                    if ((healView.transform.position - snapWorld).sqrMagnitude > 0.15f * 0.15f)
                        healView.ApplyPosition(snapWorld);
                }
            }

            SnapshotUpdated?.Invoke(message.snapshot);
        }

        public void OnSegment(SegmentMessage message)
        {
            // 即时制（docs/active/49 A3/D1）：不再由片派生 Resolving 相位——秒拍段每秒到达，
            // 旧逻辑会让客户端相位在 Selecting↔Resolving 间每秒振荡并触发全场立牌重排。
            // 相位唯一派生源=快照（Selecting 常驻）+ BattleOver（Finished）。
            SegmentReceived?.Invoke(message.segment); // 到达即发（全类型——底部进度线片计数）
            StartCoroutine(PlaySegmentCoroutine(message.segment));
        }

        public void OnTurnEnd(TurnEndMessage message)
        {
            GICLog.Info($"[BattlePlayer] 回合 {message.turnNumber} 结束");
        }

        /// <summary>行动预告到达（2026-09-29 执行预览）：转发事件——订阅方（执行预览）在执行相位
        /// 侧消费建行</summary>
        public void OnTurnPlan(TurnPlanMessage message)
        {
            TurnPlanUpdated?.Invoke(message);
        }

        /// <summary>战斗结束（S10 轻量全灭软停）：一方全灭——HUD 胜负提示消费；结算画面=B8</summary>
        public void OnBattleOver(BattleOverMessage message)
        {
            GICLog.Info($"[BattlePlayer] 战斗结束：胜方队伍 {(TeamType)message.winnerTeam}");
            DeriveClientPhase(BattlePhase.Finished, _clientTurn); // 终局相位派生（唯一非 Selecting 相位）
            BattleOver?.Invoke(message);
        }

        // ==================== 片播放 ====================

        // 投射物/移动速度常量统一走 BattleMetrics（B5 连续判定：Host 判定与客户端播放同源，
        // "所见即所得"=双方按同一速度常量推进时间轴）。P3 cm 化：Host 常量=800cm/s——客户端
        // 世界单位/秒=cm÷100=8，数值与旧 8f 完全一致（1 格=1 世界单位=100cm）
        private const float ProjectileSpeed =
            BattleMetrics.ProjectileSpeedCmPerSecond / (float)CellMetrics.CellSizeCm;

        /// <summary>在途有意义行动计数（2026-09-30 拍板「跳过空等：只要这期间已经没有任何正在进行的
        /// 行动，包括箭矢飞行等，必须已经没有任何意义了才可跳过空等」——攻速片片头空等跳过
        /// WaitSliceDelaySkippingDeadAir 的探测源）：计数=行动类演出段未结算数量——投射物飞行
        /// （命中/消散）、箭雨单箭下落（含散布延迟，fire-and-forget=**唯一跨片在途源**——片 ack
        /// 不等它）、移动行走；结算反馈类（伤害数字飘字/元能跳变/闪色恢复/落箭插土滞留淡出）不计数
        /// （已结算信息属装饰尾巴）。片 ack 门控保证 gate 型演出在下一片到达前必然演完，故计数实际
        /// 只拦 fire-and-forget 段——gate 型行动段同样计数属防线冗余（防未来 fire-and-forget 化回归
        /// 静默破约）；PlaySkillCastVfxCoroutine 后续新增 fire-and-forget 行动类 cue 必须同步计数。</summary>
        private int _meaningfulActionInFlight;

        /// <summary>
        /// 直线投射物：箭矢从发射格飞至**命中点**（Host 接触判定千分定点下发，docs/22 §11——
        /// 移动中目标命中点=中途接触位置，所见即所得），到达后接伤害表现。
        /// B4 的"飞向目标当前位置"近似已废弃；无命中点数据时兜底旧行为。
        /// </summary>
        private IEnumerator PlayProjectileThenDamageCoroutine(UnitView target, BattleCommand command, float stagger)
        {
            _meaningfulActionInFlight++; // 在途有意义行动：投射物飞行（发射延迟+飞行+落地弹字）——空等跳过探测源
            try
            {
                if (stagger > 0f)
                    yield return new WaitForSeconds(stagger);

                // 时轮（B-S1）：发射时刻延迟（Host 时轮资产下发，勿推算——前摇=箭矢延迟起飞）
                if (command.launchMs > 0)
                    yield return new WaitForSeconds(LaunchDelayOf(command));

                // 箭矢全程贴立牌面飞（2026-10-05 十轮定案「箭和立牌同一平面」）：起点=施放者立牌面内
                // 弓位点（ProjectileOriginWorld——箭高=**面内语义**〔tiltGroup 局部 y〕，预览贴弓直读）；
                // 终点 z 同加立牌面前伸量——两端同面同深：东西向全程等深=屏幕水平直线，且与一切同俯仰
                // 立牌共面（目标立牌面同深≈到点贴上目标身）。历史：八轮「格心 xz+屏幕解算 y」箭在立牌面
                // 前 ~0.5 格飞=「不在一个平面上」报障根因；六报前「面内起点+格心终点」两端深度差=屏幕
                // 斜线——教训=两端必须同面同深，勿混合
                float arrowFacial = command.arrowHeightY > 0 ? command.arrowHeightY / 1000f : BattleMetrics.ArrowFlightHeight;
                Vector3 from = _board.CellToWorld(command.cell);
                float planeDepth = 0f; // 立牌面前伸量（55° 后仰：面上点随高度前移 sin55°×面内高）
                if (_views.TryGetValue(command.actorUnitId, out var casterView) && casterView != null)
                {
                    Vector3 cellCenter = from;
                    from = casterView.ProjectileOriginWorld(arrowFacial);
                    planeDepth = from.z - cellCenter.z;
                }
                Vector3 to = command.hitX != 0 || command.hitY != 0
                    ? _board.ContinuousCellToWorld(command.hitX / 1000f, command.hitY / 1000f)
                    : target != null ? target.transform.position : _board.CellToWorld(command.cell); // 兜底：无定点数据时飞向目标；目标在飞行窗口内被拆件回落命中格（守卫族）
                to.y = from.y; // 与发射点同高=水平直线（飞行段世界系正常视差）
                to.z += planeDepth; // 全程保持发射面深度=贴面飞行（东西向与一切立牌共面）

                // 元素色动态染色（2026-09-28 拍板方案 A；2026-10-04 起走箭矢色板单源）：命中箭取 Damage 命令自带伤害元素（metadata）——与结算同源；
                // 施法者配了专属弹射物时 CreateProjectileVisual 内改走专属素材+白染（2026-10-05 per-unit 定制）
                var arrowGo = CreateProjectileVisual(from, to, ResolveArrowTint(command.metadata), command.actorUnitId);

                float distance = Vector3.Distance(from, to);
                float duration = distance > 0f ? distance / (ProjectileSpeed * _playbackSpeed) : 0f;
                yield return BattleViewTween.Over(duration, t => arrowGo.transform.position = Vector3.Lerp(from, to, t));

                ReleaseProjectileVisual(arrowGo);
                yield return PlayDamageCoroutine(target, -command.value, 0f, false, command.reactionKind,
                    command.metadata, isCrit: command.crit == 1, actorUnitId: command.actorUnitId,
                    blocked: command.blocked == 1);
            }
            finally
            {
                _meaningfulActionInFlight--;
            }
        }

        /// <summary>
        /// 投射物消散（Effect 命令·EffectKindProjectileVanish）：无接触命中（虚空截断或 24 格上限，
        /// docs/05 §5.3）——从发射格飞至消散点（千分定点）后消失；无定点时按方向飞 value 格兜底
        /// </summary>
        private IEnumerator PlayProjectileVanishCoroutine(BattleCommand command, float stagger)
        {
            _meaningfulActionInFlight++; // 在途有意义行动：投射物飞行（发射延迟+飞行至消散）——空等跳过探测源
            try
            {
                if (stagger > 0f)
                    yield return new WaitForSeconds(stagger);

                // 时轮（B-S1）：发射时刻延迟（与命中侧投射物同源对齐）
                if (command.launchMs > 0)
                    yield return new WaitForSeconds(LaunchDelayOf(command));

                // 箭矢视觉高度（与命中侧同源）：命令千分携带（**面内语义**=tiltGroup 局部 y）；
                // 起点=施放者立牌面内弓位点+全程保持面深（十轮定案「箭和立牌同一平面」，详见命中侧注释）
                float vanishArrowFacial = command.arrowHeightY > 0 ? command.arrowHeightY / 1000f : BattleMetrics.ArrowFlightHeight;
                Vector3 from = _board.CellToWorld(command.cell);
                float vanishPlaneDepth = 0f;
                if (_views.TryGetValue(command.actorUnitId, out var vanishView))
                {
                    Vector3 vanishCellCenter = from;
                    from = vanishView.ProjectileOriginWorld(vanishArrowFacial);
                    vanishPlaneDepth = from.z - vanishCellCenter.z;
                }
                Vector3 to;
                if (command.hitX != 0 || command.hitY != 0)
                {
                    to = _board.ContinuousCellToWorld(command.hitX / 1000f, command.hitY / 1000f);
                }
                else
                {
                    // P3 cm 兼容：value=厘米（2400）——÷100 回格；正常路径恒有 hitX/hitY 定点，本兜底不可达
                    var delta = SkillHitResolver.DirectionToDelta((Direction2D)command.direction);
                    int valueCells = command.value / CellMetrics.CellSizeCm;
                    to = _board.CellToWorld(new BattleCell(command.cell.x + delta.x * valueCells,
                        command.cell.y + delta.y * valueCells));
                }
                to.y = from.y; // 与发射点同高=水平直线
                to.z += vanishPlaneDepth; // 消散端保持发射面深度（无目标立牌可贴，东西向全程同面）

                // 元素色=消散命令随带投射物元素（reactionKind，Host 与命中 Damage.metadata 同口径回填——
                // 丘丘人借霜袭时消散箭同为冰色，非施法者物理灰；2026-10-04 起走箭矢色板单源；
                // 施法者配了专属弹射物时 CreateProjectileVisual 内改走专属素材+白染）
                var arrowGo = CreateProjectileVisual(from, to, ResolveArrowTint(command.reactionKind), command.actorUnitId);

                float distance = Vector3.Distance(from, to);
                float duration = distance > 0f ? distance / (ProjectileSpeed * _playbackSpeed) : 0f;
                yield return BattleViewTween.Over(duration, t => arrowGo.transform.position = Vector3.Lerp(from, to, t));

                // 落地表现（决策七十六「箭矢落在水面上应当表现为沉入水下，落在其它表面上插入地面
                // 尾部弹性摇晃」）：消散箭不再凭空消失——fire-and-forget（落地=已结算装饰尾巴，
                // 不阻下一片空等跳过，雨箭同口径）
                StartCoroutine(PlayVanishArrowLandingCoroutine(arrowGo, command));
            }
            finally
            {
                _meaningfulActionInFlight--;
            }
        }

        /// <summary>消散箭落地表现（决策七十六）：落点格水面/沼泽=保持水平姿态坠至水面→沉入水下
        /// （下潜+淡出）；实面（地面/薄冰）=尖自空中落至表面+绕尖摆到插地角+沿轴压入+尾部弹性摇晃
        /// +滞留淡出（参数与箭雨落地同族）；虚空边缘无表面=无落地直接消失（旧行为）。
        /// 落点格=千分定点向下取整（连续格心系 c+0.5）；无定点方向兜底路径按 cell+方向×value 算终点格</summary>
        private IEnumerator PlayVanishArrowLandingCoroutine(GameObject arrowGo, BattleCommand command)
        {
            if (arrowGo == null) yield break;
            BattleCell cell;
            if (command.hitX != 0 || command.hitY != 0)
                cell = new BattleCell(Mathf.FloorToInt(command.hitX / 1000f), Mathf.FloorToInt(command.hitY / 1000f));
            else
            {
                // P3 cm 兼容：value=厘米 ÷100 回格（同 PlayProjectileVanishCoroutine 兜底口径）
                var delta = SkillHitResolver.DirectionToDelta((Direction2D)command.direction);
                int valueCells = command.value / CellMetrics.CellSizeCm;
                cell = new BattleCell(command.cell.x + delta.x * valueCells,
                    command.cell.y + delta.y * valueCells);
            }
            if (_board == null || _board.Map == null || !_board.Map.HasTile(cell.x, cell.y))
            {
                ReleaseProjectileVisual(arrowGo); // 虚空边缘（无表面）：无落地——回池（现状消失同观感）
                yield break;
            }

            var tile = _board.Map.GetTile(cell.x, cell.y);
            var surfaceY = _board.GetVisualSurfaceHeight(cell);
            var renderer = arrowGo.GetComponent<SpriteRenderer>();
            float halfLen = renderer != null ? renderer.bounds.size.x * 0.5f : 0f;
            var baseRot = arrowGo.transform.rotation;

            if (tile == TileType.Water || tile == TileType.Swamp)
            {
                // 落水：坠至水面（保持水平姿态）→沉入水下+淡出（「沉入水下」，无插土无摇晃）
                var dropFrom = arrowGo.transform.position;
                var dropTo = new Vector3(dropFrom.x, surfaceY, dropFrom.z);
                yield return BattleViewTween.Over(箭落坠地时长 / _playbackSpeed, t =>
                {
                    if (arrowGo != null) arrowGo.transform.position = Vector3.Lerp(dropFrom, dropTo, t);
                });
                var sinkColor = renderer != null ? renderer.color : Color.white;
                var sinkFrom = dropTo;
                yield return BattleViewTween.Over(箭落沉水时长 / _playbackSpeed, t =>
                {
                    if (arrowGo != null)
                        arrowGo.transform.position = sinkFrom + new Vector3(0f, -箭落沉水深度 * t, 0f);
                    if (renderer != null) { var c = sinkColor; c.a = sinkColor.a * (1f - t); renderer.color = c; }
                });
                if (arrowGo != null) ReleaseProjectileVisual(arrowGo);
                yield break;
            }

            // 实面（地面/薄冰）：尖自空中落至表面+绕尖摆到插地角（绕移动尖旋转=坠地+摆正一段完成）
            var tipArrival = arrowGo.transform.position + arrowGo.transform.right * halfLen;
            var tipGround = new Vector3(tipArrival.x, surfaceY + 0.02f, tipArrival.z);
            var plantRot = baseRot * Quaternion.Euler(0f, 0f, 箭落插地角);
            yield return BattleViewTween.Over(箭落坠地时长 / _playbackSpeed, t =>
            {
                if (arrowGo == null) return;
                arrowGo.transform.rotation = Quaternion.Slerp(baseRot, plantRot, t);
                var tip = Vector3.Lerp(tipArrival, tipGround, t);
                arrowGo.transform.position = tip - arrowGo.transform.right * halfLen; // 绕尖旋转的位姿补偿
            });
            if (arrowGo == null) yield break;
            var plantPos = arrowGo.transform.position;
            var plantRotFinal = arrowGo.transform.rotation;

            // 沿轴压入（箭雨插土同语言：尖入表面、尾翘起）
            if (箭雨入土深度 > 0f)
            {
                var stuckTo = plantPos + plantRotFinal * Vector3.right * 箭雨入土深度;
                var stuckFrom = plantPos;
                yield return BattleViewTween.Over(箭雨入土时长 / _playbackSpeed, t =>
                {
                    if (arrowGo != null) arrowGo.transform.position = Vector3.Lerp(stuckFrom, stuckTo, t);
                });
            }

            // 尾部弹性摇晃（绕埋入尖屏幕面内正弦衰减）
            yield return PlayArrowTailWobbleCoroutine(arrowGo, plantRotFinal * Vector3.forward);

            // 滞留+淡出（箭雨落地参数同族：拍板「插地箭摇晃衰减后滞留 3 秒淡出」）
            if (箭雨落地滞留 > 0f)
                yield return new WaitForSeconds(箭雨落地滞留 / _playbackSpeed);
            if (renderer != null && 箭雨落地淡出 > 0f)
            {
                var color = renderer.color;
                yield return BattleViewTween.Over(箭雨落地淡出 / _playbackSpeed, t =>
                {
                    if (renderer != null) { var c = color; c.a = color.a * (1f - t); renderer.color = c; }
                });
            }
            if (arrowGo != null) ReleaseProjectileVisual(arrowGo);
        }

        /// <summary>插地箭尾部弹性摇晃（决策七十六「插入地面时，尾部有弹性摇晃」）：绕埋入尖
        /// （position+right×半长）绕布告板法线正弦衰减摆动，两三个来回即收敛；纯代码动画零素材。
        /// axis=摆动轴（布告板法线=baseRot*forward，屏幕面内摆动与箭矢朝向构建同族）</summary>
        private IEnumerator PlayArrowTailWobbleCoroutine(GameObject arrowGo, Vector3 axis)
        {
            if (arrowGo == null || 箭落摇晃时长 <= 0f || 箭落摇晃幅度 <= 0f) yield break;
            var renderer = arrowGo.GetComponent<SpriteRenderer>();
            float halfLen = renderer != null ? renderer.bounds.size.x * 0.5f : 0f;
            var pivot = arrowGo.transform.position + arrowGo.transform.right * halfLen; // 埋入尖=摆动枢轴
            var baseRot = arrowGo.transform.rotation;
            var centerOffset = arrowGo.transform.position - pivot;
            float elapsed = 0f;
            while (elapsed < 箭落摇晃时长 && arrowGo != null)
            {
                elapsed += Time.deltaTime / _playbackSpeed;
                float theta = 箭落摇晃幅度 * Mathf.Sin(9f * elapsed) * Mathf.Exp(-5.5f * elapsed);
                arrowGo.transform.rotation = baseRot * Quaternion.Euler(0f, 0f, theta);
                arrowGo.transform.position = pivot + Quaternion.AngleAxis(theta, axis) * centerOffset;
                yield return null;
            }
            if (arrowGo == null) yield break;
            arrowGo.transform.rotation = baseRot; // 摇晃收尾：回正姿态
            arrowGo.transform.position = pivot + centerOffset;
        }

        /// <summary>正式箭矢 sprite（Resources/UI/Battle/ArrowBolt——AI 生成白色箭矢，运行时元素色染色；
        /// B-S3 箭矢正式素材 2026-09-28 拍板落地。加载失败回退程序化白色光条占位）</summary>
        private static Sprite _arrowSprite;
        private static bool _arrowSpriteLoaded;
        private static Sprite ArrowSprite
        {
            get
            {
                if (!_arrowSpriteLoaded)
                {
                    _arrowSpriteLoaded = true;
                    _arrowSprite = Resources.Load<Sprite>("UI/Battle/ArrowBolt");
                    if (_arrowSprite == null)
                        GICLog.Warn("[BattlePlayer] 箭矢素材 UI/Battle/ArrowBolt 加载失败，回退白色光条占位");
                }
                return _arrowSprite;
            }
        }

        /// <summary>投射物光条 sprite（程序化 1×1 白图缓存；箭矢素材缺失时的占位兜底）</summary>
        private static Sprite _projectileSprite;
        private static Sprite ProjectileSprite
        {
            get
            {
                if (_projectileSprite != null) return _projectileSprite;
                var tex = new Texture2D(1, 1);
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();
                _projectileSprite = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
                return _projectileSprite;
            }
        }

        /// <summary>箭矢视觉（2026-09-28 拍板方案 A：屏幕平行布告板 + 屏幕平面内旋转对齐飞行方向——
        /// 固定视角十字四向只有 0/90/180/270 四角，上下射=屏幕竖直箭、四向全长；元素色动态染色。
        /// 创建即就位，飞行由调用方 tween；占位兜底配色=BattlePalette「箭矢占位色」活色勿写字面量。
        /// per-unit 专属弹射物（2026-10-05 拍板）：actorUnitId 已登记=专属素材+白染（美术即最终色，
        /// 缩放乘「箭矢长度」归一化）；未登记=通用 ArrowBolt+传入 tint 元素染色</summary>
        /// <summary>#15 审查批箭矢池：命中箭/消散箭/箭雨落箭共用（箭雨 8 格×4 波×2 箭=单次施放
        /// 64 个 GO+渲染器的实例化尖峰——池化后复用）。失活池件挂 _viewRoot 下随场同亡；
        /// Pop 出已销毁引用（场景重载后 Unity fake-null）自动回落新建</summary>
        private readonly Stack<GameObject> _arrowPool = new Stack<GameObject>();

        private GameObject CreateProjectileVisual(Vector3 from, Vector3 to, Color tint, string actorUnitId = null)
        {
            var arrowGo = _arrowPool.Count > 0 ? _arrowPool.Pop() : null;
            if (arrowGo == null) arrowGo = new GameObject("Projectile");
            arrowGo.SetActive(true);
            BattleViewFactory.SetMainWorldLayer(arrowGo); // 维度渲染层（2026-10-25 泄漏修复：箭矢随主世界层=相机 culling 单开关隐藏）
            arrowGo.transform.SetParent(_viewRoot, false);
            arrowGo.transform.position = from;

            // 朝向：先取屏幕平行布告板（面正对视线，与立牌后仰同族），再按飞行方向的屏幕投影
            // 绕视线轴旋转到四向其一（投影走 WorldToScreenPoint，不假设画布/世界轴向映射）
            var cam = _viewCamera != null ? _viewCamera : CameraContext.Resolve();
            if (cam != null && (to - from).sqrMagnitude > 1e-8f)
            {
                var screenFrom = cam.WorldToScreenPoint(from);
                var screenTo = cam.WorldToScreenPoint(to);
                var angle = Mathf.Atan2(screenTo.y - screenFrom.y, screenTo.x - screenFrom.x) * Mathf.Rad2Deg;
                arrowGo.transform.rotation = cam.transform.rotation * Quaternion.Euler(0f, 0f, angle);
            }
            else
            {
                arrowGo.transform.rotation = _billboardRotation; // 无相机/零程兜底：竖立布告板
            }

            var renderer = arrowGo.GetComponent<SpriteRenderer>();
            if (renderer == null) renderer = arrowGo.AddComponent<SpriteRenderer>();
            renderer.enabled = true; // 池件复位：迷雾门控可能以 disabled 归还
            renderer.sortingOrder = BattleMetrics.ProjectileSortingOrder;
            var sprite = ArrowSprite;
            float customScale = 1f;
            var color = tint;
            if (actorUnitId != null && _customProjectiles.TryGetValue(actorUnitId, out var custom))
            {
                sprite = custom.sprite;   // 专属弹射物：美术即最终色
                customScale = custom.scale;
                color = Color.white;     // 白染=不再元素染色（专属素材自带角色元素配色）
            }
            if (sprite != null)
            {
                var scale = 箭矢长度 / sprite.bounds.size.x * customScale; // 箭矢长度=屏幕长轴全长，高度随素材纵横比
                arrowGo.transform.localScale = new Vector3(scale, scale, 1f);
                renderer.sprite = sprite;
                renderer.color = color;
            }
            else
            {
                arrowGo.transform.localScale = new Vector3(0.05f, 0.30f, 1f); // 占位兜底：白色光条
                renderer.sprite = ProjectileSprite;
                renderer.color = Palette.箭矢占位色;
            }
            // 迷雾批追拍二（2026-10-17 用户报「敌方安柏射出的箭矢我能直接看到」）：全箭矢工厂单点注册
            // 迷雾门控——敌方施放的箭矢（飞行/消散/天降/落地滞留）逐帧按当前位置所在格探索态显隐，
            // 飞到已探索格才显示；我方特效不注册恒显（施法者视图查无=按我方处理防误吞）
            if (_fog != null && _fog.Active && actorUnitId != null
                && _views.TryGetValue(actorUnitId, out var actorView) && actorView != null)
                _fog.RegisterProjectileVisual(arrowGo, actorView.OwnerTeam);
            return arrowGo;
        }

        /// <summary>箭矢回池（#15 审查批：命中/消散/箭雨三路箭协程的 Destroy 统一改此口——
        /// 注销迷雾门控+失活回池待复用；协程被腰斩（清场/退局）时不经此口，池件随场销毁无泄漏）</summary>
        private void ReleaseProjectileVisual(GameObject arrowGo)
        {
            if (arrowGo == null) return;
            _fog?.UnregisterProjectileVisual(arrowGo);
            arrowGo.SetActive(false);
            _arrowPool.Push(arrowGo);
        }

        // ==================== 时轮表现轨（SkillCast 消费，B-S3 ②首个接线=2026-09-28 箭雨） ====================

        private static readonly Dictionary<int, SkillConfig> _skillConfigCache = new Dictionary<int, SkillConfig>();

        /// <summary>按 skillID 约定路径加载技能配置（Resources/Configs/Skills/{SkillName}——技能独立化约定；
        /// 缺失 Warn 一次；缓存防逐段重复加载。**public 单源**（2026-10-06 头顶 Buff 图标批）：
        /// BattleOverheadBars 图标解析同链消费——来源技能 id → SkillConfig.icon</summary>
        public static SkillConfig.SkillData LoadSkillData(int skillId)
        {
            if (_skillConfigCache.TryGetValue(skillId, out var config)) return config?.data;
            config = Resources.Load<SkillConfig>($"Configs/Skills/{(SkillName)skillId}");
            if (config == null)
                GICLog.Warn($"[BattlePlayer] 技能配置缺失：Configs/Skills/{(SkillName)skillId}（表现轨无源可播）");
            _skillConfigCache[skillId] = config;
            return config?.data;
        }

        /// <summary>时轮特效轨播放入口：SkillCast 命令→技能配置→时轮资产，cueName→视觉开关台
        ///（首个 cue=arrow_rain 箭雨天降；动作/音效轨素材落地后同入口扩展）。
        /// fire-and-forget 不 gate 片 ack（片节拍由 Damage 数字 launchMs 承载，同 S5 装饰尾巴口径）</summary>
        private IEnumerator PlaySkillCastVfxCoroutine(BattleCommand command)
        {
            var timeline = LoadSkillData(command.value)?.timeline;
            if (timeline == null) yield break; // 无时轮技能（移动等）——无表现轨可播，静默
            foreach (var clip in SkillTimelineQuery.ClipsOf(timeline, SkillTrackType.Vfx))
            {
                switch (clip.cueName)
                {
                    case "arrow_rain":
                        yield return PlayArrowRainCoroutine(timeline, command);
                        break;
                    // 后续特效 cue 随 B-S3 素材落地扩（音效轨 cueName=arrow_rain_release 同期接播放）
                }
            }
        }

        /// <summary>箭雨天降（arrow_rain）：判定轨 LineBurst 为节拍/射程单源——段时刻=startTime+hitInterval×段
        ///（与 CompileLineBurstSegment 同源）、整线格集=step≥1 虚空截断同口径、段数=参数表 DamageCount
        ///（数值归参数表铁律）；每段每格 N 支技能元素色箭自高处**斜落**（复用箭矢素材，起点沿安柏射向
        /// 反方向侧移=落向随安柏射向「箭雨落下倾斜角」——2026-09-28 拍板「斜着落下」+追拍板「方向根据
        /// 安柏的位置」）。
        /// 落地时刻=段时刻（与 Damage 数字同拍）——起飞提前一个坠落时长</summary>
        private IEnumerator PlayArrowRainCoroutine(SkillTimelineAsset timeline, BattleCommand command)
        {
            var burstClips = SkillTimelineQuery.JudgmentClips(timeline, SkillJudgmentKind.LineBurst);
            if (burstClips.Count == 0) yield break;
            var clip = burstClips[0];
            var skillData = LoadSkillData(command.value);
            int waves = skillData != null ? skillData.GetInt(SkillParamKey.DamageCount, 1) : 1;
            int maxRange = clip.maxRange > 0 ? clip.maxRange : ProjectileRule.MaxRange;

            var cells = new List<BattleCell>(); // 整线格集（Host CompileLineBurstSegment 同口径）
            var delta = SkillHitResolver.DirectionToDelta((Direction2D)command.direction);
            var shotDir = new Vector3(delta.x, 0f, delta.y); // 射向世界向量（落箭斜落沿它、起点向安柏一侧回撤）
            for (int step = 1; step <= maxRange; step++)
            {
                var cell = new BattleCell(command.cell.x + delta.x * step, command.cell.y + delta.y * step);
                if (_board == null || _board.Map == null || !_board.Map.HasTile(cell.x, cell.y)) break;
                cells.Add(cell);
            }
            if (cells.Count == 0) yield break;

            // 安柏 2命「爆发破雾」客户端表现（docs/active/44 §9-②，与 Host CompileBurstFogBreak 同口径
            // 线域+同首段时刻）：我方/队友施放且快照命座≥2 → 首段命中时刻到点揭雾（WYSIWYG 箭到雾开；
            // fire-and-forget 不 gate 片 ack——与单箭同口径）
            if (_fog != null && _fog.ShouldRevealBurstFog(command.actorUnitId))
                StartCoroutine(RevealFogDelayedCoroutine(cells, Mathf.Max(0f, clip.startTime) / _playbackSpeed));

            var tint = ResolveArrowTint(command.reactionKind);
            float prevSpawn = 0f;
            for (int wave = 0; wave < waves; wave++)
            {
                float spawnAt = Mathf.Max(0f, clip.startTime + clip.hitInterval * wave - 箭雨坠落时长);
                if (spawnAt > prevSpawn)
                {
                    yield return new WaitForSeconds((spawnAt - prevSpawn) / _playbackSpeed);
                    prevSpawn = spawnAt;
                }
                foreach (var cell in cells)
                {
                    for (int i = 0; i < 箭雨每格每段箭数; i++)
                        StartCoroutine(PlayRainArrowCoroutine(cell, shotDir, tint, command.actorUnitId,
                            UnityEngine.Random.Range(0f, 箭雨逐箭散布延迟))); // 单箭 fire-and-forget（不 gate ack）
                }
            }
        }

        /// <summary>延迟揭雾（安柏 2命破雾表现）：等首段命中时刻到点揭——「箭矢还未飞到薄冰就融」
        /// 同款 WYSIWYG 纪律（雾随箭落地揭开，非施放即揭）</summary>
        private IEnumerator RevealFogDelayedCoroutine(List<BattleCell> cells, float delaySeconds)
        {
            if (delaySeconds > 0f) yield return new WaitForSeconds(delaySeconds);
            _fog?.RevealCells(cells);
        }

        /// <summary>单支落箭：延迟起飞→斜落（落向沿安柏射向「箭雨落下倾斜角」）→落地沿箭轴压入插土
        ///（入土段被地形深度裁掉=尖插表面、尾翘起；水面格落点=波浪表面之上）→原地滞留（2026-09-28
        /// 拍板「插在表面 3 秒」）→淡出销毁</summary>
        private IEnumerator PlayRainArrowCoroutine(BattleCell cell, Vector3 shotDir, Color tint, string actorUnitId, float delaySeconds)
        {
            // 在途有意义行动计数（fire-and-forget 单箭=唯一跨片在途源——片 ack 不等它，下一片片头
            // 空等跳过据此探测，2026-09-30 拍板）：散布延迟+斜落段=箭矢飞行；落地即结算，
            // 插土/滞留/淡出=已结算装饰尾巴不计数
            _meaningfulActionInFlight++;
            GameObject arrowGo = null;
            Vector3 to = Vector3.zero;
            try
            {
                if (delaySeconds > 0f)
                    yield return new WaitForSeconds(delaySeconds / _playbackSpeed);

                var jitter = new Vector3(UnityEngine.Random.Range(-箭雨格内散布, 箭雨格内散布), 0f,
                    UnityEngine.Random.Range(-箭雨格内散布, 箭雨格内散布));
                var basePos = _board.CellToWorld(cell) + jitter;
                var from = basePos + new Vector3(0f, 箭雨起始高度, 0f);
                to = new Vector3(basePos.x, _board.GetVisualSurfaceHeight(cell) + 0.02f, basePos.z);
                // 斜落方向=沿安柏射向（2026-09-28 追拍板「斜落的方向应当根据安柏的位置来」）：起点沿射向
                // 反方向侧移——落箭读作安柏射出的箭越过格心继续飞行坠入该格（东射=自西侧高处向东落、
                // 北射=自南侧高处向北落，起点天然在安柏一侧）；侧移量=tan(倾斜角)×起始高度（世界倾角）；
                // 落点/落地时刻/节拍全不变（纯视觉，判定无涉）
                if (shotDir.sqrMagnitude > 1e-6f && 箭雨落下倾斜角 != 0f)
                {
                    from -= shotDir * (Mathf.Tan(箭雨落下倾斜角 * Mathf.Deg2Rad) * 箭雨起始高度);
                }
                arrowGo = CreateProjectileVisual(from, to, tint, actorUnitId);
                arrowGo.transform.localScale *= 箭雨箭矢缩放;

                yield return BattleViewTween.Over(箭雨坠落时长 / _playbackSpeed,
                    t => { if (arrowGo != null) arrowGo.transform.position = Vector3.Lerp(from, to, t); });
            }
            finally
            {
                _meaningfulActionInFlight--; // 落地/中断都结算——插土滞留淡出段不阻下一片空等跳过
            }

            // 落地分支（决策七十六「箭矢落在水面上应当表现为沉入水下」）：水面/沼泽=沉入水下
            //（下潜+淡出，无插土无滞留）；实面（地面/薄冰）=现行插土+新增尾部弹性摇晃
            var landTile = _board.Map != null && _board.Map.HasTile(cell.x, cell.y)
                ? _board.Map.GetTile(cell.x, cell.y) : TileType.None;
            if (landTile == TileType.Water || landTile == TileType.Swamp)
            {
                var sinkRenderer = arrowGo != null ? arrowGo.GetComponent<SpriteRenderer>() : null;
                var sinkFrom = arrowGo != null ? arrowGo.transform.position : to;
                var sinkColor = sinkRenderer != null ? sinkRenderer.color : Color.white;
                yield return BattleViewTween.Over(箭落沉水时长 / _playbackSpeed, t =>
                {
                    if (arrowGo != null)
                        arrowGo.transform.position = sinkFrom + new Vector3(0f, -箭落沉水深度 * t, 0f);
                    if (sinkRenderer != null)
                    {
                        var c = sinkColor;
                        c.a = sinkColor.a * (1f - t);
                        sinkRenderer.color = c;
                    }
                });
                if (arrowGo != null) ReleaseProjectileVisual(arrowGo);
                yield break;
            }

            // 落地插土：沿箭轴向落点内压入「箭雨入土深度」——屏幕平面布告板，入土段被地形深度裁掉
            //（尖插表面、尾翘起；薄冰格=实面同地面插土不沉，决策七十六）
            if (arrowGo != null && 箭雨入土深度 > 0f)
            {
                var stuckTo = to + arrowGo.transform.right * 箭雨入土深度; // transform.right=屏面内飞行方向（尖朝前）
                yield return BattleViewTween.Over(箭雨入土时长 / _playbackSpeed,
                    t => { if (arrowGo != null) arrowGo.transform.position = Vector3.Lerp(to, stuckTo, t); });
            }

            // 尾部弹性摇晃（决策七十六「插入地面时，尾部有弹性摇晃」）：绕埋入尖屏幕面内正弦衰减
            if (arrowGo != null)
                yield return PlayArrowTailWobbleCoroutine(arrowGo, arrowGo.transform.forward);

            if (箭雨落地滞留 > 0f)
                yield return new WaitForSeconds(箭雨落地滞留 / _playbackSpeed);

            var renderer = arrowGo != null ? arrowGo.GetComponent<SpriteRenderer>() : null;
            if (renderer != null && 箭雨落地淡出 > 0f)
            {
                var color = renderer.color;
                yield return BattleViewTween.Over(箭雨落地淡出 / _playbackSpeed, t =>
                {
                    if (renderer != null)
                    {
                        var c = color;
                        c.a = color.a * (1f - t);
                        renderer.color = c;
                    }
                });
            }
            if (arrowGo != null) ReleaseProjectileVisual(arrowGo);
        }

        private IEnumerator PlaySegmentCoroutine(Segment segment)
        {
            // 片开始时刻 =（全场最高攻速 − 自身攻速）÷ 10 秒（连续换算）；
            // 即时行动块/部署段 = 短节拍；回合结束段 = 固定节拍（不按攻速排程）
            float delay;
            if (segment.turnEnd != 0)
                delay = TurnEndDelaySeconds;
            else if (segment.insertedInstantAction != 0 || segment.deploy != 0)
                delay = BattleMetrics.DeploySegmentBeatSeconds;
            else
                delay = Mathf.Max(0f, (segment.turnMaxAttackSpeed - segment.sliceAttackSpeed)
                    / BattleMetrics.AttackSpeedSliceLeadInDivisor);
            if (delay > 0f)
            {
                // 空等跳过（2026-09-30 拍板「凯亚行动完后芭芭拉空等 3 秒，应当跳过空等时间——只要
                // 这期间已经没有任何正在进行的行动（含箭矢飞行等）才可跳过」——docs/18 L44 既有
                ///「衍生事件提早完成即跳过剩余等待」语义的落地）：攻速片片头等待逐帧探测在途有意义
                // 行动，无在途即提前进片；部署/回合结束/即时块固定短拍不参与跳过
                if (segment.turnEnd == 0 && segment.deploy == 0 && segment.insertedInstantAction == 0)
                    yield return WaitSliceDelaySkippingDeadAir(delay / _playbackSpeed);
                else
                    yield return new WaitForSeconds(delay / _playbackSpeed);
            }

            // 只对攻速行动片发（2026-09-29 语义收紧：部署/回合结束/即时行动块不按攻速排程——
            // 执行预览的行进推进只属攻速片；旧消费方攻速队列已随队列退役）
            if (segment.turnEnd == 0 && segment.deploy == 0 && segment.insertedInstantAction == 0)
                OnSegmentPlaying?.Invoke(segment.sliceAttackSpeed);

            var playbacks = new List<Coroutine>();
            float stagger = 0f;
            _segmentDirectDamage.Clear(); // 段首清空（防跨段污染——吸血自疗只绑本段节拍）
            foreach (var command in segment.commands)
            {
                int playbacksBefore = playbacks.Count;
                switch (command.type)
                {
                    case BattleCommandType.Move:
                        if (_views.TryGetValue(command.actorUnitId, out var mover))
                        {
                            if (command.path.Count == 0)
                            {
                                // P4-E 位置校正命令（被挡收尾）：空 path+hitX/hitY=停点 cm——停活动补间
                                //（世代递增=旧补间自废）+落位+撞墙弹回；被挡=真停（移动循环动画收）
                                _moveGeneration[command.actorUnitId] =
                                    (_moveGeneration.TryGetValue(command.actorUnitId, out var prevGen) ? prevGen : 0) + 1;
                                _activeMovePlayback.Remove(command.actorUnitId);
                                var stopWorld = BoardCmToWorld(command.hitX, command.hitY);
                                mover.ApplyPosition(stopWorld);
                                mover.Cell = BattleCell.FromCm(command.hitX, command.hitY); // 停点格（勿留旧的路径终点格：
                                // 迷雾/命中判定等按 Cell 消费）
                                mover.SetMoveAnimation(false);
                                // 被挡=直接停在停点（2026-11-07 拍板「不需要弹回，就和大多数游戏一样」）：
                                // 原「探出→弹回」表现已整体移除——连续空间下 AI 每 0.5s 重决策会对同一点反复
                                // 下发校正命令，任何顶墙反馈都会退化成原地高频抖动。
                            }
                            else
                            {
                                mover.Cell = command.path[command.path.Count - 1];
                                // P4-E 移动世代（同单位新移动=替换旧补间防双写 jitter——玩家连点重定
                                // 目标/AI 0.5s 重决策两条路都高频换目标）
                                int gen = (_moveGeneration.TryGetValue(command.actorUnitId, out var g) ? g : 0) + 1;
                                _moveGeneration[command.actorUnitId] = gen;
                                _activeMovePlayback.Add(command.actorUnitId);
                                playbacks.Add(StartCoroutine(PlayMoveCoroutine(mover, command, gen)));
                            }
                        }
                        break;

                    case BattleCommandType.Damage:
                        if (_views.TryGetValue(command.targetUnitId, out var target))
                        {
                            if (command.direction == 1)
                                // 直线投射物（B5）：与移动同 t=0 起跑（时间轴对齐——命中时刻=接触时刻），
                                // 不吃命令 stagger；命中点由命令定点下发
                                playbacks.Add(StartCoroutine(PlayProjectileThenDamageCoroutine(target, command, 0f)));
                            else
                            {
                                // 直击数字节拍（2026-09-28 对齐 Heal 分支口径）：launchMs>0=时轮段时刻到点再弹
                                //（箭雨 4 段 0.15s 间隔与落箭同拍连续弹）；0=立即维持命令 stagger 旧节拍
                                //（霜袭等 startTime=0 技能与无时轮兜底路径不变）
                                float dmgDelay = command.launchMs > 0 ? LaunchDelayOf(command) : stagger;
                                if (command.launchMs == 0)
                                {
                                    // 吸血自疗同拍绑定源（§107 追记）+ 立即响应配对队列（拍板 B 勘正）：
                                    // 逐次记录本方各次直击节拍，第 n 笔自疗随第 n 次直击同帧弹
                                    if (!_segmentDirectDamage.TryGetValue(command.actorUnitId, out var beats))
                                    {
                                        beats = new DirectDamageBeats();
                                        _segmentDirectDamage[command.actorUnitId] = beats;
                                    }
                                    beats.beats.Add(dmgDelay);
                                }
                                playbacks.Add(StartCoroutine(PlayDamageCoroutine(target, -command.value, dmgDelay, false,
                                    command.reactionKind, command.metadata, isCrit: command.crit == 1,
                                    actorUnitId: command.actorUnitId, blocked: command.blocked == 1)));
                            }
                        }
                        break;

                    case BattleCommandType.Heal:
                        if (_views.TryGetValue(command.targetUnitId, out var healed))
                        {
                            // 命中时刻（2026-09-25「命中时才给」）：OnHit 治疗（水之浅唱）到命中毫秒再弹 +N
                            //（与投射物落地同时刻）；0=立即（OnCast 治疗/回合结束段，保持命令 stagger 节拍）。
                            // 吸血自疗同拍（2026-10-01 实战报障「伤害 40 先出、+30 慢一拍才出」）：
                            // source==target 且本段该攻击者有 launchMs=0 直击（stagger 旧节拍、Host 无从
                            // 携带该时刻）——绑定其直击节拍同帧弹，勿再吃治疗命令自己的 stagger 槽。
                            // 立即响应配对（拍板 B 勘正「不要错峰」）：第 n 笔自疗随第 n 次直击节拍；
                            // 游标耗尽回落末次节拍=同帧各弹（不造视觉时差）
                            float healDelay;
                            if (command.launchMs > 0)
                                healDelay = LaunchDelayOf(command);
                            else if (command.actorUnitId == command.targetUnitId
                                     && _segmentDirectDamage.TryGetValue(command.actorUnitId, out var beats))
                                healDelay = beats.NextOrLast(stagger);
                            else
                                healDelay = stagger;
                            playbacks.Add(StartCoroutine(PlayDamageCoroutine(healed, command.value, healDelay, true,
                                lifestealPrefix: command.metadata == BattleCommand.HealKindLifesteal,
                                actorUnitId: command.actorUnitId)));
                        }
                        break;

                    case BattleCommandType.Revive:
                        // 复苏（B-3 ②，芭芭拉闪耀奇迹——docs/05 §5.4「血量永远0不复苏」唯一例外）：
                        // 解灰+底座还原+视频恢复（SetCorpseVisual 原路反转）+ 复活血量与 +N 治疗数字
                        // 复用 Heal 链（PlayDamageCoroutine isHeal——含 ApplyHpDelta 即时反馈）
                        if (_views.TryGetValue(command.targetUnitId, out var revived))
                        {
                            revived.SetCorpseVisual(false);
                            playbacks.Add(StartCoroutine(PlayDamageCoroutine(revived, command.value, stagger, true,
                                actorUnitId: command.actorUnitId)));
                        }
                        break;

                    case BattleCommandType.UpgradeConstellation:
                        // 命座提升（B8 批，docs/09）：3★+ 同名重复出战升命。被动属性/环参数由 Host 结算、
                        // 快照权威自愈；徽章数字随命令即时更新（value=新等级——2026-10-08 报障返修：原只弹
                        // toast、数字等下回合选择阶段快照才跳变，与部署段最先结算矛盾）
                        GICLog.Info($"[BattlePlayer] 命座提升：{command.targetUnitId} → C{command.value}");
                        if (_views.TryGetValue(command.targetUnitId, out var constellationUpgraded))
                            constellationUpgraded.SetConstellation(command.value, constellationUpgraded.IsFamiliar);
                        if (PopupManager.Instance != null)
                            PopupManager.Instance.ShowToast(
                                new LocalizedString(TableName.PopupText.ToString(), "Battle_ConstellationUp"));
                        break;

                    case BattleCommandType.Death:
                        if (_views.TryGetValue(command.actorUnitId, out var dying))
                        {
                            // 消散（决策八十六批B，docs/45 §4 拍板 #5）：建筑+眷属死亡不留尸体
                            // ——立牌淡出+视图清理；伙伴/魔神尸体路径维持既有 SetCorpseVisual
                            if (command.metadata == 1)
                                playbacks.Add(StartCoroutine(PlayDissolveCoroutine(dying, stagger)));
                            else
                                playbacks.Add(StartCoroutine(PlayDeathCoroutine(dying, stagger)));
                        }
                        break;

                    case BattleCommandType.ApplyBuff:
                        // 来源三件随命令透传（2026-10-06 头顶 Buff 图标批）：层数=buffLevel（Host 合并后
                        // 回填值）、来源单位=actorUnitId、来源技能=metadata 复用字段——客户端图标行即时刷新。
                        // buffKey=具名身份（决策五十七——同族异名共存各一枚徽章；旧回放 key 空按 type 匹配）
                        // **命中时刻到点应用（决策七十六复测返修 WYSIWYG）**：箭在飞时目标不提前结冰/
                        // 挂减防徽章——launchMs>0 延迟到点（合并取首条时刻=徽章首现拍），0=立即
                        if (_views.TryGetValue(command.targetUnitId, out var buffed))
                        {
                            var buffedView = buffed;
                            var buffType = command.buffType;
                            var buffTurns = command.buffTurns;
                            var buffLevel = command.buffLevel;
                            var buffActor = command.actorUnitId;
                            var buffSourceSkill = command.metadata;
                            var buffKey = command.buffKey;
                            if (command.launchMs > 0)
                                playbacks.Add(StartCoroutine(PlayDelayedStateApply(LaunchDelayOf(command),
                                    () =>
                                    {
                                        if (buffedView != null)
                                            buffedView.ApplyBuffBadge(buffType, buffTurns, buffLevel,
                                                buffActor, buffSourceSkill, buffKey);
                                    })));
                            else
                                buffedView.ApplyBuffBadge(buffType, buffTurns, buffLevel,
                                    buffActor, buffSourceSkill, buffKey);
                        }
                        break;

                    case BattleCommandType.RemoveBuff:
                        if (_views.TryGetValue(command.targetUnitId, out var unbuffed))
                        {
                            unbuffed.RemoveBuffBadge(command.buffType, command.buffKey);
                            // 冻结到期即时退冰色（与 Reaction(Freeze) 即时上色对称；其它视觉仍随快照）
                            if (command.buffType == (int)BuffType.Freeze)
                                unbuffed.SetFrozenVisual(false);
                        }
                        break;

                    case BattleCommandType.ElementAttach:
                        // 附着即时刷新（2026-09-22 接线：此前靠下回合快照自愈，反应当片不可见）；
                        // **命中时刻到点应用（决策七十六复测返修 WYSIWYG）**：箭在飞时目标不提前挂
                        // 附着图标——launchMs>0 延迟到点（投射物/整线段时刻），0=立即（OnCast/回合末）
                        if (_views.TryGetValue(command.targetUnitId, out var attached))
                        {
                            var attachedView = attached;
                            var attachElement = (ElementType)command.metadata;
                            if (command.launchMs > 0)
                                playbacks.Add(StartCoroutine(PlayDelayedStateApply(LaunchDelayOf(command),
                                    () => { if (attachedView != null) attachedView.SetAttachedElement(attachElement); })));
                            else
                                attachedView.SetAttachedElement(attachElement);
                        }
                        break;

                    case BattleCommandType.TerrainChange:
                        // 元素×地形（决策七十六）：本地 map 真值+薄冰面板刷新——后续瞄准预览/箭落
                        // 判地形即时正确（移动 BFS 客户端侧不跑、只 HUD 预览读图）；
                        // **命中时刻到点应用（决策七十六复测返修 WYSIWYG「箭矢还未飞到薄冰就融化」）**：
                        // launchMs>0 延迟到点（箭雨多波按波融化），0=立即（冰棱回合末冻结/泡湿类）
                        if (_board != null)
                        {
                            var terrainCell = command.cell;
                            var terrainTile = (TileType)command.value;
                            if (command.launchMs > 0)
                                playbacks.Add(StartCoroutine(PlayDelayedStateApply(LaunchDelayOf(command),
                                    () => { if (_board != null) _board.ApplyTerrainChange(terrainCell, terrainTile); })));
                            else
                                _board.ApplyTerrainChange(terrainCell, terrainTile);
                        }
                        break;

                    case BattleCommandType.ResourceHarvest:
                        // 资源点采集（docs/47，2026-10-27）：采集事实镜像——BattleBoard 表现件余量/耗尽
                        // 维护+资源点位置弹「+N」摩拉金飘字（采集是玩家可见的主动交互，默认弹——
                        // 与「元能获取弹数字」默认关不同：采集场景少且是资源点核心反馈）。
                        // 玩家摩拉入账由 StatChange(StatKindMora) 既有链承载（本命令零池写）。
                        // **命中时刻到点应用（2026-10-28 返修「表现对齐动画」）**：途经采集
                        // launchMs=行走到达堆格时刻（Host 按 i×MoveStepSeconds+k×CommandStaggerSeconds
                        // 同源计算）——到点再减件/弹字（段首即减=安柏还没走到堆顶件就没了）；
                        // 0=立即（回合末站立采集）。
                        // 迷雾口径（2026-10-28 返修「摩拉堆在迷雾中应当隐藏」）：堆格对本端未探索=
                        // 零飘字（堆件已隐藏，凭空浮字会泄露位置；我方采集者途经格必然已揭雾，
                        // 本门实际只拦敌方迷雾内采集）
                        if (_board != null)
                        {
                            var harvestCell = command.cell;
                            var harvestRemaining = command.metadata;
                            var harvestGain = command.value;
                            if (command.launchMs > 0)
                                playbacks.Add(StartCoroutine(PlayDelayedStateApply(LaunchDelayOf(command), () =>
                                {
                                    if (_board == null) return;
                                    _board.ApplyResourceHarvest(harvestCell, harvestRemaining);
                                    if (_fog == null || _fog.IsExplored(harvestCell))
                                        SpawnMoraHarvestNumber(harvestCell, harvestGain);
                                })));
                            else
                            {
                                _board.ApplyResourceHarvest(harvestCell, harvestRemaining);
                                if (_fog == null || _fog.IsExplored(harvestCell))
                                    SpawnMoraHarvestNumber(harvestCell, harvestGain);
                            }
                        }
                        break;

                    case BattleCommandType.Reaction:
                        // 反应发生事件：冻结=立牌冰色即时同步（此前冰色只随快照来，反应当回合不显）；
                        // 融化=反应爆发特效属 B5/B6 画面批次，此处仅留挂点。
                        // 反应消耗（决策二十五：反应 1:1 双方全消耗、等层零残留）——到点清附着图标
                        // （命中时刻 launchMs>0 延迟到点；0=立即；Physical=图标隐藏）
                        if (_views.TryGetValue(command.targetUnitId, out var reacted))
                        {
                            if (command.metadata == BattleCommand.ReactionKindFreeze)
                                reacted.SetFrozenVisual(true);
                            float consumeDelay = command.launchMs > 0 ? LaunchDelayOf(command) : 0f;
                            playbacks.Add(StartCoroutine(PlayAttachConsumeCoroutine(reacted, consumeDelay)));
                        }
                        break;

                    case BattleCommandType.StatChange:
                        // 属性变化增量：元能（B6a）→单位缓存；摩拉/体力（B6d）→玩家资源事件——
                        // 玩家资源命令的 targetUnitId=玩家 ID 不在 _views，先分流否则被静默丢弃。
                        // 摩拉掠夺（B-3）/途经采集体力（2026-10-28 返修）带命中时刻：launchMs>0
                        // 到点再应用（行走到达堆格再跳 chips，与堆减件/飘字同拍）
                        if (command.metadata == BattleCommand.StatKindMora
                            || command.metadata == BattleCommand.StatKindStamina)
                        {
                            if (command.launchMs > 0)
                                playbacks.Add(StartCoroutine(PlayResourceDeltaCoroutine(command)));
                            else
                                OnResourceDelta?.Invoke(command.targetUnitId, command.metadata, command.value);
                            break;
                        }
                        if (_views.TryGetValue(command.targetUnitId, out var statChanged))
                        {
                            if (command.metadata == BattleCommand.StatKindEnergy)
                            {
                                // 命中时刻（2026-09-25 拍板「命中时才给」）：战技获能命令带命中毫秒——
                                // 到点再跳元能（与投射物命中表现同时刻）；0=立即（移动获能/协奏/消耗/回合发放）。
                                // 获取类随跳弹「+N」元能数字（2026-10-01 拍板④「目前少了元能数字弹出」
                                // ——白=元能条色单源，消耗不弹）
                                if (command.launchMs > 0)
                                    playbacks.Add(StartCoroutine(PlayEnergyDeltaCoroutine(statChanged, command)));
                                else
                                {
                                    statChanged.ApplyEnergyDelta(command.value);
                                    SpawnEnergyNumber(statChanged, command.value);
                                }
                            }
                            else if (command.metadata == BattleCommand.StatKindSanity)
                            {
                                // 理智恢复（2026-09-30 歌声之环批）：环 tick 立即应用数据缓存——
                                // 视觉消费方随未来理智机制批；下个快照自然校正
                                statChanged.ApplySanityDelta(command.value);
                            }
                            else if (command.metadata == BattleCommand.StatKindHp)
                            {
                                // 地脉灵魂衰减（决策九十一）：血条即时下降+灵魂蓝数字（非伤害路径——
                                // 无受击反馈/闪红/格挡）；灵魂在主世界视角隐藏时不弹（数字锚=灵魂位置）
                                statChanged.ApplyHpDelta(command.value);
                                if (command.value < 0 && !statChanged.IsDimensionHidden)
                                    SpawnSoulDecayNumber(statChanged, command.value);
                            }
                        }
                        break;

                    case BattleCommandType.ItemConsume:
                        // 物品消耗（统一消耗模型 C-1）：先扣本地 handCards 镜像（按 (物品,实例耐久) 条目
                        // 匹配——决策八十一；减尽移除条目），再广播 HUD 刷新角标；下回合快照权威兜底
                        ApplyItemConsumeToLocalHand(command.targetUnitId, (ItemName)command.metadata,
                            command.value, command.buffLevel);
                        OnItemConsumed?.Invoke(command.targetUnitId, command.metadata, command.value, command.buffLevel);
                        break;

                    case BattleCommandType.EquipItem:
                        // 装备/耐久刷新（装备批 2026-10-18）：血条右侧图标行增改——value=物品名、
                        // metadata=槽位种类、direction=槽位序号、buffLevel=剩余耐久；下回合快照权威自愈
                        if (_views.TryGetValue(command.targetUnitId, out var equippedView))
                            equippedView.ApplyEquipBadge(command.value, command.metadata, command.direction,
                                command.buffLevel);
                        break;

                    case BattleCommandType.UnequipItem:
                        // 卸下（决策八十一·死亡掠夺清槽——阵亡单位装备被击杀方掠走）：图标行移除该槽位
                        if (_views.TryGetValue(command.targetUnitId, out var unequippedView))
                            unequippedView.RemoveEquipBadge(command.metadata, command.direction);
                        break;

                    case BattleCommandType.ItemGain:
                        // 获得物品卡（命座授予/死亡掠夺）：本地手牌镜像新增（GainCard 对偶——按 (物品,
                        // 实例耐久) 条目合并/新建，掠夺物耐久保留）；HUD 重建手牌（新实例=新槽位）；
                        // 下回合快照权威自愈
                        ApplyItemGainToLocalHand(command.targetUnitId, (ItemName)command.metadata,
                            command.value, command.buffLevel);
                        OnItemGained?.Invoke(command.targetUnitId, command.metadata, command.value, command.buffLevel);
                        break;

                    case BattleCommandType.CardGain:
                        // 获得单位卡（购买批 2026-10-22，决策八十六：购买角色/建筑卡入手牌）：
                        // 本地手牌镜像新增（count 合并/新建）+HUD 重建手牌；下回合快照权威自愈
                        ApplyCardGainToLocalHand(command.targetUnitId, command.metadata, command.value);
                        OnUnitCardGained?.Invoke(command.targetUnitId, command.metadata, command.value);
                        break;

                    case BattleCommandType.Summon:
                        // 部署登场（B6c）：按命令携带的全量状态即时建 view（快照权威自愈兜底）
                        if (command.summonUnit != null && !_views.ContainsKey(command.summonUnit.unitId))
                        {
                            CreateView(command.summonUnit);
                            // 新登场立即按当前布局态入位（两态模型：执行阶段=收拢重叠格心）
                            if (_views.TryGetValue(command.summonUnit.unitId, out var summoned))
                                summoned.ApplyPosition(_board.CellToWorld(command.summonUnit.position));
                            RefreshAllFormations();
                            // 迷雾追拍七：视野缓存预热（召唤单位不在当前快照——防部署先揭回落半径）
                            // +敌方立即隐藏（不等下一帧评估——「闪一下才隐藏」报障根因）
                            _fog?.OnUnitSummoned(command.summonUnit);
                        }
                        break;

                    case BattleCommandType.SkillCast:
                        // 时轮演出起点事件（B-S1）：按 skillID 加载时轮资产播动作/音效/特效轨
                        // ——特效轨首个消费方=arrow_rain 箭雨天降（2026-09-28）、动作轨=技能资产 动作视频
                        // （B-S4c 战技/爆发立牌动作，2026-10-04），音效轨待素材同入口扩展；
                        // fire-and-forget 不进 playbacks（不 gate 片 ack，节拍由 Damage launchMs 承载）；
                        // 前摇期投射物视觉由投射物命令的 launchMs 延迟起飞承载
                        GICLog.Info($"[BattlePlayer] 技能施放：{command.actorUnitId} → {(SkillName)command.value}" +
                                    $" 方向 {(Direction2D)command.direction}");
                        if (_views.TryGetValue(command.actorUnitId, out var caster))
                        {
                            // P3 自由角朝向：aimX/aimZ 千分向量优先（x 符号定左右——任意角）、
                            // 0=旧 direction 十字口径（P4 HUD 接线前 AI/HUD 上交恒十字=行为不变）
                            caster.SetFacing(command.aimX != 0 ? command.aimX < 0 : IsLeftFacing(command.direction));
                            // 动作轨（B-S4c）：技能资产 动作视频≠null → 立牌从头播一次性动作片（拉弓-释放-收势，
                            // 时长≈时轮 totalTime）、播完自动回待机循环（UnitView.PlayActionVideo 双 RT+缩放补偿
                            // ——宽幅 16:9 动作片原生尺寸独立 RT 渲染+主体视觉大小恒等补偿）；null=待机照播
                            var skillData = LoadSkillData(command.value);
                            // P4-D CD 转圈即时起点（决策九十五）：cdTicks>0=新施放——HUD 记就绪时刻
                            //（战技/爆发两槽）；移动施放 cdTicks=0 与技能缺载不发
                            if (skillData != null && command.cdTicks > 0)
                                SkillCasted?.Invoke(command.actorUnitId, skillData.skillType, command.cdTicks);
                            var actionClip = skillData?.当前动作片;
                            if (actionClip != null)
                            {
                                // B-S4c 校准批次：播放速度=战斗回放速度×技能校准倍率（0/负=按 1，非法回落原速）
                                var 校准速度 = skillData.动作片播放速度 > 0f ? skillData.动作片播放速度 : 1f;
                                // P3 释放速度（蓝图 §4.3）：动作片播放速率同乘 castRate 千分
                                //（Host 编译=时轮时刻÷率——攻速快动画同倍加快；攻速 0 恒 1000=原速）
                                float releaseRate = command.castRate > 0 ? command.castRate / 1000f : 1f;
                                if (skillData.skillType == SkillType.Move)
                                {
                                    // 移动技能动作片=循环态移动片（2026-10-05 决策四十四「尽可能统一」）：
                                    // SkillCast 片头登记（每段移动行动同产 SkillCast——AddMoveCast，登记恒新鲜）、
                                    // Move 命令片起止 SetMoveAnimation 消费；速度同一次性动作片口径乘回放速度
                                    //（行走 tween 同按回放速度缩放，快进不脚滑）
                                    caster.SetMoveVideo(actionClip, _playbackSpeed * 校准速度 * releaseRate,
                                        skillData.动作片缩放补偿, skillData.动作片位置偏移,
                                        skillData.当前动作片Alpha打包); // 打包标志随片（2026-10-16 离线抠绿批）
                                }
                                else
                                {
                                    caster.PlayActionVideo(actionClip, _playbackSpeed * 校准速度 * releaseRate,
                                        skillData.动作片缩放补偿, skillData.动作片位置偏移,
                                        packedAlpha: skillData.当前动作片Alpha打包); // 打包片直读 alpha 区跳过运行时抠色
                                }
                            }
                        }
                        StartCoroutine(PlaySkillCastVfxCoroutine(command));
                        break;

                    case BattleCommandType.Effect:
                        // 特效命令（B5 首个接线=投射物消散，与投射物/移动同 t=0 起跑）
                        if (command.metadata == BattleCommand.EffectKindProjectileVanish)
                            playbacks.Add(StartCoroutine(PlayProjectileVanishCoroutine(command, 0f)));
                        break;

                    default:
                        // B4+ 接线：ElementAttach/Reaction/StatChange/Summon 等
                        break;
                }
                // 非播放命令不吃错峰槽（2026-10-23 拍板 B1「回合末级联膨胀」病灶）：元能/理智/
                // 附着/RemoveBuff/发放等即时应用类命令不推进 stagger——此前 121 条命令的回合末段里
                // 大量隐形命令把末段治疗/伤害弹字的起跳槽位推到 ~10 秒深处；只有真正产生演出
                // playback 的命令才占槽，弹字间 0.12s 节奏与逐层各弹语义均不变
                if (playbacks.Count > playbacksBefore)
                    stagger += CommandStaggerSeconds / _playbackSpeed;
            }

            foreach (var playback in playbacks)
                yield return playback;

            // 按当前布局态收拢/散开重排（执行阶段=收拢重叠格心；阶段切换事件负责切态）
            RefreshAllFormations();

            // P6 退役件清理：片播放完成确认（SegmentAck）随 ack 门控退役——Host 已无监听
            //（旧「等全体 ack 才结算下一片」语义随回合机退役；死发送=每段刷
            // 「BattleMessageRouter 未注册的消息类型」Warning，2026-11-05 观察局实录实锤）
        }

        /// <summary>攻速片片头等待——空等跳过（2026-09-30 拍板，docs/18「回合流程重排+攻速延迟执行模型」
        /// 既有「行动及其衍生事件提早完成时，后续行动跳过剩余等待立即执行」语义的落地）：期间逐帧探测
        /// 在途有意义行动计数（_meaningfulActionInFlight——箭矢飞行/箭雨下落/移动行走未结算）——
        /// &gt;0 则继续等（等到其在途结算即止，**剩余空等不再补满**——空等只承担攻速节奏提示）；
        /// ==0 立即进片。片 ack 门控保证 gate 型演出在下一片到达前演完，实际拦的只有 fire-and-forget
        /// 行动段（箭雨单箭）；已结算装饰尾巴（飘字/插箭滞留淡出/闪色恢复）不计数不阻跳过。</summary>
        private IEnumerator WaitSliceDelaySkippingDeadAir(float scaledDelay)
        {
            float elapsed = 0f;
            while (elapsed < scaledDelay && _meaningfulActionInFlight > 0)
            {
                yield return null;
                elapsed += Time.deltaTime;
            }
        }

        /// <summary>行动方向→立牌朝向（2026-09-29 拍板③：方向 ∈ {上,左上,左,左下} 朝左水平镜像，其余朝右——
        /// 素材统一朝右单份复用；朝向行动后保持、待机延续；后续背后刺杀类技能可读 UnitView.FaceLeft）</summary>
        private static bool IsLeftFacing(int direction)
        {
            return direction == (int)Direction2D.Up || direction == (int)Direction2D.UpLeft
                || direction == (int)Direction2D.Left || direction == (int)Direction2D.DownLeft;
        }

        private IEnumerator PlayMoveCoroutine(UnitView view, BattleCommand command, int generation)
        {
            // 移动态动画（B-S4a；2026-10-05 决策四十四 per-skill 统一）：移动片内切登记的移动循环片
            // （SkillCast 片头经 SetMoveVideo 登记）、片末回 待机（含被挡弹回段——弹回也是移动表现）；
            // try/finally 保异常不滞留移动态
            view.SetFacing(IsLeftFacing(command.direction)); // 立牌朝向随移动方向（拍板③）；行动后保持=待机延续
            view.SetMoveAnimation(true);
            _meaningfulActionInFlight++; // 在途有意义行动：移动行走（含被挡弹回段）——空等跳过探测源
            try
            {
                var path = command.path;
                // P4-E 逐段时长=段距离/实际移速（斜向 1.41 格段不再恒 1s——与模拟同速推进；
                // speedCm=施放时刻实际移速含 Buff，0=回落默认；_playbackSpeed 回放倍率沿用）
                float speedWorldPerSec = (command.speedCm > 0 ? command.speedCm : BattleMetrics.DefaultMoveSpeedCm)
                    / (float)CellMetrics.CellSizeCm;
                // 连续移动（2026-11-05「角色闪烁/瞬移」修复）：起点=立牌当前视觉位，并跳过已经走过的
                // 路径点。AI 每 0.5 秒重决策、摇杆变向都会换发 Move 命令；旧实现恒从 path[0] 格心起步，
                // 已推进的立牌被拉回格心（≤半格回退），与片尾队形重排叠加即每帧在起点/终点间瞬移。
                if (path.Count == 1 && view != null && (command.hitX != 0 || command.hitY != 0))
                {
                    // 同格移动（起点格=终点格，命令只带 1 个格心）：旧实现无补间→客户端不动、Host 在格内
                    // 走到精确落点，随后每秒快照自愈再拉一次=格内瞬移。按精确落点补间即可（≤1 格行程）。
                    Vector3 sameCellFrom = view.transform.position;
                    Vector3 sameCellTo = BoardCmToWorld(command.hitX, command.hitY);
                    float sameCellSec = Mathf.Max(0.05f,
                        Vector3.Distance(sameCellFrom, sameCellTo) / (speedWorldPerSec * _playbackSpeed));
                    if ((sameCellTo - sameCellFrom).sqrMagnitude > 0.0001f)
                    {
                        var segFromSame = sameCellFrom;
                        yield return BattleViewTween.Over(sameCellSec, t =>
                        {
                            if (view != null
                                && (!_moveGeneration.TryGetValue(command.actorUnitId, out var c0) || c0 == generation))
                                view.ApplyPosition(Vector3.Lerp(segFromSame, sameCellTo, t));
                        });
                    }
                }
                else if (path.Count > 1)
                {
                    // 补间点列（2026-11-05）：末点=命令携带的精确落点 cm（连续空间 Host 真实停点），
                    // 其余=格心——旧实现收尾于格心，静止后与 Host 差最多半格，下一秒快照自愈再拉回=瞬移
                    var points = new List<Vector3>(path.Count);
                    for (int i = 0; i < path.Count; i++) points.Add(_board.CellToWorld(path[i]));
                    if (command.hitX != 0 || command.hitY != 0)
                        points[points.Count - 1] = BoardCmToWorld(command.hitX, command.hitY);
                    Vector3 startPos = view != null ? view.transform.position : points[0];
                    int firstTarget = 1;
                    float bestSqr = float.MaxValue;
                    for (int i = 1; i < points.Count; i++)
                    {
                        float distToWp = (points[i] - startPos).sqrMagnitude;
                        if (distToWp < bestSqr) { bestSqr = distToWp; firstTarget = i; }
                    }
                    for (int i = firstTarget; i < points.Count; i++)
                    {
                        if (view == null) break; // 中途死亡/消散拆件：停止补间（守卫族同 PlayDamageCoroutine）
                        if (_moveGeneration.TryGetValue(command.actorUnitId, out var cur) && cur != generation)
                            yield break; // P4-E 世代替换：新移动命令已接管（移动态/活动表由新世代 finally 管理）
                        Vector3 from = startPos;
                        Vector3 to = points[i];
                        float stepSeconds = Mathf.Max(0.05f,
                            Vector3.Distance(from, to) / (speedWorldPerSec * _playbackSpeed));
                        var segFrom = from; // 逐段闭包快照（startPos 每段推进，lambda 不得读到新值）
                        yield return BattleViewTween.Over(stepSeconds, t =>
                        {
                            if (view != null
                                && (!_moveGeneration.TryGetValue(command.actorUnitId, out var c) || c == generation))
                                view.ApplyPosition(Vector3.Lerp(segFrom, to, t));
                        });
                        startPos = to;
                    }
                }
                if (path.Count > 1 && view != null
                    && (!_moveGeneration.TryGetValue(command.actorUnitId, out var c3) || c3 == generation))
                    view.ApplyPosition(command.hitX != 0 || command.hitY != 0
                        ? BoardCmToWorld(command.hitX, command.hitY)
                        : _board.CellToWorld(path[path.Count - 1]));
            }
            finally
            {
                // P4-E 世代守卫：仅当世代仍归本补间（未被替换/校正）才收移动态与活动表——
                // 被替换时移动态由新世代管理（防旧 finally 关掉新补间刚开的动画）
                if (!_moveGeneration.TryGetValue(command.actorUnitId, out var curF) || curF == generation)
                {
                    if (view != null) view.SetMoveAnimation(false); // 拆件后勿触（守卫族）
                    _activeMovePlayback.Remove(command.actorUnitId);
                }
                _meaningfulActionInFlight--; // 移动结算（正常/中断同收）
            }
        }

        /// <summary>反应子类型 → 本地化反应名（短命数字对象直接求值当前语言，不挂 TextCombiner）。
        /// 三反应全带名（2026-10-01 三次拍板「反应名都应该加上」——冻结不再例外；本地化键 Battle_ReactionFreeze=12035）；
        /// 超导带名（决策七十四实装——Battle_ReactionSuperconduct）</summary>
        private static string ReactionNameOf(int reactionKind)
        {
            string key = null;
            if (reactionKind == BattleCommand.ReactionKindMelt) key = "Battle_ReactionMelt";
            else if (reactionKind == BattleCommand.ReactionKindVaporize) key = "Battle_ReactionVaporize";
            else if (reactionKind == BattleCommand.ReactionKindFreeze) key = "Battle_ReactionFreeze";
            else if (reactionKind == BattleCommand.ReactionKindSuperconduct) key = "Battle_ReactionSuperconduct";
            if (key == null) return null;
            return new LocalizedString("UIText", key).GetLocalizedString();
        }

        /// <summary>吸血名前缀（2026-10-01 拍板）：吸血自疗数字「吸血 +N」——与反应名前缀同源取词</summary>
        private static string LifeStealName()
            => new LocalizedString("UIText", "Battle_LifeSteal").GetLocalizedString();

        /// <summary>装备格挡名（装备批 2026-10-18·兔兔伯爵）：伤害被完全抵挡时替代伤害数字的文案
        ///（短命数字对象直接求值当前语言，同 ReactionNameOf 模式）</summary>
        private static string BlockName()
            => new LocalizedString("UIText", "Battle_Block").GetLocalizedString();

        /// <summary>反应消耗附着图标（决策二十五：反应 1:1 双方全消耗、等层零残留——到点清；
        /// Physical=图标隐藏；与伤害数字/清附着同一时刻=反应消耗所见即所得）</summary>
        private IEnumerator PlayAttachConsumeCoroutine(UnitView view, float delay)
        {
            if (delay > 0f)
                yield return new WaitForSeconds(delay);
            if (view != null)
                view.SetAttachedElement(ElementType.Physical);
        }

        /// <summary>带命中时刻的状态命令到点应用（决策七十六复测返修 WYSIWYG「应当始终遵循所见即所得」）：
        /// 地形变化/附着图标/Buff 徽章类状态命令的通用延迟壳——箭在飞时冰不提前融/目标不提前结冰；
        /// 挂 playbacks=段完成门控（下一片不早于到点应用，瞄准/预判读图不读到旧态）。
        /// apply 内部自守卫（view/board 可能已销毁——场景切换/单位移除）</summary>
        private IEnumerator PlayDelayedStateApply(float delay, System.Action apply)
        {
            if (delay > 0f)
                yield return new WaitForSeconds(delay);
            apply();
        }

        /// <summary>伤害数字配色（2026-10-01 拍板+二次拍板「原神里反应都有自己的颜色」）：
        /// 普通伤害数字=BattlePalette 伤害数字元素色（原神数字=亮彩霓虹风，显著亮于元素主题色——
        /// 图标/箭矢仍用 ElementFactionConfig 主题色，两套语义勿混）；三反应各独立色
        /// （蒸发/融化/冻结——基准=官方反应图标双色调实测，按母元素冷暖拆分保互异，见 BattlePalette 字段注释）；
        /// 三反应数字均带名（三次拍板「反应名都应该加上」——冻结例外废除）。配置缺失兜底链=数字色板→
        /// ElementFactionConfig 主题色→白</summary>
        private static Color ResolveDamageNumberColor(int reactionKind, int element)
        {
            var palette = Palette;
            if (palette != null)
            {
                switch (reactionKind)
                {
                    case BattleCommand.ReactionKindMelt: return palette.反应融化色;
                    case BattleCommand.ReactionKindVaporize: return palette.反应蒸发色;
                    case BattleCommand.ReactionKindFreeze: return palette.反应冻结色;
                    case BattleCommand.ReactionKindSuperconduct: return palette.反应超导色;
                }
                switch ((ElementType)element)
                {
                    case ElementType.Pyro: return palette.伤害数字火色;
                    case ElementType.Hydro: return palette.伤害数字水色;
                    case ElementType.Cryo: return palette.伤害数字冰色;
                    case ElementType.Electro: return palette.伤害数字雷色;
                    case ElementType.Anemo: return palette.伤害数字风色;
                    case ElementType.Geo: return palette.伤害数字岩色;
                    case ElementType.Dendro: return palette.伤害数字草色;
                    case ElementType.Light: return palette.伤害数字光色;
                    default: return palette.伤害数字物理色; // Physical/未知=白（原神物理数字白）
                }
            }
            var cfg = ElementFactionConfig.Instance; // 数字色板缺失兜底：回落元素主题色（观感偏深但可用）
            return cfg != null ? cfg.GetElementColor((ElementType)element) : Color.white;
        }

        /// <summary>箭矢染色单源（2026-10-04 拍板「箭矢的颜色应当为红色」）：命中箭/消散箭/箭雨落箭
        /// 三路同源走 BattlePalette「箭矢元素色」色板——主题色≠箭矢表现色（主题火红 #EF5350 染白箭偏粉
        /// 不被读作红；同「伤害数字元素色」色板先例分家）。配置缺失兜底链=箭矢色板→ElementFactionConfig
        /// 主题色→白</summary>
        private static Color ResolveArrowTint(int element)
        {
            var palette = Palette;
            if (palette != null)
            {
                switch ((ElementType)element)
                {
                    case ElementType.Pyro: return palette.箭矢火色;
                    case ElementType.Hydro: return palette.箭矢水色;
                    case ElementType.Cryo: return palette.箭矢冰色;
                    case ElementType.Electro: return palette.箭矢雷色;
                    case ElementType.Anemo: return palette.箭矢风色;
                    case ElementType.Geo: return palette.箭矢岩色;
                    case ElementType.Dendro: return palette.箭矢草色;
                    case ElementType.Light: return palette.箭矢光色;
                    default: return palette.箭矢物理色; // Physical/未知=灰白
                    }
            }
            var cfg = ElementFactionConfig.Instance; // 色板缺失兜底：回落元素主题色（旧行为）
            return cfg != null ? cfg.GetElementColor((ElementType)element) : Color.white;
        }

        private IEnumerator PlayDamageCoroutine(UnitView view, int displayValue, float delay, bool isHeal,
            int reactionKind = 0, int element = 0, bool lifestealPrefix = false, bool isCrit = false,
            string actorUnitId = null, bool blocked = false)
        {
            if (delay > 0f)
                yield return new WaitForSeconds(delay);

            // 延迟窗口内目标视图被拆件守卫（2026-10-24 观察局终局实锤：核心消散时在途伤害数字协程×2
            // 访问已销毁 UnitView 抛 MissingReferenceException——死亡/消散命令与带命中延迟的数字协程
            // 同段并发）：视图没了数字无锚定点，静默收尾（Unity 假 null 判定覆盖已销毁对象）
            if (view == null) yield break;

            // 治疗不闪受击红（2026-09-25 三轮审查 S5 批修正：原实现无条件 FlashHit，
            // 治疗 +N 跳血时立牌闪红观感错误）；受击闪色维持原数字存活节律再恢复——
            // 拆独立协程不 gate ack（S5：纯装饰尾巴曾挂在 playbacks 里把每片拉长 0.8s）
            if (!isHeal)
            {
                view.FlashHit();
                StartCoroutine(RestoreColorAfterFlashRoutine(view));
            }
            // 装备格挡（装备批 2026-10-18·兔兔伯爵）：伤害被完全抵挡——不掉血（Host 已跳过 ApplyDamage，
            // 客户端镜像同口径），改弹「格挡」文案（尺寸随原伤害量=大攻击大反馈）；受击闪色照播
            if (!blocked)
                view.ApplyHpDelta(displayValue); // 头顶血条即时反馈（快照权威，下个选择阶段头校正）

            // 伤害/治疗数字=原神式屏幕空间层（2026-09-24 拍板「按照原神的做法」：Overlay 画布永不遮挡、
            // 尺寸随伤害对数放大；反应名前缀为 GIC 特色保留、伤害不带负号、治疗带 +；随机偏移防同点重叠。
            // 回血类曲线例外=不爆裂直接最终大小+上浮更高，2026-10-02 拍板）
            var reactionName = !isHeal ? ReactionNameOf(reactionKind) : null;
            int value = Mathf.Abs(displayValue);
            // 吸血名前缀（2026-10-01 拍板「吸血数字少了前缀」）：「吸血 +N」——与反应名前缀同风格，
            // 普通治疗仍裸 +N（color=治疗绿共用）
            // 暴击后缀「!」（2026-10-02 拍板）：伤害数字尾部追加，与反应名前缀共存（如"蒸发 40!"）；
            // 治疗不暴（暴击只 roll 伤害）恒无后缀
            string critSuffix = !isHeal && isCrit ? "!" : "";
            string text = isHeal
                ? lifestealPrefix ? $"{LifeStealName()} +{value}" : $"+{value}"
                : blocked ? BlockName()
                : reactionName != null ? $"{reactionName} {value}{critSuffix}" : $"{value}{critSuffix}";
            // 配色（2026-10-01 拍板「与原神一致」）：治疗=治疗绿；伤害=ResolveDamageNumberColor
            // （元素色/反应独特色，见该方法注释）；格挡=原神白（被挡下的攻击无数字归属）
            var numberColor = isHeal ? Palette.治疗绿
                : blocked ? (Palette != null ? Palette.伤害数字物理色 : Color.white)
                : ResolveDamageNumberColor(reactionKind, element);
            // 随机偏移防同点多数字重叠（2026-09-24 目检后用户拍板加大散布：XZ 加宽、高度带随机）
            var randomOffset = new Vector3(
                UnityEngine.Random.Range(-0.42f, 0.42f), UnityEngine.Random.Range(0.3f, 0.65f),
                UnityEngine.Random.Range(-0.15f, 0.15f));
            // 迷雾批追拍二（2026-10-17 拍板「我方特效不用隐藏，比如我方安柏的箭矢，命中后的伤害数字等」）
            // ——修订首版 D4 的目标侧抑制为**归属方**口径：我方命中的伤害数字恒显（即便目标在迷雾内）；
            // 仅敌方施放且目标隐藏（敌方自奶/自增益类反馈）才抑制
            if (view.IsFogHidden && _fog != null && _fog.IsEnemyActor(actorUnitId)) yield break;
            // 维度视角门（2026-10-25 泄漏修复：主世界伤害数字只在主世界视角弹、灵魂数字只在地脉
            // 视角弹——Overlay 画布不吃相机 culling，逐弹出点门控；②a 泛化 DimensionView）
            if (view.DimensionView != _currentDimension) yield break;
            EnsureDamageNumbers().Spawn(view.transform.position + randomOffset, text,
                numberColor, value, _playbackSpeed, isHeal, isCrit);
        }

        /// <summary>受击闪色恢复尾巴（S5：不进 playbacks=不 gate ack——Host 片节拍只等位移/伤害主体）</summary>
        private IEnumerator RestoreColorAfterFlashRoutine(UnitView view)
        {
            yield return new WaitForSeconds(0.8f / _playbackSpeed);
            if (view != null) view.RestoreColor();
        }

        /// <summary>元能命中时刻应用（2026-09-25 拍板「命中时才给」）：战技获能命令带命中毫秒——
        /// 到点再跳元能（与投射物命中表现同时刻）；0=立即不走本协程。获取类到点随跳弹「+N」元能数字
        /// （弹字链已实现——**暂不弹**：SpawnEnergyNumber 开关守卫，2026-10-01 拍板「暂时决定不弹」；
        /// 恢复=Inspector 勾「元能获取弹数字」）</summary>
        private IEnumerator PlayEnergyDeltaCoroutine(UnitView view, BattleCommand command)
        {
            yield return new WaitForSeconds(LaunchDelayOf(command));
            if (view == null) yield break; // 命中延迟窗口内视图被拆（同 PlayDamageCoroutine 守卫族）
            view.ApplyEnergyDelta(command.value);
            SpawnEnergyNumber(view, command.value);
        }

        /// <summary>元能获取弹数字（2026-10-01 拍板④「目前少了元能数字弹出」曾启用；**同日拍板翻转
        /// 「不要整个删掉，只是暂时决定不弹」——实现与两处调用全保留，仅关本开关**）</summary>
        [SerializeField] [Tooltip("元能获取随条跳变弹「+N」白字（2026-10-01 拍板暂时关闭——实现保留，勾选即恢复）")]
        private bool 元能获取弹数字 = false;

        /// <summary>元能获取数字：「+N」白=元能条色单源（Palette.元能条色），复用伤害数字屏幕空间层
        /// （尺寸/停留/边缘夹取同链）；**消耗不弹**（delta&lt;0 静默——爆发扣 40 不飘负数，条本身会掉）；
        /// 随机偏移同伤害/治疗防同点重叠。两处调用（即时/到点协程）不动——恢复弹字只改开关</summary>
        private void SpawnEnergyNumber(UnitView view, int delta)
        {
            if (!元能获取弹数字) return; // 拍板「暂时决定不弹」——实现与调用链全保留，恢复只改本开关
            if (delta <= 0 || view == null) return;
            if (view.IsFogHidden) return; // 迷雾批（2026-10-17 拍板 D4）：迷雾内敌方不弹数字
            var randomOffset = new Vector3(
                UnityEngine.Random.Range(-0.42f, 0.42f), UnityEngine.Random.Range(0.3f, 0.65f),
                UnityEngine.Random.Range(-0.15f, 0.15f));
            if (view.DimensionView != _currentDimension) return; // 维度视角门（2026-10-25 泄漏修复；②a 泛化）
            EnsureDamageNumbers().Spawn(view.transform.position + randomOffset, $"+{delta}",
                Palette.元能条色, delta, _playbackSpeed);
        }

        /// <summary>玩家资源命令命中时刻应用（B-3 摩拉掠夺，同元能「命中时才给」）：到点再发资源事件；
        /// 0=立即不走本协程（部署扣费/回合发放/体力恒立即）</summary>
        private IEnumerator PlayResourceDeltaCoroutine(BattleCommand command)
        {
            yield return new WaitForSeconds(LaunchDelayOf(command));
            OnResourceDelta?.Invoke(command.targetUnitId, command.metadata, command.value);
        }

        /// <summary>物品消耗命令扣本地手牌镜像（统一消耗模型 C-1）：LatestSnapshot.resources 内该玩家
        /// handCards 条目数量减/减尽移除——片内即时反映（HUD 角标刷新走 OnItemConsumed 事件）；
        /// 非我方玩家命令仅镜像维护（HUD 只刷我方手牌），下回合快照权威兜底</summary>
        private void ApplyItemConsumeToLocalHand(string playerId, ItemName item, int amount, int durability)
        {
            var snapshot = LatestSnapshot;
            if (snapshot == null) return;
            PlayerResourceState res = null;
            foreach (var r in snapshot.resources)
            {
                if (r.playerId == playerId) { res = r; break; }
            }
            if (res == null) return;
            HandCard target = null;
            foreach (var entry in res.handCards)
            {
                if ((CardType)entry.cardType == CardType.Item && entry.value == (int)item
                    && entry.durability == durability) { target = entry; break; }
            }
            if (target == null)
            {
                GICLog.Warn($"[BattlePlayer] ItemConsume 镜像无条目（{playerId} {item}×{amount} 耐久{durability}）——快照权威自愈兜底");
                return;
            }
            target.count -= amount;
            if (target.count <= 0)
                res.handCards.Remove(target);
        }

        /// <summary>获得物品卡命令并本地手牌镜像（命座授予/死亡掠夺）：LatestSnapshot.resources 内
        /// 该玩家 handCards 条目按 (物品,实例耐久) 匹配——有则加数量/无则新建携带耐久条目（GainCard
        /// 对偶——装备按耐久分实例，掠夺物耐久不变保留；货币卡走资源池不在此列）；
        /// HUD 重建走 OnItemGained 事件；下回合快照权威兜底</summary>
        private void ApplyItemGainToLocalHand(string playerId, ItemName item, int amount, int durability)
        {
            var snapshot = LatestSnapshot;
            if (snapshot == null) return;
            PlayerResourceState res = null;
            foreach (var r in snapshot.resources)
            {
                if (r.playerId == playerId) { res = r; break; }
            }
            if (res == null) return;
            foreach (var entry in res.handCards)
            {
                if ((CardType)entry.cardType == CardType.Item && entry.value == (int)item
                    && entry.durability == durability)
                {
                    entry.count += amount;
                    return;
                }
            }
            var newCard = new HandCard(new CardId(item), amount);
            newCard.durability = durability;
            res.handCards.Add(newCard);
        }

        /// <summary>获得单位卡的本地手牌镜像（购买批 2026-10-22）：按 (Unit, value) 条目合并数量/
        /// 新建条目（单位卡无耐久实例语义——同种角色多张合并 count）；镜像后 HUD 经 OnUnitCardGained
        /// 重建手牌，下回合快照权威自愈</summary>
        private void ApplyCardGainToLocalHand(string playerId, int unitName, int amount)
        {
            var snapshot = LatestSnapshot;
            if (snapshot == null) return;
            PlayerResourceState res = null;
            foreach (var r in snapshot.resources)
            {
                if (r.playerId == playerId) { res = r; break; }
            }
            if (res == null) return;
            foreach (var entry in res.handCards)
            {
                if ((CardType)entry.cardType == CardType.Unit && entry.value == unitName)
                {
                    entry.count += amount;
                    return;
                }
            }
            res.handCards.Add(new HandCard(new CardId((UnitName)unitName), amount));
        }

        /// <summary>原神式伤害数字层懒建（随 BattleScreen 场景卸载消亡）</summary>
        private BattleDamageNumbers EnsureDamageNumbers()
        {
            if (_damageNumbers == null)
            {
                var go = new GameObject("DamageNumbers");
                go.transform.SetParent(transform, false);
                _damageNumbers = go.AddComponent<BattleDamageNumbers>();
                _damageNumbers.Init(_viewCamera != null ? _viewCamera : CameraContext.Resolve());
            }
            return _damageNumbers;
        }

        /// <summary>原神式头顶条层懒建（血条+元能条，2026-09-24；随 BattleScreen 场景卸载消亡）</summary>
        private BattleOverheadBars EnsureOverheadBars()
        {
            if (_overheadBars == null)
            {
                var go = new GameObject("OverheadBars");
                go.transform.SetParent(transform, false);
                _overheadBars = go.AddComponent<BattleOverheadBars>();
                _overheadBars.Init(_viewCamera != null ? _viewCamera : CameraContext.Resolve());
                if (_hpBarColorOf != null)
                    _overheadBars.SetHpBarColorOf(_hpBarColorOf); // 血条色解析器透传（2026-10-15 三色批；Bind 先于此注入）
            }
            return _overheadBars;
        }

        /// <summary>头顶条层访问器（BattleHud.Bind 接线用——来源玩家色解析器+相机缩放源注入；
        /// 幂等：未建层时先建空层，OnBattleStart 再逐单位 Register）</summary>
        public BattleOverheadBars OverheadBars => EnsureOverheadBars();

        private IEnumerator PlayDeathCoroutine(UnitView view, float delay)
        {
            if (delay > 0f)
                yield return new WaitForSeconds(delay);
            view.SetCorpseVisual(true);
        }

        /// <summary>消散退场协程（决策八十六批B，docs/45 §4）：延迟到位后驱动 UnitView 全渲染面
        /// 淡出（素材批落地前=通用渐隐，特效随素材批替换），淡出完成清理视图——_views/头顶条/
        /// 专属弹射物登记（UnitView.OnDestroy 自清常驻播放器/RT 租约/材质，Destroyed 事件→
        /// ResidentVideoBudget 注销双保险）。此后该 unitId 的一切命令 TryGetValue miss=静默跳过
        /// （Host 已同步移除=预期；快照消失路径天然自洽）。对局中途清场（ClearViews）竞态由
        /// Unity-null 守卫吸收</summary>
        private IEnumerator PlayDissolveCoroutine(UnitView view, float delay)
        {
            const float fadeSeconds = 0.45f;
            if (delay > 0f)
                yield return new WaitForSeconds(delay);
            if (view == null) yield break;
            float elapsed = 0f;
            while (elapsed < fadeSeconds && view != null)
            {
                elapsed += Time.deltaTime;
                view.SetDissolveAlpha(elapsed / fadeSeconds);
                yield return null;
            }
            if (view == null) yield break;
            _overheadBars?.Unregister(view);
            _customProjectiles.Remove(view.UnitId);
            _views.Remove(view.UnitId);
            Destroy(view.gameObject);
        }

        // ==================== 立牌与队形 ====================

        private void ClearViews()
        {
            foreach (var kv in _views)
                if (kv.Value != null)
                    Destroy(kv.Value.gameObject);
            _views.Clear();
            _customProjectiles.Clear(); // per-unit 专属弹射物随场同清（与 _views 同生命周期）
            _overheadBars?.ClearAll(); // 头顶条随单位同清（2026-09-24 屏幕空间层）
            ResidentVideoBudget.ClearAll(); // 常驻解码器注册表随清场清空（2026-10-16 常驻批——单位 OnDestroy 已逐个拆，此处双保险）
            ActionRtPool.ClearAll(); // 动作 RT 全局池随场清（#3 审查批：共享/私有 RT 释放+租约清）
            _fog?.Clear(); // 迷雾批 2026-10-17：探索集/雾面/隐藏态随场同清
        }

        /// <summary>层级→眷属判定（命座徽章门控数据，2026-10-06）：快照 tier 优先（0=未填回落星级换算，
        /// 与 BattleHud.TierOfUnit 同口径——沙盒 TierOverrideStars 覆盖随快照 tier 生效）</summary>
        private bool ResolveIsFamiliar(UnitState state)
        {
            if (state == null) return false;
            if (state.tier != 0) return (UnitTier)state.tier == UnitTier.Familiar;
            return _unitConfig != null && Enum.TryParse<UnitName>(state.unitName, out var name)
                && _unitConfig.TryGetUnitData(name, out var d)
                && UnitTierHelper.FromStars(d.starLevel) == UnitTier.Familiar;
        }

        private void CreateView(UnitState state)
        {
            if (_views.ContainsKey(state.unitId)) return;

            // 客户端只依快照与共享配置建场（不触 Host 逻辑对象）；配置经 [Autowired] 注入（Y10）
            // 灵魂（决策九十一）：幽灵形态（冰蓝半透明+幽浮+柔光晕）+待机动画视频（2026-10-26 拍板
            // 「灵魂应当用其待机动画，而不是静态的待机立牌」——曾「跳视频省解码器」已废）、
            // 维度视角门（主世界默认隐藏——地脉视角现身）；动作片预热仍跳（灵魂无行动）
            bool isSoul = state.isSoul != 0;

            Sprite avatar = null;
            bool useFullBody = false;
            VideoClip idleVideo = null;
            var prewarmClips = new List<VideoClip>(); // 动作片预热线（决策四十五）：本单位全部技能动作视频（含 Move）
            float unitScale = 1f;   // UnitData.额外缩放（2026-09-27 拍板：乘在全身立牌放大倍数之上，1=不缩放）
            float hoverHeight = 0f; // UnitData.离地高度（2026-09-27 拍板：飞行/悬浮单位纸片人整体上浮）
            float standForward = 0f; // UnitData.立牌前移（2026-10-14 拍板：俯视立牌视觉重心补偿，0=不移动）
            GameObject buildingModel = null; // UnitData.建筑模型（配置后以 3D 替代纸片立牌）
            float buildingModelWidth = 1f;
            if (_unitConfig != null && Enum.TryParse<UnitName>(state.unitName, out var unitName) &&
                _unitConfig.TryGetUnitData(unitName, out var unitData))
            {
                // 全身立牌（立牌图）人物占比小，按 Inspector 倍数整体放大；缺立牌图的单位回落头像原尺寸
                useFullBody = unitData.立牌图 != null;
                avatar = useFullBody ? unitData.立牌图 : unitData.avatar;
                // （单位名世界空间文本已随 2026-10-06 命座徽章批退役——名字显示改血条上方命座数字）
                // 立牌循环动画（B-S3 视频路线）：视频（绿幕+运行时 ChromaKey 抠色）；null=静态立牌兜底
                // （序列帧路线已退役——2026-10-05 拍板全库移除，旧尝试遗留零单位在用）。
                // 移动循环片=per-skill（决策四十四「尽可能统一」：Move 型技能 SkillData.动作视频，
                // SkillCast 登记+Move 片起止消费——不再走单位级字段）
                // **灵魂同享待机动画视频（2026-10-26 拍板）**：tint 走 ChromaKey _Color、幽浮/光晕/
                // 无底座盘链对视频路径全兼容（RefreshTint 单源）；灵魂=+1 解码器属拍板接受成本
                idleVideo = unitData.立牌动画视频;
                // 动作片预热线（决策四十五：首次施放/移动零黑屏）——仅视频路径单位有意义
                // （动作片走主视频机，PlayActionVideo 无播放器早退；灵魂无行动=跳过预热省常驻解码器，
                // 待机视频照常）
                if (idleVideo != null && !isSoul && unitData.skills != null)
                    foreach (var s in unitData.skills)
                    {
                        var d = s != null ? s.data : null;
                        if (d != null && d.当前动作片 != null && !prewarmClips.Contains(d.当前动作片))
                            prewarmClips.Add(d.当前动作片);
                    }
                unitScale = unitData.额外缩放;
                hoverHeight = unitData.离地高度;
                standForward = unitData.立牌前移;
                buildingModel = unitData.建筑模型;
                buildingModelWidth = unitData.建筑模型宽度;
                if (unitData.专属弹射物 != null && !isSoul) // per-unit 专属弹射物登记（2026-10-05 拍板；命中/消散/箭雨三路消费——灵魂无弹射路径不登记）
                    _customProjectiles[state.unitId] = (unitData.专属弹射物,
                        unitData.专属弹射物缩放 > 0f ? unitData.专属弹射物缩放 : 1f);
            }

            var team = (TeamType)state.team;
            // 单位显示色=归属玩家色（2026-10-12 拍板取代旧「队伍主色」口径——2v2 同队无法区分彼此单位；
            // 底座圆盘/选中描边/血条填充全链同源；HUD 未绑解析器时回落旧队伍主色兜底）
            var unitColor = _playerColorOf != null
                ? _playerColorOf(state.playerId)
                : (team == TeamType.B ? Palette.敌方主色 : Palette.我方主色);
            var view = UnitView.Create(_viewRoot, state.unitId, avatar, unitColor,
                _billboardRotation, 立牌后倾角, state.hp, state.maxHp,
                (useFullBody ? 全身立牌放大倍数 : 1f) * unitScale, idleVideo, hoverHeight,
                state.cylinderDiameter, standForward, buildingModel, buildingModelWidth); // per-unit 受击圆柱直径（协议核心批：快照真源与 Host 判定同源，0=回落全局 0.42）；立牌前移（俯视立牌格心视觉补偿）
            view.OwnerPlayerId = state.playerId; // 归属身份（2026-10-15 血条三色批：三色模式按自己/队友/敌分档判定）
            view.OwnerTeam = (TeamType)state.team;
            // 维度渲染层（2026-10-25 泄漏修复拍板 A）：单位渲染件整树挂维度层——相机 cullingMask
            // 单开关管全部单位 3D；按 state.dimension 落层（灵魂=LeyLine、主世界=Main；霜月=新枚举自动纳入）
            SetViewDimensionLayer(view.transform, (BattleDimension)state.dimension);
            view.Cell = state.position;
            view.SetCorpseVisual(state.isCorpse != 0);
            view.SetFrozenVisual(state.isFrozen != 0);
            view.SetAttachedElement((ElementType)state.dyedElement); // 附着元素（建场权威态）
            view.SetEnergy(state.energy, state.maxEnergy); // 元能（建场权威态；B6a）
            view.SetSanity(state.sanity); // 理智（建场权威态；2026-09-30 歌声之环批）
            view.SetConstellation(state.constellation, ResolveIsFamiliar(state)); // 命座+眷属（2026-10-06 命座徽章批）
            view.SetBuffs(state.buffs);
            view.SetEquipment(state.weaponItem, state.weaponDurability, state.accessoryItems,
                state.accessoryDurability); // 装备态（建场权威态；装备批 2026-10-18）
            view.ApplyPosition(_board.CellToWorld(state.position));
            _views[state.unitId] = view;
            if (isSoul)
            {
                // 灵魂形态落地（决策九十一）：冰蓝半透明+幽浮+柔光晕（参数=Inspector「地脉灵魂表现」段）
                view.SetSoulVisual(灵魂漂浮幅度, 灵魂漂浮速度, 灵魂立牌不透明度, 灵魂光晕不透明度);
            }
            // 维度视角门建场初始化（2026-10-26 返修二）：地脉视角期间入场的主世界单位（部署/召唤）
            // 3D 被 cullingMask 隐藏，但 Overlay 头顶条/伤害数字不经相机——漏设 IsDimensionHidden=
            // 消费方逐帧重估恒显=「凭空出现血条」根因（此前仅灵魂分支设，主世界分支漏）。
            // 幂等早退：主世界视角建场零开销。详案=docs/14 §174
            view.SetDimensionVisible(view.DimensionView == _currentDimension);
            if (prewarmClips.Count > 0)
                view.PrewarmActionClips(prewarmClips); // 动作片建场预热（决策四十五）：首次施放/移动零黑屏
            EnsureOverheadBars().Register(view); // 原神式头顶条（血条+元能条+附着图标，2026-09-24；灵魂=蓝色血条计时器，2026-10-25）
        }

        /// <summary>单位渲染树挂维度层（2026-10-25 泄漏修复拍板 A；②b 泛化 dimension 落层）——与
        /// BattleViewFactory.DimensionLayerName 同源；层未配置（<0）静默退回旧行为
        /// （IsDimensionHidden 逐单位门仍在，双保险）</summary>
        private static void SetViewDimensionLayer(Transform root, BattleDimension dimension)
        {
            int layer = LayerMask.NameToLayer(BattleViewFactory.DimensionLayerName(dimension));
            if (layer < 0) return;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layer;
        }

        /// <summary>
        /// 立牌归位（2026-11-07 拍板「同格多单位不需要额外站队，自然即可」）：
        /// 原两态队形（选择阶段散开 / 执行阶段收拢）整体退役——任何阶段都贴各自权威站位，
        /// 同格多单位自然重叠。本方法只做一次幂等归零重放（收敛历史残留偏移）。
        /// </summary>
        private void RefreshAllFormations()
        {
            foreach (var view in _views.Values)
            {
                if (view.IsSoulView) continue; // 灵魂不参与队形（决策九十一：恒钉自身位置浮于尸体上——幽灵语义）
                // 移动补间独占位置（2026-11-05「角色闪烁/瞬移」修复）：片尾每段都会重放全场立牌，
                // 而移动中的 view.Cell 是「出发时登记的路径终点格心」——重排会把已推进的立牌拽到终点
                // 格心，下一帧补间又写回近起点，多段并发下每帧来回瞬移。移动中单位的位置一律由
                // PlayMoveCoroutine 单源写入，移动结束（活动表移除）后才重新纳入。
                if (_activeMovePlayback.Contains(view.UnitId)) continue;
                view.ApplyFormationOffset(Vector3.zero);
            }
        }
    }
}
