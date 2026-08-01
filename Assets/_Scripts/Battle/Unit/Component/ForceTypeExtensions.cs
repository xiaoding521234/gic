using System.Reflection;
using UnityEngine;
using UnityEngine.Localization;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
namespace GIC.Battle
{


    /// <summary>
    /// ForceType 扩展方法
    /// </summary>
    public static class ForceTypeExtensions
    {
        public static string GetDisplayName(this ForceType forceType)
        {
            var member = typeof(ForceType).GetMember(forceType.ToString());
            if (member.Length > 0)
            {
                var attr = member[0].GetCustomAttribute<InspectorNameAttribute>();
                if (attr != null) return attr.displayName;
            }
            return forceType.ToString();
        }

        private static string GetEntryKey(this ForceType forceType) => forceType.ToString();

        private static LocalizedString GetLocalizedString(this ForceType forceType)
        {
            return new LocalizedString(TableName.UIText.ToString(), forceType.GetEntryKey());
        }

        public static TextEntry GetEntry(this ForceType forceType, string leadingSeparator = "")
        {
            return new TextEntry(forceType.GetLocalizedString(), leadingSeparator);
        }
    }

}


