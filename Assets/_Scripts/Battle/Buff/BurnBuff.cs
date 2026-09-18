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
    /// 燃烧（docs/06 元素与反应系统）：每回合结束受到 10 点火伤（0 层），持续 3 回合 × 级别。
    /// 来源=火+草反应（B4 接）；B2 由调试技能附带施加验证 DoT 链路。
    /// 草元素延长机制（附着草→消耗草→延长 3 回合 × 层数）B4 随元素反应落地。
    /// </summary>
    public class BurnBuff : BaseBuff
    {
        public const int DamagePerTurn = 10;  // docs/06：每回合结束 10 点火伤（0 层）
        public const int TurnsPerLevel = 3;   // docs/06：持续 3 回合 × 级别

        public override BuffType Type => BuffType.Burn;

        public BurnBuff(int level = 1)
        {
            Level = Mathf.Max(1, level);
            RemainingTurns = TurnsPerLevel * Level;
        }

        public override List<BattleEffect> OnTurnEnd()
        {
            var effects = new List<BattleEffect>();
            string ownerId = owner?.GetUnitComponent<UnitIdentity>()?.UnitID;
            if (ownerId == null) return effects;

            // 伤害归属=施加者（快照/命令流的 attacker），无施加者信息时归目标自身
            string sourceId = source != null
                ? (source.GetUnitComponent<UnitIdentity>()?.UnitID ?? ownerId)
                : ownerId;
            effects.Add(new DamageEffect(sourceId, ownerId, DamagePerTurn, (int)ElementType.Pyro));
            return effects;
        }
    }

    /// <summary>
    /// Buff 工厂（B2 最小：switch 分发；B4 反应批次扩为注册表）
    /// </summary>
    public static class BuffFactory
    {
        public static BaseBuff Create(BuffType type, int level, Unit source = null)
        {
            switch (type)
            {
                case BuffType.Burn:
                    return new BurnBuff(level) { source = source };
                case BuffType.Freeze:
                    return new FreezeBuff(level) { source = source };
                default:
                    GICLog.Warn($"[BuffFactory] 未实现的 Buff 类型 {type}");
                    return null;
            }
        }
    }
}
