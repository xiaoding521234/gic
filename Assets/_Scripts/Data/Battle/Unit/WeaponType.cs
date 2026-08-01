using System;
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
    /// 武器类型枚举
    /// </summary>
    public enum WeaponType
    {
        [InspectorName("双手剑")]
        Claymore = 0,   // 双手剑
        
        [InspectorName("单手剑")]
        Sword = 1,      // 单手剑
        
        [InspectorName("长枪")]
        Polearm = 2,    // 长枪
        
        [InspectorName("臂铠")]
        Gauntlet = 3,   // 臂铠
        
        [InspectorName("法器")]
        Catalyst = 4,   // 法器
        
        [InspectorName("弓")]
        Bow = 5,        // 弓
        
        [InspectorName("枪")]
        Gun = 6         // 枪
    }

    /// <summary>
    /// WeaponType 扩展方法
    /// </summary>
    public static class WeaponTypeExtensions
    {
        
        /// <summary>
        /// 获取武器类型在本地化表中的 Entry Key
        /// </summary>
        private static string GetEntryKey(this WeaponType weaponType)
        {
            return weaponType.ToString();
        }

        /// <summary>
        /// 创建用于本地化系统的 LocalizedString 对象
        /// </summary>
        private static LocalizedString GetLocalizedString(this WeaponType weaponType)
        {
            return new LocalizedString(TableName.WeaponType.ToString(), weaponType.GetEntryKey());
        }

        /// <summary>
        /// 创建 Entry 对象（用于 TextCombiner）
        /// </summary>
        /// <param name="leadingSeparator">前置连接符</param>
        public static TextEntry GetEntry(this WeaponType weaponType, string leadingSeparator = "")
        {
            LocalizedString localizedString = weaponType.GetLocalizedString();
            return new TextEntry(localizedString, leadingSeparator);
        }
    }
}



