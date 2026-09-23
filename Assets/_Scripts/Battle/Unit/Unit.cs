using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
using GIC.Framework;
namespace GIC.Battle
{


    public class Unit : MonoBehaviour
    {
        private Dictionary<System.Type, IUnitComponent> _components = new();
        public UnitConfig.UnitData RawData { get; private set; }

        public List<BaseSkill> Skills = new();
        public List<BaseBuff> Buffs = new();

        /// <summary>
        /// 单位体积（docs/05 §5.3：角色/造物 = 1，建筑 = 2；格子体积容量 3。
        /// 体积判定在阻挡规则之上，无视阻挡能力也不可绕过）
        /// </summary>
        public int Volume => RawData != null && RawData.unitType == UnitType.Building ? 2 : 1;


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
            var skills = data.skills;
            if (skills == null) return;
            for (int i = 0; i < skills.Count; i++)
            {
                var sd = skills[i]?.data;
                if (sd == null)
                {
                    // 空引用槽会破坏 skillIndex 对齐（skills 列表序=skillIndex 语义）——数据错误态，告警暴露
                    GICLog.Warn($"[Unit] {data.unitName} 技能引用列表第 {i} 位为空槽——skillIndex 对齐被破坏，请修 UnitConfig");
                    continue;
                }
                var skill = SkillFactory.CreateWithData(sd);
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
}


