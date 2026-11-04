using System;
using System.Collections.Generic;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// tile A* 寻路（决策九十五 P2，蓝图 §3.3——业界选型论证见 48 §3.3：本作单位量级十几~几十，
    /// grid A* 优于 flow field/NavMesh；动态障碍重建成本≈0）。8 向寻路（直线代价 10/斜向 14，
    /// 整数化 √2×10），阻塞=不可走 tile（按 ForceType）+尸体格；输出=格心厘米路径点序列，
    /// 末点=精确目标点（自由落点）。确定性纪律：闭集遍历按序、零随机。
    /// </summary>
    public static class RTSPathfinder
    {
        // 8 向邻格（固定序=确定性）：直 4 向在前、斜 4 向在后；代价直 10 斜 14
        private static readonly (int dx, int dy, int cost)[] Neighbors =
        {
            (1, 0, 10), (-1, 0, 10), (0, 1, 10), (0, -1, 10),
            (1, 1, 14), (1, -1, 14), (-1, 1, 14), (-1, -1, 14),
        };

        /// <summary>
        /// 寻路：起点格→目标格 A*（目标不可走时自动钳到最近可走格——A* 展开域内最近可达）；
        /// 返回厘米 waypoint 序列（格心逐点+末点=精确目标 cm；空=起终点同格/不可达）。
        /// 尸体格=硬阻塞（蓝图 §3.2：尸体=死亡位静态碰撞圆——占格近似）；友军不阻塞路径（可穿）。
        /// </summary>
        public static List<Vector2IntCm> FindPath(BattleSimState sim, int fromXCm, int fromZCm,
            int toXCm, int toZCm, ForceType forceType)
        {
            var start = BattleCell.FromCm(fromXCm, fromZCm);
            var goal = BattleCell.FromCm(toXCm, toZCm);
            if (start.Equals(goal))
            {
                // 同格直达：末点=精确目标（格内自由落点）
                var direct = new List<Vector2IntCm> { new Vector2IntCm(toXCm, toZCm) };
                return direct;
            }

            var map = sim.Map;
            if (!map.HasTile(goal.x, goal.y) || !map.IsPassable(goal.x, goal.y, forceType))
            {
                // 目标格不可走：沿起点→目标方向回退找最近可走格（简化钳制——距离目标最近的合法落点）
                goal = ClampToNearestPassable(sim, start, goal, forceType);
                if (goal.Equals(start))
                {
                    var fallback = new List<Vector2IntCm> { new Vector2IntCm(fromXCm, fromZCm) };
                    return fallback;
                }
            }

            // 尸体格阻塞集（确定性：unitId 升序收集）
            var corpseCells = new HashSet<string>();
            var unitIds = new List<string>(sim.Units.Keys);
            unitIds.Sort(StringComparer.Ordinal);
            foreach (var unitId in unitIds)
            {
                var unit = sim.Units[unitId];
                if (unit != null && BattleSimState.IsDead(unit))
                    corpseCells.Add($"{sim.GetPosition(unit).x},{sim.GetPosition(unit).y}");
            }

            // A*（open=列表线性取最小——单位量级小，勿造堆）
            var open = new List<Node>();
            var closed = new HashSet<string>();
            var best = new Dictionary<string, Node>();
            var startNode = new Node { cell = start, g = 0, h = Heuristic(start, goal) };
            open.Add(startNode);
            best[Key(start)] = startNode;

            Node goalNode = null;
            int guard = 0;
            while (open.Count > 0 && guard++ < 10000)
            {
                // 取 f 最小（tie=g 最小→确定性）
                int minIndex = 0;
                for (int i = 1; i < open.Count; i++)
                    if (open[i].F < open[minIndex].F
                        || (open[i].F == open[minIndex].F && open[i].g < open[minIndex].g))
                        minIndex = i;
                var current = open[minIndex];
                open.RemoveAt(minIndex);
                closed.Add(Key(current.cell));

                if (current.cell.Equals(goal))
                {
                    goalNode = current;
                    break;
                }

                foreach (var (dx, dy, cost) in Neighbors)
                {
                    var next = new BattleCell(current.cell.x + dx, current.cell.y + dy);
                    if (!map.HasTile(next.x, next.y)) continue;
                    if (!map.IsPassable(next.x, next.y, forceType)) continue;
                    if (corpseCells.Contains($"{next.x},{next.y}")) continue; // 尸体格硬阻塞
                    if (dx != 0 && dy != 0)
                    {
                        // 斜向防穿角：两相邻直格都不可走则不许斜穿
                        if (!map.IsPassable(current.cell.x + dx, current.cell.y, forceType)
                            && !map.IsPassable(current.cell.x, current.cell.y + dy, forceType)) continue;
                    }
                    if (closed.Contains(Key(next))) continue;

                    int ng = current.g + cost;
                    string nKey = Key(next);
                    if (best.TryGetValue(nKey, out var existing) && existing.g <= ng) continue;

                    var node = new Node { cell = next, g = ng, h = Heuristic(next, goal), parent = current };
                    best[nKey] = node;
                    open.Add(node);
                }
            }

            if (goalNode == null)
            {
                // 不可达（被地形/尸体围死）：原地待命
                var stuck = new List<Vector2IntCm> { new Vector2IntCm(fromXCm, fromZCm) };
                return stuck;
            }

            // 回溯路径→waypoint（格心 cm；末点替换为精确目标 cm）
            var waypoints = new List<Vector2IntCm>();
            var pathCells = new List<BattleCell>();
            for (var n = goalNode; n != null; n = n.parent)
            {
                pathCells.Add(n.cell);
                waypoints.Insert(0, new Vector2IntCm(n.cell.CenterXCm, n.cell.CenterYCm));
            }
            if (waypoints.Count > 0)
            {
                // P5-D 零推进修复：首点=精确起点（对称于末点=精确目标）。旧形态首点=起点格心——
                // AI 每 0.5s 重决策替换 mover 时，新 mover 先回走格心再前进，单位围绕格心震荡、
                // 净推进≈0（双 AI 观察局实锤：8 秒净位移 1~48cm）；首点改精确起点后重决策=从
                // 当前位连续推进（同格直达/钳制回退的单点路径不经过本段，零影响）
                waypoints[0] = new Vector2IntCm(fromXCm, fromZCm);
                waypoints[waypoints.Count - 1] = new Vector2IntCm(toXCm, toZCm);
            }
            return waypoints;
        }

        /// <summary>直线钳制：起点→目标方向步进取首个可走格（找不到回落起点）</summary>
        private static BattleCell ClampToNearestPassable(BattleSimState sim, BattleCell start, BattleCell goal, ForceType forceType)
        {
            int dx = Math.Sign(goal.x - start.x);
            int dy = Math.Sign(goal.y - start.y);
            var candidate = goal;
            while (sim.Map.HasTile(candidate.x, candidate.y))
            {
                if (sim.Map.IsPassable(candidate.x, candidate.y, forceType)) return candidate;
                if (candidate.x == start.x && candidate.y == start.y) break;
                candidate = new BattleCell(candidate.x - dx, candidate.y - dy);
            }
            return start;
        }

        /// <summary>启发式（八向一致的整数 octile：10×max+4×min，非 admissible 修正版 14-2×10 直斜混合）</summary>
        private static int Heuristic(BattleCell a, BattleCell b)
        {
            int dx = Math.Abs(a.x - b.x);
            int dy = Math.Abs(a.y - b.y);
            return dx > dy ? 10 * dx + 4 * dy : 10 * dy + 4 * dx;
        }

        private static string Key(BattleCell cell) => $"{cell.x},{cell.y}";

        private class Node
        {
            public BattleCell cell;
            public int g;
            public int h;
            public int F => g + h;
            public Node parent;
        }
    }

    /// <summary>厘米坐标点（XZ 平面，决策九十五 P2——避免与 UnityEngine.Vector2Int 语义混淆的轻量载体）</summary>
    public struct Vector2IntCm
    {
        public int x;
        public int z;
        public Vector2IntCm(int x, int z) { this.x = x; this.z = z; }
    }
}
