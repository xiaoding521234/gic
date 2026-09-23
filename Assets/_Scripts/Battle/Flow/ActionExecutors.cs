using System;
using System.Collections.Generic;
using GIC.Framework;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 移动行动执行器：构建移动者状态，实际结算由 MovementResolver 同片同步逐步展开。
    /// 移动是特殊的技能（2026-09-23 B-S1b 拍板）：步数上限=移动技能条目 MoveDistance 参数
    /// （skills[0]·Move 型，数据驱动与 HUD 瞄准同源）；无移动技能/无参数回落 3（旧默认）。
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
                RemainingSteps = Math.Min(Math.Max(0, action.moveMagnitude), MaxMoveDistance(unit)),
            };
            return mover;
        }

        /// <summary>
        /// 移动步数上限（Host 权威收口）：移动技能 MoveDistance 按基准换算（2026-09-23 用户拍板
        /// 「拼接后为10%移速」——BasedOnMoveSpeed=百分比×当前移速，SkillData.ResolveMoveDistance；
        /// 安柏 50 移速→5 格、凯亚 30→3 格）；移速读 UnitStats 当前值（含 Buff——减速缩短移动距离）。
        /// </summary>
        public static int MaxMoveDistance(Unit unit)
        {
            if (unit?.Skills != null)
            {
                foreach (var skill in unit.Skills)
                {
                    if (skill?.RawData?.skillType != SkillType.Move) continue;
                    int moveSpeed = unit.GetUnitComponent<UnitStats>()?.MoveSpeed ?? 30;
                    return skill.RawData.ResolveMoveDistance(moveSpeed);
                }
            }
            return 3; // 无移动技能条目=旧默认回落
        }
    }

    /// <summary>
    /// 技能行动执行器（B4：正式技能链——ActionData.skillIndex 索引 unit.Skills（=UnitConfig.skills
    /// 顺序创建），BaseSkill.ResolveEffects 纯结算产出效应；DebugAttackSkill 已退役）。
    /// 单位指向型技能只对目标单位生效（不适用格子判定，docs/05 §5.3）；允许鞭尸。
    /// 目标校验读片前快照（瞬发效应按片初状态结算）。
    /// 时轮（B-S1）：通过全部门槛的施放各产一条 SkillCast 命令（castsOut，段内最前发射——
    /// 客户端时轮演出起点；元能不足/不可施放=行动落空，不产施放事件）。
    /// </summary>
    public static class SkillExecutor
    {
        public static List<BattleEffect> Resolve(BattleSimState sim, ActionData action, BattleSnapshot sliceSnapshot,
            List<BattleCommand> castsOut = null)
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

            // 时轮施放事件（B-S1）：施放者片初位置=发射格/朝向参考（sliceIndex/indexInSlice 由发射出口回填）
            if (castsOut != null)
            {
                var casterState = SkillHitResolver.FindUnitState(sliceSnapshot, action.unitId);
                castsOut.Add(BattleCommand.SkillCast(action.unitId, 0, 0,
                    (int)skill.RawData.skillID, (int)action.direction,
                    casterState != null ? casterState.position : BattleCell.zero));
            }

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
        /// 校验链=配置存在 → 手牌成员（Host 权威）→ 落点合法 → 摩拉够。
        /// </summary>
        public static bool TryResolve(BattleSimState sim, ActionData action, out BattleCommand summonCommand,
            out BattleCommand moraCommand)
        {
            summonCommand = null;
            moraCommand = null;

            var config = Wargame.Instance?.Context?.Get<UnitConfig>();
            var unitName = (UnitName)action.deployUnitName;
            var data = config?.GetUnitData(unitName);
            if (data == null)
            {
                GICLog.Warn($"[DeployUnit] UnitConfig 无 {unitName}，部署落空");
                return false;
            }

            // 手牌成员校验（2026-09-23 审查 Y2）：部署角色必须在玩家手牌内——本地 UI 只给手牌卡入口
            // 不可见，但 Host 权威体系下客户端可凭空上交任意角色名（B7 LAN 前必须收口）
            var hand = sim.GetHand(action.playerId);
            bool inHand = false;
            if (hand != null)
            {
                foreach (var card in hand)
                {
                    if (card.cardType == CardType.Unit && card.value == action.deployUnitName)
                    {
                        inHand = true;
                        break;
                    }
                }
            }
            if (!inHand)
            {
                GICLog.Warn($"[DeployUnit] {action.playerId} 手牌无 {unitName}，部署拒绝");
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

            // 全量 UnitState 单一出口（2026-09-23 审查 Y3：与快照同构——登场回合附着/元能/防御
            // 等字段不缺，客户端建 view 与下回合快照零偏差）
            var state = sim.BuildUnitState(unitId, unit);

            // sliceIndex/indexInSlice 传 0 占位——ResolveDeploySegment 产出时统一回填真实值（2026-09-23：
            // 旧代码误传 turnNumber 进 sliceIndex 参数，语义误导）
            summonCommand = BattleCommand.Summon(action.playerId, 0, 0, state);
            moraCommand = BattleCommand.StatChange(action.playerId, 0, 0,
                BattleCommand.StatKindMora, -cost);
            GICLog.Info($"[DeployUnit] {action.playerId} 出战 {unitName} @ {action.deployCell}（摩拉 {cost}）");
            return true;
        }
    }
}
