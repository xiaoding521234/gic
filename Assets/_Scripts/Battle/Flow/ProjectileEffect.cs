using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{
    /// <summary>
    /// 投射物待判定效应（B5 连续判定体系；决策九十五 P3 蓝图 §4.4 弹道连续化）：技能结算阶段只声明
    /// "发射"（发射点厘米坐标/千分瞄准向量/伤害参数），实际命中由 ProjectileResolver 在移动展开后按
    /// 时间轴连续判定（接触立牌圆柱之时、读命中时刻的连续插值位置，docs/18 决策二）。
    /// 不进入效应应用阶段——命中后被替换为 Hit 全套产物。
    /// P3 起：发射点=施法者连续位置（厘米，快照 posX/posZ）；方向=自由角千分向量
    /// （SkillHitResolver.AimVectorOf 单源——十字=向量子集零回归）；速度/射程=厘米单位
    /// （默认 800cm/s / 2400cm，蓝图 §10 数值迁移）；FromCell 保留为派生格（旧协议兜底——
    /// 消散命令的发射格字段给旧客户端播放链）。
    /// </summary>
    public class ProjectileEffect : BattleEffect
    {
        public string AttackerUnitId;

        /// <summary>发射者行动（命中后 Hit 全套效应产出需要；Host 进程内引用，不序列化）</summary>
        public ActionData Action;

        /// <summary>敌我判定参照（行动归属玩家）</summary>
        public string PlayerId;

        /// <summary>发射点 X（厘米，施法者快照连续位置 posX）</summary>
        public int FromXCm;

        /// <summary>发射点 Z（厘米，同上）</summary>
        public int FromZCm;

        /// <summary>飞行方向千分向量（轴向单位向量×1000——SkillHitResolver.AimVectorOf 产出）</summary>
        public int AimX;
        public int AimZ;

        /// <summary>发射点所在格（派生粗粒度——旧协议兜底：消散命令发射格字段）</summary>
        public BattleCell FromCell;

        /// <summary>每发攻击百分比（时轮 B-S1 起逐发独立判定——多段不再合并；无时轮兜底=旧合并值）</summary>
        public int AttackPercent;

        /// <summary>发射时刻（秒，相对段起点；**释放速度已除**=时轮 clip.startTime÷releaseRate
        /// （蓝图 §4.3——攻速快发射提前）；0=立即发射）</summary>
        public float LaunchSeconds;

        /// <summary>飞行速度（厘米/秒；0=BattleMetrics.ProjectileSpeedCmPerSecond 默认 800——per-skill 规格时轮化）</summary>
        public int SpeedCm;

        /// <summary>判定圆柱直径（世界单位=格；0=BattleMetrics.UnitCylinderDiameter 默认 0.5——
        /// 与 BattleMetrics.CylinderDiameterOf 同族 float，判定内部×CellSizeCm 换厘米）</summary>
        public float Diameter;

        /// <summary>射程上限（厘米；0=ProjectileRule.MaxRange 默认 2400）</summary>
        public int RangeCm;

        /// <summary>命中伤害范围真圆半径（厘米，SkillParamKey.AoERadius——蓝图 §4.2；0=单体
        /// 仅接触命中者，取代旧「同格成员聚合」方形域）</summary>
        public int AoeRadiusCm;

        /// <summary>箭矢视觉飞行高度（格=世界单位；0=BattleMetrics.ArrowFlightHeight 默认——纯视觉，
        /// 判定圆柱与世界高度无关；per-skill 时轮 clip.projectileHeight，2026-10-05 对齐动画松弦位）</summary>
        public float Height;

        public ProjectileEffect(string attackerUnitId, ActionData action, int attackPercent,
            int fromXCm, int fromZCm, Vector2Int aim,
            float launchSeconds = 0f, int speedCm = 0, float diameter = 0f, int rangeCm = 0,
            int aoeRadiusCm = 0, float height = 0f)
        {
            AttackerUnitId = attackerUnitId;
            TargetUnitId = null; // 命中前未定
            Action = action;
            PlayerId = action.playerId;
            FromXCm = fromXCm;
            FromZCm = fromZCm;
            AimX = aim.x;
            AimZ = aim.y;
            FromCell = BattleCell.FromCm(fromXCm, fromZCm);
            AttackPercent = attackPercent;
            LaunchSeconds = launchSeconds;
            SpeedCm = speedCm;
            Diameter = diameter;
            RangeCm = rangeCm;
            AoeRadiusCm = aoeRadiusCm;
            Height = height;
        }
    }
}
