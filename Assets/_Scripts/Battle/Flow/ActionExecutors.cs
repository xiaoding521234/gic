using System;
using System.Collections.Generic;
using GIC.Framework;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 移动行动执行器：构建移动者状态，实际结算由 MovementResolver 同片同步逐步展开
    /// </summary>
    public static class MoveExecutor
    {
        public static MoveActionState BuildMover(BattleSimState sim, ActionData action)
        {
            var unit = sim.GetUnit(action.unitId);
            if (unit == null) return null;
            if (BattleSimState.IsDead(unit) || !BattleSimState.CanAct(unit)) return null;

            var mover = new MoveActionState
            {
                UnitId = action.unitId,
                Unit = unit,
                Direction = action.direction,
                RemainingSteps = Math.Max(0, action.moveMagnitude),
            };
            return mover;
        }
    }

    /// <summary>
    /// 技能行动执行器（B4：正式技能链——ActionData.skillIndex 索引 unit.Skills（=UnitConfig.skills
    /// 顺序创建），BaseSkill.ResolveEffects 纯结算产出效应；DebugAttackSkill 已退役）。
    /// 单位指向型技能只对目标单位生效（不适用格子判定，docs/05 §5.3）；允许鞭尸。
    /// 目标校验读片前快照（瞬发效应按片初状态结算）。
    /// </summary>
    public static class SkillExecutor
    {
        public static List<BattleEffect> Resolve(BattleSimState sim, ActionData action, BattleSnapshot sliceSnapshot)
        {
            var effects = new List<BattleEffect>();

            var attacker = sim.GetUnit(action.unitId);
            if (attacker == null) return effects;
            if (BattleSimState.IsDead(attacker) || !BattleSimState.CanAct(attacker)) return effects;

            var skill = GetSkill(attacker, action.skillIndex);
            if (skill == null)
            {
                GICLog.Warn($"[SkillExecutor] 单位 {action.unitId} 无技能索引 {action.skillIndex}，行动落空");
                return effects;
            }

            // 元能门槛（B6a）：消耗值=技能条目 EnergyCost（0=无消耗——战技/移动不耗能；
            // 爆发 30/40/100、延奏 20，攒够才可放；不足→行动落空）
            int energyCost = BattleSimState.GetEnergyCost(skill.RawData);
            if (energyCost > 0 && !BattleSimState.HasEnoughEnergy(attacker, energyCost))
            {
                var stats = attacker.GetUnitComponent<UnitStats>();
                GICLog.Info($"[SkillExecutor] 单位 {action.unitId} 技能 {skill.RawData?.skillID} 元能不足" +
                            $"（{stats?.Energy ?? 0}/{energyCost}），行动落空");
                return effects;
            }

            if (!skill.CanCast(attacker))
            {
                GICLog.Info($"[SkillExecutor] 单位 {action.unitId} 技能 {skill.RawData?.skillID} 不可施放，行动落空");
                return effects;
            }

            effects.AddRange(skill.ResolveEffects(sim, action, sliceSnapshot));

            // 元能消耗随效应产出（负值，随片统一应用；获取端=战技命中，在 SkillHitResolver）
            if (energyCost > 0)
                effects.Add(new EnergyEffect(action.unitId, -energyCost));

            return effects;
        }

        /// <summary>skillIndex = unit.Skills 数组索引（InitSkills 按 UnitConfig.skills 顺序创建，HUD 同源映射）</summary>
        private static BaseSkill GetSkill(Unit unit, int skillIndex)
        {
            if (unit == null || skillIndex < 0 || skillIndex >= unit.Skills.Count) return null;
            return unit.Skills[skillIndex];
        }
    }

    /// <summary>
    /// 空过执行器（无效果）
    /// </summary>
    public static class PassExecutor
    {
        public static List<BattleEffect> Resolve(BattleSimState sim, ActionData action)
        {
            return new List<BattleEffect>();
        }
    }
}
