using System.Collections.Generic;
using UnityEngine;
namespace GIC.Data
{


    /// <summary>
    /// Buff 配置资产基类（2026-10-07 拍板「遵循大厂的做法」——Buff 与技能/单位同等待遇，建独立配置资产。
    /// docs/active/39；业界对照=EGamePlay StatusConfig 形态。**2026-10-08 决策五十七「资产即身份」**：
    /// 每个具名 buff=一个资产（buffKey=资产名）——同资产叠层合并、**异资产同族共存**（安柏加攻
    /// 「百发百中」与班尼特加攻可同时存在，用户拍板「不同名即可叠加」）；BuffType 枚举降级为族分类。
    /// 施加方经 SkillEffectConfig.buffAsset 引用（或效应 BuffKey）指定具名 buff；新增同类 buff=
    /// 新建 Buff_* 资产+关联名/图标/数值，零代码。
    /// 行为留 C# 类（BaseBuff 子类，编译期展开定式）——**本资产只装数据不装过程**。
    /// 行为族子类（BurnBuffConfig 等四件）**必须各自同名文件**——Unity 脚本资产关联按文件名匹配，
    /// 多类共文件=m_Script 丢失资产重载即死（决策五十四批实证）。
    /// </summary>
    [CreateAssetMenu(fileName = "BuffConfig", menuName = "Game/BuffConfig 通用（加攻/加速/减防）")]
    public class BuffConfig : ScriptableObject
    {
        [Header("Buff 类型（协议标识——注册表索引键，一资产一类型勿重复）")]
        public BuffType buffType = BuffType.None;

        [Header("专属图标（可空：客户端解析优先级=专属图标＞来源技能图标＞元素图标，决策五十回落链）")]
        public Sprite 专属图标;

        [Header("关联名（RelatedName 表键=link id——每个具名 buff 必填〔百发百中/隐藏的实力/冰棱减防〕：关联描述占位符反查+关联面板显示名；决策五十七：具名 buff 一律填，旧「StatBuff 族留空」口径作废）")]
        public string 关联名;

        // ==================== 关联描述占位符（2026-10-07 决策五十六：Buff 描述 {Key} 数值=BuffConfig 单源金色高亮） ====================

        /// <summary>每回合伤害百分比（行为族光环通用）</summary>
        public const string KeyAuraDamagePercent = "AuraDamagePercent";
        /// <summary>每回合治疗百分比（歌声之环）</summary>
        public const string KeyAuraHealPercent = "AuraHealPercent";
        /// <summary>基础元能获取（歌声之环）</summary>
        public const string KeyAuraEnergyGain = "AuraEnergyGain";
        /// <summary>每回合理智恢复（歌声之环）</summary>
        public const string KeyAuraSanityGain = "AuraSanityGain";
        /// <summary>作用半径（行为族光环通用）</summary>
        public const string KeyAuraRadius = "AuraRadius";
        /// <summary>碎裂治疗百分比（寒冰之棱）</summary>
        public const string KeyShatterHealPercent = "ShatterHealPercent";
        /// <summary>碎裂元能阈值百分比（寒冰之棱）</summary>
        public const string KeyShatterEnergyThreshold = "ShatterEnergyThreshold";
        /// <summary>基础叠层上限（寒冰之棱）</summary>
        public const string KeyBaseStackLimit = "BaseStackLimit";
        /// <summary>每层加成（StatBuff 族——延奏类 buff 数值单源，2026-10-07 返修三「技能资产剥离 buff 配置」）</summary>
        public const string KeyStatBonus = "BonusPerStack";
        /// <summary>叠层上限（StatBuff 族）</summary>
        public const string KeyStatStackLimit = "StackLimit";
        /// <summary>持续回合（StatBuff 族；&lt;0 渲染本地化「永久」）</summary>
        public const string KeyStatDurationTurns = "DurationTurns";

        /// <summary>全部占位符键（SkillDescriptionBuilder.BuildRelated 遍历集——单源防拼写漂移）</summary>
        public static readonly string[] RelatedPlaceholderKeys =
        {
            KeyAuraDamagePercent, KeyAuraHealPercent, KeyAuraEnergyGain, KeyAuraSanityGain,
            KeyAuraRadius, KeyShatterHealPercent, KeyShatterEnergyThreshold, KeyBaseStackLimit,
            KeyStatBonus, KeyStatStackLimit, KeyStatDurationTurns,
        };

        /// <summary>关联描述占位符解析（家族覆写：键→资产数值；null=未识别键，构建层保留原文便于排查）。
        /// StatBuff 族不覆写——其数值由来源技能参数经 ApplyBuffEffect 通道单源（延奏类 buff 描述占位符
        /// 直接用 SkillParamKey，同技能描述全链）</summary>
        public virtual int? ResolveRelatedPlaceholder(string key) => null;

        /// <summary>关联描述占位符的数值基准（家族覆写：键→SkillBaseType；null=纯数字）。
        /// 与技能参数通道**同构**（value+baseType 二元组——渲染时 % 与基底名随数值**整体金色高亮**，
        /// 与技能描述「100%攻击力全金」观感一致；2026-10-07 返修四：此前占位符只回纯数字、
        /// 模板自带 %/基底名当普通文本导致观感分裂）</summary>
        public virtual SkillBaseType? RelatedPlaceholderBaseType(string key) => null;

        /// <summary>按类型查**首个**配置资产（决策五十七「资产即身份」后本查询仅作旧路径回落：
        /// 无 buffKey 的效应/旧回放按族找默认资产。具名路径一律 ByKey(buffKey)）。首次访问惰性加载
        /// Assets/Resources/Configs/Buffs 全目录；同族多资产共存合法（第二个加攻来源等）；
        /// 缺资产=null 由调用方防御（工厂 Warn+null）</summary>
        public static BuffConfig OfType(BuffType type)
        {
            EnsureLoaded();
            foreach (var cfg in _all)
                if (cfg.buffType == type) return cfg;
            return null;
        }

        /// <summary>按具名身份键查配置资产（决策五十七「资产即身份」：buffKey=资产名。
        /// 注册表单源：BuffFactory 具名构造 / 客户端图标解析共用；缺键=null 调用方回落 OfType）</summary>
        public static BuffConfig ByKey(string buffKey)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(buffKey)) return null;
            foreach (var cfg in _all)
                if (cfg.name == buffKey) return cfg;
            return null;
        }

        /// <summary>按关联名反查（link id=RelatedName 表键〔中文名〕→ 配置资产——关联面板描述占位符
        /// 解析与显示名单源；具名 buff 关联名必填=反查恒命中）。
        /// 消费方=SkillDetailView.ShowRuleMode（Buff 类 link 的描述模板数值解析）</summary>
        public static BuffConfig ByRelatedName(string relatedName)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(relatedName)) return null;
            foreach (var cfg in _all)
                if (cfg.关联名 == relatedName) return cfg;
            return null;
        }

        private static List<BuffConfig> _all;
        private static bool _loaded;

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            _all = new List<BuffConfig>();
            var all = Resources.LoadAll<BuffConfig>("Configs/Buffs");
            foreach (var cfg in all)
            {
                if (cfg == null || cfg.buffType == BuffType.None)
                {
                    GIC.Framework.GICLog.Warn($"[BuffConfig] 资产 {cfg?.name} 缺 buffType 配置——检查 Buffs 目录漏填");
                    continue;
                }
                _all.Add(cfg);
            }
        }
    }
}
