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
    /// 战斗命令类型（union 风格；命令粒度 = 一次原子视觉事件）
    /// </summary>
    public enum BattleCommandType
    {
        [InspectorName("移动")]
        Move = 0,

        [InspectorName("伤害")]
        Damage = 1,

        [InspectorName("死亡")]
        Death = 2,

        [InspectorName("治疗")]
        Heal = 3,

        [InspectorName("附加Buff")]
        ApplyBuff = 4,

        [InspectorName("移除Buff")]
        RemoveBuff = 5,

        [InspectorName("元素附着")]
        ElementAttach = 6,

        [InspectorName("元素反应")]
        Reaction = 7,

        [InspectorName("属性变化")]
        StatChange = 8,

        [InspectorName("召唤")]
        Summon = 9,

        [InspectorName("特效")]
        Effect = 10,

        [InspectorName("界面")]
        UI = 11,

        [InspectorName("硬停")]
        Pause = 12,

        [InspectorName("恢复")]
        Resume = 13,
    }

    /// <summary>
    /// 单条战斗命令（union 风格平铺 payload，按 type 部分有效）
    /// </summary>
    [Serializable]
    public class BattleCommand
    {
        [Header("标识")]
        public BattleCommandType type;
        public string actorUnitId;
        public string targetUnitId;

        [Header("定位")]
        public int sliceIndex;
        public int indexInSlice;

        [Header("可见性（迷雾预留，切片期恒全可见）")]
        public int visibilityScope;

        [Header("载荷（按类型部分有效）")]
        public int value;
        public int metadata;
        public int direction;
        public BattleCell cell;
        public List<BattleCell> path = new List<BattleCell>();

        public static BattleCommand Move(string unitId, int sliceIndex, int indexInSlice, List<BattleCell> path)
        {
            return new BattleCommand
            {
                type = BattleCommandType.Move,
                actorUnitId = unitId,
                sliceIndex = sliceIndex,
                indexInSlice = indexInSlice,
                path = path,
                cell = path.Count > 0 ? path[path.Count - 1] : BattleCell.zero,
            };
        }

        public static BattleCommand Damage(string actorUnitId, string targetUnitId, int sliceIndex, int indexInSlice, int amount, int metadata)
        {
            return new BattleCommand
            {
                type = BattleCommandType.Damage,
                actorUnitId = actorUnitId,
                targetUnitId = targetUnitId,
                sliceIndex = sliceIndex,
                indexInSlice = indexInSlice,
                value = amount,
                metadata = metadata,
            };
        }

        public static BattleCommand Death(string unitId, int sliceIndex, int indexInSlice)
        {
            return new BattleCommand
            {
                type = BattleCommandType.Death,
                actorUnitId = unitId,
                targetUnitId = unitId,
                sliceIndex = sliceIndex,
                indexInSlice = indexInSlice,
            };
        }

        public static BattleCommand Heal(string actorUnitId, string targetUnitId, int sliceIndex, int indexInSlice, int amount)
        {
            return new BattleCommand
            {
                type = BattleCommandType.Heal,
                actorUnitId = actorUnitId,
                targetUnitId = targetUnitId,
                sliceIndex = sliceIndex,
                indexInSlice = indexInSlice,
                value = amount,
            };
        }
    }
}
