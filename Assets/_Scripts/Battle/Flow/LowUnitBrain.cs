using System.Collections.Generic;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 低级单位决策器（B6b，docs/04 §4.1 第一阶段）：1~2 星单位无论归属都自主决定本回合行动
    /// （表现类似 MOBA 小兵），决策先于玩家选择完成、不依赖玩家本回合选择；本阶段只决策不结算。
    /// v1 极简三档（拍板）：战技射程内有敌→打最近 → 否则朝最近敌移动 1 步 → 不可动不出行动。
    /// 纯读 BattleSimState 由 Host 在选择阶段头生成（docs/18 决策一），行动与玩家行动合并进执行阶段。
    /// </summary>
    public static class LowUnitBrain
    {
        /// <summary>
        /// 全部低级单位的本回合行动（存活的；不可动者缺席=无行动——Pass 无结算意义不出命令）
        /// </summary>
        public static List<ActionData> DecideAll(BattleSimState sim, int turnNumber)
        {
            var actions = new List<ActionData>();
            foreach (var kv in sim.Units)
            {
                var unit = kv.Value;
                if (!BattleHeuristics.IsMinorUnit(unit)) continue;
                if (BattleSimState.IsDead(unit) || !BattleSimState.CanAct(unit)) continue;

                var action = DecideOne(sim, kv.Key, unit, turnNumber);
                if (action != null)
                    actions.Add(action);
            }
            return actions;
        }

        /// <summary>
        /// 单个低级单位决策：①战技可命中（直线方向有敌格）→ 战技朝敌；
        /// ②否则朝最近敌移动 1 步（小兵蠕动；被挡由 MovementResolver 结算弹回）；
        /// ③无敌人 → 缺席
        /// </summary>
        private static ActionData DecideOne(BattleSimState sim, string unitId, Unit unit, int turnNumber)
        {
            var identity = unit.GetUnitComponent<UnitIdentity>();
            if (identity == null) return null;
            var enemy = BattleHeuristics.FindNearestEnemy(sim, unit);
            if (enemy == null) return null; // 无敌人（终局/空场）：不出行动

            var selfPos = sim.GetPosition(unit);
            var enemyPos = sim.GetPosition(enemy);

            // 战技（丘丘族暂无技能=自动跳过此档）
            int skillIndex = BattleHeuristics.FindSkillIndex(unit, SkillType.Normal);
            if (skillIndex >= 0)
            {
                var direction = BattleHeuristics.FindLineSkillDirection(sim, unit, skillIndex);
                if (direction != 0)
                {
                    return new ActionData
                    {
                        playerId = identity.OwnerPlayerID,
                        unitId = unitId,
                        actionType = ActionType.Skill,
                        skillIndex = skillIndex,
                        direction = direction,
                        turnNumber = turnNumber,
                    };
                }
            }

            // 朝最近敌移动 1 步（低级单位=小兵蠕动，非战棋全距离）
            int dx = enemyPos.x - selfPos.x;
            int dy = enemyPos.y - selfPos.y;
            return new ActionData
            {
                playerId = identity.OwnerPlayerID,
                unitId = unitId,
                actionType = ActionType.Move,
                direction = BattleHeuristics.DeltaToDirection(dx, dy),
                moveMagnitude = 1,
                turnNumber = turnNumber,
            };
        }
    }
}
