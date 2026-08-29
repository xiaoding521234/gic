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
    /// 单位标签枚举
    /// </summary>
    public enum UnitTag
    {
        [InspectorName("无")]
        None = 0,

        #region buffEffect
        [InspectorName("攻击提升")]
        AttackUp = 1001,

        [InspectorName("防御提升")]
        DefenseUp = 1002,

        [InspectorName("移速提升")]
        MoveSpeedUp = 1003,

        [InspectorName("幸运暴击")]
        LuckyCrit = 1004,

        [InspectorName("视野")]
        Vision = 1005,

        [InspectorName("隐身")]
        Invisible = 1006,

        [InspectorName("护盾")]
        Shield = 1007,

        [InspectorName("韧性提升")]
        TenacityUp = 1008,
        #endregion

        #region debuffEffect
        [InspectorName("攻击削弱")]
        AttackDown = 2001,

        [InspectorName("防御削弱")]
        DefenseDown = 2002,

        [InspectorName("移速削弱")]
        MoveSpeedDown = 2003,
        #endregion

        #region battleFeature
        [InspectorName("承伤")]
        DamageAbsorption = 3001,

        [InspectorName("爆发输出")]
        BurstDamage = 3002,

        [InspectorName("稳定输出")]
        StableDamage = 3003,

        [InspectorName("灵巧")]
        Agile = 3004,

        [InspectorName("笨重")]
        Heavy = 3005,

        [InspectorName("前线推进")]
        FrontlinePush = 3006,

        [InspectorName("突进")]
        Dash = 3007,

        [InspectorName("协同攻击")]
        CoordinatedAttack = 3008,

        [InspectorName("推力")]
        Push = 3009,
        #endregion

        #region featureSpec
        [InspectorName("采集")]
        Gathering = 4001,

        [InspectorName("治疗")]
        Healing = 4002,

        [InspectorName("控制")]
        Control = 4003,

        [InspectorName("资源")]
        Resource = 4004,

        [InspectorName("复苏")]
        Revive = 4005,

        [InspectorName("时间操控")]
        TimeManipulation = 4006,

        [InspectorName("充能")]
        EnergyRecharge = 4007,
        #endregion

        #region factionFeature
        [InspectorName("快速延奏")]
        FastEnso = 5001,
        #endregion

        #region difficulty
        [InspectorName("入门")]
        DifficultyBeginner = 6001,

        [InspectorName("简单")]
        DifficultyEasy = 6002,

        [InspectorName("普通")]
        DifficultyNormal = 6003,

        [InspectorName("困难")]
        DifficultyHard = 6004,

        [InspectorName("专家")]
        DifficultyExpert = 6005,
        #endregion
    }

    /// <summary>
    /// UnitTag 扩展方法
    /// </summary>
    public static class UnitTagExtensions
    {
        
        /// <summary>
        /// 获取单位标签在本地化表中的 Entry Key
        /// </summary>
        private static string GetEntryKey(this UnitTag tag)
        {
            return tag.ToString();
        }

        /// <summary>
        /// 创建用于本地化系统的 LocalizedString 对象
        /// </summary>
        private static LocalizedString GetLocalizedString(this UnitTag tag)
        {
            return new LocalizedString(TableName.UnitTag.ToString(), tag.GetEntryKey());
        }

        /// <summary>
        /// 创建 Entry 对象（用于 TextCombiner）
        /// </summary>
        /// <param name="leadingSeparator">前置连接符</param>
        public static TextEntry GetEntry(this UnitTag tag, string leadingSeparator = "")
        {
            LocalizedString localizedString = tag.GetLocalizedString();
            return new TextEntry(localizedString, leadingSeparator);
        }
    }
}



