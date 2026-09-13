using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Framework;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Data
{


    /// <summary>
    /// 玩家出生区（3x3 石路区域）
    /// </summary>
    [Serializable]
    public class SpawnZone
    {
        [InspectorName("玩家ID")]
        public string playerId;

        [InspectorName("区域中心格")]
        public BattleCell center;
    }

    /// <summary>
    /// 战场地图配置（字符行布局，可读可手编）
    /// 字符约定：G=草地 W=水 S=石路 P=平原 .=虚空（无地形）
    /// </summary>
    [CreateAssetMenu(fileName = "BattleMapConfig", menuName = "Game/BattleMapConfig")]
    public class BattleMapConfig : ScriptableObject
    {
        [Header("地图名")]
        public string mapName = "未命名战场";

        [Header("所属势力")]
        [Tooltip("地图所属势力：开局加载页按此显示对应势力徽标")]
        [InspectorName("所属势力")]
        public FactionType faction = FactionType.Mondstadt;

        [Header("尺寸")]
        public int width = 20;
        public int height = 20;

        [Header("地形布局（每行一个字符串，字符数须等于宽度）")]
        [TextArea]
        public List<string> rows = new List<string>();

        [Header("玩家出生区（3x3 石路中心格）")]
        public List<SpawnZone> spawnZones = new List<SpawnZone>();

        /// <summary>
        /// 解析为运行时地形数据；布局非法时返回 null 并报错
        /// </summary>
        public BattleMapData BuildData()
        {
            if (rows == null || rows.Count != height)
            {
                GICLog.Error($"[BattleMapConfig] {mapName} 行数 {rows?.Count ?? 0} != 高度 {height}");
                return null;
            }

            var data = new BattleMapData { width = width, height = height };
            data.tiles.Clear();
            for (int y = 0; y < height; y++)
            {
                var row = rows[y];
                if (row.Length != width)
                {
                    GICLog.Error($"[BattleMapConfig] {mapName} 第 {y} 行字符数 {row.Length} != 宽度 {width}");
                    return null;
                }
                for (int x = 0; x < width; x++)
                {
                    data.tiles.Add((int)CharToTile(row[x]));
                }
            }

            data.spawnCenters.Clear();
            foreach (var zone in spawnZones)
                data.spawnCenters.Add(zone.center);

            return data;
        }

        private static TileType CharToTile(char c)
        {
            switch (c)
            {
                case 'G': return TileType.Grassland;
                case 'P': return TileType.Plain;
                case 'W': return TileType.Water;
                case 'S': return TileType.StonePath;
                case '.': return (TileType)0; // 虚空：无地形
                default:
                    GICLog.Warn($"[BattleMapConfig] 未知地形字符 '{c}'，按虚空处理");
                    return (TileType)0;
            }
        }
    }
}
