using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 安柏·一箭双丘丘（战技，docs/units/蒙德/安柏.md）：
    /// 选择十字方向其一，射出 2 发箭矢，每发 40% 火伤。
    /// 投射物直线飞行（docs/05 §5.3 + docs/18 决策二）：B5 连续判定体系——命中 = 接触首个敌方立牌圆柱之时、
    /// 读命中时刻连续插值位置（移动中可被中途命中，所见即所得）；射程上限无接触则消散。
    /// 本技能只声明发射（ProjectileEffect），实际命中由 ProjectileResolver 在同片移动展开后判定。
    /// 时轮（B-S1，2026-09-23 逐发化拍板）：前摇 0.30s=发射时刻偏移、两发间隔 0.05s、
    /// 逐发独立判定/附着/反应（推翻 B4 简化①"多段合并单次结算"；元能获取仍同片按行动者去重——
    /// B6a 口径不受影响）。无时轮兜底=旧合并行为（timeline 为 null 时）。
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
            var delta = SkillHitResolver.DirectionToDelta(action.direction);

            // 时轮判定轨（LineProjectile clip）：逐发发射声明——发射时刻=前摇、连发间隔、
            // per-skill 投射物规格（速度/体积/射程，0=BattleMetrics 默认）
            var clips = SkillTimelineQuery.JudgmentClips(Timeline, SkillJudgmentKind.LineProjectile);
            if (clips.Count > 0)
            {
                foreach (var clip in clips)
                {
                    for (int i = 0; i < damageCount; i++)
                    {
                        float launch = clip.startTime + clip.hitInterval * i;
                        effects.Add(new ProjectileEffect(action.unitId, action, damagePercent,
                            casterState.position, delta.x, delta.y,
                            launch, clip.projectileSpeed, clip.hitDiameter, clip.maxRange));
                    }
                }
                return effects;
            }

            // 无时轮兜底（旧行为：多段合并单次）
            effects.Add(new ProjectileEffect(action.unitId, action, damagePercent * damageCount,
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
