using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
using GIC.Framework;
namespace GIC.Battle
{


    /// <summary>
    /// 单位装备组件——武器与配件（2026-10-02 拍板立项骨架、2026-10-18 装备批全链接线）。
    ///
    /// 规则真源：docs/05 §5.2 装备槽位——**武器槽恒 1**（选择策略性而非堆叠）、**配件槽按层级**
    /// （2026-10-18 用户拍板「眷属统一为可装 1 个配件，伙伴统一为可装 2 个配件，魔神统一为可装
    /// 3 个配件」——取代旧星级表 max(1, 星级-1)）；装备=玩家配额行动之一（EquipItem，与使用物品
    /// 同走打出链——精确指定目标单位），装备后物品牌消耗（docs/01 §卡牌性质）。
    ///
    /// 词条与特殊效果（数据驱动，docs/11 装备批 schema）：
    /// - 属性词条：ItemData.equipStats（武器=加攻击力等战斗属性）→ 装备时挂 UnitStats 修改器
    ///   （BaseFlat 区），配件耐久耗尽卸下时随槽撤除；
    /// - 伤害抵挡（兔兔伯爵）：ItemData.customParams 的 BlockThreshold/Durability 键
    ///   （描述占位符/参数表/格挡真源三方单源，食物批 SanityCost 先例）——受到 ≥阈值 的伤害时
    ///   完全抵挡并消耗 1 点耐久，耗尽消失（TryConsumeBlock 单出口，TurnResolver.ApplyEffects 消费）。
    ///
    /// 战场外（大地图探索）如需装备另有系统，勿跨层复用本组件。
    /// </summary>
    public class UnitEquipment : MonoBehaviour, IUnitComponent
    {
        private Unit _owner;

        /// <summary>武器槽（恒 1，docs/05 §5.2——null=未装备）</summary>
        private ItemName? _weapon;

        /// <summary>武器耐久（0=无耐久概念）</summary>
        private int _weaponDurability;

        /// <summary>配件槽（数量按层级——Init 分配；null=空槽位）</summary>
        private readonly List<ItemName?> _accessories = new();

        /// <summary>配件耐久表（与 _accessories 平行；0=空槽/无耐久概念）</summary>
        private readonly List<int> _accessoryDurability = new();

        /// <summary>武器词条修改器（装备时挂、武器无卸下路径恒随单位存续）</summary>
        private readonly List<StatModifier> _weaponMods = new();

        /// <summary>配件词条修改器（与 _accessories 平行——耐久耗尽卸下时随槽撤除）</summary>
        private readonly List<List<StatModifier>> _accessoryMods = new();

        /// <summary>已装备武器（null=空槽）</summary>
        public ItemName? Weapon => _weapon;

        /// <summary>武器耐久（0=无耐久概念）</summary>
        public int WeaponDurability => _weaponDurability;

        /// <summary>配件槽快照（只读；null=空槽位）</summary>
        public IReadOnlyList<ItemName?> Accessories => _accessories;

        /// <summary>配件槽数量上限（层级驱动：眷属 1/伙伴 2/魔神 3——2026-10-18 拍板，UnitTierHelper 单源）</summary>
        public int AccessorySlotCount => _accessories.Count;

        public void Init(Unit unit)
        {
            _owner = unit;
            _weapon = null;
            _weaponDurability = 0;
            _accessories.Clear();
            _accessoryDurability.Clear();
            _weaponMods.Clear();
            _accessoryMods.Clear();
            int star = unit.RawData != null ? unit.RawData.starLevel : 1;
            int slots = AccessorySlotsOfTier(UnitTierHelper.FromStars(star));
            for (int i = 0; i < slots; i++)
            {
                _accessories.Add(null);
                _accessoryDurability.Add(0);
                _accessoryMods.Add(new List<StatModifier>());
            }
        }

        /// <summary>层级→配件槽位数（单源出口：眷属 1/伙伴 2/魔神 3，2026-10-18 拍板）</summary>
        public static int AccessorySlotsOfTier(UnitTier tier) => tier switch
        {
            UnitTier.Companion => 2,
            UnitTier.Archon => 3,
            _ => 1, // 眷属
        };

        // ==================== 装备/卸下（状态操作——行动链校验由 EquipItemExecutor 负责） ====================

        /// <summary>武器槽是否空闲</summary>
        public bool HasFreeWeaponSlot => !_weapon.HasValue;

        /// <summary>是否有空配件槽</summary>
        public bool HasFreeAccessorySlot
        {
            get
            {
                foreach (var accessory in _accessories)
                    if (!accessory.HasValue) return true;
                return false;
            }
        }

        /// <summary>装备武器（武器槽恒 1，已占用返回 false 不顶替）；词条即时挂 UnitStats</summary>
        public bool EquipWeapon(ItemName weapon)
        {
            if (_weapon.HasValue) return false;
            var data = ItemDataOf(weapon);
            _weapon = weapon;
            _weaponDurability = data != null ? data.GetInt(nameof(SkillParamKey.Durability), 0) : 0;
            ApplyEquipStats(data, _weaponMods);
            return true;
        }

        /// <summary>装备配件（首个空槽；无空槽返回 false）——槽号经 out 返回（命令载荷用）</summary>
        public bool EquipAccessory(ItemName accessory, out int slotIndex)
        {
            slotIndex = -1;
            for (int i = 0; i < _accessories.Count; i++)
            {
                if (_accessories[i].HasValue) continue;
                _accessories[i] = accessory;
                var data = ItemDataOf(accessory);
                _accessoryDurability[i] = data != null ? data.GetInt(nameof(SkillParamKey.Durability), 0) : 0;
                ApplyEquipStats(data, _accessoryMods[i]);
                slotIndex = i;
                return true;
            }
            return false;
        }

        /// <summary>指定配件槽耐久（槽号越界=0）</summary>
        public int GetAccessoryDurability(int slotIndex)
            => slotIndex >= 0 && slotIndex < _accessoryDurability.Count ? _accessoryDurability[slotIndex] : 0;

        /// <summary>卸下武器（空槽返回 false）；状态回写装备批产出命令时由执行器调用</summary>
        public bool UnequipWeapon()
        {
            if (!_weapon.HasValue) return false;
            _weapon = null;
            _weaponDurability = 0;
            return true;
        }

        /// <summary>卸下指定槽位配件（槽号越界/空槽返回 false）——撤词条修改器</summary>
        public bool UnequipAccessory(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _accessories.Count || !_accessories[slotIndex].HasValue)
                return false;
            _accessories[slotIndex] = null;
            _accessoryDurability[slotIndex] = 0;
            RemoveSlotMods(slotIndex);
            return true;
        }

        /// <summary>是否已装备指定物品（任意槽）</summary>
        public bool HasEquipped(ItemName item)
        {
            if (_weapon == item) return true;
            foreach (var accessory in _accessories)
                if (accessory == item) return true;
            return false;
        }

        // ==================== 伤害抵挡（兔兔伯爵；TurnResolver.ApplyEffects 消费单出口） ====================

        /// <summary>格挡结果（TryConsumeBlock 出参载体——槽位/耐久供命令层发 EquipItem/UnequipItem）</summary>
        public struct BlockResult
        {
            public int Item;          // 格挡物品（ItemName 枚举值）
            public int SlotKind;      // BattleCommand.EquipKindWeapon/EquipKindAccessory
            public int SlotIndex;     // 配件槽号（武器=0）
            public int DurabilityLeft; // 消耗后剩余耐久
            public bool Removed;      // 耐久耗尽已卸下
        }

        /// <summary>格挡查询+消耗：incomingDamage ≥ 某已装备配件的 BlockThreshold 且该配件耐久 &gt;0 →
        /// 完全抵挡——耐久 -1，耗尽即卸下（撤词条）。返回 false=无可格挡。**尸体不调用**（HP 恒 0
        /// 无可保护，鞭尸白烧耐久——调用方守卫）。多枚可格挡配件按槽序取首个（确定性）</summary>
        public bool TryConsumeBlock(int incomingDamage, out BlockResult result)
        {
            result = default;
            if (incomingDamage <= 0) return false;
            for (int i = 0; i < _accessories.Count; i++)
            {
                var accessory = _accessories[i];
                if (!accessory.HasValue || _accessoryDurability[i] <= 0) continue;
                var data = ItemDataOf(accessory.Value);
                int threshold = data != null ? data.GetInt(nameof(SkillParamKey.BlockThreshold), 0) : 0;
                if (threshold <= 0 || incomingDamage < threshold) continue;

                _accessoryDurability[i]--;
                result = new BlockResult
                {
                    Item = (int)accessory.Value,
                    SlotKind = BattleCommand.EquipKindAccessory,
                    SlotIndex = i,
                    DurabilityLeft = _accessoryDurability[i],
                    Removed = _accessoryDurability[i] <= 0,
                };
                if (result.Removed)
                {
                    _accessories[i] = null;
                    _accessoryDurability[i] = 0;
                    RemoveSlotMods(i);
                }
                return true;
            }
            return false;
        }

        // ==================== 内部（配置解析/词条） ====================

        private static ItemConfig.ItemData ItemDataOf(ItemName item)
            => Wargame.Instance?.Context?.Get<ItemConfig>()?.GetItemData(item);

        /// <summary>装备词条挂 UnitStats（BaseFlat 平伏加值——Percent 基准暂不支持，首个百分比武器需求时扩）</summary>
        private void ApplyEquipStats(ItemConfig.ItemData data, List<StatModifier> into)
        {
            if (data?.equipStats == null || data.equipStats.Count == 0) return;
            var stats = _owner != null ? _owner.GetUnitComponent<UnitStats>() : null;
            if (stats == null)
            {
                GICLog.Warn($"[UnitEquipment] 单位无 UnitStats，物品 {data.itemID} 词条未应用");
                return;
            }
            foreach (var entry in data.equipStats)
            {
                if (entry == null || entry.value == 0) continue;
                var mod = new StatModifier(entry.statType, entry.value, StatModifierType.BaseFlat);
                stats.AddModifier(mod);
                into.Add(mod);
            }
        }

        /// <summary>撤指定配件槽全部词条修改器（耐久耗尽卸下）</summary>
        private void RemoveSlotMods(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _accessoryMods.Count) return;
            var stats = _owner != null ? _owner.GetUnitComponent<UnitStats>() : null;
            if (stats == null) return;
            foreach (var mod in _accessoryMods[slotIndex])
                stats.RemoveModifier(mod);
            _accessoryMods[slotIndex].Clear();
        }
    }
}
