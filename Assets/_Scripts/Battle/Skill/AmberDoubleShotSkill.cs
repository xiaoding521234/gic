using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 安柏·一箭双丘丘（战技，docs/units/蒙德/安柏.md）：
    /// 选择十字方向其一，射出 2 发箭矢，每发 40% 火伤。
    /// 投射物直线飞行（docs/05 §5.3 + docs/18 决策二）：沿方向逐格扫描，命中**首个敌方单位所在格截停**、
    /// 该格全部敌方单位生效（格 AoE）；24 格上限无命中则消散。
    /// B4 简化：①伤害按 Damage×DamageCount 合并为单次结算（元素反应一次命中只触发一次）；
    /// ②命中判定按片初快照格位置（Host 即定路径；移动单位连续插值命中=B5 表现批次）；
    /// ③层数恒 1（反应级别恒 1）。
    /// </summary>
    [SkillAttribute(SkillName.Amber_DoubleShot)]
    public class AmberDoubleShotSkill : BaseSkill
    {
        public override bool CanCast(Unit caster) => true;

        public override List<BattleEffect> ResolveEffects(BattleSimState sim, ActionData action, BattleSnapshot sliceSnapshot)
        {
            var effects = new List<BattleEffect>();
            var casterState = SkillHitResolver.FindUnitState(sliceSnapshot, action.unitId);
            if (casterState == null) return effects;

            int damagePercent = GetParamValue(SkillParamKey.Damage, 40);
            int damageCount = GetParamValue(SkillParamKey.DamageCount, 2);
            int totalPercent = damagePercent * damageCount; // 多段合并（B4 简化①）

            var delta = SkillHitResolver.DirectionToDelta(action.direction);
            var from = casterState.position;

            // 直线逐格扫描：首个敌方单位所在格截停（格 AoE；docs/05 §5.3 统一拍板 24 格上限）
            for (int step = 1; step <= ProjectileRule.MaxRange; step++)
            {
                var cell = new BattleCell(from.x + delta.x * step, from.y + delta.y * step);
                if (!sim.Map.HasTile(cell.x, cell.y)) break; // 虚空=消散

                var enemies = SkillHitResolver.FindEnemiesAt(sliceSnapshot, action.playerId, cell);
                if (enemies.Count == 0) continue;

                foreach (var enemy in enemies)
                    effects.AddRange(SkillHitResolver.Hit(sim, action, sliceSnapshot, enemy.unitId,
                        totalPercent, ProjectileRule.LineDelivery, from));
                break; // 首个敌方格截停
            }
            return effects;
        }
    }

    /// <summary>
    /// 投射物规则常量（docs/05 §5.3 统一拍板 + docs/18 决策二"投放形态由技能数据驱动"）
    /// </summary>
    public static class ProjectileRule
    {
        /// <summary>直线型弹射物飞行上限（统一 24 格）</summary>
        public const int MaxRange = 24;

        /// <summary>投放形态：直线飞行投射物（客户端播箭矢）</summary>
        public const int LineDelivery = 1;
    }
}
