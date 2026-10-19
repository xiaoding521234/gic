using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{
    /// <summary>
    /// 摩拉掠夺效应（B-3 首个资源类原子，2026-09-25 霜袭接线）：命中敌方单位时从其**所属玩家的
    /// 摩拉池**掠夺给施法者玩家（玩家池转移——璃月契约"拒签差额折算摩拉由主契者掠夺"同族语义，
    /// docs/units/璃月/行秋.md）。TargetUnitId=被掠夺玩家 ID、ToPlayerId=掠夺方玩家 ID（双玩家，
    /// 同 StaminaEffect 的"玩家 ID 入 TargetUnitId"约定）。应用时 AppliedGain=min(Amount, 被掠夺方池)
    /// ——池空抢不到（实现取值，观感确认点 docs/11）；双向池写经 ApplyMoraDelta（含手牌货币条目同步）。
    /// HitSeconds=命中时刻（同元能「命中时才给」；霜袭瞬发段恒 0=立即）
    /// </summary>
    public class MoraPlunderEffect : BattleEffect
    {
        public string ToPlayerId;
        public int Amount;

        /// <summary>实际掠夺量（ApplyEffects 按被掠夺方池钳出后回填；0=池空零动作零命令）</summary>
        public int AppliedGain;

        /// <summary>命中时刻（秒，相对片播放起点；0=立即）</summary>
        public float HitSeconds;

        public MoraPlunderEffect(string fromPlayerId, string toPlayerId, int amount)
        {
            TargetUnitId = fromPlayerId;
            ToPlayerId = toPlayerId;
            Amount = amount;
        }
    }
}
