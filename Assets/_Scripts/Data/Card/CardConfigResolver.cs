using GIC.Framework;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Data
{

    /// <summary>
    /// 全局卡牌配置查询入口 — 类似原神的 GDataManager / 星穹铁道的 ExcelManager。
    /// 由 ConfigManager [Bean] 产出，可通过 [Autowired] 或 ApplicationContext.Get 获取。
    /// </summary>
    public class CardConfigResolver
    {
        public UnitConfig UnitConfig { get; }
        public ItemConfig ItemConfig { get; }

        public CardConfigResolver(UnitConfig unitConfig, ItemConfig itemConfig)
        {
            UnitConfig = unitConfig;
            ItemConfig = itemConfig;
        }

        public ICardConfig Resolve(CardId id)
        {
            return id.cardType switch
            {
                CardType.Unit => new UnitConfigAdapter(UnitConfig?.GetUnitData(id.AsUnitName()), UnitConfig),
                CardType.Item => new ItemConfigAdapter(ItemConfig?.GetItemData(id.AsItemName()), ItemConfig),
                _             => null,
            };
        }
    }

}
