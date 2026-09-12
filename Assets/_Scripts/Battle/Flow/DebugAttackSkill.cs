using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
namespace GIC.Battle
{


    /// <summary>
    /// B1 调试技能：硬编码固定伤害，用于跑通 DamagePipeline 全链路。
    /// B4 换正式 BaseSkill 子类 + SkillConfig.asset（docs/22 §5）。
    /// 直接 AddSkill 挂载（不走 SkillFactory/SkillName 枚举，避免调试残留进数据层）。
    /// </summary>
    public class DebugAttackSkill : BaseSkill
    {
        /// <summary>固定伤害值</summary>
        public const int FixedDamage = 30;

        public override bool CanCast(Unit caster)
        {
            return caster != null;
        }

        public override void Execute(Unit caster, SkillContext context)
        {
            // B1 逻辑结算走 SkillExecutor（快照结算架构），本方法保留给未来的直接执行路径
        }
    }
}
