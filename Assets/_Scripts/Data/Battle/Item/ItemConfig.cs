using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEngine.Localization;
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
            [Range(1, 10)]
            public int countPerServing = 1;

            [Header("战斗")]
            [Tooltip("战斗结束后是否回收")]
            public bool recycleAfterBattle = true;

            [Tooltip("回收时丢失数量")]
            public int recycleLossCount = 0;

            [Header("自定义参数")]
            [SerializeField] public ItemParam[] customParams;

            [Header("战斗效果（2026-10-16 首个可打出物品批）")]
            /// <summary>使用效果原子（可打出物品判定单源：非空=可经 UseItem 行动打出手牌，目标=点选的
            /// 我方存活单位〔饮用者〕；原子=技能效果原子同款，物品侧约定 trigger 恒 OnCast、paramKey 恒
            /// None〔物品数值走 value 直读 Fixed 语义〕、数值与描述文本双源点登记 docs/11）。
            /// 首个物品=蒲公英酒（SanityChange -10 + Heal 40，酒鬼条件在原子上配）。</summary>
            public List<SkillEffectConfig> effects;

            /// <summary>是否配有使用效果（=可打出判定单源：货币卡/素材无 effects 恒不可打出）</summary>
            public bool HasEffects => effects != null && effects.Count > 0;

            [Header("装备词条（2026-10-18 装备批：武器=加属性/配件=特殊效果——docs/05 §5.2）")]
            /// <summary>装备时应用的属性修改（武器词条：铁剑=攻击等；空=无词条纯占位装备）。
            /// 装备时经 UnitEquipment 挂 UnitStats 修改器（BaseFlat 区），配件耐久耗尽卸下时随槽撤除</summary>
            public List<EquipStatEntry> equipStats;

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

            #region localization Entry 获取方法

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
            /// 获取物品效果描述的 Entry（用于 TextCombiner；2026-10-16 分表重构批——风味/效果分表，
            /// 对齐单位系统「UnitDescription 纯风味/效果在技能描述表」的分表惯例；效果行·\n 分行+
            /// {Key} 占位符渲染由消费方走 SkillDescriptionBuilder.BuildItem）
            /// </summary>
            public TextEntry GetEffectDescriptionEntry()
            {
                var localizedString = new LocalizedString(TableName.ItemEffectDescription.ToString(), itemID.ToString());
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

        /// <summary>
        /// 装备词条（2026-10-18 装备批）：装备时挂到单位 UnitStats 的属性修改（武器=加攻击力等
        /// 战斗属性，docs/05 §5.2「武器槽位固定 1，确保武器选择的策略性」）；配在 ItemData.equipStats
        /// </summary>
        [Serializable]
        public class EquipStatEntry
        {
            [Tooltip("属性类型（StatType：攻击/防御/移速…）")]
            public StatType statType = StatType.Attack;

            [Tooltip("加值（正=提升/负=降低；BaseFlat 平伏加值——Percent 基准暂不支持，首个百分比武器需求时扩）")]
            public int value;

            public EquipStatEntry() { }

            public EquipStatEntry(StatType statType, int value)
            {
                this.statType = statType;
                this.value = value;
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



