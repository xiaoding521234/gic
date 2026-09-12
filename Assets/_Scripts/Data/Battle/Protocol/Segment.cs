using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Framework;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Data
{


    /// <summary>
    /// 片命令块（Host 逐片推送；不再有回合级整块 TurnScript）
    /// </summary>
    [Serializable]
    public class Segment
    {
        [Header("定位")]
        public int turnNumber;
        public int sliceIndex;

        /// <summary>本片攻速（播放侧按 (回合最高攻速 - 本片攻速) / 10 秒排程）</summary>
        public int sliceAttackSpeed;

        /// <summary>本回合全场最高攻速</summary>
        public int turnMaxAttackSpeed;

        /// <summary>是否为片边界插入的即时行动块（0/1）</summary>
        public int insertedInstantAction;

        [Header("命令流")]
        public List<BattleCommand> commands = new List<BattleCommand>();

        public override string ToString()
        {
            return $"[Segment turn={turnNumber} slice={sliceIndex} speed={sliceAttackSpeed} cmds={commands.Count}" +
                   (insertedInstantAction != 0 ? " INSTANT" : "") + "]";
        }
    }
}
