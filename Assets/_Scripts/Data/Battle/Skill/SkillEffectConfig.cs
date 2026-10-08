using System;
using UnityEngine;
namespace GIC.Data
{


    /// <summary>
    /// 效果原子触发时机（docs/29 §3；对照 EGamePlay FireType 的回合制退化）
    /// </summary>
    public enum SkillEffectTrigger
    {
        [InspectorName("施放时")] OnCast = 0,
        [InspectorName("命中时")] OnHit = 1,
        [InspectorName("消散时")] OnVanish = 2, // B-3 预留（投射物消散点效果——采集类技能依赖）
        [InspectorName("登场/升命时")] OnDeploy = 3, // 命座被动（B8 批 2026-09-30 实装）：Talent 技能专用——ConstellationApplier 登场与升命时幂等重算消费，不经 EffectCompiler 施放链
    }

    /// <summary>
    /// 效果原子类型（2026-09-25 B-1 首批六 kind=现有四技能反推，docs/18 决策九护栏：
    /// 新需求先问"现有原子能否组合表达"，不能才扩枚举；对照 EGamePlay SkillEffectType）
    /// </summary>
    public enum SkillEffectKind
    {
        None = 0,

        [InspectorName("伤害")] Damage = 1,              // paramKey=技能 Damage 键（攻击百分比，含元素反应预览并入乘区）
        [InspectorName("治疗")] Heal = 2,                // paramKey=技能 Heal 键（编译时按 baseType 换算——docs/11 HealEffect 裸 int 坑的出口）
        [InspectorName("施加Buff")] ApplyBuff = 3,       // buffType + paramKey/2/3=BuffValue/StackLimit/Duration 三键（0=无参 Buff 默认构造）
        [InspectorName("元素附着")] AttachElement = 4,    // element=0(Physical)=施法者自身元素（覆盖=消耗被反应附着，docs/06）
        [InspectorName("元能变化")] EnergyGain = 5,      // value 直读（机制常量；技能参数驱动的获能改走 paramKey）
        [InspectorName("触发技能")] TriggerSkill = 6,    // 技能链：targetSkillType=查目标该型技能并结算（延奏→变奏 Henka，docs/18 决策九 D8）

        [InspectorName("摩拉掠夺")] MoraPlunder = 7,      // B-3 首扩（2026-09-25 随霜袭接线）：OnHit 每命中一个敌人，从其所属玩家摩拉池掠夺给施法者玩家（paramKey=MoraPlunder 键防双源；璃月契约先例=玩家池转移）

        [InspectorName("复苏")] Revive = 8,               // B-3 ②（2026-09-30 随芭芭拉闪耀奇迹接线）：目标为尸体时清除尸体态+治疗（paramKey=Heal 键按 baseType 换算；唯一复苏通道——docs/05 §5.4「血量永远0不复苏」例外条款）

        [InspectorName("属性提升")] StatBoost = 9,        // B8 命座批（2026-09-30）：OnDeploy 被动属性——statType+paramKey/value（Fixed=BaseFlat/Percent=BasePercent 修改器；容量型属性〔Energy〕=直接扩上限）；minConstellation=生效命座层

        [InspectorName("元能溢出转移")] EnergyOverflowTransfer = 10, // B8 命座批：OnDeploy 被动旗标——本命座层起，获得元能溢出部分转移给最近未满我方（安柏1命；ApplyEnergy 消费）
    }

    /// <summary>
    /// 效果原子作用条件（B-3 ②，docs/11「IfCorpse 条件原子」落地）：按目标存活态过滤——
    /// 同一技能 effects 列表内用 condition 声明分支（芭芭拉闪耀奇迹：尸体→复苏+治疗 /
    /// 活体→歌声之环）。None=无条件（存量原子全此档，纯新增字段零行为变化）。
    /// </summary>
    public enum SkillEffectCondition
    {
        [InspectorName("无条件")] None = 0,
        [InspectorName("目标已倒下")] TargetIsCorpse = 1,
        [InspectorName("目标未倒下")] TargetIsAlive = 2,
    }

    /// <summary>
    /// 作用目标筛选（OnCast=指向/施法者/势力语义；OnHit 默认=命中目标——Caster=施法者/行动者：
    /// 战技获能 B6a 受益者=施法者非命中敌（2026-09-25 修复实证）、CasterRadiusAllies=施法者半径群体）
    /// </summary>
    public enum SkillEffectTargetFilter
    {
        [InspectorName("目标自身")] Target = 0,
        [InspectorName("施法者")] Caster = 1,
        [InspectorName("蒙德或自身")] MondstadtOrSelf = 2, // 蒙德协奏规则（docs/07：目标=蒙德角色或施法者自身）
        [InspectorName("非蒙德")] NotMondstadt = 3,       // 蒙德协奏规则（非蒙德目标分支）
        [InspectorName("我方全体")] AllAllies = 4,        // OnCast 群体：我方全部存活单位各编译一次（元气迸发）
        [InspectorName("施法者半径内我方")] CasterRadiusAllies = 5, // OnHit 群体：以施法者为中心 radiusKey 格内我方存活（水之浅唱治疗）
    }

    /// <summary>
    /// 效果原子配置（B-1，docs/29 §3）——union 平铺载荷（决策九 D1 拍板：BattleCommand/
    /// SkillTimelineClip 同款风格，编辑器按 kind 显字段）；**原子只做效果产出声明（WHAT）**，
    /// 编译期由 EffectCompiler 展开成 BattleEffect——应用/合并/对账/命令发射全链零改动。
    /// 分工三真源：时轮=时间与判定规格（WHERE/WHEN）；参数表=数值（HOW MUCH，paramKey 引用防双源）；
    /// 效果原子=产出声明（WHAT）。
    /// 技能数据 effects 为空列表=UnimplementedSkill 占位不可施放（旧技能类兜底轨已随 2026-09-30
    /// SkillFactory 旧轨退役拆除——加技能=配 effects 零代码）。
    /// </summary>
    [Serializable]
    public class SkillEffectConfig
    {
        [Header("触发与类型")]
        public SkillEffectTrigger trigger = SkillEffectTrigger.OnHit;

        /// <summary>原子类型（载荷按 kind 部分有效）</summary>
        public SkillEffectKind kind = SkillEffectKind.Damage;

        [Header("作用目标（OnCast 语义）")]
        public SkillEffectTargetFilter targetFilter = SkillEffectTargetFilter.Target;

        [Header("作用条件（B-3 ②：按目标存活态过滤——复苏/增益分支声明位）")]
        /// <summary>作用条件：None=无条件；TargetIsCorpse/TargetIsAlive=编译层按目标 isCorpse 过滤（docs/11 IfCorpse）</summary>
        public SkillEffectCondition condition = SkillEffectCondition.None;

        [Header("数值（paramKey≠None 优先取参数表；value 为机制常量直读）")]
        /// <summary>主数值参数键（Damage/Heal=技能伤害/治疗键；ApplyBuff=BuffValue 键如 ATKBonus）</summary>
        public SkillParamKey paramKey = SkillParamKey.None;

        /// <summary>第二参数键（ApplyBuff=StackLimit 键）</summary>
        public SkillParamKey paramKey2 = SkillParamKey.None;

        /// <summary>第三参数键（ApplyBuff=Duration 键；None=无参 Buff 默认构造）</summary>
        public SkillParamKey paramKey3 = SkillParamKey.None;

        /// <summary>范围半径参数键（targetFilter=CasterRadiusAllies 有效：以施法者为中心切比雪夫半径，
        /// 数值引用参数表键防双源——如水之浅唱 HealRadius；None=无范围语义单目标）</summary>
        public SkillParamKey radiusKey = SkillParamKey.None;

        /// <summary>机制常量直读值（paramKey=None 时生效——如协奏元能 +10/+20 这类势力规则值）</summary>
        public int value;

        [Header("载荷（按 kind 部分有效）")]
        /// <summary>元素（Damage/AttachElement 用；0=Physical=施法者自身元素——非物理伤害天然取施法者元素）</summary>
        public ElementType element = ElementType.Physical;

        /// <summary>Buff 族类型（ApplyBuff 用；=BuffConfig.buffType 的镜像——具名 buff 身份=下方
        /// buffAsset 资产引用，此字段仅作缺引用时的回落与 Inspector 展示）</summary>
        public BuffType buffType = BuffType.Burn;

        /// <summary>具名 Buff 资产引用（ApplyBuff 用，决策五十七「资产即身份」）：拖 Buff_* 资产=
        /// 该原子施加这个具名 buff（同资产叠层/异资产同族共存——安柏加攻与班尼特加攻可同时存在）；
        /// 数值单源=资产（每层加成/叠层上限/持续回合）。null=回落 buffType 按同族首个资产（旧配置兼容）。
        /// 新增同类 buff（第二个加攻来源等）=新建 Buff_* 资产+此处拖引用，零代码</summary>
        public BuffConfig buffAsset;

        /// <summary>触发的目标技能类型（TriggerSkill 用：查目标该型技能并结算——延奏→Henka 变奏）</summary>
        public SkillType targetSkillType = SkillType.Henka;

        [Header("命座被动载荷（B8 批：OnDeploy 用）")]
        /// <summary>提升的属性类型（StatBoost 用；容量型属性〔Energy〕=直接扩上限非 BaseFlat）</summary>
        public StatType statType = StatType.HP;

        /// <summary>生效命座层（0=固有被动〔登场即有，docs/09「0命」〕；1~3=需升命到该层才生效——
        /// ConstellationApplier 按当前命座层幂等重算，层数只增故重算=全撤后重挂）</summary>
        public int minConstellation = 0;
    }
}
