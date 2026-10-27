using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{
    /// <summary>
    /// 体力变化效应（B6d 经济闭环）：配额行动（移动/战技/爆发）的体力消耗随行动效应产出，
    /// 经效应统一应用后产出 StatChange(StatKindStamina) 命令——与元能同构（B6a 先例）。
    /// **注意：TargetUnitId 字段此处承载玩家 ID 而非单位 ID**（体力/摩拉=玩家持有的物品牌，
    /// 非 Unit 属性）——ApplyEffects/EmitSliceCommands/对账按玩家资源解释该字段。
    /// 回合结束发放不走本效应（直产命令，同部署摩拉先例）。
    /// </summary>
    public class StaminaEffect : BattleEffect
    {
        public int Delta;

        /// <summary>命中时刻（秒，相对片播放起点；0=立即——配额消耗/延奏津贴/回合发放无命中时刻）。
        /// 途经采集=行走到达资源点格时刻（2026-10-28 返修：安柏「每次采集获得体力」逐次到点入账，
        /// docs/units/蒙德/安柏.md 技能行——同片与移动消耗并存各发各命令，**勿恢复同玩家去重**
        /// 〔去重会吞采集增量：状态逐条应用 vs 命令只剩消耗=漂移〕）</summary>
        public float HitSeconds;

        public StaminaEffect(string playerId, int delta, float hitSeconds = 0f)
        {
            TargetUnitId = playerId;
            Delta = delta;
            HitSeconds = hitSeconds;
        }
    }
}
