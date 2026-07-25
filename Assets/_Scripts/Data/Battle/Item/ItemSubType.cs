using UnityEngine;
using UnityEngine.Localization;

/// <summary>
/// 物品子类型枚举 — 序列化在 ItemData 上，用于决定背包 Tab 归属
/// </summary>
public enum ItemSubType
{
    [InspectorName("货币")]   Currency = 0,
    [InspectorName("武器")]   Weapon = 1,
    [InspectorName("圣遗物")] Artifact = 2,
    [InspectorName("食物")]   Food = 3,
    [InspectorName("饮品")]   Drink = 4,
    [InspectorName("材料")]   Material = 5,
    [InspectorName("任务")]   Quest = 6,
}

/// <summary>
/// ItemSubType 扩展方法
/// </summary>
public static class ItemSubTypeExtensions
{
    /// <summary>
    /// 物品子类型 → 背包分页映射
    /// </summary>
    public static BackpackTab ToBackpackTab(this ItemSubType subType) => subType switch
    {
        ItemSubType.Currency  => BackpackTab.Currency,
        ItemSubType.Weapon    => BackpackTab.Equipment,
        ItemSubType.Artifact  => BackpackTab.Equipment,
        ItemSubType.Food      => BackpackTab.Consumable,
        ItemSubType.Drink     => BackpackTab.Consumable,
        ItemSubType.Material  => BackpackTab.Material,
        ItemSubType.Quest     => BackpackTab.Quest,
        _                     => BackpackTab.Material,
    };

    /// <summary>
    /// 获取子类型在本地化表中的 Entry Key
    /// </summary>
    private static string GetEntryKey(this ItemSubType subType)
    {
        return subType.ToString();
    }

    /// <summary>
    /// 创建 Entry 对象（用于 TextCombiner）
    /// </summary>
    public static TextEntry GetEntry(this ItemSubType subType, string leadingSeparator = "")
    {
        var localizedString = new LocalizedString(TableName.UIText.ToString(), subType.GetEntryKey());
        return new TextEntry(localizedString, leadingSeparator);
    }
}
