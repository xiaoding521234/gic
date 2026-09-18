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
    /// 元素反应预判结果（结算时读片前快照的附着元素；纯数据，不改状态）
    /// </summary>
    public class ReactionOutcome
    {
        public bool HasReaction;

        /// <summary>反应提供的易伤增量（并入本次伤害易伤乘区，docs/18 决策五：同乘区加法并入）</summary>
        public float VulnerabilityBonus;

        /// <summary>反应施加的 Buff 类型（-1=无；冻结=水+冰）</summary>
        public int BuffType = -1;

        /// <summary>反应级别（B4 层数简化恒 1；多层消耗强化=2 层火+2 层冰=2 级融化 → B5/B8 层数批次）</summary>
        public int Level = 1;
    }

    /// <summary>
    /// 元素反应判定器（B4：融化 + 冻结两例，docs/18 决策三）。
    /// 纯判定（读目标附着 + 来袭元素 → 产出易伤增量/控制效果），状态修改全部走 BattleEffect：
    /// - 融化（火+冰 / 冰+火）：易伤 +50%（×级别）并入本次伤害；消耗被反应附着
    /// - 冻结（水+冰 / 冰+水）：冰冻 2 回合（ApplyBuffEffect 产出）；消耗被反应附着
    /// - 其它组合：无反应（同元素=刷新附着，由 AttachElementEffect 覆盖）
    /// 命中后的新附着统一由 AttachElementEffect 承载（覆盖=消耗被反应附着）。
    /// 层数系统（多层附着/扩散/燃烧草延长等）后续批次；本类只看 DyedElement 单值。
    /// </summary>
    public static class ElementReactionResolver
    {
        public const float MeltVulnerabilityBonus = 0.5f; // docs/06：融化 易伤 50% × 级别

        public static ReactionOutcome Preview(ElementType dyed, ElementType incoming)
        {
            var outcome = new ReactionOutcome();

            // 物理来袭不参与反应、不改附着（docs/06 §6.2 默认附着=元素伤害）
            if (incoming == ElementType.Physical)
                return outcome;

            // 融化：火 + 冰（docs/06）
            if (IsPair(dyed, incoming, ElementType.Pyro, ElementType.Cryo))
            {
                outcome.HasReaction = true;
                outcome.VulnerabilityBonus = MeltVulnerabilityBonus;
                return outcome;
            }

            // 冻结：水 + 冰（docs/06）——冰冻 2 × 级别 回合，重复取更长（FreezeBuff.Merge）
            if (IsPair(dyed, incoming, ElementType.Hydro, ElementType.Cryo))
            {
                outcome.HasReaction = true;
                outcome.BuffType = (int)BuffType.Freeze;
                return outcome;
            }

            return outcome;
        }

        private static bool IsPair(ElementType dyed, ElementType incoming, ElementType a, ElementType b)
        {
            return (dyed == a && incoming == b) || (dyed == b && incoming == a);
        }
    }
}
