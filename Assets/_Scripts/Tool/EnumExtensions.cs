using System;
using UnityEngine;
using GIC.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
namespace GIC.Tool
{


    public static class EnumExtensions
    {
        public static string GetInspectorName(this Enum enumValue)
        {
            var type = enumValue.GetType();
            var member = type.GetMember(enumValue.ToString());

            if (member.Length > 0)
            {
                var attrs = member[0].GetCustomAttributes(typeof(InspectorNameAttribute), false);
                if (attrs.Length > 0)
                {
                    return ((InspectorNameAttribute)attrs[0]).displayName;
                }
            }

            return enumValue.ToString();
        }
    }
}


