using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{
    /// <summary>
    /// 元能变化效应（B6a：正=获取——移动使用+10 / 战技至少1次命中+10（多次命中不叠加，
    /// 同片按行动者合并实现）；负=爆发消耗。TargetUnitId=受益行动者自身）。
    /// 来源类别（2026-09-25 审查 R1 修复）：命令合并键=目标+类别——同类别去重（B6a 单行动
    /// 多命中只发一条），跨类别各发一条互不吞；此前合并键只含目标，协奏获能与消耗/同片移动获能
    /// 并存时取首条会静默吞掉命令（客户端元能显示背离）。类别内跨行动同目标（双延奏者同片协奏
    /// 同一目标）当前角色池不可能出现，出现时再细分携带行动源。
    /// </summary>
    public class EnergyEffect : BattleEffect
    {
        /// <summary>来源类别：未注明（兜底）</summary>
        public const int CategoryDefault = 0;
        /// <summary>来源类别：移动使用获能</summary>
        public const int CategoryMoveGain = 1;
        /// <summary>来源类别：战技命中获能</summary>
        public const int CategorySkillHitGain = 2;
        /// <summary>来源类别：协奏（延奏）获能——EffectCompiler 按**触发点**分类：一切 OnCast 获能
        /// 原子皆入此类别（命名沿协奏先例；未来 Normal 技能 OnCast 获能同用，无「协奏专属」语义，2026-09-27 复审注记）</summary>
        public const int CategoryEnsoGain = 3;
        /// <summary>来源类别：技能元能消耗</summary>
        public const int CategoryCost = 4;
        /// <summary>来源类别：Buff 回合末 tick 获取（B8 批 2026-09-30：歌声之环持有者元能——独立类别防与其它来源去重互吞）</summary>
        public const int CategoryBuffTickGain = 5;
        /// <summary>来源类别：元能溢出转移（B8 批，安柏1命被动——转移增量独立成类防与本体获能合并键互吞）</summary>
        public const int CategoryOverflowTransfer = 6;
        /// <summary>来源类别：破雾获能（2026-10-17 迷雾批追拍六，安柏2命「爆发破除迷雾，每破除
        /// C2FogEnergyThreshold 格获得 C2FogEnergyGain 元能」——独立类别防与其它来源合并键互吞）</summary>
        public const int CategoryFogBreakGain = 7;

        public int Delta;

        /// <summary>来源类别（命令合并键成员；CategoryDefault=兜底）</summary>
        public int Category;

        /// <summary>命中时刻（秒，相对片播放起点，ProjectileResolver 接触判定得出；0=立即——
        /// 移动获能/协奏/消耗/回合发放无命中时刻语义）。战技命中获能到点应用（2026-09-25 拍板
        /// 「命中时才给」：客户端元能随命中时刻跳变，非施放即跳；同片多命中去重取首条=最早命中）</summary>
        public float HitSeconds;

        public EnergyEffect(string targetUnitId, int delta, int category = CategoryDefault)
        {
            TargetUnitId = targetUnitId;
            Delta = delta;
            Category = category;
        }

        /// <summary>状态/命令合并键单源（ApplyEffects 与 MergeEnergyEffects 同口径恒等）：
        /// (目标, 来源类别)；**Buff 回合末 tick 逐层例外（2026-10-01 拍板④「元能条也逐层各跳一次，
        /// 每次+10」）**——BuffTickGain 类别并入层时刻（第 i 层 HitSeconds=i×BuffLayerStaggerSeconds：
        /// 逐层条目键各异=各跳各弹；同刻同目标仍并=同层多来源合并不变）。其余类别维持纯 (目标,类别)
        /// ——战技多命中「多次命中只获一次」B6a 去重口径不变（其 HitSeconds=真实命中时刻，并入键
        /// 会让两发箭矢各自成键破坏去重，勿扩）。</summary>
        public string MergeKey()
        {
            return Category == CategoryBuffTickGain
                ? $"{TargetUnitId}:{Category}:{Mathf.RoundToInt(HitSeconds * 1000f)}"
                : $"{TargetUnitId}:{Category}";
        }
    }
}
