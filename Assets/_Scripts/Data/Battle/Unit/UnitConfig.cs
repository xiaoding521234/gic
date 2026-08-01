using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using GIC.Framework;
using GIC.Battle;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
namespace GIC.Data
{


    [CreateAssetMenu(fileName = "UnitConfig", menuName = "Game/UnitConfig")]
    public class UnitConfig : ScriptableObject
    {
        /// <summary>
        /// 哨兵值：表示该属性未手动设置，应使用派生值
        /// </summary>
        public const int Unspecified = -64;

        [System.Serializable]
        public class UnitData
        {
            public UnitName unitName = UnitName.Amber;
            public UnitType unitType = UnitType.Character;
            public FactionType[] factions;

            public Sprite avatar;
            public List<Sprite> cards;
            public Sprite nameCard;
            public bool hideInBackpack = false;

            [Header("稀有度")]
            [Range(1, 5)]
            public int starLevel = 3;
            public int deployCost = Unspecified;

            [Header("武器")]
            public WeaponType weaponType = WeaponType.Claymore;

            [Header("基础属性")]
            public int baseHP = Unspecified;
            public int baseAttack = Unspecified;
            public int baseDefense = Unspecified;
            public int baseAttackSpeed = Unspecified;
            public int baseMoveSpeed = Unspecified;
            public int baseLuck = Unspecified;
            public int baseTenacity = Unspecified;
            public int baseMastery = Unspecified;
            public int baseSanity = Unspecified;

            [Header("常态移动")]
            public ForceType normalMoveType = ForceType.Walk;

            [Header("碰撞规则")]
            public bool blockAllies = true;
            public bool blockEnemies = true;
            public bool blockedByEnemies = true;

            [Header("元素")]
            public ElementType selfElement = ElementType.Physical;

            [Header("视野")]
            public int visionRange = Unspecified;

            [Header("标签")]
            public UnitTag[] tags;

            [Header("技能")]
            public SkillConfig.SkillData[] skills;

            private bool IsSpecified(int value) => value != Unspecified;

            public int GetDeployCost()
            {
                if (IsSpecified(deployCost)) return deployCost;
                return starLevel switch { 1 => 10, 2 => 20, 3 => 50, 4 => 100, 5 => 300, _ => 50 };
            }

            public int GetHPByStarLevel()
            {
                return starLevel switch { 1 => 150, 2 => 200, 3 => 200, 4 => 250, 5 => 300, _ => 200 };
            }

            public int GetAttackByWeaponType()
            {
                return weaponType switch
                {
                    WeaponType.Claymore => 60, WeaponType.Sword => 40,
                    WeaponType.Polearm => 40, WeaponType.Gauntlet => 40,
                    WeaponType.Catalyst => 60, WeaponType.Bow => 50,
                    WeaponType.Gun => 50, _ => 40
                };
            }

            public int GetAttackSpeedByWeaponType()
            {
                return weaponType switch
                {
                    WeaponType.Claymore => 20, WeaponType.Sword => 40,
                    WeaponType.Polearm => 50, WeaponType.Gauntlet => 60,
                    WeaponType.Catalyst => 10, WeaponType.Bow => 30,
                    WeaponType.Gun => 70, _ => 40
                };
            }

            public int GetEffectiveAttack() => IsSpecified(baseAttack) ? baseAttack : GetAttackByWeaponType();
            public int GetEffectiveAttackSpeed() => IsSpecified(baseAttackSpeed) ? baseAttackSpeed : GetAttackSpeedByWeaponType();
            public int GetEffectiveDefense() => IsSpecified(baseDefense) ? baseDefense : 0;
            public int GetEffectiveMoveSpeed() => IsSpecified(baseMoveSpeed) ? baseMoveSpeed : 3;
            public int GetEffectiveLuck() => IsSpecified(baseLuck) ? baseLuck : 0;
            public int GetEffectiveTenacity() => IsSpecified(baseTenacity) ? baseTenacity : 0;
            public int GetEffectiveMastery() => IsSpecified(baseMastery) ? baseMastery : 0;
            public int GetEffectiveSanity() => IsSpecified(baseSanity) ? baseSanity : 50;
            public int GetEffectiveHP() => IsSpecified(baseHP) ? baseHP : GetHPByStarLevel();
            public int GetEffectiveVisionRange() => IsSpecified(visionRange) ? visionRange : 1;
            public int GetEffectiveDeployCost() => IsSpecified(deployCost) ? deployCost : GetDeployCost();

            /// <summary>
            /// 是否为角色单位
            /// </summary>
            public bool IsCharacter => unitType == UnitType.Character;

            /// <summary>
            /// 是否为造物单位
            /// </summary>
            public bool IsCreation => unitType == UnitType.Creation;

            /// <summary>
            /// 是否为造物（原 Interactive 已合并到 Creation）
            /// </summary>
            public bool IsInteractive => unitType == UnitType.Creation;

            public Sprite GetCard(int skin)
            {
                // 添加边界检查
                if (cards == null || cards.Count == 0)
                    return null;

                // 确保索引在有效范围内
                if (skin < 0 || skin >= cards.Count)
                {
                    Debug.LogWarning($"GetCard: 皮肤索引 {skin} 超出范围 (0-{cards.Count - 1})，返回默认卡片");
                    return cards[0]; // 返回第一张卡片作为默认
                }

                return cards[skin];
            }

            #region 本地化 Entry 获取方法

            /// <summary>
            /// 获取单位名称的 Entry（用于 TextCombiner）
            /// </summary>
            public TextEntry GetNameEntry()
            {
                return unitName.GetEntry();
            }

            /// <summary>
            /// 获取单位称号的 Entry（用于 TextCombiner）
            /// </summary>
            public TextEntry GetTitleEntry()
            {
                var localizedString = new LocalizedString(TableName.UnitTitle.ToString(), unitName.ToString());
                return new TextEntry(localizedString, "");
            }

            /// <summary>
            /// 获取单位介绍的 Entry（用于 TextCombiner）
            /// </summary>
            public TextEntry GetDescriptionEntry()
            {
                var localizedString = new LocalizedString(TableName.UnitDescription.ToString(), unitName.ToString());
                return new TextEntry(localizedString, "");
            }

            /// <summary>
            /// 获取单位获取描述的 Entry（用于 TextCombiner）
            /// </summary>
            public TextEntry GetObtainDescriptionEntry()
            {
                var localizedString = new LocalizedString(TableName.UnitObtainDescription.ToString(), unitName.ToString());
                return new TextEntry(localizedString, "");
            }

            #endregion
        }

        public List<UnitData> unitDataList = new();
        private Dictionary<UnitName, UnitData> dataCache;

        public void BuildCache()
        {
            dataCache = new Dictionary<UnitName, UnitData>();
            foreach (var data in unitDataList)
            {
                if (data != null && !dataCache.ContainsKey(data.unitName))
                {
                    dataCache.Add(data.unitName, data);
                }
            }
        }

        public UnitData GetUnitData(UnitName unitName)
        {
            if (dataCache == null) BuildCache();
            dataCache.TryGetValue(unitName, out var data);
            return data;
        }

        public bool TryGetUnitData(UnitName unitName, out UnitData data)
        {
            if (dataCache == null) BuildCache();
            return dataCache.TryGetValue(unitName, out data);
        }

        public List<UnitData> GetAllUnits()
        {
            if (dataCache == null) BuildCache();
            return new List<UnitData>(dataCache.Values);
        }

        public List<UnitData> GetUnitsByType(UnitType unitType)
        {
            if (dataCache == null) BuildCache();

            var result = new List<UnitData>();
            foreach (var data in dataCache.Values)
            {
                if (data.unitType == unitType)
                    result.Add(data);
            }
            return result;
        }

        public List<UnitData> GetUnitsByFaction(FactionType faction)
        {
            if (dataCache == null) BuildCache();

            var result = new List<UnitData>();
            foreach (var data in dataCache.Values)
            {
                if (data.factions != null)
                {
                    foreach (var f in data.factions)
                    {
                        if (f == faction)
                        {
                            result.Add(data);
                            break;
                        }
                    }
                }
            }
            return result;
        }

        public List<UnitData> GetUnitsByStarLevel(int starLevel)
        {
            if (dataCache == null) BuildCache();

            var result = new List<UnitData>();
            foreach (var data in dataCache.Values)
            {
                if (data.starLevel == starLevel)
                    result.Add(data);
            }
            return result;
        }

        public List<UnitData> GetUnitsByWeaponType(WeaponType weaponType)
        {
            if (dataCache == null) BuildCache();

            var result = new List<UnitData>();
            foreach (var data in dataCache.Values)
            {
                if (data.weaponType == weaponType)
                    result.Add(data);
            }
            return result;
        }

        public bool HasUnit(UnitName unitName)
        {
            if (dataCache == null) BuildCache();
            return dataCache.ContainsKey(unitName);
        }

        public int GetUnitCount()
        {
            if (dataCache == null) BuildCache();
            return dataCache.Count;
        }
    }
}




