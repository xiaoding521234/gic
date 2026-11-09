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
    ///   （BaseFlat 区），耐久归零停效/跨 0 恢复重挂（ReplenishAccessory 内联）；
    /// - 伤害抵挡（兔兔伯爵）：ItemData.customParams 的 BlockThreshold/Durability 键
    ///   （描述占位符/参数表/格挡真源三方单源，食物批 SanityCost 先例）——受到 ≥阈值 的伤害时
    ///   完全抵挡并消耗 1 点耐久（TryConsumeBlock 单出口，BattleResolver.ApplyEffects 消费）。
    ///
    /// 耐久统一方案（2026-10-18 决策八十一，用户拍板「舍弃堆叠数量，统一采用耐久方案」；
    /// 同日追记勘正——**双方案并存**：勾玉/魔法箭矢类回归堆叠）：
    /// - **耐久配件**（customParams Durability>0，如兔兔伯爵）：耐久归零不销毁——留在槽位里只是
    ///   不再生效（撤词条+不再格挡）——「除非有特别描述」；重复装备=补充耐久（实例耐久相加封顶、
    ///   同物永不双槽）；耐久减少触发爆炸（兔兔伯爵增强）；持有者采集恢复（RecoverDurability 钩子）；
    /// - **堆叠配件**（customParams EquipStackLimit>0，如勾玉——追记拍板「堆叠上限为3〔指装备槽
    ///   而非手牌〕+每次装备仅装备1个」）：同槽可叠 N 枚、每张物品牌装备 +1 层、每层全效无停效态；
    ///   掠夺按层数拆 N 枚单卡；
    /// - 槽位计数器单字段承载双语义（_accessoryDurability：耐久件=剩余耐久/堆叠件=当前层数），
    ///   统一上限原语=SlotCapOf（两参数互斥取大）、单次安装量=SingleInstallCount（堆叠件恒 1 层）；
    /// - 阵亡装备被击杀方掠夺（BattleResolver 死亡命令发射处收集，耐久不变/堆叠按层拆枚入其手牌）。
    ///
    /// 战场外（大地图探索）如需装备另有系统，勿跨层复用本组件。
    /// </summary>
    public class UnitEquipment : MonoBehaviour, IUnitComponent
    {
        private Unit _owner;

        /// <summary>武器槽（恒 1，docs/05 §5.2——null=未装备）</summary>
        private ItemName? _weapon;

        /// <summary>武器耐久（0=无耐久概念）。**T10 注记（2026-10-26 复审批）**：与配件 _accessoryDurability
        /// 的「耐久/堆叠双语义」同族但**独立字段=刻意平行实现**（武器恒耐久语义、配件堆叠语义分叉——
        /// 合并需动存档/快照/命令三链序列化，等「按源撤销」需求出现再裁，勿提前统一）</summary>
        private int _weaponDurability;

        /// <summary>配件槽（数量按层级——Init 分配；null=空槽位）</summary>
        private readonly List<ItemName?> _accessories = new();

        /// <summary>配件耐久表（与 _accessories 平行；0=空槽/无耐久概念）</summary>
        private readonly List<int> _accessoryDurability = new();

        /// <summary>武器词条修改器（装备时挂；掠夺卸下/跨 0 恢复重挂——决策八十一起武器有卸下路径）</summary>
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

        /// <summary>装备耐久上限（customParams Durability 单源；0=无耐久概念——恒满恒生效，
        /// 补充耐久对无耐久装备恒无效）</summary>
        public static int MaxDurabilityOf(ItemName item)
            => ItemDataOf(item)?.GetInt(nameof(SkillParamKey.Durability), 0) ?? 0;

        /// <summary>装备槽内堆叠上限（决策八十一追记·勾玉回归堆叠方案）：customParams EquipStackLimit
        /// 单源；0=非堆叠配件</summary>
        public static int StackLimitOf(ItemName item)
            => ItemDataOf(item)?.GetInt(nameof(SkillParamKey.EquipStackLimit), 0) ?? 0;

        /// <summary>槽位计数器上限（统一上限原语——双方案件型无关取大）：耐久配件=配置 Durability、
        /// 堆叠配件=EquipStackLimit（两类互斥配置，实际只配其一；客户端域判定/Host 校验共用）</summary>
        public static int SlotCapOf(ItemName item)
        {
            int cap = MaxDurabilityOf(item);
            int stack = StackLimitOf(item);
            return stack > cap ? stack : cap;
        }

        /// <summary>单次安装量（GrantItem 授予/新鲜卡空槽安装的计数器初值）：耐久配件=配置满耐久
        ///（掠夺物例外走 override）、堆叠配件=恒 1 层（「每次装备仅装备1个」——决策八十一追记）</summary>
        public static int SingleInstallCount(ItemName item)
            => StackLimitOf(item) > 0 ? 1 : MaxDurabilityOf(item);

        /// <summary>是否堆叠配件（EquipStackLimit>0——每层全效、无停效态；与耐久配件互斥配置）</summary>
        public static bool IsStackAccessory(ItemName item) => StackLimitOf(item) > 0;

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

        /// <summary>是否装备有任何装备（掠夺快查——武器/配件全空=true 跳过）</summary>
        public bool HasAnyEquipment
        {
            get
            {
                if (_weapon.HasValue) return true;
                foreach (var accessory in _accessories)
                    if (accessory.HasValue) return true;
                return false;
            }
        }

        /// <summary>装备武器（武器槽恒 1，已占用返回 false 不顶替）；词条即时挂 UnitStats。
        /// durabilityOverride≥0=实例耐久安装（掠夺物保留原耐久）；-1=新鲜卡（配置满耐久）；
        /// 堆叠配件（恒配件、理论不到武器槽——防御）：安装计数器恒 1 层</summary>
        public bool EquipWeapon(ItemName weapon, int durabilityOverride = -1)
        {
            if (_weapon.HasValue) return false;
            var data = ItemDataOf(weapon);
            _weapon = weapon;
            if (IsStackAccessory(weapon)) _weaponDurability = 1;
            else _weaponDurability = durabilityOverride >= 0
                ? durabilityOverride
                : (data != null ? data.GetInt(nameof(SkillParamKey.Durability), 0) : 0);
            ApplyEquipStats(data, _weaponMods);
            return true;
        }

        /// <summary>装备配件（首个空槽；无空槽返回 false）——槽号经 out 返回（命令载荷用）。
        /// durabilityOverride≥0=实例耐久安装（掠夺物保留原耐久）；-1=新鲜卡（配置满耐久）；
        /// **堆叠配件恒 1 层起装**（「每次装备仅装备1个」——决策八十一追记；掠夺按层拆枚恒单枚）</summary>
        public bool EquipAccessory(ItemName accessory, out int slotIndex, int durabilityOverride = -1)
        {
            slotIndex = -1;
            for (int i = 0; i < _accessories.Count; i++)
            {
                if (_accessories[i].HasValue) continue;
                _accessories[i] = accessory;
                var data = ItemDataOf(accessory);
                if (IsStackAccessory(accessory)) _accessoryDurability[i] = 1;
                else _accessoryDurability[i] = durabilityOverride >= 0
                    ? durabilityOverride
                    : (data != null ? data.GetInt(nameof(SkillParamKey.Durability), 0) : 0);
                ApplyEquipStats(data, _accessoryMods[i]);
                slotIndex = i;
                return true;
            }
            return false;
        }

        /// <summary>已装备指定配件的槽位耐久（未装备=-1——EquipItemExecutor 补充路径与瞄准域判定用）</summary>
        public int HeldAccessoryDurability(ItemName accessory)
        {
            for (int i = 0; i < _accessories.Count; i++)
                if (_accessories[i] == accessory) return _accessoryDurability[i];
            return -1;
        }

        /// <summary>补充武器槽位计数器（已装备该武器时再次装备——决策八十一「补充耐久，永不双槽」）：
        /// 耐久相加封顶（上限=SlotCapOf 统一原语）；已满=返回 false（客户端已提示、Host 兜底落空）；
        /// 跨 0 恢复（归零停效态→生效态）重挂词条</summary>
        public bool ReplenishWeapon(ItemName weapon, int addDurability, out int durabilityLeft)
        {
            durabilityLeft = 0;
            if (_weapon != weapon) return false;
            int max = SlotCapOf(weapon);
            int applied = Mathf.Max(0, Mathf.Min(addDurability, max - _weaponDurability));
            if (applied <= 0) return false;
            bool crossZero = _weaponDurability <= 0;
            _weaponDurability += applied;
            durabilityLeft = _weaponDurability;
            if (crossZero)
            {
                _weaponMods.Clear();
                ApplyEquipStats(ItemDataOf(weapon), _weaponMods);
            }
            return true;
        }

        /// <summary>补充配件耐久（已装备该配件时再次装备——决策八十一「补充耐久，永不双槽」）：
        /// 耐久相加封顶（上限=MaxDurabilityOf）；未装备/已满=返回 false；跨 0 恢复重挂词条。
        /// out appliedAdd=实际补充量（满则 0）</summary>
        public bool ReplenishAccessory(ItemName accessory, int addDurability, out int slotIndex,
            out int durabilityLeft, out int appliedAdd)
        {
            slotIndex = -1; durabilityLeft = 0; appliedAdd = 0;
            for (int i = 0; i < _accessories.Count; i++)
            {
                if (_accessories[i] != accessory) continue;
                int max = SlotCapOf(accessory); // 统一上限原语：耐久件=Durability/堆叠件=EquipStackLimit
                appliedAdd = Mathf.Max(0, Mathf.Min(addDurability, max - _accessoryDurability[i]));
                if (appliedAdd <= 0) return false;
                bool crossZero = _accessoryDurability[i] <= 0;
                _accessoryDurability[i] += appliedAdd;
                slotIndex = i;
                durabilityLeft = _accessoryDurability[i];
                if (crossZero)
                {
                    _accessoryMods[i].Clear();
                    ApplyEquipStats(ItemDataOf(accessory), _accessoryMods[i]);
                }
                return true;
            }
            return false;
        }

        /// <summary>采集恢复耐久钩子（兔兔伯爵增强「持有者每次采集，会使此装备耐久恢复1」——
        /// 资源点系统落地后由采集事件接线，docs/11 挂账；**恢复≠补充**：跨 0 恢复同样重挂词条）。
        /// 对已装备该物品的武器/配件槽位恢复（同物永不双槽=至多一槽命中）；返回实际恢复量</summary>
        public int RecoverDurability(ItemName item, int amount)
        {
            if (amount <= 0) return 0;
            int recovered = 0;
            if (_weapon == item)
            {
                int applied = Mathf.Max(0, Mathf.Min(amount, MaxDurabilityOf(item) - _weaponDurability));
                if (applied > 0 && ReplenishWeapon(item, applied, out _)) recovered += applied;
            }
            if (ReplenishAccessory(item, amount, out _, out _, out int accessoryApplied))
                recovered += accessoryApplied;
            return recovered;
        }

        /// <summary>采集事件入口（docs/47 资源点批，2026-10-27 接线——决策八十一挂账义务销案）：
        /// 采集者每次采集（站立/途经两路径统一调本口），对**全部已装备耐久件**各恢复 1——
        /// 数据驱动单源（描述文案绑定兔兔伯爵，现役耐久件仅此一件；未来「采集恢复」型装备零代码）。
        /// 耐久件判定=MaxDurabilityOf>0（纯词条武器恒满/堆叠件勾玉=层语义，均不吃采集恢复）；
        /// 恢复经 RecoverDurability 单口（跨 0 恢复重挂词条同款）</summary>
        public void OnCollect()
        {
            if (_weapon.HasValue && MaxDurabilityOf(_weapon.Value) > 0)
                RecoverDurability(_weapon.Value, 1);
            for (int i = 0; i < _accessories.Count; i++)
            {
                var item = _accessories[i];
                if (!item.HasValue || MaxDurabilityOf(item.Value) <= 0) continue;
                RecoverDurability(item.Value, 1);
            }
        }

        /// <summary>指定配件槽耐久（槽号越界=0）</summary>
        public int GetAccessoryDurability(int slotIndex)
            => slotIndex >= 0 && slotIndex < _accessoryDurability.Count ? _accessoryDurability[slotIndex] : 0;

        /// <summary>卸下武器（空槽返回 false）；撤词条（掠夺路径——复活单位不残留幻影词条）</summary>
        public bool UnequipWeapon()
        {
            if (!_weapon.HasValue) return false;
            _weapon = null;
            _weaponDurability = 0;
            RemoveWeaponMods();
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

        // ==================== 伤害抵挡（兔兔伯爵；BattleResolver.ApplyEffects 消费单出口） ====================

        /// <summary>格挡结果（TryConsumeBlock 出参载体——槽位/耐久供命令层发 EquipItem 同槽重发刷角标）</summary>
        public struct BlockResult
        {
            public int Item;          // 格挡物品（ItemName 枚举值）
            public int SlotKind;      // BattleCommand.EquipKindWeapon/EquipKindAccessory
            public int SlotIndex;     // 配件槽号（武器=0）
            public int DurabilityLeft; // 消耗后剩余耐久
        }

        /// <summary>格挡查询+消耗：incomingDamage ≥ 某已装备配件的 BlockThreshold 且该配件耐久 &gt;0 →
        /// 完全抵挡——耐久 -1。返回 false=无可格挡。**尸体不调用**（HP 恒 0 无可保护，鞭尸白烧耐久
        /// ——调用方守卫）。多枚可格挡配件按槽序取首个（确定性）。
        /// **耐久归零不销毁（决策八十一）**：留在槽位里只是不再生效——撤该槽词条（停效），
        /// 槽位继续被占用（恢复须再装备同物品补充耐久）</summary>
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
                };
                if (_accessoryDurability[i] <= 0)
                    RemoveSlotMods(i); // 归零停效：撤词条——槽位保留（决策八十一「不会销毁，仍然留在槽位里」）
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

        /// <summary>撤指定配件槽全部词条修改器（耐久归零停效/耐久耗尽卸下）</summary>
        private void RemoveSlotMods(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _accessoryMods.Count) return;
            var stats = _owner != null ? _owner.GetUnitComponent<UnitStats>() : null;
            if (stats == null) return;
            foreach (var mod in _accessoryMods[slotIndex])
                stats.RemoveModifier(mod);
            _accessoryMods[slotIndex].Clear();
        }

        /// <summary>撤武器全部词条修改器（掠夺卸下路径——阵亡掠夺后单位不再持有武器，
        /// 复苏也不残留幻影词条）</summary>
        private void RemoveWeaponMods()
        {
            var stats = _owner != null ? _owner.GetUnitComponent<UnitStats>() : null;
            if (stats == null) return;
            foreach (var mod in _weaponMods)
                stats.RemoveModifier(mod);
            _weaponMods.Clear();
        }
    }
}
