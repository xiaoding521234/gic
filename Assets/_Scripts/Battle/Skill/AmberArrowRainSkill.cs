using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 安柏·箭雨（爆发，docs/units/蒙德/安柏.md）：选择十字方向其一射出大量箭矢，
    /// 对直线上的**所有**敌人造成 4 次 40% 火伤（整线 AoE，无截停——投放形态=整线天降斜落）。
    /// 时轮（B-S1，2026-09-23 逐发化拍板）：LineBurst clip 前摇 0.30s + 连段间隔逐段独立判定
    /// （每段独立时刻/独立 Damage 命令——天降连射节拍；推翻 B4 简化①"合并单次"）。
    /// 命中按片初快照格位置（瞬发结算）；天降视觉 B5 表现遗留（暂按直击表现）。
    /// 无时轮兜底=旧合并单次行为（timeline 为 null 时）。
    /// </summary>
    [SkillAttribute(SkillName.Amber_ArrowRain)]
    public class AmberArrowRainSkill : BaseSkill
    {
        public override bool CanCast(Unit caster) => true;

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

            // 时轮判定轨（LineBurst clip）：逐段整线 AoE——每段发射时刻=前摇+间隔×序号，
            // 各段独立结算/附着/反应（同片快照语义：各段读同一片初附着）
            var clips = SkillTimelineQuery.JudgmentClips(Timeline, SkillJudgmentKind.LineBurst);
            if (clips.Count > 0)
            {
                foreach (var clip in clips)
                {
                    for (int i = 0; i < damageCount; i++)
                    {
                        float launch = clip.startTime + clip.hitInterval * i;
                        effects.AddRange(ResolveLineBurst(sim, action, sliceSnapshot, from, delta,
                            damagePercent, launch));
                    }
                }
                return effects;
            }

            // 无时轮兜底（旧行为：合并单次整线）
            effects.AddRange(ResolveLineBurst(sim, action, sliceSnapshot, from, delta, totalPercent, 0f));
            return effects;
        }

        /// <summary>整线全目标 AoE（无截停；虚空=线终止）——attackPercent=本段伤害百分比，launchSeconds=本段时刻</summary>
        private static List<BattleEffect> ResolveLineBurst(BattleSimState sim, ActionData action,
            BattleSnapshot sliceSnapshot, BattleCell from, Vector2Int delta, int attackPercent, float launchSeconds)
        {
            var effects = new List<BattleEffect>();
            for (int step = 1; step <= ProjectileRule.MaxRange; step++)
            {
                var cell = new BattleCell(from.x + delta.x * step, from.y + delta.y * step);
                if (!sim.Map.HasTile(cell.x, cell.y)) break;

                foreach (var enemy in SkillHitResolver.FindEnemiesAt(sliceSnapshot, action.playerId, cell))
                    effects.AddRange(SkillHitResolver.Hit(sim, action, sliceSnapshot, enemy.unitId,
                        attackPercent, 0, from, 0f, 0f, launchSeconds));
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

        /// <summary>方向推荐预判（近战距离段形态）：前方 DamageDistance 格内有敌=推荐（虚空截断；
        /// 与 ResolveEffects 同参数同扫描口径）</summary>
        public override bool WouldHitEnemyInDirection(BattleMapData map, BattleSnapshot snapshot,
            string casterPlayerId, BattleCell from, Direction2D direction)
        {
            var delta = SkillHitResolver.DirectionToDelta(direction);
            int distance = GetParamValue(SkillParamKey.DamageDistance, 2);
            for (int step = 1; step <= distance; step++)
            {
                var cell = new BattleCell(from.x + delta.x * step, from.y + delta.y * step);
                if (!map.HasTile(cell.x, cell.y)) break;
                if (SkillHitResolver.FindEnemiesAt(snapshot, casterPlayerId, cell).Count > 0) return true;
            }
            return false;
        }
    }
}
