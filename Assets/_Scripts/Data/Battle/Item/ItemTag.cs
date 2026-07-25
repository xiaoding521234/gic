using UnityEngine;
using UnityEngine.Localization;

/// <summary>
/// 物品标签枚举（酒类、食物、道具、装备）
/// </summary>
public enum ItemTag
{
    [InspectorName("货币")]
    Currency = 0,

    [InspectorName("装备")]
    Equipment = 1,

    [InspectorName("饮品")]
    Beverage = 2,

    [InspectorName("食物")]
    Food = 3,

    [InspectorName("材料")]
    Material = 4,

    [InspectorName("道具")]
    Prop = 5,

    [InspectorName("任务物品")]
    Quest = 6,
    
    [InspectorName("酒类")]
    Alcohol = 7,
}

/// <summary>
/// ItemTag 扩展方法
/// </summary>
public static class ItemTagExtensions
{

    /// <summary>
    /// 获取物品标签在本地化表中的 Entry Key
    /// </summary>
    private static string GetEntryKey(this ItemTag tag)
    {
        return tag.ToString();
    }

    /// <summary>
    /// 创建用于本地化系统的 LocalizedString 对象
    /// </summary>
    private static LocalizedString GetLocalizedString(this ItemTag tag)
    {
        return new LocalizedString(TableName.ItemTag.ToString(), tag.GetEntryKey());
    }

    /// <summary>
    /// 创建 Entry 对象（用于 TextCombiner）
    /// </summary>
    /// <param name="leadingSeparator">前置连接符</param>
    public static TextEntry GetEntry(this ItemTag tag, string leadingSeparator = "")
    {
        LocalizedString localizedString = tag.GetLocalizedString();
        return new TextEntry(localizedString, leadingSeparator);
    }
}