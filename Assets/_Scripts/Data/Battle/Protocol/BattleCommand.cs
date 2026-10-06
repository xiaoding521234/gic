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

        [InspectorName("物品消耗")]
        ItemConsume = 15,

        [InspectorName("复苏")]
        Revive = 16,

        [InspectorName("命座提升")]
        UpgradeConstellation = 17,
    }

    /// <summary>
    /// 单条战斗命令（union 风格平铺 payload，按 type 部分有效）
    /// </summary>
    /// <remarks>
    /// 字段有效性矩阵（复用字段总表——新增命令类型/新增复用语义时同步更新此表防误用；
    /// 返修先例=launchMs 双语义、direction 复用投放形态，2026-09-27 复审批1 收口）：
    /// type            | value    | metadata         | direction        | cell      | path | hitX/Y | launchMs              | buff 载荷      | summonUnit
    /// ----------------|----------|------------------|------------------|-----------|------|--------|-----------------------|----------------|-----------
    /// Move            | —        | MoveBlocked 标记 | 移动方向（恒填；被挡=被挡方向。2026-09-29 前仅被挡填——拍板③朝向镜像据此翻立牌，成功移动传 0 曾把朝向重置回右） | 终点格    | 路径 | —      | —                     | —              | —
    /// Damage          | 伤害量   | 元素             | 投放形态 delivery | 发射格    | —    | 命中点 | 发射时刻（投射物前摇） | —              | —
    /// Heal            | 治疗量   | —                | —                | —         | —    | —      | 应用时刻（命中类治疗） | —              | —
    /// Death           | —        | —                | —                | —         | —    | —      | —                     | —              | —
    /// ApplyBuff       | —        | 来源技能ID（0=无——客户端 Buff 图标按来源技能解析） | — | — | — | — | — | 类型/级别/回合 | —
    /// RemoveBuff      | —        | —                | —                | —         | —    | —      | —                     | buffType       | —
    /// ElementAttach   | —        | 附着元素         | —                | —         | —    | —      | —                     | —              | —
    /// Reaction        | 反应级别 | 反应子类型       | —                | —         | —    | —      | —                     | —              | —
    /// StatChange      | 变化量   | 属性子类型       | —                | —         | —    | —      | 应用时刻（元能/命中） | —              | —
    /// Summon          | —        | —                | —                | —         | —    | —      | —                     | —              | 新单位全量态
    /// Effect          | 特效值   | 特效子类型       | 飞行方向         | 发射格    | —    | —      | 发射时刻              | —              | —
    /// SkillCast       | skillID  | —                | 瞄准方向         | 施放者位置| —    | —      | —                     | —              | —
    /// ItemConsume     | 消耗数量 | 物品名 ItemName  | —                | —         | —    | —      | —                     | —              | —（targetUnitId=玩家）
    /// Revive          | 复苏治疗量 | —              | —                | —         | —    | —      | —                     | —              | —（B-3 ② 复苏+治疗单命令）
    /// UpgradeConstellation | 新命座层 | —            | —                | —         | —    | —      | —                     | —              | —（B8 命座批；targetUnitId=升命单位）
    /// Pause/Resume/UI | —        | —                | —                | —         | —    | —      | —                     | —              | —
    /// </remarks>
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

        [Header("发射时刻（Damage 投射物/Effect 消散=发射延迟；StatChange(元能)·Heal=应用时刻——命中时才给，2026-09-25；毫秒，相对片播放起点；0=立即）")]
        public int launchMs;

        [Header("反应标记（Damage 命令=元素反应子类型，0=无反应——本次命中触发的反应，客户端伤害数字带反应名；Effect(投射物消散)/SkillCast(技能施放)=投射物/技能元素 ElementType——箭矢/箭雨元素色染色单源，Host 按 Damage.metadata 同口径下发）")]
        public int reactionKind;

        [Header("暴击标记（Damage 命令；1=本次命中暴击——Host 幸运 roll 结论〔暴击率=幸运/100、效果=理智乘区〕，客户端数字放大；0=未暴击。2026-10-02 幸运暴击批）")]
        public int crit;

        [Header("箭矢视觉高度（Damage 投射物/Effect 消散有效；千分=格面起算高度×1000；0=客户端回落 BattleMetrics.ArrowFlightHeight 默认——纯视觉参数对齐动画松弦位，判定圆柱与世界高度无关。2026-10-05）")]
        public int arrowHeightY;

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
        public const int StatKindSanity = 4; // 理智（value=变化量；2026-09-30 歌声之环批：Buff tick 恢复走 SanityEffect 效应产出+客户端 UnitView 缓存；UnitState.sanity 快照携带）

        [Header("治疗子类型（Heal 命令 metadata；2026-10-01 拍板 B——吸血自疗带名前缀）")]
        public const int HealKindLifesteal = 1; // 吸血（客户端弹「吸血 +N」名前缀；普通治疗仍裸 +N）

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
        /// reactionKind=本次命中触发的元素反应（0=无；反应命中时伤害数字带反应名——三反应全带，2026-10-01 三次拍板「反应名都应该加上」）；
        /// launchMs=发射时刻毫秒（时轮 B-S1——客户端投射物延迟起飞/瞬发段伤害数字节拍；合并键含此值=逐发不并）；
        /// crit=暴击标记（0/1——幸运 roll 结论，客户端暴击数字放大，2026-10-02 幸运暴击批）</summary>
        public static BattleCommand Damage(string actorUnitId, string targetUnitId, int sliceIndex, int indexInSlice,
            int amount, int metadata, int delivery = 0, BattleCell fromCell = default, int hitX = 0, int hitY = 0,
            int reactionKind = 0, int launchMs = 0, int crit = 0)
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
                crit = crit,
            };
        }

        /// <summary>特效命令工厂（Effect）。metadata=特效子类型（EffectKindProjectileVanish=投射物消散：
        /// cell=发射格、direction=飞行方向 Direction2D、value=最大飞行格数——客户端播放飞至尽头消散；
        /// hitX/hitY=消散点千分定点、launchMs=发射时刻毫秒（时轮 B-S1）、reactionKind=投射物元素
        /// （箭矢染色单源，与 Damage.metadata 同口径）——三者由调用方回填）</summary>
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
        /// 段内最前发射——客户端的时轮演出起点事件（按 skillID 加载时轮资产播
        /// 动作/音效/特效轨；特效轨首个消费方=arrow_rain 箭雨天降，2026-09-28）。value=skillID（SkillName 枚举值）、
        /// direction=瞄准方向、cell=施放者片初位置（朝向参考）；reactionKind=技能元素
        /// （箭雨染色单源=ResolveProjectileElement，移动施放不填=物理）。
        /// 前摇期投射物视觉由 Damage/Effect 命令的 launchMs 承载。</summary>
        public static BattleCommand SkillCast(string unitId, int sliceIndex, int indexInSlice,
            int skillId, int direction, BattleCell fromCell, int element = 0)
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
                reactionKind = element,
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

        /// <summary>复苏（B-3 ②，芭芭拉闪耀奇迹）：单命令=解灰+复活血量（客户端 SetCorpseVisual(false)
        /// + 弹 +N 治疗数字+血量增量——复苏与治疗原子化无中间态）</summary>
        public static BattleCommand Revive(string actorUnitId, string targetUnitId, int sliceIndex, int indexInSlice,
            int healAmount)
        {
            return new BattleCommand
            {
                type = BattleCommandType.Revive,
                actorUnitId = actorUnitId,
                targetUnitId = targetUnitId,
                sliceIndex = sliceIndex,
                indexInSlice = indexInSlice,
                value = healAmount,
            };
        }

        /// <summary>命座提升（B8 批，docs/09）：3★+同名重复出战→命座+1。value=新命座层；
        /// targetUnitId=升命单位（不生成新单位——DeployUnitExecutor 升命分支产出）；
        /// 客户端弹「命座提升」toast（等级显示待 UI 批）</summary>
        public static BattleCommand UpgradeConstellation(string actorUnitId, string targetUnitId, int sliceIndex,
            int indexInSlice, int newLevel)
        {
            return new BattleCommand
            {
                type = BattleCommandType.UpgradeConstellation,
                actorUnitId = actorUnitId,
                targetUnitId = targetUnitId,
                sliceIndex = sliceIndex,
                indexInSlice = indexInSlice,
                value = newLevel,
            };
        }

        public static BattleCommand ApplyBuff(string actorUnitId, string targetUnitId, int sliceIndex, int indexInSlice,
            int buffType, int buffLevel, int buffTurns, int sourceSkillId = 0)
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
                metadata = sourceSkillId, // 复用字段：来源技能 id（0=无；客户端 Buff 图标按来源技能解析，2026-10-06）
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

        /// <summary>物品消耗命令工厂（统一消耗模型 C-1，docs/active/30）：技能消耗手牌物品牌（如酒/苹果）
        /// ——targetUnitId=归属玩家、metadata=物品名（ItemName 枚举值）、value=消耗数量。
        /// 客户端应用=本地 handCards 镜像条目扣减（减尽移除）+手牌角标即时刷新（不等下回合快照）</summary>
        public static BattleCommand ItemConsume(string playerId, int sliceIndex, int indexInSlice,
            int itemName, int amount)
        {
            return new BattleCommand
            {
                type = BattleCommandType.ItemConsume,
                actorUnitId = playerId,
                targetUnitId = playerId,
                sliceIndex = sliceIndex,
                indexInSlice = indexInSlice,
                metadata = itemName,
                value = amount,
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
