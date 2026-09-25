using System.Collections.Generic;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 数据驱动通用技能类（B-1，docs/18 决策九 D5）：SkillData.effects 非空且无注册专属类时的
    /// 通用承载——ResolveEffects=EffectCompiler 全编译（判定轨+OnCast/OnHit 效果原子），
    /// 瞄准预判=按判定 kind 工厂派发（预判/结算同形由工厂强制）。
    /// 加新技能=配 effects+时轮零代码（除新判定形制须扩 EffectCompiler 编译分支）；本类不再增长。
    /// </summary>
    public class ConfiguredSkill : BaseSkill
    {
        public override bool CanCast(Unit caster) => true;

        public override List<BattleEffect> ResolveEffects(BattleSimState sim, ActionData action, BattleSnapshot sliceSnapshot)
            => EffectCompiler.CompileSkill(sim, action, sliceSnapshot, RawData);

        public override bool WouldHitEnemyInDirection(BattleMapData map, BattleSnapshot snapshot,
            string casterPlayerId, BattleCell from, Direction2D direction)
            => EffectCompiler.WouldHitEnemyInDirection(Timeline, map, snapshot, casterPlayerId, from, direction);
    }
}
