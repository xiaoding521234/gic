using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 移速提升（凯亚延奏·隐藏的实力，docs/units/蒙德/凯亚.md #4 + docs/07 蒙德）：
    /// 被协奏角色移速提升 BonusPerStack × Level（层数），持续 Duration 回合；叠层+时长累加同 AttackUp。
    /// 生效载体=UnitStats 修改器（BaseFlat 区）——移速含 Buff 直连移动距离换算
    /// （MoveExecutor.MaxMoveDistance 读 UnitStats.MoveSpeed，移速提升即走更远，B-S1b 同源链）。
    /// 实现体=StatBuff 基类（2026-09-25 三轮审查 S7：与 AttackUpBuff 同构收口）。
    /// </summary>
    public class MoveSpeedBuff : StatBuff
    {
        public override BuffType Type => BuffType.MoveSpeedUp;

        public MoveSpeedBuff(int level, int bonusPerStack, int stackLimit, int turns)
            : base(StatType.MoveSpeed, level, bonusPerStack, stackLimit, turns)
        {
        }
    }
}
