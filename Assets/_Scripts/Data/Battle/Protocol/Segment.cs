using System;
using System.Collections.Generic;
using UnityEngine;
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

        /// <summary>是否为部署段（0/1；B6c——回合开始先于攻速分桶结算；播放侧短节拍不驱动攻速排程/高亮，docs/18 决策七：出战占玩家行动配额）</summary>
        public int deploy;

        /// <summary>是否为回合结束效果块（0/1；B2：Buff 计时与回合结束效果——播放侧短节拍、不驱动攻速排程/执行高亮）</summary>
        public int turnEnd;

        [Header("命令流")]
        public List<BattleCommand> commands = new List<BattleCommand>();

        public override string ToString()
        {
            return $"[Segment turn={turnNumber} slice={sliceIndex} speed={sliceAttackSpeed} cmds={commands.Count}" +
                   (insertedInstantAction != 0 ? " INSTANT" : "") +
                   (deploy != 0 ? " DEPLOY" : "") +
                   (turnEnd != 0 ? " TURN-END" : "") + "]";
        }
    }
}
