using System;
using UnityEngine;
namespace GIC.Data
{


    /// <summary>
    /// 效果原子触发时机（docs/active/29 §3；对照 EGamePlay FireType 的回合制退化）
    /// </summary>
    public enum SkillEffectTrigger
    {
        [InspectorName("施放时")] OnCast = 0,
        [InspectorName("命中时")] OnHit = 1,
        [InspectorName("消散时")] OnVanish = 2, // B-3 预留（投射物消散点效果——采集类技能依赖）
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
    }

    /// <summary>
    /// 作用目标筛选（OnCast=指向/施法者/势力语义；OnHit 恒=命中目标，除 CasterRadiusAllies 群体语义）
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
    /// 效果原子配置（B-1，docs/active/29 §3）——union 平铺载荷（决策九 D1 拍板：BattleCommand/
    /// SkillTimelineClip 同款风格，编辑器按 kind 显字段）；**原子只做效果产出声明（WHAT）**，
    /// 编译期由 EffectCompiler 展开成 BattleEffect——应用/合并/对账/命令发射全链零改动。
    /// 分工三真源：时轮=时间与判定规格（WHERE/WHEN）；参数表=数值（HOW MUCH，paramKey 引用防双源）；
    /// 效果原子=产出声明（WHAT）。
    /// 技能数据 effects 为空列表=走旧技能类兜底（渐进双轨，决策九 D5）。
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

        /// <summary>Buff 类型（ApplyBuff 用）</summary>
        public BuffType buffType = BuffType.Burn;

        /// <summary>触发的目标技能类型（TriggerSkill 用：查目标该型技能并结算——延奏→Henka 变奏）</summary>
        public SkillType targetSkillType = SkillType.Henka;
    }
}
