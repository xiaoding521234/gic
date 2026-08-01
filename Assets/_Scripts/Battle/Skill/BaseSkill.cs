using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
namespace GIC.Battle
{


    /// <summary>
    /// 技能基类
    /// </summary>
    public abstract class BaseSkill
    {
        public Unit owner;
        public SkillConfig.SkillData RawData { get; private set; }

        private SkillType skillType = SkillType.Normal;

        public int energyCost = 0;
        public int moraCost = 0;
        public int staminaCost = 0;

        public Sprite icon;
        
        public List<SkillParam> SkillParams { get; private set; }

        public virtual void Init(SkillConfig.SkillData rawData)
        {
            RawData = rawData;
            
            // 深拷贝参数数据
            SkillParams = new List<SkillParam>();
            if (rawData.customParams != null)
            {
                foreach (var p in rawData.customParams)
                {
                    SkillParams.Add(new SkillParam(p.key,p.value,p.baseType));
                }
            }


            skillType = rawData.skillType;

            /* energyCost = rawData.energyCost;
            moraCost = rawData.moraCost;
            staminaCost = rawData.staminaCost; */

            if(rawData.icon != null)
            {
                icon = rawData.icon;
            }
            else
            {
                string path = $"UI/Skills/{rawData.skillID.ToString().ToSnakeCase()}";
                icon = Resources.Load<Sprite>(path);
            }
            
        }

        public SkillParam GetParam(SkillParamKey key)
        {
            foreach (var p in SkillParams)
            {
                if (p.key == key)
                {
                    return p;
                }
            }
            return null;
        }

        public abstract bool CanCast(Unit caster);
        public abstract void Execute(Unit caster, SkillContext context);
    }
}


