using UnityEngine;
using System.Collections.Generic;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
namespace GIC.Battle
{



    public class StatModifier
    {
        public StatType StatType;
        public int Value;
        public StatModifierType Type;

        public StatModifier(StatType statType, int value, StatModifierType type)
        {
            StatType = statType;
            Value = value;
            Type = type;
        }
    }

    public class UnitStats : MonoBehaviour, IUnitComponent
    {
        private Unit _owner;

        // ==================== 原始属性模板 ====================
        private static readonly Dictionary<StatType, RangedInt> _templateStats = new()
    {
        { StatType.Attack, new RangedInt(-500, 500, 40) },
        { StatType.Defense, new RangedInt(-300, 300, 0) },
        { StatType.AttackSpeed, new RangedInt(-100, 100, 40) },
        { StatType.MoveSpeed, new RangedInt(-100, 100, 30) },
        { StatType.Luck, new RangedInt(-100, 100, 0) },
        { StatType.Tenacity, new RangedInt(-100, 100, 0) },
        { StatType.Mastery, new RangedInt(-300, 300, 0) },
        { StatType.Sanity, new RangedInt(-300, 300, 100) },
        { StatType.Penetration, new RangedInt(-100, 100, 0) },
        { StatType.HP, new RangedInt(0, 200, 200) },
        { StatType.VisionRange, new RangedInt(0, 10, 1) },
        
        { StatType.DamageReduction, new RangedInt(-300, 300, 0) },
        { StatType.DamageBonus, new RangedInt(-300, 300, 0) },
        { StatType.LifeSteal, new RangedInt(0, 100, 0) },
        { StatType.HealEfficiency, new RangedInt(0, 300, 100) },
        { StatType.Energy, new RangedInt(0, 1000, 0) },
    };

        // ==================== 当前属性实例 ====================
        private Dictionary<StatType, RangedInt> _stats = new();

        // ==================== 修改器列表 ====================
        private List<StatModifier> _modifiers = new();

        // ==================== 最终属性 ====================
        public int Attack => GetFinalStat(StatType.Attack);
        public int Defense => GetFinalStat(StatType.Defense);
        public int AttackSpeed => GetFinalStat(StatType.AttackSpeed);
        public int MoveSpeed => GetFinalStat(StatType.MoveSpeed);
        public int Luck => GetFinalStat(StatType.Luck);
        public int Tenacity => GetFinalStat(StatType.Tenacity);
        public int Mastery => GetFinalStat(StatType.Mastery);
        public int Sanity => GetFinalStat(StatType.Sanity);
        public float Penetration => GetFinalStat(StatType.Penetration);
        public int HP => GetFinalStat(StatType.HP);
        public int VisionRange => GetFinalStat(StatType.VisionRange);

        public float DamageReduction => GetFinalStat(StatType.DamageReduction);
        public float DamageBonus => GetFinalStat(StatType.DamageBonus);
        public int LifeSteal => GetFinalStat(StatType.LifeSteal);
        public int HealEfficiency => GetFinalStat(StatType.HealEfficiency);
        public int Energy => GetFinalStat(StatType.Energy);

        // ==================== 初始化 ====================
        public void Init(Unit owner)
        {
            _owner = owner;

            // 从模板复制初始值
            _stats.Clear();
            foreach (var kv in _templateStats)
            {
                _stats[kv.Key] = new RangedInt(kv.Value.Min, kv.Value.Max, kv.Value.Value);
            }

            // 从 UnitConfig 读取基础属性值
            var rawData = owner.RawData;
            if (rawData != null)
            {
                SetBaseValue(StatType.Attack, rawData.GetEffectiveAttack());
                SetBaseValue(StatType.Defense, rawData.GetEffectiveDefense());
                SetBaseValue(StatType.AttackSpeed, rawData.GetEffectiveAttackSpeed());
                SetBaseValue(StatType.MoveSpeed, rawData.GetEffectiveMoveSpeed());
                SetBaseValue(StatType.Luck, rawData.GetEffectiveLuck());
                SetBaseValue(StatType.Tenacity, rawData.GetEffectiveTenacity());
                SetBaseValue(StatType.Mastery, rawData.GetEffectiveMastery());
                SetBaseValue(StatType.Sanity, rawData.GetEffectiveSanity());
                // HP: value=max=baseHP（满血登场）
                SetCapacityValue(StatType.HP, rawData.GetEffectiveHP());
                SetBaseValue(StatType.VisionRange, rawData.GetEffectiveVisionRange());
                SetBaseValue(StatType.LifeSteal, rawData.GetEffectiveLifeSteal());
                SetBaseValue(StatType.HealEfficiency, rawData.GetEffectiveHealEfficiency());
                // Energy: max=baseEnergy, value=0（空蓝登场）
                _stats[StatType.Energy] = new RangedInt(0, rawData.GetEffectiveEnergy(), 0);
            }
            else
            {
                GICLog.Warn($"[UnitStats] 单位 {owner.name} 没有 rawData，使用模板默认值");
            }
        }

        // ==================== 最终属性计算 ====================
        public int GetFinalStat(StatType statType)
        {
            if (!_stats.TryGetValue(statType, out RangedInt template))
            {
                GICLog.Warn($"[UnitStats] 未找到属性: {statType}");
                return 0;
            }

            int baseValue = template.Value;
            int min = template.Min;
            int max = template.Max;

            float baseFlat = 0, basePercent = 0;
            float minFlat = 0, minPercent = 0;
            float maxFlat = 0, maxPercent = 0;
            float valueFlat = 0, valuePercent = 0;

            foreach (var mod in _modifiers)
            {
                if (mod.StatType != statType) continue;

                switch (mod.Type)
                {
                    case StatModifierType.BaseFlat:
                        baseFlat += mod.Value;
                        break;
                    case StatModifierType.BasePercent:
                        basePercent += mod.Value;
                        break;
                    case StatModifierType.MinFlat:
                        minFlat += mod.Value;
                        break;
                    case StatModifierType.MinPercent:
                        minPercent += mod.Value;
                        break;
                    case StatModifierType.MaxFlat:
                        maxFlat += mod.Value;
                        break;
                    case StatModifierType.MaxPercent:
                        maxPercent += mod.Value;
                        break;
                    case StatModifierType.ValueFlat:
                        valueFlat += mod.Value;
                        break;
                    case StatModifierType.ValuePercent:
                        valuePercent += mod.Value;
                        break;
                }
            }

            int finalBase = Mathf.RoundToInt(baseValue * (1 + basePercent / 100f) + baseFlat);
            int finalMin = Mathf.RoundToInt(min * (1 + minPercent / 100f) + minFlat);
            int finalMax = Mathf.RoundToInt(max * (1 + maxPercent / 100f) + maxFlat);
            int finalValue = Mathf.RoundToInt(finalBase * (1 + valuePercent / 100f) + valueFlat);

            return Mathf.Clamp(finalValue, finalMin, finalMax);
        }

        // ==================== 基础值管理 ====================
        public void SetBaseValue(StatType statType, int value)
        {
            if (_stats.TryGetValue(statType, out RangedInt stat))
            {
                stat.Value = value;
                _stats[statType] = stat;
            }
            else
            {
                GICLog.Warn($"[UnitStats] 未找到属性: {statType}");
            }
        }

        /// <summary>
        /// 设置容量型属性（value=max=传入值），用于 HP 等登场即满的属性
        /// </summary>
        public void SetCapacityValue(StatType statType, int value)
        {
            _stats[statType] = new RangedInt(0, value, value);
        }

        public int GetBaseValue(StatType statType)
        {
            return _stats.TryGetValue(statType, out RangedInt stat) ? stat.Value : 0;
        }

        // ==================== 属性结构体访问 ====================
        public RangedInt GetStatStruct(StatType statType)
        {
            if (_stats.TryGetValue(statType, out RangedInt stat))
                return stat;
            return default;
        }

        public void SetStatStruct(StatType statType, RangedInt stat)
        {
            _stats[statType] = stat;
        }

        public void SetStatRange(StatType statType, int? min = null, int? max = null)
        {
            if (_stats.TryGetValue(statType, out RangedInt stat))
            {
                if (min.HasValue)
                    stat.SetMin(min.Value);
                if (max.HasValue)
                    stat.SetMax(max.Value);
                _stats[statType] = stat;
            }
        }

        // ==================== 修改器管理 ====================
        public void AddModifier(StatModifier modifier)
        {
            _modifiers.Add(modifier);
        }

        public bool RemoveModifier(StatModifier modifier)
        {
            return _modifiers.Remove(modifier);
        }

        public void RemoveModifiers(System.Predicate<StatModifier> match)
        {
            _modifiers.RemoveAll(match);
        }

        public void ClearAllModifiers()
        {
            _modifiers.Clear();
        }

        public List<StatModifier> GetAllModifiers()
        {
            return new List<StatModifier>(_modifiers);
        }

        // ==================== 便利方法 ====================
        public void ResetToTemplate()
        {
            _stats.Clear();
            foreach (var kv in _templateStats)
            {
                _stats[kv.Key] = new RangedInt(kv.Value.Min, kv.Value.Max, kv.Value.Value);
            }
        }

        public Dictionary<StatType, RangedInt> GetAllStats()
        {
            return new Dictionary<StatType, RangedInt>(_stats);
        }
    }
}


