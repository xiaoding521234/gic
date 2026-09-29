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

        /// <summary>中性档案（无配置/魔神档兜底：全 1 权重=评分制 v2 原口径）</summary>
        private static readonly UnitConfig.CompanionProfile NeutralProfile = new UnitConfig.CompanionProfile();

        // ==================== 决策主入口（伙伴决策阶段） ====================

        /// <summary>
        /// 全部伙伴单位的本回合自主行动（存活的；不可动/已被号令/无好行动=缺席）。
        /// playerActions=各玩家已提交选择（Host 收齐后传入——号令跳过+体力预留的感知源）。
        /// </summary>
        public static List<ActionData> DecideAll(BattleSimState sim, int turnNumber, List<ActionData> playerActions)
        {
            var actions = new List<ActionData>();
            var snapshot = sim.TakeSnapshot(turnNumber);

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
                ScoreUnitCandidates(sim, snapshot, unit, identity.OwnerPlayerID, identity.Team,
                    turnNumber, tracker, pool, profile);

                if (tracker.Best != null)
                {
                    actions.Add(tracker.Best);
                    int cost = ActionStaminaCost(sim, tracker.Best, unit);
                    if (cost > 0)
                        committed[identity.OwnerPlayerID] =
                            (committed.TryGetValue(identity.OwnerPlayerID, out var c2) ? c2 : 0) + cost;
                }
            }
            return actions;
        }

        // ==================== 共享评分骨架（伙伴脑+配额脑操魔神档复用） ====================

        /// <summary>
        /// 单位级行动候选评分（评分制 v2 骨架收口）：①攻击技能（战技/爆发）——CanCast+costs 门槛
        /// （体力按层级换算；决策期虚拟池透传）、十字四向逐向预判（与 HUD 瞄准推荐/结算形态同源）、
        /// 逐目标有效伤害（过杀截断）+斩杀加成+战技命中获能；②移动——十字逼近锚点（输出型=最近敌，
        /// 支援型=最缺血我方）、步数逼近到偏好交战距离止步（非无脑贴脸）、走位进射击线加分。
        /// 每维度乘 UnitConfig.行为档案权重（profile null=中性=评分制 v2 原口径）。
        /// 支援型骨架多一个候选类别：技能携带治疗原子时估有效治疗并入评分（估「奶谁」而非只「打谁」）。
        /// </summary>
        public static void ScoreUnitCandidates(BattleSimState sim, BattleSnapshot snapshot, Unit unit,
            string playerId, TeamType team, int turn, CandidateTracker tracker,
            int? staminaPoolOverride, UnitConfig.CompanionProfile profile)
        {
            if (unit == null || tracker == null) return;
            profile ??= NeutralProfile;
            ScoreAttackCandidates(sim, snapshot, unit, playerId, team, turn, tracker, staminaPoolOverride, profile);
            ScoreMoveCandidate(sim, snapshot, unit, playerId, team, turn, tracker, staminaPoolOverride, profile);
        }

        /// <summary>①攻击技能候选（战技/爆发）：预判与结算形态同源（WouldHitEnemyInDirection）；评分=逐目标
        /// 有效伤害+斩杀×激进度+战技获能；乘技能类型优先权重；支援型并入治疗原子估值</summary>
        private static void ScoreAttackCandidates(BattleSimState sim, BattleSnapshot snapshot, Unit unit,
            string playerId, TeamType team, int turn, CandidateTracker tracker,
            int? staminaPoolOverride, UnitConfig.CompanionProfile profile)
        {
            var skills = unit.Skills;
            var from = sim.GetPosition(unit);

            for (int i = 0; i < skills.Count; i++)
            {
                var skill = skills[i];
                var data = skill?.RawData;
                if (data == null) continue;
                if (data.skillType != SkillType.Normal && data.skillType != SkillType.Burst) continue;
                if (!skill.CanCast(unit)) continue; // 占位技能跳过——凛冽轮舞/闪耀奇迹未实装不可施放

                // 消耗门槛（统一消耗模型 C-2）：costs 全条目镜像 Host 判定（体力按施法者层级换算；
                // 决策期虚拟池透传——伙伴决策不超额承诺）
                if (!ResourceGate.HasAll(sim, unit, playerId, data.costs, out _, staminaPoolOverride)) continue;

                int perTarget = BattleHeuristics.EstimatePerTargetDamage(unit, data);
                int healValue = profile.候选类别 == UnitConfig.CompanionRole.Support
                    ? EstimateSkillHealValue(sim, unit, team, data)
                    : 0;
                if (perTarget <= 0 && healValue <= 0) continue;

                foreach (var direction in BattleHeuristics.CrossDirections)
                {
                    if (!skill.WouldHitEnemyInDirection(sim.Map, snapshot, team, from, direction))
                        continue;
                    var targets = BattleHeuristics.PreviewLineTargets(sim, snapshot, team, from, data, direction);
                    if (targets.Count == 0 && healValue <= 0) continue; // 纯尸体线：不浪费行动

                    int score = 0;
                    foreach (var target in targets)
                    {
                        score += Math.Min(perTarget, target.hp); // 过杀部分不重复计分
                        if (perTarget >= target.hp)
                            score += Mathf.RoundToInt(KillBonusScore * profile.激进度);
                    }
                    if (data.skillType == SkillType.Normal)
                        score += SkillHitEnergyScore; // 战技命中 +10 元能（爆发/延奏命中不获能）
                    score += healValue; // 支援型：治疗原子估值（方向无关的平加——与其它候选同池竞争）
                    score = Mathf.RoundToInt(score * (data.skillType == SkillType.Normal
                        ? profile.战技优先权重
                        : profile.爆发优先权重));

                    tracker.Offer(score, Skill(playerId, unit, i, direction, turn));
                }
            }
        }

        /// <summary>②移动候选（2026-09-29 报障返修「单位堆积湖边试图走又被弹回」）：锚点=距离升序
        /// 取**首个可达成**目标（支援型先试最缺血我方锚）——最近敌常悬湖上（飞行单位），其十字四邻
        /// 全水=BFS 目标集空，旧直行逼近=每回合原地弹回白耗体力（现场取证：凯亚/芭芭拉 Up×3 撞
        /// (7,5) 水格弹回）；方向=BFS 最短路首步（FindApproachFirstStep，眷属 v3/v4 同款——绕湖/
        /// 绕虚空拐弯），步数三重钳=偏好交战距离余量 × 移速上限 × 该向地形可行程（移动是推力直线
        /// 语义，转向留给下一回合重算=BFS 取直线段）；已在偏好距离内=驻位即最优位不再逼近。
        /// 评分=兜底+逼近进度+接敌×激进度+走位进射击线，乘移动优先权重</summary>
        private static void ScoreMoveCandidate(BattleSimState sim, BattleSnapshot snapshot, Unit unit,
            string playerId, TeamType team, int turn, CandidateTracker tracker,
            int? staminaPoolOverride, UnitConfig.CompanionProfile profile)
        {
            if (!ResourceGate.HasAll(sim, unit, playerId, MoveExecutor.GetMoveCosts(unit), out _, staminaPoolOverride))
            {
                return; // 移动消耗（C-2 costs 单源；体力按层级换算+虚拟池）
            }

            var from = sim.GetPosition(unit);
            var forceType = unit.GetUnitComponent<UnitMoveable>()?.NormalMoveType ?? ForceType.Walk;
            int maxMove = MoveExecutor.MaxMoveDistance(unit);

            // 锚序：支援型先试最缺血我方（奶谁/去哪护），全军敌迭代殿后
            var anchors = new List<Unit>();
            if (profile.候选类别 == UnitConfig.CompanionRole.Support
                && profile.目标偏好 == UnitConfig.CompanionTargetPreference.MostWoundedAlly)
            {
                var wounded = FindMostWoundedAlly(sim, unit, team);
                if (wounded != null) anchors.Add(wounded);
            }
            anchors.AddRange(BattleHeuristics.FindEnemiesByDistance(sim, unit));

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

                // 步数三重钳：偏好距离余量 → 移速上限 → 该向地形可行程（BFS 只保证首步，直线段
                // 可能中途遇湖——按地形截断；被单位挡由 MovementResolver 停格前=预期部分行进）
                int steps = Math.Min(Math.Max(0, distance - Math.Max(0, profile.偏好交战距离)), maxMove);
                if (steps <= 0) return; // 已在偏好交战距离内（就近锚）：驻位即最优位，不换锚逼近
                var step = SkillHitResolver.DirectionToDelta(direction);
                int straightRun = 0;
                for (int s = 1; s <= steps; s++)
                {
                    var cell = new BattleCell(from.x + step.x * s, from.y + step.y * s);
                    if (!sim.Map.HasTile(cell.x, cell.y) || !sim.Map.IsPassable(cell.x, cell.y, forceType)) break;
                    straightRun++;
                }
                if (straightRun <= 0) continue; // 首步即被单位占住（BFS 保守近似外的兜底）：换下一锚
                steps = Math.Min(steps, straightRun);

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
                return; // 首个可达成锚即选（勿迭代全锚取最高分——保持最近可达优先的确定性）
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
        private static int EstimateSkillHealValue(BattleSimState sim, Unit caster, TeamType team,
            SkillConfig.SkillData data)
        {
            if (data?.effects == null) return 0;
            var casterStats = caster.GetUnitComponent<UnitStats>();
            if (casterStats == null) return 0;
            var casterPos = sim.GetPosition(caster);
            int total = 0;

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
                    int effective = Math.Min(heal, missing);
                    if (effective * 100 < heal * HealWorthRatioPercent) continue; // 缺口不足半量不占行动
                    total += effective;
                }
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
