using UnityEngine;
using UnityEngine.Localization;
using GIC.Framework;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Data
{


    /// <summary>
    /// 技能参数键枚举 — 替代原来的裸字符串 key
    /// </summary>
    public enum SkillParamKey
    {
        [InspectorName("无")]
        None = 0,

        // 伤害类
        [InspectorName("伤害")]
        Damage = 1,

        [InspectorName("单次伤害")]
        SingleDamage = 2,

        // 治疗
        [InspectorName("治疗")]
        Heal = 3,

        // 数量/次数
        [InspectorName("次数")]
        Count = 4,

        [InspectorName("采集次数")]
        CollectCount = 16,

        [InspectorName("伤害次数")]
        DamageCount = 17,

        // 距离/范围
        [InspectorName("移动距离")]
        MoveDistance = 5,

        [InspectorName("生效半径")]
        EffectRadius = 6,

        [InspectorName("施法半径")]
        CastRadius = 7,

        // 属性提升
        [InspectorName("攻击提升")]
        ATKBonus = 8,

        // 消耗
        [InspectorName("元能消耗")]
        EnergyCost = 9,

        [InspectorName("摩拉消耗")]
        MoraCost = 10,

        [InspectorName("体力消耗")]
        StaminaCost = 11,

        // 持续
        [InspectorName("持续回合")]
        Duration = 12,

        // 命座
        [InspectorName("1命移速提升")]
        C1MoveSpeed = 13,

        [InspectorName("1命视野提升")]
        C1Vision = 14,

        [InspectorName("2命移速提升")]
        C2MoveSpeed = 15,

        [InspectorName("2命迷雾元能阈值")]
        C2FogEnergyThreshold = 18,

        [InspectorName("2命迷雾元能获取")]
        C2FogEnergyGain = 19,

        [InspectorName("视野暴露持续回合")]
        VisionDuration = 20,

        [InspectorName("召唤数量")]
        ShardCount = 21,

        [InspectorName("飞行距离")]
        ProjectileDistance = 22,

        [InspectorName("治疗半径")]
        HealRadius = 23,

        // 凯亚命座
        [InspectorName("1命治疗效率")]
        C1HealEfficiency = 24,

        [InspectorName("1命吸血")]
        C1LifeSteal = 25,

        [InspectorName("2命叠层上限")]
        C2StackLimit = 26,

        [InspectorName("2命防御减少")]
        C2DefenseReduce = 27,

        [InspectorName("3命叠层上限")]
        C3StackLimit = 28,

        [InspectorName("3命生效半径")]
        C3Radius = 29,

        // 凯亚延奏
        [InspectorName("移速提升")]
        MoveSpeedBonus = 30,

        // 凯亚战技
        [InspectorName("伤害距离")]
        DamageDistance = 31,

        // 芭芭拉命座
        [InspectorName("1命获取元能")]
        C1EnergyGain = 32,

        [InspectorName("2命生效半径")]
        C2Radius = 33,

        // 安柏采集
        [InspectorName("采集元能获取")]
        CollectEnergyGain = 34,

        [InspectorName("采集体力获取")]
        CollectStaminaGain = 35,
    }

    /// <summary>
    /// SkillParamKey 扩展方法
    /// </summary>
    public static class SkillParamKeyExtensions
    {
        /// <summary>
        /// 获取参数键在本地化表中的 Entry Key
        /// </summary>
        private static string GetEntryKey(this SkillParamKey key)
        {
            return key.ToString();
        }

        /// <summary>
        /// 创建用于本地化系统的 LocalizedString 对象
        /// </summary>
        private static LocalizedString GetLocalizedString(this SkillParamKey key)
        {
            return new LocalizedString(TableName.SkillParamName.ToString(), key.GetEntryKey());
        }

        /// <summary>
        /// 创建 Entry 对象（用于 TextCombiner）
        /// </summary>
        public static TextEntry GetEntry(this SkillParamKey key, string leadingSeparator = "")
        {
            LocalizedString localizedString = key.GetLocalizedString();
            return new TextEntry(localizedString, leadingSeparator);
        }
    }

}



