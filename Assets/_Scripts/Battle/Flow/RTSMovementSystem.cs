using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 即时制 tick 移动推进器（决策九十五 P2，蓝图 §3.3）：
    /// - **启动**：BuildCmMover（RTSPathfinder A* 路径+移速）→入 sim.ActiveMovers；移动免费
    ///   （0 体力 0 元能——蓝图 §3.3「移动不消耗体力、不获取元能」，旧轨获能/扣费随 P5 旧轨退役消失）；
    /// - **推进**：RTSFlowController 每 tick 调 AdvanceTick——整数速度累积器（AccumSpeedCm 累积
    ///   speedCm/秒，每 tick 取 Accum/20 为步预算——非整除移速零偏差，全整数确定性）；
    /// - **碰撞**（蓝图 §3.2 语义连续化）：敌方/尸体/核心=硬挡圆（停于接触前=Blocked 收尾）；
    ///   友军=可穿不可停（推进期零阻挡）；地形=ForceType（寻径层已保证，推进期不再判）；
    /// - **收尾**：到达目标点（Arrived）或被挡（Blocked）→ 由 Flow 交 BattleResolver.ResolveMoveArrivals
    ///   产命令段（途经采集保留〔技能参数数据驱动〕、移动获能已删除、SkillCast 启动段已发）。
    /// 客户端表现（P2 中间态）：启动段随 casts 发旧格式 Move 命令（格路径+方向）——旧播放器照播
    /// 行走动画；连续位置插值播放随 P4 客户端批改造。
    /// </summary>
    public class RTSMoveState
    {
        public string UnitId;
        public Unit Unit;
        public List<Vector2IntCm> WaypointsCm;
        public List<BattleCell> PathCells;      // 格序列（途经采集按格判堆——旧语义保留）
        public int WaypointIndex;
        public int SpeedCmPerSecond;
        public int AccumSpeedCm;                // 整数速度累积器（每 tick 累积，除以 TicksPerSecond 取步）
        public bool Arrived;
        public bool Blocked;
        public Direction2D BlockedDirection;   // P4-E 被挡方向（被挡校正命令 direction 载荷——客户端弹回表现）
    }

    public static class RTSMovementSystem
    {
        // ==================== 启动 ====================

        /// <summary>构建即时制移动者（A* 寻径+移速），并产启动 Move 命令（casts 首发位）。
        /// 返回 null=单位不可用/起点不可走。移动免费（蓝图 §3.3：0 体力 0 元能——不走 ResourceGate）</summary>
        public static RTSMoveState BuildCmMover(BattleSimState sim, ActionData action,
            out BattleCommand moveCommand)
        {
            moveCommand = null;
            var unit = sim.GetUnit(action.unitId);
            if (unit == null || BattleSimState.IsDead(unit) || !BattleSimState.CanAct(unit)) return null;

            var moveable = unit.GetUnitComponent<UnitMoveable>();
            var forceType = moveable != null ? moveable.NormalMoveType : ForceType.Walk;

            var posCm = sim.GetPosCm(unit);
            var waypoints = RTSPathfinder.FindPath(sim, posCm.x, posCm.y,
                action.targetX, action.targetZ, forceType);
            if (waypoints == null || waypoints.Count == 0) return null;

            // 移速：UnitStats 当前值（cm/s，蓝图 §3.3 全员 100=资产迁移 P3 前=现值 30/50 直读——
            // 移速语义已随蓝图改厘米/秒，30→30cm/s 慢是 P3 资产批前的预期中间态）
            int speedCm = unit.GetUnitComponent<UnitStats>()?.MoveSpeed ?? BattleMetrics.DefaultMoveSpeedCm;
            if (speedCm <= 0) speedCm = BattleMetrics.DefaultMoveSpeedCm;

            var mover = new RTSMoveState
            {
                UnitId = action.unitId,
                Unit = unit,
                WaypointsCm = waypoints,
                PathCells = BuildPathCells(sim, posCm, waypoints),
                SpeedCmPerSecond = speedCm,
            };

            // 启动 Move 命令（旧协议格式：格路径+方向——客户端按实际移速逐段补间〔P4-E：speedCm 载荷，
            // 斜向段时长=距离/速度，修旧 1s/格恒速误差〕；blocked=0）
            moveCommand = BattleCommand.Move(action.unitId, 0, 0, mover.PathCells, 0,
                (int)DirectionOfSegment(posCm.x, posCm.y, waypoints[0].x, waypoints[0].z),
                mover.SpeedCmPerSecond);
            // 精确落点 cm（连续空间，2026-11-05 移动抖动修复）：格路径只表达「经过哪些格」，
            // 客户端若按格心收尾会与 Host 的真实停点差最多半格，随后又被每秒快照自愈拉回=瞬移；
            // 末 waypoint=精确目标（不可达时=钳制落点），与 Host 推进终点同源
            var lastWp = waypoints[waypoints.Count - 1];
            moveCommand.hitX = lastWp.x;
            moveCommand.hitY = lastWp.z;
            return mover;
        }

        /// <summary>格序列（起点格+逐 waypoint 所在格去重——途经采集消费；含起终点语义同旧轨；
        /// P5 起 CompanionBrain 收获档路径堆值同源复用——脑层估值与执行层途经判定共用格序=单源）</summary>
        public static List<BattleCell> BuildPathCells(BattleSimState sim, Vector2Int fromCm, List<Vector2IntCm> waypoints)
        {
            var cells = new List<BattleCell>();
            var first = BattleCell.FromCm(fromCm.x, fromCm.y);
            cells.Add(first);
            foreach (var wp in waypoints)
            {
                var cell = BattleCell.FromCm(wp.x, wp.z);
                if (!cells[cells.Count - 1].Equals(cell)) cells.Add(cell);
            }
            return cells;
        }

        // ==================== tick 推进（RTSFlow 每 tick 调） ====================

        /// <summary>推进全部活跃移动者一 tick；返回本 tick 收尾者（Arrived/Blocked——Flow 交
        /// BattleResolver.ResolveMoveArrivals 产命令段）</summary>
        public static List<RTSMoveState> AdvanceTick(BattleSimState sim)
        {
            List<RTSMoveState> finished = null;
            var movers = sim.ActiveMoversInternal;
            if (movers.Count == 0) return null;

            // 速度累积（整数无偏差：每 tick 累 speed，每 20 tick 总推进恰为 speed）
            int budgetStep = 0;
            for (int i = movers.Count - 1; i >= 0; i--)
            {
                var mover = movers[i];
                if (mover.Unit == null || BattleSimState.IsDead(mover.Unit))
                {
                    // 单位死亡/消散：移动自然终止（无收尾命令）
                    movers.RemoveAt(i);
                    continue;
                }

                mover.AccumSpeedCm += mover.SpeedCmPerSecond;
                budgetStep = mover.AccumSpeedCm / BattleMetrics.TicksPerSecond;
                mover.AccumSpeedCm %= BattleMetrics.TicksPerSecond;
                if (budgetStep <= 0) continue;

                var posCm = sim.GetPosCm(mover.Unit);
                int x = posCm.x, z = posCm.y;
                bool done = false, blockedHit = false;

                // 跨 waypoint 消耗步预算
                while (budgetStep > 0 && !done)
                {
                    if (mover.WaypointIndex >= mover.WaypointsCm.Count)
                    {
                        done = true;
                        break;
                    }
                    var wp = mover.WaypointsCm[mover.WaypointIndex];
                    int dx = wp.x - x, dz = wp.z - z;
                    long distSq = (long)dx * dx + (long)dz * dz;
                    int dist = SqrtI(distSq);
                    if (dist <= budgetStep)
                    {
                        // 足够到达本 waypoint
                        budgetStep -= Math.Max(dist, 1);
                        x = wp.x;
                        z = wp.z;
                        mover.WaypointIndex++;
                        continue;
                    }
                    // 朝 waypoint 推进 budgetStep
                    // 整数方向步进（dx, dz 同比缩放——避免浮点）
                    int nx = x + (int)((long)dx * budgetStep / dist);
                    int nz = z + (int)((long)dz * budgetStep / dist);
                    int stepFromX = x, stepFromZ = z; // P4-E：被挡方向采样（步进前位）
                    (x, z, _) = CheckBlocked(sim, mover, x, z, nx, nz, out blockedHit);
                    if (blockedHit)
                        mover.BlockedDirection = DirectionOfSegment(stepFromX, stepFromZ, nx, nz);
                    budgetStep = 0;
                    if (blockedHit) done = true;
                }

                // 路径走完=到达
                if (!done && mover.WaypointIndex >= mover.WaypointsCm.Count) done = true;

                sim.SetPosCm(mover.Unit, x, z);

                if (done)
                {
                    mover.Arrived = !blockedHit;
                    mover.Blocked = blockedHit;
                    movers.RemoveAt(i);
                    (finished ??= new List<RTSMoveState>()).Add(mover);
                }
            }
            return finished;
        }

        /// <summary>碰撞检查：步进线段上与硬挡圆（敌方存活/尸体/核心）的接触判定——
        /// 重叠时回退至接触前并报阻挡（「被敌圆阻挡=停于接触前」蓝图语义）。
        /// 友军零阻挡（可穿不可停）；阻挡圆半径=受击圆柱直径同源（CylinderDiameterOf）</summary>
        private static (int x, int z, bool blocked) CheckBlocked(BattleSimState sim, RTSMoveState mover,
            int fromX, int fromZ, int toX, int toZ, out bool blockedHit)
        {
            blockedHit = false;

            foreach (var kv in sim.SortedUnits())
            {
                if (kv.Key == mover.UnitId) continue;
                var other = kv.Value;
                if (other == null) continue;

                // 硬挡域（2026-11-08「真·皇室战争」）：尸体/建筑恒挡；敌方存活单位**只有推不动时才挡**
                // （重量 ≥ 自身）——更重的一方推开轻的（IsHardBlockerForMover）；友军零阻挡
                if (!IsHardBlockerForMover(mover.Unit, other)) continue;

                var otherPos = sim.GetPosCm(other);
                int selfRadius = RadiusCmOf(mover.Unit);
                int otherRadius = RadiusCmOf(other);
                long touchDist = (long)selfRadius + otherRadius;

                // 圆与线段最近点距离（整数近似：采样步进中点+终点两查——量级小精度足）
                // 端点重叠检查（简化：仅查推进终点与线段中点——tick 步长 ≤5cm 远小于圆径 50）
                if (CircleHitsPoint(otherPos.x, otherPos.y, toX, toZ, touchDist))
                {
                    blockedHit = true;
                    // 回退至接触前：沿步进向量回退重叠深度
                    int backX = fromX + (toX - fromX) / 2;
                    int backZ = fromZ + (toZ - fromZ) / 2;
                    return (backX, backZ, true);
                }
            }
            return (toX, toZ, false);
        }

        /// <summary>空中单位（ForceType.Fly）——2026-11-08 空/地碰撞分层：**飞行与地面单位互不碰撞**
        /// （皇室战争语义：飞行从头顶飞过），飞行×飞行照常推挤；Amphibious=地面（可走水面但不是空中）。
        /// **建筑只挡地面层**（追拍：飞行直接越过建筑/塔）。</summary>
        public static bool IsAirborne(Unit unit)
        {
            var moveable = unit != null ? unit.GetUnitComponent<UnitMoveable>() : null;
            return moveable != null && moveable.NormalMoveType == ForceType.Fly;
        }

        /// <summary>两单位是否在同一碰撞层（同为空中 或 同为地面）——分离与推进阻挡共用</summary>
        public static bool SharesCollisionPlane(Unit a, Unit b) => IsAirborne(a) == IsAirborne(b);

        /// <summary>硬墙（对**同一碰撞层**的单位而言）：尸体（静态硬挡圆）、建筑含核心。
        /// 2026-11-08 追拍：**建筑只挡地面层**——飞行单位可越过建筑（塔顶飞过）；
        /// 尸体同样按自身碰撞层挡；敌方存活单位从来不是硬墙（轻重决定谁被推开，见 IsHardBlockerForMover）。</summary>
        public static bool IsHardBlocker(Unit self, Unit other)
        {
            if (other == null) return false;
            bool samePlane = SharesCollisionPlane(self, other);
            if (other.RawData != null && other.RawData.unitType == UnitType.Building)
                return samePlane; // 建筑：只挡地面；飞行越过
            if (BattleSimState.IsDead(other)) return samePlane; // 尸体按碰撞层挡
            return false;
        }

        /// <summary>推进层对该 mover 的硬挡：真硬墙 + **推不动的同层敌队存活单位**（皇室战争口径——
        /// 巨人（重 8）推开小骷髅（重 1）；小骷髅撞巨人=停于接触前；**等重=互不相让（停）**，
        /// 只有严格更重才推得走）。友军恒零阻挡（可穿）；**异碰撞层（空/地）恒不挡**。</summary>
        public static bool IsHardBlockerForMover(Unit mover, Unit other)
        {
            if (IsHardBlocker(mover, other)) return true;
            if (mover == null || other == null) return false;
            if (BattleSimState.IsDead(other)) return false; // 尸体已由 IsHardBlocker 判过（异层=不挡）
            var moverIdentity = mover.GetUnitComponent<UnitIdentity>();
            var otherIdentity = other.GetUnitComponent<UnitIdentity>();
            if (moverIdentity == null || otherIdentity == null) return false;
            if (otherIdentity.Team == moverIdentity.Team) return false;
            if (!SharesCollisionPlane(mover, other)) return false; // 空/地分层：互不阻挡
            return WeightOf(other) >= WeightOf(mover);
        }

        /// <summary>有效重量（0/缺失回落 1）——UnitData.重量 单源</summary>
        public static int WeightOf(Unit unit)
        {
            int w = unit?.RawData != null ? unit.RawData.GetEffectiveWeight() : 1;
            return w > 0 ? w : 1;
        }

        /// <summary>该点是否被硬挡圆压住（弹性分离落点守卫）。圆距口径=受击圆柱半径真值
        /// （直径 0.5 格 → 半径 25cm，两圆接触=50cm；2026-11-08 修正：原把直径当半径用，
        /// 接触距离曾为 100cm=1 格）</summary>
        public static bool HardBlockedAt(BattleSimState sim, Unit self, int xCm, int zCm)
            => HardPenetrationCm(sim, self, xCm, zCm) > 0;

        /// <summary>硬挡圆最大穿透深度（cm；0=不接触）——弹性分离守卫用「不许比现在更深」而不是
        /// 「绝对不许碰」：自由落点/召唤常把单位放在建筑 65cm 接触距之内（既成事实），若按绝对规则
        /// 判非法，该单位任何方向都动不了、会被永久锁死（2026-11-08 观测局实锤：双静止重叠 74/74
        /// 样本零收敛，根因就是贴建筑的单位被拒）。</summary>
        public static int HardPenetrationCm(BattleSimState sim, Unit self, int xCm, int zCm)
        {
            if (sim == null || self == null) return 0;
            string selfId = self.GetUnitComponent<UnitIdentity>()?.UnitID;
            int worst = 0;
            foreach (var kv in sim.SortedUnits())
            {
                if (kv.Key == selfId) continue;
                var other = kv.Value;
                if (!IsHardBlocker(self, other)) continue;
                var p = sim.GetPosCm(other);
                long touch = (long)RadiusCmOf(self) + RadiusCmOf(other);
                int dist = SqrtI((long)(xCm - p.x) * (xCm - p.x) + (long)(zCm - p.y) * (zCm - p.y));
                int pen = (int)touch - dist;
                if (pen > worst) worst = pen;
            }
            return worst;
        }

        private static bool CircleHitsPoint(int cx, int cz, int px, int pz, long touchDist)
        {
            long dx = px - cx;
            long dz = pz - cz;
            return dx * dx + dz * dz < touchDist * touchDist;
        }

        /// <summary>单位碰撞圆半径（厘米）=受击圆柱直径 × 格宽 ÷ 2（直径 0.5 格 → 半径 25cm，
        /// 两圆接触=50cm）。2026-11-08 修正：原漏 ÷2 把直径当半径用，接触距离曾达 100cm=1 格
        /// （与 docs/05 §5.3、部署校验、投射物判定三处口径不符）。</summary>
        private static int RadiusCmOf(Unit unit)
            => Mathf.RoundToInt(BattleMetrics.CylinderDiameterOf(unit) * CellMetrics.CellSizeCm * 0.5f);

        /// <summary>整数平方根（牛顿迭代全整数——确定性纪律零浮点；弹性分离共用）</summary>
        public static int SqrtI(long value)
        {
            if (value <= 0) return 0;
            int x = (int)Math.Sqrt(value); // 初值（浮点仅初始化，迭代收敛后与平台无关）
            while ((long)(x + 1) * (x + 1) <= value) x++;
            while ((long)x * x > value) x--;
            return x;
        }

        /// <summary>步进方向（8 向枚举近似——启动 Move 命令的方向字段：客户端立牌朝向镜像消费）</summary>
        private static Direction2D DirectionOfSegment(int fromX, int fromZ, int toX, int toZ)
        {
            int dx = Math.Sign(toX - fromX);
            int dz = Math.Sign(toZ - fromZ);
            if (dx == 0 && dz == 0) return Direction2D.Right;
            if (dx == 0) return dz > 0 ? Direction2D.Up : Direction2D.Down;
            if (dz == 0) return dx > 0 ? Direction2D.Right : Direction2D.Left;
            if (dx > 0) return dz > 0 ? Direction2D.UpRight : Direction2D.DownRight;
            return dz > 0 ? Direction2D.UpLeft : Direction2D.DownLeft;
        }
    }
}
