using System;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 结算效应（片内快照结算的产物；统一应用到状态后再产出命令）
    /// </summary>
    public abstract class BattleEffect
    {
        public string TargetUnitId;
    }

    /// <summary>
    /// 伤害效应（同片多来源伤害按 (攻击者,目标) 合并）
    /// </summary>
    public class DamageEffect : BattleEffect
    {
        public string AttackerUnitId;
        public int Amount;
        public int Element;

        /// <summary>投放形态（B4）：0=瞬发直击 / 1=直线飞行投射物（客户端播箭矢）；docs/18 决策二"投放形态由技能数据驱动"</summary>
        public int Delivery;

        /// <summary>投射物发射格（Delivery=1 时有效）</summary>
        public BattleCell FromCell;

        /// <summary>命中点连续格心坐标（Delivery=1 投射物有效；格心坐标系：格 c 的心=c+0.5）。
        /// Host 接触判定得出、命令下发时千分定点化（hitX/hitY）——勿由双端各自推算（docs/active/22 §11）</summary>
        public float HitPointX;
        public float HitPointY;

        /// <summary>本次命中触发的元素反应子类型（0=无；2026-09-22——Damage 命令带反应标记，
        /// 客户端伤害数字带反应名，如"蒸发 40"）</summary>
        public int ReactionType;

        /// <summary>发射时刻（毫秒，相对片播放起点；时轮 B-S1——投射物前摇/瞬发段时刻，
        /// 客户端据此延迟箭矢起飞与伤害数字节拍；命令合并键含此值=逐发不并）</summary>
        public int LaunchMs;

        public DamageEffect(string attackerUnitId, string targetUnitId, int amount, int element = 0,
            int delivery = 0, BattleCell fromCell = default, float hitPointX = 0f, float hitPointY = 0f,
            int reactionType = 0, int launchMs = 0)
        {
            AttackerUnitId = attackerUnitId;
            TargetUnitId = targetUnitId;
            Amount = amount;
            Element = element;
            Delivery = delivery;
            FromCell = fromCell;
            HitPointX = hitPointX;
            HitPointY = hitPointY;
            ReactionType = reactionType;
            LaunchMs = launchMs;
        }
    }

    /// <summary>
    /// 投射物待判定效应（B5 连续判定体系）：技能结算阶段只声明"发射"（发射格/方向/伤害参数），
    /// 实际命中由 ProjectileResolver 在同片移动展开后按执行阶段时间轴连续判定（接触立牌圆柱之时、
    /// 读命中时刻连续插值位置，docs/18 决策二）。不进入效应应用阶段——命中后被替换为 Hit 全套产物。
    /// </summary>
    public class ProjectileEffect : BattleEffect
    {
        public string AttackerUnitId;

        /// <summary>发射者行动（命中后 Hit 全套效应产出需要；Host 进程内引用，不序列化）</summary>
        public ActionData Action;

        /// <summary>敌我判定参照（行动归属玩家）</summary>
        public string PlayerId;

        /// <summary>发射格（片初快照位置）</summary>
        public BattleCell FromCell;

        /// <summary>飞行方向增量（十字归一）</summary>
        public int DeltaX;
        public int DeltaY;

        /// <summary>每发攻击百分比（时轮 B-S1 起逐发独立判定——多段不再合并；无时轮兜底=旧合并值）</summary>
        public int AttackPercent;

        /// <summary>发射时刻（秒，相对片播放起点；时轮 B-S1——前摇即此偏移；0=立即发射）</summary>
        public float LaunchSeconds;

        /// <summary>飞行速度（格/s；0=BattleMetrics.ProjectileSpeed 默认——per-skill 规格时轮化）</summary>
        public float Speed;

        /// <summary>判定圆柱直径（0=BattleMetrics.UnitCylinderDiameter 默认）</summary>
        public float Diameter;

        /// <summary>射程上限（格；0=ProjectileRule.MaxRange 默认）</summary>
        public int Range;

        public ProjectileEffect(string attackerUnitId, ActionData action, int attackPercent,
            BattleCell fromCell, int deltaX, int deltaY,
            float launchSeconds = 0f, float speed = 0f, float diameter = 0f, int range = 0)
        {
            AttackerUnitId = attackerUnitId;
            TargetUnitId = null; // 命中前未定
            Action = action;
            PlayerId = action.playerId;
            FromCell = fromCell;
            DeltaX = deltaX;
            DeltaY = deltaY;
            AttackPercent = attackPercent;
            LaunchSeconds = launchSeconds;
            Speed = speed;
            Diameter = diameter;
            Range = range;
        }
    }

    /// <summary>
    /// 治疗效应
    /// </summary>
    public class HealEffect : BattleEffect
    {
        public string SourceUnitId;
        public int Amount;

        public HealEffect(string sourceUnitId, string targetUnitId, int amount)
        {
            SourceUnitId = sourceUnitId;
            TargetUnitId = targetUnitId;
            Amount = amount;
        }
    }

    /// <summary>
    /// 施加 Buff 效应（B2；片内快照结算产物 → Host 应用后产出 ApplyBuff 命令）
    /// </summary>
    public class ApplyBuffEffect : BattleEffect
    {
        public string SourceUnitId;
        public int BuffType;
        public int Level;

        /// <summary>应用/合并后的剩余回合数（命令流用；由 Host 在应用后回填）</summary>
        public int Turns;

        /// <summary>技能参数通道（B-S1b：如 AttackUp 每层加成=ATKBonus——数值单源=技能参数表，
        /// 工厂按 Buff 类型解释；协议命令不携带）</summary>
        public int BuffValue;

        /// <summary>叠层上限通道（AttackUp 用=StackLimit 参数；协议命令不携带）</summary>
        public int StackLimit;

        /// <summary>持续回合通道（AttackUp 用=Duration 参数——工厂按此构造初始计时）</summary>
        public int DurationTurns;

        public ApplyBuffEffect(string sourceUnitId, string targetUnitId, int buffType, int level,
            int buffValue = 0, int stackLimit = 0, int durationTurns = 0)
        {
            SourceUnitId = sourceUnitId;
            TargetUnitId = targetUnitId;
            BuffType = buffType;
            Level = level;
            BuffValue = buffValue;
            StackLimit = stackLimit;
            DurationTurns = durationTurns;
        }
    }

    /// <summary>
    /// 元能变化效应（B6a：正=获取——移动使用+10 / 战技至少1次命中+10（多次命中不叠加，
    /// 同片按行动者合并实现）；负=爆发消耗。TargetUnitId=受益行动者自身）。
    /// 来源类别（2026-09-25 审查 R1 修复）：命令合并键=目标+类别——同类别去重（B6a 单行动
    /// 多命中只发一条），跨类别各发一条互不吞；此前合并键只含目标，协奏获能与消耗/同片移动获能
    /// 并存时取首条会静默吞掉命令（客户端元能显示背离）。类别内跨行动同目标（双延奏者同片协奏
    /// 同一目标）当前角色池不可能出现，出现时再细分携带行动源。
    /// </summary>
    public class EnergyEffect : BattleEffect
    {
        /// <summary>来源类别：未注明（兜底）</summary>
        public const int CategoryDefault = 0;
        /// <summary>来源类别：移动使用获能</summary>
        public const int CategoryMoveGain = 1;
        /// <summary>来源类别：战技命中获能</summary>
        public const int CategorySkillHitGain = 2;
        /// <summary>来源类别：协奏（延奏）获能</summary>
        public const int CategoryEnsoGain = 3;
        /// <summary>来源类别：技能元能消耗</summary>
        public const int CategoryCost = 4;

        public int Delta;

        /// <summary>来源类别（命令合并键成员；CategoryDefault=兜底）</summary>
        public int Category;

        public EnergyEffect(string targetUnitId, int delta, int category = CategoryDefault)
        {
            TargetUnitId = targetUnitId;
            Delta = delta;
            Category = category;
        }
    }

    /// <summary>
    /// 元素附着效应（B4）：反应消耗语义已由 ElementReactionResolver 在结算时定夺，
    /// 本效应=效应应用阶段直接 Dye 目标为 incoming 元素（覆盖旧附着=消耗）
    /// </summary>
    public class AttachElementEffect : BattleEffect
    {
        public string SourceUnitId;
        public int Element;

        public AttachElementEffect(string sourceUnitId, string targetUnitId, int element)
        {
            SourceUnitId = sourceUnitId;
            TargetUnitId = targetUnitId;
            Element = element;
        }
    }

    /// <summary>
    /// 元素反应效应（2026-09-22 接线）：反应发生的事实载体——融化此前只有"更大的伤害数字"无事件、
    /// 冻结只有 ApplyBuff 无反应语义，客户端无从表现反应。产出 Reaction 命令（快照自愈外的即时通道）
    /// </summary>
    public class ReactionEffect : BattleEffect
    {
        public string SourceUnitId;

        /// <summary>反应类型（BattleCommand.ReactionKindMelt / ReactionKindFreeze）</summary>
        public int ReactionType;

        /// <summary>反应级别（B4 层数简化恒 1）</summary>
        public int Level;

        public ReactionEffect(string sourceUnitId, string targetUnitId, int reactionType, int level)
        {
            SourceUnitId = sourceUnitId;
            TargetUnitId = targetUnitId;
            ReactionType = reactionType;
            Level = level;
        }
    }

    /// <summary>
    /// 体力变化效应（B6d 经济闭环）：配额行动（移动/战技/爆发）的体力消耗随行动效应产出，
    /// 经效应统一应用后产出 StatChange(StatKindStamina) 命令——与元能同构（B6a 先例）。
    /// **注意：TargetUnitId 字段此处承载玩家 ID 而非单位 ID**（体力/摩拉=玩家持有的物品牌，
    /// 非 Unit 属性）——ApplyEffects/EmitSliceCommands/对账按玩家资源解释该字段。
    /// 回合结束发放不走本效应（直产命令，同部署摩拉先例）。
    /// </summary>
    public class StaminaEffect : BattleEffect
    {
        public int Delta;

        public StaminaEffect(string playerId, int delta)
        {
            TargetUnitId = playerId;
            Delta = delta;
        }
    }
}
