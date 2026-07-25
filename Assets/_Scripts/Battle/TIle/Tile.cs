using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 地块基类 - 组件容器
/// </summary>
public class Tile : MonoBehaviour
{
    private Dictionary<System.Type, ITileComponent> _components = new();

    private void Awake()
    {
        foreach (var comp in GetComponents<ITileComponent>())
        {
            _components[comp.GetType()] = comp;
            comp.Initialize(this);
        }
    }

    /// <summary>
    /// 获取指定类型的地块组件
    /// </summary>
    public T GetTileComponent<T>() where T : class, ITileComponent
    {
        _components.TryGetValue(typeof(T), out var comp);
        return comp as T;
    }

    /// <summary>
    /// 是否拥有指定类型的地块组件
    /// </summary>
    public bool HasTileComponent<T>() where T : ITileComponent
    {
        return _components.ContainsKey(typeof(T));
    }
}