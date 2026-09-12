using System;
using UnityEngine;
using GIC.Framework;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
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

        public bool Equals(BattleCell other) => x == other.x && y == other.y;
        public override bool Equals(object obj) => obj is BattleCell other && Equals(other);
        public override int GetHashCode() => (x * 397) ^ y;

        public static BattleCell zero => new BattleCell(0, 0);

        public override string ToString() => $"({x},{y})";
    }
}
