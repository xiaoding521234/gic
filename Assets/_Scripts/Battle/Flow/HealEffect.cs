using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{
    /// <summary>
    /// 治疗效应（HitSeconds=命中时刻（秒，相对片播放起点，ProjectileResolver 接触判定得出；
    /// 0=立即——延奏/变奏 OnCast 治疗无飞行段）。客户端治疗数字/血条到点应用（2026-09-25
    /// 拍板「命中时才给」——水之浅唱半径治疗随水球落地弹 +N，非施放即跳）
    /// </summary>
    public class HealEffect : BattleEffect
    {
        public string SourceUnitId;
        public int Amount;

        /// <summary>命中时刻（秒，相对片播放起点；0=立即）</summary>
        public float HitSeconds;

        /// <summary>吸血自疗标记（2026-10-01 三次拍板终版「修改吸血，同源同刻合并」）：不再豁免
        /// 合并——吸血与普通治疗同走 (来源,目标,命中毫秒) 键（合并副本保留本标记）；现役唯一职责=
        /// 命令层载荷 metadata=HealKindLifesteal，客户端据此弹「吸血 +N」名前缀（与反应名前缀同风格）</summary>
        public bool IsLifesteal;

        public HealEffect(string sourceUnitId, string targetUnitId, int amount)
        {
            SourceUnitId = sourceUnitId;
            TargetUnitId = targetUnitId;
            Amount = amount;
        }
    }
}
