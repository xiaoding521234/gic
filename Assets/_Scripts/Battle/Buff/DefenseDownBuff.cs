using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 冰棱减防（凯亚寒冰之棱2命 tick 命中附带，2026-10-01 凛冽轮舞批立；决策五十七具名化）：
    /// StatBuff 族负值修改器——持有者防御 -每层加成×层；**叠层（上限内）+时长累加**、
    /// 永久不计时（与攻击提升/移速提升完全同构）；易伤乘区消费（DamagePipeline 100/(100+防御)
    /// ——减防=增伤）。**数值全单源=Buff_DefenseDown 资产**（每层加成 5/叠层上限 10/持续 -1
    /// ——C2DefenseReduce 命座注入通道 2026-10-08 决策五十七退役）；显示名/关联面板=
    /// 关联名「冰棱减防」（RelatedName/RelatedDescription 表键链）。
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
