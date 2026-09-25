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

        [InspectorName("技能施放")]
        SkillCast = 14,
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

        [Header("发射时刻（Damage 投射物/Effect 消散有效；毫秒，相对片播放起点——时轮 B-S1 前摇偏移）")]
        public int launchMs;

        [Header("反应标记（Damage 命令有效；0=无反应——本次命中触发的元素反应子类型，供客户端伤害数字带反应名）")]
        public int reactionKind;

        [Header("Buff 载荷（ApplyBuff/RemoveBuff 有效）")]
        public int buffType;
        public int buffLevel;
        public int buffTurns;

        [Header("特效子类型（Effect 命令 metadata；随用随加）")]
        public const int EffectKindProjectileVanish = 1;

        [Header("反应子类型（Reaction 命令 metadata；docs/06 元素反应）")]
        public const int ReactionKindMelt = 1;      // 融化（火+冰）
        public const int ReactionKindFreeze = 2;     // 冻结（水+冰）
        public const int ReactionKindVaporize = 3;  // 蒸发（火+水；2026-09-22 补全，docs/06 §反应表）

        [Header("属性子类型（StatChange 命令 metadata）")]
        public const int StatKindEnergy = 1; // 元能（value=变化量，正获取负消耗；客户端 UnitView 缓存即时增量，B6a）
        public const int StatKindMora = 2;   // 摩拉（value=变化量；actorUnitId=归属玩家。B6c 部署扣费直产；B6d 回合结束发放直产+客户端 HUD 消费）
        public const int StatKindStamina = 3; // 体力（value=变化量；actorUnitId=归属玩家。B6d：配额行动消耗走 StaminaEffect 效应产出+回合结束发放直产）

        [Header("召唤载荷（Summon 命令有效；B6c 部署）")]
        /// <summary>新登场的单位全量状态（客户端建 view 用；与快照 UnitState 同构）</summary>
        public UnitState summonUnit;

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
        /// （Delivery=1 有效——Host 接触判定得出，客户端按此播放弹着点，勿自行推算）；
        /// reactionKind=本次命中触发的元素反应（0=无；增伤反应时伤害数字带反应名）；
        /// launchMs=发射时刻毫秒（时轮 B-S1——客户端投射物延迟起飞/瞬发段伤害数字节拍；合并键含此值=逐发不并）</summary>
        public static BattleCommand Damage(string actorUnitId, string targetUnitId, int sliceIndex, int indexInSlice,
            int amount, int metadata, int delivery = 0, BattleCell fromCell = default, int hitX = 0, int hitY = 0,
            int reactionKind = 0, int launchMs = 0)
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
                reactionKind = reactionKind,
                launchMs = launchMs,
            };
        }

        /// <summary>特效命令工厂（Effect）。metadata=特效子类型（EffectKindProjectileVanish=投射物消散：
        /// cell=发射格、direction=飞行方向 Direction2D、value=最大飞行格数——客户端播放飞至尽头消散）；
        /// launchMs=发射时刻毫秒（时轮 B-S1）</summary>
        public static BattleCommand Effect(string actorUnitId, int sliceIndex, int indexInSlice,
            int effectKind, int direction, BattleCell fromCell, int intValue, int launchMs = 0)
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
                launchMs = launchMs,
            };
        }

        /// <summary>技能施放命令工厂（时轮 B-S1）：片内每个通过门槛的技能行动各产一条、
        /// 段内最前发射——客户端的时轮演出起点事件（按 skillID 加载 SkillTimelineAsset 播
        /// 动作/音效/特效轨；素材接线=B-S3）。value=skillID（SkillName 枚举值）、
        /// direction=瞄准方向、cell=施放者片初位置（朝向参考）。无表现素材时不产出视觉，
        /// 投射物延迟起飞由 Damage/Effect 命令的 launchMs 承载。</summary>
        public static BattleCommand SkillCast(string unitId, int sliceIndex, int indexInSlice,
            int skillId, int direction, BattleCell fromCell)
        {
            return new BattleCommand
            {
                type = BattleCommandType.SkillCast,
                actorUnitId = unitId,
                sliceIndex = sliceIndex,
                indexInSlice = indexInSlice,
                value = skillId,
                direction = direction,
                cell = fromCell,
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

        /// <summary>元素附着命令工厂。metadata=附着元素（覆盖语义=消耗被反应附着，docs/06）；
        /// 客户端即时刷新目标附着显示（此前靠下回合快照自愈）</summary>
        public static BattleCommand ElementAttach(string actorUnitId, string targetUnitId, int sliceIndex, int indexInSlice,
            int element)
        {
            return new BattleCommand
            {
                type = BattleCommandType.ElementAttach,
                actorUnitId = actorUnitId,
                targetUnitId = targetUnitId,
                sliceIndex = sliceIndex,
                indexInSlice = indexInSlice,
                metadata = element,
            };
        }

        /// <summary>元素反应命令工厂。metadata=反应子类型（ReactionKindMelt/Freeze）；value=反应级别。
        /// 融化的伤害并入已由 Damage 命令承载，本命令只播"反应发生"事件；冻结时客户端即时同步立牌冰色
        /// （此前冰色靠下回合快照，反应当回合不可见）</summary>
        public static BattleCommand Reaction(string actorUnitId, string targetUnitId, int sliceIndex, int indexInSlice,
            int reactionType, int level)
        {
            return new BattleCommand
            {
                type = BattleCommandType.Reaction,
                actorUnitId = actorUnitId,
                targetUnitId = targetUnitId,
                sliceIndex = sliceIndex,
                indexInSlice = indexInSlice,
                metadata = reactionType,
                value = level,
            };
        }

        /// <summary>属性变化命令工厂。metadata=属性子类型（StatKindEnergy/StatKindMora/StatKindStamina）；
        /// value=变化量（正=获取/负=消耗）。元能：unitId=单位，客户端 UnitView 缓存即时增量；
        /// 摩拉/体力：unitId=归属玩家（物品牌持有者），客户端 HUD 资源显示即时增量+快照权威刷新（B6d）</summary>
        public static BattleCommand StatChange(string unitId, int sliceIndex, int indexInSlice,
            int statKind, int delta)
        {
            return new BattleCommand
            {
                type = BattleCommandType.StatChange,
                actorUnitId = unitId,
                targetUnitId = unitId,
                sliceIndex = sliceIndex,
                indexInSlice = indexInSlice,
                metadata = statKind,
                value = delta,
            };
        }

        /// <summary>召唤命令工厂（B6c 部署）：summonUnit=新登场单位全量状态（客户端建 view）；
        /// actorUnitId=部署玩家（摩拉归属方）。部署成功时与 StatChange(Mora) 成对出现</summary>
        public static BattleCommand Summon(string deployPlayerId, int sliceIndex, int indexInSlice,
            UnitState summonedUnit)
        {
            return new BattleCommand
            {
                type = BattleCommandType.Summon,
                actorUnitId = deployPlayerId,
                targetUnitId = summonedUnit.unitId,
                sliceIndex = sliceIndex,
                indexInSlice = indexInSlice,
                summonUnit = summonedUnit,
            };
        }
    }
}
