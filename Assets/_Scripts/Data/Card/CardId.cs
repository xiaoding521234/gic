using System;
namespace GIC.Data
{


    /// <summary>
    /// 统一卡牌标识 — 将 CardType + 数值封为一个值类型。
    /// </summary>
    [Serializable]
    public struct CardId : IEquatable<CardId>
    {
        public CardType cardType;
        public int      value;

        // ── 构造 ──────────────────────────────────
        public CardId(CardType type, int v) { cardType = type; value = v; }
        public CardId(UnitName name)        { cardType = CardType.Unit; value = (int)name; }
        public CardId(ItemName name)        { cardType = CardType.Item; value = (int)name; }

        // ── 隐式转换，让旧调用 (UnitName → CardId) 自动通过 ─
        public static implicit operator CardId(UnitName name) => new CardId(name);
        public static implicit operator CardId(ItemName name) => new CardId(name);

        // ── 类型安全的取值 ─────────────────────────
        public UnitName AsUnitName() => (UnitName)value;
        public ItemName AsItemName() => (ItemName)value;

        // ── IEquatable ────────────────────────────
        public bool Equals(CardId other) => cardType == other.cardType && value == other.value;
        public override bool   Equals(object obj) => obj is CardId o && Equals(o);
        public override int    GetHashCode()      => ((int)cardType * 397) ^ value;
        public static bool     operator ==(CardId a, CardId b) => a.Equals(b);
        public static bool     operator !=(CardId a, CardId b) => !a.Equals(b);

        public override string ToString() => $"{cardType}:{value}";

        // 常用预置
        public static readonly CardId None = new CardId(CardType.Item, 0);
    }

    // ── CardType 枚举（从 Card.cs 移出，供全局使用） ──
    public enum CardType
    {
        Item = 0,
        Unit = 1,
    }

    /// <summary>
    /// 购买价格单源（决策八十六批E，docs/45 §7.2 拍板 #11）：
    /// - 单位卡（角色/建筑）= GetEffectiveDeployCost() / 2（部署价一半——安柏/凯亚/琴/芭芭拉 3★ 出战 50→25、
    ///   骑士团总部/大教堂 3★ 出战 60→30；2026-10-23 起建筑默认表按单位类型分档，见 UnitConfig.GetDeployCost）；
    /// - 物品卡=摩拉价值星级表 MoraValueOfStar（1★=5 / 2★=15 / 3★=30 / 4★=80 / 5★=200）。
    /// 消费方=BuyCardExecutor、购买面板和 AI 购物预算。
    /// </summary>
    public static class CardIdPurchaseExtensions
    {
        /// <summary>显示和估值用价格；执行时用 TryGetPurchaseCost 拒绝缺失配置。</summary>
        public static int PurchaseCostOf(this CardId cardId, UnitConfig unitConfig, ItemConfig itemConfig)
            => cardId.TryGetPurchaseCost(unitConfig, itemConfig, out int cost) ? cost : 0;

        public static bool TryGetPurchaseCost(this CardId cardId, UnitConfig unitConfig, ItemConfig itemConfig,
            out int cost)
        {
            cost = 0;
            if (cardId.value <= 0) return false;
            if (cardId.cardType == CardType.Unit)
            {
                var unitData = unitConfig?.GetUnitData(cardId.AsUnitName());
                if (unitData == null) return false;
                cost = unitData.GetEffectiveDeployCost() / 2;
                return cost >= 0;
            }
            if (cardId.cardType != CardType.Item) return false;
            var itemData = itemConfig?.GetItemData(cardId.AsItemName());
            if (itemData == null) return false;
            cost = MoraValueOfStar(itemData.starLevel);
            return true;
        }

        public static bool RequiresPurchaseUnlock(this CardId cardId, UnitConfig unitConfig)
            => cardId.cardType == CardType.Unit
                && unitConfig?.GetUnitData(cardId.AsUnitName())?.unitType != UnitType.Building;

        /// <summary>物品星级→摩拉价值表（拍板 #11 原需求）：1★=5 / 2★=15 / 3★=30 / 4★=80 / 5★=200</summary>
        public static int MoraValueOfStar(int starLevel)
        {
            switch (starLevel)
            {
                case 1: return 5;
                case 2: return 15;
                case 3: return 30;
                case 4: return 80;
                default: return 200; // 5★及以上
            }
        }
    }
}



