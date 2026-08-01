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


    [Serializable]
    public class Element
    {
        public ElementType elementType;
        public int stacks;

        public Element(ElementType elementType) : this(elementType, 1)
        {
        }

        public Element(ElementType elementType, int stacks)
        {
            this.elementType = elementType;
            this.stacks = stacks;
        }
    }

    /// <summary>
    /// 元素类型
    /// </summary>
    public enum ElementType
    {
        [InspectorName("物理")]
        [ElementFullName("物理")]
        Physical = 0,

        [InspectorName("火")]
        [ElementFullName("火元素")]
        Pyro = 1,

        [InspectorName("水")]
        [ElementFullName("水元素")]
        Hydro = 2,

        [InspectorName("风")]
        [ElementFullName("风元素")]
        Anemo = 3,

        [InspectorName("雷")]
        [ElementFullName("雷元素")]
        Electro = 4,

        [InspectorName("冰")]
        [ElementFullName("冰元素")]
        Cryo = 5,

        [InspectorName("草")]
        [ElementFullName("草元素")]
        Dendro = 6,

        [InspectorName("岩")]
        [ElementFullName("岩元素")]
        Geo = 7,

        [InspectorName("光")]
        [ElementFullName("光元素")]
        Light = 8,
    }

    /// <summary>
    /// 元素全名特性，用于存储元素的完整名称（如"火元素"）
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public sealed class ElementFullNameAttribute : Attribute
    {
        public string FullName { get; }

        public ElementFullNameAttribute(string fullName)
        {
            FullName = fullName;
        }
    }

    /// <summary>
    /// ElementType 扩展方法
    /// </summary>
    public static class ElementTypeExtensions
    {
        public static string GetShortName(this ElementType elementType)
        {
            var type = typeof(ElementType);
            var member = type.GetMember(elementType.ToString());

            if (member.Length > 0)
            {
                var attrs = member[0].GetCustomAttributes(typeof(InspectorNameAttribute), false);
                if (attrs.Length > 0)
                {
                    return ((InspectorNameAttribute)attrs[0]).displayName;
                }
            }

            return elementType.ToString();
        }

        /// <summary>
        /// 获取元素的完整名称（如"火元素"）
        /// </summary>
        public static string GetFullName(this ElementType elementType)
        {
            var type = typeof(ElementType);
            var member = type.GetMember(elementType.ToString());

            if (member.Length > 0)
            {
                var attrs = member[0].GetCustomAttributes(typeof(ElementFullNameAttribute), false);
                if (attrs.Length > 0)
                {
                    return ((ElementFullNameAttribute)attrs[0]).FullName;
                }
            }

            return elementType.GetShortName() + "元素";
        }

        /// <summary>
        /// 获取元素类型在本地化表中的 Entry Key
        /// </summary>
        private static string GetEntryKey(this ElementType elementType)
        {
            return elementType.ToString();
        }

        /// <summary>
        /// 创建用于本地化系统的 LocalizedString 对象（短名称）
        /// </summary>
        private static LocalizedString GetLocalizedString(this ElementType elementType)
        {
            return new LocalizedString(TableName.UIText.ToString(), elementType.GetEntryKey());
        }

        /// <summary>
        /// 创建 Entry 对象（用于 TextCombiner）- 短名称
        /// </summary>
        public static TextEntry GetEntry(this ElementType elementType, string leadingSeparator = "")
        {
            LocalizedString localizedString = elementType.GetLocalizedString();
            return new TextEntry(localizedString, leadingSeparator);
        }

        /// <summary>
        /// 获取元素的完整名称（本地化版本）
        /// </summary>
        public static LocalizedString GetLocalizedFullName(this ElementType elementType)
        {
            return new LocalizedString(TableName.UIText.ToString(), $"{elementType.ToString()}Full");
        }

        /// <summary>
        /// 创建 Entry 对象（用于 TextCombiner）- 完整名称
        /// </summary>
        public static TextEntry GetFullNameEntry(this ElementType elementType, string leadingSeparator = "")
        {
            return new TextEntry(elementType.GetLocalizedFullName(), leadingSeparator);
        }
    }
}



