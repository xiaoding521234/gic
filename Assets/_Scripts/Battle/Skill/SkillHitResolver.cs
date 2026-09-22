using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 技能命中结算工具（B4）：单目标"伤害 + 元素反应 + 附着"效应产物。
    /// 反应判定读片前快照的 DyedElement（快照一致性）；状态修改全部以效应形态产出
    /// （伤害走 DamagePipeline、冻结走 ApplyBuffEffect、附着走 AttachElementEffect）。
    /// </summary>
    public static class SkillHitResolver
    {
        /// <summary>
        /// 产出命中目标的全套效应（伤害含反应易伤并入；命中后附着来袭元素）
        /// </summary>
        /// <param name="attackPercent">攻击百分比（多段伤害已在此合并）</param>
        /// <param name="delivery">投放形态（0=直击 / 1=直线投射物）</param>
        /// <param name="fromCell">投射物发射格</param>
        /// <param name="hitPointX">命中点连续格心坐标（投射物有效；Host 接触判定得出）</param>
        /// <param name="hitPointY">命中点连续格心坐标 Y</param>
        public static List<BattleEffect> Hit(BattleSimState sim, ActionData action, BattleSnapshot sliceSnapshot,
            string targetUnitId, int attackPercent, int delivery, BattleCell fromCell,
            float hitPointX = 0f, float hitPointY = 0f)
        {
            var effects = new List<BattleEffect>();
            var attacker = sim.GetUnit(action.unitId);
            var target = sim.GetUnit(targetUnitId);
            if (attacker == null || target == null) return effects;

            var targetState = FindUnitState(sliceSnapshot, targetUnitId);
            if (targetState == null) return effects; // 快照中不存在（瞬发读片初状态）

            var element = attacker.GetUnitComponent<UnitElement>()?.SelfElement ?? ElementType.Physical;

            // 元素反应预判（融化=易伤 / 蒸发=增伤 / 冻结=施加控制，docs/06）
            var outcome = ElementReactionResolver.Preview((ElementType)targetState.dyedElement, element);

            var request = new DamageRequest
            {
                Attacker = attacker,
                Target = target,
                AttackPercent = attackPercent,
                Element = (int)element,
                VulnerabilityBonus = outcome.VulnerabilityBonus,
                DamageBonusDelta = outcome.DamageBonusDelta,
            };
            var result = DamagePipeline.Calculate(request);
            if (!result.Cancelled && result.FinalDamage > 0)
            {
                effects.Add(new DamageEffect(action.unitId, targetUnitId, result.FinalDamage,
                    (int)element, delivery, fromCell, hitPointX, hitPointY, outcome.ReactionType));
            }

            if (outcome.HasReaction)
            {
                // 反应发生事件（2026-09-22 接线）：融化的伤害并入已由 DamageEffect 承载，
                // 此处补"反应发生"事实载体——客户端即时表现（冻结立牌冰色等）
                effects.Add(new ReactionEffect(action.unitId, targetUnitId, outcome.ReactionType, outcome.Level));

                if (outcome.BuffType >= 0)
                    effects.Add(new ApplyBuffEffect(action.unitId, targetUnitId, outcome.BuffType, outcome.Level));
            }

            // 命中后附着来袭元素（覆盖旧附着=消耗被反应附着；物理不附着）
            if (element != ElementType.Physical)
                effects.Add(new AttachElementEffect(action.unitId, targetUnitId, (int)element));

            return effects;
        }

        /// <summary>片前快照中找单位状态（瞬发结算唯一状态源）</summary>
        public static UnitState FindUnitState(BattleSnapshot snapshot, string unitId)
        {
            if (snapshot == null || unitId == null) return null;
            foreach (var state in snapshot.units)
                if (state.unitId == unitId) return state;
            return null;
        }

        /// <summary>十字方向 → 格增量（斜向输入归一到主轴；投射物=十字方向其一）</summary>
        public static Vector2Int DirectionToDelta(Direction2D direction)
        {
            switch (direction)
            {
                case Direction2D.Right:
                case Direction2D.UpRight:
                case Direction2D.DownRight:
                    return new Vector2Int(1, 0);
                case Direction2D.Left:
                case Direction2D.UpLeft:
                case Direction2D.DownLeft:
                    return new Vector2Int(-1, 0);
                case Direction2D.Up:
                    return new Vector2Int(0, 1);
                case Direction2D.Down:
                    return new Vector2Int(0, -1);
                default:
                    return new Vector2Int(1, 0);
            }
        }

        /// <summary>片前快照中该格的敌方存活+尸体单位（含尸体——尸体完全算判定，docs/05 §5.4）</summary>
        public static List<UnitState> FindEnemiesAt(BattleSnapshot snapshot, string playerId, BattleCell cell)
        {
            var result = new List<UnitState>();
            if (snapshot == null) return result;
            foreach (var state in snapshot.units)
            {
                if (state.playerId == playerId) continue;
                if (state.position.x != cell.x || state.position.y != cell.y) continue;
                result.Add(state);
            }
            return result;
        }
    }
}
