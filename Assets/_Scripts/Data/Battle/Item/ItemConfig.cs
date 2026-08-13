using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEngine.Localization;
using GIC.Framework;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Data
{


    /// <summary>
    /// 物品配置 - 支持多个物品数据
    /// </summary>
    [CreateAssetMenu(fileName = "ItemConfig", menuName = "Game/ItemConfig")]
    public class ItemConfig : ScriptableObject
    {
        [Serializable]
        public class ItemData
        {
            [Header("物品标识")]
            public ItemName itemID;

            [Header("基础信息")]
            public List<Sprite> icon;

            [Header("稀有度")]
            [Range(1, 5)]
            public int starLevel = 1;

            [Header("元素构成")]
            public Element[] elements;

            [Header("物品子类型")]
            public ItemSubType subType = ItemSubType.Material;

            [Header("堆叠")]
            public int maxStack = 9999;

            [Header("卡组")]
            public int maxPrepareCount = 10;

            [Header("每份数量")]
            [Range(1, 5)]
            public int countPerServing = 1;

            [Header("战斗")]
            [Tooltip("战斗结束后是否回收")]
            public bool recycleAfterBattle = true;

            [Tooltip("回收时丢失数量")]
            public int recycleLossCount = 0;

            [Header("自定义参数")]
            [SerializeField] public ItemParam[] customParams;

            // 删除 description 字段，改用本地化

            /// <summary>
            /// 获取整数参数
            /// </summary>
            public int GetInt(string key, int defaultValue = 0)
            {
                if (customParams != null)
                {
                    foreach (var p in customParams)
                    {
                        if (p.key == key) return p.value;
                    }
                }
                return defaultValue;
            }

            public Sprite GetIcon(int index)
            {
                if (icon == null || icon.Count == 0) return null;
                if (index < 0 || index >= icon.Count) return icon[0];
                return icon[index];
            }

            #region 本地化 Entry 获取方法

            /// <summary>
            /// 获取物品名称的 Entry（用于 TextCombiner）
            /// </summary>
            public TextEntry GetNameEntry()
            {
                return itemID.GetEntry();
            }

            /// <summary>
            /// 获取物品描述的 Entry（用于 TextCombiner）
            /// </summary>
            public TextEntry GetDescriptionEntry()
            {
                var localizedString = new LocalizedString(TableName.ItemDescription.ToString(), itemID.ToString());
                return new TextEntry(localizedString, "");
            }

            /// <summary>
            /// 获取物品子类型的 Entry（用于 TextCombiner）
            /// </summary>
            public TextEntry GetCategoryEntry()
            {
                return subType.GetEntry();
            }

            #endregion
        }

        /// <summary>
        /// 物品自定义参数
        /// </summary>
        [Serializable]
        public class ItemParam
        {
            public string key;
            public string displayName;
            public int value;

            public ItemParam() { }

            public ItemParam(string key, string displayName, int value)
            {
                this.key = key;
                this.displayName = displayName;
                this.value = value;
            }

            /// <summary>
            /// 获取参数名称的本地化 Entry
            /// </summary>
            public TextEntry GetNameEntry()
            {
                if (string.IsNullOrEmpty(key)) return null;
                var localizedString = new LocalizedString(TableName.SkillParamName.ToString(), key);
                return new TextEntry(localizedString, "");
            }
        }

        public List<ItemData> itemDataList = new();
        private Dictionary<ItemName, ItemData> dataCache;

    #if UNITY_EDITOR
        private void OnValidate()
        {
            if (itemDataList == null) return;
        }
    #endif

        public void BuildCache()
        {
            dataCache = new Dictionary<ItemName, ItemData>();
            foreach (var data in itemDataList)
            {
                if (data != null && !dataCache.ContainsKey(data.itemID))
                {
                    dataCache.Add(data.itemID, data);
                }
            }
        }

        public ItemData GetItemData(ItemName itemID)
        {
            if (dataCache == null) BuildCache();
            dataCache.TryGetValue(itemID, out var data);
            return data;
        }

        public bool TryGetItemData(ItemName itemID, out ItemData data)
        {
            if (dataCache == null) BuildCache();
            return dataCache.TryGetValue(itemID, out data);
        }

        public List<ItemData> GetAllItems()
        {
            if (dataCache == null) BuildCache();
            return new List<ItemData>(dataCache.Values);
        }

        public List<ItemData> GetItemsByStarLevel(int starLevel)
        {
            if (dataCache == null) BuildCache();

            var result = new List<ItemData>();
            foreach (var data in dataCache.Values)
            {
                if (data.starLevel == starLevel)
                    result.Add(data);
            }
            return result;
        }

        public bool HasItem(ItemName itemID)
        {
            if (dataCache == null) BuildCache();
            return dataCache.ContainsKey(itemID);
        }

        /// <summary>
        /// 获取物品在 itemDataList 中的索引（配置文件顺序），不存在返回 -1
        /// </summary>
        public int GetItemIndex(ItemName itemID)
        {
            for (int i = 0; i < itemDataList.Count; i++)
            {
                if (itemDataList[i] != null && itemDataList[i].itemID == itemID)
                    return i;
            }
            return -1;
        }

        public int GetItemCount()
        {
            if (dataCache == null) BuildCache();
            return dataCache.Count;
        }
    }
}



