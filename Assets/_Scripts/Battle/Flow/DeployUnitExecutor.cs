using System;
using System.Collections.Generic;
using GIC.Framework;
using GIC.Data;
namespace GIC.Battle
{
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
        /// 执行部署：校验+扣费+生成单位。成功=true 且返回命令（Summon+StatChange 摩拉变化；
        /// 升命跨层授予物品时追加 ItemGain 命令列表——安柏3命兔兔伯爵，装备批）。
        /// 校验链=配置存在 → 手牌成员（Host 权威）→ 落点合法 → 摩拉够。
        /// </summary>
        public static bool TryResolve(BattleSimState sim, ActionData action, out BattleCommand summonCommand,
            out BattleCommand moraCommand, out List<BattleCommand> itemGainCommands)
        {
            summonCommand = null;
            moraCommand = null;
            itemGainCommands = null;

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
                sim.TryGetUnitId(existingUnit, out var upgradedId);
                if (!string.IsNullOrEmpty(upgradedId))
                {
                    summonCommand = BattleCommand.UpgradeConstellation(action.playerId, upgradedId, 0, 0,
                        existingUnit.ConstellationLevel);
                }
                moraCommand = BattleCommand.StatChange(action.playerId, 0, 0,
                    BattleCommand.StatKindMora, -upgradeCost);
                // 命座跨层物品授予（装备批 2026-10-18·安柏3命兔兔伯爵；同日追拍「直接装备优先」）：
                // GrantItem 原子首次达到 minConstellation 层——**武器/配件直接装备/补充耐久到该单位**
                //（决策八十一：已装备同物→补充既有槽，满耐久或槽满才 GainCard 入手牌）；命令层
                // Equipped→EquipItem（客户端血条右侧图标即时出现/角标更新）/手牌→ItemGain（手牌镜像
                // ——**耐久载荷=配置满值**，与 Host GainCard 新鲜条目一致），indexInSlice 由 ResolveDeploySegment 统一回填
                var granted = ConstellationApplier.ApplyUnlockedItemGrants(sim, existingUnit,
                    existingUnit.ConstellationLevel - 1, existingUnit.ConstellationLevel);
                if (granted.Count > 0)
                {
                    itemGainCommands = new List<BattleCommand>();
                    foreach (var grant in granted)
                    {
                        if (grant.equipped && !string.IsNullOrEmpty(upgradedId))
                            itemGainCommands.Add(BattleCommand.EquipItem(action.playerId, upgradedId, 0, 0,
                                grant.item, grant.slotKind, grant.slotIndex, grant.durabilityLeft));
                        else
                            itemGainCommands.Add(BattleCommand.ItemGain(action.playerId, 0, 0, grant.item, 1,
                                UnitEquipment.MaxDurabilityOf((ItemName)grant.item)));
                    }
                }
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
        /// 3★+ 同名在场即升命，1~2★ 不适用〔眷属重复出战=加单位〕）。
        /// AI 配额脑部署/升命候选同读此单源（AI-1 批公开——防两处手写同构漂移）</summary>
        public static Unit FindOwnUnitByName(BattleSimState sim, string playerId, UnitName unitName)
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
