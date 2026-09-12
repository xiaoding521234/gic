using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Framework;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
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

        public override string ToString()
        {
            return $"[ActionData {playerId}/{unitId} {actionType} turn={turnNumber}" +
                   (actionType == ActionType.Move ? $" dir={direction} mag={moveMagnitude}" : "") +
                   (actionType == ActionType.Skill ? $" skill={skillIndex} target={targetUnitId}" : "") + "]";
        }
    }
}
