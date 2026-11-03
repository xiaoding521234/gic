using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 投射物连续命中判定（B5 连续判定体系，docs/22 §11 + docs/18 决策二；**决策九十五 P3 全面 cm 化**
    /// ——蓝图 §4.4 弹道连续化）：
    /// 命中 = 投射物轨迹接触首个敌方立牌**圆柱**之时，读**命中时刻的连续插值位置**（移动中可被中途命中，
    /// 所见即所得）；命中点/命中格由 Host 判定后随命令下发（hitX/hitY=毫米千分定点=cm×10——与旧
    /// 「千分格」数值自洽：1 格=100cm=1000mm，客户端 ÷1000 换算零改动），勿由双端各自推算。
    ///
    /// P3 空间模型（蓝图 §3/§4）：发射点=施法者连续位置（厘米，ProjectileEffect.FromXCm/ZCm）；
    /// 方向=自由角千分向量（AimX/AimZ）；速度/射程=厘米单位（默认 800cm/s、2400cm）；敌方轨迹=
    /// ①即时制 tick 移动者（sim.ActiveMovers——A* 剩余 waypoints 外推，含近似：飞行途中路径变更不重算/
    /// 被挡后实际停更早=P6 观察项）②旧格轨移动者（legacyMovers——P5 旧轨退役前兜底）③静止（快照
    /// 连续位置 posX/posZ）。命中域=AoERadius 真圆（命中点为中心半径内敌方；0=单体仅接触命中者——
    /// 取代旧「同格成员聚合」方形域，蓝图 §4.2）。
    ///
    /// 时间轴：施放时刻 t=0 起算；投射物自发射时刻（时轮前摇÷释放速度）起存在，发射前不参与判定。
    /// 只有"位置"读命中时刻；HP/Buff 等状态仍读片前快照（同片并发基石，docs/11 销案）；
    /// 附着读片内编译视图（反应消耗/新附着随编译序对同片后续命中可见——2026-10-01 双蒸发修复）。
    /// 虚空截断（对应旧逐格扫描的虚空消散——沿线半格采样首个无 tile 采样点）；无接触时产出消散
    /// Effect 命令（客户端播飞至尽头）。
    /// 编译序=命中时刻序（2026-10-01 WYSIWYG 拍板，docs/18 决策二十一）：同段全部投射物先求交收集、
    /// 按 (hitT, 施法者 unitId) 排序后逐发编译——双单位多箭按**到达顺序**消耗附着（安柏近+甘雨远
    /// =A1→G1→A2→G2）；同刻平局按 unitId 升序先到先得（枚举序铁律保留给真同时）。
    /// </summary>
    public static class ProjectileResolver
    {
        /// <summary>单位轨迹段（cm 域、float 求交——段内位置恒速线性；与旧 B5 求交数学同族，
        /// 单机同平台确定性）</summary>
        private struct TrajSeg
        {
            public float tStart, tEnd; // 窗口（秒，施放时刻起算）
            public float px, pz;       // 段起点位置（cm）
            public float vx, vz;       // 段内速度（cm/s）
        }

        /// <summary>
        /// 把 effects 中的全部 ProjectileEffect（发射声明）替换为命中产物（Damage/Buff/附着全套）；
        /// 无接触（虚空截断或射程上限）时向 vanishes 追加消散 Effect 命令
        /// （sliceIndex/indexInSlice 留空，由调用方回填）。必须在移动展开之后调用
        /// ——投射物判定读移动者完整轨迹（即时制 tick mover + 旧格轨 mover）。
        /// </summary>
        public static void Resolve(BattleSimState sim, BattleSnapshot sliceSnapshot,
            List<BattleEffect> effects, List<MoveActionState> legacyMovers, List<BattleCommand> vanishes)
        {
            var projectiles = new List<ProjectileEffect>();
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                if (effects[i] is ProjectileEffect projectile)
                {
                    projectiles.Add(projectile);
                    effects.RemoveAt(i);
                }
            }
            if (projectiles.Count == 0) return;
            projectiles.Reverse(); // 还原产出序（=行动枚举序）

            // 轨迹索引（unitId → 轨迹段表）：即时制 tick 移动者/旧格轨移动者/静止，三者归一
            var trajectories = new Dictionary<string, List<TrajSeg>>();
            foreach (var mover in sim.ActiveMovers)
            {
                if (mover.Unit == null || string.IsNullOrEmpty(mover.UnitId)) continue;
                trajectories[mover.UnitId] = BuildCmMoverTrajectory(sim, mover);
            }
            if (legacyMovers != null)
            {
                foreach (var mover in legacyMovers)
                    if (!string.IsNullOrEmpty(mover.UnitId) && !trajectories.ContainsKey(mover.UnitId))
                        trajectories[mover.UnitId] = BuildLegacyTrajectory(mover);
            }

            // 两遍法·命中时刻序（2026-10-01 WYSIWYG 拍板，docs/18 决策二十一）：先求交收集全部接触
            // 时刻，排序后逐发编译——双单位多箭按到达顺序消耗附着（A1→G1→A2→G2）。
            // 求交彼此独立（投射物互不遮挡、尸体也算判定、目标位置只读移动轨迹）——先求交不改
            // 任何单发的命中结果，只改编译序；seq=产出序尾键（List.Sort 不稳定，同刻同施法者兜底）
            var contacts = new List<(float hitT, string attackerUnitId, int seq, ProjectileEffect projectile,
                float launch, Vector2 hitPointCm, List<UnitState> members)>();
            int sequence = 0;
            foreach (var projectile in projectiles)
            {
                int speedCm = projectile.SpeedCm > 0 ? projectile.SpeedCm : BattleMetrics.ProjectileSpeedCmPerSecond;
                int maxRangeCm = projectile.RangeCm > 0 ? projectile.RangeCm : ProjectileRule.MaxRange;
                float launch = projectile.LaunchSeconds;
                float maxT = launch + maxRangeCm / (float)speedCm;
                var from = new Vector2(projectile.FromXCm, projectile.FromZCm);
                var aimDir = new Vector2(projectile.AimX, projectile.AimZ).normalized;
                float voidT = VoidBoundaryTime(sim, projectile, aimDir, maxRangeCm, speedCm, launch, maxT);

                // 敌方按 unitId 升序（含尸体——尸体完全算判定，docs/05 §5.4；先到先得取最早接触）。
                // 敌我=TeamType 口径（2026-09-25 三轮审查 C2：2v2 下不再把队友单位算进命中筛选）
                var casterTeam = sim.GetTeamOf(projectile.PlayerId);
                var enemies = new List<UnitState>();
                foreach (var state in sliceSnapshot.MainWorldUnits()) // 主世界域单源（灵魂维度隔离判据收口）
                    if ((TeamType)state.team != casterTeam)
                        enemies.Add(state);
                enemies.Sort((a, b) => string.CompareOrdinal(a.unitId, b.unitId));

                float hitT = float.MaxValue;
                string hitUnitId = null;
                foreach (var enemy in enemies)
                {
                    // per-enemy 受击半径（协议核心批 2026-09-29）：技能声明 hitDiameter 覆写优先
                    //（per-skill 判定圆柱，替代语义保持既有行为）；否则按该单位自身受击圆柱
                    //（默认 0.5 格；协议核心 0.8——大目标接触距离更远=易命中，攻城手感）
                    float radiusCm = (projectile.Diameter > 0f ? projectile.Diameter
                        : BattleMetrics.CylinderDiameterOf(enemy)) * CellMetrics.CellSizeCm * 0.5f;
                    if (!trajectories.TryGetValue(enemy.unitId, out var traj))
                        traj = BuildStaticTrajectory(enemy);
                    float t = FirstContactTime(traj, from, aimDir * speedCm, maxT, launch, radiusCm);
                    if (t < hitT)
                    {
                        hitT = t;
                        hitUnitId = enemy.unitId;
                    }
                }

                if (hitUnitId == null || hitT >= voidT)
                {
                    // 消散：飞至虚空边界或射程上限。消散点定点下发（hitX/hitY=毫米=cm×10——与旧
                    // 千分格数值自洽，客户端 ÷1000 零改动），value=最大飞行厘米数作兜底
                    //（客户端 hitX/hitY 为 0 时按方向飞——语义随 P3 改 cm，正常路径恒有定点不触发）；
                    // launchMs=发射时刻（客户端延迟起飞）
                    float vanishT = Mathf.Min(voidT, maxT);
                    var vanishPoint = from + aimDir * (speedCm * (vanishT - launch));
                    var vanishCmd = BattleCommand.Effect(projectile.AttackerUnitId, 0, 0,
                        BattleCommand.EffectKindProjectileVanish,
                        (int)projectile.Action.direction, projectile.FromCell, maxRangeCm);
                    vanishCmd.hitX = Mathf.RoundToInt(vanishPoint.x * 10f);
                    vanishCmd.hitY = Mathf.RoundToInt(vanishPoint.y * 10f);
                    vanishCmd.launchMs = Mathf.RoundToInt(launch * 1000f);
                    // 投射物元素（箭矢染色单源 2026-09-28）：与命中 Damage.metadata 同口径——
                    // 丘丘人借凯亚霜袭时消散箭也是冰色，非施法者物理灰
                    vanishCmd.reactionKind = (int)SkillHitResolver.ResolveProjectileElement(sim, projectile.Action);
                    // 箭矢视觉高度（2026-10-05 对齐动画松弦位）：发射声明透传千分下发，客户端消散箭同高
                    vanishCmd.arrowHeightY = Mathf.RoundToInt(projectile.Height * 1000f);
                    vanishes.Add(vanishCmd);
                    continue;
                }

                // 命中点 = 接触时刻投射物中心位置（停在圆柱边缘，视觉即判定）
                var hitPoint = from + aimDir * (speedCm * (hitT - launch));

                // 真圆 AoE 域（蓝图 §4.2）：命中点为中心、AoERadiusCm 半径内的敌方（读各自命中时刻
                // 位置——移动中的敌人按命中瞬间位置判定）；0=单体（仅接触命中者）。取代旧「同格成员
                // 聚合」方形域
                var members = new List<UnitState>();
                if (projectile.AoeRadiusCm > 0)
                {
                    long aoeSq = (long)projectile.AoeRadiusCm * projectile.AoeRadiusCm;
                    foreach (var enemy in enemies)
                    {
                        var p = PositionOfAt(trajectories, enemy, hitT);
                        long dx = Mathf.RoundToInt(p.x) - Mathf.RoundToInt(hitPoint.x);
                        long dz = Mathf.RoundToInt(p.y) - Mathf.RoundToInt(hitPoint.y);
                        if (dx * dx + dz * dz <= aoeSq) members.Add(enemy);
                    }
                }
                else
                {
                    var hitState = FindState(enemies, hitUnitId);
                    if (hitState != null) members.Add(hitState);
                }
                contacts.Add((hitT, projectile.AttackerUnitId, sequence++, projectile, launch, hitPoint, members));
            }

            // 到达序编译：命中时刻升序，平局按施法者 unitId 升序（真同时=枚举序铁律），再按产出序
            contacts.Sort((a, b) =>
            {
                int byTime = a.hitT.CompareTo(b.hitT);
                if (byTime != 0) return byTime;
                int byUnit = string.CompareOrdinal(a.attackerUnitId, b.attackerUnitId);
                return byUnit != 0 ? byUnit : a.seq.CompareTo(b.seq);
            });

            foreach (var contact in contacts)
            {
                foreach (var enemy in contact.members)
                {
                    // hitT=接触时刻随效应下发（「命中时才给」2026-09-25：战技获能/命中治疗到点应用）
                    var hitEffects = SkillHitResolver.Hit(sim, contact.projectile.Action, sliceSnapshot, enemy.unitId,
                        contact.projectile.AttackPercent, ProjectileRule.LineDelivery, contact.projectile.FromCell,
                        contact.hitPointCm.x, contact.hitPointCm.y, contact.launch, contact.hitT);
                    // 箭矢视觉高度透传（2026-10-05 对齐动画松弦位）：发射声明 → 命中产物，
                    // 随 Damage 命令千分下发（纯视觉参数，判定圆柱与世界高度无关零影响）
                    foreach (var e in hitEffects)
                        if (e is DamageEffect dmg) dmg.ProjectileHeightY = contact.projectile.Height;
                    effects.AddRange(hitEffects);
                }
            }
        }

        // ==================== 轨迹构造 ====================

        /// <summary>即时制 tick 移动者轨迹：起点=当前连续位置，沿剩余 waypoints 折线推进
        ///（速度=SpeedCmPerSecond 恒速）；走完=终点静止尾段。近似口径：A* 路径变更不重算、
        /// 被挡停更早不预知（P6 观察项）——单机 AI 局精度足</summary>
        private static List<TrajSeg> BuildCmMoverTrajectory(BattleSimState sim, RTSMoveState mover)
        {
            var segs = new List<TrajSeg>();
            var posCm = sim.GetPosCm(mover.Unit);
            float x = posCm.x, z = posCm.y;
            float t = 0f;
            int idx = mover.WaypointIndex;
            float speed = Mathf.Max(mover.SpeedCmPerSecond, 1);
            while (idx < mover.WaypointsCm.Count)
            {
                var wp = mover.WaypointsCm[idx];
                float dx = wp.x - x, dz = wp.z - z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                if (dist < 0.01f) { x = wp.x; z = wp.z; idx++; continue; }
                float dur = dist / speed;
                segs.Add(new TrajSeg
                {
                    tStart = t, tEnd = t + dur,
                    px = x, pz = z,
                    vx = dx / dur, vz = dz / dur,
                });
                t += dur;
                x = wp.x; z = wp.z;
                idx++;
            }
            // 终点静止尾段（时长∞→取大值 600 秒封顶）
            segs.Add(new TrajSeg { tStart = t, tEnd = 600f, px = x, pz = z, vx = 0f, vz = 0f });
            return segs;
        }

        /// <summary>旧格轨移动者轨迹（P5 旧轨退役前兜底）：沿格路径逐格心推进（每格
        /// BattleMetrics.MoveStepSeconds——与旧播放器补间同源节拍）</summary>
        private static List<TrajSeg> BuildLegacyTrajectory(MoveActionState mover)
        {
            var segs = new List<TrajSeg>();
            if (mover.Path == null || mover.Path.Count == 0) return segs;
            float stepT = BattleMetrics.MoveStepSeconds;
            float speed = CellMetrics.CellSizeCm / stepT; // cm/s（1 格/0.18s）
            var first = mover.Path[0];
            float x = first.CenterXCm, z = first.CenterYCm;
            float t = 0f;
            for (int i = 1; i < mover.Path.Count; i++)
            {
                var cell = mover.Path[i];
                float dx = cell.CenterXCm - x, dz = cell.CenterYCm - z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                if (dist < 0.01f) continue;
                float dur = dist / speed;
                segs.Add(new TrajSeg
                {
                    tStart = t, tEnd = t + dur,
                    px = x, pz = z,
                    vx = dx / dur, vz = dz / dur,
                });
                t += dur;
                x = cell.CenterXCm; z = cell.CenterYCm;
            }
            segs.Add(new TrajSeg { tStart = t, tEnd = 600f, px = x, pz = z, vx = 0f, vz = 0f });
            return segs;
        }

        /// <summary>静止轨迹（快照连续位置；0 回落格心——UnitStatePosExtensions 同口径防御）</summary>
        private static List<TrajSeg> BuildStaticTrajectory(UnitState state)
        {
            return new List<TrajSeg>
            {
                new TrajSeg { tStart = 0f, tEnd = 600f, px = state.PosXCm(), pz = state.PosZCm(), vx = 0f, vz = 0f }
            };
        }

        /// <summary>单位在 t 时刻位置（轨迹缺失=静止回落；段表外=尾段静止位置）</summary>
        private static Vector2 PositionOfAt(Dictionary<string, List<TrajSeg>> trajectories, UnitState state, float t)
        {
            if (trajectories.TryGetValue(state.unitId, out var segs) && segs.Count > 0)
            {
                foreach (var seg in segs)
                {
                    if (t < seg.tStart || t > seg.tEnd) continue;
                    return new Vector2(seg.px + seg.vx * (t - seg.tStart), seg.pz + seg.vz * (t - seg.tStart));
                }
                var last = segs[segs.Count - 1];
                return new Vector2(last.px, last.pz);
            }
            return new Vector2(state.PosXCm(), state.PosZCm());
        }

        private static UnitState FindState(List<UnitState> enemies, string unitId)
        {
            foreach (var state in enemies)
                if (state.unitId == unitId) return state;
            return null;
        }

        /// <summary>
        /// 投射物与单位圆柱的最早接触时刻（无接触=float.MaxValue）——旧 B5 二次方程求交数学
        /// 直译到 cm 域：逐轨迹段开窗（段内两者速度恒定 → 相对位移线性 → |A+V·τ|²=r² 二次方程求
        /// 小根=最早进入时刻）；t∈[0, launch) 投射物不存在（前摇期），该窗口段直接跳过。
        /// 窗口起点取段与 launch 的较晚者（单位起点位置按窗口起点精确插值）。
        /// </summary>
        private static float FirstContactTime(List<TrajSeg> traj, Vector2 fromCm, Vector2 projVel,
            float maxT, float launch, float radiusCm)
        {
            foreach (var seg in traj)
            {
                float t0 = seg.tStart >= launch ? seg.tStart : launch;
                if (t0 >= maxT) break;
                float segEnd = Mathf.Min(seg.tEnd, maxT);
                if (t0 >= segEnd) continue; // 本段全在前摇窗口内（launch 尚未到）——下段再试

                var unitPos = new Vector2(seg.px + seg.vx * (t0 - seg.tStart), seg.pz + seg.vz * (t0 - seg.tStart));
                var rel = fromCm + projVel * (t0 - launch) - unitPos; // 窗口起点相对位移（投射物自 launch 起算）
                var relVel = projVel - new Vector2(seg.vx, seg.vz);    // 窗口内相对速度

                float c = Vector2.Dot(rel, rel) - radiusCm * radiusCm;
                if (c <= 0f)
                    return t0; // 窗口起点已在圆柱内（含发射即贴脸同格）

                float a = Vector2.Dot(relVel, relVel);
                if (a > 0f)
                {
                    float b = 2f * Vector2.Dot(rel, relVel);
                    float disc = b * b - 4f * a * c;
                    if (disc >= 0f)
                    {
                        float hitT = t0 + (-b - Mathf.Sqrt(disc)) / (2f * a); // 小根=最早进入
                        if (hitT >= t0 && hitT <= segEnd + 1e-5f)
                            return hitT;
                    }
                }
            }
            return float.MaxValue;
        }

        /// <summary>
        /// 弹道虚空截断时刻：沿瞄准向量半格采样（50cm 步——格对角 √2/2≈70.7cm 保证无漏格），
        /// 首个无 tile 采样点=进入虚空之时；无虚空=射程上限时刻。
        /// （旧实现=逐格扫描中心越过 k−0.5 格——采样口径为 cm 域等价直译）
        /// </summary>
        private static float VoidBoundaryTime(BattleSimState sim, ProjectileEffect projectile, Vector2 aimDir,
            int maxRangeCm, int speedCm, float launch, float maxT)
        {
            const int stepCm = 50;
            int steps = maxRangeCm / stepCm;
            for (int i = 1; i <= steps; i++)
            {
                int dCm = i * stepCm;
                int x = projectile.FromXCm + (int)(aimDir.x * dCm);
                int z = projectile.FromZCm + (int)(aimDir.y * dCm);
                var cell = BattleCell.FromCm(x, z);
                if (!sim.Map.HasTile(cell.x, cell.y))
                    return launch + dCm / (float)speedCm;
            }
            return maxT;
        }
    }
}
