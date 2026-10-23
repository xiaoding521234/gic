using System;
using System.Collections.Generic;
using GIC.Framework;
using GIC.Data;
namespace GIC.Battle
{
    /// <summary>
    /// 出战角色执行器（B6c，docs/18 决策七 + docs/05 §5.1）：校验链=配置存在 → 手牌成员（Host 权威）→
    /// 建筑星级链前置+建筑上限（决策八十六批C，2026-10-23 追记二改梯度为星级链，docs/active/45 §2/§3——仅建筑）→ 升命分支（**仅角色 3★+**，
    /// 建筑一律铺场新实例）→ 落点合法（**自己存活建筑锚并集**：核心 tag 锚=建筑 3/角色 2、
    /// 其它建筑锚=建筑 2/角色 1〔2026-10-23 拍板「任何自己的建筑即可为锚，部署角色比建筑减 1」〕
    /// + 碰撞判定链——2026-09-25 拍板
    /// 「根据碰撞决定」：体积绝对层最高级不可绕过、与格内单位互不阻挡才可部署、地形按常态移动类型
    /// 通行，与 MovementResolver 进入判定同构）→ 摩拉够 → 生成单位登场。卡不消耗留手牌（可重复出战）；
    /// 1~2 星角色直接铺场，3 星+角色重复出战升命座（命座 B8）。
    /// 直产命令（Summon+StatChange(Mora)），不走 BattleEffect 效应链（资源/召唤不是单位效应）。
    /// </summary>
    public static class DeployUnitExecutor
    {
        /// <summary>角色部署·核心锚半径（docs/18 决策七拍板；2026-10-23 锚点拍板后=核心锚专属值，
        /// 其它建筑锚再减 1——单源 DeployRadiusOf）</summary>
        public const int DeployRadiusFromCore = 2;

        /// <summary>建筑部署·核心锚半径（决策八十六拍板 #6：角色 2 格+1=3 格，docs/active/45 §3；
        /// 2026-10-23 锚点拍板后=核心锚专属值，其它建筑锚再减 1——单源 DeployRadiusOf）</summary>
        public const int DeployRadiusFromCoreBuilding = 3;

        /// <summary>部署半径单源（2026-10-23 拍板「任何自己的建筑即可为锚；核心 tag 的建筑才是 3，
        /// 其它建筑都是 2；部署角色比部署建筑半径减 1」）：锚=Core tag 建筑 → 建筑 3/角色 2；
        /// 锚=其它建筑 → 比核心锚各减 1（建筑 2/角色 1）。Host IsDeployCellValid/AI 配额脑/
        /// 客户端镜像（BattleHud.CollectDeployAnchorsFromSnapshot 消费侧）三处同读。</summary>
        public static int DeployRadiusOf(bool anchorIsCore, bool deployIsBuilding)
        {
            return anchorIsCore
                ? (deployIsBuilding ? DeployRadiusFromCoreBuilding : DeployRadiusFromCore)
                : (deployIsBuilding ? DeployRadiusFromCoreBuilding - 1 : DeployRadiusFromCore - 1);
        }

        /// <summary>部署锚点集合（2026-10-23 拍板）：自己（**不共享队友**——OwnerPlayerID 过滤）
        /// 存活建筑全集（含协议核心 Core tag）。锚点序=核心优先（安全区先铺），其余按 unitId 升序
        /// （确定性枚举序铁律）。客户端镜像=BattleHud.CollectDeployAnchorsFromSnapshot（快照同口径）。
        /// 核心被拆=判负局终，无锚场景仅作防御性空集处理（一切落点不合法）</summary>
        public static List<(BattleCell cell, bool isCore)> CollectDeployAnchors(BattleSimState sim, string playerId)
        {
            var result = new List<(BattleCell, bool)>();
            List<(string unitId, BattleCell cell)> others = null;
            foreach (var kv in sim.Units)
            {
                var unit = kv.Value;
                if (unit == null || BattleSimState.IsDead(unit)) continue;
                var identity = unit.GetUnitComponent<UnitIdentity>();
                if (identity == null || identity.OwnerPlayerID != playerId) continue;
                var data = unit.RawData;
                if (data == null || data.unitType != UnitType.Building) continue;
                var cell = sim.GetPosition(kv.Key);
                if (BattleHeuristics.IsCore(unit))
                {
                    result.Add((cell, true)); // 核心锚优先登记（锚半径 3/2）
                    continue;
                }
                if (others == null) others = new List<(string, BattleCell)>();
                others.Add((kv.Key, cell));
            }
            if (others != null)
            {
                others.Sort((a, b) => string.CompareOrdinal(a.unitId, b.unitId));
                foreach (var o in others) result.Add((o.cell, false));
            }
            return result;
        }

        /// <summary>建筑部署数量上限（决策八十六拍板 #4：自己存活建筑总数〔不含协议核心〕；
        /// 拆掉即释放名额可重建——消散机制保证在场必存活，docs/active/45 §2）</summary>
        public const int MaxBuildingsPerPlayer = 3;

        /// <summary>部署落点合法（角色/建筑统一）：**自己存活建筑锚并集**半径内（2026-10-23 拍板
        /// 「任何自己的建筑即可为锚」：核心 tag 锚=建筑 3/角色 2、其它建筑锚=建筑 2/角色 1，切比雪夫）
        /// + 地形层（按部署单位常态移动类型）+ 碰撞共享原语
        /// （体积绝对层+阻挡规则层=MovementResolver.PassesVolumeLimit/PassesBlockingRules——与移动进入
        /// 判定**同源**，2026-10-02 复审收口，碰撞口径改动只改原语处）：① 体积绝对层（无视阻挡配置
        /// 不可绕过）；② 阻挡规则层（与格内单位互相阻挡即不可部署）。含尸体——尸体保留碰撞/体积
        /// （消散单位已移除天然不占格）。客户端镜像预判=BattleHud.CanDeployEnterPreview（快照静态口径，
        /// Host 结算兜底）</summary>
        public static bool IsDeployCellValid(BattleSimState sim, string playerId, BattleCell cell,
            UnitConfig.UnitData deployData)
        {
            if (!sim.Map.HasTile(cell.x, cell.y)) return false;
            // 锚点层（2026-10-23 拍板）：落点须落在任一自己存活建筑锚的部署半径内——
            // 多锚并集取代旧「核心单锚」（锚序/半径=CollectDeployAnchors/DeployRadiusOf 单源）
            bool deployIsBuilding = deployData != null && deployData.unitType == UnitType.Building;
            bool withinAnchor = false;
            foreach (var anchor in CollectDeployAnchors(sim, playerId))
            {
                int radius = DeployRadiusOf(anchor.isCore, deployIsBuilding);
                if (Math.Max(Math.Abs(cell.x - anchor.cell.x), Math.Abs(cell.y - anchor.cell.y)) <= radius)
                {
                    withinAnchor = true;
                    break;
                }
            }
            if (!withinAnchor) return false;

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

            // ==================== 建筑星级链/上限前置（决策八十六批C，2026-10-23 追记二：梯度制改星级链）====================
            // 建筑**永不升命**（拍板 #4「建筑无命座」+冲突勘正「3★+升命分支对建筑不适用」）：
            // 重复部署=新实例，走铺场链；星级链/上限先于落点与摩拉校验（不满足=零成本拦截）
            if (data.unitType == UnitType.Building)
            {
                if (!PassesBuildingStarGate(sim, action.playerId, data))
                {
                    GICLog.Info($"[DeployUnit] {action.playerId} 部署 {unitName} 被星级链前置拦截（上一星级存活建筑不在场）");
                    return false;
                }
                if (!PassesBuildingLimit(sim, action.playerId, data))
                {
                    GICLog.Info($"[DeployUnit] {action.playerId} 部署 {unitName} 被建筑上限拦截（该种建筑存活 {MaxBuildingsPerPlayer} 座）");
                    return false;
                }
            }

            // ==================== 升命座分支（B8 批 2026-09-30，docs/05 §5.2+docs/09）====================
            // 3★+同名重复出战=提升命座（docs/05 §5.4：尸体也仅升命不复苏；封顶 MaxConstellation=3）——
            // 不生成新单位、扣等同出战摩拉、占部署族配额（ActionType 仍=DeployUnit，语义由同名在场判定）。
            // **仅角色**（建筑分支已提前分流——建筑重复部署=铺场新实例）
            var existingUnit = FindOwnUnitByName(sim, action.playerId, unitName);
            if (existingUnit != null && data.starLevel >= 3 && data.unitType != UnitType.Building)
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
                GICLog.Info($"[DeployUnit] {unitName} 落点 {action.deployCell} 不合法（自己建筑锚半径内+碰撞判定），部署落空");
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

        // ==================== 建筑星级链/上限（决策八十六批C，2026-10-23 追记二：梯度制退役）====================

        /// <summary>建筑部署星级链前置（2026-10-23 追记二拍板，取代原科技梯度拍板 #3）：部署 1★=自己有
        /// 存活的 Core tag 建筑在场（核心开局在场、被打掉即判负，恒满足仍显式校验）；部署 N★(N≥2)=
        /// 自己有存活 **N-1★ 建筑**在场（任意同名——部署 2★ 要 1★、3★ 要 2★，以此类推；高星不抵低星：
        /// 旗帜全灭时 2★ 也不可部署，须先重铺 1★）。**不共享队友**（2v2 各算各——一律按 OwnerPlayerID
        /// 过滤不走 TeamType）；核心 5★ 恒≠N-1(≤4) 天然不占链位，IsCore 排除为防御性冗余。
        /// 客户端镜像=BattleHud.PassesBuildingStarPreview（快照同口径）</summary>
        public static bool PassesBuildingStarGate(BattleSimState sim, string playerId, UnitConfig.UnitData data)
        {
            if (data == null || data.unitType != UnitType.Building) return true; // 非建筑不查
            if (data.starLevel <= 1)
            {
                // 1★ 前置=自己有存活 Core tag 建筑（含协议核心——恒满足仍显式校验）
                foreach (var kv in sim.Units)
                {
                    var identity = kv.Value.GetUnitComponent<UnitIdentity>();
                    if (identity == null || identity.OwnerPlayerID != playerId) continue;
                    if (!BattleHeuristics.IsCore(kv.Value) || BattleSimState.IsDead(kv.Value)) continue;
                    return true;
                }
                return false;
            }
            var requiredStar = data.starLevel - 1;
            foreach (var kv in sim.Units)
            {
                var identity = kv.Value.GetUnitComponent<UnitIdentity>();
                if (identity == null || identity.OwnerPlayerID != playerId) continue;
                var unitData = kv.Value.RawData;
                if (unitData == null || unitData.unitType != UnitType.Building) continue;
                if (BattleHeuristics.IsCore(kv.Value) || BattleSimState.IsDead(kv.Value)) continue;
                if (unitData.starLevel == requiredStar) return true;
            }
            return false;
        }

        /// <summary>每种建筑上限（拍板 #4 勘正 2026-10-22：**每种**同名建筑各自 ≤MaxBuildingsPerPlayer(3)——
        /// 3 座旗帜+3 座餐馆同场合法；自己存活的该种（同名、UnitName 相等）非 Core tag 建筑计数；
        /// 消散不留尸体、IsDead 守卫兜底；拆掉立即释放名额。客户端镜像=BattleHud.PassesBuildingLimitPreview
        /// （快照同口径）</summary>
        public static bool PassesBuildingLimit(BattleSimState sim, string playerId, UnitConfig.UnitData data)
        {
            if (data == null || data.unitType != UnitType.Building) return true; // 非建筑不查
            int count = 0;
            foreach (var kv in sim.Units)
            {
                var identity = kv.Value.GetUnitComponent<UnitIdentity>();
                if (identity == null || identity.OwnerPlayerID != playerId) continue;
                if (identity.UnitName != data.unitName) continue; // 同种（同名）计数
                if (BattleHeuristics.IsCore(kv.Value) || BattleSimState.IsDead(kv.Value)) continue;
                count++;
            }
            return count < MaxBuildingsPerPlayer;
        }

        // ==================== 召唤原语（决策八十六批D，docs/active/45 §1.1 骑士团）====================

        /// <summary>最近可部署格召唤（拍板 #10「自己格占满→最近可部署格顺延」）：BFS 距离场从锚点
        /// 扩散（固定方向序=CrossDirections 枚举序、距离升序=确定性），取首个合法格（地形按召唤单位
        /// 常态移动类型+体积+碰撞原语——与 IsDeployCellValid 的格内判定同源，**不带核心半径**：
        /// 召唤锚=建筑格非核心）；找不到合法格=召唤落空零动作。消费方=骑士团部署即召+周期召唤。
        /// exploreVision 揭雾预热走 Summon 命令既有链（命令携带 exploreVision）</summary>
        public static bool TrySummonAtNearestCell(BattleSimState sim, string playerId, UnitName unitName,
            BattleCell origin, out BattleCommand summonCommand)
        {
            summonCommand = null;
            var config = Wargame.Instance?.Context?.Get<UnitConfig>();
            var data = config?.GetUnitData(unitName);
            if (data == null) return false;
            if (!TryFindNearestValidCell(sim, playerId, origin, data, out var cell)) return false;

            var unit = UnitFactory.CreateUnitWithData(data);
            if (unit == null) return false;
            if (sim.LogicRoot != null)
                unit.transform.SetParent(sim.LogicRoot, false);
            var unitId = sim.RegisterUnit(unit, playerId, sim.GetTeamOf(playerId), cell);
            var state = sim.BuildUnitState(unitId, unit); // 全量 UnitState 单一出口（同部署链）
            summonCommand = BattleCommand.Summon(playerId, 0, 0, state); // sliceIndex/indexInSlice 由调用方回填
            GICLog.Info($"[DeployUnit] {playerId} 召唤 {unitName} @ {cell}（最近格顺延）");
            return true;
        }

        /// <summary>BFS 距离场最近合法格（origin 自身优先；固定方向序+距离升序=确定性——
        /// 同距平局按扩散序先到先得；与 MovementResolver 原语同源的格内合法性，无核心半径约束）</summary>
        private static bool TryFindNearestValidCell(BattleSimState sim, string playerId, BattleCell origin,
            UnitConfig.UnitData data, out BattleCell cell)
        {
            cell = default;
            if (CellCanHost(sim, playerId, origin, data)) { cell = origin; return true; }
            var visited = new HashSet<BattleCell> { origin };
            var frontier = new List<BattleCell> { origin };
            int mapWidth = sim.Map.width;   // 越界快筛（HasTile 终判）
            int mapHeight = sim.Map.height;
            while (frontier.Count > 0)
            {
                var next = new List<BattleCell>();
                foreach (var c in frontier)
                {
                    foreach (var dir in BattleHeuristics.CrossDirections)
                    {
                        int nx = c.x + SkillHitResolver.DirectionToDelta(dir).x;
                        int ny = c.y + SkillHitResolver.DirectionToDelta(dir).y;
                        if (nx < 0 || ny < 0 || nx >= mapWidth || ny >= mapHeight) continue;
                        var n = new BattleCell(nx, ny);
                        if (!visited.Add(n)) continue;
                        if (!CellCanHost(sim, playerId, n, data)) { next.Add(n); continue; } // 不可停仍扩散穿过
                        cell = n;
                        return true;
                    }
                }
                frontier = next;
            }
            return false;
        }

        /// <summary>召唤单位可否落格（地形+体积+碰撞——与 IsDeployCellValid 格内判定同源；
        /// 含尸体占用；无核心半径〔召唤锚=建筑格〕）</summary>
        private static bool CellCanHost(BattleSimState sim, string playerId, BattleCell cell, UnitConfig.UnitData data)
        {
            if (!sim.Map.HasTile(cell.x, cell.y)) return false;
            if (!sim.Map.IsPassable(cell.x, cell.y, data.normalMoveType)) return false;
            var occupants = sim.GetUnitsAt(cell);
            if (!MovementResolver.PassesVolumeLimit(occupants, data.GetVolume())) return false;
            return MovementResolver.PassesBlockingRules(occupants, sim.GetTeamOf(playerId),
                data.与友方互不阻挡, data.blockedByEnemies);
        }
    }
}
