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
    /// 技能类型
    /// </summary>
    public enum SkillType
    {
        [InspectorName("移动")]
        Move = 1,

        [InspectorName("战技")]
        Normal = 2,

        [InspectorName("爆发")]
        Burst = 3,

        [InspectorName("天赋")]
        Talent = 4,

        [InspectorName("交互")]
        Interact = 5,

        [InspectorName("延奏")]
        Enso = 6,

        [InspectorName("变奏")]
        Henka = 7,

        [InspectorName("契约")]
        Contract = 8,

        [InspectorName("连携")]
        Renkei = 9,

        [InspectorName("智慧")]
        Wisdom = 10,
    }

    /// <summary>
    /// SkillType 扩展方法
    /// </summary>
    public static class SkillTypeExtensions
    {

        /// <summary>
        /// 获取技能类型在本地化表中的 Entry Key
        /// </summary>
        private static string GetEntryKey(this SkillType skillType)
        {
            return skillType.ToString();
        }

        /// <summary>
        /// 创建用于本地化系统的 LocalizedString 对象
        /// </summary>
        private static LocalizedString GetLocalizedString(this SkillType skillType)
        {
            return new LocalizedString(TableName.SkillType.ToString(), skillType.GetEntryKey());
        }

        /// <summary>
        /// 创建 Entry 对象（用于 TextCombiner）
        /// </summary>
        /// <param name="leadingSeparator">前置连接符</param>
        public static TextEntry GetEntry(this SkillType skillType, string leadingSeparator = "")
        {
            LocalizedString localizedString = skillType.GetLocalizedString();
            return new TextEntry(localizedString, leadingSeparator);
        }

        /// <summary>
        /// 判断是否为主动技能
        /// </summary>
        public static bool IsActive(this SkillType skillType)
        {
            switch (skillType)
            {
                case SkillType.Move:
                case SkillType.Normal:
                case SkillType.Burst:
                case SkillType.Interact:
                case SkillType.Enso:
                case SkillType.Contract:
                    return true;
                case SkillType.Talent:
                case SkillType.Henka:
                case SkillType.Renkei:
                case SkillType.Wisdom:
                    return false;
                default:
                    return false;
            }
        }
    }
}



