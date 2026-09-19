using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 安柏·一箭双丘丘（战技，docs/units/蒙德/安柏.md）：
    /// 选择十字方向其一，射出 2 发箭矢，每发 40% 火伤。
    /// 投射物直线飞行（docs/05 §5.3 + docs/18 决策二）：B5 连续判定体系——命中 = 接触首个敌方立牌圆柱之时、
    /// 读命中时刻连续插值位置（移动中可被中途命中，所见即所得）；24 格上限无接触则消散。
    /// 本技能只声明发射（ProjectileEffect），实际命中由 ProjectileResolver 在同片移动展开后判定。
    /// 简化延续：多段伤害合并单次结算（元素反应一次命中只触发一次）；反应级别恒 1。
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
            int totalPercent = damagePercent * damageCount; // 多段合并（B4 简化①延续）

            var delta = SkillHitResolver.DirectionToDelta(action.direction);

            // 只声明发射——命中判定延迟到同片移动展开后（ProjectileResolver 按
            // 执行阶段时间轴模拟接触；发射格=片初快照位置，docs/active/22 §11）
            effects.Add(new ProjectileEffect(action.unitId, action, totalPercent,
                casterState.position, delta.x, delta.y));
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
