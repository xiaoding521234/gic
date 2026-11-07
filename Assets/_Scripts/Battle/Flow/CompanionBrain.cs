using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 伙伴决策脑（D 批次操控分层，docs/32 §3/§6.1）：3-4 星伙伴单位自主决策——
    /// Host 在**收齐全部玩家选择之后、开演之前**的机器瞬时阶段跑（五阶段流程的「伙伴决策阶段」，
    /// 无选择时限、玩家无感知、HUD 不设 UI 态；与眷属决策同为 Host 内部阶段，零新协议）。
    ///
    /// 感知输入（docs/32 §3「意图可见性边界」）：
    /// - **己方玩家已提交选择**（号令/部署/魔神操作等）——①已被号令的单位跳过自主决策
    ///   （玩家上交该单位势力技能=其本回合行动，无白决策）；②体力预留：同池需求总先于执行可知
    ///   （池只减不增，总需求≤池则任意攻速序全部成立），伙伴决策按「实池−己方已提交消耗−
    ///   已定伙伴消耗」的虚拟池判定——不出现「选了落空」（§5.2）；
    /// - 敌方玩家选择不可见（同时回合制信息隐藏）；己方眷属意图 v1 不可见（信息口径统一）。
    ///
    /// 动作空间=移动/战技/爆发（**势力技能选项剥离**——延奏/契约=玩家域，自主单位永不自用）；
    /// 无好行动=缺席（站桩；评分制「有好行动才动」=体力经济上界低于理论值）。
    /// 决策确定性：unitId 升序 × skillIndex 升序 × 十字向枚举序，严格大于替换（同分先到先得）。
    ///
    /// 共享评分骨架（§6.1）：ScoreUnitCandidates=评分制 v2 单位级评估的骨架收口——
    /// AIDebugBrain→PlayerQuotaBrain（AI 玩家配额脑）操魔神档复用同一骨架（评分制单位级评估既有逻辑复用为候选打分器）；
    /// 行为差异全在 UnitConfig.行为档案（每维度乘档案权重），骨架稳定后加维度勿改骨架调用方。
    /// P5 连续空间移植（2026-11-04 决策九十五蓝图 §6）：移动档=「选目标点」（StandoffTargetCm 驻位点，
    /// targetX/targetZ 直交——A*+steering 全权交执行层 RTSMovementSystem）；攻击档=任意角瞄准
    ///（aimX/aimZ 千分向量，PredictAimTargets 线段×圆预判——存活/雾限/AoE 首接触截停内建）；
    /// 距离全欧氏 cm；十字向枚举/BFS 直线段/轴对齐/换线探针/真无解兜底/移动意图占位
    ///（BoardReservedCells）全退役——自由角时代「无线可打」问题结构性消失（蓝图 §4.1）。
    /// </summary>
    public static class CompanionBrain
    {
        // ==================== 评分口径（#4 审查批资产化：数值真源=Resources/Configs/AI/AITuning.asset——
        // 调 AI 平衡=改资产 Inspector 零代码；下列属性=代码侧访问别名，勿散写调用处） ====================

        /// <summary>斩杀加成：预估伤害 ≥ 目标当前生命（保守估值，未计反应乘区）</summary>
        public static int KillBonusScore => AITuningConfig.Instance.击杀斩杀加成分;

        /// <summary>战技命中获能 +10 的评分（B6a：向爆发攒能的行动价值）</summary>
        public static int SkillHitEnergyScore => AITuningConfig.Instance.战技命中获能分;

        /// <summary>移动兜底分（低于一切攻击/有效治疗候选）</summary>
        public static int MoveBaseScore => AITuningConfig.Instance.移动兜底分;

        /// <summary>移动每逼近 1 格的加分</summary>
        public static int MoveProgressScorePerCell => AITuningConfig.Instance.移动每格逼近分;

        /// <summary>无敌环境推进分（2026-10-25 凌晨批议题①，docs/11 决策八十四挂账）：感知无敌
        ///（无可感知非建筑敌——敌锚全为建筑=核心透视恒可见）且移动锚为敌方建筑时的移动加分——
        /// 治「号令挤掉推核心」（决策八十四根因④：配额脑延奏续航 10 恒赢裸推进 10+2p， commanded
        /// 单位被无营养号令占用、伙伴自主推进被压制）。量级设计：低于一切战斗候选（攻击/治疗
        /// 20~90）、高于裸移动兜底与延奏续航档——配额脑操魔神/伙伴操控共享骨架同获该分，号令
        /// 在无敌环境让位推进。业界对照：petra/ItB 无目标时目标导向推进（attack-move 语义）。</summary>
        public static int EmptyFieldAdvanceScore => AITuningConfig.Instance.无敌环境推进分;

        /// <summary>采集意图基准分（docs/47 拍板⑦，2026-10-27）：稳压攻击档典型分——采集型角色
        /// 有可见资源点时优先采集移动（保命档恒在其上）；**不乘人格移动优先权重**（拍板硬语义）</summary>
        public static int CollectIntentBaseScore => AITuningConfig.Instance.采集意图基准分;

        /// <summary>采集每格衰减分（docs/47）：采集候选分=基准分−距离×本值——距离越远优先度
        /// 越低，衰减至攻击典型分之下自然回归战斗（「够得着才优先」软边界）</summary>
        public static int CollectDistancePenalty => AITuningConfig.Instance.采集每格衰减分;

        /// <summary>近敌接敌加分上限（离最近敌越近越优先动该单位）</summary>
        public static int MoveEngageBonusMax => AITuningConfig.Instance.接敌加分上限;

        /// <summary>走位进射击线加分（移动落点即有可开火攻击线=下回合可输出）</summary>
        public static int MoveLineUpScore => AITuningConfig.Instance.射线对齐分;

        /// <summary>治疗候选门槛：有效治疗 ≥ 治疗量一半才值得占行动（勿为挠痒花行动）</summary>
        public static int HealWorthRatioPercent => AITuningConfig.Instance.治疗门槛百分比;

        /// <summary>单位指向型爆发：复苏候选评分（B-3 ② 芭芭拉闪耀奇迹——回一整个单位+40% 血，
        /// 价值对齐斩杀档之上：100&gt;KillBonus 80，有尸体=复苏即最优）</summary>
        public static int UnitTargetBurstReviveScore => AITuningConfig.Instance.复苏候选分;

        /// <summary>单位指向型爆发：增益候选评分（歌声之环永久光环——中等偏高：低于斩杀档、
        /// 高于常规攻击均值，攒满即放勿囤积；调手感改此常量）</summary>
        public static int UnitTargetBurstBuffScore => AITuningConfig.Instance.单位指向增益分;

        /// <summary>无目标自施放爆发：自身增益候选评分（aimMode=None——首个=凯亚凛冽轮舞寒冰之棱：
        /// 持续冰伤+碎裂回血复合价值，对齐单位指向增益档；攒满即放勿囤积——能量满后继续获取即浪费；
        /// 调手感改此常量）</summary>
        public static int SelfCastBurstBuffScore => AITuningConfig.Instance.自施放增益分;

        /// <summary>J-2 自施放爆发语境评分（2026-10-04 拍板）：即将施加的光环半径内每个存活敌的
        /// 加分——贴敌群施放=每回合 tick 逐层吃满（优先于常规攻击先放），空场施放=预载（Buff 永久
        /// 非浪费，维持基础分照常放）</summary>
        public static int SelfCastBurstPerEnemyScore => AITuningConfig.Instance.自施放每敌分;

        /// <summary>J-1 冻结协同分（2026-10-04 拍板）：反应预览命中冻结（控制反应——目标冰冻 2 回合
        /// 跳过行动）的 flat 加分——旧口径冻结无增伤乘区恒 0 分，凯亚对「打哪个敌人」无偏好；
        /// 芭芭拉光环挂水后霜袭打水附着目标=冻结联动主链；量级低于斩杀 80、高于常规集火每档 3</summary>
        public static int FreezeControlScore => AITuningConfig.Instance.冻结控制分;

        /// <summary>随军追逐兜底分（2026-10-04 观察局返修）：先锋伴随 BFS 追逐兜底原为裸
        /// MoveBaseScore(10)——恒输一切技能（水球≈57）=被先锋落后超过伴随半径后永远站桩施法不再
        /// 归队。托底 60=压常规单体技能（先归队再输出）、低于群奶/救命奶（75-90，途中仍救场）
        /// 与就位托底 95；调手感改此常量</summary>
        public static int VanguardChaseFloorScore => AITuningConfig.Instance.追赶托底分;

        /// <summary>反威胁分上限（E-1 配额脑对手建模，docs/archive/33 §2.3）：攻击候选目标是敌方
        /// 威胁源（威胁图 EnemyThreatScore 高者）时加分——优先拆除敌方火力核心；上限低于斩杀 80
        /// （反威胁优先于平打、不优先于斩杀）。伙伴脑 threat=null 不消费=零行为变化</summary>
        public static int ThreatResponseScoreCap => AITuningConfig.Instance.反威胁分上限;

        /// <summary>落点受威胁扣分（E-1）：候选落点在敌方预测火线格集合内=下回合白挨打，避险
        /// 走位；与走位进射击线加分（8）同量级正负抵偿——有开火线但在火线上=平手倾向、纯避险
        /// 换位为负倾向。扣后钳 1 保底 Offer（危险落点排后不缺席——挂机防线语义）</summary>
        public static int ThreatenedCellPenalty => AITuningConfig.Instance.火线格惩罚;

        /// <summary>集火每档加分（E-2 协调层，docs/archive/33 §3）：行为档案「集火权重」1 档=+3 分——
        /// 量级低于战技获能（12）与斩杀（80）：集火倾向只影响同分域内的目标选择，不压过斩杀/反应
        /// 等强信号；权重 0（默认）=游走型不消费</summary>
        public static int FocusFireScorePerRank => AITuningConfig.Instance.集火每名次分;

        /// <summary>支援自保危险半径（F-1，docs/archive/34 §5.3）：落点距最近敌的切比雪夫距离
        /// 小于此值=「站敌人刀口」——支援型自保负分生效阈值</summary>
        public static int SupportDangerRadius => AITuningConfig.Instance.支援危险半径;

        /// <summary>光环敌覆盖每敌加分（G-1 光环位置价值，docs/archive/35）：持有半径型 tick 光环
        /// 时落点光环半径内每敌 +6——量级对齐走位线分 8（3 敌=18 显著牵引）：光环挂水/挂冰引擎
        /// 的贴敌驱动力；乘档案「光环贴敌权重」（0=默认不消费）与形态因子（保险形态归零 G-2）</summary>
        public static int SupportAuraPerEnemy => AITuningConfig.Instance.光环贴敌每敌分;

        /// <summary>支援形态切换血线门（G-2，docs/archive/35 §2）：hp% 低于此值=保险形态
        ///（贴敌分归零+自保×SupportStanceSafeFactor 后撤）——她活着=复活保险在（复苏无限程，
        /// 生存问题非站位问题）；光环 tick 含持有者自奶→奶回线上自动重返前线。
        /// 2026-10-04 观察局返修（W1）：伙伴流 threat=null 时血线门补「近敌存在」条件——
        /// 无近敌+低血线=安全区（低血不构成危险，随军照常、光环途中自愈）；有近敌+低血线
        /// 才保险后撤（G-2 语义保持）。观察局实证：她在 (2,13) 周围 7+ 格无敌却因 49% 血
        /// 保险驻场 15+ 回合，先锋前排单打至死无奶</summary>
        public static int SupportStanceRetreatHpPercent => AITuningConfig.Instance.保险血线百分比;

        /// <summary>保险形态近敌半径（2026-10-04 W1 返修）：血线门降级判定的「威胁可及域」——
        /// 最近敌切比雪夫 ≤ 此值=有近敌（保险候选），否则=安全区（攻击视野回落 5 同口径）</summary>
        public static int InsuranceDangerRadius => AITuningConfig.Instance.保险近敌半径;

        /// <summary>支援形态切换预警门（G-2）：威胁图预期承伤 ≥ hp×此百分比=保险形态——
        /// 提前一回合后撤（不等到掉血才反应；E-1 威胁图基建复用零新感知；threat=null 时仅血线门）</summary>
        public static int SupportStanceRetreatThreatPercent => AITuningConfig.Instance.保险预警承伤百分比;

        /// <summary>保险形态自保倍率（G-2）：自保负分 ×3=贴敌格扣 45 分——后撤位（远离敌仍在
        /// 奶程内）自然胜出</summary>
        public static int SupportStanceSafeFactor => AITuningConfig.Instance.保险自保倍率;

        /// <summary>光环前瞻基准距离（G-5 光环前瞻梯度，docs/archive/35 §2 返修——远距趋近驱动力）：
        /// 距最近敌此距离处=0 分起点，每近 1 格 +SupportAuraApproachPerStep，上限 Cap。
        /// **2026-10-03 验证局调参 8→14**：开局敌距 9~11 格、旧值 8=零牵引（她缩角到敌自己走过来）；
        /// 14 覆盖测试军地图对角开局距</summary>
        public static int SupportAuraApproachBase => AITuningConfig.Instance.光环前瞻基准距离;

        /// <summary>光环前瞻每步分（G-5）：每近敌 1 格 +3（权重 1 档）——**验证局调参 2→3**：
        /// 4 格敌距=+12 恰好对冲火线扣分 12（站位评分器内支援型火线减半后=净正 +6）、6 格=+6
        /// 净正——趋敌梯度在前线形态全程为正</summary>
        public static int SupportAuraApproachPerStep => AITuningConfig.Instance.光环前瞻每步分;

        /// <summary>光环前瞻上限（G-5）：+12=对齐「光环内 2 敌」满档——近身光环实分接管后前瞻失效
        ///（enemiesInAura>0 时不再叠加前瞻，防双计）</summary>
        public static int SupportAuraApproachCap => AITuningConfig.Instance.光环前瞻上限;

        /// <summary>支援站位火线扣分减半系数（G-5 验证局调参）：站位评分器内火线扣分 ×50%——
        /// 支援型前线形态「接受中等风险换贴敌挂水」（整列火线吓退支援型=缩角实证）；E-1 配额脑
        /// 原路径全额不动（输出型零回归）</summary>
        public static float SupportThreatPenaltyScale => AITuningConfig.Instance.支援火线扣分系数;

        /// <summary>保险形态后撤梯度（G-5 验证局调参——保险形态零移动驱动返修）：远离最近敌每格
        /// +2（上限 12）——旧版保险形态贴敌归零+前瞻归零后 2~11 格区间无任何驱动=驻位挨打；
        /// 后撤梯度给她方向性（撤到我方阵内/敌方射程外）</summary>
        public static int SupportRetreatPerStep => AITuningConfig.Instance.保险后撤每格分;

        /// <summary>保险形态后撤梯度上限（G-5）</summary>
        public static int SupportRetreatCap => AITuningConfig.Instance.保险后撤分上限;

        /// <summary>先锋交战判定距离（H-2 交战就位，docs/archive/36 §5——2026-10-04 拍板「先锋与敌人
        /// 交战时优先主动上前贴脸敌人」）：锚单位（先锋/伤员=先锋）距最近敌 ≤ 此值=交战态
        ///（对齐凯亚霜袭 2/丘丘人挥棒 1 的近战交换距离）</summary>
        public static int VanguardEngageRange => AITuningConfig.Instance.先锋交战距离;

        /// <summary>交战就位优先 Offer 托底分（H-2）：锚交战+未就位（当前格光环罩不到敌）+最优格
        /// 光环罩得到敌（就位可达）时，移动 Offer 分托底到恒压常规技能（水球伤害+获能+治疗、
        /// VitalityBurst 群奶≈50-90）。**让位门控**（恒在就位之上，勿靠分数竞争——救命/复苏量级
        /// 与常规技能挤同窗）：威胁图将死队友存在→救命奶让位；我方尸体在场且爆发满槽→复苏让位；
        /// 斩杀分（+80×激进度）自然 &gt;95 恒赢。双覆盖格（光环同时罩敌+先锋）=奶程 20+光环 6×敌
        /// 已是评分器天然最高分格（交集格无需专项逻辑）</summary>
        public static int SupportAuraTakePositionScore => AITuningConfig.Instance.就位接管托底分;

        /// <summary>伴随语境光环敌分倍率（H-3 随军优化，2026-10-04 拍板「主动跑去先锋攻击视野内的
        /// 敌，让光环尽可能覆盖多的敌人，覆盖先锋为其次」）：伴随调用（视野敌集传入）时光环内
        /// 每敌分=SupportAuraPerEnemy×此倍率——视野敌群覆盖主导选格（罩 2~3 视野敌的格压过先锋旁
        /// 单敌格）；同敌数下奶程仍区分先锋旁 vs 远格（「覆盖先锋为其次」的其次语义）。非伴随调用
        /// （伤员/光环贴敌/保险）恒 ×1=G 批基线不动</summary>
        public static int VanguardAuraPerEnemyMult => AITuningConfig.Instance.伴随光环每敌倍率;

        /// <summary>支援自保基准扣分（F-1）：量级压过移动兜底（10）+走位进射击线（8）——有更远
        /// 候选时支援型不选贴敌落点；乘档案「自保权重」（0=默认不消费，1=标准）</summary>
        public static int SupportSelfPreserveScore => AITuningConfig.Instance.自保基准扣分;

        /// <summary>支援奶程内站位基础分（F-2 支援站位评分器，docs/archive/34 §4）：落点距伤员
        /// ≤ 治疗半径=「能奶到」满档；量级对齐攻击候选均值（水球伤害 20+获能 12）——奶程内站位
        /// 优先于平打，攻击候选在站位达成后自然由 healValue 估值接手</summary>
        public static int SupportHealReachScore => AITuningConfig.Instance.奶程内站位分;

        /// <summary>支援奶程外每格衰减（F-2）：超出治疗半径每远 1 格 -6（约 3 格外归 0）——
        /// 「走近伤员」的连续梯度，非硬门槛</summary>
        public static int SupportHealReachDecayPerCell => AITuningConfig.Instance.奶程外每格衰减;

        /// <summary>救命分（F-3 预治疗档，docs/archive/34 §4——上提共享单源：配额脑号令原独立常量
        /// EnsoRescueScore 上提后两脑同口径防漂移）：治疗行动覆盖「威胁图 DoomedAllies」（敌方预测
        /// 合计承伤 ≥ 当前 hp=将被集火致死）的我方单位 → 治疗估值 +30——斩杀档（80）的对偶、
        /// 量级低于斩杀（保命优先于平奶/平打，不优先于斩杀线候选）</summary>
        public static int RescueScore => AITuningConfig.Instance.救命分;

        /// <summary>中性档案（无配置/魔神档兜底：全 1 权重=评分制 v2 原口径）</summary>
        private static readonly UnitConfig.CompanionProfile NeutralProfile = new UnitConfig.CompanionProfile();

        /// <summary>移动锚延续槽（E-3 意图延续槽，docs/archive/33 §4）：unitId → 上回合移动锚 unitId。
        /// static 生命周期=进程——DecideAll 里 turn≤1 自清（每次 StartBattle 恒从回合 1 起=新战斗
        /// 自动清零，TurnFlow 零接线）；记录=评分期锚循环现场（RememberMoveAnchor 只记敌方锚）；
        /// 消费=ScoreMoveCandidate 锚序锁定插队（CR 式 target lock，治多锚贪心抖动）</summary>
        private static readonly Dictionary<string, string> _lastMoveAnchors = new Dictionary<string, string>();

        /// <summary>追随外推槽（H-4，docs/archive/36 §7——2026-10-04 拍板「不在先锋 1 格内就立刻跟上，
        /// 治同速追逐恒滞后」）：跟随者 unitId → (先锋 unitId, 上回合先锋位置, 回合号)。static 生命
        /// 周期=进程——DecideAll 里 turn≤1 自清（与 _lastMoveAnchors 同款零接线）；消费=伴随分支
        /// 按先锋上回合位移向量外推本回合追逐目标=当前位置+位移（迎头追逐——同速追逐〔双方皆
        /// 2 格/回合〕物理上永不收敛，先锋驻位她才贴上=「凯亚走了四格远才跟」的根因）；先锋换人
        /// （死亡换先锋）=记录失配按真实位置追逐（位移向量不可信）</summary>
        private static readonly Dictionary<string, (string vanguardId, BattleCell pos, int turn)> _vanguardTrack =
            new Dictionary<string, (string, BattleCell, int)>();

        /// <summary>申报段标志（I-A 两遍法，docs/archive/37 §2.2）：DecideAll 申报遍置 true——评分器
        /// 内全部副作用（槽写入/板声明）统一抑制（「没承诺不记槽」：未获名额伙伴零槽脏写）；执行遍
        /// 复位 false。单线程同步决策链=static 安全；try/finally 保证复位</summary>
        private static bool _bidPhase;

        /// <summary>静态槽跨局自清判据（sim 实例号——2026-10-25 复审批修复，OpponentProfiler.EnsureFresh 同款）</summary>
        private static int _lastSimInstance = -1;

        // ==================== 决策主入口（伙伴决策阶段） ====================

        /// <summary>
        /// 全部伙伴单位的本回合自主行动（存活的；不可动/已被号令/无好行动=缺席）。
        /// playerActions=各玩家已提交选择（Host 收齐后传入——号令跳过+体力预留的感知源）。
        /// </summary>
        public static List<ActionData> DecideAll(BattleSimState sim, int turnNumber, List<ActionData> playerActions)
        {
            var actions = new List<ActionData>();
            var snapshot = sim.TakeSnapshot(turnNumber);
            // 新战斗自清（2026-10-25 复审批修复：改 sim 实例号判据——旧 turn<=1 对秒弃调试局/回放/
            // 中途入局失效；docs/18 2026-10-15 四定案②：新静态 AI 注册表一律 InstanceId）
            if (sim.InstanceId != _lastSimInstance)
            {
                _lastMoveAnchors.Clear();
                _vanguardTrack.Clear();
                _lastSimInstance = sim.InstanceId;
            }

            // 玩家选择足迹：号令占用集 + 各玩家体力预留（己方已提交单位级行动的层级消耗）
            var commanded = new HashSet<string>();
            var staminaReserve = new Dictionary<string, int>();
            if (playerActions != null)
            {
                foreach (var action in playerActions)
                {
                    if (action == null || string.IsNullOrEmpty(action.unitId)) continue;
                    commanded.Add(action.unitId); // 任何玩家上交的单位级行动=该单位本回合已被指令占用
                    var actor = sim.GetUnit(action.unitId);
                    if (actor == null) continue;
                    var identity = actor.GetUnitComponent<UnitIdentity>();
                    if (identity == null) continue;
                    int cost = ActionStaminaCost(sim, action, actor);
                    if (cost > 0)
                        staminaReserve[identity.OwnerPlayerID] =
                            (staminaReserve.TryGetValue(identity.OwnerPlayerID, out var r) ? r : 0) + cost;
                }
            }

            // 已定伙伴消耗：I-A 起分离两计数器——packed=装包遍预算承诺、committed=执行遍定稿
            // 累计（名额承诺≠已定稿：执行遍 pool 只见前位已定稿消耗，防被装包总额误扣；
            // packed≥committed 恒成立，执行遍不会超支——资源门槛内建真实递减池检查）
            var committed = new Dictionary<string, int>();
            var packed = new Dictionary<string, int>();

            // E-2 本方意图板（决策序内登记-消费，docs/archive/33 §3）：前位已定行动入板，后位评分
            // 消费（伤害溢出去重/治疗去重/集火跟随）——与 committed 虚拟池同构；配额脑无此机制
            //（每回合仅 1 配额行动，无跨单位声明域）
            var board = new TeamIntentBoard();

            // I-B 全玩家行动入板（docs/archive/37 §3，2026-10-04 拍板「玩家做出的选择伙伴脑需要
            // 考虑进去」——玩家选择已定稿=非隐私，阶段三本就感知）：Skill（魔神战技/爆发、伙伴
            // 号令）经 DeclareIntent 同口径声明攻击/治疗面——伙伴感知「魔神打了谁」自动补刀/
            // 转火、集火权重对魔神目标生效；Move/DeployUnit 落点入移动意图占位（I-D）——伙伴
            // 不撞魔神换位与新出战单位落格。三表全按目标单位 id 键控、id 空间按队分割=跨队
            // 天然零命中（E-2 单板双队共用同机制实证），无需分板
            if (playerActions != null)
            {
                foreach (var pa in playerActions)
                {
                    if (pa == null) continue;
                    if (pa.actionType == ActionType.Skill)
                    {
                        var actor = sim.GetUnit(pa.unitId);
                        if (actor == null) continue;
                        var actorIdentity = actor.GetUnitComponent<UnitIdentity>();
                        if (actorIdentity == null) continue;
                        DeclareIntent(sim, snapshot, actor, actorIdentity.Team, pa, board);
                    }
                    else if (pa.actionType == ActionType.Move)
                    {
                        var actor = sim.GetUnit(pa.unitId);
                        if (actor == null) continue;
                        board.DeclareMovement(pa.unitId, DeclaredLandingCell(sim, pa, actor)); // P5：targetX 直读/旧格式桥
                    }
                    else if (pa.actionType == ActionType.DeployUnit)
                    {
                        if (sim.Map != null && sim.Map.HasTile(pa.deployCell.x, pa.deployCell.y))
                            board.DeclareMovement("deploy:" + pa.playerId, pa.deployCell); // 部署单位不在场：合成 key 不撞任何在场 id，全员避让
                    }
                }
            }

            // 对手档案采集（AI-2 批，docs/active/42 §4/§11）：各玩家已提交配额行动 → 攻击性占比
            // 样本（已交选择=非隐私，信息红线①合规；消费侧随 AI-3 外交层激活）
            OpponentProfiler.ObserveTurnActions(sim, playerActions, turnNumber);

            // 威胁图接伙伴脑（AI-2 批消费点扩展，docs/active/42 §11）：**全伙伴**（输出型+支援型）
            // 走位避险接线——移动落点避火线 −12（ThreatPenaltyAt 既有原语）+攻击反威胁分
            // （ThreatResponseBonus——拆威胁源优先）。**每在场队伍一张图**（Build 按队伍视角——
            // 替代 F-1「首支援型单图全队共享」的跨队视角缺陷）。
            // AI-4 难度门（§9.2）：走位避险=棋圣档开启件——仅当该队存在 ThreatFeed 开启的归属玩家
            // 才建图（学徒/劲敌 AI 队伍零成本跳过；真人玩家不注册=门恒全开=全件质量）；
            // E-1 拍板 #4「威胁图暂只接配额脑」由 AI 批设计蓝图全面取代（F-1 支援型先行=前身）。
            var threatByTeam = new Dictionary<TeamType, OpponentThreatModel.ThreatMap>();
            // 单遍聚合「该队存在 ThreatFeed 开启的伙伴」（2026-11-06 后期卡顿修复）：旧实现对每个
            // 队伍都重扫一遍全场（O(队×单位²)，且每次 SortedUnits 重建+排序）；语义完全一致
            var feedTeams = new HashSet<TeamType>();
            foreach (var kv0 in sim.SortedUnits())
            {
                var u0 = kv0.Value;
                if (!BattleHeuristics.IsCompanion(u0) || BattleSimState.IsDead(u0)) continue;
                var id0 = u0.GetUnitComponent<UnitIdentity>();
                if (id0 == null || !AIPlayerConfig.GatesFor(id0.OwnerPlayerID).ThreatFeed) continue;
                feedTeams.Add(id0.Team);
            }
            foreach (var kv0 in sim.SortedUnits())
            {
                var u0 = kv0.Value;
                if (!BattleHeuristics.IsCompanion(u0) || BattleSimState.IsDead(u0)) continue;
                var id0 = u0.GetUnitComponent<UnitIdentity>();
                if (id0 == null || threatByTeam.ContainsKey(id0.Team) || !feedTeams.Contains(id0.Team)) continue;
                threatByTeam[id0.Team] = OpponentThreatModel.BuildCached(sim, snapshot, id0.Team, turnNumber); // #2 审查批：回合级缓存
            }

            // I-A 申报遍（docs/archive/37 §2.1，2026-10-04 拍板「伙伴们会讨论分配」）：全员裸分
            //（「假如只有我」的独立视角——排序公平；满池=实池−玩家预留，不扣同队申报）产出
            // (bestScore, bestAction, cost) 申报表。_bidPhase=true 抑制全部副作用（槽写入/板
            // 声明——「没承诺不记槽」，未获名额伙伴零脏写）。
            // I 返修 2（2026-10-04 实测「凯亚走了 3 格芭芭拉没立刻跟上」）：申报遍拆两轮——
            // **输出型先申报**（纯独立视角 board=null），**支援型后申报且消费「申报落点板」**
            //（bidBoard=输出型申报中的移动落点预广播——跟随者的追逐锚当回合即读到先锋申报
            // 落点，治 T1 申报死角：快照视角下她与凯亚同在出生区=「已贴伴」驻位无候选→无
            // 名额→结算后凯亚冲出 3 格她原地不动。支援型=天然响应者，独立视角原则对跟随者
            // 修正为「消费先锋申报」）。执行序同构修复见 grants 重排（先锋先正式声明）
            var bids = new List<CompanionBid>();
            _bidPhase = true;
            try
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    bool supportPass = pass == 1;
                    var bidBoard = supportPass ? new TeamIntentBoard() : null;
                    if (supportPass)
                    {
                        // 输出型申报中的移动落点预广播（申报表=意图的第一次广播——讨论的起点）
                        foreach (var b in bids)
                        {
                            if (b.bid.actionType != ActionType.Move) continue;
                            bidBoard.DeclareMovement(b.unitId, DeclaredLandingCell(sim, b.bid, b.unit)); // P5：targetX 直读
                        }
                    }
                    foreach (var kv in sim.SortedUnits())
                    {
                        var unit = kv.Value;
                        if (BattleHeuristics.IsBuilding(unit)) continue; // 建筑不参与任何行动（协议核心批单一判据）
                        if (!BattleHeuristics.IsCompanion(unit)) continue;
                        if (BattleSimState.IsDead(unit) || !BattleSimState.CanAct(unit)) continue;
                        var identity = unit.GetUnitComponent<UnitIdentity>();
                        if (identity == null) continue;
                        if (commanded.Contains(kv.Key)) continue; // 已被号令：行动=号令技能，不占自主名额
                        var profile = unit.RawData?.行为档案;
                        bool isSupport = profile != null
                            && profile.候选类别 == UnitConfig.CompanionRole.Support;
                        if (isSupport != supportPass) continue; // 两轮分拣：输出型→支援型

                        int? pool = staminaReserve.TryGetValue(identity.OwnerPlayerID, out var r2) && r2 > 0
                            ? Math.Max(0, sim.GetStamina(identity.OwnerPlayerID) - r2)
                            : (int?)null;
                        var tracker = new CandidateTracker();
                        // AI-4 难度门（§9.2）：按归属玩家——学徒=关威胁图/意图板/反应预期分；
                        // 真人不注册=门恒全开（其自主军团全件质量），棋圣=AI-2 全件基线。
                        // AI-5 军团软系数：按归属玩家的军团倾向（回防=斩杀加成下调）
                        var gates = AIPlayerConfig.GatesFor(identity.OwnerPlayerID);
                        var unitThreat = gates.ThreatFeed && threatByTeam.TryGetValue(identity.Team, out var tMap)
                            ? tMap
                            : null;
                        ScoreUnitCandidates(sim, snapshot, unit, identity.OwnerPlayerID, identity.Team,
                            turnNumber, tracker, pool, profile, unitThreat,
                            gates.IntentBoard ? bidBoard : null, false, gates.ReactionPreview,
                            AIStrategist.LegionAggressionOf(identity.OwnerPlayerID));
                        if (tracker.Best == null) continue; // 无候选=不占名额
                        bids.Add(new CompanionBid
                        {
                            unit = unit,
                            unitId = kv.Key,
                            owner = identity.OwnerPlayerID,
                            team = identity.Team,
                            score = tracker.BestScore,
                            bid = tracker.Best,
                            cost = ActionStaminaCost(sim, tracker.Best, unit),
                            isSupport = isSupport,
                        });
                    }
                }
            }
            finally
            {
                _bidPhase = false; // 执行遍副作用恢复
            }

            // 议题⑤申报附着广播（凌晨批）：全部 bid 的 OnHit 元素附着意图预声明进意图板——
            // 执行序支援型殿后（I 返修 3），芭芭拉（Hydro）的挂水意图若只在执行遍声明，先决策的
            // 安柏永远看不到=跨单位元素连招（挂水→蒸发/冻结）在决策层不可预判。申报遍全员广播=
            // WEGO 版 WorldState 顺序模拟（Gears Tactics 映射）：「讨论时各自亮牌」，先位/后位
            // 同回合都能预判连招。声明=估值口径（未获名额者可能落空），消费端只用于反应预期分
            //（软信号 ±半伤），下回合重决策自然吸收
            foreach (var b in bids)
                DeclareElementIntent(sim, snapshot, b.unit, b.unitId, b.bid, board, false);

            // I-A 装包遍：按队伍独立装包（池 per playerId）。cost=0=白给（不占预算直接获名额，
            // unitId 升序）；cost>0 按单位体力价值（score/cost 交叉相乘降序——cost 恒 5 时退化为
            // 分数降序；long 防溢出）+unitId 决胜逐个扣预算，装不下的跳过（低价者仍可能装得下）
            var grants = new List<CompanionBid>();
            foreach (var teamGroup in bids.GroupBy(b => b.team).OrderBy(g => (int)g.Key)) // Team 枚举升序=确定性
            {
                foreach (var b in teamGroup.Where(b => b.cost <= 0).OrderBy(b => b.unitId, StringComparer.Ordinal))
                    grants.Add(b);
                foreach (var b in teamGroup.Where(b => b.cost > 0).OrderBy(b => b, BidValueComparer.Instance))
                {
                    int left = Math.Max(0, sim.GetStamina(b.owner)
                        - (staminaReserve.TryGetValue(b.owner, out var rv) ? rv : 0)
                        - (packed.TryGetValue(b.owner, out var pv) ? pv : 0));
                    if (b.cost > left) continue;
                    packed[b.owner] = (packed.TryGetValue(b.owner, out var pv2) ? pv2 : 0) + b.cost;
                    grants.Add(b);
                }
            }

            // I 返修 3（docs/archive/37 §2.1 追记）：执行序=同队内支援型**稳定殿后**——先锋/输出型
            // 先正式声明移动落点，跟随者后重评分消费正式声明（「凯亚宣告、芭芭拉响应」的顺序
            // 保证：追逐锚读到的声明落点=先锋本回合真实决策位）。OrderBy 稳定排序保持装包
            // 价值序；无支援型队伍=恒等重排零变化；支援型后决策同时获得更多板信息（输出型
            // 集火/伤害声明）=决策质量只增不减
            grants = grants.GroupBy(g => g.team).OrderBy(g => (int)g.Key)
                .SelectMany(g => g.OrderBy(b => b.isSupport ? 1 : 0))
                .ToList();

            // I-A 执行遍：获名额者按（Team 升序→队内装包序）带正式板重评分定稿——名额分配依据
            //（裸分）与执行依据（板后协调分）分离：E-2 去重/集火/预治疗语义全保留；重评与申报
            // 不同=正常重定向（前位声明改变后位最优），null=名额浪费（罕见，下回合自愈）
            foreach (var g in grants)
            {
                var identity = g.unit.GetUnitComponent<UnitIdentity>();
                if (identity == null) continue;
                int gReserved = (staminaReserve.TryGetValue(g.owner, out var r3) ? r3 : 0)
                              + (committed.TryGetValue(g.owner, out var c2) ? c2 : 0);
                int? pool = gReserved > 0
                    ? Math.Max(0, sim.GetStamina(g.owner) - gReserved)
                    : (int?)null;
                var tracker = new CandidateTracker();
                var profile = g.unit.RawData?.行为档案;
                // AI-4 难度门（执行遍同申报遍口径）：按归属玩家门控威胁图/意图板/反应预期分。
                // AI-5 军团软系数：按归属玩家（回防=斩杀加成下调）
                var gates2 = AIPlayerConfig.GatesFor(g.owner);
                var unitThreat = gates2.ThreatFeed && threatByTeam.TryGetValue(g.team, out var tMap2)
                    ? tMap2
                    : null;
                ScoreUnitCandidates(sim, snapshot, g.unit, g.owner, g.team, turnNumber,
                    tracker, pool, profile, unitThreat, gates2.IntentBoard ? board : null,
                    false, gates2.ReactionPreview, AIStrategist.LegionAggressionOf(g.owner));
                if (tracker.Best == null) continue;
                DeclareIntent(sim, snapshot, g.unit, g.team, tracker.Best, board); // E-2：意图入板
                actions.Add(tracker.Best);
                // I-D 移动意图占位：移动定稿落点入板（乐观口径——执行层停格偏差保守向，下回合吸收）
                if (tracker.Best.actionType == ActionType.Move)
                    board.DeclareMovement(g.unitId, DeclaredLandingCell(sim, tracker.Best, g.unit)); // P5：targetX 直读
                int cost = ActionStaminaCost(sim, tracker.Best, g.unit);
                if (cost > 0)
                    committed[g.owner] = (committed.TryGetValue(g.owner, out var c3) ? c3 : 0) + cost;
            }
            return actions;
        }

        /// <summary>I-A 申报条目（docs/archive/37 §2）：申报遍产物——裸分最优候选+消耗，装包遍消费</summary>
        private sealed class CompanionBid
        {
            public Unit unit;
            public string unitId;
            public string owner;
            public TeamType team;
            public int score;
            public ActionData bid;
            public int cost;
            public bool isSupport; // I 返修：执行序支援型殿后（先锋先正式声明、跟随者后消费）
        }

        /// <summary>I-A 装包价值比较器（docs/archive/37 §2.1）：单位体力价值 score/cost 降序
        /// （交叉相乘 long 防溢出——cost 恒 5 现实下退化为分数降序）+unitId 升序决胜=确定性全序</summary>
        private sealed class BidValueComparer : IComparer<CompanionBid>
        {
            public static readonly BidValueComparer Instance = new BidValueComparer();
            public int Compare(CompanionBid x, CompanionBid y)
            {
                long vx = (long)x.score * y.cost;
                long vy = (long)y.score * x.cost;
                if (vx != vy) return vy.CompareTo(vx); // 价值降序
                return string.CompareOrdinal(x.unitId, y.unitId);
            }
        }

        /// <summary>行动意图入板（E-2，docs/archive/33 §3）：对已定行动按决策快照反推声明面——
        /// ①方向攻击技能：PreviewLineTargets 逐命中目标声明估伤（溢出去重消费）+入集火集；
        /// ②治疗原子：逐受治者声明 per-ally 有效治疗（治疗去重消费，EnumerateSkillHeals 单源——
        /// 声明端 board=null 全量口径）；③移动/Pass/自增益/单位指向增益：零伤害声明。
        /// 声明口径=估值（与评分同源）——结算偏差（防御/反应/被挡落空）由下回合重决策自然吸收</summary>
        private static void DeclareIntent(BattleSimState sim, BattleSnapshot snapshot, Unit unit,
            TeamType team, ActionData action, TeamIntentBoard board)
        {
            if (action == null || action.actionType != ActionType.Skill) return; // Move/Pass 零声明
            var skill = action.skillIndex >= 0 && action.skillIndex < unit.Skills.Count
                ? unit.Skills[action.skillIndex]
                : null;
            var data = skill?.RawData;
            if (data == null) return;

            if (!data.IsUnitTargeted() && !data.IsSelfCast())
            {
                int perTarget = BattleHeuristics.EstimatePerTargetDamage(unit, data);
                if (perTarget > 0)
                {
                    var aim = SkillHitResolver.AimVectorOf(sim, action, snapshot); // P5：行动瞄准向量（aimX 直读/方向桥）
                    var targets = BattleHeuristics.PredictAimTargets(sim, snapshot, team, unit, data, aim.x, aim.y);
                    foreach (var target in targets)
                        board.DeclareDamage(target.unitId, MitigatedDamage(perTarget, target.defense)); // E-3：折减口径与评分同源（声明不过称高防敌足杀）
                }
            }

            foreach (var heal in EnumerateSkillHeals(sim, unit, team, data, null))
                board.DeclareHeal(heal.allyId, heal.effective);

            // 议题⑤：已承诺附着落地声明（replacePrior=覆盖申报期旧声明——执行遍重定向时旧目标集
            // 作废=最新意图权威；后位执行序消费）
            DeclareElementIntent(sim, snapshot, unit, UnitIdOf(sim, unit), action, board, true);
        }

        /// <summary>行动的元素附着意图声明（凌晨批议题⑤）：方向攻击技能按行动方向预览命中集，
        /// 逐命中目标声明 OnHit AttachElement 原子的元素与层数（element=Physical=施法者自身元素
        /// ——SkillElementOf 同源回落，与 EffectCompiler 编译口径一致）。单位指向/自施放/非 Skill
        /// 行动零声明（与 DeclareIntent 伤害声明同域）；board=null（配额脑/学徒档）零变化</summary>
        private static void DeclareElementIntent(BattleSimState sim, BattleSnapshot snapshot, Unit unit,
            string unitId, ActionData action, TeamIntentBoard board, bool replacePriorDeclarations)
        {
            if (board == null || action == null || action.actionType != ActionType.Skill) return;
            var skill = action.skillIndex >= 0 && action.skillIndex < unit.Skills.Count
                ? unit.Skills[action.skillIndex]
                : null;
            var data = skill?.RawData;
            if (data == null || data.effects == null || data.IsUnitTargeted() || data.IsSelfCast()) return;
            bool anyAttach = false;
            foreach (var atom in data.effects)
                if (atom != null && atom.trigger == SkillEffectTrigger.OnHit
                    && atom.kind == SkillEffectKind.AttachElement && atom.elementLayers > 0)
                { anyAttach = true; break; }
            if (!anyAttach) return;
            var identity = unit.GetUnitComponent<UnitIdentity>();
            if (identity == null) return;
            var aim = SkillHitResolver.AimVectorOf(sim, action, snapshot); // P5：行动瞄准向量（aimX 直读/方向桥）
            var targets = BattleHeuristics.PredictAimTargets(sim, snapshot, identity.Team, unit, data, aim.x, aim.y);
            if (targets.Count == 0) return;
            foreach (var atom in data.effects)
            {
                if (atom == null || atom.trigger != SkillEffectTrigger.OnHit
                    || atom.kind != SkillEffectKind.AttachElement || atom.elementLayers <= 0) continue;
                var element = atom.element != ElementType.Physical
                    ? atom.element
                    : SkillHitResolver.SkillElementOf(unit, data); // Physical=施法者自身元素（编译同源回落）
                foreach (var target in targets)
                    board.DeclareElement(unitId, target.unitId, (int)element, atom.elementLayers,
                        replacePriorDeclarations);
            }
        }

        // ==================== 共享评分骨架（伙伴脑+配额脑操魔神档复用） ====================

        /// <summary>
        /// 单位级行动候选评分（评分制 v2 骨架收口）：①攻击技能（战技/爆发）——CanCast+costs 门槛
        /// （体力按层级换算；决策期虚拟池透传）、十字四向逐向预判（与 HUD 瞄准推荐/结算形态同源）、
        /// 逐目标有效伤害（过杀截断）+斩杀加成+战技命中获能；②移动——十字逼近锚点（输出型=最近敌，
        /// 支援型=最缺血我方）、步数逼近到偏好交战距离止步（非无脑贴脸）、走位进射击线加分。
        /// 每维度乘 UnitConfig.行为档案权重（profile null=中性=评分制 v2 原口径）。
        /// 支援型骨架多一个候选类别：技能携带治疗原子时估有效治疗并入评分（估「奶谁」而非只「打谁」）。
        /// E-1 对手建模（docs/archive/33 §2.3）：threat=威胁图（配额脑操魔神档传——攻击候选吃反威胁分、
        /// 移动候选落点吃避险扣分；伙伴脑不传=null 零行为变化——拍板 #4 威胁图暂只接配额脑）。
        /// isPlayerDirectControl（AI-1 批，2026-10-12 拍板「AI 玩家操控严格对齐真人」）：玩家配额直接
        /// 操控的候选门槛镜像——true 时体力减免 5 与真人 HUD 同口径（StaminaAmountOf 层级门=仅
        /// Companion 生效，魔神档传 true 计价不变）；伙伴脑自主路径=false 维持不减免。
        /// reactionPreview（AI-4 批难度门，docs/active/42 §9.2）：E-3 反应预期分（增伤反应预判）——
        /// 学徒档关闭件；默认 true=全件。
        /// legionAggression（AI-5 批军团软系数，docs/active/42 §3 协作机制 2）：回防态=
        /// AIStrategist.RegroupAggressionMul（0.7——斩杀加成下调=「激进度下调」的伙伴侧软注入）；
        /// 推进/默认=1；权重注入不直接指定行动。
        /// </summary>
        public static void ScoreUnitCandidates(BattleSimState sim, BattleSnapshot snapshot, Unit unit,
            string playerId, TeamType team, int turn, CandidateTracker tracker,
            int? staminaPoolOverride, UnitConfig.CompanionProfile profile,
            OpponentThreatModel.ThreatMap threat = null, TeamIntentBoard board = null,
            bool isPlayerDirectControl = false, bool reactionPreview = true, float legionAggression = 1f)
        {
            if (unit == null || tracker == null) return;
            profile ??= NeutralProfile;
            // 采集档（docs/47 拍板⑦，2026-10-27）：采集型角色（IsHarvester——安柏双判据都满足）
            // 有可见资源点时先评采集候选——基准分稳压攻击档（「相比攻击敌人，更主动采集资源点」），
            // 距离衰减保证远资源点不越俎代庖。**保命档（Rescue/复苏）天然在采集之上**——采集候选
            // 只是 Offer 进共享 tracker 与救命/斩杀候选公平竞争，采集基准分只压「攻击典型分」。
            // **站上存活资源点=移动候选整体让位**（2026-10-28 返修；**仅纯 tag 型采集者**——无途经
            // 配额站桩即其采集上限；途经采集型〔安柏〕同日追拍「站住采集的收益远不如用移动来回走」
            // 改收获档持续移动采集，不让位——采集 Offer 分值自稳压移动档）
            bool hasActionCandidate = ScoreAttackCandidates(sim, snapshot, unit, playerId, team, turn,
                tracker, staminaPoolOverride, profile, threat, board, isPlayerDirectControl, reactionPreview,
                legionAggression);
            bool standingOnLiveNode = ScoreCollectCandidate(sim, unit, playerId, team, turn, tracker);
            if (!standingOnLiveNode)
                ScoreMoveCandidate(sim, snapshot, unit, playerId, team, turn, tracker, staminaPoolOverride,
                    profile, hasActionCandidate, threat, board, isPlayerDirectControl);
        }

        /// <summary>该格存活资源点的单步可采次数=min(移动配额, 堆余量)（无堆=0）——收获档路径
        /// 价值估算，与 Host 途经采集同口径（per-堆配额）</summary>
        private static int LivePileCollectValue(BattleSimState sim, BattleCell cell, int quota)
        {
            foreach (var node in sim.SortedResourceNodes())
                if (node != null && node.remainingCharges > 0
                    && node.cell.x == cell.x && node.cell.y == cell.y)
                    return Mathf.Min(quota, node.remainingCharges);
            return 0;
        }

        /// <summary>该格是否存活资源点（收获档落点 +1=回合末站立采集估值）</summary>
        private static bool LivePileAt(BattleSimState sim, BattleCell cell)
        {
            foreach (var node in sim.SortedResourceNodes())
                if (node != null && node.remainingCharges > 0
                    && node.cell.x == cell.x && node.cell.y == cell.y)
                    return true;
            return false;
        }

        /// <summary>①攻击技能候选（战技/爆发）：预判与结算形态同源（WouldHitEnemyInDirection）；评分=逐目标
        /// 有效伤害+斩杀×激进度+战技获能；乘技能类型优先权重；支援型并入治疗原子估值。
        /// 返回=本单位是否存在任何攻击/增益候选（Offer 调用即算——共享 tracker 下被更优候选
        /// 压制不等于无候选；挂机修复的驻位判定消费此值）</summary>
        private static bool ScoreAttackCandidates(BattleSimState sim, BattleSnapshot snapshot, Unit unit,
            string playerId, TeamType team, int turn, CandidateTracker tracker,
            int? staminaPoolOverride, UnitConfig.CompanionProfile profile,
            OpponentThreatModel.ThreatMap threat, TeamIntentBoard board,
            bool isPlayerDirectControl, bool reactionPreview, float legionAggression)
        {
            var skills = unit.Skills;
            var fromCm = sim.GetPosCm(unit); // P5：连续位（任意角瞄准向量源）
            var selfBoardId = UnitIdOf(sim, unit); // 议题⑤：声明附着消费的自身排除键
            bool offeredAny = false; // 本单位任一技能产生过候选（Offer 调用即算——驻位判定消费）

            for (int i = 0; i < skills.Count; i++)
            {
                var skill = skills[i];
                var data = skill?.RawData;
                if (data == null) continue;
                // F-3 技能维度集（docs/archive/34 §4）：门槛扩 Henka（变奏技能接自主评估域——
                // 2026-10-03 拍板「Henka 治疗类接自主域」；Enso/Contract=玩家域仍剥离不含）
                if (data.skillType != SkillType.Normal && data.skillType != SkillType.Burst
                    && data.skillType != SkillType.Henka) continue;
                if (!skill.CanCast(unit)) continue; // 占位技能跳过——凛冽轮舞/闪耀奇迹未实装不可施放

                // 攻击冷却门槛（决策九十五 P3，蓝图 §6「候选门槛加 CD 检查」）：战技/爆发 CD 中
                //（Normal 3 秒/Burst 5 秒）不进候选域——防每 0.5 秒节拍空放刷决策（上交层 Host gate
                // 也会拦，此处=AI 侧不浪费决策预算；Henka 无 CD 恒过）
                if (sim.IsSkillOnCooldown(selfBoardId, data.skillType)) continue;

                // AI 施放节流（P5-D）：无 CD 技能（变奏/治疗族）按 AITuning「AI 重施最短间隔 tick」
                // 节流——0.5s 节拍下治疗每拍施放会把伤害完全抵消（死水僵局观察局实锤）；CD 攻击族不受影响
                if (BattleHeuristics.AiCastThrottled(sim, selfBoardId, i)) continue;

                // 消耗门槛（统一消耗模型 C-2）：costs 全条目镜像 Host 判定（体力按施法者层级换算；
                // 决策期虚拟池透传——伙伴决策不超额承诺；isPlayerDirectControl=配额直接操控减免口径）
                if (!ResourceGate.HasAll(sim, unit, playerId, data.costs, out _, staminaPoolOverride,
                        isPlayerDirectControl)) continue;

                // 单位指向型爆发（B-3 ② 首个=芭芭拉闪耀奇迹）：无方向域——目标估值档（复苏优先/增益次之）
                if (data.IsUnitTargeted())
                {
                    offeredAny |= ScoreUnitTargetBurst(sim, snapshot, unit, playerId, team, turn, i, data, tracker, profile);
                    continue;
                }

                // 无目标自施放爆发（aimMode=None——凛冽轮舞）：无方向域/无目标域——自身增益估值档
                if (data.IsSelfCast())
                {
                    offeredAny |= ScoreSelfCastBurst(sim, unit, playerId, turn, i, data, tracker, profile);
                    continue;
                }

                int perTarget = BattleHeuristics.EstimatePerTargetDamage(unit, data);
                int healValue = profile.候选类别 == UnitConfig.CompanionRole.Support
                    ? EstimateSkillHealValue(sim, unit, team, data, board, threat)
                    : 0;
                if (perTarget <= 0 && healValue <= 0) continue;

                // F-3 无伤害纯治疗技能（Henka 治疗族/未来治疗型 Normal——无方向域，敌方十字预判对
                // 无伤害技能恒无线）：方向 Up 占位直接 Offer（UnitTarget/SelfCast 同款「无方向域」
                // 处理族）；评分=治疗估值（EstimateSkillHealValue 已含 E-2 治疗去重+救命分预治疗档），
                // 乘战技优先权重（与战技候选同域竞争口径）
                if (perTarget <= 0)
                {
                    tracker.Offer(Mathf.RoundToInt(healValue * profile.战技优先权重),
                        Skill(playerId, unit, i, Direction2D.Up, turn));
                    offeredAny = true;
                    continue; // 下一技能（勿进十字向循环——无方向域）
                }

                // E-3 技能元素（反应预期分用——SkillHitResolver.SkillElementOf 单源：OnHit 首个
                // Damage 原子优先、Physical=施法者元素回落，与伤害编译同口径）
                var skillElement = SkillHitResolver.SkillElementOf(unit, data);

                // P5 任意角瞄准：可见敌距离序逐敌试瞄（aim=自身→敌向量千分；镜像旧十字向枚举的
                // 「首个可命中向」语义，角度域连续）——每锚敌一候选；命中集=PredictAimTargets
                //（存活+雾限内建）。确定性=距离序（FindEnemiesByDistance），同分先到先得
                foreach (var anchorEnemy in BattleHeuristics.FindEnemiesByDistance(sim, unit))
                {
                    int aimDist = BattleHeuristics.DistanceCm(sim, unit, anchorEnemy);
                    int avx, avz;
                    if (aimDist <= 0) { avx = 1000; avz = 0; } // 同点堆叠：东兜底（与 AimVectorOf 同向）
                    else
                    {
                        var anchorPos = sim.GetPosCm(anchorEnemy);
                        avx = (anchorPos.x - fromCm.x) * 1000 / aimDist;
                        avz = (anchorPos.y - fromCm.y) * 1000 / aimDist;
                    }
                    var targets = BattleHeuristics.PredictAimTargets(sim, snapshot, team, unit, data, avx, avz);
                    if (targets.Count == 0 && healValue <= 0) continue; // 纯尸体线：不浪费行动

                    int score = 0;
                    foreach (var target in targets)
                    {
                        // E-2 伤害溢出去重（docs/archive/33 §3）：前位已声明伤害覆盖后的剩余有效血量——
                        // remaining ≤ 0 =前位足杀，本发边际价值 0（评分机制自然转向次优目标，勿显式
                        // 降档）；斩杀判定同步剩余口径（击杀已被前位拿走不重复计）；board=null
                        //（配额脑路径）时 remaining=target.hp 与原逻辑恒等
                        int remaining = target.hp - (board?.DeclaredDamage(target.unitId) ?? 0);
                        if (remaining > 0)
                        {
                            // E-3 防御折减（估值感知补强，docs/archive/33 §4）：DamagePipeline 易伤乘区
                            // 轻量镜像——修「对高防目标伤害虚高」盲区（骨架级修正：伙伴脑与配额脑操
                            // 魔神档共享受益；眷属脑不消费估值零影响）
                            int effPerTarget = MitigatedDamage(perTarget, target.defense);
                            score += Math.Min(effPerTarget, remaining); // 过杀截断（E-2 剩余口径+E-3 折减口径）
                            if (effPerTarget >= remaining)
                                // AI-5 军团软系数：回防态斩杀加成 ×0.7（激进度下调——§3 协作机制 2）
                                score += Mathf.RoundToInt(KillBonusScore * profile.激进度 * legionAggression);
                            // E-3 反应预期分：快照附着×技能元素经 ElementReactionResolver.Preview 单源
                            // 预判——增伤反应（蒸发/融化 级别×50% 乘区）按折减伤害一半计；冻结
                            // 控制反应无增伤乘区不计分。跨单位连招基础：凯亚挂冰→安柏跟火自动蒸发
                            // **冻结层兜底（决策七十四拍板「AI 一并接入」）**：附着为空且目标冻结时
                            // 冻结 Buff=第二冰源——火技能预判到融化（每回合=1层、同片逐发消耗）：
                            // 芭芭拉挂水→凯亚冻结→安柏跟火=融化收益可估（超导减防属间接收益不接，
                            // J-1 冻结协同分既有）
                            var reaction = ElementReactionResolver.Preview(
                                (ElementType)target.dyedElement, target.dyedLayers, skillElement, 1);
                            // 议题⑤：声明附着消费（凌晨批）——快照附着为空时读意图板「他人已声明的
                            // 附着意图」预判反应（芭芭拉申报挂水→安柏同回合即预判蒸发：比快照预判准，
                            // WEGO 版顺序模拟；board=null=零变化；与下方冻结层兜底同=附着为空才走）
                            if (!reaction.HasReaction && (ElementType)target.dyedElement == ElementType.Physical
                                && board != null)
                            {
                                var declaredEl = board.DeclaredElementExcluding(target.unitId, selfBoardId);
                                if (declaredEl.element > 0)
                                    reaction = ElementReactionResolver.Preview(
                                        (ElementType)declaredEl.element, declaredEl.layers, skillElement, 1);
                            }
                            if (!reaction.HasReaction
                                && (ElementType)target.dyedElement == ElementType.Physical
                                && target.isFrozen == 1)
                            {
                                int freezeTurns = 0;
                                foreach (var b in target.buffs)
                                    if (b.type == (int)BuffType.Freeze) freezeTurns = b.remainingTurns;
                                if (freezeTurns > 0)
                                    reaction = ElementReactionResolver.PreviewFreezeLayer(skillElement, 1, freezeTurns);
                            }
                            if (reactionPreview
                                && reaction.HasReaction
                                && reaction.DamageBonusDelta + reaction.VulnerabilityBonus > 0)
                                score += effPerTarget / 2;
                            // J-1 冻结协同分（2026-10-04 拍板）：冻结控制反应无增伤乘区（旧口径恒 0 分）
                            // ——目标跳过 2 回合行动的 flat 控制值；芭芭拉挂水→霜袭/冰棱 tick 冻结联动主链
                            else if (reaction.HasReaction
                                && reaction.ReactionType == BattleCommand.ReactionKindFreeze)
                                score += FreezeControlScore;
                            // E-2 集火跟随：档案「集火权重」>0 时对已被我方声明攻击的**未死**目标
                            // 加分（remaining>0=未死——死透的目标不构成集火锚）
                            if (board != null && profile.集火权重 > 0 && board.FocusedEnemies.Contains(target.unitId))
                                score += FocusFireScorePerRank * profile.集火权重;
                        }
                        score += ThreatResponseBonus(threat, target.unitId); // E-1 反威胁分（threat=null 恒 0）
                    }
                    if (data.skillType == SkillType.Normal)
                        score += SkillHitEnergyScore; // 战技命中 +10 元能（爆发/延奏命中不获能）
                    score += healValue; // 支援型：治疗原子估值（方向无关的平加——与其它候选同池竞争）
                    score = Mathf.RoundToInt(score * (data.skillType == SkillType.Normal
                        ? profile.战技优先权重
                        : profile.爆发优先权重));

                    tracker.Offer(score, SkillAim(playerId, unit, i, avx, avz, turn));
                    offeredAny = true;
                }
            }
            return offeredAny;
        }

        /// <summary>②b 单位指向型爆发候选（B-3 ② 首个=芭芭拉闪耀奇迹，无方向域）：复苏优先——
        /// 我方尸体存在=高价值复苏（评分=复苏常量，尸体 unitId 升序首个保确定性）；无尸体=增益档
        /// （技能 condition=TargetIsAlive 的 ApplyBuff 原子=待授光环，如歌声之环）给最缺血的**未持有**
        /// 存活我方（等比平局 unitId 升序——FindMostWoundedAlly 同口径）；全员已持有=不占行动
        /// （重施加 Merge 无增益，攒满也不空放）。评分均乘 profile.爆发优先权重（与攻击候选同池）。
        /// 返回=是否产生候选（挂机修复的驻位判定消费——非方向型候选也证明本单位有可行动作）</summary>
        private static bool ScoreUnitTargetBurst(BattleSimState sim, BattleSnapshot snapshot, Unit unit,
            string playerId, TeamType team, int turn, int skillIndex, SkillConfig.SkillData data,
            CandidateTracker tracker, UnitConfig.CompanionProfile profile)
        {
            // 复苏候选：我方尸体（unitId 升序首个——枚举确定性铁律）
            var corpses = new List<UnitState>();
            foreach (var u in snapshot.units)
                if ((TeamType)u.team == team && u.isCorpse != 0) corpses.Add(u);
            if (corpses.Count > 0)
            {
                corpses.Sort((a, b) => string.CompareOrdinal(a.unitId, b.unitId));
                tracker.Offer(Mathf.RoundToInt(UnitTargetBurstReviveScore * profile.爆发优先权重),
                    Skill(playerId, unit, skillIndex, Direction2D.Up, turn, corpses[0].unitId));
                return true; // 有尸体=复苏即本档最优（不再评增益）
            }

            // 增益原子（condition=TargetIsAlive 的 ApplyBuff——歌声之环）。G-3 发放语义修正
            //（2026-10-03 用户勘正：闪耀奇迹=**发放**非转移——施加只给目标挂新实例，施法者持有
            // 不消失）：排除条件从「已持有」改「**已满层**」——3命 C3StackLimit=2 时给 1 层持有者
            // 施加=叠层=有增益（旧「已持有即排除」漏此候选）；无活体分支增益原子（纯复苏技能）
            // 且无尸体=不占行动
            int grantBuffType = -1;
            SkillEffectConfig grantAtom = null; // 具名原子引用（决策五十七：满层判定按 buffAsset 身份）
            if (data.effects != null)
                foreach (var atom in data.effects)
                    if (atom.kind == SkillEffectKind.ApplyBuff
                        && atom.condition == SkillEffectCondition.TargetIsAlive)
                    {
                        grantAtom = atom;
                        grantBuffType = (int)(atom.buffAsset != null ? atom.buffAsset.buffType : atom.buffType);
                        break;
                    }
            if (grantBuffType < 0) return false;

            // 增益候选：最缺血的未满层存活我方（含施法者自身；等比平局 unitId 升序）——
            // 满层判定走活体 BaseBuff.IsAtStackCap（快照 BuffState 无层数/上限字段）
            UnitState best = null;
            int bestRatio = int.MaxValue;
            string bestId = null;
            foreach (var u in snapshot.MainWorldUnits()) // 主世界域单源（②④迁移：灵魂维度隔离判据收口）
            {
                if ((TeamType)u.team != team || u.isCorpse != 0) continue;
                var live = sim.GetUnit(u.unitId);
                // 满层判定按具名身份（决策五十七：buffAsset 引用优先；null 回落 type——与编译/工厂同口径）
                var existingBuff = live?.Buffs.Find(b => b != null && (grantAtom != null && grantAtom.buffAsset != null
                    ? b.Config == grantAtom.buffAsset : b.Type == (BuffType)grantBuffType));
                if (existingBuff != null && existingBuff.IsAtStackCap()) continue; // 已满层：重施加无增益
                if (u.maxHp <= 0) continue;
                int ratio = u.hp * 10000 / u.maxHp;
                if (ratio < bestRatio || (ratio == bestRatio && bestId != null
                    && string.CompareOrdinal(u.unitId, bestId) < 0))
                {
                    best = u;
                    bestRatio = ratio;
                    bestId = u.unitId;
                }
            }
            if (best == null) return false; // 全员已满层光环：不占行动（攒满也不空放）

            tracker.Offer(Mathf.RoundToInt(UnitTargetBurstBuffScore * profile.爆发优先权重),
                Skill(playerId, unit, skillIndex, Direction2D.Up, turn, best.unitId));
            return true;
        }

        /// <summary>快照 Buff 条目含类型判定（增益候选排除已持有者用——命令流/快照侧轻量读法）</summary>
        private static bool HasBuffState(List<BuffState> buffs, int type)
        {
            if (buffs == null) return false;
            foreach (var b in buffs)
                if (b.type == type) return true;
            return false;
        }

        /// <summary>②c 无目标自施放爆发候选（aimMode=None——首个=凯亚凛冽轮舞，无方向域无目标域）：
        /// OnCast ApplyBuff(Caster) 自身增益（寒冰之棱）；已达叠层上限（IsAtStackCap——重施加 Merge
        /// 无增益）不占行动；评分=增益常量乘爆发优先权重（伙伴脑/配额脑操魔神档共享骨架同分支）。
        /// J-2 语境评分（2026-10-04 拍板）：即将施加的光环半径内每存活敌 +SelfCastBurstPerEnemyScore
        ///（半径=BuffType 直查 AuraRadiusOfBuffType——施放前 Buff 未在身上 AuraRadiusOf 查不到）——
        /// 贴敌群施放=每回合 tick 逐层吃满（优先于常规攻击先放）；空场施放=预载（Buff 永久非浪费）。
        /// 返回=是否产生候选（挂机修复的驻位判定消费）</summary>
        private static bool ScoreSelfCastBurst(BattleSimState sim, Unit unit, string playerId, int turn, int skillIndex,
            SkillConfig.SkillData data, CandidateTracker tracker, UnitConfig.CompanionProfile profile)
        {
            if (data.effects == null) return false;
            foreach (var atom in data.effects)
            {
                if (atom == null || atom.trigger != SkillEffectTrigger.OnCast) continue;
                if (atom.kind != SkillEffectKind.ApplyBuff || atom.targetFilter != SkillEffectTargetFilter.Caster) continue;
                // 具名感知（决策五十七）：buffAsset 引用优先按身份判已持有/光环半径；null 回落 buffType
                var sensedBuffType = atom.buffAsset != null ? atom.buffAsset.buffType : atom.buffType;
                var existing = unit.Buffs.Find(b => b != null && (atom.buffAsset != null
                    ? b.Config == atom.buffAsset : b.Type == atom.buffType));
                if (existing != null && existing.IsAtStackCap()) return false; // 已满层：重施加无增益不占行动
                int auraRadiusJ2 = BattleHeuristics.AuraRadiusOfBuffType(sensedBuffType);
                int enemiesInAuraJ2 = 0;
                if (auraRadiusJ2 > 0)
                {
                    var selfIdJ2 = unit.GetUnitComponent<UnitIdentity>();
                    if (selfIdJ2 != null)
                        foreach (var kv in sim.SortedUnits())
                        {
                            var e = kv.Value;
                            var eid = e.GetUnitComponent<UnitIdentity>();
                            if (eid == null || eid.Team == selfIdJ2.Team) continue;
                            if (BattleSimState.IsDead(e)) continue;
                            if (!BattleHeuristics.PerceivesEnemy(sim, unit, e)) continue; // AI 雾限（追拍三）+战术视距（2026-10-23）：光环内近敌照常计数
                            if (BattleHeuristics.DistanceCm(sim, e, unit) <= auraRadiusJ2) enemiesInAuraJ2++; // 欧氏 cm（P5）
                        }
                }
                tracker.Offer(Mathf.RoundToInt((SelfCastBurstBuffScore
                        + enemiesInAuraJ2 * SelfCastBurstPerEnemyScore) * profile.爆发优先权重),
                    Skill(playerId, unit, skillIndex, Direction2D.Up, turn));
                return true;
            }
            return false;
        }

        /// <summary>对齐走位落点占据查（保守口径）：任何单位（含尸体——尸体保留碰撞）占格即视为挡，
        /// 与 FindApproachFirstStep 的 occupied 同近似（友方互不阻挡 flag 的精确判定不在对齐档做——
        /// 保守向偏差=少选格不会被结算弹回）</summary>
        private static bool IsCellOccupiedForStep(BattleSimState sim, BattleCell cell)
        {
            foreach (var kv in sim.SortedUnits())
            {
                var pos = sim.GetPosition(kv.Value);
                if (pos.x == cell.x && pos.y == cell.y) return true;
            }
            return false;
        }

        // ==================== P5 连续空间版重写件（决策九十五蓝图 §6——原十字 BFS/射线/轴对齐族整替） ====================

        /// <summary>采集候选档（docs/47 拍板⑦；P5 连续版）：采集型角色朝资源点的移动候选。
        /// **收获档（途经采集型，安柏）**：对每个存活堆按真实 A* 路径（RTSPathfinder+BuildPathCells——
        /// 与执行层途经采集同源格序）估采集价值：途经格每命中一堆 +min(配额,余量)（同堆去重=Host
        /// per-堆配额口径）、落点为存活堆再 +1（站立采集）；取价值最高堆格心为目的地（同值取首个
        /// 枚举序=确定性）。旧「十字向射线逐格扫可停落点」随 A* 失效——拐弯路径的堆值照常入账
        ///（两堆一线穿梭收益曲线保留）。
        /// **逼近档（纯 tag 型）**：目的地=最近未耗尽堆格心；PathReachable 不可达跳过。
        /// **采集角色透视（2026-10-28 返拍）=感知层豁免探索账本**（核心透视同构；客户端堆件在
        /// 未探索格隐藏属渲染层分口径）；**返回 true=纯 tag 型采集者已站在存活资源点上**——上层
        /// 据此让移动候选整体让位（tag 型无途经配额，站桩即其采集上限；攻击候选照常）；被占堆
        /// 跳过（谁站谁采，被占零收益转投次近堆）；分=采集意图基准分−距离（格折算）×每格衰减
        ///（**不乘人格移动优先权重**——拍板硬语义）。资源点全耗尽/全被占=零候选零回归</summary>
        private static bool ScoreCollectCandidate(BattleSimState sim, Unit unit,
            string playerId, TeamType team, int turn, CandidateTracker tracker)
        {
            if (!BattleHeuristics.IsHarvester(unit)) return false;
            var from = sim.GetPosCm(unit);
            var fromCell = BattleCell.FromCm(from.x, from.y);
            var collect = MoveExecutor.CollectParamsOf(unit);
            bool passCollector = collect.count > 0;

            // 纯 tag 型采集者已站上存活资源点：站桩即其采集上限——返回 true 让移动候选让位
            //（回合末/秒拍自动采），攻击候选照常
            if (!passCollector)
            {
                foreach (var node in sim.SortedResourceNodes())
                    if (node != null && node.remainingCharges > 0
                        && node.cell.x == fromCell.x && node.cell.y == fromCell.y)
                        return true;
            }

            // 最近未耗尽且未被占资源点（欧氏 cm；同距取首个枚举序=注册序确定性；
            // 透视=不查探索账本；被占堆跳过——谁站谁采，被占零收益转投次近堆）
            ResourceNodeState bestNode = null;
            int bestDist = int.MaxValue;
            foreach (var node in sim.SortedResourceNodes())
            {
                if (node == null || node.remainingCharges <= 0) continue;
                if (IsCellOccupiedForStep(sim, node.cell)) continue;
                int dist = BattleHeuristics.DistCm(from.x, from.y, node.cell.CenterXCm, node.cell.CenterYCm);
                if (dist >= bestDist) continue;
                bestDist = dist;
                bestNode = node;
            }
            if (bestNode == null && !passCollector) return false; // 全耗尽/全被占：tag 型零候选

            // 收获档（途经采集型）：真 A* 路径堆值——脑层估值与执行层途经判定共用格序单源
            if (passCollector)
            {
                var moveable = unit.GetUnitComponent<UnitMoveable>();
                var forceType = moveable != null ? moveable.NormalMoveType : ForceType.Walk;
                int bestValue = 0;
                ResourceNodeState bestPile = null;
                foreach (var node in sim.SortedResourceNodes())
                {
                    if (node == null || node.remainingCharges <= 0) continue;
                    var path = RTSPathfinder.FindPath(sim, from.x, from.y,
                        node.cell.CenterXCm, node.cell.CenterYCm, forceType);
                    if (path == null || path.Count == 0) continue;
                    var cells = RTSMovementSystem.BuildPathCells(sim, from, path);
                    int value = 0;
                    var seenPiles = new HashSet<string>();
                    foreach (var c in cells)
                        foreach (var hit in sim.SortedResourceNodes())
                        {
                            if (hit == null || hit.remainingCharges <= 0) continue;
                            if (hit.cell.x != c.x || hit.cell.y != c.y) continue;
                            if (!seenPiles.Add(c.x + "," + c.y)) continue; // 同堆只计一次（Host per-堆配额口径）
                            value += Mathf.Min(collect.count, hit.remainingCharges);
                        }
                    if (LivePileAt(sim, node.cell)) value += 1; // 落点=堆：站立采集估值
                    if (value > bestValue) { bestValue = value; bestPile = node; }
                }
                if (bestPile != null && bestValue > 0)
                {
                    tracker.Offer(CollectIntentBaseScore, Move(playerId, unit,
                        new Vector2IntCm(bestPile.cell.CenterXCm, bestPile.cell.CenterYCm), turn));
                    return false;
                }
                // 无可采路径（全围死）：落回逼近档（bestNode 通道）；全耗尽=零候选
                if (bestNode == null) return false;
            }

            // 逼近档（纯 tag 型）：目的地=堆格心；不可达（围死）跳过
            var dest = new Vector2IntCm(bestNode.cell.CenterXCm, bestNode.cell.CenterYCm);
            if (!BattleHeuristics.PathReachable(sim, unit, dest.x, dest.z)) return false;
            int score = CollectIntentBaseScore - bestDist / CellMetrics.CellSizeCm * CollectDistancePenalty;
            if (score <= 0) return false;
            tracker.Offer(score, Move(playerId, unit, dest, turn));
            return false;
        }

        /// <summary>②移动候选（P5 连续版——「选目标点」取代「算步进」）：锚点=距离升序取**首个
        /// 可达成**目标（支援型先试最缺血我方锚）；对每锚计算 **StandoffTargetCm 驻位点**（敌锚=
        /// 攻击射程 engageDistanceCm/伤员锚=治疗半径；光环持有者 J-3 收紧到光环半径；射程 0=贴身
        /// 接触），targetX/targetZ 直交——A*+steering 全权交执行层 RTSMovementSystem（tick 推进+
        /// 圆碰撞+被挡停接触前）。已在驻位距离内**且有攻击/增益候选**=驻位即最优位不再逼近；
        /// 无候选=换下一锚（CD 期站桩等下一拍，勿对锚抖动）——自由角时代「对角错位十字线打不到」
        /// 问题结构性消失，对齐走位/换线探针/真无解兜底三档全退役（蓝图 §4.1）。
        /// 评分=兜底+逼近进度（格折算，钳搜索半径）+接敌×激进度+走位进射击线（落点射程内有
        /// 可见敌=自由角近似），乘移动优先权重。
        /// hasActionCandidate=攻击档回报的本单位候选存在性（**勿改用 tracker.Best**——配额脑共享
        /// tracker 跨单位取全场最优，Best 非空≠本单位有行动）</summary>
        private static void ScoreMoveCandidate(BattleSimState sim, BattleSnapshot snapshot, Unit unit,
            string playerId, TeamType team, int turn, CandidateTracker tracker,
            int? staminaPoolOverride, UnitConfig.CompanionProfile profile, bool hasActionCandidate,
            OpponentThreatModel.ThreatMap threat, TeamIntentBoard board = null,
            bool isPlayerDirectControl = false)
        {
            // 移动免费（蓝图 §3.3「0 体力 0 元能」+P5 资产 Move costs 清空）：HasAll 恒过——
            // 保留调用（未来移动收费再接资源链）
            if (!ResourceGate.HasAll(sim, unit, playerId, MoveExecutor.GetMoveCosts(unit), out _, staminaPoolOverride,
                    isPlayerDirectControl))
            {
                return;
            }

            var fromCmRaw = sim.GetPosCm(unit);
            var from = new Vector2IntCm(fromCmRaw.x, fromCmRaw.y); // P5：GetPosCm=Vector2Int(x,y=z)→点位域载体包一层
            var forceType = unit.GetUnitComponent<UnitMoveable>()?.NormalMoveType ?? ForceType.Walk;
            int maxSearchCm = MoveExecutor.MaxMoveDistance(unit) * CellMetrics.CellSizeCm; // 站位搜索半径（数据驱动=移动技能机动半径——旧「每回合步数上限」的连续版语义，P6 调参批可改档）

            // 驻位基准（CR-Move，2026-10-02 拍板「像皇室战争那样」）：偏好交战距离>0=手动覆写微调
            // 手感（**cm**——P5 行档距离域迁移）；0=**自动=攻击射程单源**（AttackRangeCmOf——
            // 「进攻击范围即停」的连续版：安柏箭矢 800/凯亚霜袭 150/芭芭拉水球 400）
            int engageDistanceCm = profile.偏好交战距离 > 0
                ? profile.偏好交战距离 * CellMetrics.CellSizeCm
                : BattleHeuristics.AttackRangeCmOf(sim, unit, playerId);

            // 支援型四态（先锋伴随>光环贴敌>保险后撤>伤员锚）——F-2 站位评分器接管段（P5 圆盘版）
            Unit vanguard = null;
            if (profile.候选类别 == UnitConfig.CompanionRole.Support
                && profile.目标偏好 == UnitConfig.CompanionTargetPreference.MostWoundedAlly)
            {
                var wounded = profile.先锋伴随权重 > 0f
                    ? null // 随军支援不锚伤员（拍板「只关心先锋」）
                    : FindMostWoundedAlly(sim, unit, team);
                if (wounded == null && profile.先锋伴随权重 > 0f
                    && SupportStanceOf(sim, unit, team, threat) == 1) // H：仅前线形态伴随
                    vanguard = FindMeleeVanguard(sim, unit, team, board);
                if (wounded == null && vanguard == null
                    && BattleHeuristics.AuraRadiusOf(unit) > 0
                    && profile.光环贴敌权重 > 0f)
                    wounded = unit; // G-5 修 3：光环贴敌模式（自体假锚——奶程恒满，光环前瞻主导）
                if (wounded == null && vanguard == null
                    && SupportStanceOf(sim, unit, team, threat) > 1)
                    wounded = unit; // G-5 保险后撤模式：自体假锚（贴敌归零+后撤梯度主导）
                if (wounded != null)
                {
                    var woundedPosRaw = sim.GetPosCm(wounded);
                    var woundedPos = new Vector2IntCm(woundedPosRaw.x, woundedPosRaw.y);
                    TryOfferSupportPosition(sim, unit, playerId, team, turn, from, maxSearchCm, forceType,
                        wounded, woundedPos, threat, tracker, profile, null);
                    return; // 站位评分器已给全档候选（或最优=当前格），锚循环不再参与
                }
                // H-3 视野敌集（先锋攻击视野内〔cm 域〕存活敌集）——光环敌覆盖分/前瞻梯度/就位判定过滤域
                HashSet<string> visionEnemies = null;
                if (vanguard != null)
                    visionEnemies = CollectVisionEnemies(sim, vanguard, team);
                // H-4 追随外推：按先锋上拍位移向量把追逐锚前移（迎头追逐）；I 批讨论直连：追逐锚
                // 优先=先锋**已声明/已申报的落点**（board.DeclaredMovement——当拍即消费=真实决策位）
                string vanguardId = vanguard != null
                    ? vanguard.GetUnitComponent<UnitIdentity>()?.UnitID : null;
                var vanguardDeclaredPos = default(BattleCell);
                bool vanguardDeclared = vanguard != null && board != null
                    && board.DeclaredMovement.TryGetValue(vanguardId, out vanguardDeclaredPos);
                BattleCell vanguardPosCell = vanguardDeclared
                    ? vanguardDeclaredPos
                    : (vanguard != null
                        ? BattleCell.FromCm(sim.GetPosCm(vanguard).x, sim.GetPosCm(vanguard).y)
                        : BattleCell.FromCm(from.x, from.z));
                string unitIdH4 = unit.GetUnitComponent<UnitIdentity>()?.UnitID;
                if (!vanguardDeclared
                    && vanguard != null
                    && _vanguardTrack.TryGetValue(unitIdH4, out var trackH4)
                    && trackH4.vanguardId == vanguardId)
                {
                    int dx = vanguardPosCell.x - trackH4.pos.x;
                    int dy = vanguardPosCell.y - trackH4.pos.y;
                    if (dx != 0 || dy != 0)
                        vanguardPosCell = new BattleCell(vanguardPosCell.x + dx, vanguardPosCell.y + dy);
                }
                if (vanguard != null)
                {
                    if (!_bidPhase) // I-A 申报遍槽写入抑制
                    {
                        var vNow = sim.GetPosCm(vanguard);
                        _vanguardTrack[unitIdH4] = (vanguardId, BattleCell.FromCm(vNow.x, vNow.y), turn);
                        board?.DeclareFollow(vanguardId); // I-C：跟随名额声明（执行遍）
                    }
                    var vPosCm = new Vector2IntCm(vanguardPosCell.CenterXCm, vanguardPosCell.CenterYCm);
                    bool __offeredSV = TryOfferSupportPosition(sim, unit, playerId, team, turn, from, maxSearchCm, forceType,
                            vanguard, vPosCm, threat, tracker, profile, visionEnemies);
                    if (__offeredSV)
                        return; // H：伴随位已 Offer（或最优=当前格=已罩满视野敌）
                    // 评分器圆盘无改进落点：伴随半径内=驻位完成；否则兜底直奔先锋驻位（奶程内停）
                    //——托底 VanguardChaseFloorScore 压常规单体技能（T15 探针口径：先锋/伤员追赶期
                    // 裸分被尸体线自奶压过=被落后后永远站桩自奶不归队）
                    int accompanyRadiusCm = profile.支援贴近距离 > 0
                        ? profile.支援贴近距离 * CellMetrics.CellSizeCm
                        : BattleHeuristics.SupportRadiusOf(unit);
                    bool __inAccompany = BattleHeuristics.DistCm(from.x, from.z, vPosCm.x, vPosCm.z) <= accompanyRadiusCm;
                    if (__inAccompany)
                        return; // 已在伴随（奶程）半径内：驻位完成
                    var chaseDest = BattleHeuristics.StandoffPointCm(sim, unit, vPosCm.x, vPosCm.z, accompanyRadiusCm);
                    bool __chaseOk = BattleHeuristics.PathReachable(sim, unit, chaseDest.x, chaseDest.z);
                    if (__chaseOk)
                    {
                        tracker.Offer(Mathf.Max(VanguardChaseFloorScore,
                            Mathf.RoundToInt(MoveBaseScore * profile.移动优先权重)),
                            Move(playerId, unit, chaseDest, turn));
                        return;
                    }
                    // 先锋被围死不可达：落到敌锚循环（老行为）
                }
            }

            // 锚序：支援型先试最缺血我方（奶谁/去哪护），全军敌迭代殿后——E-3 滞回锁+J-4 推核心+
            // 无敌环境推进分三段语义保留，距离域全 cm
            var enemyAnchors = BattleHeuristics.FindEnemiesByDistance(sim, unit);
            if (_lastMoveAnchors.TryGetValue(UnitIdOf(sim, unit), out var lockedAnchorId)
                && !string.IsNullOrEmpty(lockedAnchorId))
            {
                var locked = sim.GetUnit(lockedAnchorId);
                if (locked != null && !BattleSimState.IsDead(locked) && enemyAnchors.Contains(locked))
                {
                    // 锚点滞回（凌晨批议题③）：现锚廉价复核通过后与最优新锚比距离——新锚须比现锚
                    // 严格近「锚点滞回门槛」cm 才接管，门槛内延续锁定（治两等价锚来回=机器味暴露点）
                    int lockedDist = BattleHeuristics.DistanceCm(sim, unit, locked);
                    int bestOtherDist = int.MaxValue;
                    foreach (var e in enemyAnchors)
                    {
                        if (e == locked) continue;
                        int d = BattleHeuristics.DistanceCm(sim, unit, e);
                        if (d < bestOtherDist) bestOtherDist = d;
                    }
                    if (bestOtherDist == int.MaxValue
                        || lockedDist <= bestOtherDist + AITuningConfig.Instance.锚点滞回门槛) // cm（P5 ×100）
                    {
                        enemyAnchors.Remove(locked);
                        enemyAnchors.Insert(0, locked); // 锁定延续（滞回门槛内）：插队敌序首
                    }
                    else if (!_bidPhase)
                    {
                        _lastMoveAnchors.Remove(UnitIdOf(sim, unit)); // 显著更优新锚：释放锁（申报遍槽写入抑制）
                    }
                }
            }
            // J-4 伙伴推核心：自身攻击视野内（cm 单源）无普通敌单位（建筑不算「其它敌人」）→ 敌方
            // 核心插队锚序首直奔推进；核心永不入 E-3 锁（RememberMoveAnchor 排除）
            int attackVisionJ4 = BattleHeuristics.TacticalSightOf(unit);
            bool anyVisionEnemyJ4 = false;
            foreach (var e in enemyAnchors)
            {
                if (BattleHeuristics.IsBuilding(e)) continue;
                if (BattleHeuristics.DistanceCm(sim, unit, e) <= attackVisionJ4) { anyVisionEnemyJ4 = true; break; }
            }
            if (!anyVisionEnemyJ4)
            {
                for (int ci = 0; ci < enemyAnchors.Count; ci++)
                {
                    if (!BattleHeuristics.IsCore(enemyAnchors[ci])) continue;
                    var coreJ4 = enemyAnchors[ci];
                    enemyAnchors.RemoveAt(ci);
                    enemyAnchors.Insert(0, coreJ4);
                    break;
                }
            }
            // 无敌环境判定（议题①）：全部可感知敌锚都是建筑=感知无敌——唯一推进目标=敌核心，
            // 移动分吃「无敌环境推进分」（锚循环主评分处）
            bool emptyFieldAdvance = enemyAnchors.Count > 0;
            if (emptyFieldAdvance)
            {
                foreach (var e in enemyAnchors)
                {
                    if (!BattleHeuristics.IsBuilding(e)) { emptyFieldAdvance = false; break; }
                }
            }
            var anchors = new List<Unit>();
            if (profile.候选类别 == UnitConfig.CompanionRole.Support
                && profile.目标偏好 == UnitConfig.CompanionTargetPreference.MostWoundedAlly
                && profile.先锋伴随权重 <= 0f) // 随军支援锚循环不掺伤员（拍板「只关心先锋」）
            {
                var wounded = FindMostWoundedAlly(sim, unit, team);
                if (wounded != null) anchors.Add(wounded);
            }
            anchors.AddRange(enemyAnchors);

            foreach (var anchor in anchors)
            {
                int distance = BattleHeuristics.DistanceCm(sim, unit, anchor); // 欧氏 cm（P5）
                if (distance <= 0) continue; // 同点堆叠：无逼近意义

                // F-1 支援锚驻位分流：锚的敌我决定距离语义——伤员锚（我方）=治疗半径（SupportRadiusOf
                //cm 单源/档案「支援贴近距离」覆写）=贴身奶；敌锚=攻击射程 engageDistanceCm（CR-Move）
                var anchorIdentity = anchor.GetUnitComponent<UnitIdentity>();
                bool isAllyAnchor = anchorIdentity != null && anchorIdentity.Team == team;
                int anchorEngageCm = isAllyAnchor
                    ? (profile.支援贴近距离 > 0
                        ? profile.支援贴近距离 * CellMetrics.CellSizeCm
                        : BattleHeuristics.SupportRadiusOf(unit))
                    : engageDistanceCm;
                // J-3 爆发态贴脸：持有半径型 tick 光环时敌锚驻位收紧到光环半径——光环 tick+冰附着链
                // 只在半径内吃得到；**托底只在「射程内收紧带」**（distance ≤ 原攻击驻位距离——射程外
                // 走常规评分竞争；观察局 T6 实锤旧版任意锚距都托 95 的弃贴脸病灶）；斩杀恒赢
                bool auraTighten = false;
                if (!isAllyAnchor)
                {
                    int auraRadiusJ3 = BattleHeuristics.AuraRadiusOf(unit);
                    if (auraRadiusJ3 > 0 && auraRadiusJ3 < anchorEngageCm)
                    {
                        int engageDistanceJ3 = anchorEngageCm; // 原攻击驻位距离（托底带判定）
                        anchorEngageCm = auraRadiusJ3;
                        auraTighten = distance <= engageDistanceJ3 + BattleHeuristics.UnitRadiusCm(anchor);
                    }
                }

                // 已在驻位距离内（敌圆边可及=dist ≤ engage+锚半径，与命中几何同口径）：
                // 有攻击/增益候选=驻位即最优位；无候选=换下一锚（勿对锚抖动）
                if (distance <= anchorEngageCm + BattleHeuristics.UnitRadiusCm(anchor))
                {
                    if (hasActionCandidate) return;
                    continue;
                }

                // 驻位点+可达性（不可达=围死换下一锚——最近可达优先，眷属 v4 换目标巡逻同语义）
                var dest = BattleHeuristics.StandoffTargetCm(sim, unit, anchor, anchorEngageCm);
                bool reachable = BattleHeuristics.PathReachable(sim, unit, dest.x, dest.z);
                if (!reachable) continue;

                // 逼近进度（格域折算保持 AITuning「每格 X 分」语义；钳搜索半径=远锚进度有界）
                int progressCells = Math.Min(maxSearchCm, distance - anchorEngageCm) / CellMetrics.CellSizeCm;
                int score = MoveBaseScore
                            + progressCells * MoveProgressScorePerCell
                            + Mathf.RoundToInt(Math.Min(MoveEngageBonusMax, Math.Max(0, 16 - distance / CellMetrics.CellSizeCm) / 2)
                                               * profile.激进度);
                if (emptyFieldAdvance && !isAllyAnchor)
                    score += EmptyFieldAdvanceScore; // 无敌环境推进分（议题①）

                var destCell = BattleCell.FromCm(dest.x, dest.z);
                if (WouldHaveFiringLineFrom(sim, unit, playerId, team, destCell))
                    score += MoveLineUpScore; // 走位进射击线（P5=落点射程内有可见敌——自由角近似）
                int threatPenalty = ThreatPenaltyAt(threat, destCell);
                if (threatPenalty > 0) score = Mathf.Max(1, score - threatPenalty); // E-1 避险（钳 1 保底 Offer）
                score -= SelfPreservePenalty(sim, unit, team, dest.x, dest.z, profile); // F-1 支援自保（cm 域）
                score = Mathf.RoundToInt(score * profile.移动优先权重);
                if (auraTighten)
                    score = Math.Max(score, SupportAuraTakePositionScore); // J-3：收紧就位托底（压攻击档先就位，斩杀恒赢）
                tracker.Offer(score, Move(playerId, unit, dest, turn));
                RememberMoveAnchor(sim, unit, anchor, team); // E-3：逼近承诺=延续该锚（首个可达成锚即选）
                return; // 首个可达成锚即选（勿迭代全锚取最高分——保持最近可达优先的确定性）
            }
            // 真无解兜底退役（P5）：连续制下「对齐失败/换线失败」不存在——全部锚不可达或已驻位
            //（无行动候选）即终态=站桩等 CD/等下一拍，勿制造无意义微移动
        }

        /// <summary>落点是否有可开火攻击线（走位估值用——P5 连续版）：落点射程内存在可见存活敌
        ///（自由角=凡在射程内即可瞄——旧十字线对齐问题结构性消失；虚空/尸体遮挡不查=粗估档，
        /// 与旧版「近似预判」同级）</summary>
        private static bool WouldHaveFiringLineFrom(BattleSimState sim, Unit unit,
            string playerId, TeamType team, BattleCell cell)
        {
            int rangeCm = BattleHeuristics.AttackRangeCmOf(sim, unit, playerId);
            if (rangeCm <= 0) return false;
            return WouldHaveFiringLinePrecomputed(sim, BattleHeuristics.CollectVisibleEnemies(sim, team, unit), cell, rangeCm);
        }

        /// <summary>同上，但射程与可见敌集由调用方预算（2026-11-07 后期卡顿 A 批：站位圆盘每格
        /// 都重算 AttackRangeCmOf〔内部判定轨 new List+排序〕+ CollectVisibleEnemies）</summary>
        private static bool WouldHaveFiringLinePrecomputed(BattleSimState sim,
            List<(string id, Unit enemy)> visible, BattleCell cell, int rangeCm)
        {
            if (rangeCm <= 0 || visible == null) return false;
            int cellXCm = cell.CenterXCm;
            int cellZCm = cell.CenterYCm;
            for (int i = 0; i < visible.Count; i++)
            {
                var ep = sim.GetPosCm(visible[i].enemy);
                long dx = ep.x - cellXCm;
                long dz = ep.y - cellZCm;
                if (dx * dx + dz * dz <= (long)rangeCm * rangeCm) return true;
            }
            return false;
        }

        /// <summary>站位评分循环不变量（2026-11-07 后期卡顿 A 批）：圆盘枚举每格复用同一份
        /// 可见敌集/攻击射程/形态——原实现每格都重算，千格级枚举=单次站位评分 12ms 的主源。</summary>
        private readonly struct SupportScoreCtx
        {
            public readonly List<(string id, Unit enemy)> Visible;
            public readonly int Stance;
            public readonly int StanceNoThreat;
            public readonly int AttackRangeCm;

            public SupportScoreCtx(BattleSimState sim, Unit unit, string playerId, TeamType team,
                OpponentThreatModel.ThreatMap threat)
            {
                Visible = BattleHeuristics.CollectVisibleEnemies(sim, team, unit);
                Stance = SupportStanceOf(sim, unit, team, threat);
                StanceNoThreat = SupportStanceOf(sim, unit, team, null); // 自保惩罚按其原口径（threat=null）
                AttackRangeCm = BattleHeuristics.AttackRangeCmOf(sim, unit, playerId);
            }
        }

        /// <summary>支援站位评分器（F-2，docs/archive/34 §4）：支援型有伤员/先锋时的移动档形态——
        /// 「位置=选点」多维评分。P5 连续版：候选=**站位搜索圆盘**（半径=移动技能机动半径，行序
        /// 扫描=确定性；旧十字射线「移动=推力直线、拐弯格不可达」限制随 A* 失效——任意可停格全入
        /// 候选域），落点=格心 cm；评分维度不变（奶程/开火线/火线规避/自保/光环覆盖，cm 域）。
        /// 最优=当前位 → 不 Offer（驻位内化）；返回值（H 伴随批）：true=已 Offer；false=最优=当前位
        ///（或圆盘无候选）——伴随模式调用方回落「伴随半径判定+追逐兜底」，伤员/光环/保险三态=驻位</summary>
        private static bool TryOfferSupportPosition(BattleSimState sim, Unit unit,
            string playerId, TeamType team, int turn, Vector2IntCm from, int maxSearchCm, ForceType forceType,
            Unit wounded, Vector2IntCm woundedPos, OpponentThreatModel.ThreatMap threat, CandidateTracker tracker,
            UnitConfig.CompanionProfile profile, HashSet<string> visionEnemies)
        {
            bool woundedIsSelf = ReferenceEquals(wounded, unit); // G-5：伤员=自身（含保险后撤假锚）——治疗半径随她走
            int supportRadius = profile.支援贴近距离 > 0
                ? profile.支援贴近距离 * CellMetrics.CellSizeCm
                : BattleHeuristics.SupportRadiusOf(unit);
            if (supportRadius <= 0) supportRadius = CellMetrics.CellSizeCm; // 无治疗原子防御：贴 1 格（cm）
            // —— 循环不变量预算一次（2026-11-07 后期卡顿 A 批：以下两项原在 ScoreSupportCell 内
            //    每格重算——千格级枚举里 CollectVisibleEnemies 每次都新建 List+全量枚举+迷雾判定，
            //    是单次站位评分 12ms 的主源；形态 SupportStanceOf 同理纯白耗）——
            var scoreCtx = new SupportScoreCtx(sim, unit, playerId, team, threat);
            int auraRadius = BattleHeuristics.AuraRadiusOf(unit); // G-1 光环感知（无光环=0 不消费）

            // H-2 交战就位态（评分器豁免/托底共用单源）
            bool takePosition = !woundedIsSelf
                && ShouldTakeAuraPosition(sim, unit, team, threat, profile, woundedPos, visionEnemies);

            // 当前位评分（「不动」基准——所有候选格与它比，选出比它好的才动）
            int currentScore = ScoreSupportCell(sim, unit, playerId, team, from, woundedPos,
                supportRadius, auraRadius, woundedIsSelf, threat, profile, visionEnemies,
                scoreCtx);

            int bestScore = currentScore;
            Vector2IntCm bestDest = default;
            bool hasBest = false;
            int bestDistSq = 0;
            int auraBestScore = -1; Vector2IntCm auraBest = default; bool hasAuraBest = false; // H-2 罩敌格跟踪
            int currentCoverage = visionEnemies != null && auraRadius > 0
                ? EnemiesInAuraFrom(sim, scoreCtx.Visible, from, auraRadius, visionEnemies) : -1;
            int coverBestCount = currentCoverage; int coverBestScore = -1;
            Vector2IntCm coverBest = default; bool hasCoverBest = false; // H-3b 覆盖最优格跟踪

            // 圆盘枚举（P5：A* 可达取代直线射线——拐弯格照常可停；确定性=行序扫描+严格大于+同分取更近）
            var map = sim.Map;
            var fromCell = BattleCell.FromCm(from.x, from.z);
            int rCells = maxSearchCm / CellMetrics.CellSizeCm;
            for (int dy = -rCells; dy <= rCells; dy++)
            {
                for (int dx = -rCells; dx <= rCells; dx++)
                {
                    if (dx * dx + dy * dy > rCells * rCells) continue; // 圆盘（欧氏，整数域）
                    var cell = new BattleCell(fromCell.x + dx, fromCell.y + dy);
                    if (!map.HasTile(cell.x, cell.y) || !map.IsPassable(cell.x, cell.y, forceType)) continue;
                    if (IsCellOccupiedForStep(sim, cell)) continue; // 占格不可停（自身格天然排除）
                    var dest = new Vector2IntCm(cell.CenterXCm, cell.CenterYCm);
                    int score = ScoreSupportCell(sim, unit, playerId, team, dest, woundedPos,
                        supportRadius, auraRadius, woundedIsSelf, threat, profile, visionEnemies,
                        scoreCtx);
                    int cellCoverage = auraRadius > 0 && (visionEnemies != null || takePosition)
                        ? EnemiesInAuraFrom(sim, scoreCtx.Visible, dest, auraRadius, visionEnemies) : 0;
                    if (takePosition && auraRadius > 0 && cellCoverage > 0 && score > auraBestScore)
                    { auraBestScore = score; auraBest = dest; hasAuraBest = true; }
                    if (visionEnemies != null && auraRadius > 0
                        && (cellCoverage > coverBestCount
                            || (cellCoverage == coverBestCount && cellCoverage > currentCoverage && score > coverBestScore)))
                    { coverBestCount = cellCoverage; coverBestScore = score; coverBest = dest; hasCoverBest = true; }
                    int distSq = dx * dx + dy * dy;
                    if (score > bestScore || (score == bestScore && hasBest && distSq < bestDistSq))
                    {
                        bestScore = score;
                        bestDest = dest;
                        hasBest = true;
                        bestDistSq = distSq;
                    }
                }
            }

            // H-2 就位豁免：交战未就位+圆盘存在罩敌可停格→罩敌格直接成为移动目标（准号令语义，
            // 豁免严格大于；罩敌格内部取评分最高者=双覆盖交集格）
            if (takePosition && hasAuraBest
                && (!hasBest || auraBestScore >= bestScore || (hasBest
                        && EnemiesInAuraFrom(sim, scoreCtx.Visible, bestDest, auraRadius, visionEnemies) == 0)))
            {
                bestDest = auraBest;
                bestScore = Math.Max(auraBestScore, bestScore);
                hasBest = true;
            }

            // H-3b 覆盖提升接管：随军语境存在**严格更多视野敌覆盖**的圆盘可停格→覆盖最优格无条件
            // 成为移动目标（覆盖数主导、并列取评分）+托底 95；让位：仅尸体+满槽复苏
            bool reviveYield = HasAllyCorpse(sim, team) && BurstEnergyReady(unit);
            bool improveTake = visionEnemies != null && auraRadius > 0 && !reviveYield
                && coverBestCount > currentCoverage && hasCoverBest;
            if (improveTake)
            {
                bestDest = coverBest;
                bestScore = Math.Max(coverBestScore, bestScore);
                hasBest = true;
            }

            if (!hasBest) return false; // 无改进落点（最优=当前位）：伤员/光环/保险三态=驻位；伴随=回落追逐

            // H-2/H-3b 就位 Offer 托底：选中罩敌格→Offer 分托底恒压常规技能（先就位贴脸再输出）
            int offerFloor = 0;
            if (takePosition && hasAuraBest
                && EnemiesInAuraFrom(sim, scoreCtx.Visible, bestDest, auraRadius, visionEnemies) > 0)
                offerFloor = SupportAuraTakePositionScore;
            if (improveTake)
                offerFloor = Math.Max(offerFloor, SupportAuraTakePositionScore);

            int offerScore = Mathf.Max(offerFloor, Mathf.RoundToInt(bestScore * profile.移动优先权重));
            // T15 探针返修：先锋/伤员追赶期（锚=真实单位、超出伴随半径）的移动 Offer 同享托底
            if (!woundedIsSelf && offerScore < VanguardChaseFloorScore
                && BattleHeuristics.DistCm(from.x, from.z, woundedPos.x, woundedPos.z) > supportRadius)
                offerScore = VanguardChaseFloorScore;
            tracker.Offer(offerScore, Move(playerId, unit, bestDest, turn));
            RememberMoveAnchor(sim, unit, wounded, team); // 敌方锚才记录（wounded=我方→内部跳过）——E-3 槽不污染
            return true;
        }

        /// <summary>支援站位单点评分（F-2 四维 + G-1 光环维度，cm 域）：奶程梯度 + 开火线 − 火线规避
        /// − 自保 + 光环敌覆盖（各维正交）。woundedIsSelf（伤员=施法者自身）：治疗半径随施法者
        /// 移动——distToWounded 恒 0（G-5 返修 2 语义保留）</summary>
        private static int ScoreSupportCell(BattleSimState sim, Unit unit, string playerId, TeamType team,
            Vector2IntCm pos, Vector2IntCm woundedPos, int supportRadius, int auraRadius, bool woundedIsSelf,
            OpponentThreatModel.ThreatMap threat, UnitConfig.CompanionProfile profile, HashSet<string> visionEnemies,
            in SupportScoreCtx ctx)
        {
            // stance 由调用方预算一次（2026-11-07 后期卡顿 A 批：逐格重算形态=纯白耗） // G-2 形态因子（本点评估用）
            int distToWounded = woundedIsSelf ? 0
                : BattleHeuristics.DistCm(pos.x, pos.z, woundedPos.x, woundedPos.z);
            int score = distToWounded <= supportRadius
                ? SupportHealReachScore
                : Math.Max(0, SupportHealReachScore
                    - (distToWounded - supportRadius) / CellMetrics.CellSizeCm * SupportHealReachDecayPerCell);
            if (WouldHaveFiringLinePrecomputed(sim, ctx.Visible, BattleCell.FromCm(pos.x, pos.z), ctx.AttackRangeCm))
                score += MoveLineUpScore; // 支援型照常输出：落点有开火线=能打水球
            int threatPenalty = Mathf.CeilToInt(ThreatPenaltyAt(threat, BattleCell.FromCm(pos.x, pos.z))
                * SupportThreatPenaltyScale); // E-1 火线规避——支援型 ×减半系数（G-5 调参语义保留）
            if (threatPenalty > 0) score = Mathf.Max(1, score - threatPenalty);
            score -= SelfPreservePenaltyFrom(sim, ctx.Visible, pos.x, pos.z, profile, ctx.StanceNoThreat); // F-1 自保（内含 G-2 形态因子）
            // G-5 保险形态后撤梯度：远离最近敌每格 +2（上限 12）——保险形态贴敌分归零后的方向性驱动
            int nearestEnemyForStance = NearestEnemyDistanceFrom(sim, ctx.Visible, pos.x, pos.z);
            if (ctx.Stance > 1 && nearestEnemyForStance < int.MaxValue)
                score += Math.Min(SupportRetreatCap,
                    nearestEnemyForStance / CellMetrics.CellSizeCm * SupportRetreatPerStep);
            // G-1 光环敌覆盖 + G-5 前瞻梯度 + H-3 视野口径（语义保留，距离域 cm——「每格」系数格折算）
            if (auraRadius > 0 && profile.光环贴敌权重 > 0f && ctx.Stance == 1)
            {
                int nearestEnemy = NearestEnemyDistanceFrom(sim, ctx.Visible, pos.x, pos.z, visionEnemies);
                int enemiesInAura = EnemiesInAuraFrom(sim, ctx.Visible, pos, auraRadius, visionEnemies);
                int perEnemy = SupportAuraPerEnemy
                    * (visionEnemies != null ? VanguardAuraPerEnemyMult : 1); // H-3：伴随语境敌分翻倍
                int auraScore = Mathf.RoundToInt(perEnemy * profile.光环贴敌权重) * enemiesInAura;
                if (enemiesInAura == 0 && nearestEnemy < int.MaxValue)
                    auraScore += Math.Min(SupportAuraApproachCap,
                        Math.Max(0, (SupportAuraApproachBase - nearestEnemy) / CellMetrics.CellSizeCm
                            * SupportAuraApproachPerStep))
                        * Mathf.RoundToInt(Mathf.Min(1f, profile.光环贴敌权重));
                score += auraScore;
            }
            return score;
        }

        /// <summary>落点距最近存活敌欧氏 cm（G-5 抽提共用——光环前瞻/保险后撤梯度两消费方；
        /// 无敌=int.MaxValue。filter=null 不过滤；非空（H-3 视野敌集）=只对集合内敌求最近</summary>
        private static int NearestEnemyDistance(BattleSimState sim, Unit unit, TeamType team,
            int xCm, int zCm, HashSet<string> filter = null)
        {
            return NearestEnemyDistanceFrom(sim, BattleHeuristics.CollectVisibleEnemies(sim, team, unit), xCm, zCm, filter);
        }

        /// <summary>同上，但复用调用方预算好的可见敌列表（2026-11-07 后期卡顿 A 批：站位枚举
        /// 每格一次 CollectVisibleEnemies=每次新建 List+全量枚举+迷雾判定，千格级=秒级开销）</summary>
        private static int NearestEnemyDistanceFrom(BattleSimState sim, List<(string id, Unit enemy)> visible,
            int xCm, int zCm, HashSet<string> filter = null)
        {
            int nearest = int.MaxValue;
            if (visible == null) return nearest;
            for (int i = 0; i < visible.Count; i++)
            {
                var (enemyId, enemy) = visible[i];
                if (filter != null && !filter.Contains(enemyId)) continue;
                var ep = sim.GetPosCm(enemy);
                int dist = BattleHeuristics.DistCm(xCm, zCm, ep.x, ep.y);
                if (dist < nearest) nearest = dist;
            }
            return nearest;
        }

        /// <summary>落点光环半径内敌数（H-2 交战就位——与 ScoreSupportCell 光环敌覆盖同口径；
        /// filter=null 不过滤；非空（H-3 视野敌集）=只数集合内的敌=「先锋攻击视野外的敌人不关心」）</summary>
        private static int EnemiesInAuraAt(BattleSimState sim, Unit unit, TeamType team,
            Vector2IntCm pos, int auraRadiusCm, HashSet<string> filter)
        {
            return EnemiesInAuraFrom(sim, BattleHeuristics.CollectVisibleEnemies(sim, team, unit), pos, auraRadiusCm, filter);
        }

        /// <summary>同上，但复用调用方预算好的可见敌列表（2026-11-07 后期卡顿 A 批）</summary>
        private static int EnemiesInAuraFrom(BattleSimState sim, List<(string id, Unit enemy)> visible,
            Vector2IntCm pos, int auraRadiusCm, HashSet<string> filter)
        {
            int count = 0;
            if (visible == null) return count;
            for (int i = 0; i < visible.Count; i++)
            {
                var (enemyId, enemy) = visible[i];
                if (filter != null && !filter.Contains(enemyId)) continue;
                var ep = sim.GetPosCm(enemy);
                if (BattleHeuristics.DistCm(pos.x, pos.z, ep.x, ep.y) <= auraRadiusCm) count++;
            }
            return count;
        }

        /// <summary>H-3 视野敌集：先锋攻击视野内（cm 单源）的存活敌 id 集——伴随语境光环敌覆盖分/
        /// 前瞻梯度/就位判定的过滤域；视野外敌全不关心（不为它们移动、不被它们吸引）</summary>
        private static HashSet<string> CollectVisionEnemies(BattleSimState sim, Unit vanguard, TeamType myTeam)
        {
            var result = new HashSet<string>();
            int vision = BattleHeuristics.TacticalSightOf(vanguard); // cm 单源（P5）
            var vp = sim.GetPosCm(vanguard);
            foreach (var (enemyId, enemy) in BattleHeuristics.CollectVisibleEnemies(sim, myTeam, vanguard))
                if (BattleHeuristics.DistCm(vp.x, vp.y, sim.GetPosCm(enemy).x, sim.GetPosCm(enemy).y) <= vision)
                    result.Add(enemyId);
            return result;
        }

        /// <summary>支援自保扣分（F-1，cm 域）：落点距最近敌 &lt; 支援危险半径（cm）=「站敌人刀口」
        /// 扣 SupportSelfPreserveScore × 档案「自保权重」（默认 0=恒 0）；与 E-1 火线扣分正交。
        /// G-2 形态切换：返回值乘 SupportStanceOf 形态因子（保险形态 ×3 后撤位自然胜出）</summary>
        private static int SelfPreservePenalty(BattleSimState sim, Unit unit, TeamType team,
            int xCm, int zCm, UnitConfig.CompanionProfile profile)
        {
            if (profile == null || profile.自保权重 <= 0f) return 0;
            var identity = unit.GetUnitComponent<UnitIdentity>();
            if (identity == null) return 0;
            var visible = BattleHeuristics.CollectVisibleEnemies(sim, team, unit);
            int stanceNoThreat = SupportStanceOf(sim, unit, team, null);
            return SelfPreservePenaltyFrom(sim, visible, xCm, zCm, profile, stanceNoThreat);
        }

        /// <summary>同上，但可见敌集/形态由调用方预算（2026-11-07 后期卡顿 A 批：站位圆盘每格
        /// 都重扫全场+PerceivesEnemy 判定+触发时再算一次形态）</summary>
        private static int SelfPreservePenaltyFrom(BattleSimState sim, List<(string id, Unit enemy)> visible,
            int xCm, int zCm, UnitConfig.CompanionProfile profile, int stanceNoThreat)
        {
            if (profile == null || profile.自保权重 <= 0f || visible == null) return 0;
            for (int i = 0; i < visible.Count; i++)
            {
                var ep = sim.GetPosCm(visible[i].enemy);
                if (BattleHeuristics.DistCm(xCm, zCm, ep.x, ep.y) < SupportDangerRadius) // cm（P5 ×100）
                    return Mathf.RoundToInt(SupportSelfPreserveScore * profile.自保权重) * stanceNoThreat;
            }
            return 0;
        }

        /// <summary>H-2 交战就位态判定（评分器豁免/托底共用单源）：锚=真实单位（先锋/伤员——
        /// woundedIsSelf 由调用方先行排除）+触发域+未就位（当前位光环罩不到敌）+让位门控过
        ///（威胁图将死队友→救命奶让位；我方尸体在场且爆发满槽→复苏让位）。
        /// **触发域两口径（H-3）**：visionEnemies 非空=伴随语境，先锋攻击视野内敌集非空即触发；
        /// null=伤员语境，维持 H-2 交战口径（锚距最近敌 ≤ VanguardEngageRange=cm）</summary>
        private static bool ShouldTakeAuraPosition(BattleSimState sim, Unit unit, TeamType team,
            OpponentThreatModel.ThreatMap threat, UnitConfig.CompanionProfile profile, Vector2IntCm anchorPos,
            HashSet<string> visionEnemies)
        {
            int auraRadius = BattleHeuristics.AuraRadiusOf(unit);
            if (auraRadius <= 0 || profile.光环贴敌权重 <= 0f) return false;
            if (threat != null && threat.DoomedAllies != null && threat.DoomedAllies.Count > 0) return false;
            if (HasAllyCorpse(sim, team) && BurstEnergyReady(unit)) return false;
            if (visionEnemies != null)
            {
                if (visionEnemies.Count == 0) return false;
            }
            else if (NearestEnemyDistance(sim, unit, team, anchorPos.x, anchorPos.z) > VanguardEngageRange) return false; // cm（P5 ×100）
            var fromRaw = sim.GetPosCm(unit);
            var from = new Vector2IntCm(fromRaw.x, fromRaw.y);
            return EnemiesInAuraAt(sim, unit, team, from, auraRadius, visionEnemies) == 0;
        }

        // ==================== E-1 威胁消费原语（配额脑对手建模，docs/archive/33 §2.3） ====================

        /// <summary>反威胁加分：目标是敌方威胁源（EnemyThreatScore 高者）加 min(威胁值/2, 上限)——
        /// 拆火力核心优先于平打、不优先于斩杀；threat=null（伙伴脑路径）恒 0</summary>
        private static int ThreatResponseBonus(OpponentThreatModel.ThreatMap threat, string targetUnitId)
        {
            if (threat == null) return 0;
            return threat.EnemyThreatScore.TryGetValue(targetUnitId, out var t)
                ? Mathf.Min(ThreatResponseScoreCap, t / 2)
                : 0;
        }

        /// <summary>落点威胁扣分：落点在敌方预测火线格集合内=ThreatenedCellPenalty；
        /// threat=null 恒 0。扣分钳 1 由调用方处理</summary>
        private static int ThreatPenaltyAt(OpponentThreatModel.ThreatMap threat, BattleCell cell)
        {
            return threat != null && threat.ThreatenedCells.Contains(cell) ? ThreatenedCellPenalty : 0;
        }

        // ==================== E-3 估值与延续原语（伙伴脑规划层，docs/archive/33 §4） ====================

        /// <summary>防御折减（E-3 估值感知补强）：DamagePipeline 易伤乘区轻量镜像——正防御
        /// 100/(100+防御) 除法递减、负防御每点 +1% 线性增伤（与 Calculate 同公式口径，不并易伤 buff
        /// 增量——估值只需防御主区）。评分与意图声明（DeclareIntent）共用单源</summary>
        private static int MitigatedDamage(int rawDamage, int targetDefense)
        {
            float mitigation = targetDefense >= 0
                ? 100f / (100f + targetDefense)
                : 1f - targetDefense / 100f;
            return Mathf.Max(0, Mathf.RoundToInt(rawDamage * Mathf.Max(0f, mitigation)));
        }

        /// <summary>移动锚延续记录（E-3）：只记**敌方非核心**锚（支援伤员锚动态变化频繁——奶满即失效，
        /// 锁定反而滞后；敌方锚=火力目标，锁定语义成立）。我方锚/核心/取不到 id=不记录。
        /// **核心不锁（2026-10-23 实战报障修复）**：核心=J-4 每回合按「自身攻击视野内无普通敌」
        /// 重插锚序首的战略目标，非追击对象——锁核心会让单位在视野内出现更近敌后仍永久直奔核心
        /// （2026-10-23 观察局 S2 实锤：账本可见 d=3 敌+锁核心 → 仍奔核心；与 J-4 注释
        /// 「视野内有普通敌=原最近敌锚序零变化」自相矛盾）。J-4 插队已提供核心推进的每回合续位
        /// （无视野敌时恒重插，无需锁）；E-3 防多锚振荡语义仅保留给非核心敌锚——与眷属 v7
        /// （每回合纯核心锚定、无锁）对齐</summary>
        private static void RememberMoveAnchor(BattleSimState sim, Unit unit, Unit anchor, TeamType team)
        {
            if (_bidPhase) return; // I-A 申报遍：纯读申报，槽写入抑制（未获名额伙伴零脏写）
            var anchorIdentity = anchor?.GetUnitComponent<UnitIdentity>();
            if (anchorIdentity == null || anchorIdentity.Team == team) return;
            if (BattleHeuristics.IsCore(anchor)) return; // 核心不锁（2026-10-23）：J-4 按回合重插，锁它=新出现的近敌被永久无视
            if (sim.TryGetUnitId(unit, out var unitId) && sim.TryGetUnitId(anchor, out var anchorId))
                _lastMoveAnchors[unitId] = anchorId;
        }

        /// <summary>单位 id 轻取（E-3 锚延续槽键；取不到=null）</summary>
        private static string UnitIdOf(BattleSimState sim, Unit unit)
        {
            return sim.TryGetUnitId(unit, out var id) ? id : null;
        }

        /// <summary>支援形态因子（G-2 形态切换，docs/archive/35 §2）：1=前线形态（光环贴敌满效）/
        /// SupportStanceSafeFactor(3)=保险形态（贴敌分归零+自保×3 后撤）。双门控纯函数判据：
        /// ①血线门=hp% &lt; SupportStanceRetreatHpPercent(60)；②预警门（threatThreat 非 null 时）=
        /// 威胁图预期承伤 ≥ hp × SupportStanceRetreatThreatPercent(50)%——提前一回合后撤。
        /// 她（光环载体+复苏持有者）活着=复活保险与光环引擎都在——形态切换承担「前线战斗」与
        /// 「稳定复活」两诉求的动态权衡</summary>
        private static int SupportStanceOf(BattleSimState sim, Unit unit, TeamType team,
            OpponentThreatModel.ThreatMap threatThreat)
        {
            var stats = unit.GetUnitComponent<UnitStats>();
            var hpStruct = stats != null ? stats.GetStatStruct(StatType.HP) : default;
            if (hpStruct.Max <= 0) return 1;
            if (stats.HP * 100 < hpStruct.Max * SupportStanceRetreatHpPercent)
            {
                // W1 返修（2026-10-04 观察局）：threat=null（伙伴流）时血线门补近敌判定——
                // 无近敌（>InsuranceDangerRadius）+低血线=安全区随军（随军/光环照常，光环自愈回线）；
                // 有近敌+低血线=保险后撤（G-2 语义）。threat!=null（配额脑操魔神档）维持原双门
                if (threatThreat == null)
                {
                    var upCm = sim.GetPosCm(unit);
                    if (NearestEnemyDistance(sim, unit, team, upCm.x, upCm.y) > InsuranceDangerRadius) // cm（P5）
                        return 1;
                }
                return SupportStanceSafeFactor;
            }
            if (threatThreat != null
                && threatThreat.ExpectedDamageOnAlly.TryGetValue(UnitIdOf(sim, unit), out var expected)
                && expected * 100 >= stats.HP * SupportStanceRetreatThreatPercent)
                return SupportStanceSafeFactor;
            return 1;
        }

        // ==================== 支援型估值原语 ====================

        /// <summary>缺口比例最大的存活我方（HP/MaxHP 最小者，等比平局 unitId 升序）；全员满血=null</summary>
        private static Unit FindMostWoundedAlly(BattleSimState sim, Unit self, TeamType team)
        {
            Unit best = null;
            int bestRatio = int.MaxValue;
            string bestId = null;
            foreach (var kv in sim.SortedUnits())
            {
                var candidate = kv.Value;
                if (BattleSimState.IsDead(candidate)) continue;
                var identity = candidate.GetUnitComponent<UnitIdentity>();
                if (identity == null || identity.Team != team) continue;
                var stats = candidate.GetUnitComponent<UnitStats>();
                if (stats == null) continue;
                var hpStruct = stats.GetStatStruct(StatType.HP);
                if (hpStruct.Max <= 0) continue;
                int missing = hpStruct.Max - stats.HP;
                if (missing <= 0) continue; // 满血不计
                // 缺口比例（万分比）——比例口径：残血 1/3 优先于半血大量单位
                int ratio = stats.HP * 10000 / hpStruct.Max;
                if (ratio < bestRatio || (ratio == bestRatio && bestId != null
                    && string.CompareOrdinal(kv.Key, bestId) < 0))
                {
                    best = candidate;
                    bestRatio = ratio;
                    bestId = kv.Key;
                }
            }
            return best;
        }

        /// <summary>近战先锋选择（H 批伴随，docs/archive/36）：本方存活单位中「近战武器」
        /// （单手剑/双手剑/长枪/臂铠——原神口径法器/弓/枪=远程）的最近者（切比雪夫；
        /// 同距 unitId 升序——确定性铁律）。排除建筑（含协议核心）/支援型（奶妈互随=无人跟
        /// 战线）/自己。近战=武器身份判定（与体力/元能状态解耦——体力暂缺的凯亚仍是先锋，
        /// AttackRangeOf 的动态过滤不适用于身份域）；无候选=null（回落光环贴敌/锚循环）。
        /// I-C 跟随目标分配（docs/archive/37 §4）：board 非 null 时比较键前置「跟随者数升序」
        /// ——后位支援型选跟随者最少的先锋（「你跟凯亚、我跟丘丘人」的讨论分散；单先锋/
        /// 板 null=原语义零变化）</summary>
        private static Unit FindMeleeVanguard(BattleSimState sim, Unit self, TeamType team,
            TeamIntentBoard board = null)
        {
            Unit best = null;
            int bestDist = int.MaxValue;
            int bestFollowers = int.MaxValue;
            string bestId = null;
            var fromCm = sim.GetPosCm(self); // P5：连续位
            foreach (var kv in sim.SortedUnits())
            {
                var candidate = kv.Value;
                if (BattleSimState.IsDead(candidate) || ReferenceEquals(candidate, self)) continue;
                if (BattleHeuristics.IsBuilding(candidate)) continue;
                var identity = candidate.GetUnitComponent<UnitIdentity>();
                if (identity == null || identity.Team != team) continue;
                var weapon = identity.WeaponType;
                if (weapon != WeaponType.Claymore && weapon != WeaponType.Sword
                    && weapon != WeaponType.Polearm && weapon != WeaponType.Gauntlet) continue; // 近战武器域
                var candidateProfile = candidate.RawData?.行为档案;
                if (candidateProfile != null
                    && candidateProfile.候选类别 == UnitConfig.CompanionRole.Support) continue; // 支援型不充当先锋
                var pos = sim.GetPosCm(candidate);
                int dist = BattleHeuristics.DistCm(fromCm.x, fromCm.y, pos.x, pos.y); // 欧氏 cm（P5）
                int followers = board != null ? board.FollowersOf(kv.Key) : 0;
                if (followers < bestFollowers
                    || (followers == bestFollowers && dist < bestDist)
                    || (followers == bestFollowers && dist == bestDist && bestId != null
                        && string.CompareOrdinal(kv.Key, bestId) < 0))
                {
                    best = candidate;
                    bestDist = dist;
                    bestFollowers = followers;
                    bestId = kv.Key;
                }
            }
            return best;
        }

        /// <summary>本方是否有尸体（H-2 就位让位门控——尸体在场且爆发满槽时复苏候选恒优先）</summary>
        private static bool HasAllyCorpse(BattleSimState sim, TeamType team)
        {
            foreach (var kv in sim.SortedUnits())
            {
                if (!BattleSimState.IsDead(kv.Value)) continue;
                var identity = kv.Value.GetUnitComponent<UnitIdentity>();
                if (identity != null && identity.Team == team) return true;
            }
            return false;
        }

        /// <summary>爆发元能是否已满槽（H-2 就位让位门控——未满槽=复苏不可施放，就位不受让位约束）</summary>
        private static bool BurstEnergyReady(Unit unit)
        {
            var stats = unit.GetUnitComponent<UnitStats>();
            if (stats == null) return false;
            var es = stats.GetStatStruct(StatType.Energy);
            return es.Max > 0 && stats.Energy >= es.Max;
        }

        /// <summary>技能携带治疗原子的估值（支援型评分用）：按 targetFilter 估受治者集合
        /// （CasterRadiusAllies=施法者半径内我方 / AllAllies=我方全体 / 其余=施法者自身），
        /// 逐受治者算有效治疗（min(治疗量,缺口)，缺口不足半量不计——与延奏治疗档同口径）求和。
        /// 治疗=方向无关的平加（CasterRadius 以施法者位置为心，不随攻击方向变）。</summary>
        /// <summary>技能治疗原子 per-ally 明细（E-2 重构抽提：估值/意图声明共用单源）。
        /// board=null=声明端全量（DeclareIntent）；非 null=消费端扣减前位已声明治疗（治疗去重——
        /// 有效治疗对「剩余缺口」计，伤员被前位覆盖满后本发边际 0；半量门槛对剩余缺口计）</summary>
        private static IEnumerable<(string allyId, int effective)> EnumerateSkillHeals(
            BattleSimState sim, Unit caster, TeamType team, SkillConfig.SkillData data,
            TeamIntentBoard board)
        {
            if (data?.effects == null) yield break;
            var casterStats = caster.GetUnitComponent<UnitStats>();
            if (casterStats == null) yield break;
            var casterPos = sim.GetPosCm(caster); // P5：连续位

            foreach (var atom in data.effects)
            {
                if (atom.kind != SkillEffectKind.Heal) continue;
                if (atom.trigger != SkillEffectTrigger.OnCast && atom.trigger != SkillEffectTrigger.OnHit) continue;

                int radius = atom.radiusKey != SkillParamKey.None
                    ? data.GetInt(atom.radiusKey, CellMetrics.CellSizeCm) : CellMetrics.CellSizeCm; // P5：cm（与 SupportRadiusOf 同口径）

                foreach (var kv in sim.SortedUnits())
                {
                    var ally = kv.Value;
                    if (BattleSimState.IsDead(ally)) continue;
                    var identity = ally.GetUnitComponent<UnitIdentity>();
                    if (identity == null || identity.Team != team) continue;

                    switch (atom.targetFilter)
                    {
                        case SkillEffectTargetFilter.AllAllies:
                            break; // 我方全体
                        case SkillEffectTargetFilter.CasterRadiusAllies:
                        {
                            var pos = sim.GetPosCm(ally);
                            if (BattleHeuristics.DistCm(casterPos.x, casterPos.y, pos.x, pos.y) > radius)
                                continue; // 出半径（欧氏 cm——P5）
                            break;
                        }
                        default:
                            if (ally != caster) continue; // Target/Caster 等单目标档=仅施法者自身粗估
                            break;
                    }

                    var allyStats = ally.GetUnitComponent<UnitStats>();
                    if (allyStats == null) continue;
                    // 治疗换算单出口（协议核心批 2026-09-29 收口：EffectCompiler.ResolveHealAmount
                    // 同源，含受疗者治疗效率——协议核心 50%=守家续航估值同步减半，勿再手抄公式）
                    int heal = EffectCompiler.ResolveHealAmount(data, atom.paramKey, atom.value, caster, ally);
                    int missing = Math.Max(0, allyStats.GetStatStruct(StatType.HP).Max - allyStats.HP);
                    // E-2 治疗去重：剩余缺口=缺口 − 前位已声明治疗覆盖（board=null=未扣减=声明端全量）
                    int remainingMissing = board != null ? Math.Max(0, missing - board.DeclaredHeal(kv.Key)) : missing;
                    int effective = Math.Min(heal, remainingMissing);
                    if (effective * 100 < heal * HealWorthRatioPercent) continue; // 剩余缺口不足半量不占行动
                    yield return (kv.Key, effective);
                }
            }
        }

        /// <summary>技能携带治疗原子的聚合估值（支援型评分用）——E-2 起经 EnumerateSkillHeals 单源，
        /// board 非 null 时扣减前位已声明治疗（治疗去重）。F-3：threat 非 null 时逐受治者判
        /// DoomedAllies 加救命分（预治疗档——斩杀档对偶，配额脑号令同口径 RescueScore 共享单源）</summary>
        private static int EstimateSkillHealValue(BattleSimState sim, Unit caster, TeamType team,
            SkillConfig.SkillData data, TeamIntentBoard board, OpponentThreatModel.ThreatMap threat)
        {
            int total = 0;
            foreach (var heal in EnumerateSkillHeals(sim, caster, team, data, board))
            {
                total += heal.effective;
                // F-3 预治疗救命分：伤员在威胁图 DoomedAllies（敌方预测合计承伤 ≥ 当前 hp=将被
                // 集火致死）→ 该治疗的边际价值=保命档（+RescueScore）
                if (threat != null && threat.DoomedAllies.Contains(heal.allyId))
                    total += RescueScore;
            }
            return total;
        }

        // ==================== 足迹与工具 ====================

        /// <summary>单位级行动的体力层级消耗（伙伴体力预留口径）：Move=移动技能 costs；
        /// Skill=技能 costs；Stamina 条目按施法者层级换算（眷属 0/伙伴 5/魔神 10）；
        /// 2026-10-12 拍板伙伴技能解锁：玩家直接操控伙伴=体力减免 5（可减免为负数=获得体力）</summary>
        private static int ActionStaminaCost(BattleSimState sim, ActionData action, Unit actor)
        {
            List<SkillCostEntry> costs = null;
            if (action.actionType == ActionType.Move)
            {
                costs = MoveExecutor.GetMoveCosts(actor);
            }
            else if (action.actionType == ActionType.Skill)
            {
                var skill = action.skillIndex >= 0 && action.skillIndex < actor.Skills.Count
                    ? actor.Skills[action.skillIndex]
                    : null;
                costs = skill?.RawData?.costs;
            }
            if (costs == null) return 0;
            foreach (var cost in costs)
            {
                if (cost != null && cost.kind == CostKind.Stamina && cost.amount > 0)
                {
                    var tier = BattleHeuristics.TierOf(actor);
                    int tierCost = UnitTierHelper.StaminaCostOf(tier);
                    // 玩家直接操控伙伴=体力减免 5（2026-10-12 拍板，可减免为负数=获得体力）
                    if (action.isPlayerDirectControl && tier == UnitTier.Companion)
                        return tierCost - 5;
                    return tierCost;
                }
            }
            return 0;
        }

        private static ActionData Skill(string playerId, Unit unit, int skillIndex, Direction2D direction,
            int turn, string targetUnitId = null)
        {
            return new ActionData
            {
                playerId = playerId,
                unitId = unit.GetUnitComponent<UnitIdentity>()?.UnitID,
                actionType = ActionType.Skill,
                skillIndex = skillIndex,
                direction = direction,
                targetUnitId = targetUnitId,
                turnNumber = turn,
            };
        }

        /// <summary>P5 任意角技能行动构造（aimX/aimZ 千分向量直交）</summary>
        private static ActionData SkillAim(string playerId, Unit unit, int skillIndex, int aimX, int aimZ,
            int turn, string targetUnitId = null)
        {
            return new ActionData
            {
                playerId = playerId,
                unitId = unit.GetUnitComponent<UnitIdentity>()?.UnitID,
                actionType = ActionType.Skill,
                skillIndex = skillIndex,
                aimX = aimX,
                aimZ = aimZ,
                targetUnitId = targetUnitId,
                turnNumber = turn,
            };
        }

        /// <summary>P5 移动行动构造（targetX/targetZ 直交——「选目标点」语义）</summary>
        private static ActionData Move(string playerId, Unit unit, Vector2IntCm dest, int turn)
        {
            return new ActionData
            {
                playerId = playerId,
                unitId = unit.GetUnitComponent<UnitIdentity>()?.UnitID,
                actionType = ActionType.Move,
                targetX = dest.x,
                targetZ = dest.z,
                turnNumber = turn,
            };
        }

        /// <summary>移动行动的声明落点（I-D 移动意图占位）：P5 targetX/targetZ 直读；旧格式
        ///（direction+magnitude——玩家旧端兼容）按 Host Move 分支同式桥格心</summary>
        private static BattleCell DeclaredLandingCell(BattleSimState sim, ActionData action, Unit actor)
        {
            if (action.targetX > 0 || action.targetZ > 0)
                return BattleCell.FromCm(action.targetX, action.targetZ);
            var delta = SkillHitResolver.DirectionToDelta(action.direction);
            var from = BattleCell.FromCm(sim.GetPosCm(actor).x, sim.GetPosCm(actor).y);
            int mag = Math.Max(0, action.moveMagnitude);
            return new BattleCell(from.x + delta.x * mag, from.y + delta.y * mag);
        }


        /// <summary>
        /// 本方意图板（E-2 协调层，docs/archive/33 §3）：伙伴决策序内（unitId 升序）前位伙伴已定
        /// 行动的声明载体——与 staminaReserve/committed 虚拟池同构的「决策序内登记-消费」模式。
        /// 后位伙伴评分时消费：伤害溢出去重（DeclaredDamageOnEnemy）/治疗去重（DeclaredHealOnAlly）/
        /// 集火跟随（FocusedEnemies）。配额脑不使用（每回合仅 1 配额行动，无跨单位声明域）。
        /// 声明与消费同基于决策快照（DecideAll 头 TakeSnapshot 单源），伤害/治疗均为估值口径
        /// （与评分同源，非结算精确值）——结算偏差由下回合重决策自然吸收。决策序铁律不变=确定性零损。
        /// </summary>
        public sealed class TeamIntentBoard
        {
            /// <summary>我方已声明对敌伤害和（敌方 unitId → 估伤累计——溢出去重消费）</summary>
            public readonly Dictionary<string, int> DeclaredDamageOnEnemy = new Dictionary<string, int>();

            /// <summary>我方已声明治疗覆盖和（我方 unitId → per-ally 有效治疗累计——治疗去重消费）</summary>
            public readonly Dictionary<string, int> DeclaredHealOnAlly = new Dictionary<string, int>();

            /// <summary>已被我方声明攻击的敌方目标集（集火跟随消费——「已被集火」判定）</summary>
            public readonly HashSet<string> FocusedEnemies = new HashSet<string>();

            /// <summary>先锋跟随声明（先锋 unitId → 跟随者数——I-C 跟随目标分配消费：
            /// 后位支援型选跟随者最少的先锋=「你跟凯亚、我跟丘丘人」的讨论分散）</summary>
            public readonly Dictionary<string, int> DeclaredVanguardFollow = new Dictionary<string, int>();

            /// <summary>移动意图落点（单位 id → 决策落点——I-D 移动意图占位：伙伴移动定稿/玩家
            /// Move/DeployUnit 预载声明；后位 BFS 视作占位避让，集团推进不互撞。声明口径=乐观
            /// 落点，结算偏差保守向，下回合重算吸收）</summary>
            public readonly Dictionary<string, BattleCell> DeclaredMovement = new Dictionary<string, BattleCell>();

            /// <summary>已声明的元素附着意图（凌晨批议题⑤，WEGO 版 WorldState 顺序模拟）：敌方 unitId
            /// → 声明列表（声明者 unitId、元素 int、层数）。两路写入：①申报遍后全员 bid 附着广播
            ///（执行序支援型殿后〔I 返修 3〕的补救——芭芭拉的挂水意图在安柏决策前即可见=「讨论时
            /// 各自亮牌」）；②执行遍 DeclareIntent 落地已承诺附着（replacePriorDeclarations 覆盖该
            /// 声明者申报期旧声明=最新意图权威）。消费=反应预期分（快照附着为空时读声明附着预判
            /// 蒸发/冻结连招——ItB 先动者危险格标记/Gears Tactics WorldState 顺序模拟的映射）。
            /// 声明口径=估值（OnHit 原子×预览命中集），未获名额者声明可能落空=评分层软信号，
            /// 下回合重决策自然吸收</summary>
            public readonly Dictionary<string, List<(string declarerId, int element, int layers)>> DeclaredElementOnEnemy =
                new Dictionary<string, List<(string, int, int)>>();

            public void DeclareDamage(string enemyId, int amount)
            {
                if (amount <= 0) return;
                DeclaredDamageOnEnemy.TryGetValue(enemyId, out var cur);
                DeclaredDamageOnEnemy[enemyId] = cur + amount;
                FocusedEnemies.Add(enemyId);
            }

            public void DeclareHeal(string allyId, int amount)
            {
                if (amount <= 0) return;
                DeclaredHealOnAlly.TryGetValue(allyId, out var cur);
                DeclaredHealOnAlly[allyId] = cur + amount;
            }

            /// <summary>元素附着意图声明（议题⑤）。replacePriorDeclarations=true=执行遍落地口径：
            /// 先清该声明者全部旧声明（申报期广播→执行重定向时旧目标集作废=最新意图权威）</summary>
            public void DeclareElement(string declarerId, string enemyId, int element, int layers,
                bool replacePriorDeclarations = false)
            {
                if (string.IsNullOrEmpty(declarerId) || string.IsNullOrEmpty(enemyId) || element <= 0 || layers <= 0)
                    return;
                if (replacePriorDeclarations) ForgetElementDeclarationsOf(declarerId);
                if (!DeclaredElementOnEnemy.TryGetValue(enemyId, out var list))
                {
                    list = new List<(string, int, int)>();
                    DeclaredElementOnEnemy[enemyId] = list;
                }
                list.Add((declarerId, element, layers));
            }

            /// <summary>查某敌已被**他人**声明的附着元素（排除自身声明——自声明自消费无新信息；
            /// 无=0=Physical）。多声明取首个非自身（估值粗检，首个足用——多水叠加不改变反应类型）</summary>
            public (int element, int layers) DeclaredElementExcluding(string enemyId, string selfId)
            {
                if (enemyId == null || !DeclaredElementOnEnemy.TryGetValue(enemyId, out var list))
                    return (0, 0);
                foreach (var d in list)
                {
                    if (d.declarerId == selfId) continue;
                    return (d.element, d.layers);
                }
                return (0, 0);
            }

            /// <summary>清某声明者的全部附着声明（执行遍落地前调——申报广播的覆盖钩）</summary>
            public void ForgetElementDeclarationsOf(string declarerId)
            {
                if (string.IsNullOrEmpty(declarerId) || DeclaredElementOnEnemy.Count == 0) return;
                List<string> emptyKeys = null;
                foreach (var kv in DeclaredElementOnEnemy)
                {
                    var list = kv.Value;
                    for (int i = list.Count - 1; i >= 0; i--)
                        if (list[i].declarerId == declarerId) list.RemoveAt(i);
                    if (list.Count == 0) (emptyKeys ??= new List<string>()).Add(kv.Key);
                }
                if (emptyKeys != null)
                    foreach (var k in emptyKeys) DeclaredElementOnEnemy.Remove(k);
            }

            /// <summary>跟随名额声明（I-C）：选定伴随先锋时调用——后位支援型经 FollowersOf 感知分散</summary>
            public void DeclareFollow(string vanguardId)
            {
                if (string.IsNullOrEmpty(vanguardId)) return;
                DeclaredVanguardFollow.TryGetValue(vanguardId, out var cur);
                DeclaredVanguardFollow[vanguardId] = cur + 1;
            }

            /// <summary>移动落点声明（I-D）：key=属主单位 id（消费端排除自身声明；部署预载用
            /// "deploy:"+playerId 合成 key——部署单位不在场，任何在场伙伴都该避让）</summary>
            public void DeclareMovement(string ownerId, BattleCell cell)
            {
                if (string.IsNullOrEmpty(ownerId)) return;
                DeclaredMovement[ownerId] = cell;
            }

            /// <summary>该先锋当前跟随者数（I-C 复合键首序）</summary>
            public int FollowersOf(string vanguardId) =>
                DeclaredVanguardFollow.TryGetValue(vanguardId, out var v) ? v : 0;

            public int DeclaredDamage(string enemyId) =>
                DeclaredDamageOnEnemy.TryGetValue(enemyId, out var v) ? v : 0;

            public int DeclaredHeal(string allyId) =>
                DeclaredHealOnAlly.TryGetValue(allyId, out var v) ? v : 0;
        }

        /// <summary>候选追踪：严格大于替换（同分先到先得——枚举序即决策确定性）。
        /// 配额脑（PlayerQuotaBrain）跨单位共享一个实例取全场最优；伙伴脑每单位一个实例取该单位最优。
        /// I-A 申报遍消费 BestScore（装包价值排序输入）。
        /// All=全候选清单（AI-4 失误注入轮转法消费——学徒档评分榜取第 (turn mod N)+1 名；
        /// 伙伴脑 per-unit 实例同样记录=量小随用随弃，仅 score>0 入榜与 Best 口径一致）</summary>
        public sealed class CandidateTracker
        {
            public ActionData Best { get; private set; }
            private int _bestScore;

            /// <summary>全候选清单（分数，行动）——Offer 即录（含落选者）</summary>
            public readonly List<(int score, ActionData action)> All = new List<(int, ActionData)>();

            /// <summary>当前最优候选分数（无候选=0——I-A 申报用；执行遍勿消费）</summary>
            public int BestScore => _bestScore;

            public void Offer(int score, ActionData action)
            {
                if (action == null || score <= 0) return; // 零/负分不入池不入榜
                All.Add((score, action));
                if (score <= _bestScore) return;
                _bestScore = score;
                Best = action;
            }
        }
    }
}
