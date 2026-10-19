using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{
    /// <summary>
    /// 投射物待判定效应（B5 连续判定体系）：技能结算阶段只声明"发射"（发射格/方向/伤害参数），
    /// 实际命中由 ProjectileResolver 在同片移动展开后按执行阶段时间轴连续判定（接触立牌圆柱之时、
    /// 读命中时刻连续插值位置，docs/18 决策二）。不进入效应应用阶段——命中后被替换为 Hit 全套产物。
    /// </summary>
    public class ProjectileEffect : BattleEffect
    {
        public string AttackerUnitId;

        /// <summary>发射者行动（命中后 Hit 全套效应产出需要；Host 进程内引用，不序列化）</summary>
        public ActionData Action;

        /// <summary>敌我判定参照（行动归属玩家）</summary>
        public string PlayerId;

        /// <summary>发射格（片初快照位置）</summary>
        public BattleCell FromCell;

        /// <summary>飞行方向增量（十字归一）</summary>
        public int DeltaX;
        public int DeltaY;

        /// <summary>每发攻击百分比（时轮 B-S1 起逐发独立判定——多段不再合并；无时轮兜底=旧合并值）</summary>
        public int AttackPercent;

        /// <summary>发射时刻（秒，相对片播放起点；时轮 B-S1——前摇即此偏移；0=立即发射）</summary>
        public float LaunchSeconds;

        /// <summary>飞行速度（格/s；0=BattleMetrics.ProjectileSpeed 默认——per-skill 规格时轮化）</summary>
        public float Speed;

        /// <summary>判定圆柱直径（0=BattleMetrics.UnitCylinderDiameter 默认）</summary>
        public float Diameter;

        /// <summary>射程上限（格；0=ProjectileRule.MaxRange 默认）</summary>
        public int Range;

        /// <summary>箭矢视觉飞行高度（格=世界单位；0=BattleMetrics.ArrowFlightHeight 默认——纯视觉，
        /// 判定圆柱与世界高度无关；per-skill 时轮 clip.projectileHeight，2026-10-05 对齐动画松弦位）</summary>
        public float Height;

        public ProjectileEffect(string attackerUnitId, ActionData action, int attackPercent,
            BattleCell fromCell, int deltaX, int deltaY,
            float launchSeconds = 0f, float speed = 0f, float diameter = 0f, int range = 0,
            float height = 0f)
        {
            AttackerUnitId = attackerUnitId;
            TargetUnitId = null; // 命中前未定
            Action = action;
            PlayerId = action.playerId;
            FromCell = fromCell;
            DeltaX = deltaX;
            DeltaY = deltaY;
            AttackPercent = attackPercent;
            LaunchSeconds = launchSeconds;
            Speed = speed;
            Diameter = diameter;
            Range = range;
            Height = height;
        }
    }
}
