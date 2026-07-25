using UnityEngine;
using UnityEngine.Localization;

/// <summary>
/// 背包分页枚举 — 由 ICardConfig.GetBackpackTab() 返回，驱动背包 Tab 筛选
/// </summary>
public enum BackpackTab
{
    [InspectorName("角色")]     Character = 0,
    [InspectorName("造物")]     Creation = 1,
    [InspectorName("装备")]     Equipment = 2,
    [InspectorName("消耗品")]   Consumable = 3,
    [InspectorName("材料")]     Material = 4,
    [InspectorName("货币")]     Currency = 5,
    [InspectorName("任务")]     Quest = 6,
}

/// <summary>
/// BackpackTab 扩展方法
/// </summary>
public static class BackpackTabExtensions
{
    /// <summary>
    /// 获取背包分页在本地化表中的 Entry Key
    /// </summary>
    private static string GetEntryKey(this BackpackTab tab)
    {
        return tab.ToString();
    }

    /// <summary>
    /// 创建用于本地化系统的 LocalizedString 对象
    /// </summary>
    private static LocalizedString GetLocalizedString(this BackpackTab tab)
    {
        return new LocalizedString(TableName.UIText.ToString(), tab.GetEntryKey());
    }

    /// <summary>
    /// 创建 Entry 对象（用于 TextCombiner）
    /// </summary>
    public static TextEntry GetEntry(this BackpackTab tab, string leadingSeparator = "")
    {
        LocalizedString localizedString = tab.GetLocalizedString();
        return new TextEntry(localizedString, leadingSeparator);
    }
}
