using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Framework
{


    public class ConfigManager : IWargameManager
    {

        [Header("配置")]
        private PositionConfig positionConfig;
        private UnitConfig unitConfig;
        private ItemConfig itemConfig;
        private ElementFactionIconConfig elementFactionIconConfig;

        public void Start()
        {
            positionConfig = LoadConfig<PositionConfig>("Configs/PositionConfig");
            unitConfig = LoadConfig<UnitConfig>("Configs/UnitConfig");
            itemConfig = LoadConfig<ItemConfig>("Configs/ItemConfig");
            elementFactionIconConfig = LoadConfig<ElementFactionIconConfig>("Configs/ElementFactionIconConfig");

            // 注入全局配置查询入口
            CardConfigResolver.Initialize(unitConfig, itemConfig);
        }

        public void Update(float deltaTime)
        {

        }

        /// <summary>
        /// 泛型加载配置并自动 BuildCache
        /// </summary>
        private T LoadConfig<T>(string path) where T : ScriptableObject
        {
            var config = Resources.Load<T>(path);

            if (config == null)
            {
                Debug.LogError($"无法加载 {typeof(T).Name}，请确保文件在 Resources 文件夹下: {path}");
                return null;
            }

            Debug.Log($"加载 {typeof(T).Name} 成功");

            // 通过反射调用 BuildCache（如果存在）
            var method = typeof(T).GetMethod("BuildCache",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                null, System.Type.EmptyTypes, null);
            method?.Invoke(config, null);

            return config;
        }

        // 获取配置的公共方法
        public PositionConfig GetPositionConfig()
        {
            return positionConfig;
        }

        public UnitConfig GetUnitConfig()
        {
            return unitConfig;
        }

        public ItemConfig GetItemConfig()
        {
            return itemConfig;
        }

        public ElementFactionIconConfig GetElementFactionIconConfig()
        {
            return elementFactionIconConfig;
        }
    }

}


