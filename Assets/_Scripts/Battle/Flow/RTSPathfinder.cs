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

            // 尸体格阻塞集（确定性：unitId 升序收集）——按 tick 缓存（2026-11-06 后期卡顿修复）：
            // 旧实现每次寻路都「复制全单位 id + 排序 + 逐单位拼 "x,y" 字符串」重建集合；后期 AI
            // 每拍上千次寻路（AI 预判 + 执行层 A*）= 纯分配风暴（实测每帧数百 KB 分配的主源之一）
            // 2026-11-07：改用按格索引的 bool 掩码——A* 里「每节点 8 邻居各查一次」不再触发字符串插值
            var corpseMask = sim.CorpseMaskOfTick();

            int w = map.width, h = map.height;
            int size = w * h;
            EnsureBuffers(size);

            // 缓冲重置（复用池；尺寸变化时 EnsureBuffers 已重建）
            System.Array.Fill(_bestG, int.MinValue);
            System.Array.Clear(_closedCells, 0, size);
            _open.Clear();
            _nodeCount = 0;

            int startIdx = start.y * w + start.x;
            int goalIdx = goal.y * w + goal.x;

            // A*（open=列表线性取最小——单位量级小，勿造堆；tie=g 最小→确定性）
            // 语义与旧字符串键实现逐条等价：格可重复入 open（旧 Node 对象多条）、闭集只作剪枝
            // 不阻止重开（旧代码 Add 的返回值被忽略）——勿顺手改成标准 A*，那会改变路径、
            // 破坏行为/回放基线。
            int rootNode = NewNode(startIdx, start.x, start.y, 0, Heuristic(start, goal), -1);
            _bestG[startIdx] = 0;
            _open.Add(rootNode);

            int goalNode = -1;
            int guard = 0;
            while (_open.Count > 0 && guard++ < 10000)
            {
                // 取 f 最小（tie=g 最小→再 tie=先入 open 者）——保持与旧实现的线性扫描逐条等价
                //（曾试过二叉堆：无论比较键怎么设都出现路径差异，判断踩到「同格多次入 open、
                // 堆序与插入序不可交换」的边界，故放弃；本表规模小、O(n²) 代价可接受）
                int minOpenIndex = 0;
                int bestNode = _open[0];
                int bestF = _nodes[bestNode].g + _nodes[bestNode].h;
                for (int i = 1; i < _open.Count; i++)
                {
                    var cand = _nodes[_open[i]];
                    int f = cand.g + cand.h;
                    if (f < bestF || (f == bestF && cand.g < _nodes[bestNode].g))
                    {
                        minOpenIndex = i;
                        bestNode = _open[i];
                        bestF = f;
                    }
                }
                _open.RemoveAt(minOpenIndex);
                var current = _nodes[bestNode];
                _closedCells[current.cell] = true;

                if (current.cell == goalIdx)
                {
                    goalNode = bestNode;
                    break;
                }

                int cx = current.x, cy = current.y;
                for (int ni = 0; ni < Neighbors.Length; ni++)
                {
                    var (dx, dy, cost) = Neighbors[ni];
                    int nx = cx + dx, ny = cy + dy;
                    if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                    if (!map.HasTile(nx, ny)) continue;
                    if (!map.IsPassable(nx, ny, forceType)) continue;
                    int nIdx = ny * w + nx;
                    if (corpseMask[nIdx]) continue; // 尸体格硬阻塞
                    if (dx != 0 && dy != 0)
                    {
                        // 斜向防穿角：两相邻直格都不可走则不许斜穿
                        if (!map.IsPassable(cx + dx, cy, forceType)
                            && !map.IsPassable(cx, cy + dy, forceType)) continue;
                    }
                    if (_closedCells[nIdx]) continue;

                    int ng = current.g + cost;
                    if (_bestG[nIdx] != int.MinValue && _bestG[nIdx] <= ng) continue;

                    _bestG[nIdx] = ng;
                    _open.Add(NewNode(nIdx, nx, ny, ng, HeuristicIdx(nx, ny, goal.x, goal.y), bestNode));
                }
            }

            if (goalNode < 0)
            {
                // 不可达（被地形/尸体围死）：原地待命
                var stuck = new List<Vector2IntCm> { new Vector2IntCm(fromXCm, fromZCm) };
                return stuck;
            }

            // 回溯路径→waypoint（格心 cm；末点替换为精确目标 cm）
            var waypoints = new List<Vector2IntCm>();
            for (int n = goalNode; n >= 0; n = _nodes[n].parentIndex)
            {
                var node = _nodes[n];
                waypoints.Insert(0, new Vector2IntCm(
                    node.x * CellMetrics.CellSizeCm + CellMetrics.CellHalfCm,
                    node.y * CellMetrics.CellSizeCm + CellMetrics.CellHalfCm));
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

        // ==================== 连通域可达性（决策期 O(1)） ====================
        // 2026-11-07 三轮：决策期每个眷属/伙伴都要问「这个驻位点能走到吗」，旧实现对每次询问跑
        // 一条**完整 A***（实测 0.45ms，后期每秒数百次 = 100ms/s 的主项）。地形与尸体在同一 tick
        // 内静态，故按 (tick, ForceType) 惰性构建一次连通域标号，之后 O(1) 查表。
        // 通行规则与 FindPath 完全同源（8 向 + 斜向需至少一个直邻可走 + 尸体硬阻塞）——
        // 「同连通域 ⇔ A* 能到」严格成立，决策结果与旧实现逐条一致。
        private static readonly Dictionary<ForceType, int[]> _compCache = new Dictionary<ForceType, int[]>();
        private static int[] _compQueue;
        private static int _compSim = int.MinValue;
        private static int _compTick = int.MinValue;

        /// <summary>可达性判定（与 FindPath 同口径：目标不可走时先按同方向钳到最近可走格）</summary>
        public static bool IsReachable(BattleSimState sim, int fromXCm, int fromZCm, int toXCm, int toZCm, ForceType forceType)
        {
            if (sim == null || sim.Map == null) return false;
            var start = BattleCell.FromCm(fromXCm, fromZCm);
            var goal = BattleCell.FromCm(toXCm, toZCm);
            if (start.Equals(goal)) return true;

            var map = sim.Map;
            if (!map.HasTile(goal.x, goal.y) || !map.IsPassable(goal.x, goal.y, forceType))
            {
                goal = ClampToNearestPassable(sim, start, goal, forceType);
                if (goal.Equals(start)) return false; // 与 FindPath 的原地回落一致=不可达
            }

            var comp = ComponentsOf(sim, forceType);
            int w = map.width;
            int cs = comp[start.y * w + start.x];
            int cg = comp[goal.y * w + goal.x];
            if (cs != 0) return cg != 0 && cs == cg;

            // 起点格本身不可走（站在尸体格/不可走地形上）——A* 对起点有豁免：它不检查起点自身的
            // 通行性，直接从起点展开合法邻居。这里的等价形态=「目标与起点的任一合法邻居同连通域」。
            // （2026-11-07 一致性断言抓到的唯一差异族：U61 站尸体格，旧实现可达、连通域版恒不可达）
            if (cg == 0) return false;
            var corpse = sim.CorpseMaskOfTick();
            int sx = start.x, sy = start.y;
            for (int ni = 0; ni < Neighbors.Length; ni++)
            {
                var (dx, dy, _) = Neighbors[ni];
                int nx = sx + dx, ny = sy + dy;
                if (nx < 0 || nx >= w || ny < 0 || ny >= map.height) continue;
                if (!map.HasTile(nx, ny) || !map.IsPassable(nx, ny, forceType)) continue;
                if (corpse[ny * w + nx]) continue;
                if (dx != 0 && dy != 0)
                {
                    if (!map.IsPassable(sx + dx, sy, forceType)
                        && !map.IsPassable(sx, sy + dy, forceType)) continue;
                }
                if (comp[ny * w + nx] == cg) return true;
            }
            return false;
        }

        private static int[] ComponentsOf(BattleSimState sim, ForceType forceType)
        {
            if (sim.InstanceId != _compSim || sim.CurrentTick != _compTick)
            {
                _compCache.Clear();
                _compSim = sim.InstanceId;
                _compTick = sim.CurrentTick;
            }
            if (_compCache.TryGetValue(forceType, out var cached)) return cached;
            var built = BuildComponents(sim, forceType);
            _compCache[forceType] = built;
            return built;
        }

        /// <summary>连通域标号（0=不可走；>=1=连通分量号，按格索引升序首次发现的顺序编号=确定性）</summary>
        private static int[] BuildComponents(BattleSimState sim, ForceType forceType)
        {
            var map = sim.Map;
            int w = map.width, h = map.height, size = w * h;
            var comp = new int[size];
            var corpse = sim.CorpseMaskOfTick();
            if (_compQueue == null || _compQueue.Length < size) _compQueue = new int[size];

            int next = 0;
            for (int startIdx = 0; startIdx < size; startIdx++)
            {
                if (comp[startIdx] != 0) continue;
                int sx = startIdx % w, sy = startIdx / w;
                if (!map.HasTile(sx, sy) || !map.IsPassable(sx, sy, forceType) || corpse[startIdx]) continue;

                next++;
                int head = 0, tail = 0;
                _compQueue[tail++] = startIdx;
                comp[startIdx] = next;
                while (head < tail)
                {
                    int cur = _compQueue[head++];
                    int cx = cur % w, cy = cur / w;
                    for (int ni = 0; ni < Neighbors.Length; ni++)
                    {
                        var (dx, dy, _) = Neighbors[ni];
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                        int nIdx = ny * w + nx;
                        if (comp[nIdx] != 0) continue;
                        if (!map.HasTile(nx, ny) || !map.IsPassable(nx, ny, forceType)) continue;
                        if (corpse[nIdx]) continue;
                        if (dx != 0 && dy != 0)
                        {
                            // 与 FindPath 同一条防穿角规则
                            if (!map.IsPassable(cx + dx, cy, forceType)
                                && !map.IsPassable(cx, cy + dy, forceType)) continue;
                        }
                        comp[nIdx] = next;
                        _compQueue[tail++] = nIdx;
                    }
                }
            }
            return comp;
        }

        /// <summary>启发式（八向一致的整数 octile：10×max+4×min，非 admissible 修正版 14-2×10 直斜混合）</summary>
        private static int Heuristic(BattleCell a, BattleCell b)
        {
            int dx = Math.Abs(a.x - b.x);
            int dy = Math.Abs(a.y - b.y);
            return dx > dy ? 10 * dx + 4 * dy : 10 * dy + 4 * dx;
        }

        /// <summary>格索引版启发式（与 Heuristic 同式；坐标已预存，无除法）</summary>
        private static int HeuristicIdx(int x, int y, int goalX, int goalY)
        {
            int dx = Math.Abs(x - goalX);
            int dy = Math.Abs(y - goalY);
            return dx > dy ? 10 * dx + 4 * dy : 10 * dy + 4 * dx;
        }

        // ==================== A* 复用缓冲（2026-11-07 去分配改造） ====================
        // 旧实现每次寻路 new List<Node>+HashSet<string>+Dictionary<string,Node>，且每个节点/每个
        // 邻居都做 $"x,y" 字符串插值——A* 展开数百节点 = 数千次字符串分配，实测单次寻路 0.5ms、
        // 后期 AI 每秒数百次 = 分配风暴主力。现把键换成格索引（y*width+x）、Node 换进池化数组。
        // 2026-11-07 二轮：①坐标与 h 预存进节点（旧线性取最小每轮比较都在算 x=idx%w 除法 + 启发式）；
        // ②open 由「List 线性取最小」改二叉堆（O(n²)→O(n log n)）——比较键 (F, g, nodeIndex)，
        // nodeIndex 单调递增=插入序，与旧「取第一个最小 (F,g)」逐条等价（决定性不变）。
        // 搜索顺序/闭集语义保持旧行为（含「允许重开已闭合格」——勿顺手改成标准 A*，那会改变路径）。
        // 单线程主线程使用，静态复用安全（跨战斗/跨地图按尺寸重建）。
        private struct PNode
        {
            public int cell;        // 格索引（closed/corpse 查表用）
            public int x, y;        // 格坐标（避免每轮除法）
            public int g;
            public int h;           // 启发式（构造时算好，同旧实现）
            public int parentIndex; // _nodes 下标（-1=根）
        }

        private static int[] _bestG;
        private static bool[] _closedCells;
        private static PNode[] _nodes;
        private static int _nodeCount;
        private static int _buffSize = -1;

        // open（存 _nodes 下标）——复用 List，线性取最小（与旧实现同口径）
        private static readonly List<int> _open = new List<int>(128);

        private static void EnsureBuffers(int size)
        {
            if (_buffSize == size && _bestG != null) return;
            _buffSize = size;
            _bestG = new int[size];
            _closedCells = new bool[size];
            _nodes = new PNode[Math.Max(size * 2, 64)];
        }

        private static int NewNode(int cell, int x, int y, int g, int h, int parentIndex)
        {
            if (_nodeCount >= _nodes.Length)
                Array.Resize(ref _nodes, _nodes.Length * 2);
            _nodes[_nodeCount] = new PNode { cell = cell, x = x, y = y, g = g, h = h, parentIndex = parentIndex };
            return _nodeCount++;
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
