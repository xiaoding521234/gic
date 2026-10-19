using System;
using System.Collections.Generic;
using GIC.Data;

namespace GIC.Battle
{
    /// <summary>死亡归因单源（2026-10-20 审查批#7 下沉）：「命令流内该受击者最近一次 Damage 的攻击者」
    /// ——决策八十一装备掠夺（TurnResolver.KillerPlayerOf）与 OpponentProfiler 核心摧毁记账原为两份
    /// 实现，仅靠注释宣称同源且语义有暗差（Profiler 跳过无主伤害不记、掠夺按最近 Damage 直取）。
    /// 统一为决策八十一口径：最近 Damage 直取；归属（OwnerPlayerID）由调用方自行解析。</summary>
    public static class KillCredit
    {
        /// <summary>命令流 [0, uptoExclusive) 内 victimId 最近一次 Damage 的攻击者 unitId（无=返回 null）。
        /// TurnResolver 传 segment.commands.Count（死亡命令刚入列，列表末即死亡位——其后本段不会再有
        /// 针对 victim 的更早 Damage）；OpponentProfiler 传当前 Death 命令下标（同段其后的鞭尸 Damage
        /// 不回填归属）。</summary>
        public static string LastDamagerBefore(List<BattleCommand> commands, int uptoExclusive, string victimId)
        {
            if (commands == null || string.IsNullOrEmpty(victimId)) return null;
            int last = Math.Min(uptoExclusive, commands.Count);
            for (int i = last - 1; i >= 0; i--)
            {
                var c = commands[i];
                if (c.type == BattleCommandType.Damage && c.targetUnitId == victimId)
                    return c.actorUnitId;
            }
            return null;
        }
    }
}
