using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 攻击提升（延奏 Buff，docs/07-势力机制/蒙德.md「延奏自身效果」）：
    /// 被协奏角色攻击力提升 BonusPerStack × Level（层数），持续 Duration 回合；
    /// 同类重复施加=叠层（至多 StackLimit 层）+ 时长累加（持续时间随层数叠加，docs/units/蒙德/安柏.md #4）。
    /// 实现体=StatBuff 基类（2026-09-25 三轮审查 S7：与 MoveSpeedBuff 同构收口）。
    /// </summary>
    public class AttackUpBuff : StatBuff
    {
        public override BuffType Type => BuffType.AttackUp;

        public AttackUpBuff(int level, int bonusPerStack, int stackLimit, int turns)
            : base(StatType.Attack, level, bonusPerStack, stackLimit, turns)
        {
        }
    }
}
