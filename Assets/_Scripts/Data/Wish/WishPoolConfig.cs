using System;
using System.Collections.Generic;
using UnityEngine;

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

        [Header("出卡概率（百分比）")]
        [Range(0, 100)] public float star5Rate = 2f;
        [Range(0, 100)] public float star4Rate = 8f;
        [Range(0, 100)] public float star3Rate = 15f;
        [Range(0, 100)] public float star2Rate = 30f;
        [Range(0, 100)] public float star1Rate = 45f;

        [Header("角色/物品比例")]
        [Range(0, 100)] public float unitRate = 50f;

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
        /// 按概率抽取一个星级 (1-5)
        /// </summary>
        public int RollStarLevel()
        {
            float roll = UnityEngine.Random.Range(0f, 100f);
            float cumulative = 0f;

            cumulative += star5Rate;
            if (roll < cumulative) return 5;

            cumulative += star4Rate;
            if (roll < cumulative) return 4;

            cumulative += star3Rate;
            if (roll < cumulative) return 3;

            cumulative += star2Rate;
            if (roll < cumulative) return 2;

            return 1;
        }

        /// <summary>
        /// 按概率决定是角色还是物品
        /// </summary>
        public bool RollIsUnit()
        {
            return UnityEngine.Random.Range(0f, 100f) < unitRate;
        }
    }
}
