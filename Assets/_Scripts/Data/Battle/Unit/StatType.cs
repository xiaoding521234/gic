using UnityEngine;
using GIC.Framework;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Data
{


    /// <summary>
    /// 属性类型
    /// </summary>
    public enum StatType
    {
        [InspectorName("生命值")]
        HP,

        [InspectorName("攻击力")]
        Attack,

        [InspectorName("防御力")]
        Defense,

        [InspectorName("攻速")]
        AttackSpeed,

        [InspectorName("移速")]
        MoveSpeed,

        [InspectorName("幸运")]
        Luck,

        [InspectorName("韧性")]
        Tenacity,

        [InspectorName("精通")]
        Mastery,

        [InspectorName("理智")]
        Sanity,

        [InspectorName("穿透")]
        Penetration,

        [InspectorName("视野范围")]
        VisionRange,

        [InspectorName("免伤")]
        DamageReduction,

        [InspectorName("增伤")]
        DamageBonus
    }

    // StatType 扩展方法
    public static class StatTypeExtensions
    {
        public static string GetDisplayName(this StatType statType)
        {
            var type = typeof(StatType);
            var member = type.GetMember(statType.ToString());

            if (member.Length > 0)
            {
                var attrs = member[0].GetCustomAttributes(typeof(InspectorNameAttribute), false);
                if (attrs.Length > 0)
                {
                    return ((InspectorNameAttribute)attrs[0]).displayName;
                }
            }

            return statType.ToString();
        }
    }
}

