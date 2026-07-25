using UnityEngine;
using UnityEngine.Localization;

/// <summary>
/// 势力类型枚举
/// </summary>
public enum FactionType
{
    #region 天空岛 (1001)
    [InspectorName("天空岛")]
    Celestia = 1001,
    #endregion

    #region 挪德卡莱 (2001)
    [InspectorName("挪德")]
    Nodkrai = 2001,
    #endregion

    #region 蒙德 (3001)
    [InspectorName("蒙德")]
    Mondstadt = 3001,
    #endregion

    #region 璃月 (4001)
    [InspectorName("璃月")]
    Liyue = 4001,
    #endregion

    #region 稻妻 (5001)
    [InspectorName("稻妻")]
    Inazuma = 5001,
    #endregion

    #region 须弥 (6001)
    [InspectorName("须弥")]
    Sumeru = 6001,
    #endregion

    #region 枫丹 (7001)
    [InspectorName("枫丹")]
    Fontaine = 7001,
    #endregion

    #region 纳塔 (8001)
    [InspectorName("纳塔")]
    Natlan = 8001,
    #endregion

    #region 至冬 (9001)
    [InspectorName("至冬")]
    Snezhnaya = 9001,
    #endregion

    #region 坎瑞亚 (10001)
    [InspectorName("坎瑞亚")]
    Khaenriah = 10001,
    #endregion

    #region 特殊 (11001)
    [InspectorName("特殊")]
    Special = 11001,
    #endregion
}

/// <summary>
/// FactionType 扩展方法
/// </summary>
public static class FactionTypeExtensions
{

    /// <summary>
    /// 获取势力类型在本地化表中的 Entry Key
    /// </summary>
    private static string GetEntryKey(this FactionType faction)
    {
        return faction.ToString();
    }

    /// <summary>
    /// 创建用于本地化系统的 LocalizedString 对象
    /// </summary>
    private static LocalizedString GetLocalizedString(this FactionType faction)
    {
        return new LocalizedString(TableName.FactionType.ToString(), faction.GetEntryKey());
    }

    /// <summary>
    /// 创建 Entry 对象（用于 TextCombiner）
    /// </summary>
    /// <param name="leadingSeparator">前置连接符</param>
    public static TextEntry GetEntry(this FactionType faction, string leadingSeparator = "")
    {
        LocalizedString localizedString = faction.GetLocalizedString();
        return new TextEntry(localizedString, leadingSeparator);
    }

}