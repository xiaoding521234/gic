using UnityEngine;
using System;
using System.Linq;
using System.Collections.Generic;

/// <summary>
/// 地形标签特性
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public class TileTagAttribute : Attribute
{
    public TileTag[] Tags { get; }
    
    public TileTagAttribute(params TileTag[] tags)
    {
        Tags = tags;
    }
}

/// <summary>
/// 地形标签
/// </summary>
public enum TileTag
{
    AllowWalk,          // 允许步行
    AllowFly,           // 允许飞行
    WaterTerrain,       // 水域地形
}

/// <summary>
/// 地形类型枚举
/// </summary>
public enum TileType
{
    #region 基础地形 (1-99)
    [InspectorName("平原")]
    [TileTag(TileTag.AllowWalk, TileTag.AllowFly)]
    Plain = 1,
    
    [InspectorName("草地")]
    [TileTag(TileTag.AllowWalk, TileTag.AllowFly)]
    Grassland = 2,
    
    [InspectorName("沙地")]
    [TileTag(TileTag.AllowWalk, TileTag.AllowFly)]
    Sand = 3,
    
    [InspectorName("雪地")]
    [TileTag(TileTag.AllowWalk, TileTag.AllowFly)]
    Snow = 4,
    
    [InspectorName("冰面")]
    [TileTag(TileTag.AllowWalk, TileTag.AllowFly)]
    Ice = 5,
    #endregion

    #region 水域地形 (100-199)
    [InspectorName("水面")]
    [TileTag(TileTag.AllowFly, TileTag.WaterTerrain)]
    Water = 101,
    
    [InspectorName("沼泽")]
    [TileTag(TileTag.AllowFly, TileTag.WaterTerrain)]
    Swamp = 102,
    #endregion
}

/// <summary>
/// TileType 扩展方法
/// </summary>
public static class TileTypeExtensions
{
    /// <summary>
    /// 获取显示名称
    /// </summary>
    public static string GetDisplayName(this TileType tileType)
    {
        var type = typeof(TileType);
        var member = type.GetMember(tileType.ToString());
        
        if (member.Length > 0)
        {
            var attrs = member[0].GetCustomAttributes(typeof(InspectorNameAttribute), false);
            if (attrs.Length > 0)
            {
                return ((InspectorNameAttribute)attrs[0]).displayName;
            }
        }
        
        return tileType.ToString();
    }

    /// <summary>
    /// 获取所有标签
    /// </summary>
    public static TileTag[] GetTags(this TileType tileType)
    {
        var type = typeof(TileType);
        var member = type.GetMember(tileType.ToString());
        
        if (member.Length > 0)
        {
            var attr = member[0].GetCustomAttributes(typeof(TileTagAttribute), false)
                               .FirstOrDefault() as TileTagAttribute;
            if (attr != null)
            {
                return attr.Tags;
            }
        }
        
        return new TileTag[0];
    }

    /// <summary>
    /// 是否拥有指定标签
    /// </summary>
    public static bool HasTag(this TileType tileType, TileTag tag)
    {
        return tileType.GetTags().Contains(tag);
    }

    /// <summary>
    /// 是否允许步行
    /// </summary>
    public static bool AllowWalk(this TileType tileType)
    {
        return tileType.HasTag(TileTag.AllowWalk);
    }

    /// <summary>
    /// 是否允许飞行
    /// </summary>
    public static bool AllowFly(this TileType tileType)
    {
        return tileType.HasTag(TileTag.AllowFly);
    }

    /// <summary>
    /// 转换为存储字符串
    /// </summary>
    public static string ToStorageString(this TileType tileType)
    {
        return tileType.ToString();
    }

    /// <summary>
    /// 从存储字符串解析
    /// </summary>
    public static TileType FromStorageString(string str)
    {
        if (System.Enum.TryParse<TileType>(str, out var result))
            return result;
        
        Debug.LogWarning($"无法解析地形类型: {str}");
        return TileType.Plain;
    }
}