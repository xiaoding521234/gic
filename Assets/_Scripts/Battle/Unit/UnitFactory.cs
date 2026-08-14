using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
namespace GIC.Battle
{


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
            if (_unitPrefab == null)
            {
                GICLog.Error("[UnitFactory] 未找到单位预制体 Prefabs/Units/NormalUnit，请确认 Resources 目录");
                return; // 不置 _isInitialized，下次调用重试加载
            }

            _isInitialized = true;
            GICLog.Info($"[UnitFactory] 初始化完成");
        }


        // ==================== 创建方法 ====================

        /// <summary>
        /// 创建单位实例
        /// </summary>
        public static Unit CreateUnitWithData(UnitConfig.UnitData data)
        {
            if (!_isInitialized) Initialize();

            if (_unitPrefab == null)
            {
                GICLog.Error("[UnitFactory] 预制体未就绪，无法创建单位");
                return null;
            }

            GameObject unitObj = UnityEngine.Object.Instantiate(_unitPrefab);
            Unit unit = unitObj.GetComponent<Unit>();
            unit.InitWithData(data);

            return unit;
        }
        
    }    
}


