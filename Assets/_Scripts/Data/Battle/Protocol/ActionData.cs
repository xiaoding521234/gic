using System;
using UnityEngine;
namespace GIC.Data
{


    /// <summary>
    /// 行动类型（选择阶段上交；B1 实现 Move/Skill/Pass，其余 B6 补全）
    /// </summary>
    public enum ActionType
    {
        [InspectorName("移动")]
        Move = 0,

        [InspectorName("战技")]
        Skill = 1,

        [InspectorName("爆发")]
        Burst = 2,

        [InspectorName("出战角色")]
        DeployUnit = 3,

        [InspectorName("出战建筑")]
        DeployBuilding = 4,

        [InspectorName("提升命座")]
        UpgradeConstellation = 5,

        [InspectorName("购买卡牌")]
        BuyCard = 6,

        [InspectorName("使用物品")]
        UseItem = 7,

        [InspectorName("装备物品")]
        EquipItem = 8,

        [InspectorName("势力特殊技能")]
        FactionSkill = 9,

        [InspectorName("开启核心宝箱")]
        OpenChest = 10,

        [InspectorName("空过")]
        Pass = 11,
    }

    /// <summary>
    /// 行动选择数据（选择阶段上交；字段按类型部分有效）
    /// </summary>
    [Serializable]
    public class ActionData
    {
        [Header("归属")]
        public string playerId;
        public string unitId;
        public ActionType actionType;

        [Header("回合")]
        public int turnNumber;

        [Header("移动参数")]
        public Direction2D direction = Direction2D.Right;
        public int moveMagnitude;

        [Header("技能参数")]
        public int skillIndex;
        public string targetUnitId;
        public BattleCell targetCell;

        [Header("部署参数（B6c：DeployUnit 有效）")]
        /// <summary>出战角色（UnitName 枚举值）</summary>
        public int deployUnitName;

        /// <summary>部署落点</summary>
        public BattleCell deployCell;

        [Header("使用物品参数（UseItem 有效，2026-10-16 首个可打出物品批）")]
        /// <summary>使用的物品（ItemName 枚举值；消耗手牌 1 张；目标饮用单位复用 targetUnitId——
        /// 玩家级行动 unitId 留空〔同 DeployUnit 口径〕）</summary>
        public int useItemId;

        [Header("购买参数（BuyCard 有效，决策八十六批E，docs/active/45 §7）")]
        /// <summary>购买的卡牌类型（CardType 枚举值：Unit=1/Item=0——单位卡/物品卡统一承载）</summary>
        public int buyCardType;

        /// <summary>购买的卡牌内容（UnitName 或 ItemName 枚举值）</summary>
        public int buyCardValue;

        [Header("装备物品参数（EquipItem 有效，2026-10-18 装备批）")]
        /// <summary>装备的物品（ItemName 枚举值——subType 须为 Weapon/Accessory；装备即消耗手牌 1 张
        /// docs/01 §卡牌性质；目标装备单位复用 targetUnitId——玩家级行动 unitId 留空〔同 UseItem 口径，
        /// 精确指定目标=打出食物同款交互〕）</summary>
        public int equipItemId;

        /// <summary>装备的手牌条目实例耐久（决策八十一「舍弃堆叠数量，统一采用耐久方案」：装备条目按
        /// 耐久分实例——客户端从拖拽的条目填充，Host 按此实例匹配消耗与安装/补充。新鲜卡=配置满值、
        /// 掠夺物=保留耐久。默认 0 序列化兼容）</summary>
        public int equipDurability;

        /// <summary>玩家直接操控标记（2026-10-12 拍板：伙伴技能解锁——玩家通过配额直接操控伙伴
        /// 行动时标记 true（真人/AI 提交路径同标，AI-1 批起含势力技能号令），ResourceGate 体力换算
        /// 据此减免 5（可减免为负数=获得体力）；SkillExecutor 对伙伴施法者的延奏/契约号令发津贴 +5
        /// （拍板口径「延奏号令 0→−5」）；AI 伙伴自主行动=false 不减免。默认 false 序列化兼容旧存档</summary>
        public bool isPlayerDirectControl;

        public override string ToString()
        {
            return $"[ActionData {playerId}/{unitId} {actionType} turn={turnNumber}" +
                   (actionType == ActionType.Move ? $" dir={direction} mag={moveMagnitude}" : "") +
                   (actionType == ActionType.Skill ? $" skill={skillIndex} target={targetUnitId}" : "") +
                   (actionType == ActionType.DeployUnit ? $" unit={(UnitName)deployUnitName} at={deployCell}" : "") + "]";
        }
    }
}
