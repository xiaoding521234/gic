using System;
using System.Collections.Generic;
using GIC.Framework;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Data
{


    /// <summary>
    /// 战场地形运行时数据（对局开始全量下发一次；地形变化 B8 走增量命令）
    /// </summary>
    [Serializable]
    public class BattleMapData
    {
        public int width;
        public int height;

        /// <summary>平铺地形类型（TileType 的 int 值，行优先：index = y * width + x）</summary>
        public List<int> tiles = new List<int>();

        /// <summary>玩家出生区（3x3 石路中心格）</summary>
        public List<BattleCell> spawnCenters = new List<BattleCell>();

        public bool InBounds(int x, int y) => x >= 0 && x < width && y >= 0 && y < height;

        /// <summary>
        /// 获取地形类型；越界视为虚空（不可通行不可放置）
        /// </summary>
        public TileType GetTile(int x, int y)
        {
            if (!InBounds(x, y)) return TileType.Plain; // 越界返回默认值，配合 HasTile 使用
            return (TileType)tiles[y * width + x];
        }

        /// <summary>
        /// 该格是否有地形（无地形 = 虚空格）
        /// </summary>
        public bool HasTile(int x, int y) => InBounds(x, y) && tiles[y * width + x] > 0;

        public void SetTile(int x, int y, TileType type)
        {
            if (!InBounds(x, y)) return;
            tiles[y * width + x] = (int)type;
        }

        /// <summary>
        /// 地形通行性判定（地形层）：按 ForceType 检查标签
        /// </summary>
        public bool IsPassable(int x, int y, ForceType forceType)
        {
            if (!HasTile(x, y)) return false; // 虚空不可通行

            var tile = GetTile(x, y);
            switch (forceType)
            {
                case ForceType.Walk:
                    return tile.AllowWalk();
                case ForceType.Fly:
                    return tile.AllowFly();
                case ForceType.Amphibious:
                    return tile.AllowWalk() || tile.HasTag(TileTag.WaterTerrain);
                case ForceType.Knockback:
                case ForceType.Pull:
                    return tile.AllowWalk(); // 强制位移按地面路径判定（牵引无视阻挡但受地形/体积约束）
                case ForceType.Jump:
                case ForceType.Teleport:
                    return true; // 跳跃/瞬移落点校验由调用方做
                default:
                    return false;
            }
        }
    }
}
