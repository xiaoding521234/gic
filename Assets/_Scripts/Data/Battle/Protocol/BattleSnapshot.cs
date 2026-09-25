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
        public int isCorpse;
        public int isFrozen;

        /// <summary>元能当前值/上限（B6a；上限=UnitConfig baseEnergy，爆发门槛=技能条目 EnergyCost）</summary>
        public int energy;
        public int maxEnergy;
        public int volume;
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
    /// </summary>
    [Serializable]
    public class HandCard
    {
        public int cardType;
        public int value;
        public int count;

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
