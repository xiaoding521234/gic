using System;
using System.Collections.Generic;
using GIC.Data;
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
    /// 移动同步逐步结算（docs/active/22 §7）。
    ///
    /// 判定链（每步只判将要进入的下一格，依序）：
    /// 1. 体积绝对层：格内现有体积 + 自身体积 ≤ 3——无视阻挡能力也不可绕过
    /// 2. 阻挡规则层：UnitMoveable 三配置（blockAllies/blockEnemies/blockedByEnemies）
    /// 3. 地形层：TileType 标签按 ForceType 判定
    ///
    /// 同片轮次推进：每轮先按"本轮开始时的位置"做全部未完成移动者的下一格判定，
    /// 通过者同轮进入；当前所在格不判定（同格共存单位不阻挡彼此离开）。
    /// 由此（判定基准=本轮开始位置快照）：相向对穿——默认阻挡配置（双方对侧阻挡均开）时
    /// 两者的下一格都判「被对方占据」→ 互弹双作废（各停格前）；仅互不阻挡对/开「与友方互不阻挡」
    /// 豁免的单位才真正互相穿过（批4 复审 A 案勘正，docs/05 §5.3 同步）。
    /// 同攻速同时移入同格 = 共存；被挡 = 停在格前（该移动结束）。
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

        /// <summary>
        /// 方向 → 每步步进格增量（8 向全支持）。2026-09-21 转 public：View 撞墙弹回方向与 Host 步进同源；
        /// 勿复用 SkillHitResolver.DirectionToDelta——那是直线投射物的十字归一映射，斜向会被归一到主轴
        /// </summary>
        public static BattleCell StepVector(Direction2D direction)
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
        /// 下一格进入判定（地形层 → 体积绝对层 → 阻挡规则层；体积/阻挡两段走下方共享原语）
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

            // 体积绝对层：格内现有体积 + 自身体积 ≤ 3（共享原语——与部署落点判定同源）
            if (!PassesVolumeLimit(occupants, mover.Unit.Volume))
                return false;

            // 阻挡规则层：牵引不检查阻挡规则（仍受体积/地形约束，docs/05 §5.3）
            if (forceType == ForceType.Pull)
                return true;

            var selfIdentity = mover.Unit.GetUnitComponent<UnitIdentity>();
            return PassesBlockingRules(occupants, selfIdentity?.Team,
                moveable == null || moveable.与友方互不阻挡,
                moveable != null && moveable.BlockedByEnemies);
        }

        // ==================== 碰撞判定共享原语（2026-10-02 执行阶段复审收口） ====================
        // 原移动进入（CanEnter）与部署落点（DeployUnitExecutor.IsDeployCellValid）两处平行手写的
        // 同构链收单源——碰撞口径改动（体积规则/互不阻挡语义等）只改这里，两侧自动同响应；
        // 地形层与占据收集保留在各调用方（移动=roundStart 工作位置收集、部署=GetUnitsAt 现位置）。

        /// <summary>体积绝对层（最高级，无视阻挡配置不可绕过）：格内现有体积+自身体积 ≤
        /// BattleMetrics.MaxTileVolume（docs/05 §5.3）。occupants=该格现有单位（含尸体——
        /// 尸体保留碰撞/体积；调用方负责收集与排除自身）</summary>
        public static bool PassesVolumeLimit(List<Unit> occupants, int selfVolume)
        {
            int existingVolume = 0;
            foreach (var occupant in occupants)
                existingVolume += occupant.Volume;
            return existingVolume + selfVolume <= BattleMetrics.MaxTileVolume;
        }

        /// <summary>阻挡规则层（UnitMoveable 三配置+「与友方互不阻挡」双向豁免，2026-09-26 拍板
        /// 「只要这个单位互不阻挡字段为 true，无论他穿其它友军还是友军穿他，都不阻挡」——
        /// 配置驱动：改 UnitConfig 即四处全响应）：友方=占据者 BlockAllies 且双方均未开豁免；
        /// 敌方=占据者 BlockEnemies 且自身 BlockedByEnemies。selfTeam=null=自身身份缺失恒按异队
        /// （防御口径——注册单位必有 identity）；无 identity 的占据者同按异队（原移动侧口径；
        /// 原部署侧对无 identity 占据者是跳过，该场景注册单位必带 identity 不可达）。
        /// 自身规格参数：移动侧从 UnitMoveable 组件取、部署侧从 UnitConfig.UnitData 取
        /// （部署单位尚未生成）——原语只认参数，两侧判定恒同口径</summary>
        public static bool PassesBlockingRules(List<Unit> occupants, TeamType? selfTeam,
            bool selfNoBlockAllies, bool selfBlockedByEnemies)
        {
            foreach (var occupant in occupants)
            {
                var occupantMoveable = occupant.GetUnitComponent<UnitMoveable>();
                if (occupantMoveable == null) continue;

                var occupantIdentity = occupant.GetUnitComponent<UnitIdentity>();
                bool sameTeam = selfTeam.HasValue && occupantIdentity != null
                    && occupantIdentity.Team == selfTeam.Value;

                if (sameTeam && occupantMoveable.BlockAllies
                    && !occupantMoveable.与友方互不阻挡
                    && !selfNoBlockAllies)
                    return false;

                if (!sameTeam && occupantMoveable.BlockEnemies && selfBlockedByEnemies)
                    return false;
            }
            return true;
        }
    }
}
