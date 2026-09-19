using System.Collections.Generic;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 占位技能（未注册技能类的兜底）：不可施放、零效应——唯一职责=占住 unit.Skills 槽位，
    /// 保证 ActionData.skillIndex 与 UnitConfig.skills 数组严格对齐（HUD 与 Host 同源映射，
    /// docs/active/22 §3.1）。B4→B5 渐进注册技能时索引永不漂移。
    /// </summary>
    public class UnimplementedSkill : BaseSkill
    {
        public override bool CanCast(Unit caster) => false;

        public override List<BattleEffect> ResolveEffects(BattleSimState sim, ActionData action, BattleSnapshot sliceSnapshot)
        {
            return new List<BattleEffect>();
        }
    }
}
