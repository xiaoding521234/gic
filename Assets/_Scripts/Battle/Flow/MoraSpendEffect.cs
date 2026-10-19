using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{
    /// <summary>
    /// 摩拉消耗效应（统一消耗模型 C-1，docs/30 §2.3——技能声明 Mora cost 用；玩家级）：
    /// TargetUnitId=玩家 ID（同 StaminaEffect 约定）。应用=TrySpendMora 池写；
    /// 命令=StatChange(StatKindMora)（客户端 §78 玩家资源分流已备）。
    /// 部署扣费/回合发放维持直产命令先例（决策七）不走本效应——本效应仅技能消耗链。
    /// AppliedAmount=应用回填（池不足时实际扣减量，0=零命令——同 MoraPlunder 先例；
    /// 理论不可达：门槛先行+每玩家每回合单行动，防御性钳制+Warn 记账）。
    /// </summary>
    public class MoraSpendEffect : BattleEffect
    {
        public int Amount;

        /// <summary>实际扣减量（ApplyEffects 回填；0=不足零命令）</summary>
        public int AppliedAmount;

        public MoraSpendEffect(string playerId, int amount)
        {
            TargetUnitId = playerId;
            Amount = amount;
        }
    }
}
