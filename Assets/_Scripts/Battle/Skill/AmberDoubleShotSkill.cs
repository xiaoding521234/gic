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

        /// <summary>方向推荐预判（投射物截停形态）：镜像 ProjectileResolver 圆柱接触判定的静态版——
        /// 敌方恒=快照格心（移动中途中命中不可预知，属提示非校验）；规格=时轮 LineProjectile clip
        /// （0=BattleMetrics/ProjectileRule 默认），弹道距离上限=min(射程+0.5, 首个虚空格近边界 k−0.5)
        /// ——距离空间版的 Host maxT/VoidBoundaryTime 同口径</summary>
        public override bool WouldHitEnemyInDirection(BattleMapData map, BattleSnapshot snapshot,
            string casterPlayerId, BattleCell from, Direction2D direction)
        {
            var delta = SkillHitResolver.DirectionToDelta(direction);
            var clips = SkillTimelineQuery.JudgmentClips(Timeline, SkillJudgmentKind.LineProjectile);
            var clip = clips.Count > 0 ? clips[0] : null;
            float radius = clip != null && clip.hitDiameter > 0f ? clip.hitDiameter : BattleMetrics.UnitCylinderDiameter;
            radius *= 0.5f;
            int maxRange = clip != null && clip.maxRange > 0 ? clip.maxRange : ProjectileRule.MaxRange;

            float maxDist = maxRange + 0.5f;
            for (int k = 1; k <= maxRange; k++)
            {
                if (map.HasTile(from.x + delta.x * k, from.y + delta.y * k)) continue;
                maxDist = k - 0.5f; // 首个虚空格近边界=弹道截断
                break;
            }

            var dir = new Vector2(delta.x, delta.y).normalized;
            var origin = new Vector2(from.x + 0.5f, from.y + 0.5f);
            foreach (var enemy in snapshot.units)
            {
                if (enemy.playerId == casterPlayerId) continue; // 含尸体——尸体完全算判定
                var rel = new Vector2(enemy.position.x + 0.5f, enemy.position.y + 0.5f) - origin;
                if (rel.sqrMagnitude <= radius * radius) return true; // 发射即贴脸（同格堆叠）
                float along = Vector2.Dot(rel, dir);
                float perpSq = rel.sqrMagnitude - along * along;
                if (perpSq > radius * radius) continue; // 弹道不穿该圆柱
                float entry = along - Mathf.Sqrt(radius * radius - perpSq);
                if (entry >= 0f && entry <= maxDist) return true;
            }
            return false;
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
