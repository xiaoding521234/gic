using System.Collections.Generic;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 低级单位决策器（B6b，docs/04 §4.1 第一阶段）：1~2 星单位无论归属都自主决定本回合行动
    /// （表现类似 MOBA 小兵），决策先于玩家选择完成、不依赖玩家本回合选择；本阶段只决策不结算。
    /// v2（2026-09-25 方向纪律收口）：战技预判/移动方向一律十字四向（docs/18 决策八「十字方向其一」
    /// ——八向上交在技能侧被归一主轴斜向必空放，docs/11 方向纪律①④）；预判走技能实例
    /// WouldHitEnemyInDirection（与 HUD 瞄准推荐/结算形态同源）+ 存活目标校验（纯尸体线不浪费行动）。
    /// 纯读 BattleSimState 由 Host 在选择阶段头生成（docs/18 决策一），行动与玩家行动合并进执行阶段。
    /// 决策档（v1 语义保留）：战技可命中→打 → 否则朝最近敌蠕动 1 步 → 无敌可向/不可动=缺席。
    /// </summary>
    public static class LowUnitBrain
    {
        /// <summary>
        /// 全部低级单位的本回合行动（存活的；不可动者缺席=无行动——Pass 无结算意义不出命令）
        /// </summary>
        public static List<ActionData> DecideAll(BattleSimState sim, int turnNumber)
        {
            // 决策快照（纯读，与选择阶段广播同源状态）——技能预判的唯一状态源
            var snapshot = sim.TakeSnapshot(turnNumber);

            var actions = new List<ActionData>();
            foreach (var kv in sim.Units)
            {
                var unit = kv.Value;
                if (!BattleHeuristics.IsMinorUnit(unit)) continue;
                if (BattleSimState.IsDead(unit) || !BattleSimState.CanAct(unit)) continue;

                var action = DecideOne(sim, snapshot, kv.Key, unit, turnNumber);
                if (action != null)
                    actions.Add(action);
            }
            return actions;
        }

        /// <summary>
        /// 单个低级单位决策：①战技可命中（十字向预判有存活敌）→ 战技朝敌；
        /// ②否则朝最近敌蠕动 1 步（小兵蠕动；被挡由 MovementResolver 结算弹回——被挡也算已使用，
        /// 低级单位无体力配额 B6d 豁免）；③无敌人/同格堆叠 → 缺席
        /// </summary>
        private static ActionData DecideOne(BattleSimState sim, BattleSnapshot snapshot,
            string unitId, Unit unit, int turnNumber)
        {
            var identity = unit.GetUnitComponent<UnitIdentity>();
            if (identity == null) return null;
            var enemy = BattleHeuristics.FindNearestEnemy(sim, unit);
            if (enemy == null) return null; // 无敌人（终局/空场）：不出行动

            var selfPos = sim.GetPosition(unit);

            // 战技（丘丘族无技能=自动跳过此档；占位技能不可施放同理）
            int skillIndex = BattleHeuristics.FindSkillIndex(unit, SkillType.Normal);
            if (skillIndex >= 0)
            {
                var skill = unit.Skills[skillIndex];
                if (skill != null && skill.CanCast(unit)
                    && BattleSimState.HasEnoughEnergy(unit, BattleSimState.GetEnergyCost(skill.RawData)))
                {
                    var direction = BattleHeuristics.FindAttackDirection(sim, snapshot, unit, skill);
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
            }

            // 朝最近敌蠕动 1 步（低级单位=小兵蠕动，非战棋全距离；十字逼近——方向纪律同玩家）
            var enemyPos = sim.GetPosition(enemy);
            int dx = enemyPos.x - selfPos.x;
            int dy = enemyPos.y - selfPos.y;
            var moveDirection = BattleHeuristics.BestCrossApproachDirection(dx, dy);
            if (moveDirection == 0) return null; // 同格堆叠：无逼近意义，缺席

            return new ActionData
            {
                playerId = identity.OwnerPlayerID,
                unitId = unitId,
                actionType = ActionType.Move,
                direction = moveDirection,
                moveMagnitude = 1,
                turnNumber = turnNumber,
            };
        }
    }
}
