using System;
using System.Collections.Generic;
using GIC.Data;
using UnityEngine;

namespace GIC.Battle
{
    /// <summary>
    /// 单位弹性分离（2026-11-08 拍板「碰撞箱像我的世界/皇室战争那样有弹性，建筑很硬」+「真·皇室战争」）：
    /// **皇室战争式质量加权、敌我通用**——单位有重量（UnitData.重量），重叠时按**反质量**分摊位移：
    /// 巨人（重 8）让小骷髅（重 1）几乎全额让位，自己只挪 1/9。
    ///
    /// - 每 tick 在移动推进 + 移动收尾之后跑：穿透 &gt; ContactToleranceCm 的两圆沿圆心连线各退；
    /// - **动静一律参与**（2026-11-08 追拍「两个都在走也不许互相穿过」）：移动中的单位同样按质量被推开，
    ///   位移经 MovePushed 下发，客户端**续跑原目标**（PlayMoveResumeCoroutine）而非另起补间；
    /// - 推进层配合规则=「推不动的敌人才当墙」（RTSMovementSystem.IsHardBlockerForMover：
    ///   敌方重量 ≥ 自身才挡）——重者走过来把轻者挤开，轻者撞重者停于接触前；
    /// - 建筑/核心=硬墙（推进层已挡）、尸体=静态硬挡圆、灵魂=独立注册表——三者都不进分离域；
    /// - 挤开不许把单位挤进尸体/建筑或不可走地形（落点守卫）；
    /// - 全整数 + SortedUnits 稳定序 + 固定迭代次数（确定性纪律：同 seed 同命令=同结果）。
    ///
    /// 观感链路：Host 权威位移按 5Hz 批量成段下发绝对位（BattleCommand.MovePushed），
    /// 客户端短补间滑过去——不逐 tick 发命令、不与 1Hz 快照自愈打架。
    /// </summary>
    public static class UnitSeparationSystem
    {
        /// <summary>每 tick 单单位最大分离位移（cm）——多对同时挤时的累计上限（防弹射）</summary>
        public const int MaxDisplacePerTickCm = 6;

        /// <summary>每 tick 单对**总**分离量（cm，双方合计）——按反质量分配给两侧：轻者吃大头、
        /// 重者几乎不动（巨人 8 vs 丘丘人 1 → 轻者 5.3cm、重者 0.7cm）。**勿改成"每侧各自上限"**：
        /// 深度重叠时两侧都顶上限会把质量差抹平（2026-11-08 观测局实锤）</summary>
        public const int MaxSeparatePerTickCm = 6;

        /// <summary>剩余穿透小于此值视为已分离（防整数取整在贴边处反复互推）</summary>
        public const int ContactToleranceCm = 2;

        /// <summary>松弛迭代次数=**1**（2026-11-08 受控实验定案）：同对每 tick 只吃一次总量预算，
        /// 否则两次迭代各分一次会把质量差冲淡（实测 4:2 质量比退化成 4:6 位移比）。多点堆叠靠
        /// 20Hz 逐 tick 收敛即可（每 tick 6cm/对 → 满穿透约 0.4 秒分开）。</summary>
        public const int Iterations = 1;

        /// <summary>推挤校正下发间隔（tick；4=5Hz）——客户端用短补间吃掉，不逐 tick 发命令</summary>
        public const int PushEmitIntervalTicks = 4;

        /// <summary>推进一 tick 的分离（RTSFlowController / ReplayRunner 每 tick 同一位置调用）</summary>
        public static void ResolveTick(BattleSimState sim)
        {
            if (sim == null) return;
            var units = sim.SortedUnits();
            if (units == null || units.Count < 2) return;

            var displaced = sim.SeparationDisplacedCm;
            displaced.Clear();

            // 空/地分层（2026-11-08）：飞行×地面=不参与分离（飞过头顶）；飞行×飞行照常。
            // 按 SortedUnits 下标预取，避免每对调组件查询。
            var airborne = sim.SeparationAirborneScratch;
            if (airborne == null || airborne.Length < units.Count)
            {
                airborne = new bool[units.Count];
                sim.SeparationAirborneScratch = airborne;
            }
            for (int i = 0; i < units.Count; i++)
                airborne[i] = RTSMovementSystem.IsAirborne(units[i].Value);

            for (int iter = 0; iter < Iterations; iter++)
            {
                bool any = false;
                for (int i = 0; i < units.Count; i++)
                {
                    var a = units[i].Value;
                    if (!Participates(a)) continue;

                    string idA = units[i].Key;
                    int rA = RadiusCmOf(a);
                    int wA = WeightOf(a);
                    var pa = sim.GetPosCm(a);

                    for (int j = i + 1; j < units.Count; j++)
                    {
                        var b = units[j].Value;
                        if (!Participates(b)) continue;
                        if (airborne[i] != airborne[j]) continue; // 异层=不相撞

                        string idB = units[j].Key;

                        int rB = RadiusCmOf(b);
                        var pb = sim.GetPosCm(b);
                        long dx = (long)pb.x - pa.x;
                        long dz = (long)pb.y - pa.y;
                        long touch = (long)rA + rB;
                        long d2 = dx * dx + dz * dz;
                        if (d2 >= touch * touch) continue; // 未重叠

                        int dist = RTSMovementSystem.SqrtI(d2);
                        int pen = (int)(touch - dist);
                        if (pen < ContactToleranceCm) continue; // 已贴边=不再互推（防抖）

                        int ux, uz; // 千分单位向量（A→B）
                        if (dist > 0)
                        {
                            ux = (int)(dx * 1000 / dist);
                            uz = (int)(dz * 1000 / dist);
                        }
                        else
                        {
                            // 圆心完全重合（极端：两点下令同落点）——按 id 派生固定方向（零随机，确定性）
                            CoincidentAxis(idA, idB, out int ax, out int az);
                            ux = ax * 1000;
                            uz = az * 1000;
                        }

                        // 反质量分摊（敌我、动静一律适用——2026-11-08「两个都在走也不许互相穿过」）：
                        // 本 tick 总分离量按重量反比切，A 让位 = 总分离 × wB/(wA+wB)
                        int wB = WeightOf(b);
                        int total = pen < MaxSeparatePerTickCm ? pen : MaxSeparatePerTickCm;
                        int shareA = total * wB / (wA + wB);
                        int shareB = total - shareA;

                        if (shareA > 0 && TryDisplace(sim, a, idA, pa, -1, ux, uz, shareA, displaced))
                        {
                            pa = sim.GetPosCm(a);
                            any = true;
                        }
                        if (shareB > 0 && TryDisplace(sim, b, idB, pb, 1, ux, uz, shareB, displaced))
                            any = true;
                    }
                }
                if (!any) break; // 全分离=本 tick 收敛
            }

            // 待下发绝对位（5Hz 批量；下发侧跳过移动中单位并清表）
            foreach (var kv in displaced)
            {
                if (kv.Value <= 0) continue;
                var unit = sim.GetUnit(kv.Key);
                if (unit == null) continue;
                var pos = sim.GetPosCm(unit);
                sim.PushPending[kv.Key] = new Vector2Int(pos.x, pos.y);
            }
        }

        /// <summary>分离域：存活、非建筑（建筑=硬墙；尸体=静态硬挡圆不参与；灵魂在独立注册表天然不在域内）</summary>
        private static bool Participates(Unit unit)
        {
            if (unit == null) return false;
            if (BattleSimState.IsDead(unit)) return false;
            var raw = unit.RawData;
            if (raw == null || raw.unitType == UnitType.Building) return false;
            return true;
        }

        /// <summary>尝试把单位沿 ±单位向量退 step cm：单 tick 位移上限 + 落点守卫（地形/硬挡圆）。
        /// sign=-1=远离 B（A 侧）、+1=远离 A（B 侧）。</summary>
        private static bool TryDisplace(BattleSimState sim, Unit unit, string unitId, Vector2Int pos,
            int sign, int ux, int uz, int share, Dictionary<string, int> displaced)
        {
            int used = displaced.TryGetValue(unitId, out int v) ? v : 0;
            int room = MaxDisplacePerTickCm - used;
            if (room <= 0) return false;

            int step = share < room ? share : room;
            if (step <= 0) return false;

            int nx = pos.x + sign * (int)((long)ux * step / 1000);
            int nz = pos.y + sign * (int)((long)uz * step / 1000);
            if (nx == pos.x && nz == pos.y) return false; // 取整为 0（亚厘米穿透，无需动）
            if (!CanStandAt(sim, unit, pos.x, pos.y, nx, nz)) return false;

            sim.SetPosCm(unit, nx, nz);
            displaced[unitId] = used + step;
            return true;
        }

        /// <summary>落点守卫：可走地形（按常态移动类型）+ **不许比现在更深入**硬挡圆
        /// （贴建筑站着的单位必须能挤开——绝对「不许碰」会把它们永久锁死，2026-11-08 观测局实锤）。
        /// 容差 2cm：允许贴边滑动（弦向位移会带来亚厘米级穿透增量）。</summary>
        private static bool CanStandAt(BattleSimState sim, Unit unit, int fromXCm, int fromZCm, int xCm, int zCm)
        {
            var cell = BattleCell.FromCm(xCm, zCm);
            if (!sim.Map.HasTile(cell.x, cell.y)) return false;
            var moveable = unit.GetUnitComponent<UnitMoveable>();
            var forceType = moveable != null ? moveable.NormalMoveType : ForceType.Walk;
            if (!sim.Map.IsPassable(cell.x, cell.y, forceType)) return false;
            int nowPen = RTSMovementSystem.HardPenetrationCm(sim, unit, fromXCm, fromZCm);
            int newPen = RTSMovementSystem.HardPenetrationCm(sim, unit, xCm, zCm);
            return newPen <= nowPen + ContactToleranceCm;
        }

        /// <summary>碰撞圆半径（cm）=受击圆柱直径 × 格宽 ÷ 2（直径 0.5 格 → 半径 25cm，两圆接触=50cm）。
        /// 与 RTSMovementSystem.RadiusCmOf / 部署校验 / 投射物判定同一口径（2026-11-08 修正推进层的
        /// 漏 ÷2 偏差，此前接触距离曾为 100cm=1 格）。</summary>
        private static int RadiusCmOf(Unit unit)
            => Mathf.RoundToInt(BattleMetrics.CylinderDiameterOf(unit) * CellMetrics.CellSizeCm * 0.5f);

        /// <summary>有效重量（0/缺失回落 1——分离按反质量分摊，分母不可为零；真源=RTSMovementSystem.WeightOf）</summary>
        private static int WeightOf(Unit unit) => RTSMovementSystem.WeightOf(unit);

        /// <summary>圆心重合时的分离轴（按两 id 的 FNV-1a 派生 4 向之一——跨端/回放确定一致，零随机）</summary>
        private static void CoincidentAxis(string idA, string idB, out int ux, out int uz)
        {
            uint h = Fnv1a(idA ?? string.Empty) ^ (Fnv1a(idB ?? string.Empty) * 16777619u);
            switch (h % 4u)
            {
                case 0: ux = 1; uz = 0; break;
                case 1: ux = 0; uz = 1; break;
                case 2: ux = -1; uz = 0; break;
                default: ux = 0; uz = -1; break;
            }
        }

        private static uint Fnv1a(string text)
        {
            uint h = 2166136261u;
            for (int i = 0; i < text.Length; i++)
            {
                h ^= text[i];
                h *= 16777619u;
            }
            return h;
        }
    }
}
