// PlayerColor.cs - 玩家颜色枚举及相关扩展
using UnityEngine;
using UnityEngine.Localization;

/// <summary>
/// 玩家颜色枚举
/// </summary>
public enum PlayerColor
{
    [InspectorName("红色")]
    Red = 1,
    [InspectorName("蓝色")]
    Blue = 2,
    [InspectorName("绿色")]
    Green = 3,
    [InspectorName("黄色")]
    Yellow = 4,
    [InspectorName("紫色")]
    Purple = 5,
    [InspectorName("青色")]
    Cyan = 6,
    [InspectorName("橙色")]
    Orange = 7,
    [InspectorName("粉色")]
    Pink = 8,
    [InspectorName("白色")]
    White = 9,
    [InspectorName("灰色")]
    Gray = 10,
}

/// <summary>
/// PlayerColor 扩展方法
/// </summary>
public static class PlayerColorExtensions
{
    /// <summary>
    /// 将枚举值转换为 Unity Color
    /// </summary>
    public static Color ToColor(this PlayerColor playerColor)
    {
        return playerColor switch
        {
            PlayerColor.Red => new Color(1f, 0.3f, 0.3f),
            PlayerColor.Blue => new Color(0.3f, 0.3f, 1f),
            PlayerColor.Green => new Color(0.3f, 1f, 0.3f),
            PlayerColor.Yellow => new Color(1f, 1f, 0.3f),
            PlayerColor.Purple => new Color(1f, 0.3f, 1f),
            PlayerColor.Cyan => new Color(0.3f, 1f, 1f),
            PlayerColor.Orange => new Color(1f, 0.6f, 0.3f),
            PlayerColor.Pink => new Color(1f, 0.5f, 0.8f),
            PlayerColor.White => new Color(1f, 1f, 1f),
            PlayerColor.Gray => new Color(0.5f, 0.5f, 0.5f),
            _ => Color.white
        };
    }

    /// <summary>
    /// 获取颜色在本地化表中的 Entry Key
    /// </summary>
    private static string GetEntryKey(this PlayerColor playerColor)
    {
        return playerColor.ToString();
    }

    /// <summary>
    /// 创建用于本地化系统的 LocalizedString 对象
    /// </summary>
    private static LocalizedString GetLocalizedString(this PlayerColor playerColor)
    {
        return new LocalizedString(TableName.UIText.ToString(), playerColor.GetEntryKey());
    }

    /// <summary>
    /// 创建 Entry 对象（用于 TextCombiner）
    /// </summary>
    /// <param name="leadingSeparator">前置连接符</param>
    public static TextEntry GetEntry(this PlayerColor playerColor, string leadingSeparator = "")
    {
        LocalizedString localizedString = playerColor.GetLocalizedString();
        return new TextEntry(localizedString, leadingSeparator);
    }
}