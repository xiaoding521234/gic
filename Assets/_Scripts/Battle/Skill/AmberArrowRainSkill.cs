using System.Collections.Generic;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
namespace GIC.Battle
{


    /// <summary>
    /// 安柏·箭雨（爆发，docs/units/蒙德/安柏.md）：选择十字方向其一射出大量箭矢，
    /// 对直线上的**所有**敌人造成 4 次 40% 火伤（整线 AoE，无截停——投放形态=整线天降斜落）。
    /// B4 简化：①伤害按 Damage×DamageCount 合并单次（反应一次）；②天降视觉 B5 表现批次
    /// （暂按直击表现）；③元能消耗检查待元能系统（B6）落地；④命中按片初快照格位置。
    /// </summary>
    [SkillAttribute(SkillName.Amber_ArrowRain)]
    public class AmberArrowRainSkill : BaseSkill
    {
        public override bool CanCast(Unit caster) => true;

        public override void Execute(Unit caster, SkillContext context) { /* 结算走 ResolveEffects */ }

        public override List<BattleEffect> ResolveEffects(BattleSimState sim, ActionData action, BattleSnapshot sliceSnapshot)
        {
            var effects = new List<BattleEffect>();
            var casterState = SkillHitResolver.FindUnitState(sliceSnapshot, action.unitId);
            if (casterState == null) return effects;

            int damagePercent = GetParamValue(SkillParamKey.Damage, 40);
            int damageCount = GetParamValue(SkillParamKey.DamageCount, 4);
            int totalPercent = damagePercent * damageCount;

            var delta = SkillHitResolver.DirectionToDelta(action.direction);
            var from = casterState.position;

            // 整线全目标（无截停；虚空=线终止）
            for (int step = 1; step <= ProjectileRule.MaxRange; step++)
            {
                var cell = new BattleCell(from.x + delta.x * step, from.y + delta.y * step);
                if (!sim.Map.HasTile(cell.x, cell.y)) break;

                foreach (var enemy in SkillHitResolver.FindEnemiesAt(sliceSnapshot, action.playerId, cell))
                    effects.AddRange(SkillHitResolver.Hit(sim, action, sliceSnapshot, enemy.unitId,
                        totalPercent, 0, from));
            }
            return effects;
        }
    }

    /// <summary>
    /// 凯亚·霜袭（战技，docs/units/蒙德/凯亚.md）：选择十字方向其一刺出寒气，
    /// 对前方 DamageDistance 格内的**所有**敌人造成 100% 冰伤。
    /// B4 简化：掠夺摩拉（MoraPlunder）待局内经济结算（B6）落地；命中按片初快照格位置。
    /// </summary>
    [SkillAttribute(SkillName.Kaeya_Frostgnaw)]
    public class KaeyaFrostgnawSkill : BaseSkill
    {
        public override bool CanCast(Unit caster) => true;

        public override void Execute(Unit caster, SkillContext context) { /* 结算走 ResolveEffects */ }

        public override List<BattleEffect> ResolveEffects(BattleSimState sim, ActionData action, BattleSnapshot sliceSnapshot)
        {
            var effects = new List<BattleEffect>();
            var casterState = SkillHitResolver.FindUnitState(sliceSnapshot, action.unitId);
            if (casterState == null) return effects;

            int damagePercent = GetParamValue(SkillParamKey.Damage, 100);
            int distance = GetParamValue(SkillParamKey.DamageDistance, 2);

            var delta = SkillHitResolver.DirectionToDelta(action.direction);
            var from = casterState.position;

            // 前方 N 格内全目标（近战寒气=直击形态，无投射物）
            for (int step = 1; step <= distance; step++)
            {
                var cell = new BattleCell(from.x + delta.x * step, from.y + delta.y * step);
                if (!sim.Map.HasTile(cell.x, cell.y)) break;

                foreach (var enemy in SkillHitResolver.FindEnemiesAt(sliceSnapshot, action.playerId, cell))
                    effects.AddRange(SkillHitResolver.Hit(sim, action, sliceSnapshot, enemy.unitId,
                        damagePercent, 0, from));
            }
            return effects;
        }
    }
}
