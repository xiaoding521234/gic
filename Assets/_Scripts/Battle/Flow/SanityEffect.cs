using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{
    /// <summary>
    /// 理智恢复效应（2026-09-30 歌声之环批：环 tick「恢复1理智」结算——Host 应用到 UnitStats
    /// （RangedInt -300~300 自动钳）后经 TurnResolver 产 StatChange(Sanity) 命令；玩法消费方随未来理智机制批）
    /// </summary>
    public class SanityEffect : BattleEffect
    {
        public int Delta;

        /// <summary>命中时刻（秒，相对片播放起点；0=立即）——全库 WYSIWYG 命中时刻语义对齐
        ///（2026-10-16 首个可打出物品批随 SanityChange 原子补字段：物品使用/OnCast 恒 0 立即；
        /// 预留 OnHit 理智类原子的到点应用——命令层 StatChange(Sanity) 暂不带时刻=快照自愈）</summary>
        public float HitSeconds;

        public SanityEffect(string targetUnitId, int delta)
        {
            TargetUnitId = targetUnitId;
            Delta = delta;
        }
    }
}
