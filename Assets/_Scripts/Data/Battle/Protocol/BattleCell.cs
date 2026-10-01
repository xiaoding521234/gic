using System;
using UnityEngine;
namespace GIC.Data
{


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

        /// <summary>切比雪夫距离（八方向等价度量，docs/03 §3.3）——战斗域半径/逼近/转移类判定的
        /// 几何单源（光环 Buff 半径过滤、溢出转移找最近、AI 逼近等统一走此口，勿散抄双 Math.Abs）</summary>
        public int ChebyshevTo(BattleCell other) => Math.Max(Math.Abs(x - other.x), Math.Abs(y - other.y));

        public bool Equals(BattleCell other) => x == other.x && y == other.y;
        public override bool Equals(object obj) => obj is BattleCell other && Equals(other);
        public override int GetHashCode() => (x * 397) ^ y;

        public static BattleCell zero => new BattleCell(0, 0);

        public override string ToString() => $"({x},{y})";
    }
}
