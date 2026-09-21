using System;
using System.Collections.Generic;
using UnityEngine;
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

        [Header("命中点（Damage 投射物有效；千分定点连续格心坐标×1000，docs/active/22 §11）")]
        public int hitX;
        public int hitY;

        [Header("Buff 载荷（ApplyBuff/RemoveBuff 有效）")]
        public int buffType;
        public int buffLevel;
        public int buffTurns;

        [Header("特效子类型（Effect 命令 metadata；随用随加）")]
        public const int EffectKindProjectileVanish = 1;

        [Header("移动载荷（Move 命令 metadata；2026-09-21）")]
        /// <summary>被挡标记：移动尝试进入 direction 方向的下一格失败（逻辑已停在被挡格前），
        /// 客户端播"撞墙弹回"表现——探出后弹回，逻辑位置不变</summary>
        public const int MoveBlocked = 1;

        public static BattleCommand Move(string unitId, int sliceIndex, int indexInSlice, List<BattleCell> path,
            int blocked = 0, int blockedDirection = 0)
        {
            return new BattleCommand
            {
                type = BattleCommandType.Move,
                actorUnitId = unitId,
                sliceIndex = sliceIndex,
                indexInSlice = indexInSlice,
                path = path,
                cell = path.Count > 0 ? path[path.Count - 1] : BattleCell.zero,
                metadata = blocked,
                direction = blockedDirection,
            };
        }

        /// <summary>Damage 命令工厂。metadata=元素；direction=投放形态（0=瞬发直击/1=直线投射物，复用字段）；
        /// cell=投射物发射格（Delivery≠0 时有效）；hitX/hitY=命中点千分定点连续格心坐标
        /// （Delivery=1 有效——Host 接触判定得出，客户端按此播放弹着点，勿自行推算）</summary>
        public static BattleCommand Damage(string actorUnitId, string targetUnitId, int sliceIndex, int indexInSlice,
            int amount, int metadata, int delivery = 0, BattleCell fromCell = default, int hitX = 0, int hitY = 0)
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
                direction = delivery,
                cell = fromCell,
                hitX = hitX,
                hitY = hitY,
            };
        }

        /// <summary>特效命令工厂（Effect）。metadata=特效子类型（EffectKindProjectileVanish=投射物消散：
        /// cell=发射格、direction=飞行方向 Direction2D、value=最大飞行格数——客户端播放飞至尽头消散）</summary>
        public static BattleCommand Effect(string actorUnitId, int sliceIndex, int indexInSlice,
            int effectKind, int direction, BattleCell fromCell, int intValue)
        {
            return new BattleCommand
            {
                type = BattleCommandType.Effect,
                actorUnitId = actorUnitId,
                sliceIndex = sliceIndex,
                indexInSlice = indexInSlice,
                metadata = effectKind,
                direction = direction,
                cell = fromCell,
                value = intValue,
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

        public static BattleCommand ApplyBuff(string actorUnitId, string targetUnitId, int sliceIndex, int indexInSlice,
            int buffType, int buffLevel, int buffTurns)
        {
            return new BattleCommand
            {
                type = BattleCommandType.ApplyBuff,
                actorUnitId = actorUnitId,
                targetUnitId = targetUnitId,
                sliceIndex = sliceIndex,
                indexInSlice = indexInSlice,
                buffType = buffType,
                buffLevel = buffLevel,
                buffTurns = buffTurns,
            };
        }

        public static BattleCommand RemoveBuff(string actorUnitId, string targetUnitId, int sliceIndex, int indexInSlice, int buffType)
        {
            return new BattleCommand
            {
                type = BattleCommandType.RemoveBuff,
                actorUnitId = actorUnitId,
                targetUnitId = targetUnitId,
                sliceIndex = sliceIndex,
                indexInSlice = indexInSlice,
                buffType = buffType,
            };
        }
    }
}
