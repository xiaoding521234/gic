using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{
    /// <summary>
    /// 地脉灵魂衰减效应（决策九十一）：每回合末灵魂 −⌊10%×最大生命⌋（SoulDecayPercentPerTurn 单源）。
    /// 走标准效应管道：应用层直扣 HP（非伤害——无防御/暴击/反应/格档；死亡由死亡循环
    /// 「灵魂+尸体双双消散」分支统一处理，将来胡桃/丽莎入地脉伤害致死灵魂走同一条链）；
    /// 命令层=StatChange(StatKindHp)（客户端血条衰减+灵魂蓝数字）；对账映射=BattleEffectCommandAudit。
    /// </summary>
    public class SoulDecayEffect : BattleEffect
    {
        /// <summary>衰减量（编译期按 ⌊10%×MaxHp⌋ 算好，应用层直扣）</summary>
        public int Amount;

        public SoulDecayEffect(string targetUnitId, int amount)
        {
            TargetUnitId = targetUnitId;
            Amount = amount;
        }
    }
}
