using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 地砖身份组件
/// </summary>
public class TileIdentity : MonoBehaviour, ITileComponent
{
    private Tile _owner;
    
    [Header("基础信息")]
    [SerializeField] private TileType _tileType = TileType.Plain;
    [SerializeField] private string _displayName;
    [SerializeField] private Sprite _tileSprite;
    [SerializeField] private Color _tileColor = Color.white;
    
    public TileType TileType => _tileType;
    public string DisplayName => _displayName;
    public Sprite TileSprite => _tileSprite;
    public Color TileColor => _tileColor;
    
    public void Initialize(Tile owner)
    {
        _owner = owner;
        
        if (string.IsNullOrEmpty(_displayName))
        {
            _displayName = _tileType.GetDisplayName();
        }
    }
    
    public void SetTileType(TileType type)
    {
        _tileType = type;
        _displayName = type.GetDisplayName();
    }
    public void SetSprite(Sprite sprite) => _tileSprite = sprite;
    public void SetColor(Color color) => _tileColor = color;
    
    // ==================== 标签方法 ====================
    
    /// <summary>
    /// 获取所有标签
    /// </summary>
    public TileTag[] GetTags()
    {
        return _tileType.GetTags();
    }
    
    /// <summary>
    /// 是否拥有指定标签
    /// </summary>
    public bool HasTag(TileTag tag)
    {
        return _tileType.HasTag(tag);
    }
    
    // ==================== 移动判断 ====================
    
    /// <summary>
    /// 是否允许步行
    /// </summary>
    public bool AllowWalk()
    {
        return _tileType.AllowWalk();
    }
    
    /// <summary>
    /// 是否允许飞行
    /// </summary>
    public bool AllowFly()
    {
        return _tileType.AllowFly();
    }

    
    // ==================== 地形分类判断 ====================
    
    /// <summary>
    /// 是否为水域地形
    /// </summary>
    public bool IsWaterTerrain()
    {
        return _tileType.HasTag(TileTag.WaterTerrain);
    }
    
}