using System;
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
    /// 技能行动执行器（B1 = DebugAttackSkill 固定伤害，走 DamagePipeline）。
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

            // 目标必须存在于片前快照（尸体可被打）
            var targetState = FindUnitState(sliceSnapshot, action.targetUnitId);
            if (targetState == null)
            {
                GICLog.Warn($"[SkillExecutor] 快照中找不到目标单位 {action.targetUnitId}，行动落空");
                return effects;
            }

            var target = sim.GetUnit(action.targetUnitId);
            if (target == null) return effects;

            // B1：取 DebugAttackSkill（正式技能 B4 换 BaseSkill 子类 + SkillConfig.asset）
            var debugSkill = FindDebugSkill(attacker);
            if (debugSkill == null)
            {
                GICLog.Warn($"[SkillExecutor] 单位 {action.unitId} 无可用调试技能，行动落空");
                return effects;
            }

            var request = new DamageRequest
            {
                Attacker = attacker,
                Target = target,
                AttackPercent = 100,
                FlatDamage = DebugAttackSkill.FixedDamage,
            };
            var result = DamagePipeline.Calculate(request);
            if (!result.Cancelled && result.FinalDamage > 0)
                effects.Add(new DamageEffect(action.unitId, action.targetUnitId, result.FinalDamage));

            return effects;
        }

        private static UnitState FindUnitState(BattleSnapshot snapshot, string unitId)
        {
            if (snapshot == null || unitId == null) return null;
            foreach (var state in snapshot.units)
                if (state.unitId == unitId) return state;
            return null;
        }

        private static DebugAttackSkill FindDebugSkill(Unit unit)
        {
            foreach (var skill in unit.Skills)
                if (skill is DebugAttackSkill debug) return debug;
            return null;
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
