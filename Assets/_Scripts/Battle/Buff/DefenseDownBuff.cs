using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 防御减少（寒冰之棱2命「命中敌人还会使其防御减少5」，2026-10-01 凛冽轮舞批）：
    /// StatBuff 族负值修改器——持有者防御 -ReduceAmount×层；**10 层 12 回合、叠层+时长累加**
    /// （2026-10-01 复测拍板「可以叠加10层，持续12回合，持续时间随层数叠加」——与攻击提升/
    /// 移速提升完全同构）；易伤乘区消费（DamagePipeline 100/(100+防御)——减防=增伤）。
    /// 数值单源=施加者命座技能参数 C2DefenseReduce（经 ApplyBuffEffect 参数通道注入）；
    /// 上限/时长=IcicleBuff.DefDownStackLimit/DefDownDurationTurns（tick 施加方收口）。
    /// </summary>
    public class DefenseDownBuff : StatBuff
    {
        public override BuffType Type => BuffType.DefenseDown;

        /// <param name="reduceAmount">每次施加的防御减少量（正数语义，构造时取负入修改器）</param>
        public DefenseDownBuff(int level, int reduceAmount, int stackLimit, int turns)
            : base(StatType.Defense, level, -Mathf.Max(0, reduceAmount), stackLimit, turns)
        {
        }
    }
}
