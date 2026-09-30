using System;
using System.Collections.Generic;
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
    /// 治疗效应（HitSeconds=命中时刻（秒，相对片播放起点，ProjectileResolver 接触判定得出；
    /// 0=立即——延奏/变奏 OnCast 治疗无飞行段）。客户端治疗数字/血条到点应用（2026-09-25
    /// 拍板「命中时才给」——水之浅唱半径治疗随水球落地弹 +N，非施放即跳）
    /// </summary>
    public class HealEffect : BattleEffect
    {
        public string SourceUnitId;
        public int Amount;

        /// <summary>命中时刻（秒，相对片播放起点；0=立即）</summary>
        public float HitSeconds;

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
        /// <summary>来源类别：协奏（延奏）获能——EffectCompiler 按**触发点**分类：一切 OnCast 获能
        /// 原子皆入此类别（命名沿协奏先例；未来 Normal 技能 OnCast 获能同用，无「协奏专属」语义，2026-09-27 复审注记）</summary>
        public const int CategoryEnsoGain = 3;
        /// <summary>来源类别：技能元能消耗</summary>
        public const int CategoryCost = 4;
        /// <summary>来源类别：Buff 回合末 tick 获取（B8 批 2026-09-30：歌声之环持有者元能——独立类别防与其它来源去重互吞）</summary>
        public const int CategoryBuffTickGain = 5;
        /// <summary>来源类别：元能溢出转移（B8 批，安柏1命被动——转移增量独立成类防与本体获能合并键互吞）</summary>
        public const int CategoryOverflowTransfer = 6;

        public int Delta;

        /// <summary>来源类别（命令合并键成员；CategoryDefault=兜底）</summary>
        public int Category;

        /// <summary>命中时刻（秒，相对片播放起点，ProjectileResolver 接触判定得出；0=立即——
        /// 移动获能/协奏/消耗/回合发放无命中时刻语义）。战技命中获能到点应用（2026-09-25 拍板
        /// 「命中时才给」：客户端元能随命中时刻跳变，非施放即跳；同片多命中去重取首条=最早命中）</summary>
        public float HitSeconds;

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

    /// <summary>
    /// 摩拉掠夺效应（B-3 首个资源类原子，2026-09-25 霜袭接线）：命中敌方单位时从其**所属玩家的
    /// 摩拉池**掠夺给施法者玩家（玩家池转移——璃月契约"拒签差额折算摩拉由主契者掠夺"同族语义，
    /// docs/units/璃月/行秋.md）。TargetUnitId=被掠夺玩家 ID、ToPlayerId=掠夺方玩家 ID（双玩家，
    /// 同 StaminaEffect 的"玩家 ID 入 TargetUnitId"约定）。应用时 AppliedGain=min(Amount, 被掠夺方池)
    /// ——池空抢不到（实现取值，观感确认点 docs/11）；双向池写经 ApplyMoraDelta（含手牌货币条目同步）。
    /// HitSeconds=命中时刻（同元能「命中时才给」；霜袭瞬发段恒 0=立即）
    /// </summary>
    public class MoraPlunderEffect : BattleEffect
    {
        public string ToPlayerId;
        public int Amount;

        /// <summary>实际掠夺量（ApplyEffects 按被掠夺方池钳出后回填；0=池空零动作零命令）</summary>
        public int AppliedGain;

        /// <summary>命中时刻（秒，相对片播放起点；0=立即）</summary>
        public float HitSeconds;

        public MoraPlunderEffect(string fromPlayerId, string toPlayerId, int amount)
        {
            TargetUnitId = fromPlayerId;
            ToPlayerId = toPlayerId;
            Amount = amount;
        }
    }

    /// <summary>
    /// 摩拉消耗效应（统一消耗模型 C-1，docs/active/30 §2.3——技能声明 Mora cost 用；玩家级）：
    /// TargetUnitId=玩家 ID（同 StaminaEffect 约定）。应用=TrySpendMora 池写；
    /// 命令=StatChange(StatKindMora)（客户端 §78 玩家资源分流已备）。
    /// 部署扣费/回合发放维持直产命令先例（决策七）不走本效应——本效应仅技能消耗链。
    /// AppliedAmount=应用回填（池不足时实际扣减量，0=零命令——同 MoraPlunder 先例；
    /// 理论不可达：门槛先行+每玩家每回合单行动，防御性钳制+Warn 记账）。
    /// </summary>
    public class MoraSpendEffect : BattleEffect
    {
        public int Amount;

        /// <summary>实际扣减量（ApplyEffects 回填；0=不足零命令）</summary>
        public int AppliedAmount;

        public MoraSpendEffect(string playerId, int amount)
        {
            TargetUnitId = playerId;
            Amount = amount;
        }
    }

    /// <summary>
    /// 物品消耗效应（统一消耗模型 C-1，docs/active/30 §2.3——技能声明 Item/AnyItem cost 用，如酒/苹果/食物）：
    /// TargetUnitId=玩家 ID。**双模式**：AnyOfSubType=false=指定物品（Item 字段有效）；
    /// AnyOfSubType=true=同类任意（SubType 字段有效，如「任意饮品」——应用时按手牌列表序
    /// 确定性逐条扣、跨条目凑足、原子性失败不扣分毫；货币卡不可被匹配，见 ResourceGate）。
    /// 应用=LoseCard（条目真源扣减、减尽移除）；命令=按 Consumed 明细逐条 ItemConsume
    /// （客户端手牌镜像即时扣减+角标刷新）；对账按明细 (玩家,物品) 查存在性。
    /// AppliedAmount 同 MoraSpend 防御性回填口径（0=零命令非漏发）。
    /// </summary>
    public class ItemConsumeEffect : BattleEffect
    {
        public int Item;

        /// <summary>同类任意模式的子类型（AnyOfSubType=true 时有效）</summary>
        public int SubType;

        /// <summary>true=消耗任意 SubType 同类物品（Item 忽略）；false=消耗指定 Item</summary>
        public bool AnyOfSubType;

        /// <summary>申请消耗量</summary>
        public int Amount;

        /// <summary>实际扣减总量（ApplyEffects 回填；0=不足零命令）</summary>
        public int AppliedAmount;

        /// <summary>实际消耗明细（ApplyEffects 回填：(物品, 数量) 逐条——命令发射/对账遍历用；
        /// 指定模式=单条，同类任意模式=按手牌序逐条凑量）</summary>
        public readonly List<(int item, int count)> Consumed = new List<(int item, int count)>();

        /// <summary>指定物品模式构造（kind=Item）</summary>
        public ItemConsumeEffect(string playerId, ItemName item, int amount)
        {
            TargetUnitId = playerId;
            Item = (int)item;
            AnyOfSubType = false;
            Amount = amount;
        }

        /// <summary>同类任意模式构造（kind=AnyItem——如「任意饮品×1」）</summary>
        public ItemConsumeEffect(string playerId, ItemSubType subType, int amount)
        {
            TargetUnitId = playerId;
            SubType = (int)subType;
            AnyOfSubType = true;
            Amount = amount;
        }
    }

    /// <summary>
    /// 复苏效应（B-3 ②，芭芭拉闪耀奇迹——docs/05 §5.4「血量永远 0 不复苏」的唯一例外通道）：
    /// 应用=清除目标尸体态+治疗（一次性=isCorpse→存活 与 HP+HealAmount 单效应原子化，
    /// 客户端单命令同步解灰+弹 +N，无中间态）。HealAmount=编译期 ResolveHealAmount 换算后的
    /// 终值（BasedOnMaxHealth=施法者最大生命——2026-09-30 拍板翻转，受疗者治疗效率单源）；
    /// 目标非尸体=应用层防御性 no-op（编译层 condition=TargetIsCorpse 已保证，零命令）。
    /// </summary>
    public class ReviveEffect : BattleEffect
    {
        public string SourceUnitId;

        /// <summary>复活血量（治疗量终值——编译期换算，应用层直加）</summary>
        public int HealAmount;

        /// <summary>应用回填（目标为尸体才应用；false=活体防御性 no-op——命令发射/对账豁免零命令）</summary>
        public bool Applied;

        public ReviveEffect(string sourceUnitId, string targetUnitId, int healAmount)
        {
            SourceUnitId = sourceUnitId;
            TargetUnitId = targetUnitId;
            HealAmount = healAmount;
        }
    }
}
