using System;
using System.Collections.Generic;
namespace GIC.Data
{


    /// <summary>
    /// 单位状态快照条目（选择阶段头全量）
    /// </summary>
    [Serializable]
    public class UnitState
    {
        public string unitId;
        public string unitName;
        public string playerId;
        public int team;
        public BattleCell position;
        public int hp;
        public int maxHp;
        public int attack;
        public int defense;
        public int attackSpeed;

        /// <summary>移速（2026-09-23：移动距离=10%×移速换算的基准值——HUD 瞄准与 Host 同源）</summary>
        public int moveSpeed;
        public int dyedElement;

        /// <summary>染色层数（docs/06 §6.2 多层附着，决策七十四铺路；旧回放/旧端缺省 0——消费方按
        /// 「dyedElement≠Physical 且层数≤0 → 归一为 1」兼容）</summary>
        public int dyedLayers;
        public int isCorpse;
        public int isFrozen;

        /// <summary>命座等级（B8 批 2026-09-30，docs/09：0~3——3★+同名重复出战升命；命座被动由 Host
        /// ConstellationApplier 结算，本字段=展示/升命门控/B7 联机同源真源）</summary>
        public int constellation;

        /// <summary>元能当前值/上限（B6a；上限=UnitConfig baseEnergy，爆发门槛=技能条目 EnergyCost）</summary>
        public int energy;
        public int maxEnergy;

        /// <summary>理智（2026-09-30 歌声之环批：歌声之环 tick 恢复结算携带；基值=UnitConfig baseSanity 回落 50、
        /// 钳制 -300~300（UnitStats RangedInt）；玩法消费方随未来理智机制批接线）</summary>
        public int sanity;
        public int volume;

        /// <summary>受击圆柱直径（格=世界单位；0=未指定回落全局 0.42——BattleMetrics.CylinderDiameterOf 单出口解析）。
        /// 协议核心批（2026-09-29）：投射物接触判定/命中预判/底座盘视觉/选中弧光贴紧四消费方同源；
        /// Host BuildUnitState 从 UnitData.受击圆柱直径 填充，B7 联机随快照自动携带</summary>
        public float cylinderDiameter;

        /// <summary>操控层级（UnitTier 枚举值；Host BuildUnitState 从 BattleHeuristics.TierOf 填充——
        /// 试招沙盒的层级覆盖 Unit.TierOverrideStars 经此进快照，客户端技能盘门控与 Host 同源
        /// （2026-10-05「开一把试招」：覆盖不进快照则客户端按原星判 3★=伙伴档，战技被门控置灰拦截）；
        /// 0=未填（旧快照/异常）——消费方回落 UnitConfig 星级换算）</summary>
        public int tier;

        /// <summary>探索视野（2026-10-17 迷雾批追拍六「1命视野真源化」）：Host 从 UnitStats.VisionRange
        /// 终值填充（=config 基值+命座 StatBoost——安柏 1命 +C1Vision 随此进快照，docs/active/44 §9-③）；
        /// 客户端 BattleFog 首选直读（零配置解析链）。0=未填（旧端/手工构造态）——消费方回落 config 基值链</summary>
        public int exploreVision;

        /// <summary>武器（装备批 2026-10-18）：ItemName 枚举值，0=空槽（武器槽恒 1，docs/05 §5.2）。
        /// Host BuildUnitState 从 UnitEquipment 填充；客户端=血条右侧装备图标行（首个图标）+瞄准域槽位预判</summary>
        public int weaponItem;

        /// <summary>武器耐久（0=无耐久概念——装备批）</summary>
        public int weaponDurability;

        /// <summary>配件槽位表（按槽序平铺，长度=该单位配件槽上限〔眷属 1/伙伴 2/魔神 3〕；0=空槽）——
        /// 客户端图标行按槽序渲染、瞄准域「有空槽」预判读零位存在性</summary>
        public List<int> accessoryItems = new List<int>();

        /// <summary>配件耐久表（与 accessoryItems 平行；0=空槽/无耐久概念）</summary>
        public List<int> accessoryDurability = new List<int>();

        public List<BuffState> buffs = new List<BuffState>();
    }

    /// <summary>
    /// 玩家资源快照条目（摩拉/体力/手牌/牌库；B1 只携带初始值不结算）
    /// </summary>
    [Serializable]
    public class PlayerResourceState
    {
        public string playerId;
        public int mora;
        public int stamina;
        public int handCardCount;
        public int deckCardCount;

        /// <summary>手牌条目（卡+持有数量；2026-09-25 拍板：初始手牌=初始卡组按顺序获得+开局送
        /// 200 摩拉+60 体力，编没编货币卡都送。物品/角色条目 count=局内真源；
        /// 货币条目 count=资源池镜像（快照序列化时映射））</summary>
        public List<HandCard> handCards = new List<HandCard>();
    }

    /// <summary>
    /// 手牌条目（2026-09-25 拍板「获得卡片=手牌构建唯一入口」：卡=条目+持有数量一等属性——
    /// 获得=有则加数量/无则加卡；失去=减数量/减至零移除卡，对称）。
    /// 角色卡恒 1（卡不消耗，docs/01）；普通物品卡=备战数起（使用/装备后消耗）；
    /// 货币物品牌（摩拉/体力）持有数量=玩家资源池（PlayerResourceState.mora/stamina 即该牌堆张数，
    /// 资源池是牌堆的视图非独立系统）。
    /// **装备实例耐久（决策八十一「舍弃堆叠数量，统一采用耐久方案」）**：装备类物品（Weapon/Accessory）
    /// 条目按耐久分实例——同物异耐久=不同条目（掠夺物耐久不变保留、新鲜卡=配置 Durability 满值），
    /// 条目身份=(cardType, value, durability)；非装备物品恒 0 不参与匹配。旧数据缺省 0=新鲜/非装备语义
    /// （快速迭代期存档每次重建，无迁移负担）。
    /// </summary>
    [Serializable]
    public class HandCard
    {
        public int cardType;
        public int value;
        public int count;

        /// <summary>装备实例耐久（Weapon/Accessory 条目=实例身份之一；非装备恒 0）</summary>
        public int durability;

        public HandCard() { }

        public HandCard(CardId id, int count)
        {
            cardType = (int)id.cardType;
            value = id.value;
            this.count = count;
        }

        public CardId AsCardId() => new CardId((CardType)cardType, value);
        public ItemName AsItemName() => (ItemName)value;
        public UnitName AsUnitName() => (UnitName)value;
        public bool IsUnit => (CardType)cardType == CardType.Unit;

        /// <summary>货币物品牌（摩拉/体力——数量走资源池的牌堆）</summary>
        public bool IsCurrency => (CardType)cardType == CardType.Item
            && (value == (int)ItemName.Mora || value == (int)ItemName.Stamina);
    }

    /// <summary>
    /// 战场全量快照（每选择阶段头广播一次；断线重连 = 重发快照 + 跳过演算）
    /// </summary>
    [Serializable]
    public class BattleSnapshot
    {
        public int turnNumber;
        public List<UnitState> units = new List<UnitState>();
        public List<PlayerResourceState> resources = new List<PlayerResourceState>();
    }
}
