using UnityEngine;
using GIC.Framework;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Data
{


    /// <summary>
    /// 状态类型
    /// </summary>
    public enum StatusType
    {
        [InspectorName("隐身")]
        Invisible,

        [InspectorName("霸体")]
        SuperArmor,

        [InspectorName("晕眩")]
        Stunned,

        [InspectorName("沉默")]
        Silenced,

        [InspectorName("冰冻")]
        Frozen,

        [InspectorName("石化")]
        Petrified
    }

    // StatusType 扩展方法
    public static class StatusTypeExtensions
    {
        public static string GetDisplayName(this StatusType statusType)
        {
            var type = typeof(StatusType);
            var member = type.GetMember(statusType.ToString());

            if (member.Length > 0)
            {
                var attrs = member[0].GetCustomAttributes(typeof(InspectorNameAttribute), false);
                if (attrs.Length > 0)
                {
                    return ((InspectorNameAttribute)attrs[0]).displayName;
                }
            }

            return statusType.ToString();
        }
    }
}

