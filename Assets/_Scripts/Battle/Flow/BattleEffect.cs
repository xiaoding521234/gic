using System;
using System.Collections.Generic;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
namespace GIC.Battle
{


    /// <summary>
    /// 结算效应（片内快照结算的产物；统一应用到状态后再产出命令）
    /// </summary>
    public abstract class BattleEffect
    {
        public string TargetUnitId;
    }

    /// <summary>
    /// 伤害效应（同片多来源伤害按 (攻击者,目标) 合并）
    /// </summary>
    public class DamageEffect : BattleEffect
    {
        public string AttackerUnitId;
        public int Amount;
        public int Element;

        /// <summary>投放形态（B4）：0=瞬发直击 / 1=直线飞行投射物（客户端播箭矢）；docs/18 决策二"投放形态由技能数据驱动"</summary>
        public int Delivery;

        /// <summary>投射物发射格（Delivery=1 时有效；命中点=目标位置）</summary>
        public BattleCell FromCell;

        public DamageEffect(string attackerUnitId, string targetUnitId, int amount, int element = 0,
            int delivery = 0, BattleCell fromCell = default)
        {
            AttackerUnitId = attackerUnitId;
            TargetUnitId = targetUnitId;
            Amount = amount;
            Element = element;
            Delivery = delivery;
            FromCell = fromCell;
        }
    }

    /// <summary>
    /// 治疗效应
    /// </summary>
    public class HealEffect : BattleEffect
    {
        public string SourceUnitId;
        public int Amount;

        public HealEffect(string sourceUnitId, string targetUnitId, int amount)
        {
            SourceUnitId = sourceUnitId;
            TargetUnitId = targetUnitId;
            Amount = amount;
        }
    }

    /// <summary>
    /// 施加 Buff 效应（B2；片内快照结算产物 → Host 应用后产出 ApplyBuff 命令）
    /// </summary>
    public class ApplyBuffEffect : BattleEffect
    {
        public string SourceUnitId;
        public int BuffType;
        public int Level;

        /// <summary>应用/合并后的剩余回合数（命令流用；由 Host 在应用后回填）</summary>
        public int Turns;

        public ApplyBuffEffect(string sourceUnitId, string targetUnitId, int buffType, int level)
        {
            SourceUnitId = sourceUnitId;
            TargetUnitId = targetUnitId;
            BuffType = buffType;
            Level = level;
        }
    }

    /// <summary>
    /// 元素附着效应（B4）：反应消耗语义已由 ElementReactionResolver 在结算时定夺，
    /// 本效应=效应应用阶段直接 Dye 目标为 incoming 元素（覆盖旧附着=消耗）
    /// </summary>
    public class AttachElementEffect : BattleEffect
    {
        public string SourceUnitId;
        public int Element;

        public AttachElementEffect(string sourceUnitId, string targetUnitId, int element)
        {
            SourceUnitId = sourceUnitId;
            TargetUnitId = targetUnitId;
            Element = element;
        }
    }
}
