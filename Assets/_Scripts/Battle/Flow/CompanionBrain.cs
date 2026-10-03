using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 伙伴决策脑（D 批次操控分层，docs/active/32 §3/§6.1）：3-4 星伙伴单位自主决策——
    /// Host 在**收齐全部玩家选择之后、开演之前**的机器瞬时阶段跑（五阶段流程的「伙伴决策阶段」，
    /// 无选择时限、玩家无感知、HUD 不设 UI 态；与眷属决策同为 Host 内部阶段，零新协议）。
    ///
    /// 感知输入（docs/active/32 §3「意图可见性边界」）：
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
    /// </summary>
    public static class CompanionBrain
    {
        // ==================== 评分口径（共享骨架常量，勿散写调用处） ====================

        /// <summary>斩杀加成：预估伤害 ≥ 目标当前生命（保守估值，未计反应乘区）</summary>
        public const int KillBonusScore = 80;

        /// <summary>战技命中获能 +10 的评分（B6a：向爆发攒能的行动价值）</summary>
        public const int SkillHitEnergyScore = 12;

        /// <summary>移动兜底分（低于一切攻击/有效治疗候选）</summary>
        public const int MoveBaseScore = 10;

        /// <summary>移动每逼近 1 格的加分</summary>
        public const int MoveProgressScorePerCell = 2;

        /// <summary>近敌接敌加分上限（离最近敌越近越优先动该单位）</summary>
        public const int MoveEngageBonusMax = 8;

        /// <summary>走位进射击线加分（移动落点即有可开火攻击线=下回合可输出）</summary>
        public const int MoveLineUpScore = 8;

        /// <summary>治疗候选门槛：有效治疗 ≥ 治疗量一半才值得占行动（勿为挠痒花行动）</summary>
        public const int HealWorthRatioPercent = 50;

        /// <summary>单位指向型爆发：复苏候选评分（B-3 ② 芭芭拉闪耀奇迹——回一整个单位+40% 血，
        /// 价值对齐斩杀档之上：100&gt;KillBonus 80，有尸体=复苏即最优）</summary>
        public const int UnitTargetBurstReviveScore = 100;

        /// <summary>单位指向型爆发：增益候选评分（歌声之环永久光环——中等偏高：低于斩杀档、
        /// 高于常规攻击均值，攒满即放勿囤积；调手感改此常量）</summary>
        public const int UnitTargetBurstBuffScore = 45;

        /// <summary>无目标自施放爆发：自身增益候选评分（aimMode=None——首个=凯亚凛冽轮舞寒冰之棱：
        /// 持续冰伤+碎裂回血复合价值，对齐单位指向增益档；攒满即放勿囤积——能量满后继续获取即浪费；
        /// 调手感改此常量）</summary>
        public const int SelfCastBurstBuffScore = 45;

        /// <summary>反威胁分上限（E-1 配额脑对手建模，docs/active/33 §2.3）：攻击候选目标是敌方
        /// 威胁源（威胁图 EnemyThreatScore 高者）时加分——优先拆除敌方火力核心；上限低于斩杀 80
        /// （反威胁优先于平打、不优先于斩杀）。伙伴脑 threat=null 不消费=零行为变化</summary>
        public const int ThreatResponseScoreCap = 25;

        /// <summary>落点受威胁扣分（E-1）：候选落点在敌方预测火线格集合内=下回合白挨打，避险
        /// 走位；与走位进射击线加分（8）同量级正负抵偿——有开火线但在火线上=平手倾向、纯避险
        /// 换位为负倾向。扣后钳 1 保底 Offer（危险落点排后不缺席——挂机防线语义）</summary>
        public const int ThreatenedCellPenalty = 12;

        /// <summary>集火每档加分（E-2 协调层，docs/active/33 §3）：行为档案「集火权重」1 档=+3 分——
        /// 量级低于战技获能（12）与斩杀（80）：集火倾向只影响同分域内的目标选择，不压过斩杀/反应
        /// 等强信号；权重 0（默认）=游走型不消费</summary>
        public const int FocusFireScorePerRank = 3;

        /// <summary>支援自保危险半径（F-1，docs/active/34 §5.3）：落点距最近敌的切比雪夫距离
        /// 小于此值=「站敌人刀口」——支援型自保负分生效阈值</summary>
        public const int SupportDangerRadius = 2;

        /// <summary>光环敌覆盖每敌加分（G-1 光环位置价值，docs/active/35）：持有半径型 tick 光环
        /// 时落点光环半径内每敌 +6——量级对齐走位线分 8（3 敌=18 显著牵引）：光环挂水/挂冰引擎
        /// 的贴敌驱动力；乘档案「光环贴敌权重」（0=默认不消费）与形态因子（保险形态归零 G-2）</summary>
        public const int SupportAuraPerEnemy = 6;

        /// <summary>支援形态切换血线门（G-2，docs/active/35 §2）：hp% 低于此值=保险形态
        ///（贴敌分归零+自保×SupportStanceSafeFactor 后撤）——她活着=复活保险在（复苏无限程，
        /// 生存问题非站位问题）；光环 tick 含持有者自奶→奶回线上自动重返前线</summary>
        public const int SupportStanceRetreatHpPercent = 60;

        /// <summary>支援形态切换预警门（G-2）：威胁图预期承伤 ≥ hp×此百分比=保险形态——
        /// 提前一回合后撤（不等到掉血才反应；E-1 威胁图基建复用零新感知；threat=null 时仅血线门）</summary>
        public const int SupportStanceRetreatThreatPercent = 50;

        /// <summary>保险形态自保倍率（G-2）：自保负分 ×3=贴敌格扣 45 分——后撤位（远离敌仍在
        /// 奶程内）自然胜出</summary>
        public const int SupportStanceSafeFactor = 3;

        /// <summary>光环前瞻基准距离（G-5 光环前瞻梯度，docs/active/35 §2 返修——远距趋近驱动力）：
        /// 距最近敌此距离处=0 分起点，每近 1 格 +SupportAuraApproachPerStep，上限 Cap。
        /// **2026-10-03 验证局调参 8→14**：开局敌距 9~11 格、旧值 8=零牵引（她缩角到敌自己走过来）；
        /// 14 覆盖测试军地图对角开局距</summary>
        public const int SupportAuraApproachBase = 14;

        /// <summary>光环前瞻每步分（G-5）：每近敌 1 格 +3（权重 1 档）——**验证局调参 2→3**：
        /// 4 格敌距=+12 恰好对冲火线扣分 12（站位评分器内支援型火线减半后=净正 +6）、6 格=+6
        /// 净正——趋敌梯度在前线形态全程为正</summary>
        public const int SupportAuraApproachPerStep = 3;

        /// <summary>光环前瞻上限（G-5）：+12=对齐「光环内 2 敌」满档——近身光环实分接管后前瞻失效
        ///（enemiesInAura>0 时不再叠加前瞻，防双计）</summary>
        public const int SupportAuraApproachCap = 12;

        /// <summary>支援站位火线扣分减半系数（G-5 验证局调参）：站位评分器内火线扣分 ×50%——
        /// 支援型前线形态「接受中等风险换贴敌挂水」（整列火线吓退支援型=缩角实证）；E-1 配额脑
        /// 原路径全额不动（输出型零回归）</summary>
        public const float SupportThreatPenaltyScale = 0.5f;

        /// <summary>保险形态后撤梯度（G-5 验证局调参——保险形态零移动驱动返修）：远离最近敌每格
        /// +2（上限 12）——旧版保险形态贴敌归零+前瞻归零后 2~11 格区间无任何驱动=驻位挨打；
        /// 后撤梯度给她方向性（撤到我方阵内/敌方射程外）</summary>
        public const int SupportRetreatPerStep = 2;

        /// <summary>保险形态后撤梯度上限（G-5）</summary>
        public const int SupportRetreatCap = 12;

        /// <summary>支援自保基准扣分（F-1）：量级压过移动兜底（10）+走位进射击线（8）——有更远
        /// 候选时支援型不选贴敌落点；乘档案「自保权重」（0=默认不消费，1=标准）</summary>
        public const int SupportSelfPreserveScore = 15;

        /// <summary>支援奶程内站位基础分（F-2 支援站位评分器，docs/active/34 §4）：落点距伤员
        /// ≤ 治疗半径=「能奶到」满档；量级对齐攻击候选均值（水球伤害 20+获能 12）——奶程内站位
        /// 优先于平打，攻击候选在站位达成后自然由 healValue 估值接手</summary>
        public const int SupportHealReachScore = 20;

        /// <summary>支援奶程外每格衰减（F-2）：超出治疗半径每远 1 格 -6（约 3 格外归 0）——
        /// 「走近伤员」的连续梯度，非硬门槛</summary>
        public const int SupportHealReachDecayPerCell = 6;

        /// <summary>救命分（F-3 预治疗档，docs/active/34 §4——上提共享单源：配额脑号令原独立常量
        /// EnsoRescueScore 上提后两脑同口径防漂移）：治疗行动覆盖「威胁图 DoomedAllies」（敌方预测
        /// 合计承伤 ≥ 当前 hp=将被集火致死）的我方单位 → 治疗估值 +30——斩杀档（80）的对偶、
        /// 量级低于斩杀（保命优先于平奶/平打，不优先于斩杀线候选）</summary>
        public const int RescueScore = 30;

        /// <summary>中性档案（无配置/魔神档兜底：全 1 权重=评分制 v2 原口径）</summary>
        private static readonly UnitConfig.CompanionProfile NeutralProfile = new UnitConfig.CompanionProfile();

        /// <summary>移动锚延续槽（E-3 意图延续槽，docs/active/33 §4）：unitId → 上回合移动锚 unitId。
        /// static 生命周期=进程——DecideAll 里 turn≤1 自清（每次 StartBattle 恒从回合 1 起=新战斗
        /// 自动清零，TurnFlow 零接线）；记录=评分期锚循环现场（RememberMoveAnchor 只记敌方锚）；
        /// 消费=ScoreMoveCandidate 锚序锁定插队（CR 式 target lock，治多锚贪心抖动）</summary>
        private static readonly Dictionary<string, string> _lastMoveAnchors = new Dictionary<string, string>();

        // ==================== 决策主入口（伙伴决策阶段） ====================

        /// <summary>
        /// 全部伙伴单位的本回合自主行动（存活的；不可动/已被号令/无好行动=缺席）。
        /// playerActions=各玩家已提交选择（Host 收齐后传入——号令跳过+体力预留的感知源）。
        /// </summary>
        public static List<ActionData> DecideAll(BattleSimState sim, int turnNumber, List<ActionData> playerActions)
        {
            var actions = new List<ActionData>();
            var snapshot = sim.TakeSnapshot(turnNumber);
            if (turnNumber <= 1)
                _lastMoveAnchors.Clear(); // E-3：新战斗自清（StartBattle 恒从回合 1 起）

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

            // 已定伙伴消耗（决策序内虚拟扣减——同池多伙伴不超额承诺）
            var committed = new Dictionary<string, int>();

            // E-2 本方意图板（决策序内登记-消费，docs/active/33 §3）：前位伙伴已定行动入板，后位评分
            // 消费（伤害溢出去重/治疗去重/集火跟随）——与 committed 虚拟池同构；配额脑无此机制
            //（每回合仅 1 配额行动，无跨单位声明域）
            var board = new TeamIntentBoard();

            // F-1 威胁图接伙伴脑（兑现 E 批拍板 #4，范围=支援型，docs/active/34 §5.2）：检出支援型
            // 在场才构建（每回合至多一次——无支援型零成本；输出型 threat=null 路径恒不变=复测基线）。
            // 支援型消费=既有 E-1 评分点自动生效（移动落点避火线/攻击反威胁）——Ellie 站位哲学的
            // 「不站刀口」维度（Gears Tactics WorldState 同语义的伙伴侧补全）
            OpponentThreatModel.ThreatMap threat = null;
            foreach (var kv0 in sim.Units)
            {
                var u0 = kv0.Value;
                if (!BattleHeuristics.IsCompanion(u0) || BattleSimState.IsDead(u0)) continue;
                if (u0.RawData?.行为档案?.候选类别 != UnitConfig.CompanionRole.Support) continue;
                threat = OpponentThreatModel.Build(sim, snapshot, sim.GetTeamOf(
                    u0.GetUnitComponent<UnitIdentity>()?.OwnerPlayerID), turnNumber);
                break; // 支援型威胁图为全队共享（Build 不分我方玩家——敌方威胁对我队全体一致）
            }

            foreach (var kv in sim.Units)
            {
                var unit = kv.Value;
                if (BattleHeuristics.IsBuilding(unit)) continue; // 建筑不参与任何行动（协议核心批单一判据）
                if (!BattleHeuristics.IsCompanion(unit)) continue;
                if (BattleSimState.IsDead(unit) || !BattleSimState.CanAct(unit)) continue;
                var identity = unit.GetUnitComponent<UnitIdentity>();
                if (identity == null) continue;
                if (commanded.Contains(kv.Key)) continue; // 已被号令：跳过自主决策（本回合行动=号令技能）

                // 虚拟体力池：实池 − 己方已提交行动预留 − 决策序内已定伙伴消耗
                int reserved = (staminaReserve.TryGetValue(identity.OwnerPlayerID, out var r2) ? r2 : 0)
                              + (committed.TryGetValue(identity.OwnerPlayerID, out var c) ? c : 0);
                int? pool = reserved > 0
                    ? Math.Max(0, sim.GetStamina(identity.OwnerPlayerID) - reserved)
                    : (int?)null;

                var tracker = new CandidateTracker();
                var profile = unit.RawData?.行为档案;
                // F-1：支援型传威胁图（避火线/反威胁自动生效）；输出型恒 null（零回归基线）
                var unitThreat = profile != null && profile.候选类别 == UnitConfig.CompanionRole.Support
                    ? threat
                    : null;
                ScoreUnitCandidates(sim, snapshot, unit, identity.OwnerPlayerID, identity.Team,
                    turnNumber, tracker, pool, profile, unitThreat, board);

                if (tracker.Best != null)
                {
                    DeclareIntent(sim, snapshot, unit, identity.Team, tracker.Best, board); // E-2：意图入板
                    actions.Add(tracker.Best);
                    int cost = ActionStaminaCost(sim, tracker.Best, unit);
                    if (cost > 0)
                        committed[identity.OwnerPlayerID] =
                            (committed.TryGetValue(identity.OwnerPlayerID, out var c2) ? c2 : 0) + cost;
                }
            }
            return actions;
        }

        /// <summary>行动意图入板（E-2，docs/active/33 §3）：对已定行动按决策快照反推声明面——
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
                    var from = sim.GetPosition(unit);
                    var targets = BattleHeuristics.PreviewLineTargets(sim, snapshot, team, from, data, action.direction);
                    foreach (var target in targets)
                        board.DeclareDamage(target.unitId, MitigatedDamage(perTarget, target.defense)); // E-3：折减口径与评分同源（声明不过称高防敌足杀）
                }
            }

            foreach (var heal in EnumerateSkillHeals(sim, unit, team, data, null))
                board.DeclareHeal(heal.allyId, heal.effective);
        }

        // ==================== 共享评分骨架（伙伴脑+配额脑操魔神档复用） ====================

        /// <summary>
        /// 单位级行动候选评分（评分制 v2 骨架收口）：①攻击技能（战技/爆发）——CanCast+costs 门槛
        /// （体力按层级换算；决策期虚拟池透传）、十字四向逐向预判（与 HUD 瞄准推荐/结算形态同源）、
        /// 逐目标有效伤害（过杀截断）+斩杀加成+战技命中获能；②移动——十字逼近锚点（输出型=最近敌，
        /// 支援型=最缺血我方）、步数逼近到偏好交战距离止步（非无脑贴脸）、走位进射击线加分。
        /// 每维度乘 UnitConfig.行为档案权重（profile null=中性=评分制 v2 原口径）。
        /// 支援型骨架多一个候选类别：技能携带治疗原子时估有效治疗并入评分（估「奶谁」而非只「打谁」）。
        /// E-1 对手建模（docs/active/33 §2.3）：threat=威胁图（配额脑操魔神档传——攻击候选吃反威胁分、
        /// 移动候选落点吃避险扣分；伙伴脑不传=null 零行为变化——拍板 #4 威胁图暂只接配额脑）。
        /// </summary>
        public static void ScoreUnitCandidates(BattleSimState sim, BattleSnapshot snapshot, Unit unit,
            string playerId, TeamType team, int turn, CandidateTracker tracker,
            int? staminaPoolOverride, UnitConfig.CompanionProfile profile,
            OpponentThreatModel.ThreatMap threat = null, TeamIntentBoard board = null)
        {
            if (unit == null || tracker == null) return;
            profile ??= NeutralProfile;
            // 攻击档先跑并回报「本单位是否有攻击/增益候选」（2026-10-02 挂机修复：驻位判定用
            // per-unit 候选存在性——配额脑共享 tracker，勿用 tracker.Best 判定〔那是全场最优〕）
            bool hasActionCandidate = ScoreAttackCandidates(sim, snapshot, unit, playerId, team, turn,
                tracker, staminaPoolOverride, profile, threat, board);
            ScoreMoveCandidate(sim, snapshot, unit, playerId, team, turn, tracker, staminaPoolOverride,
                profile, hasActionCandidate, threat);
        }

        /// <summary>①攻击技能候选（战技/爆发）：预判与结算形态同源（WouldHitEnemyInDirection）；评分=逐目标
        /// 有效伤害+斩杀×激进度+战技获能；乘技能类型优先权重；支援型并入治疗原子估值。
        /// 返回=本单位是否存在任何攻击/增益候选（Offer 调用即算——共享 tracker 下被更优候选
        /// 压制不等于无候选；挂机修复的驻位判定消费此值）</summary>
        private static bool ScoreAttackCandidates(BattleSimState sim, BattleSnapshot snapshot, Unit unit,
            string playerId, TeamType team, int turn, CandidateTracker tracker,
            int? staminaPoolOverride, UnitConfig.CompanionProfile profile,
            OpponentThreatModel.ThreatMap threat, TeamIntentBoard board)
        {
            var skills = unit.Skills;
            var from = sim.GetPosition(unit);
            bool offeredAny = false; // 本单位任一技能产生过候选（Offer 调用即算——驻位判定消费）

            for (int i = 0; i < skills.Count; i++)
            {
                var skill = skills[i];
                var data = skill?.RawData;
                if (data == null) continue;
                // F-3 技能维度集（docs/active/34 §4）：门槛扩 Henka（变奏技能接自主评估域——
                // 2026-10-03 拍板「Henka 治疗类接自主域」；Enso/Contract=玩家域仍剥离不含）
                if (data.skillType != SkillType.Normal && data.skillType != SkillType.Burst
                    && data.skillType != SkillType.Henka) continue;
                if (!skill.CanCast(unit)) continue; // 占位技能跳过——凛冽轮舞/闪耀奇迹未实装不可施放

                // 消耗门槛（统一消耗模型 C-2）：costs 全条目镜像 Host 判定（体力按施法者层级换算；
                // 决策期虚拟池透传——伙伴决策不超额承诺）
                if (!ResourceGate.HasAll(sim, unit, playerId, data.costs, out _, staminaPoolOverride)) continue;

                // 单位指向型爆发（B-3 ② 首个=芭芭拉闪耀奇迹）：无方向域——目标估值档（复苏优先/增益次之）
                if (data.IsUnitTargeted())
                {
                    offeredAny |= ScoreUnitTargetBurst(sim, snapshot, unit, playerId, team, turn, i, data, tracker, profile);
                    continue;
                }

                // 无目标自施放爆发（aimMode=None——凛冽轮舞）：无方向域/无目标域——自身增益估值档
                if (data.IsSelfCast())
                {
                    offeredAny |= ScoreSelfCastBurst(unit, playerId, turn, i, data, tracker, profile);
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

                foreach (var direction in BattleHeuristics.CrossDirections)
                {
                    if (!skill.WouldHitEnemyInDirection(sim.Map, snapshot, team, from, direction))
                        continue;
                    var targets = BattleHeuristics.PreviewLineTargets(sim, snapshot, team, from, data, direction);
                    if (targets.Count == 0 && healValue <= 0) continue; // 纯尸体线：不浪费行动

                    int score = 0;
                    foreach (var target in targets)
                    {
                        // E-2 伤害溢出去重（docs/active/33 §3）：前位已声明伤害覆盖后的剩余有效血量——
                        // remaining ≤ 0 =前位足杀，本发边际价值 0（评分机制自然转向次优目标，勿显式
                        // 降档）；斩杀判定同步剩余口径（击杀已被前位拿走不重复计）；board=null
                        //（配额脑路径）时 remaining=target.hp 与原逻辑恒等
                        int remaining = target.hp - (board?.DeclaredDamage(target.unitId) ?? 0);
                        if (remaining > 0)
                        {
                            // E-3 防御折减（估值感知补强，docs/active/33 §4）：DamagePipeline 易伤乘区
                            // 轻量镜像——修「对高防目标伤害虚高」盲区（骨架级修正：伙伴脑与配额脑操
                            // 魔神档共享受益；眷属脑不消费估值零影响）
                            int effPerTarget = MitigatedDamage(perTarget, target.defense);
                            score += Math.Min(effPerTarget, remaining); // 过杀截断（E-2 剩余口径+E-3 折减口径）
                            if (effPerTarget >= remaining)
                                score += Mathf.RoundToInt(KillBonusScore * profile.激进度);
                            // E-3 反应预期分：快照附着×技能元素经 ElementReactionResolver.Preview 单源
                            // 预判——增伤反应（蒸发/融化 1 级 +50% 伤害乘区）按折减伤害一半计；冻结
                            // 控制反应无增伤乘区不计分。跨单位连招基础：凯亚挂冰→安柏跟火自动蒸发
                            var reaction = ElementReactionResolver.Preview(
                                (ElementType)target.dyedElement, skillElement);
                            if (reaction.HasReaction
                                && reaction.DamageBonusDelta + reaction.VulnerabilityBonus > 0)
                                score += effPerTarget / 2;
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

                    tracker.Offer(score, Skill(playerId, unit, i, direction, turn));
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
            if (data.effects != null)
                foreach (var atom in data.effects)
                    if (atom.kind == SkillEffectKind.ApplyBuff
                        && atom.condition == SkillEffectCondition.TargetIsAlive)
                    {
                        grantBuffType = (int)atom.buffType;
                        break;
                    }
            if (grantBuffType < 0) return false;

            // 增益候选：最缺血的未满层存活我方（含施法者自身；等比平局 unitId 升序）——
            // 满层判定走活体 BaseBuff.IsAtStackCap（快照 BuffState 无层数/上限字段）
            UnitState best = null;
            int bestRatio = int.MaxValue;
            string bestId = null;
            foreach (var u in snapshot.units)
            {
                if ((TeamType)u.team != team || u.isCorpse != 0) continue;
                var live = sim.GetUnit(u.unitId);
                var existingBuff = live?.Buffs.Find(b => b != null && b.Type == (BuffType)grantBuffType);
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
        /// 返回=是否产生候选（挂机修复的驻位判定消费）</summary>
        private static bool ScoreSelfCastBurst(Unit unit, string playerId, int turn, int skillIndex,
            SkillConfig.SkillData data, CandidateTracker tracker, UnitConfig.CompanionProfile profile)
        {
            if (data.effects == null) return false;
            foreach (var atom in data.effects)
            {
                if (atom == null || atom.trigger != SkillEffectTrigger.OnCast) continue;
                if (atom.kind != SkillEffectKind.ApplyBuff || atom.targetFilter != SkillEffectTargetFilter.Caster) continue;
                var existing = unit.Buffs.Find(b => b != null && b.Type == atom.buffType);
                if (existing != null && existing.IsAtStackCap()) return false; // 已满层：重施加无增益不占行动
                tracker.Offer(Mathf.RoundToInt(SelfCastBurstBuffScore * profile.爆发优先权重),
                    Skill(playerId, unit, skillIndex, Direction2D.Up, turn));
                return true;
            }
            return false;
        }

        /// <summary>②移动候选（2026-09-29 报障返修「单位堆积湖边试图走又被弹回」）：锚点=距离升序
        /// 取**首个可达成**目标（支援型先试最缺血我方锚）——最近敌常悬湖上（飞行单位），其十字四邻
        /// 全水=BFS 目标集空，旧直行逼近=每回合原地弹回白耗体力（现场取证：凯亚/芭芭拉 Up×3 撞
        /// (7,5) 水格弹回）；方向=BFS 最短路首步（FindApproachFirstStep，眷属 v3/v4 同款——绕湖/
        /// 绕虚空拐弯），步数三重钳=偏好交战距离余量 × 移速上限 × 该向地形可行程（移动是推力直线
        /// 语义，转向留给下一回合重算=BFS 取直线段）；已在偏好距离内**且有攻击/增益候选**=驻位即
        /// 最优位不再逼近；无候选≠驻位最优——对角错位等十字线打不到的站位会永久挂机
        /// （2026-10-02 报障「伙伴安柏击杀敌方安柏后挂机不攻城」），降级=对齐走位（1 格进射击线），
        /// **对齐失败换下一锚继续试**（同日二轮报障「连续 3 回合挂机」现场实证：首锚恰在偏好距离
        /// 边界且四向都换不来开火线，直接 return=永久挂机——第二锚逼近步才是出口）。
        /// 评分=兜底+逼近进度+接敌×激进度+走位进射击线，乘移动优先权重。
        /// hasActionCandidate=攻击档回报的本单位候选存在性（**勿改用 tracker.Best**——配额脑共享
        /// tracker 跨单位取全场最优，Best 非空≠本单位有行动）</summary>
        private static void ScoreMoveCandidate(BattleSimState sim, BattleSnapshot snapshot, Unit unit,
            string playerId, TeamType team, int turn, CandidateTracker tracker,
            int? staminaPoolOverride, UnitConfig.CompanionProfile profile, bool hasActionCandidate,
            OpponentThreatModel.ThreatMap threat)
        {
            if (!ResourceGate.HasAll(sim, unit, playerId, MoveExecutor.GetMoveCosts(unit), out _, staminaPoolOverride))
            {
                return; // 移动消耗（C-2 costs 单源；体力按层级换算+虚拟池）
            }

            var from = sim.GetPosition(unit);
            var forceType = unit.GetUnitComponent<UnitMoveable>()?.NormalMoveType ?? ForceType.Walk;
            int maxMove = MoveExecutor.MaxMoveDistance(unit);

            // 驻位基准（CR-Move，2026-10-02 拍板「像皇室战争那样」）：偏好交战距离>0=手动覆写
            // 微调手感；0=**自动=攻击射程单源**（BattleHeuristics.AttackRangeOf——皇室战争
            /// 「进攻击范围即停」的回合制等价：安柏箭矢/箭雨未配 clip 射程=24 全图狙击=射程内
            // 恒驻位只换线、凯亚霜袭 2=逼近到 2 格外停、芭芭拉水球 5=5 格外停）
            int engageDistance = profile.偏好交战距离 > 0
                ? profile.偏好交战距离
                : BattleHeuristics.AttackRangeOf(sim, unit, playerId);

            // F-2 支援站位评分器（docs/active/34 §4，Ellie 式候选格多维评分）：支援型+伤员存在时
            // **全接管移动档**——取代朝单锚 BFS 直冲的「位置=选格」语义：候选=十字射线直线可达格
            // （移动=推力直线，BFS 拐弯格不可达——与现有直线段钳制同构）∪当前格，多维评分选最优
            // 站位（奶程×开火线×火线规避×自保）。最优=当前格 → 无移动候选（驻位语义内化）。
            // G-5 修 3（docs/active/35 §4——三态接管，光环引擎完整形态）：支援型站位评分器接管态
            // 扩为三态——①伤员锚（原语义）②**光环贴敌模式**（无伤员但持有光环且档案「光环贴敌
            // 权重」>0：woundedPos=自身=奶程恒满〔woundedIsSelf〕，光环前瞻分主导趋敌——治「开局
            // 无伤员阶段走锚循环、光环驱动不参与决策」的接管错位）③保险后撤模式（血线/威胁预警：
            // 贴敌归零+后撤梯度主导）。输出型路径零触碰（零回归基线）。
            if (profile.候选类别 == UnitConfig.CompanionRole.Support
                && profile.目标偏好 == UnitConfig.CompanionTargetPreference.MostWoundedAlly)
            {
                var wounded = FindMostWoundedAlly(sim, unit, team);
                if (wounded == null
                    && BattleHeuristics.AuraRadiusOf(unit) > 0
                    && profile.光环贴敌权重 > 0f)
                    wounded = unit; // G-5 修 3：光环贴敌模式（自体假锚——奶程恒满，光环前瞻主导）
                if (wounded == null
                    && SupportStanceOf(sim, unit, team, threat) > 1)
                    wounded = unit; // G-5 保险后撤模式：自体假锚（贴敌归零+后撤梯度主导）
                if (wounded != null)
                {
                    TryOfferSupportPosition(sim, snapshot, unit, playerId, team, turn, from,
                        maxMove, forceType, wounded, threat, tracker, profile);
                    return; // 站位评分器已给全档候选（或最优=当前格），锚循环不再参与
                }
            }

            // 锚序：支援型先试最缺血我方（奶谁/去哪护），全军敌迭代殿后。
            // E-3 移动锚延续（意图延续槽，docs/active/33 §4）：上回合锁定的敌**插队敌序首**=
            // CR 式 target lock——锁定敌在死亡/不可达前不被「最近敌」的边界震荡抢走（治多锚
            // 贪心抖动：朝敌1走→敌2 变近改道→又回敌1 的来回震荡）；锚死=GetUnit null/IsDead
            // 不插队自然回落距离序、不可达=BFS 0 换下一锚（自动解锁）；支援伤员锚动态变化频繁
            // （奶满即失效）不延续——只锁敌方锚
            var enemyAnchors = BattleHeuristics.FindEnemiesByDistance(sim, unit);
            if (_lastMoveAnchors.TryGetValue(UnitIdOf(sim, unit), out var lockedAnchorId)
                && !string.IsNullOrEmpty(lockedAnchorId))
            {
                var locked = sim.GetUnit(lockedAnchorId);
                if (locked != null && !BattleSimState.IsDead(locked) && enemyAnchors.Remove(locked))
                    enemyAnchors.Insert(0, locked); // 锁定插队敌序首（不在敌列表=同队错配，自然回落）
            }
            var anchors = new List<Unit>();
            if (profile.候选类别 == UnitConfig.CompanionRole.Support
                && profile.目标偏好 == UnitConfig.CompanionTargetPreference.MostWoundedAlly)
            {
                var wounded = FindMostWoundedAlly(sim, unit, team);
                if (wounded != null) anchors.Add(wounded);
            }
            anchors.AddRange(enemyAnchors);

            foreach (var anchor in anchors)
            {
                var to = sim.GetPosition(anchor);
                int dx = to.x - from.x;
                int dy = to.y - from.y;
                if (dx == 0 && dy == 0) continue; // 同格堆叠：无逼近意义
                int distance = Math.Max(Math.Abs(dx), Math.Abs(dy));

                // BFS 最短路首步（0=该锚不可达〔四邻不可进入或真无路〕→ 换下一锚——最近可达优先，
                // 眷属 v4 换目标巡逻同语义）
                var direction = BattleHeuristics.FindApproachFirstStep(sim, unit, to);
                if (direction == 0) continue;
                // F-1 支援锚驻位分流（docs/active/34 §5.1）：锚的敌我决定距离语义——
                // 伤员锚（我方）=治疗半径（SupportRadiusOf 自动提取技能集最大治疗原子半径；档案
                // 「支援贴近距离」>0 覆写）=贴身奶；敌锚=攻击射程 engageDistance（CR-Move 语义
                // 不变）。治「驻位 5 格外治疗半径 1 空放」错配——Ellie 第一性原则：支援者贴着
                // 被保护对象（她的失误才被玩家归因于自己）
                var anchorIdentity = anchor.GetUnitComponent<UnitIdentity>();
                bool isAllyAnchor = anchorIdentity != null && anchorIdentity.Team == team;
                int anchorEngage = isAllyAnchor
                    ? (profile.支援贴近距离 > 0
                        ? profile.支援贴近距离
                        : BattleHeuristics.SupportRadiusOf(unit))
                    : engageDistance;

                // 步数三重钳：驻位距离余量 → 移速上限 → 该向地形可行程（BFS 只保证首步，直线段
                // 可能中途遇湖——按地形截断；被单位挡由 MovementResolver 停格前=预期部分行进）
                int steps = Math.Min(Math.Max(0, distance - anchorEngage), maxMove);
                if (steps <= 0)
                {
                    // 已在驻位距离内：有攻击/增益候选=驻位即最优位，不换锚逼近（原语义）
                    if (hasActionCandidate) return;
                    // ① 轴对齐直飞（2026-10-02 拍板「根据当前目标的位置直接尽可能飞过去」）：射程内
                    //    无线=不在目标行/列——沿轴直飞对齐（对满轴=落点在目标行/列=下回合开火；
                    //    全图级射程如安柏 24=落点即线），取代 1 格试线成为主档
                    if (TryOfferAxisAlignStep(sim, snapshot, unit, playerId, team, turn, from, to,
                            maxMove, forceType, tracker, profile, threat))
                    {
                        RememberMoveAnchor(sim, unit, anchor, team); // E-3：对齐承诺=延续该锚
                        return;
                    }
                    // ② 1 格对齐探针（边缘兜底：已对轴但线被尸体/虚空截断——侧移换线；
                    //    失败勿 return=换下一锚继续试，首锚驻位无效≠全场无解）
                    if (!TryOfferAlignmentStep(sim, snapshot, unit, playerId, team, turn, from, forceType,
                        direction, tracker, profile, threat))
                        continue;
                    RememberMoveAnchor(sim, unit, anchor, team); // E-3：换线成功=延续该锚
                    return;
                }
                var step = SkillHitResolver.DirectionToDelta(direction);
                int straightRun = 0;
                for (int s = 1; s <= steps; s++)
                {
                    var cell = new BattleCell(from.x + step.x * s, from.y + step.y * s);
                    if (!sim.Map.HasTile(cell.x, cell.y) || !sim.Map.IsPassable(cell.x, cell.y, forceType)) break;
                    straightRun++;
                }
                if (straightRun <= 0) continue; // 首步即被单位占住（BFS 保守近似外的兜底）：换下一锚
                // G-5 修 1（docs/active/35 §4，§114 同族防歪）：步长钳改「BFS 最短路首段直线前缀」——
                // 治「BFS 首步×N 直线飞越路径拐点」（环湖绕行首步=Left 被直线化执行成纯西行滑边、
                // 探针实证 A 芭锁 B 芭跨湖锚+滑向 (0,3) 角落的根因）；前缀步长 ≤ 三重钳步长
                BattleHeuristics.ApproachStraightPrefix(sim, unit, to, steps, out var prefixDir, out var prefixRun);
                if (prefixRun <= 0) continue; // 路径开头即拐点（首格即距离不减）：换下一锚
                steps = Math.Min(steps, prefixRun);

                // 逼近进度：沿 BFS 方向实际位移后的切比雪夫距离缩减量（绕行段进度可为 0——
                // 兜底分仍 >0，移动候选照常参与评分）
                int projected = Math.Max(Math.Abs(dx - step.x * steps), Math.Abs(dy - step.y * steps));
                int progress = Math.Max(0, distance - projected);

                int score = MoveBaseScore
                            + progress * MoveProgressScorePerCell
                            + Mathf.RoundToInt(Math.Min(MoveEngageBonusMax, Math.Max(0, 16 - distance) / 2)
                                               * profile.激进度);

                // 走位进射击线：落点即有可开火攻击线（近似预判——被挡提前停/移动获能未计入，粗估即可）
                var projectedCell = new BattleCell(from.x + step.x * steps, from.y + step.y * steps);
                if (WouldHaveFiringLineFrom(sim, snapshot, unit, playerId, team, projectedCell))
                    score += MoveLineUpScore;
                int threatPenalty = ThreatPenaltyAt(threat, projectedCell);
                if (threatPenalty > 0) score = Mathf.Max(1, score - threatPenalty); // E-1 避险（钳 1 保底 Offer）
                score -= SelfPreservePenalty(sim, unit, team, projectedCell, profile); // F-1 支援自保（错配 3——档案权重 0 恒 0）
                score = Mathf.RoundToInt(score * profile.移动优先权重);

                tracker.Offer(score, new ActionData
                {
                    playerId = playerId,
                    unitId = unit.GetUnitComponent<UnitIdentity>()?.UnitID,
                    actionType = ActionType.Move,
                    direction = direction,
                    moveMagnitude = steps,
                    turnNumber = turn,
                });
                RememberMoveAnchor(sim, unit, anchor, team); // E-3：逼近承诺=延续该锚（首个可达成锚即选）
                return; // 首个可达成锚即选（勿迭代全锚取最高分——保持最近可达优先的确定性）
            }

            // 真无解兜底（2026-10-02 三轮报障「重开局安柏凯亚双挂机」回合 25 现场实证）：全部锚都
            // 未能产出移动候选（steps0 档对齐失败且无后续锚/不可达/首步被占）——朝**首锚** BFS
            // 方向**走满可行程**（CR-Move 拍板：1 格版换线太慢——全图级狙击射程下这是必经链路，
            // 每回合满速换位直到与锚行/列交线；地形截断+移速上限钳制，被单位挡由执行层停格前=预期
            // 部分行进、下回合重算）。纯兜底分=裸 MoveBaseScore（低于一切正常候选）；
            // BFS=0（首步即无路）=保持缺席
            if (!hasActionCandidate && anchors.Count > 0)
            {
                var fallbackDir = BattleHeuristics.FindApproachFirstStep(sim, unit, sim.GetPosition(anchors[0]));
                if (fallbackDir != 0)
                {
                    var fd = SkillHitResolver.DirectionToDelta(fallbackDir);
                    int run = 0;
                    for (int s = 1; s <= maxMove; s++)
                    {
                        var cell = new BattleCell(from.x + fd.x * s, from.y + fd.y * s);
                        if (!sim.Map.HasTile(cell.x, cell.y) || !sim.Map.IsPassable(cell.x, cell.y, forceType)) break;
                        run++;
                    }
                    if (run > 0)
                    {
                        tracker.Offer(Mathf.RoundToInt(MoveBaseScore * profile.移动优先权重), new ActionData
                        {
                            playerId = playerId,
                            unitId = unit.GetUnitComponent<UnitIdentity>()?.UnitID,
                            actionType = ActionType.Move,
                            direction = fallbackDir,
                            moveMagnitude = run,
                            turnNumber = turn,
                        });
                        RememberMoveAnchor(sim, unit, anchors[0], team); // E-3：兜底也延续首锚（走满=锚承诺仍在）
                    }
                }
            }
        }

        /// <summary>落点是否有可开火攻击线（任一可施放攻击技能从该点十字向可命中存活敌）——走位估值用</summary>
        private static bool WouldHaveFiringLineFrom(BattleSimState sim, BattleSnapshot snapshot, Unit unit,
            string playerId, TeamType team, BattleCell cell)
        {
            var skills = unit.Skills;
            for (int i = 0; i < skills.Count; i++)
            {
                var skill = skills[i];
                var data = skill?.RawData;
                if (data == null) continue;
                if (data.skillType != SkillType.Normal && data.skillType != SkillType.Burst) continue;
                if (!skill.CanCast(unit)) continue;
                if (!ResourceGate.HasAll(sim, unit, playerId, data.costs, out _)) continue;
                if (BattleHeuristics.EstimatePerTargetDamage(unit, data) <= 0) continue;

                foreach (var direction in BattleHeuristics.CrossDirections)
                {
                    if (!skill.WouldHitEnemyInDirection(sim.Map, snapshot, team, cell, direction)) continue;
                    if (BattleHeuristics.PreviewLineTargets(sim, snapshot, team, cell, data, direction).Count > 0)
                        return true;
                }
            }
            return false;
        }

        /// <summary>轴对齐直飞（2026-10-02 用户拍板「根据当前目标的位置，直接尽可能飞过去」）：
        /// 射程内但目标不在十字线上——沿横/纵轴朝目标直飞：列对齐=朝目标 x 走 |dx| 格、行对齐=朝
        /// 目标 y 走 |dy| 格（步数=轴差钳移速上限+直线地形，永不越过目标行/列）。两轴候选择优
        /// （确定性）：①对满轴且落点有开火线 ②对满轴 ③落点有开火线（部分进展但换到线）④部分
        /// <summary>支援站位评分器（F-2，docs/active/34 §4）：支援型有伤员时的移动档形态——
        /// 「位置=选格」取代「朝单锚走」（Ellie 候选格多维评分的回合制直线版）。候选=当前格 +
        /// 十字四向直线可达格（移动=推力直线语义——BFS 拐弯格不可达；射线逐格推进、地形断止，
        /// 与主逼近档直线段钳制同构）。评分维度：
        /// ①奶程（距伤员切比雪夫 ≤ 治疗半径=SupportHealReachScore 满档；超出每格 -Decay 衰减）
        /// ②开火线（落点有可开火攻击线 +MoveLineUpScore——支援型照常打水球）
        /// ③火线规避（威胁图 ThreatPenaltyAt——F-1 传入的 threat）
        /// ④自保（SelfPreservePenalty——距最近敌 &lt; 危险半径扣分×档案权重）
        /// 确定性：CrossDirections 枚举序 × 步数升序 + 严格大于替换（同分先到先得）；零 roll。
        /// 最优=当前格 → 不 Offer（驻位内化：转攻/转 Pass 由攻击档与缺席兜底）；全射线无候选
        /// → 不 Offer（伤员不可直线达=保持原位不空耗）</summary>
        private static void TryOfferSupportPosition(BattleSimState sim, BattleSnapshot snapshot, Unit unit,
            string playerId, TeamType team, int turn, BattleCell from, int maxMove, ForceType forceType,
            Unit wounded, OpponentThreatModel.ThreatMap threat, CandidateTracker tracker,
            UnitConfig.CompanionProfile profile)
        {
            var woundedPos = sim.GetPosition(wounded);
            bool woundedIsSelf = ReferenceEquals(wounded, unit); // G-5：伤员=自身（含保险后撤假锚）——治疗半径随她走
            int supportRadius = profile.支援贴近距离 > 0
                ? profile.支援贴近距离
                : BattleHeuristics.SupportRadiusOf(unit);
            if (supportRadius <= 0) supportRadius = 1; // 无治疗原子防御：贴 1 格（支援语义兜底）
            int auraRadius = BattleHeuristics.AuraRadiusOf(unit); // G-1 光环感知（无光环=0 不消费）

            // 当前格评分（「不动」基准——所有候选格与它比，选出比它好的才动）
            int currentScore = ScoreSupportCell(sim, snapshot, unit, playerId, team, from,
                woundedPos, supportRadius, auraRadius, woundedIsSelf, threat, profile);

            int bestScore = currentScore;
            Direction2D bestDir = 0;
            int bestRun = 0;
            foreach (var direction in BattleHeuristics.CrossDirections)
            {
                var delta = SkillHitResolver.DirectionToDelta(direction);
                for (int run = 1; run <= maxMove; run++)
                {
                    var cell = new BattleCell(from.x + delta.x * run, from.y + delta.y * run);
                    if (!sim.Map.HasTile(cell.x, cell.y) || !sim.Map.IsPassable(cell.x, cell.y, forceType))
                        break; // 直线射线：地形断止（与主逼近档直线段钳制同构）
                    if (IsCellOccupiedForStep(sim, cell)) continue; // 占据格不可停：跳过续评（执行层停格前保守近似）
                    int score = ScoreSupportCell(sim, snapshot, unit, playerId, team, cell,
                        woundedPos, supportRadius, auraRadius, woundedIsSelf, threat, profile);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestDir = direction;
                        bestRun = run;
                    }
                }
            }

            if (bestDir == 0) return; // 最优=当前格或全射线不可达：无移动候选（驻位/保持原位）

            // Offer 分=最优格的绝对站位分（与攻击候选同池可比——量级≈奶程 20+开火线 8）
            int offerScore = Mathf.RoundToInt(bestScore * profile.移动优先权重);
            tracker.Offer(offerScore, new ActionData
            {
                playerId = playerId,
                unitId = unit.GetUnitComponent<UnitIdentity>()?.UnitID,
                actionType = ActionType.Move,
                direction = bestDir,
                moveMagnitude = bestRun,
                turnNumber = turn,
            });
            RememberMoveAnchor(sim, unit, wounded, team); // 敌方锚才记录（wounded=我方→内部跳过）——E-3 槽不污染
        }

        /// <summary>支援站位单格评分（F-2 四维 + G-1 光环维度）：
        /// 奶程梯度 + 开火线 − 火线规避 − 自保 + 光环敌覆盖（G-1——各维正交）。
        /// woundedIsSelf（伤员=施法者自身）：治疗半径随施法者移动——distToWounded 恒 0
        ///（G-5 返修 2，2026-10-03 验证局实证「伤员=自己 → 旧位奶程锁 → 永久驻位」盲区）</summary>
        private static int ScoreSupportCell(BattleSimState sim, BattleSnapshot snapshot, Unit unit,
            string playerId, TeamType team, BattleCell cell, BattleCell woundedPos, int supportRadius,
            int auraRadius, bool woundedIsSelf, OpponentThreatModel.ThreatMap threat,
            UnitConfig.CompanionProfile profile)
        {
            int stance = SupportStanceOf(sim, unit, team, threat); // G-2 形态因子（本格评估用）
            int distToWounded = woundedIsSelf ? 0 : Math.Max(Math.Abs(cell.x - woundedPos.x), Math.Abs(cell.y - woundedPos.y));
            int score = distToWounded <= supportRadius
                ? SupportHealReachScore
                : Math.Max(0, SupportHealReachScore - (distToWounded - supportRadius) * SupportHealReachDecayPerCell);
            if (WouldHaveFiringLineFrom(sim, snapshot, unit, playerId, team, cell))
                score += MoveLineUpScore; // 支援型照常输出：落点有开火线=能打水球
            // E-1 火线规避——支援型站位评分内 ×SupportThreatPenaltyScale 减半（G-5 调参：整列火线
            // 吓退支援型的实证返修——前线形态接受中等风险换贴敌；配额脑/锚循环路径全额不动）
            int threatPenalty = Mathf.CeilToInt(ThreatPenaltyAt(threat, cell) * SupportThreatPenaltyScale);
            if (threatPenalty > 0) score = Math.Max(1, score - threatPenalty);
            score -= SelfPreservePenalty(sim, unit, team, cell, profile); // F-1 自保（内含 G-2 形态因子）
            // G-5 保险形态后撤梯度：远离最近敌每格 +2（上限 12）——保险形态贴敌分归零后旧版无任何
            // 移动驱动（驻位挨打实证）；后撤梯度给方向性（撤向敌射程外/我方阵内），与自保负分
            //（<2 格）正交互补：近距自保扣+远距后撤加=全程「离敌越远越好」
            int nearestEnemyForStance = NearestEnemyDistance(sim, unit, team, cell);
            if (stance > 1 && nearestEnemyForStance < int.MaxValue)
                score += Math.Min(SupportRetreatCap, nearestEnemyForStance * SupportRetreatPerStep);
            // G-1 光环敌覆盖（docs/active/35 §2）：持有半径型 tick 光环且档案权重>0——落点光环半径内
            // 每敌 +SupportAuraPerEnemy×权重（光环挂水/挂冰引擎的贴敌驱动力；队友 E-3 反应预期分自发
            // 消费挂水目标=冻结/蒸发联动零新机制）；G-2 保险形态（stance>1）贴敌分归零——血线/威胁
            // 预警时她后撤保命（复苏无限程，活着=复活保险在）。
            // G-5 前瞻梯度（2026-10-03 验证局盲区 1 返修）：光环半径内 0 敌时（远距趋近段）按
            // 「落点距最近敌」给梯度分（每近 1 格 +2，上限 12=2 敌满档）——旧版只对「已在光环内」
            // 给分，远距无驱动力+趋近格被火线扣分压制=恒缩角（28 回合实证）；前瞻与自保负分在
            // 1~2 格处对抗（前线形态净贴脸 +7.5/保险形态净负后撤）——权衡轴单一化
            if (auraRadius > 0 && profile.光环贴敌权重 > 0f && stance == 1)
            {
                int nearestEnemy = NearestEnemyDistance(sim, unit, team, cell);
                int enemiesInAura = 0;
                foreach (var kv in sim.Units)
                {
                    var enemy = kv.Value;
                    var enemyIdentity = enemy.GetUnitComponent<UnitIdentity>();
                    if (enemyIdentity == null || enemyIdentity.Team == team) continue;
                    if (BattleSimState.IsDead(enemy)) continue;
                    var pos = sim.GetPosition(enemy);
                    int dist = Math.Max(Math.Abs(pos.x - cell.x), Math.Abs(pos.y - cell.y));
                    if (dist <= auraRadius) enemiesInAura++;
                }
                int auraScore = Mathf.RoundToInt(SupportAuraPerEnemy * profile.光环贴敌权重) * enemiesInAura;
                if (enemiesInAura == 0 && nearestEnemy < int.MaxValue)
                    // G-5 修 2（前瞻公式勘误）：(Base − dist) × PerStep——14 格内有梯度（每近 1 格 +3），
                    // 旧斜率式 Base − dist×PerStep 在 5 格外恒 0（探针实证 9 格敌距=0 分=无趋近驱动）
                    auraScore += Math.Min(SupportAuraApproachCap,
                        Math.Max(0, (SupportAuraApproachBase - nearestEnemy) * SupportAuraApproachPerStep))
                        * Mathf.RoundToInt(Mathf.Min(1f, profile.光环贴敌权重));
                score += auraScore;
            }
            return score;
        }

        /// <summary>落点距最近存活敌切比雪夫（G-5 抽提共用——光环前瞻/保险后撤梯度两消费方；
        /// 无敌=int.MaxValue）</summary>
        private static int NearestEnemyDistance(BattleSimState sim, Unit unit, TeamType team, BattleCell cell)
        {
            int nearest = int.MaxValue;
            foreach (var kv in sim.Units)
            {
                var enemy = kv.Value;
                var enemyIdentity = enemy.GetUnitComponent<UnitIdentity>();
                if (enemyIdentity == null || enemyIdentity.Team == team) continue;
                if (BattleSimState.IsDead(enemy)) continue;
                var pos = sim.GetPosition(enemy);
                int dist = Math.Max(Math.Abs(pos.x - cell.x), Math.Abs(pos.y - cell.y));
                if (dist < nearest) nearest = dist;
            }
            return nearest;
        }

        /// 进展（轴差小者优先=更快收敛）；同级横轴优先（枚举序）。已对轴但线被断（尸体/虚空截线）
        /// 时另一轴候选自然成为换线出路。落点占据由执行层停格前处理（部分行进=预期，与正常路径
        /// 同语义）。返回=是否 Offer；失败走 1 格探针/换锚链</summary>
        private static bool TryOfferAxisAlignStep(BattleSimState sim, BattleSnapshot snapshot, Unit unit,
            string playerId, TeamType team, int turn, BattleCell from, BattleCell to, int maxMove,
            ForceType forceType, CandidateTracker tracker, UnitConfig.CompanionProfile profile,
            OpponentThreatModel.ThreatMap threat)
        {
            int dx = to.x - from.x;
            int dy = to.y - from.y;
            int bestRank = -1, bestDiff = 0, bestRun = 0;
            Direction2D bestDir = 0;
            bool bestLineOk = false;

            for (int axis = 0; axis <= 1; axis++)
            {
                int diff = axis == 0 ? Math.Abs(dx) : Math.Abs(dy);
                if (diff == 0) continue; // 已对轴：另一轴候选即换线出路
                var dir = axis == 0
                    ? (dx > 0 ? Direction2D.Right : Direction2D.Left)
                    : (dy > 0 ? Direction2D.Up : Direction2D.Down);
                var delta = SkillHitResolver.DirectionToDelta(dir);

                int run = 0;
                int cap = Math.Min(maxMove, diff);
                for (int s = 1; s <= cap; s++)
                {
                    var cell = new BattleCell(from.x + delta.x * s, from.y + delta.y * s);
                    if (!sim.Map.HasTile(cell.x, cell.y) || !sim.Map.IsPassable(cell.x, cell.y, forceType)) break;
                    run++;
                }
                if (run <= 0) continue;

                var landing = new BattleCell(from.x + delta.x * run, from.y + delta.y * run);
                bool aligned = run == diff;
                bool lineOk = WouldHaveFiringLineFrom(sim, snapshot, unit, playerId, team, landing);
                int rank = aligned && lineOk ? 3 : aligned ? 2 : lineOk ? 1 : 0;
                if (rank > bestRank || (rank == bestRank && diff < bestDiff))
                {
                    bestRank = rank;
                    bestDiff = diff;
                    bestRun = run;
                    bestDir = dir;
                    bestLineOk = lineOk;
                }
            }
            if (bestRank < 0) return false;

            int score = MoveBaseScore + MoveProgressScorePerCell * bestRun + (bestLineOk ? MoveLineUpScore : 0);
            var bestDelta = SkillHitResolver.DirectionToDelta(bestDir);
            var landingCell = new BattleCell(from.x + bestDelta.x * bestRun, from.y + bestDelta.y * bestRun);
            int axisThreatPenalty = ThreatPenaltyAt(threat, landingCell);
            if (axisThreatPenalty > 0) score = Mathf.Max(1, score - axisThreatPenalty); // E-1 避险（钳 1 保底 Offer）
            score -= SelfPreservePenalty(sim, unit, team, landingCell, profile); // F-1 支援自保
            score = Mathf.RoundToInt(score * profile.移动优先权重);
            tracker.Offer(score, new ActionData
            {
                playerId = playerId,
                unitId = unit.GetUnitComponent<UnitIdentity>()?.UnitID,
                actionType = ActionType.Move,
                direction = bestDir,
                moveMagnitude = bestRun,
                turnNumber = turn,
            });
            return true;
        }

        /// <summary>对齐走位（1 格探针，边缘兜底档）：轴对齐不可行（两轴首格即地形断/已对轴但
        /// 线被截）时的侧移换线——先沿 BFS 首步（天然合法且指向目标正交邻格=对角一步即成线），
        /// 再按十字枚举序逐向试；落点三查=地形可行+无阻挡占据+开火线成立才 Offer。
        /// 探测序确定性：BFS 首步 → CrossDirections 枚举序。占据查=任何单位（含尸体）占格即挡
        ///（MovementResolver 结算侧同保守近似——多绕不弹回，偏差保守向）。
        /// 返回=是否成功 Offer（失败时调用方换下一锚继续试——首锚驻位无效≠全场无解，勿 return）</summary>
        private static bool TryOfferAlignmentStep(BattleSimState sim, BattleSnapshot snapshot, Unit unit,
            string playerId, TeamType team, int turn, BattleCell from, ForceType forceType,
            Direction2D bfsDirection, CandidateTracker tracker, UnitConfig.CompanionProfile profile,
            OpponentThreatModel.ThreatMap threat)
        {
            for (int probe = 0; probe <= BattleHeuristics.CrossDirections.Length; probe++)
            {
                var dir = probe == 0 ? bfsDirection : BattleHeuristics.CrossDirections[probe - 1];
                var delta = SkillHitResolver.DirectionToDelta(dir);
                var cell = new BattleCell(from.x + delta.x, from.y + delta.y);
                if (!sim.Map.HasTile(cell.x, cell.y) || !sim.Map.IsPassable(cell.x, cell.y, forceType)) continue;
                if (IsCellOccupiedForStep(sim, cell)) continue; // 尸体/友敌占格：挪进去必被弹回（保守挡）
                if (!WouldHaveFiringLineFrom(sim, snapshot, unit, playerId, team, cell)) continue;

                // E-1 避险：探针落点在敌方预测火线内扣分（18−12=6 恒正分；Max(1,·) 仅防未来调参扣成负
                // 导致 Offer 丢弃——危险落点保底 Offer 排后不缺席）+ F-1 支援自保（探针落点同判）
                int score = Mathf.RoundToInt(Mathf.Max(1,
                    MoveBaseScore + MoveLineUpScore - ThreatPenaltyAt(threat, cell)
                    - SelfPreservePenalty(sim, unit, team, cell, profile)) * profile.移动优先权重);
                tracker.Offer(score, new ActionData
                {
                    playerId = playerId,
                    unitId = unit.GetUnitComponent<UnitIdentity>()?.UnitID,
                    actionType = ActionType.Move,
                    direction = dir,
                    moveMagnitude = 1,
                    turnNumber = turn,
                });
                return true; // 首个成立落点即选（枚举序确定性）
            }
            return false; // 全向无成立落点：调用方换下一锚继续试（勿当全场无解）
        }

        /// <summary>对齐走位落点占据查（保守口径）：任何单位（含尸体——尸体保留碰撞）占格即视为挡，
        /// 与 FindApproachFirstStep 的 occupied 同近似（友方互不阻挡 flag 的精确判定不在对齐档做——
        /// 保守向偏差=少选格不会被结算弹回）</summary>
        private static bool IsCellOccupiedForStep(BattleSimState sim, BattleCell cell)
        {
            foreach (var kv in sim.Units)
            {
                var pos = sim.GetPosition(kv.Value);
                if (pos.x == cell.x && pos.y == cell.y) return true;
            }
            return false;
        }

        // ==================== E-1 威胁消费原语（配额脑对手建模，docs/active/33 §2.3） ====================

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

        // ==================== E-3 估值与延续原语（伙伴脑规划层，docs/active/33 §4） ====================

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

        /// <summary>移动锚延续记录（E-3）：只记**敌方**锚（支援伤员锚动态变化频繁——奶满即失效，
        /// 锁定反而滞后；敌方锚=火力目标，锁定语义成立）。我方锚/取不到 id=不记录</summary>
        private static void RememberMoveAnchor(BattleSimState sim, Unit unit, Unit anchor, TeamType team)
        {
            var anchorIdentity = anchor?.GetUnitComponent<UnitIdentity>();
            if (anchorIdentity == null || anchorIdentity.Team == team) return;
            if (sim.TryGetUnitId(unit, out var unitId) && sim.TryGetUnitId(anchor, out var anchorId))
                _lastMoveAnchors[unitId] = anchorId;
        }

        /// <summary>单位 id 轻取（E-3 锚延续槽键；取不到=null）</summary>
        private static string UnitIdOf(BattleSimState sim, Unit unit)
        {
            return sim.TryGetUnitId(unit, out var id) ? id : null;
        }

        /// <summary>支援自保扣分（F-1，docs/active/34 §5.3）：落点距最近敌切比雪夫 &lt; 危险半径=
        ///「站敌人刀口」扣 SupportSelfPreserveScore × 档案「自保权重」（默认 0=恒 0=非支援/未配置
        /// 零变化）；与 E-1 火线扣分正交（火线=具体危险格、近敌=广义贴脸风险）。
        /// G-2 形态切换（docs/active/35）：返回值乘 SupportStanceOf 形态因子——保险形态（血线低/
        /// 威胁预警）×3（贴敌格扣 45=后撤位自然胜出）；threat=null（配额脑路径）仅血线门生效</summary>
        private static int SelfPreservePenalty(BattleSimState sim, Unit unit, TeamType team,
            BattleCell cell, UnitConfig.CompanionProfile profile)
        {
            if (profile == null || profile.自保权重 <= 0f) return 0;
            var identity = unit.GetUnitComponent<UnitIdentity>();
            if (identity == null) return 0;
            foreach (var kv in sim.Units)
            {
                var enemy = kv.Value;
                var enemyIdentity = enemy.GetUnitComponent<UnitIdentity>();
                if (enemyIdentity == null || enemyIdentity.Team == team) continue; // 敌我=TeamType 口径
                if (BattleSimState.IsDead(enemy)) continue; // 尸体不构成贴脸威胁
                var pos = sim.GetPosition(enemy);
                if (Math.Max(Math.Abs(pos.x - cell.x), Math.Abs(pos.y - cell.y)) < SupportDangerRadius)
                    return Mathf.RoundToInt(SupportSelfPreserveScore * profile.自保权重)
                        * SupportStanceOf(sim, unit, team, null); // 任一活敌近于危险半径即触发（×G-2 形态因子）
            }
            return 0;
        }

        /// <summary>支援形态因子（G-2 形态切换，docs/active/35 §2）：1=前线形态（光环贴敌满效）/
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
            if (stats.HP * 100 < hpStruct.Max * SupportStanceRetreatHpPercent) return SupportStanceSafeFactor;
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
            foreach (var kv in sim.Units)
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
            var casterPos = sim.GetPosition(caster);

            foreach (var atom in data.effects)
            {
                if (atom.kind != SkillEffectKind.Heal) continue;
                if (atom.trigger != SkillEffectTrigger.OnCast && atom.trigger != SkillEffectTrigger.OnHit) continue;

                int radius = atom.radiusKey != SkillParamKey.None ? data.GetInt(atom.radiusKey, 1) : 1;

                foreach (var kv in sim.Units)
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
                            var pos = sim.GetPosition(ally);
                            if (Math.Max(Math.Abs(pos.x - casterPos.x), Math.Abs(pos.y - casterPos.y)) > radius)
                                continue; // 出半径
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
        /// Skill=技能 costs；Stamina 条目按施法者层级换算（眷属 0/伙伴 5/魔神 10）</summary>
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
                    return UnitTierHelper.StaminaCostOf(BattleHeuristics.TierOf(actor));
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

        /// <summary>
        /// 本方意图板（E-2 协调层，docs/active/33 §3）：伙伴决策序内（unitId 升序）前位伙伴已定
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

            public int DeclaredDamage(string enemyId) =>
                DeclaredDamageOnEnemy.TryGetValue(enemyId, out var v) ? v : 0;

            public int DeclaredHeal(string allyId) =>
                DeclaredHealOnAlly.TryGetValue(allyId, out var v) ? v : 0;
        }

        /// <summary>候选追踪：严格大于替换（同分先到先得——枚举序即决策确定性）。
        /// 配额脑（PlayerQuotaBrain）跨单位共享一个实例取全场最优；伙伴脑每单位一个实例取该单位最优。</summary>
        public sealed class CandidateTracker
        {
            public ActionData Best { get; private set; }
            private int _bestScore;

            public void Offer(int score, ActionData action)
            {
                if (action == null || score <= _bestScore) return;
                _bestScore = score;
                Best = action;
            }
        }
    }
}
