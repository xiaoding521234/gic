using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{
    /// <summary>
    /// 元素反应效应（2026-09-22 接线）：反应发生的事实载体——融化此前只有"更大的伤害数字"无事件、
    /// 冻结只有 ApplyBuff 无反应语义，客户端无从表现反应。产出 Reaction 命令（快照自愈外的即时通道）
    /// </summary>
    public class ReactionEffect : BattleEffect
    {
        public string SourceUnitId;

        /// <summary>反应类型（BattleCommand.ReactionKindMelt / ReactionKindFreeze / ReactionKindSuperconduct）</summary>
        public int ReactionType;

        /// <summary>反应级别（决策七十四：=min(双方层数)——2层火+2层冰=2级融化）</summary>
        public int Level;

        /// <summary>命中时刻（秒，相对片播放起点；0=立即）——反应命令 launchMs 载荷（客户端到点
        /// 清附着图标：反应 1:1 消耗双方、等层零残留，docs/06 §6.3 / docs/18 决策二十五）</summary>
        public float HitSeconds;

        /// <summary>从附着侧消耗的层数（决策七十四：应用层按此减层——剩余层仍附着；
        /// 等层=清附着〔决策二十五零残留〕；默认 1=存量单层行为不变）</summary>
        public int ConsumedLayers = 1;

        public ReactionEffect(string sourceUnitId, string targetUnitId, int reactionType, int level)
        {
            SourceUnitId = sourceUnitId;
            TargetUnitId = targetUnitId;
            ReactionType = reactionType;
            Level = level;
        }
    }
}
