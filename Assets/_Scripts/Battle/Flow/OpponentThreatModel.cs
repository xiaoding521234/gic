using System.Collections.Generic;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 对手威胁模型（E-1 配额脑对手建模，docs/active/33 §2）——2026-10-03 立项：
    /// 「对手如我」先验（业界标准做法：用己方策略先验建模对手）对敌方全部存活可动单位跑确定性
    /// 预测，把预测行动推导成威胁图，供配额脑（AI 玩家）的操魔神/号令候选做风险调整。
    ///
    /// 预测方法分档：敌方眷属=FamiliarBrain.DecideOne 原样复用（启发式纯函数——「行为可被玩家
    /// 预测」决策三十一特性的 AI 侧对偶，AI 玩家同样按可预测模型推演敌方眷属）；敌方伙伴/魔神=
    /// 共享评分骨架（中性档案+敌方实池——敌方伙伴体力确实从敌方玩家池扣，先验照查）。
    ///
    /// 信息边界合规（docs/active/32 §3 意图可见性边界）：预测=纯推导——只读 sim/snapshot 公共
    /// 信息 + 确定性先验，**不读** Host 内敌方眷属已定行动（_familiarUnitActions）/敌方玩家未交
    /// 选择，与真人玩家信息位完全对等（Frozen Synapse 哲学：信息对称，计划质量定胜负）。
    ///
    /// 确定性（docs/active/33 §5 红线）：全链零 roll——启发式/骨架均枚举序确定性，同快照恒同预测，
    /// 回放/联机（B7）安全。
    ///
    /// v1 语义简化（docs/active/33 §10 已登记）：①敌方预测 Move/Pass/缺席对威胁图零贡献（移动后
    /// 二阶先验不做——保守不虚报威胁）；②承伤=EstimatePerTargetDamage 粗估和（未计防御/反应
    /// 乘区——排序用途非精确预言）；③敌方伙伴真实决策发生在收齐我方选择后（会感知我们重决策），
    /// 单层预测不做博弈递归——预测失准空间=WEGO 本质。
    /// </summary>
    public static class OpponentThreatModel
    {
        /// <summary>
        /// 威胁图（E-1 数据载体）：一次构建、配额脑各候选统一消费的推导产物（docs/active/33 §2.2）。
        /// 全部为粗估值——用途是「风险调整的相对排序」，不是精确预言。
        /// </summary>
        public sealed class ThreatMap
        {
            /// <summary>我方单位预期承伤（unitId → 敌方预测攻击行动对本单位的伤害累计和）</summary>
            public readonly Dictionary<string, int> ExpectedDamageOnAlly = new Dictionary<string, int>();

            /// <summary>敌方单位威胁值（unitId → 该敌全部预测行动的预期输出和——「威胁源」排序用）</summary>
            public readonly Dictionary<string, int> EnemyThreatScore = new Dictionary<string, int>();

            /// <summary>敌方预测攻击的火线格集合（沿预测方向扫到技能射程、虚空格截断——移动候选
            /// 避险「别站火线上」用；几何与 PreviewLineTargets/AttackRangeOf 同口径）</summary>
            public readonly HashSet<BattleCell> ThreatenedCells = new HashSet<BattleCell>();

            /// <summary>预计将被击杀的我方单位（预期承伤合计 ≥ 当前 hp——合计口径：多敌集火同一
            /// 单位由构建尾部统一判定，勿在逐敌累加处判）；治疗/增益号令「救命档」消费</summary>
            public readonly HashSet<string> DoomedAllies = new HashSet<string>();
        }

        /// <summary>
        /// 构建威胁图：对敌方全部存活可动单位逐一预测，仅 Skill 预测产出威胁（Move/Pass/缺席零贡献
        /// ——v1 简化）；构建尾部按快照 hp 收口 DoomedAllies（合计致死）。
        /// 性能：敌方 3~8 单位 × 预测一遍 ≈ 伙伴脑同量级，毫秒级（配额脑每回合一次）。
        /// </summary>
        public static ThreatMap Build(BattleSimState sim, BattleSnapshot snapshot, TeamType myTeam, int turn)
        {
            var map = new ThreatMap();
            foreach (var kv in sim.Units)
            {
                var enemy = kv.Value;
                if (BattleHeuristics.IsBuilding(enemy)) continue; // 建筑（含协议核心）无技能无行动不产威胁
                var identity = enemy.GetUnitComponent<UnitIdentity>();
                if (identity == null || identity.Team == myTeam) continue; // 敌我=TeamType 口径
                if (BattleSimState.IsDead(enemy) || !BattleSimState.CanAct(enemy)) continue;

                var action = PredictAction(sim, snapshot, kv.Key, enemy, identity, turn);
                if (action == null || action.actionType != ActionType.Skill) continue;

                ApplySkillThreat(sim, snapshot, map, enemy, identity, kv.Key, action);
            }

            // 合计致死判定（构建尾部统一）：承伤粗估和 ≥ 快照当前 hp → 救命档
            foreach (var kv in map.ExpectedDamageOnAlly)
            {
                foreach (var u in snapshot.units)
                {
                    if (u.unitId != kv.Key) continue;
                    if (kv.Value >= u.hp) map.DoomedAllies.Add(kv.Key);
                    break;
                }
            }
            return map;
        }

        /// <summary>单敌预测（「对手如我」先验，确定性）：眷属=启发式脑原样复用；伙伴/魔神=共享
        /// 评分骨架中性档+敌方实池（ResourceGate 单源照查敌方玩家池）。骨架 threat=null——预测层
        /// 不递归建威胁图（被预测方不再预测我们，单层博弈——docs/active/33 §10）</summary>
        private static ActionData PredictAction(BattleSimState sim, BattleSnapshot snapshot,
            string unitId, Unit enemy, UnitIdentity identity, int turn)
        {
            if (BattleHeuristics.TierOf(enemy) == UnitTier.Familiar)
                return FamiliarBrain.DecideOne(sim, snapshot, unitId, enemy, turn);

            var tracker = new CompanionBrain.CandidateTracker();
            CompanionBrain.ScoreUnitCandidates(sim, snapshot, enemy, identity.OwnerPlayerID,
                identity.Team, turn, tracker, null, null, null);
            return tracker.Best;
        }

        /// <summary>预测攻击行动 → 威胁图三表累加：承伤表（敌方视角 PreviewLineTargets 扫命中我方）
        /// + 威胁源值（该敌预期输出）+ 火线格（沿预测方向扫射程）。单位指向/自施放型预测（敌方伙伴
        /// 的复苏/自增益）无直接伤害威胁跳过；perTarget≤0（纯附着/纯治疗技能）跳过</summary>
        private static void ApplySkillThreat(BattleSimState sim, BattleSnapshot snapshot, ThreatMap map,
            Unit enemy, UnitIdentity identity, string enemyId, ActionData action)
        {
            var skill = action.skillIndex >= 0 && action.skillIndex < enemy.Skills.Count
                ? enemy.Skills[action.skillIndex]
                : null;
            var data = skill?.RawData;
            if (data == null) return;
            if (data.IsUnitTargeted() || data.IsSelfCast()) return;
            int perTarget = BattleHeuristics.EstimatePerTargetDamage(enemy, data);
            if (perTarget <= 0) return;

            var from = sim.GetPosition(enemy);
            var targets = BattleHeuristics.PreviewLineTargets(sim, snapshot, identity.Team, from, data, action.direction);
            foreach (var target in targets)
            {
                map.ExpectedDamageOnAlly.TryGetValue(target.unitId, out var cur);
                map.ExpectedDamageOnAlly[target.unitId] = cur + perTarget;
            }
            if (targets.Count > 0)
            {
                map.EnemyThreatScore.TryGetValue(enemyId, out var enemyCur);
                map.EnemyThreatScore[enemyId] = enemyCur + perTarget * targets.Count;
            }

            foreach (var cell in ThreatLineCells(sim, from, data, action.direction))
                map.ThreatenedCells.Add(cell);
        }

        /// <summary>敌方预测攻击的火线格（沿预测方向逐格扫到技能射程，虚空格截断——与
        /// PreviewLineTargets 虚空截断/AttackRangeOf per-skill 射程提取两处同口径：投射物=
        /// clip.maxRange 缺省 ProjectileRule.MaxRange；整线=LineBurst clip 同规则）</summary>
        private static IEnumerable<BattleCell> ThreatLineCells(BattleSimState sim, BattleCell from,
            SkillConfig.SkillData data, Direction2D direction)
        {
            var projClips = SkillTimelineQuery.JudgmentClips(data.timeline, SkillJudgmentKind.LineProjectile);
            int range;
            if (projClips.Count > 0)
                range = projClips[0].maxRange > 0 ? projClips[0].maxRange : ProjectileRule.MaxRange;
            else
            {
                var burstClips = SkillTimelineQuery.JudgmentClips(data.timeline, SkillJudgmentKind.LineBurst);
                range = burstClips.Count > 0 && burstClips[0].maxRange > 0
                    ? burstClips[0].maxRange : ProjectileRule.MaxRange;
            }

            var delta = SkillHitResolver.DirectionToDelta(direction);
            for (int step = 1; step <= range; step++)
            {
                var cell = new BattleCell(from.x + delta.x * step, from.y + delta.y * step);
                if (!sim.Map.HasTile(cell.x, cell.y)) yield break; // 虚空截断（同 PreviewLineTargets）
                yield return cell;
            }
        }
    }
}
