using System;
using UnityEngine;
namespace GIC.Data
{


    /// <summary>
    /// 连续坐标/格尺寸常量（决策九十五 P2，docs/active/48 §3.1——置于 GIC.Data：BattleCell 同址、
    /// Battle 层可引用勿反向）；1 格=1 米=100 厘米=1 世界单位
    /// </summary>
    public static class CellMetrics
    {
        /// <summary>格边长（厘米）</summary>
        public const int CellSizeCm = 100;

        /// <summary>格心偏移（厘米）=CellSizeCm/2</summary>
        public const int CellHalfCm = 50;
    }

    /// <summary>
    /// 战场格子坐标（协议平铺结构，替代 Vector2Int 以保持跨端可序列化）
    /// </summary>
    [Serializable]
    public struct BattleCell : IEquatable<BattleCell>
    {
        public int x;
        public int y;

        public BattleCell(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        public BattleCell(Vector2Int position) : this(position.x, position.y) { }

        public Vector2Int ToVector2Int() => new Vector2Int(x, y);

        public static BattleCell operator +(BattleCell a, BattleCell b) => new BattleCell(a.x + b.x, a.y + b.y);
        public static BattleCell operator -(BattleCell a, BattleCell b) => new BattleCell(a.x - b.x, a.y - b.y);

        /// <summary>切比雪夫距离（八方向等价度量，docs/03 §3.3）——**决策九十五 P5 已退役**：
        /// 全链欧氏迁移收官（蓝图 §3.1），AI 三脑/光环/溢出转移/部署消费方全部改走
        /// BattleHeuristics.DistCm / SquaredEuclideanCmTo（2026-11-04 终检零引用后删除方法体；
        /// 本注记保历史口径——旧回放/文档检索锚）</summary>

        /// <summary>格心欧氏距离平方（厘米²，决策九十五欧氏迁移几何单源——**全整数零开方**，确定性
        /// 纪律不破：Math.Sqrt 跨平台浮点差异被平方比较绕开）。消费口径：①「距离≤R 格」判定=
        /// 本值 ≤ (R×CellSizeCm)²；②「最近」比较=平方值单调等价直比；③需要真距离显示时再开方
        /// （仅表现层）。半径语义（光环/视野/部署/溢出转移）迁移时按本口改，勿散抄 dx*dx</summary>
        public long SquaredEuclideanCmTo(BattleCell other)
        {
            long dx = (long)(x - other.x) * CellMetrics.CellSizeCm;
            long dy = (long)(y - other.y) * CellMetrics.CellSizeCm;
            return dx * dx + dy * dy;
        }

        /// <summary>本格格心连续坐标（厘米）——决策九十五：格→连续双轨中格心的标准表达</summary>
        public int CenterXCm => x * CellMetrics.CellSizeCm + CellMetrics.CellHalfCm;
        public int CenterYCm => y * CellMetrics.CellSizeCm + CellMetrics.CellHalfCm;

        /// <summary>连续坐标（厘米）→所在格（坐标恒第一象限，整除安全）；越界值由调用方判 HasTile</summary>
        public static BattleCell FromCm(int xCm, int zCm) => new BattleCell(xCm / CellMetrics.CellSizeCm, zCm / CellMetrics.CellSizeCm);

        public bool Equals(BattleCell other) => x == other.x && y == other.y;
        public override bool Equals(object obj) => obj is BattleCell other && Equals(other);
        public override int GetHashCode() => (x * 397) ^ y; // DETERMINISM-ALLOW: 值类型哈希仅作字典键；结算遍历已改 SortedUnits/SortedResourceNodes

        public static BattleCell zero => new BattleCell(0, 0);

        public override string ToString() => $"({x},{y})";
    }
}
