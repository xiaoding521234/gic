/// <summary>
/// 全局卡牌配置查询入口 — 类似原神的 GDataManager / 星穹铁道的 ExcelManager。
/// 由 ConfigManager.Start() 注入，之后任意位置通过 CardId 即可获取配置。
/// </summary>
public static class CardConfigResolver
{
    public static UnitConfig UnitConfig { get; set; }
    public static ItemConfig ItemConfig { get; set; }

    public static ICardConfig Resolve(CardId id)
    {
        return id.cardType switch
        {
            CardType.Unit => new UnitConfigAdapter(UnitConfig?.GetUnitData(id.AsUnitName())),
            CardType.Item => new ItemConfigAdapter(ItemConfig?.GetItemData(id.AsItemName())),
            _             => null,
        };
    }

    /// <summary>
    /// ConfigManager 在 Start 时调用一次，注入配置引用。
    /// </summary>
    public static void Initialize(UnitConfig unitConfig, ItemConfig itemConfig)
    {
        UnitConfig = unitConfig;
        ItemConfig = itemConfig;
    }
}
