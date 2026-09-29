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
    /// - Stamina（玩家账户）：**实际扣值按施法者层级强制换算**（D 批次操控分层 docs/active/32 §5.2——
    ///   眷属 0/伙伴 5/魔神 10，UnitTierHelper.StaminaCostOf 唯一真源；技能资产声明值=基准/校验值，
    ///   实际扣值=层级函数——同一技能资产被多层级单位共享，技能级 costs 无法表达 per-tier 价格。
    ///   眷属 0=门槛恒过、登记跳过——决策十「眷属豁免」语义并入层级表自然表达，豁免特判退役）；
    ///   Has=sim.HasEnoughStamina（或调用方传入的决策期虚拟池——CompanionBrain 体力预留用）
    /// - Mora（玩家账户）：Has=池数量判定；Charge=MoraSpendEffect——**眷属豁免**（1-2 星不耗玩家资源）
    /// - Item（玩家手牌，指定物品）：Has=条目 count；Charge=ItemConsumeEffect(指定)——眷属豁免
    /// - AnyItem（玩家手牌，同类任意）：Has=**跨同类条目聚合判定**（3 苹果酒+2 果汁可付「任意饮品×4」）；
    ///   Charge=ItemConsumeEffect(同类任意)——应用时按手牌列表序确定性逐条扣（零随机）、
    ///   **原子性**（防御路径不足额=不扣分毫零命令）；**货币卡（摩拉/体力）不可被 AnyItem 匹配**
    ///   （账户资源不经物品消耗链——LoseCard(货币) 会动资源池，属语义错误）；眷属豁免
    /// 门槛语义：不足=行动落空且不扣（调用方先对 costs 全条目 Has 再全条目 Charge——防「扣了体力才发现苹果不够」）。
    /// </summary>
    public static class ResourceGate
    {
        /// <summary>
        /// 门槛检查（纯查不改）：本条消耗是否可支付。
        /// </summary>
        /// <param name="actor">行动单位（Energy 判定用；层级换算/眷属豁免判定用）</param>
        /// <param name="playerId">资源归属玩家（Stamina/Mora/Item/AnyItem 判定用）</param>
        /// <param name="staminaPoolOverride">决策期虚拟体力池（可选；null=读 sim 实池）——
        /// CompanionBrain 预留口径：伙伴决策时按「实池−己方已提交行动消耗−先前已定伙伴消耗」判定，
        /// 防多伙伴对同一池超额承诺致后手落空（docs/active/32 §5.2「不出现选了落空」）</param>
        public static bool Has(BattleSimState sim, Unit actor, string playerId, SkillCostEntry cost,
            int? staminaPoolOverride = null)
        {
            if (cost == null || cost.amount <= 0) return true;
            switch (cost.kind)
            {
                case CostKind.Energy:
                    return BattleSimState.HasEnoughEnergy(actor, cost.amount);
                case CostKind.Stamina:
                {
                    int amount = StaminaAmountOf(actor, cost.amount);
                    if (amount <= 0) return true; // 眷属 0=门槛恒过（层级表自然表达，豁免特判退役）
                    if (staminaPoolOverride.HasValue) return staminaPoolOverride.Value >= amount;
                    return sim.HasEnoughStamina(playerId, amount);
                }
                case CostKind.Mora:
                    if (BattleHeuristics.IsFamiliar(actor)) return true; // 眷属豁免玩家资源（体力先例推广）
                    return sim.GetMora(playerId) >= cost.amount;
                case CostKind.Item:
                    if (BattleHeuristics.IsFamiliar(actor)) return true;
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
                    if (BattleHeuristics.IsFamiliar(actor)) return true;
                    return CountAnyItems(sim, playerId, cost.subType) >= cost.amount;
                default:
                    GICLog.Warn($"[ResourceGate] 未接路由的消耗种类 {cost.kind}——按可支付处理（请补路由）");
                    return true;
            }
        }

        /// <summary>
        /// 消耗登记（门槛已过）：向 effects 追加对应消耗效应（随片统一应用→命令产出→对账）。
        /// Stamina 按层级换算后的实际值登记（眷属 0=跳过）。
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
                {
                    int amount = StaminaAmountOf(actor, cost.amount);
                    if (amount > 0)
                        effects.Add(new StaminaEffect(playerId, -amount));
                    break;
                }
                case CostKind.Mora:
                    if (BattleHeuristics.IsFamiliar(actor)) break; // 眷属豁免
                    effects.Add(new MoraSpendEffect(playerId, cost.amount));
                    break;
                case CostKind.Item:
                    if (BattleHeuristics.IsFamiliar(actor)) break;
                    effects.Add(new ItemConsumeEffect(playerId, cost.item, cost.amount));
                    break;
                case CostKind.AnyItem:
                    if (BattleHeuristics.IsFamiliar(actor)) break;
                    effects.Add(new ItemConsumeEffect(playerId, cost.subType, cost.amount));
                    break;
            }
        }

        /// <summary>
        /// costs 全条目门槛检查（SkillExecutor 消耗段第一段：任一不足=行动落空且不登记任何消耗）。
        /// staminaPoolOverride 透传 Has（CompanionBrain 决策期虚拟池口径）。
        /// </summary>
        public static bool HasAll(BattleSimState sim, Unit actor, string playerId,
            System.Collections.Generic.List<SkillCostEntry> costs, out SkillCostEntry missing,
            int? staminaPoolOverride = null)
        {
            missing = null;
            if (costs == null) return true;
            foreach (var cost in costs)
            {
                if (Has(sim, actor, playerId, cost, staminaPoolOverride)) continue;
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

        /// <summary>Stamina 条目实际扣值（层级换算唯一出口，docs/active/32 §5.2）：声明值=基准/校验值
        /// （全技能资产按魔神基准 10 声明，编辑器校验行防漂移），实际扣值=施法者层级表
        /// （眷属 0/伙伴 5/魔神 10）。HUD 置灰镜像（BattleHud.HasSkillResources）与 AI 脑门槛同读此口径。</summary>
        private static int StaminaAmountOf(Unit actor, int declaredAmount)
        {
            if (actor == null) return declaredAmount; // 无行动单位（理论不可达）：按声明值
            return UnitTierHelper.StaminaCostOf(BattleHeuristics.TierOf(actor));
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
