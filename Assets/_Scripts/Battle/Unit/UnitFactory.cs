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
        /// 单位预制体（Resources/Prefabs/Units/Unit）
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
            _unitPrefab = Resources.Load<GameObject>("Prefabs/Units/Unit");
            if (_unitPrefab == null)
            {
                GICLog.Error("[UnitFactory] 未找到单位预制体 Prefabs/Units/Unit，请确认 Resources 目录");
                return; // 不置 _isInitialized，下次调用重试加载
            }

            _isInitialized = true;
            GICLog.Info($"[UnitFactory] 初始化完成");
        }


        /// <summary>
        /// 预热：实例化一枚单位立牌后即刻销毁——把 prefab 依赖的模型/材质/贴图等资产
        /// 首次加载反序列化的同步成本（实测首枚 ~0.37s，2026-09-13 帧取证）吃进无感时机
        /// （建房等待期），开局装配帧不再付。幂等：未初始化先 Initialize。
        /// </summary>
        public static void PreWarm()
        {
            Initialize();
            if (_unitPrefab == null) return;
            var probe = UnityEngine.Object.Instantiate(_unitPrefab);
            UnityEngine.Object.Destroy(probe); // 依赖资产已进缓存，下次实例化仅克隆开销
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


