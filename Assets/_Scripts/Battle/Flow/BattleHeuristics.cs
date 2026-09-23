using System;
using System.Collections.Generic;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// AI 决策共享工具（B6b）：AI 玩家脑（AIDebugBrain）与低级单位脑（LowUnitBrain）共用的
    /// 敌人查找/方向/技能查询启发式原语。全部纯读 BattleSimState，不改状态（docs/18 决策一）。
    /// </summary>
    public static class BattleHeuristics
    {
        // ==================== 单位分拣 ====================

        /// <summary>是否低级单位（1~2 星，自主行动如 MOBA 小兵，docs/04 §4.1；3~5 星=高级单位）</summary>
        public static bool IsMinorUnit(Unit unit)
        {
            return unit.RawData != null && unit.RawData.starLevel <= 2;
        }

        /// <summary>是否高级单位（3~5 星，由所属玩家操控）</summary>
        public static bool IsMajorUnit(Unit unit)
        {
            return unit.RawData != null && unit.RawData.starLevel >= 3;
        }

        // ==================== 敌人查找 ====================

        /// <summary>
        /// 距离某单位最近的存活敌方单位（切比雪夫距离——docs/03 §3.3 八方向等价度量；
        /// 等距平局取 unitId 升序=枚举序铁律）
        /// </summary>
        public static Unit FindNearestEnemy(BattleSimState sim, Unit self)
        {
            var selfId = self.GetUnitComponent<UnitIdentity>();
            if (selfId == null) return null;
            var selfPos = sim.GetPosition(self);

            Unit best = null;
            int bestDistance = int.MaxValue;
            string bestUnitId = null;
            foreach (var kv in sim.Units)
            {
                var unit = kv.Value;
                var id = unit.GetUnitComponent<UnitIdentity>();
                if (id == null || id.OwnerPlayerID == selfId.OwnerPlayerID) continue;
                if (BattleSimState.IsDead(unit)) continue;

                var pos = sim.GetPosition(unit);
                int distance = Math.Max(Math.Abs(pos.x - selfPos.x), Math.Abs(pos.y - selfPos.y));
                var unitId = kv.Key;
                // 等距平局：unitId 升序（枚举序铁律，确定性）
                if (distance < bestDistance || (distance == bestDistance && string.CompareOrdinal(unitId, bestUnitId) < 0))
                {
                    best = unit;
                    bestDistance = distance;
                    bestUnitId = unitId;
                }
            }
            return best;
        }

        // ==================== 方向与技能 ====================

        /// <summary>位移增量 → 八向 Direction2D（斜向朝最近敌用）</summary>
        public static Direction2D DeltaToDirection(int dx, int dy)
        {
            int sx = Math.Sign(dx);
            int sy = Math.Sign(dy);
            if (sx == 0 && sy == 0) return Direction2D.Right;
            if (sx == 0) return sy > 0 ? Direction2D.Up : Direction2D.Down;
            if (sy == 0) return sx > 0 ? Direction2D.Right : Direction2D.Left;
            if (sx > 0) return sy > 0 ? Direction2D.UpRight : Direction2D.DownRight;
            return sy > 0 ? Direction2D.UpLeft : Direction2D.DownLeft;
        }

        /// <summary>Direction2D → 位移增量（八向）</summary>
        public static (int x, int y) DeltaOf(Direction2D direction)
        {
            switch (direction)
            {
                case Direction2D.Right: return (1, 0);
                case Direction2D.Left: return (-1, 0);
                case Direction2D.Up: return (0, 1);
                case Direction2D.Down: return (0, -1);
                case Direction2D.UpRight: return (1, 1);
                case Direction2D.UpLeft: return (-1, 1);
                case Direction2D.DownRight: return (1, -1);
                case Direction2D.DownLeft: return (-1, -1);
                default: return (1, 0);
            }
        }

        /// <summary>单位技能列表中首个指定类型技能的索引（-1=无；skillIndex 与 RawData.skills
        /// 引用列表同源——HUD/执行器同规约，分拣读 .data）</summary>
        public static int FindSkillIndex(Unit unit, SkillType skillType)
        {
            var rawData = unit.RawData;
            if (rawData?.skills == null) return -1;
            for (int i = 0; i < rawData.skills.Count; i++)
            {
                if (rawData.skills[i]?.data.skillType == skillType) return i;
            }
            return -1;
        }

        /// <summary>
        /// 直线方向上是否有敌方单位格（战技可命中预判——v1 近似：沿八向扫描至虚空/24 格，
        /// 我方所在格不阻挡直线弹射物（docs/11 统一拍板），首个敌格即截停）。
        /// 返回可命中方向（0=无）
        /// </summary>
        public static Direction2D FindLineSkillDirection(BattleSimState sim, Unit self, int skillIndex)
        {
            var rawData = self.RawData;
            if (rawData?.skills == null || skillIndex < 0 || skillIndex >= rawData.skills.Count)
                return 0;
            var selfId = self.GetUnitComponent<UnitIdentity>();
            if (selfId == null) return 0;
            var from = sim.GetPosition(self);

            // 八向逐一扫描（Direction2D 1~8）
            for (int dirValue = 1; dirValue <= 8; dirValue++)
            {
                var direction = (Direction2D)dirValue;
                var (dx, dy) = DeltaOf(direction);
                for (int step = 1; step <= 24; step++)
                {
                    var cell = new BattleCell(from.x + dx * step, from.y + dy * step);
                    if (!sim.Map.HasTile(cell.x, cell.y)) break; // 虚空=线终止
                    if (HasLivingEnemyAt(sim, cell, selfId.OwnerPlayerID))
                        return direction;
                }
            }
            return 0;
        }

        private static bool HasLivingEnemyAt(BattleSimState sim, BattleCell cell, string myPlayerId)
        {
            foreach (var kv in sim.Units)
            {
                var id = kv.Value.GetUnitComponent<UnitIdentity>();
                if (id == null || id.OwnerPlayerID == myPlayerId) continue;
                if (BattleSimState.IsDead(kv.Value)) continue;
                var pos = sim.GetPosition(kv.Value);
                if (pos.x == cell.x && pos.y == cell.y) return true;
            }
            return false;
        }
    }
}
