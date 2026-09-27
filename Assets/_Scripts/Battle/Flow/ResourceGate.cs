using System.Collections.Generic;
using GIC.Framework;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 统一消耗门（统一消耗模型，docs/active/30 §2.2——StaminaGate 的泛化收编；
    /// **C-2 存量迁移完成（2026-09-28）：StaminaGate/GetStaminaCost/GetEnergyCost 已退役删除，
    /// 本门为消耗检查/登记唯一出口**——移动走 MoveExecutor.GetMoveCosts（无 Move 条目单位常量兜底）。
    /// 资源路由（docs/18 决策十五）：
    /// - Energy（单位级）：Has=HasEnoughEnergy（门槛=消耗值非上限，B6a）；Charge=EnergyEffect(-, CategoryCost)
    /// - Stamina/Mora（玩家账户）：Has=池数量判定；Charge=StaminaEffect/MoraSpendEffect
    /// - Item（玩家手牌，指定物品）：Has=条目 count；Charge=ItemConsumeEffect(指定)
    /// - AnyItem（玩家手牌，同类任意）：Has=**跨同类条目聚合判定**（3 苹果酒+2 果汁可付「任意饮品×4」）；
    ///   Charge=ItemConsumeEffect(同类任意)——应用时按手牌列表序确定性逐条扣（零随机）、
    ///   **原子性**（防御路径不足额=不扣分毫零命令）；**货币卡（摩拉/体力）不可被 AnyItem 匹配**
    ///   （账户资源不经物品消耗链——LoseCard(货币) 会动资源池，属语义错误）
    /// 门槛语义：不足=行动落空且不扣（调用方先对 costs 全条目 Has 再全条目 Charge——防「扣了体力才发现苹果不够」）；
    /// 低级单位豁免玩家资源消耗（体力先例推广 Mora/Item/AnyItem——1~2 星自主行动不耗玩家任何资源；元能不豁免）。
    /// </summary>
    public static class ResourceGate
    {
        /// <summary>
        /// 门槛检查（纯查不改）：本条消耗是否可支付。
        /// </summary>
        /// <param name="actor">行动单位（Energy 判定用；星级豁免判定用）</param>
        /// <param name="playerId">资源归属玩家（Stamina/Mora/Item/AnyItem 判定用）</param>
        public static bool Has(BattleSimState sim, Unit actor, string playerId, SkillCostEntry cost)
        {
            if (cost == null || cost.amount <= 0) return true;
            switch (cost.kind)
            {
                case CostKind.Energy:
                    return BattleSimState.HasEnoughEnergy(actor, cost.amount);
                case CostKind.Stamina:
                    if (!BattleHeuristics.IsMajorUnit(actor)) return true; // 低级单位豁免（体力先例口径）
                    return sim.HasEnoughStamina(playerId, cost.amount);
                case CostKind.Mora:
                    if (!BattleHeuristics.IsMajorUnit(actor)) return true;
                    return sim.GetMora(playerId) >= cost.amount;
                case CostKind.Item:
                    if (!BattleHeuristics.IsMajorUnit(actor)) return true;
                    var hand = sim.GetHand(playerId);
                    if (hand == null) return false;
                    var target = new CardId(cost.item);
                    foreach (var entry in hand)
                    {
                        if ((CardType)entry.cardType == target.cardType && entry.value == target.value)
                            return entry.count >= cost.amount;
                    }
                    return false;
                case CostKind.AnyItem:
                    if (!BattleHeuristics.IsMajorUnit(actor)) return true;
                    return CountAnyItems(sim, playerId, cost.subType) >= cost.amount;
                default:
                    GICLog.Warn($"[ResourceGate] 未接路由的消耗种类 {cost.kind}——按可支付处理（请补路由）");
                    return true;
            }
        }

        /// <summary>
        /// 消耗登记（门槛已过）：向 effects 追加对应消耗效应（随片统一应用→命令产出→对账）。
        /// </summary>
        public static void Charge(BattleSimState sim, Unit actor, string playerId, SkillCostEntry cost,
            List<BattleEffect> effects)
        {
            if (cost == null || cost.amount <= 0) return;
            switch (cost.kind)
            {
                case CostKind.Energy:
                    effects.Add(new EnergyEffect(actor != null
                        ? actor.GetUnitComponent<UnitIdentity>()?.UnitID : null, -cost.amount, EnergyEffect.CategoryCost));
                    break;
                case CostKind.Stamina:
                    if (BattleHeuristics.IsMajorUnit(actor))
                        effects.Add(new StaminaEffect(playerId, -cost.amount));
                    break;
                case CostKind.Mora:
                    if (BattleHeuristics.IsMajorUnit(actor))
                        effects.Add(new MoraSpendEffect(playerId, cost.amount));
                    break;
                case CostKind.Item:
                    if (BattleHeuristics.IsMajorUnit(actor))
                        effects.Add(new ItemConsumeEffect(playerId, cost.item, cost.amount));
                    break;
                case CostKind.AnyItem:
                    if (BattleHeuristics.IsMajorUnit(actor))
                        effects.Add(new ItemConsumeEffect(playerId, cost.subType, cost.amount));
                    break;
            }
        }

        /// <summary>
        /// costs 全条目门槛检查（SkillExecutor 消耗段第一段：任一不足=行动落空且不登记任何消耗）
        /// </summary>
        public static bool HasAll(BattleSimState sim, Unit actor, string playerId,
            System.Collections.Generic.List<SkillCostEntry> costs, out SkillCostEntry missing)
        {
            missing = null;
            if (costs == null) return true;
            foreach (var cost in costs)
            {
                if (Has(sim, actor, playerId, cost)) continue;
                missing = cost;
                return false;
            }
            return true;
        }

        /// <summary>costs 全条目消耗登记（第二段：门槛全过后调用）</summary>
        public static void ChargeAll(BattleSimState sim, Unit actor, string playerId,
            System.Collections.Generic.List<SkillCostEntry> costs, List<BattleEffect> effects)
        {
            if (costs == null) return;
            foreach (var cost in costs)
                Charge(sim, actor, playerId, cost, effects);
        }

        // ==================== AnyItem 匹配原语（Host 判定与效应应用共用） ====================

        /// <summary>同类物品聚合数量（AnyItem Has 用）：跨手牌条目累加——货币卡不可被匹配（账户资源不经物品消耗链）</summary>
        public static int CountAnyItems(BattleSimState sim, string playerId, ItemSubType subType)
        {
            if (subType == ItemSubType.Currency)
            {
                GICLog.Warn($"[ResourceGate] AnyItem 配了 Currency 子类型——货币=账户资源不经物品消耗链，恒不可匹配（请修正 SkillConfig）");
                return 0;
            }
            var hand = sim.GetHand(playerId);
            if (hand == null) return 0;
            var itemConfig = Wargame.Instance?.Context?.Get<ItemConfig>();
            if (itemConfig == null)
            {
                GICLog.Warn("[ResourceGate] 容器无 ItemConfig（ConfigManager 未构建？）——AnyItem 判定不可用");
                return 0;
            }
            int total = 0;
            foreach (var entry in hand)
            {
                if ((CardType)entry.cardType != CardType.Item) continue;
                var name = (ItemName)entry.value;
                if (name == ItemName.Mora || name == ItemName.Stamina) continue; // 货币卡不可被 AnyItem 匹配
                var data = itemConfig.GetItemData(name);
                if (data != null && data.subType == subType)
                    total += entry.count;
            }
            return total;
        }

        /// <summary>同类物品条目收集（AnyItem 应用用）：手牌列表序=确定性扣减顺序（零随机）。
        /// **物化列表返回（非惰性枚举）**——扣减循环内 LoseCard 会移除手牌条目，惰性枚举=集合修改异常</summary>
        public static List<(ItemName item, int count)> CollectAnyItems(BattleSimState sim,
            string playerId, ItemSubType subType)
        {
            var result = new List<(ItemName, int)>();
            var hand = sim.GetHand(playerId);
            if (hand == null) return result;
            var itemConfig = Wargame.Instance?.Context?.Get<ItemConfig>();
            if (itemConfig == null)
            {
                GICLog.Warn("[ResourceGate] 容器无 ItemConfig（ConfigManager 未构建？）——AnyItem 条目收集不可用");
                return result;
            }
            foreach (var entry in hand)
            {
                if ((CardType)entry.cardType != CardType.Item) continue;
                var name = (ItemName)entry.value;
                if (name == ItemName.Mora || name == ItemName.Stamina) continue;
                var data = itemConfig.GetItemData(name);
                if (data != null && data.subType == subType && entry.count > 0)
                    result.Add((name, entry.count));
            }
            return result;
        }
    }
}
