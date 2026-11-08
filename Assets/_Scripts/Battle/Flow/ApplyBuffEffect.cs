using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{
    /// <summary>
    /// 施加 Buff 效应（B2；片内快照结算产物 → Host 应用后产出 ApplyBuff 命令）
    /// </summary>
    public class ApplyBuffEffect : BattleEffect
    {
        public string SourceUnitId;
        public int BuffType;

        /// <summary>具名身份键（=BuffConfig 资产名，决策五十七「资产即身份」——同资产叠层/异资产共存；
        /// 空旧回放路径=BuffFactory 按 BuffType 回落查首个同族资产）</summary>
        public string BuffKey;

        public int Level;

        /// <summary>应用/合并后的剩余回合数（命令流用；由 Host 在应用后回填）</summary>
        public int Turns;

        /// <summary>技能参数通道（B-S1b：如 AttackUp 每层加成=ATKBonus——数值单源=技能参数表，
        /// 工厂按 Buff 类型解释；协议命令不携带）</summary>
        public int BuffValue;

        /// <summary>叠层上限通道（AttackUp 用=StackLimit 参数；协议命令不携带）</summary>
        public int StackLimit;

        /// <summary>持续秒数通道（AttackUp 用=Duration 参数——工厂按此构造初始计时）</summary>
        public int DurationTurns;

        /// <summary>来源技能 id（SkillName 枚举值；0=无来源技能——反应类 Buff〔燃烧/冻结/超导减防〕由
        /// CompileElementalDamage 产出无技能语境）。消费=客户端头顶 Buff 图标（2026-10-06
        /// 拍板「buff 图标用来源技能图标」）：随命令/快照透传，0=回落元素图标</summary>
        public int SourceSkillId;

        /// <summary>命中时刻（秒，相对片播放起点；0=立即）——WYSIWYG（决策七十六复测返修：
        /// 箭矢飞行中目标不提前挂 Buff 徽章〔冻结/超导减防〕；OnCast 施放类/回合末光环逐层
        /// BeatSeconds 错峰维持；合并命令取首条时刻=徽章首现拍）。Host 状态即时结算架构常态不变</summary>
        public float HitSeconds;

        public ApplyBuffEffect(string sourceUnitId, string targetUnitId, int buffType, int level,
            int buffValue = 0, int stackLimit = 0, int durationTurns = 0, int sourceSkillId = 0,
            string buffKey = null)
        {
            SourceUnitId = sourceUnitId;
            TargetUnitId = targetUnitId;
            BuffType = buffType;
            BuffKey = buffKey;
            Level = level;
            BuffValue = buffValue;
            StackLimit = stackLimit;
            DurationTurns = durationTurns;
            SourceSkillId = sourceSkillId;
        }
    }
}
