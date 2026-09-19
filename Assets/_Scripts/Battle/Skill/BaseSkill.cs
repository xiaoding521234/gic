using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
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

        /// <summary>
        /// Host 侧结算（B4）：读片前快照、产出效应列表（TurnResolver 统一应用）——纯结算不改状态，
        /// 附着/反应/冻结等状态修改一律以 BattleEffect 形态产出。默认空（即时交互类技能的承载 B6 另议）。
        /// </summary>
        /// <param name="sim">Host 战场门面（位置/注册查询，勿改状态）</param>
        /// <param name="action">行动数据（direction/skillIndex/targetUnitId）</param>
        /// <param name="sliceSnapshot">片前快照（瞬发结算唯一状态源）</param>
        public virtual List<BattleEffect> ResolveEffects(BattleSimState sim, ActionData action, BattleSnapshot sliceSnapshot)
        {
            return new List<BattleEffect>();
        }

        /// <summary>技能参数取值（int；BaseType=Fixed 输出数值本身，其余输出数值本身由技能逻辑按语境解释）</summary>
        public int GetParamValue(SkillParamKey key, int fallback = 0)
        {
            var p = GetParam(key);
            return p != null ? p.value : fallback;
        }
    }
}


