using System;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
namespace GIC.Battle
{


    /// <summary>
    /// 技能特性 - 用于标记技能类并自动注册到工厂
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class SkillAttribute : Attribute
    {
        public SkillName SkillID { get; }
        
        public SkillAttribute(SkillName skillID)
        {
            SkillID = skillID;
        }
    }
}


