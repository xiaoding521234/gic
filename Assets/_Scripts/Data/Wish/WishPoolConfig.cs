using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace GIC.Data
{
    /// <summary>
    /// 祈愿卡池配置 — 定义一个卡池包含哪些卡牌
    /// </summary>
    [CreateAssetMenu(fileName = "WishPool_", menuName = "GIC/Wish Pool Config")]
    public class WishPoolConfig : ScriptableObject
    {
        [Header("卡池信息")]
        public string poolName;
        [TextArea] public string description;

        [Header("角色卡")]
        public List<UnitName> units = new();

        [Header("物品卡")]
        public List<ItemName> items = new();

        [Header("出卡权重（概率 = 权重 / 总权重）")]
        [FormerlySerializedAs("star5Rate")] public float star5Weight = 1f;
        [FormerlySerializedAs("star4Rate")] public float star4Weight = 4f;
        [FormerlySerializedAs("star3Rate")] public float star3Weight = 10f;
        [FormerlySerializedAs("star2Rate")] public float star2Weight = 30f;
        [FormerlySerializedAs("star1Rate")] public float star1Weight = 55f;

        [Header("角色/物品权重")]
        [FormerlySerializedAs("unitRate")] public float unitWeight = 50f;
        public float itemWeight = 50f;

        [Header("相遇之线升级权重（概率 = 权重 / 总权重）")]
        public float upgrade0Weight = 40f;
        public float upgrade1Weight = 30f;
        public float upgrade2Weight = 20f;
        public float upgrade3Weight = 7f;
        public float upgrade4Weight = 3f;

        /// <summary>
        /// 获取指定星级的所有角色
        /// </summary>
        public List<UnitName> GetUnitsByStar(UnitConfig unitConfig, int starLevel)
        {
            var result = new List<UnitName>();
            foreach (var unitName in units)
            {
                var data = unitConfig.GetUnitData(unitName);
                if (data != null && data.starLevel == starLevel)
                    result.Add(unitName);
            }
            return result;
        }

        /// <summary>
        /// 获取指定星级的所有物品
        /// </summary>
        public List<ItemName> GetItemsByStar(ItemConfig itemConfig, int starLevel)
        {
            var result = new List<ItemName>();
            foreach (var itemName in items)
            {
                var data = itemConfig.GetItemData(itemName);
                if (data != null && data.starLevel == starLevel)
                    result.Add(itemName);
            }
            return result;
        }

        /// <summary>
        /// 按权重抽取一个星级 (1-5)
        /// </summary>
        public int RollStarLevel()
        {
            float totalWeight = star5Weight + star4Weight + star3Weight + star2Weight + star1Weight;
            if (totalWeight <= 0f) return 1;

            float roll = UnityEngine.Random.Range(0f, totalWeight);
            float cumulative = 0f;

            cumulative += star5Weight;
            if (roll < cumulative) return 5;

            cumulative += star4Weight;
            if (roll < cumulative) return 4;

            cumulative += star3Weight;
            if (roll < cumulative) return 3;

            cumulative += star2Weight;
            if (roll < cumulative) return 2;

            return 1;
        }

        /// <summary>
        /// 按权重决定是角色还是物品
        /// </summary>
        public bool RollIsUnit()
        {
            float totalWeight = unitWeight + itemWeight;
            if (totalWeight <= 0f) return true;
            return UnityEngine.Random.Range(0f, totalWeight) < unitWeight;
        }

        /// <summary>
        /// 按权重抽取相遇之线升级次数 (0-4)
        /// </summary>
        public int RollUpgradeCount()
        {
            float totalWeight = upgrade0Weight + upgrade1Weight + upgrade2Weight + upgrade3Weight + upgrade4Weight;
            if (totalWeight <= 0f) return 0;

            float roll = UnityEngine.Random.Range(0f, totalWeight);
            float cumulative = 0f;

            cumulative += upgrade0Weight;
            if (roll < cumulative) return 0;

            cumulative += upgrade1Weight;
            if (roll < cumulative) return 1;

            cumulative += upgrade2Weight;
            if (roll < cumulative) return 2;

            cumulative += upgrade3Weight;
            if (roll < cumulative) return 3;

            return 4;
        }
    }
}
