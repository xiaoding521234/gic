namespace GIC.Data
{

    // SpawnPositionType.cs - 出生点位置枚举

    /// <summary>
    /// 出生点位置（2026-10-11 联机房间正式化批收敛）：
    /// 数值=地图出生区序号（BattleMapConfig.spawnZones 列表序，1 起）——
    /// 房间内选点=进局内落位（RA2 式出生点）；Random=开局按序自动分配未占用区。
    /// 可选项数由当前地图 spawnZones.Count 过滤（UI 层）；枚举上限 8 与房间 maxPlayers=6 对齐。
    /// </summary>
    public enum SpawnPositionType
    {
        Random = 0,
        Zone1 = 1,
        Zone2 = 2,
        Zone3 = 3,
        Zone4 = 4,
        Zone5 = 5,
        Zone6 = 6,
        Zone7 = 7,
        Zone8 = 8,
    }
}
