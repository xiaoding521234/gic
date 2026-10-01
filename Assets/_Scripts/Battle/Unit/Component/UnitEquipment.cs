using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 单位装备组件——武器与配件（2026-10-02 拍板「UnitInventory 改为武器与配件（这些都是单位
    /// 可以装备的物品）」：原空壳背包组件退役，本组件为装备系统的单位侧载体骨架）。
    ///
    /// 规则真源：docs/05 §5.2 装备槽位表——武器槽恒 1（选择策略性而非堆叠）、配件槽随星级
    /// （1~2★=1、3★=2、4★=3、5★=4=max(1, 星级-1)）；装备=玩家配额行动之一（EquipItem，
    /// 与使用物品同走「物品本身」资源），装备后物品牌消耗（docs/01 §卡牌性质——武器=加攻击力
    /// 等战斗属性、配件=词条/触发被动/存储资源等特殊效果）。
    ///
    /// **本组件=状态载体骨架**：槽位持有+装备/卸下/查询 API；行动链（EquipItem 校验/命令产出/
    /// 属性词条应用）随装备批接线——届时词条等配置走 ItemConfig（数据驱动勿硬编码），
    /// 属性加成走 UnitStats 修改器（StatModifier），B8 遗留③兔兔伯爵挂配件系统（docs/11）。
    /// 战场外（大地图探索）如需装备另有系统，勿跨层复用本组件。
    /// </summary>
    public class UnitEquipment : MonoBehaviour, IUnitComponent
    {
        private Unit _owner;

        /// <summary>武器槽（恒 1，docs/05 §5.2——null=未装备）</summary>
        private ItemName? _weapon;

        /// <summary>配件槽（数量随星级——Init 按星级分配；槽满不可再装）</summary>
        private readonly List<ItemName?> _accessories = new();

        /// <summary>已装备武器（null=空槽）</summary>
        public ItemName? Weapon => _weapon;

        /// <summary>配件槽快照（只读；null=空槽位）</summary>
        public IReadOnlyList<ItemName?> Accessories => _accessories;

        /// <summary>配件槽数量上限（星级驱动单出口：max(1, 星级-1)，docs/05 §5.2 表）</summary>
        public int AccessorySlotCount => _accessories.Count;

        public void Init(Unit unit)
        {
            _owner = unit;
            _weapon = null;
            _accessories.Clear();
            int star = unit.RawData != null ? unit.RawData.starLevel : 1;
            int slots = star > 1 ? star - 1 : 1; // 1~2★=1、3★=2、4★=3、5★=4
            for (int i = 0; i < slots; i++)
                _accessories.Add(null);
        }

        // ==================== 装备/卸下（状态操作——行动链校验由装备批的 EquipItem 执行器负责） ====================

        /// <summary>装备武器（武器槽恒 1，已占用返回 false 不顶替）</summary>
        public bool EquipWeapon(ItemName weapon)
        {
            if (_weapon.HasValue) return false;
            _weapon = weapon;
            return true;
        }

        /// <summary>装备配件（首个空槽；无空槽返回 false）</summary>
        public bool EquipAccessory(ItemName accessory)
        {
            for (int i = 0; i < _accessories.Count; i++)
            {
                if (_accessories[i].HasValue) continue;
                _accessories[i] = accessory;
                return true;
            }
            return false;
        }

        /// <summary>卸下武器（空槽返回 false）；状态回写装备批产出命令时由执行器调用</summary>
        public bool UnequipWeapon()
        {
            if (!_weapon.HasValue) return false;
            _weapon = null;
            return true;
        }

        /// <summary>卸下指定槽位配件（槽号越界/空槽返回 false）</summary>
        public bool UnequipAccessory(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _accessories.Count || !_accessories[slotIndex].HasValue)
                return false;
            _accessories[slotIndex] = null;
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
    }
}
