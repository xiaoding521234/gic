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


    [Configuration]
    public class ConfigManager : IWargameManager
    {
        // ── [Bean] 方法：加载配置并注册到容器 ──

        [Bean] public UnitConfig GetUnitConfig() => LoadConfig<UnitConfig>("Configs/UnitConfig");
        [Bean] public ItemConfig GetItemConfig() => LoadConfig<ItemConfig>("Configs/ItemConfig");
        [Bean] public PositionConfig GetPositionConfig() => LoadConfig<PositionConfig>("Configs/PositionConfig");
        [Bean] public ElementFactionConfig GetElementFactionConfig() => LoadConfig<ElementFactionConfig>("Configs/ElementFactionConfig");
        [Bean] public StarVisualConfig GetStarVisualConfig() => LoadConfig<StarVisualConfig>("Configs/StarVisualConfig");
        [Bean] public InitialSaveConfig GetInitialSaveConfig() => LoadConfig<InitialSaveConfig>("Configs/InitialSaveConfig");

        [Autowired] private UnitConfig _unitConfig;
        [Autowired] private ItemConfig _itemConfig;
        [Autowired] private CardConfigResolver _cardResolver;
        [Bean] public CardConfigResolver GetCardConfigResolver() => new CardConfigResolver(_unitConfig, _itemConfig);

        [PostConstruct]
        public void Init()
        {
            // StarVisualConfig 静态访问兼容
            StarVisualConfig.Initialize(GetStarVisualConfig());
            // ElementFactionConfig 静态访问兼容（元素颜色等，非注入上下文调用）
            ElementFactionConfig.Initialize(GetElementFactionConfig());
            // CardConfigResolver 静态访问兼容（SaveCardData.Config / 卡牌视图策略）
            CardConfigResolver.Initialize(_cardResolver);
        }

        public void Start() { }
        public void Update(float deltaTime) { }

        /// <summary>
        /// 泛型加载配置并自动 BuildCache
        /// </summary>
        private T LoadConfig<T>(string path) where T : ScriptableObject
        {
            var config = Resources.Load<T>(path);

            if (config == null)
            {
                GICLog.Error($"无法加载 {typeof(T).Name}，请确保文件在 Resources 文件夹下: {path}");
                return null;
            }

            GICLog.Info($"加载 {typeof(T).Name} 成功");

            // 通过反射调用 BuildCache（如果存在）
            var method = typeof(T).GetMethod("BuildCache",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                null, System.Type.EmptyTypes, null);
            method?.Invoke(config, null);

            return config;
        }
    }

}
