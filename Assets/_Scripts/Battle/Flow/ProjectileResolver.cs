using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 投射物连续命中判定（B5 连续判定体系，docs/active/22 §11 + docs/18 决策二）：
    /// 命中 = 投射物轨迹接触首个敌方立牌**圆柱**之时，读**命中时刻的连续插值位置**（移动中可被中途命中，
    /// 所见即所得）；命中点/命中格由 Host 判定后随命令下发（千分定点），勿由双端各自推算。
    ///
    /// 时间轴：片内行动同 t=0 起跑（快照结算=真同时）；移动速度=BattleMetrics 逻辑常量；
    /// 投射物速度/体积/射程=BattleMetrics 默认（时轮 B-S1 起 per-skill 可覆写），
    /// 投射物自发射时刻起存在（时轮前摇=发射时刻偏移，发射前不参与判定）。
    /// 只有"位置"读命中时刻；HP/附着/Buff 等状态仍读片前快照（同片并发基石不变，docs/11 销案）。
    /// 虚空格截断弹道（对应旧逐格扫描的虚空消散）；无接触时产出消散 Effect 命令（客户端播飞至尽头）。
    /// 枚举序：接触时刻严格平局按 unitId 升序先到先得（docs/active/22 §1 枚举序铁律）。
    /// 与 B4 格级近似的差异：同格堆叠敌方（贴脸）发射即接触命中（旧实现从相邻格起扫描、不打同格）。
    /// </summary>
    public static class ProjectileResolver
    {
        /// <summary>
        /// 把 effects 中的全部 ProjectileEffect（发射声明）替换为命中产物（Damage/Buff/附着全套）；
        /// 无接触（虚空截断或 24 格上限）时向 vanishes 追加消散 Effect 命令
        /// （sliceIndex/indexInSlice 留空，由调用方回填）。必须在同片移动展开
        /// （MovementResolver.Resolve）之后调用——投射物判定读移动者完整路径。
        /// </summary>
        public static void Resolve(BattleSimState sim, BattleSnapshot sliceSnapshot,
            List<BattleEffect> effects, List<MoveActionState> movers, List<BattleCommand> vanishes)
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

            // mover 路径索引（连续位置推算用）
            var moverPaths = new Dictionary<string, List<BattleCell>>();
            if (movers != null)
            {
                foreach (var mover in movers)
                    moverPaths[mover.UnitId] = mover.Path;
            }

            foreach (var projectile in projectiles)
            {
                // 时轮（B-S1）：per-skill 投射物规格 + 发射时刻偏移（前摇）——0 值回落 BattleMetrics 默认
                float speed = projectile.Speed > 0f ? projectile.Speed : BattleMetrics.ProjectileSpeed;
                float radius = (projectile.Diameter > 0f ? projectile.Diameter : BattleMetrics.UnitCylinderDiameter) * 0.5f;
                int maxRange = projectile.Range > 0 ? projectile.Range : ProjectileRule.MaxRange;
                float launch = projectile.LaunchSeconds;
                float maxT = launch + (maxRange + 0.5f) / speed;
                var fromCenter = CellCenter(projectile.FromCell);
                var dir = new Vector2(projectile.DeltaX, projectile.DeltaY).normalized;
                float voidT = VoidBoundaryTime(sim, projectile, maxT, speed, maxRange, launch);

                // 敌方按 unitId 升序（含尸体——尸体完全算判定，docs/05 §5.4；先到先得取最早接触）
                var enemies = new List<UnitState>();
                foreach (var state in sliceSnapshot.units)
                    if (state.playerId != projectile.PlayerId)
                        enemies.Add(state);
                enemies.Sort((a, b) => string.CompareOrdinal(a.unitId, b.unitId));

                float hitT = float.MaxValue;
                string hitUnitId = null;
                foreach (var enemy in enemies)
                {
                    float t = FirstContactTime(projectile, enemy, moverPaths, maxT, speed, radius);
                    if (t < hitT)
                    {
                        hitT = t;
                        hitUnitId = enemy.unitId;
                    }
                }

                if (hitUnitId == null || hitT >= voidT)
                {
                    // 消散：飞至虚空边界或射程上限。消散点定点下发（hitX/hitY 千分），
                    // value=最大飞行格数作兜底（客户端 hitX/hitY 为 0 时按方向飞 value 格）；
                    // launchMs=发射时刻（时轮 B-S1——客户端延迟起飞）
                    float vanishT = Mathf.Min(voidT, maxT);
                    var vanishPoint = fromCenter + dir * (speed * (vanishT - launch));
                    var vanishCmd = BattleCommand.Effect(projectile.AttackerUnitId, 0, 0,
                        BattleCommand.EffectKindProjectileVanish,
                        (int)projectile.Action.direction, projectile.FromCell, maxRange);
                    vanishCmd.hitX = Mathf.RoundToInt(vanishPoint.x * 1000f);
                    vanishCmd.hitY = Mathf.RoundToInt(vanishPoint.y * 1000f);
                    vanishCmd.launchMs = Mathf.RoundToInt(launch * 1000f);
                    vanishes.Add(vanishCmd);
                    continue;
                }

                // 命中点 = 接触时刻投射物中心位置（停在圆柱边缘，视觉即判定）
                var hitPoint = fromCenter + dir * (speed * (hitT - launch));

                // 格 AoE：命中时刻全体敌方连续位置所在格 == 命中者所在格（接触判定与效果作用域解耦，
                // docs/active/22 §11；堆叠同心下与旧"格内全中"结果一致，向后兼容）
                var hitState = FindState(enemies, hitUnitId);
                var hitCell = CellOf(PositionAt(hitState, moverPaths, hitT));
                foreach (var enemy in enemies)
                {
                    if (!CellOf(PositionAt(enemy, moverPaths, hitT)).Equals(hitCell)) continue;
                    // hitT=接触时刻随效应下发（「命中时才给」2026-09-25：战技获能/命中治疗到点应用）
                    effects.AddRange(SkillHitResolver.Hit(sim, projectile.Action, sliceSnapshot, enemy.unitId,
                        projectile.AttackPercent, ProjectileRule.LineDelivery, projectile.FromCell,
                        hitPoint.x, hitPoint.y, launch, hitT));
                }
            }
        }

        // ==================== 内部 ====================

        /// <summary>格 c 的连续格心坐标（格 c 区间 [c, c+1]，心 = c+0.5；与 BattleBoard.CellToWorld 同系数）</summary>
        private static Vector2 CellCenter(BattleCell c) => new Vector2(c.x + 0.5f, c.y + 0.5f);

        /// <summary>连续格坐标 → 归属格（floor）</summary>
        private static BattleCell CellOf(Vector2 g)
            => new BattleCell(Mathf.FloorToInt(g.x), Mathf.FloorToInt(g.y));

        private static UnitState FindState(List<UnitState> enemies, string unitId)
        {
            foreach (var state in enemies)
                if (state.unitId == unitId) return state;
            return null;
        }

        /// <summary>
        /// 单位在 t 时刻的连续位置：mover 沿路径逐格插值（每格 MoveStepSeconds，超出路径总时长=停在终点）；
        /// 其余恒=快照格心
        /// </summary>
        private static Vector2 PositionAt(UnitState state, Dictionary<string, List<BattleCell>> moverPaths, float t)
        {
            if (moverPaths.TryGetValue(state.unitId, out var path) && path.Count > 1)
            {
                float stepT = BattleMetrics.MoveStepSeconds;
                int seg = Mathf.FloorToInt(t / stepT);
                if (seg >= path.Count - 1)
                    return CellCenter(path[path.Count - 1]); // 已停在终点
                float frac = (t - seg * stepT) / stepT;
                return Vector2.Lerp(CellCenter(path[seg]), CellCenter(path[seg + 1]), frac);
            }
            return CellCenter(state.position);
        }

        /// <summary>
        /// 投射物与单位圆柱的最早接触时刻（无接触=float.MaxValue）。
        /// 单位轨迹分段线性（mover=移动段+静止尾段；非 mover=全程静止段），
        /// 段内两者速度恒定 → 相对位移线性 → |A+V·τ|²=r² 二次方程求小根（最早进入时刻）。
        /// 时轮 B-S1：t∈[0, launch) 投射物不存在（前摇期），该窗口段直接跳过。
        /// </summary>
        private static float FirstContactTime(ProjectileEffect projectile, UnitState enemy,
            Dictionary<string, List<BattleCell>> moverPaths, float maxT, float speed, float radius)
        {
            var dir = new Vector2(projectile.DeltaX, projectile.DeltaY).normalized;
            var projVel = dir * speed;
            var fromCenter = CellCenter(projectile.FromCell);
            float launch = projectile.LaunchSeconds;

            // 分段窗口 [t, segEnd]：段内单位速度恒定；逐段求交直到 maxT
            float t = 0f;
            while (t < maxT)
            {
                // 时轮：窗口起点——发射前投射物不存在，首窗直接从 launch 起算
                // （launch 落在单位移动段中时，单位起点位置按窗口起点精确插值）
                float t0 = t >= launch ? t : launch;
                if (t0 >= maxT) break;

                float segEnd;
                Vector2 unitPos;
                Vector2 unitVel;
                if (moverPaths.TryGetValue(enemy.unitId, out var path) && path.Count > 1)
                {
                    float stepT = BattleMetrics.MoveStepSeconds;
                    int totalSteps = path.Count - 1;
                    if (t0 >= totalSteps * stepT)
                    {
                        segEnd = maxT; // 静止尾段：停在终点
                        unitPos = CellCenter(path[totalSteps]);
                        unitVel = Vector2.zero;
                    }
                    else
                    {
                        int seg = Mathf.FloorToInt(t0 / stepT + 1e-4f);
                        segEnd = Mathf.Min((seg + 1) * stepT, maxT);
                        float frac = Mathf.Clamp01((t0 - seg * stepT) / stepT); // t0 可能落在段中（launch 非步进整倍数）
                        unitPos = Vector2.Lerp(CellCenter(path[seg]), CellCenter(path[seg + 1]), frac);
                        unitVel = (CellCenter(path[seg + 1]) - CellCenter(path[seg])) / stepT;
                    }
                }
                else
                {
                    segEnd = maxT;
                    unitPos = CellCenter(enemy.position);
                    unitVel = Vector2.zero;
                }

                var rel = fromCenter + projVel * (t0 - launch) - unitPos; // 窗口起点相对位移（投射物自 launch 起算）
                var relVel = projVel - unitVel;                            // 窗口内相对速度

                float c = Vector2.Dot(rel, rel) - radius * radius;
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

                t = segEnd;
            }
            return float.MaxValue;
        }

        /// <summary>
        /// 弹道虚空截断时刻：中心到达首个虚空格近边界之时（中心越过 k−0.5 距离=进入第 k 格）；
        /// 无虚空=射程上限时刻（时轮 B-S1：per-skill 射程/速度 + 发射时刻偏移）
        /// </summary>
        private static float VoidBoundaryTime(BattleSimState sim, ProjectileEffect projectile,
            float maxT, float speed, int maxRange, float launch)
        {
            for (int k = 1; k <= maxRange; k++)
            {
                var cell = new BattleCell(projectile.FromCell.x + projectile.DeltaX * k,
                    projectile.FromCell.y + projectile.DeltaY * k);
                if (!sim.Map.HasTile(cell.x, cell.y))
                    return launch + (k - 0.5f) / speed;
            }
            return maxT;
        }
    }
}
