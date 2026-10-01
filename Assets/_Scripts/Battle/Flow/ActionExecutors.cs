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

        /// <summary>移动消耗声明（统一消耗模型 C-2，docs/active/30）：移动技能条目的 costs（C-2 起全移动
        /// 技能资产已配 Stamina 条目）——**无移动技能条目单位走常量兜底**（配额行动 10 体力，规则真源
        /// =BattleMetrics.StaminaCostPerAction；2026-09-28 盘点 21/34 单位无 Move 条目=兜底是主路径非边角）。
        /// 返回共享只读实例（HasAll/ChargeAll 只读消费，调用方勿改）。
        /// 有 Move 条目但未声明消耗=免费移动（costs 空=无消耗语义，数据即事实）</summary>
        public static List<SkillCostEntry> GetMoveCosts(Unit unit)
        {
            if (unit?.Skills != null)
            {
                foreach (var skill in unit.Skills)
                {
                    if (skill?.RawData?.skillType != SkillType.Move) continue;
                    return skill.RawData.HasCosts ? skill.RawData.costs : null;
                }
            }
            return FallbackMoveCosts;
        }

        private static readonly List<SkillCostEntry> FallbackMoveCosts = new()
        {
            new SkillCostEntry { kind = CostKind.Stamina, amount = BattleMetrics.StaminaCostPerAction }
        };
    }

    /// <summary>
    /// 技能行动执行器（B4：正式技能链——ActionData.skillIndex 索引 unit.Skills（=UnitConfig.skills
    /// 顺序创建），BaseSkill.ResolveEffects 纯结算产出效应；DebugAttackSkill 已退役）。
    /// 单位指向型技能只对目标单位生效（不适用格子判定，docs/05 §5.3）；允许鞭尸。
    /// 目标校验读片前快照（瞬发效应按片初状态结算）。
    /// 时轮（B-S1）：通过全部门槛的施放各产一条 SkillCast 命令（castsOut，段内最前发射——
    /// 客户端时轮演出起点；元能不足/不可施放=行动落空，不产施放事件）。
    /// 目标失效口径（2026-09-27 拍板 docs/18 决策十四，A 案=维持）：门槛过后目标在结算时已亡
    /// （同时制必然场景——选择时有效、执行时死亡）=行动已使用——体力元能照扣、SkillCast 照播；
    /// 与「门槛不足=落空不扣」为并行口径（不足=未获执行权；失效=已执行无效果）。
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

            // 可施放检查前置（2026-09-25 三轮审查 S6 正序）：占位/不可施放技能先拦，再查消耗
            // ——原顺序会让不可施放的占位技能白扣 10 体力后才落空（UI 置灰防了常规路径，异常上交仍会撞）
            if (!skill.CanCast(attacker))
            {
                GICLog.Info($"[SkillExecutor] 单位 {action.unitId} 技能 {skill.RawData?.skillID} 不可施放，行动落空");
                return effects;
            }

            // ==================== 消耗段（统一消耗模型 C-2：全量迁移完成，双轨回落退役）====================
            // 消耗单源=SkillData.costs（docs/active/30）：先全查后全扣（多 cost 防「扣了体力才发现苹果不够」
            // ——任一不足=行动落空且不登记任何消耗）；**costs 空=免费技能**（无消耗语义，数据即事实）
            if (!ResourceGate.HasAll(sim, attacker, action.playerId, skill.RawData?.costs, out var missing))
            {
                string missingDesc = missing.kind == CostKind.Item
                    ? missing.item.ToString()
                    : missing.kind == CostKind.AnyItem
                        ? $"任意{missing.subType}"
                        : missing.kind.ToString();
                GICLog.Info($"[SkillExecutor] 单位 {action.unitId} 技能 {skill.RawData?.skillID} 消耗不足" +
                            $"（{missingDesc}×{missing.amount}），行动落空");
                return effects;
            }
            ResourceGate.ChargeAll(sim, attacker, action.playerId, skill.RawData?.costs, effects);

            effects.AddRange(skill.ResolveEffects(sim, action, sliceSnapshot));

            // 时轮施放事件（B-S1）：施放者片初位置=发射格/朝向参考（sliceIndex/indexInSlice 由发射出口回填）；
            // 技能元素随命令下发（箭雨染色单源——丘丘人借凯亚霜袭=冰色箭雨非施法者物理灰）
            if (castsOut != null)
            {
                var casterState = SkillHitResolver.FindUnitState(sliceSnapshot, action.unitId);
                castsOut.Add(BattleCommand.SkillCast(action.unitId, 0, 0,
                    (int)skill.RawData.skillID, (int)action.direction,
                    casterState != null ? casterState.position : BattleCell.zero,
                    (int)SkillHitResolver.ResolveProjectileElement(sim, action)));
            }

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
    /// 出战角色执行器（B6c，docs/18 决策七 + docs/05 §5.1）：校验链=配置存在 → 手牌成员（Host 权威）→
    /// 落点合法（核心半径 2 内 + 碰撞判定链——2026-09-25 拍板「根据碰撞决定」：体积绝对层最高级不可绕过、
    /// 与格内单位互不阻挡才可部署、地形按常态移动类型通行，与 MovementResolver 进入判定同构）→ 摩拉够 →
    /// 生成单位登场。卡不消耗留手牌（可重复出战）；1~2 星直接铺场，3 星+重复出战升命座（命座 B8，当前重复铺场）。
    /// 直产命令（Summon+StatChange(Mora)），不走 BattleEffect 效应链（资源/召唤不是单位效应）。
    /// </summary>
    public static class DeployUnitExecutor
    {
        /// <summary>角色部署核心半径（docs/18 决策七拍板；建筑=核心半径 3/建筑半径 2 属 DeployBuilding 后续批次）</summary>
        public const int DeployRadiusFromCore = 2;

        /// <summary>部署落点合法（角色）：协议核心半径 2 内（切比雪夫；核心位置代理=出生区中心，B8 换真核心）
        /// + 地形层（按部署单位常态移动类型）+ 碰撞共享原语（体积绝对层+阻挡规则层=
        /// MovementResolver.PassesVolumeLimit/PassesBlockingRules——与移动进入判定**同源**，
        /// 2026-10-02 复审收口，碰撞口径改动只改原语处）：① 体积绝对层（无视阻挡配置不可绕过）；
        /// ② 阻挡规则层（与格内任一单位互相阻挡即不可部署）。含尸体——尸体保留碰撞/体积。
        /// 客户端镜像预判=BattleHud.CanDeployEnterPreview（快照静态口径，Host 结算兜底）</summary>
        public static bool IsDeployCellValid(BattleSimState sim, string playerId, BattleCell cell,
            UnitConfig.UnitData deployData)
        {
            if (!sim.Map.HasTile(cell.x, cell.y)) return false;
            var core = sim.GetCorePosition(playerId);
            int distance = Math.Max(Math.Abs(cell.x - core.x), Math.Abs(cell.y - core.y));
            if (distance > DeployRadiusFromCore) return false;

            // 地形层：按部署单位常态移动类型（步行不可入水等，与移动进入判定同构）
            var forceType = deployData?.normalMoveType ?? ForceType.Walk;
            if (!sim.Map.IsPassable(cell.x, cell.y, forceType)) return false;

            // 体积绝对层+阻挡规则层（共享原语，2026-10-02 复审收口——与移动进入判定同源：原两处
            // 平行手写的同构链收单源，碰撞口径改动只改原语处）：自身规格从 UnitData 取（部署单位
            // 尚未生成）——体积真源=UnitData.GetVolume() 单出口（与 Unit.Volume 同读，2026-09-27
            // 复审收口双源）、豁免/被挡=碰撞配置字段（与移动侧 UnitMoveable 组件同源字段）
            var occupants = sim.GetUnitsAt(cell); // 含尸体——尸体保留碰撞/体积
            if (!MovementResolver.PassesVolumeLimit(occupants, deployData != null ? deployData.GetVolume() : 1))
                return false;
            return MovementResolver.PassesBlockingRules(occupants, sim.GetTeamOf(playerId),
                deployData != null && deployData.与友方互不阻挡,
                deployData != null && deployData.blockedByEnemies);
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
                    if (card.IsUnit && card.value == action.deployUnitName)
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

            // ==================== 升命座分支（B8 批 2026-09-30，docs/05 §5.2+docs/09）====================
            // 3★+同名重复出战=提升命座（docs/05 §5.4：尸体也仅升命不复苏；封顶 MaxConstellation=3）——
            // 不生成新单位、扣等同出战摩拉、占部署族配额（ActionType 仍=DeployUnit，语义由同名在场判定）
            var existingUnit = FindOwnUnitByName(sim, action.playerId, unitName);
            if (existingUnit != null && data.starLevel >= 3)
            {
                if (existingUnit.ConstellationLevel >= ConstellationApplier.MaxConstellation)
                {
                    GICLog.Info($"[DeployUnit] {unitName} 已满命（{ConstellationApplier.MaxConstellation}），升命落空");
                    return false;
                }
                int upgradeCost = data.GetEffectiveDeployCost();
                if (!sim.TrySpendMora(action.playerId, upgradeCost))
                {
                    GICLog.Info($"[DeployUnit] {action.playerId} 摩拉不足（{sim.GetMora(action.playerId)}/{upgradeCost}），升命落空");
                    return false;
                }
                existingUnit.ConstellationLevel++;
                ConstellationApplier.ApplyPassives(existingUnit); // 幂等重算（全撤→按新层重挂）
                if (sim.TryGetUnitId(existingUnit, out var upgradedId))
                {
                    summonCommand = BattleCommand.UpgradeConstellation(action.playerId, upgradedId, 0, 0,
                        existingUnit.ConstellationLevel);
                }
                moraCommand = BattleCommand.StatChange(action.playerId, 0, 0,
                    BattleCommand.StatKindMora, -upgradeCost);
                GICLog.Info($"[DeployUnit] {action.playerId} 升命 {unitName} → C{existingUnit.ConstellationLevel}（摩拉 {upgradeCost}）");
                return true;
            }

            if (!IsDeployCellValid(sim, action.playerId, action.deployCell, data))
            {
                GICLog.Info($"[DeployUnit] {unitName} 落点 {action.deployCell} 不合法（核心半径 {DeployRadiusFromCore} 内+碰撞判定），部署落空");
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

        /// <summary>玩家场上同名单位（含尸体——升命对尸体同样生效〔docs/05 §5.4 仅升命不复苏〕；
        /// 3★+ 同名在场即升命，1~2★ 不适用〔眷属重复出战=加单位〕）</summary>
        private static Unit FindOwnUnitByName(BattleSimState sim, string playerId, UnitName unitName)
        {
            foreach (var kv in sim.Units)
            {
                var identity = kv.Value.GetUnitComponent<UnitIdentity>();
                if (identity == null || identity.OwnerPlayerID != playerId) continue;
                if (identity.UnitName == unitName) return kv.Value;
            }
            return null;
        }
    }
}
