using System.Collections.Generic;
using UnityEngine;
namespace GIC.Data
{


    /// <summary>
    /// Buff 配置资产基类（2026-10-07 拍板「遵循大厂的做法」——Buff 与技能/单位同等待遇，建独立配置资产。
    /// docs/active/39；业界对照=EGamePlay StatusConfig 形态：**每 BuffType 一个 ScriptableObject**，
    /// 技能施加时经 ApplyBuffEffect 参数通道**覆盖**（技能实参 &gt; 本资产默认——反应类无技能语境时本资产即真源）。
    /// 行为留 C# 类（BaseBuff 子类，编译期展开定式）——**本资产只装数据不装过程**。
    /// 资产路径约定：Assets/Resources/Configs/Buffs/Buff_{BuffType 名}.asset；
    /// 注册表=OfType(BuffType) 静态查表（Resources.LoadAll 惰性加载，首次访问初始化——域重载安全）。
    /// StatBuff 族（加攻/加速/减防）数值由技能参数注入，直接用基类资产（buffType+专属图标即可）；
    /// 行为族子类（BurnBuffConfig 等四件）**必须各自同名文件**——Unity 脚本资产关联按文件名匹配，
    /// 多类共文件=m_Script 丢失资产重载即死（本批实证）。
    /// </summary>
    [CreateAssetMenu(fileName = "BuffConfig", menuName = "Game/BuffConfig 通用（加攻/加速/减防）")]
    public class BuffConfig : ScriptableObject
    {
        [Header("Buff 类型（协议标识——注册表索引键，一资产一类型勿重复）")]
        public BuffType buffType = BuffType.None;

        [Header("专属图标（可空：客户端解析优先级=专属图标＞来源技能图标＞元素图标，决策五十回落链）")]
        public Sprite 专属图标;

        [Header("关联名（RelatedName 表键=link id——行为族填〔歌声之环/寒冰之棱〕供关联描述占位符反查；StatBuff 族留空=数值走来源技能参数通道）")]
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

        /// <summary>按类型查配置资产（注册表单源：BuffFactory 注入 / 客户端图标解析 / AI 光环半径感知共用）。
        /// 首次访问惰性加载 Assets/Resources/Configs/Buffs 全目录；重复 buffType=Warn 取首项（配置防呆，
        /// 批8「配置重复键防线」同款口径）；缺资产=null 由调用方防御（工厂 Warn+null）</summary>
        public static BuffConfig OfType(BuffType type)
        {
            EnsureLoaded();
            return _table.TryGetValue(type, out var cfg) ? cfg : null;
        }

        /// <summary>按关联名反查（link id=RelatedName 表键〔中文名〕→ 行为族配置资产；StatBuff 族关联名
        /// 留空=查不到返回 null——其描述数值走技能参数通道，勿给 StatBuff 资产填关联名）。
        /// 消费方=SkillDetailView.ShowRuleMode（Buff 类 link 的描述模板数值解析）</summary>
        public static BuffConfig ByRelatedName(string relatedName)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(relatedName)) return null;
            foreach (var cfg in _table.Values)
                if (cfg.关联名 == relatedName) return cfg;
            return null;
        }

        private static Dictionary<BuffType, BuffConfig> _table;
        private static bool _loaded;

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            _table = new Dictionary<BuffType, BuffConfig>();
            var all = Resources.LoadAll<BuffConfig>("Configs/Buffs");
            foreach (var cfg in all)
            {
                if (cfg == null || cfg.buffType == BuffType.None)
                {
                    GIC.Framework.GICLog.Warn($"[BuffConfig] 资产 {cfg?.name} 缺 buffType 配置——检查 Buffs 目录漏填");
                    continue;
                }
                if (_table.ContainsKey(cfg.buffType))
                {
                    GIC.Framework.GICLog.Warn($"[BuffConfig] BuffType.{cfg.buffType} 配置资产重复（{_table[cfg.buffType].name} / {cfg.name}）——取首项");
                    continue;
                }
                _table.Add(cfg.buffType, cfg);
            }
        }
    }
}
