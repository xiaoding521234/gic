using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Framework;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Data
{


    /// <summary>
    /// 初始存档配置 — 新档的初始卡牌/货币/卡组数据源（2026-09-05 外置，取代旧 PlayerSaveData.InitCards 硬编码）。
    /// 消费方：SaveManager.ApplyInitialData（CreateNewSave 路径）。资产：Resources/Configs/InitialSaveConfig.asset。
    /// 每条目直接声明 unit/item + 数量 + 所属卡组——无列表顺序依赖，调条目顺序不再引发卡组错乱（旧魔法索引根除）。
    /// </summary>
    [CreateAssetMenu(fileName = "InitialSaveConfig", menuName = "Game/InitialSaveConfig")]
    public class InitialSaveConfig : ScriptableObject
    {
        [Serializable]
        public class UnitEntry
        {
            public UnitName unit = UnitName.Paimon;
            [Min(0)] public int count = 1;
            [InspectorName("所属卡组")] public List<int> decks = new List<int>();
        }

        [Serializable]
        public class ItemEntry
        {
            public ItemName item = ItemName.Mora;
            [Min(0)] public int count = 1;
            [InspectorName("所属卡组")] public List<int> decks = new List<int>();
        }

        [Header("初始角色")]
        [InspectorName("角色条目")]
        public List<UnitEntry> initialUnits = new List<UnitEntry>();

        [Header("初始物品")]
        [InspectorName("物品条目")]
        public List<ItemEntry> initialItems = new List<ItemEntry>();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>开发测试物资条目 — 只发数量、不入卡组（测试便利用，正式版整块编译剔除）</summary>
        [Serializable]
        public class DevTestItemEntry
        {
            public ItemName item = ItemName.Mora;
            [Min(0)] public int count = 1;
        }

        [Header("开发测试（仅编辑器/开发构建生效）")]
        [Tooltip("仅 UNITY_EDITOR/DEVELOPMENT_BUILD 建档时额外发放的测试物资（AddItemCount 合并语义）；正式版编译整块剔除，正式拍板（如命运之缘新档 0/0）不受影响。只发数量不入卡组。测试物资写这里，勿写进上面的正式条目。")]
        [InspectorName("开发测试物品")]
        public List<DevTestItemEntry> devTestItems = new List<DevTestItemEntry>();
#endif

        [Header("默认卡组")]
        [InspectorName("初始选中的卡组")]
        public int defaultDeck = 1;
    }
}
