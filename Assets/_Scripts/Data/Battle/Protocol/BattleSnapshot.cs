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

        /// <summary>手牌卡列表（2026-09-22 拍板：初始手牌=完整当前卡组投影，含物品卡；
        /// 卡不消耗留手牌，可重复出战。物品卡使用/装备链后续批次，本字段仅展示）</summary>
        public List<CardId> handCards = new List<CardId>();
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
