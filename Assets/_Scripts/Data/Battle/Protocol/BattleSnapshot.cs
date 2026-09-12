using System;
using System.Collections.Generic;
using GIC.Framework;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
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
        public int volume;
        public List<string> buffs = new List<string>();
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
