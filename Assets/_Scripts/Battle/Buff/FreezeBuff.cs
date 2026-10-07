using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 冻结（docs/06：水 + 冰 → 冰冻〔Buff_Freeze.asset 每级持续回合〕× 级别 回合，重复冰冻取时间更长者
    /// ——数值单源=BuffConfig 资产，docs/active/39）。
    /// 硬控：挂载即写入 UnitStatus.Frozen（不可行动，docs/05 §5.5），到期解除。
    /// 冻结反应本身不是元素伤害（docs/06 §6.3"不涉及附着"），命中伤害正常结算。
    /// 韧性交互（超载-5 韧性 → 受控时间+1）B4 只做冻结本体，韧性管线随超载反应批次接。
    /// </summary>
    public class FreezeBuff : BaseBuff
    {
        private FreezeBuffConfig Cfg => (FreezeBuffConfig)Config;

        public override BuffType Type => BuffType.Freeze;

        public FreezeBuff(FreezeBuffConfig config, int level = 1)
        {
            Config = config;
            Level = Mathf.Max(1, level);
            RemainingTurns = Cfg.每级持续回合 * Level;
        }

        public override List<BattleEffect> OnTurnEnd()
        {
            return new List<BattleEffect>(); // 冻结无回合结束效果（纯控制）
        }

        public override void OnApplied()
        {
            owner?.GetUnitComponent<UnitStatus>()?.SetStatus(StatusType.Frozen, true);
        }

        public override void OnRemoved()
        {
            owner?.GetUnitComponent<UnitStatus>()?.SetStatus(StatusType.Frozen, false);
        }

        /// <summary>重复冰冻取时间更长者（docs/06）——覆写默认的时长累加</summary>
        public override void Merge(BaseBuff newer)
        {
            RemainingTurns = Mathf.Max(RemainingTurns, newer.RemainingTurns);
            Level = Mathf.Max(Level, newer.Level);
        }
    }
}
