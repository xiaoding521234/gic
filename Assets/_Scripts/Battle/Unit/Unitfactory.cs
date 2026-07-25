using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 单位工厂
/// </summary>
public static class UnitFactory
{
    // ==================== 预制体配置 ====================
    
    /// <summary>
    /// 单位预制体（在 Inspector 中配置或通过代码注册）
    /// </summary>
    private static GameObject _unitPrefab;
    private static bool _isInitialized = false;
    
    // ==================== 初始化 ====================
    
    /// <summary>
    /// 初始化工厂
    /// </summary>
    public static void Initialize()
    {
        if (_isInitialized) return;
        
        // 从 Resources 加载预制体
        _unitPrefab = Resources.Load<GameObject>("Prefabs/Units/NormalUnit");
        
        _isInitialized = true;
        Debug.Log($"[UnitFactory] 初始化完成");
    }
    
    
    // ==================== 创建方法 ====================
    
    /// <summary>
    /// 创建单位实例
    /// </summary>
    public static Unit CreateUnitWithData(UnitConfig.UnitData data)
    {
        if (!_isInitialized) Initialize();
        
        GameObject unitObj = UnityEngine.Object.Instantiate(_unitPrefab);
        Unit unit = unitObj.GetComponent<Unit>();
        unit.InitWithData(data);
        
        return unit;
    }
    
}    