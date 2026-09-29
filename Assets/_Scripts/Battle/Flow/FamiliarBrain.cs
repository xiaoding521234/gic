using System.Collections.Generic;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 眷属决策器（B6b 起，docs/04 §4.1 五阶段第一阶段；D 批次 2026-09-29 术语迁移改名：
    /// 低级单位→眷属、LowUnitBrain→FamiliarBrain，docs/active/32 §6）：1~2 星眷属单位无论归属
    /// 都自主决定本回合行动（表现类似 MOBA 小兵），决策先于玩家选择完成、不感知玩家本回合选择；
    /// 本阶段只决策不结算。
    /// v2（2026-09-25 方向纪律收口）：战技预判/移动方向一律十字四向（docs/18 决策八「十字方向其一」
    /// ——八向上交在技能侧被归一主轴斜向必空放，docs/11 方向纪律①④）；预判走技能实例
    /// WouldHitEnemyInDirection（与 HUD 瞄准推荐/结算形态同源）+ 存活目标校验（纯尸体线不浪费行动）。
    /// v3（2026-09-26 报障「不会绕路卡湖边」）：移动档改 BFS 最短路首步（FindApproachFirstStep）——
    /// 直行被湖/虚空挡时拐弯绕行，每回合重算走一步沿最短路逼近；小兵蠕动 1 步/回合语义不变。
    /// v4（2026-09-26 拍板「当自己的任何攻击都无法打到时，换目标巡逻」）：攻击档打不了时按距离
    /// 升序逐敌试逼近步（FindEnemiesByDistance）——贴身已到/不可达的目标自动跳过换下一个，
    /// 不再对着打不了的目标站桩；全部敌都无逼近步才缺席。
    /// 纯读 BattleSimState 由 Host 在选择阶段头生成（docs/18 决策一），行动与玩家行动合并进执行阶段。
    /// 决策档：战技可命中→打 → 否则逐敌（近→远）沿最短路蠕动 1 步 → 全部敌打不了也走不近=缺席。
    /// 眷属不配行为档案（启发式维持=层级特色，也是数量安全阀——场上眷属多，评分制算量敏感，
    /// docs/active/32 §6.1）。
    /// </summary>
    public static class FamiliarBrain
    {
        /// <summary>
        /// 全部眷属单位的本回合行动（存活的；不可动者缺席=无行动——Pass 无结算意义不出命令）
        /// </summary>
        public static List<ActionData> DecideAll(BattleSimState sim, int turnNumber)
        {
            // 决策快照（纯读，与选择阶段广播同源状态）——技能预判的唯一状态源
            var snapshot = sim.TakeSnapshot(turnNumber);

            var actions = new List<ActionData>();
            foreach (var kv in sim.Units)
            {
                var unit = kv.Value;
                if (BattleHeuristics.IsBuilding(unit)) continue; // 建筑不参与任何行动（含未来低星建筑）
                if (!BattleHeuristics.IsFamiliar(unit)) continue;
                if (BattleSimState.IsDead(unit) || !BattleSimState.CanAct(unit)) continue;

                var action = DecideOne(sim, snapshot, kv.Key, unit, turnNumber);
                if (action != null)
                    actions.Add(action);
            }
            return actions;
        }

        /// <summary>
        /// 单个眷属单位决策：①战技可命中（十字向预判有存活敌）→ 战技朝敌；
        /// ②否则按距离升序逐敌试 BFS 最短路首步（v3 绕行+v4 换目标巡逻——某敌贴身已到/不可达
        /// 即换下一个目标，被挡由 MovementResolver 结算弹回——被挡也算已使用，眷属无体力配额=层级表 0 档）；
        /// ③无敌人/全场敌都无逼近步 → 缺席
        /// </summary>
        private static ActionData DecideOne(BattleSimState sim, BattleSnapshot snapshot,
            string unitId, Unit unit, int turnNumber)
        {
            var identity = unit.GetUnitComponent<UnitIdentity>();
            if (identity == null) return null;

            // 战技（丘丘族无技能=自动跳过此档；占位技能不可施放同理）
            int skillIndex = BattleHeuristics.FindSkillIndex(unit, SkillType.Normal);
            if (skillIndex >= 0)
            {
                var skill = unit.Skills[skillIndex];
                // 消耗门槛（统一消耗模型 C-2）：costs 单源镜像（ResourceGate.HasAll——眷属豁免玩家
                // 资源、元能照查，与 Host 同口径）
                if (skill != null && skill.CanCast(unit)
                    && ResourceGate.HasAll(sim, unit, identity.OwnerPlayerID, skill.RawData?.costs, out _))
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

            // 朝敌蠕动 1 步——v4 换目标巡逻（2026-09-26 拍板「当自己的任何攻击都无法打到时，换目标
            // 巡逻」）：攻击档打不了时按距离升序逐敌试 BFS 最短路首步（FindApproachFirstStep——
            // 贴身已到/不可达返回 0 的敌自动跳过换下一个，不再对着打不了的目标站桩）；
            // 全部敌都无逼近步（全贴身/全不可达）才缺席。小兵蠕动 1 步/回合+十字方向纪律不变。
            foreach (var target in BattleHeuristics.FindEnemiesByDistance(sim, unit))
            {
                var moveDirection = BattleHeuristics.FindApproachFirstStep(sim, unit, sim.GetPosition(target));
                if (moveDirection == 0) continue; // 该敌打不了也走不近：换下一个目标

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
            return null; // 全场敌都无可逼近步：缺席
        }
    }
}
