using UnityEngine;
using UnityEngine.Localization;

/// <summary>
/// 单位类型
/// </summary>
public enum UnitType
{
    [InspectorName("角色")]
    Character = 1,      // 可操控的角色，可编入卡组，有命座

    [InspectorName("造物")]
    Creation = 2,       // 元素造物，不可编入卡组，由技能召唤产出

    [InspectorName("建筑")]
    Building = 3,       // 建筑，可编入卡组，无命座，重复打出=加一个，战技=科技面板
}

/// <summary>
/// UnitType 扩展方法
/// </summary>
public static class UnitTypeExtensions
{
    /// <summary>
    /// 获取单位类型在本地化表中的 Entry Key
    /// </summary>
    private static string GetEntryKey(this UnitType unitType)
    {
        return unitType.ToString();
    }

    /// <summary>
    /// 创建用于本地化系统的 LocalizedString 对象
    /// </summary>
    private static LocalizedString GetLocalizedString(this UnitType unitType)
    {
        return new LocalizedString(TableName.UnitType.ToString(), unitType.GetEntryKey());
    }

    /// <summary>
    /// 创建 Entry 对象（用于 TextCombiner）
    /// </summary>
    /// <param name="leadingSeparator">前置连接符</param>
    public static TextEntry GetEntry(this UnitType unitType, string leadingSeparator = "")
    {
        LocalizedString localizedString = unitType.GetLocalizedString();
        return new TextEntry(localizedString, leadingSeparator);
    }
}