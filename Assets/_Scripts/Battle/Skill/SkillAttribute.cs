using System;

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