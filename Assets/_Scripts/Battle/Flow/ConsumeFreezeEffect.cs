using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{
    /// <summary>
    /// 冻结层消耗效应（决策七十四「冻结 Buff 可作为冰元素参与反应」）：附着为空时冻结 Buff 作为
    /// 第二冰源参与融化/超导——每个回合相当于 1 层冰，反应消耗 FreezeConsumed 回合。
    /// 应用=FreezeBuff.RemainingTurns -= AppliedTurns；**归零立即解冻**（拍板「消耗到 0 立即解冻」：
    /// _sim.RemoveBuff 注销+清 Frozen 状态+Depleted=true→命令层发 RemoveBuff〔霜化即时退场——
    /// 随寒冰之棱碎裂先例立即应用，勿当延迟 bug〕）。AppliedTurns&gt;0 且未归零=零命令（冻结徽章
    /// 回合数靠下回合快照自愈——Frost 表现由 Frozen 状态驱动、仍冻结中故不受影响）；无冻结 Buff
    /// （编译后状态被外部移除的防御路径）=AppliedTurns=0 零命令零动作。
    /// </summary>
    public class ConsumeFreezeEffect : BattleEffect
    {
        public string SourceUnitId;

        /// <summary>申请消耗回合数（=反应级别）</summary>
        public int Turns;

        /// <summary>实际消耗回合数（应用回填；0=无冻结 Buff 防御路径）</summary>
        public int AppliedTurns;

        /// <summary>归零即解冻（应用回填；true=已移除冻结 Buff，命令层发 RemoveBuff）</summary>
        public bool Depleted;

        /// <summary>被移除的冻结 Buff 具名身份（应用回填=Config 资产名——RemoveBuff 命令载荷）</summary>
        public string AppliedKey;

        public ConsumeFreezeEffect(string sourceUnitId, string targetUnitId, int turns)
        {
            SourceUnitId = sourceUnitId;
            TargetUnitId = targetUnitId;
            Turns = turns;
        }
    }
}
