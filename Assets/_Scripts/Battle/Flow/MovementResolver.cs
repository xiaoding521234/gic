using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
namespace GIC.Battle
{


    /// <summary>
    /// 同片内一个移动行动的推进状态
    /// </summary>
    public class MoveActionState
    {
        public string UnitId;
        public Unit Unit;
        public Direction2D Direction;
        public int RemainingSteps;

        /// <summary>工作位置（同片内逐轮更新）</summary>
        public BattleCell Current;

        /// <summary>完整路径（含起点格）</summary>
        public List<BattleCell> Path = new List<BattleCell>();

        public bool Done;
        public bool Blocked;
    }

    /// <summary>
    /// 移动同步逐步结算（docs/22 §7）。
    ///
    /// 判定链（每步只判将要进入的下一格，依序）：
    /// 1. 体积绝对层：格内现有体积 + 自身体积 ≤ 3——无视阻挡能力也不可绕过
    /// 2. 阻挡规则层：UnitMoveable 三配置（blockAllies/blockEnemies/blockedByEnemies）
    /// 3. 地形层：TileType 标签按 ForceType 判定
    ///
    /// 同片轮次推进：每轮先按"本轮开始时的位置"做全部未完成移动者的下一格判定，
    /// 通过者同轮进入；当前所在格不判定（同格共存单位不阻挡彼此离开）。
    /// 由此：相向对穿 = 互相穿过；同攻速同时移入同格 = 共存；被挡 = 停在格前（该移动结束）。
    /// </summary>
    public static class MovementResolver
    {
        /// <summary>
        /// 同片全部移动行动按轮推进结算；结束时把最终位置写回 sim。
        /// </summary>
        public static void Resolve(BattleSimState sim, List<MoveActionState> movers)
        {
            if (movers == null || movers.Count == 0) return;

            // 枚举序：unitId 升序（决定轮内判定顺序；不影响结果语义）
            movers.Sort((a, b) => string.CompareOrdinal(a.UnitId, b.UnitId));

            foreach (var mover in movers)
            {
                mover.Current = sim.GetPosition(mover.Unit);
                mover.Path.Clear();
                mover.Path.Add(mover.Current);
                mover.Done = mover.RemainingSteps <= 0;
            }

            bool anyProgress = true;
            while (!AllDone(movers) && anyProgress)
            {
                anyProgress = false;

                // 本轮开始时的位置快照（全体单位：非移动者取 sim 位置，移动者取工作位置）
                var roundStart = CapturePositions(sim, movers);

                foreach (var mover in movers)
                {
                    if (mover.Done) continue;
                    if (mover.RemainingSteps <= 0)
                    {
                        mover.Done = true;
                        continue;
                    }

                    var next = mover.Current + StepVector(mover.Direction);
                    if (!CanEnter(sim, mover, next, roundStart))
                    {
                        // 停在格前：该移动行动结束，剩余步数作废
                        mover.Done = true;
                        mover.Blocked = true;
                        continue;
                    }

                    // 通过者同轮进入
                    mover.Current = next;
                    mover.Path.Add(next);
                    mover.RemainingSteps--;
                    anyProgress = true;

                    if (mover.RemainingSteps <= 0)
                        mover.Done = true;
                }
            }

            // 最终位置写回
            foreach (var mover in movers)
                sim.SetPosition(mover.Unit, mover.Current);
        }

        // ==================== 内部 ====================

        private static bool AllDone(List<MoveActionState> movers)
        {
            foreach (var mover in movers)
                if (!mover.Done) return false;
            return true;
        }

        /// <summary>
        /// 捕获全体单位位置：非移动者 = sim 当前位置；移动者 = 工作位置（本轮开始时）
        /// </summary>
        private static Dictionary<string, BattleCell> CapturePositions(BattleSimState sim, List<MoveActionState> movers)
        {
            var positions = new Dictionary<string, BattleCell>();
            foreach (var kv in sim.Units)
                positions[kv.Key] = sim.GetPosition(kv.Value);
            foreach (var mover in movers)
                positions[mover.UnitId] = mover.Current;
            return positions;
        }

        private static BattleCell StepVector(Direction2D direction)
        {
            switch (direction)
            {
                case Direction2D.Right: return new BattleCell(1, 0);
                case Direction2D.Left: return new BattleCell(-1, 0);
                case Direction2D.Up: return new BattleCell(0, 1);
                case Direction2D.Down: return new BattleCell(0, -1);
                case Direction2D.UpRight: return new BattleCell(1, 1);
                case Direction2D.UpLeft: return new BattleCell(-1, 1);
                case Direction2D.DownRight: return new BattleCell(1, -1);
                case Direction2D.DownLeft: return new BattleCell(-1, -1);
                default: return BattleCell.zero;
            }
        }

        /// <summary>
        /// 下一格进入判定（体积绝对层 → 阻挡规则层 → 地形层）
        /// </summary>
        private static bool CanEnter(BattleSimState sim, MoveActionState mover, BattleCell next, Dictionary<string, BattleCell> roundStart)
        {
            var moveable = mover.Unit.GetUnitComponent<UnitMoveable>();
            var forceType = moveable != null ? moveable.NormalMoveType : ForceType.Walk;

            // 地形层：TileType 标签按 ForceType 判定（虚空/越界不可通行）
            if (!sim.Map.IsPassable(next.x, next.y, forceType))
                return false;

            // 收集该格现有单位（按本轮开始时位置；含尸体——尸体保留碰撞/体积）
            var occupants = new List<Unit>();
            foreach (var kv in roundStart)
            {
                if (kv.Key == mover.UnitId) continue;
                var unit = sim.GetUnit(kv.Key);
                if (unit != null && kv.Value.Equals(next))
                    occupants.Add(unit);
            }

            // 体积绝对层：格内现有体积 + 自身体积 ≤ 3（无视阻挡能力不可绕过）
            int existingVolume = 0;
            foreach (var occupant in occupants)
                existingVolume += occupant.Volume;
            if (existingVolume + mover.Unit.Volume > 3)
                return false;

            // 阻挡规则层：牵引不检查阻挡规则（仍受体积/地形约束，docs/05 §5.3）
            if (forceType == ForceType.Pull)
                return true;

            var selfIdentity = mover.Unit.GetUnitComponent<UnitIdentity>();
            foreach (var occupant in occupants)
            {
                var occupantMoveable = occupant.GetUnitComponent<UnitMoveable>();
                if (occupantMoveable == null) continue;

                var occupantIdentity = occupant.GetUnitComponent<UnitIdentity>();
                bool sameTeam = selfIdentity != null && occupantIdentity != null && selfIdentity.IsSameTeam(occupantIdentity);

                if (sameTeam && occupantMoveable.BlockAllies)
                    return false;

                if (!sameTeam && occupantMoveable.BlockEnemies && moveable != null && moveable.BlockedByEnemies)
                    return false;
            }

            return true;
        }
    }
}
