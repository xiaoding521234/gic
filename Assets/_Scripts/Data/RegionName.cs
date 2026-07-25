using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;

public enum RegionName
{
    [InspectorName("无")] Null = 0,
    [InspectorName("挪德")] Nodkrai = 1,
    [InspectorName("蒙德")] Mondstadt = 2,   // 自由之城
    [InspectorName("璃月")] Liyue = 3,       // 契约之国
    [InspectorName("稻妻")] Inazuma = 4,     // 永恒之岛
    [InspectorName("须弥")] Sumeru = 5,      // 智慧之都
    [InspectorName("枫丹")] Fontaine = 6,    // 正义之邦
    [InspectorName("纳塔")] Natlan = 7,      // 战争之地
    [InspectorName("至冬")] Snezhnaya = 8,   // 冰雪之国
    [InspectorName("坎瑞亚")] Khaenriah = 9,   // 覆灭的古国
}

public static class RegionNameExtensions
{

    /// <summary>
    /// 获取区域的资源路径
    /// </summary>
    public static string GetResourcePath(this RegionName region, string basePath = "Prefabs/Map/")
    {
        return $"{basePath}{region}Map";
    }

    /// <summary>
    /// 创建 Entry 对象（用于 TextCombiner）
    /// </summary>
    /// <param name="leadingSeparator">前置连接符</param>
    /// <returns>Entry 对象</returns>
    public static TextEntry GetEntry(this RegionName region, string leadingSeparator = "")
    {
        LocalizedString localizedString = region.GetLocalizedString();
        return new TextEntry(localizedString, leadingSeparator);
    }

    /// <summary>
    /// 创建用于本地化系统的 LocalizedString 对象
    /// </summary>
    /// <returns>LocalizedString 对象</returns>
    private static LocalizedString GetLocalizedString(this RegionName region)
    {
        return new LocalizedString(TableName.UIText.ToString(), region.GetEntryKey());
    }

    /// <summary>
    /// 获取区域在本地化表中的 Entry Key
    /// </summary>
    /// <returns>本地化表的 Key</returns>
    private static string GetEntryKey(this RegionName region)
    {
        return region.ToString();
    }
}
