using System;
using System.Collections.Generic;
using UnityEngine;

public class Unit : MonoBehaviour
{
    private Dictionary<System.Type, IUnitComponent> _components = new();
    public UnitConfig.UnitData RawData { get; private set; }

    public List<BaseSkill> Skills = new();
    public List<BaseBuff> Buffs = new();


    private void Awake()
    {
        foreach (var comp in GetComponents<IUnitComponent>())
        {
            _components[comp.GetType()] = comp;
        }
    }

    public void InitWithData(UnitConfig.UnitData data)
    {
        RawData = data;
        foreach (var comp in _components.Values)
        {
            comp.Init(this);
        }

        InitSkills(data);
    }

    private void InitSkills(UnitConfig.UnitData data)
    {
        SkillConfig.SkillData[] skills = data.skills;
        foreach (var skillData in skills)
        {
            var skill = SkillFactory.CreateWithData(skillData);
            if (skill != null)
            {
                AddSkill(skill);
            }
        }
    }

    /// <summary>
    /// 获取指定类型的 Unit 组件
    /// </summary>
    public T GetUnitComponent<T>() where T : class, IUnitComponent
    {
        _components.TryGetValue(typeof(T), out var comp);
        return comp as T;
    }

    public bool HasUnitComponent<T>() where T : IUnitComponent
    {
        return _components.ContainsKey(typeof(T));
    }

    public void AddSkill(BaseSkill skill)
    {
        Skills.Add(skill);
        skill.owner = this;
    }

    public void RemoveSkill(BaseSkill skill)
    {
        Skills.Remove(skill);
    }

    public void AddBuff(BaseBuff buff)
    {
        Buffs.Add(buff);
        buff.owner = this;
    }

    public void RemoveBuff(BaseBuff buff)
    {
        Buffs.Remove(buff);
    }


}