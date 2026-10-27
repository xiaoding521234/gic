using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Framework;
namespace GIC.Data
{


    /// <summary>
    /// 玩家出生区（3x3 石路区域）。2026-10-11 联机房间正式化批重键：
    /// zoneIndex=出生区序号（1 起，列表序即序号）——房间内「出生位置」下拉按此序选点（RA2 式），
    /// 退役旧 playerId 字符串匹配键（P1/P2 写死两方，无法承载多点与 N 玩家）。
    /// </summary>
    [Serializable]
    public class SpawnZone
    {
        [InspectorName("出生区序号")]
        public int zoneIndex;

        [InspectorName("区域中心格")]
        public BattleCell center;
    }

    /// <summary>
    /// 战场地图配置（字符行布局，可读可手编）
    /// 字符约定：G=草地 W=水 S=石路 P=平原 D=土地 C=圆石 .=虚空（无地形）
    /// $=摩拉堆（docs/47 资源点批，2026-10-27：该格地形回落草地 + 开局摩拉堆
    /// 〔ResourceNodeMetrics.MoraPileMaxCharges 次可采〕——非地形非单位不占体积）
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

        [Header("绑定大地图位置")]
        [Tooltip("战斗地图对应的大地图锚点/位置：正式进入战斗地图后播放该位置配置的音乐（PositionConfig 昼夜曲池，按当前游戏内时段选池）")]
        [InspectorName("绑定位置")]
        public PositionName position = PositionName.StarfellLake;

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
                    char c = row[x];
                    if (c == '$')
                    {
                        // '$'=摩拉堆（docs/47）：地形回落草地 + 资源点登记（每格至多一个；
                        // 采集余量变化走 ResourceHarvest 命令增量+回合头快照自愈）
                        data.tiles.Add((int)TileType.Grassland);
                        data.resourceNodes.Add(new ResourceNodeState
                        {
                            kind = (int)ResourceNodeKind.MoraPile,
                            cell = new BattleCell(x, y),
                            remainingCharges = ResourceNodeMetrics.MoraPileMaxCharges,
                        });
                        continue;
                    }
                    data.tiles.Add((int)CharToTile(c));
                }
            }

            data.spawnCenters.Clear();
            foreach (var zone in spawnZones)
                data.spawnCenters.Add(zone.center);

            data.faction = (int)faction; // 协议携带势力：客户端 HUD 徽标等表现层取用（B7 客户端无 mapConfig 资产）

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
                case 'D': return TileType.Dirt;
                case 'C': return TileType.Cobblestone;
                case '.': return TileType.None; // 虚空：无地形
                default:
                    GICLog.Warn($"[BattleMapConfig] 未知地形字符 '{c}'，按虚空处理");
                    return TileType.None;
            }
        }
    }
}
