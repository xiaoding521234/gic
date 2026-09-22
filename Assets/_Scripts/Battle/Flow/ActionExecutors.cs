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

    /// <summary>
    /// 出战角色执行器（B6c，docs/18 决策七 + docs/05 §5.1）：校验链=摩拉够 → 落点合法（核心半径 2 内
    /// 或己方建筑半径 1 内——建筑 DeployBuilding 后续批次，当前仅核心锚点）→ 生成单位登场。
    /// 卡不消耗留手牌（可重复出战）；1~2 星直接铺场，3 星+重复出战升命座（命座 B8，当前重复铺场）。
    /// 直产命令（Summon+StatChange(Mora)），不走 BattleEffect 效应链（资源/召唤不是单位效应）。
    /// </summary>
    public static class DeployUnitExecutor
    {
        /// <summary>角色部署核心半径（docs/18 决策七拍板；建筑=核心半径 3/建筑半径 2 属 DeployBuilding 后续批次）</summary>
        public const int DeployRadiusFromCore = 2;

        /// <summary>部署落点合法（角色）：协议核心半径 2 内（切比雪夫；核心位置代理=出生区中心，B8 换真核心）</summary>
        public static bool IsDeployCellValid(BattleSimState sim, string playerId, BattleCell cell)
        {
            if (!sim.Map.HasTile(cell.x, cell.y)) return false;
            var core = sim.GetCorePosition(playerId);
            int distance = Math.Max(Math.Abs(cell.x - core.x), Math.Abs(cell.y - core.y));
            return distance <= DeployRadiusFromCore;
        }

        /// <summary>
        /// 执行部署：校验+扣费+生成单位。成功=true 且返回两条命令（Summon+StatChange 摩拉变化）。
        /// </summary>
        public static bool TryResolve(BattleSimState sim, ActionData action, out BattleCommand summonCommand,
            out BattleCommand moraCommand)
        {
            summonCommand = null;
            moraCommand = null;

            var config = UnityEngine.Resources.Load<UnitConfig>("Configs/UnitConfig");
            var unitName = (UnitName)action.deployUnitName;
            var data = config?.GetUnitData(unitName);
            if (data == null)
            {
                GICLog.Warn($"[DeployUnit] UnitConfig 无 {unitName}，部署落空");
                return false;
            }

            if (!IsDeployCellValid(sim, action.playerId, action.deployCell))
            {
                GICLog.Info($"[DeployUnit] {unitName} 落点 {action.deployCell} 不在核心半径 {DeployRadiusFromCore} 内，部署落空");
                return false;
            }

            int cost = data.GetEffectiveDeployCost();
            if (!sim.TrySpendMora(action.playerId, cost))
            {
                GICLog.Info($"[DeployUnit] {action.playerId} 摩拉不足（{sim.GetMora(action.playerId)}/{cost}），部署落空");
                return false;
            }

            // 生成登场（复用 UnitFactory 真实数据链；技能/组件与开局单位同构——同 BattleSession.SpawnDebugUnit）
            var unit = UnitFactory.CreateUnitWithData(data);
            if (unit == null) return false;
            if (sim.LogicRoot != null)
                unit.transform.SetParent(sim.LogicRoot, false);
            var unitId = sim.RegisterUnit(unit, action.playerId, sim.GetTeamOf(action.playerId), action.deployCell);

            var identity = unit.GetUnitComponent<UnitIdentity>();
            var stats = unit.GetUnitComponent<UnitStats>();
            var state = new UnitState
            {
                unitId = unitId,
                unitName = identity?.UnitName.ToString() ?? "",
                playerId = action.playerId,
                team = (int)sim.GetTeamOf(action.playerId),
                position = action.deployCell,
                hp = stats?.HP ?? 0,
                maxHp = stats?.GetStatStruct(StatType.HP).Max ?? 0,
                attackSpeed = stats?.AttackSpeed ?? 0,
            };

            summonCommand = BattleCommand.Summon(action.playerId, action.turnNumber, 0, state);
            moraCommand = BattleCommand.StatChange(action.playerId, action.turnNumber, 1,
                BattleCommand.StatKindMora, -cost);
            GICLog.Info($"[DeployUnit] {action.playerId} 出战 {unitName} @ {action.deployCell}（摩拉 {cost}）");
            return true;
        }
    }
}
