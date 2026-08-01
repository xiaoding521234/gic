using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
namespace GIC.Battle
{

    /// <summary>
    /// 卡牌视图策略工厂 — 按 CardType 返回对应策略的单例。
    /// </summary>
    public static class CardViewStrategyFactory
    {
        private static readonly UnitCardViewStrategy s_unit = new();
        private static readonly ItemCardViewStrategy s_item = new();

        public static ICardViewStrategy Get(CardType type) => type switch
        {
            CardType.Unit => s_unit,
            CardType.Item => s_item,
            _             => null,
        };
    }

}


