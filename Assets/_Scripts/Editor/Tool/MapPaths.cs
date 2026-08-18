#if UNITY_EDITOR
// MapPaths.cs - 大地图相关资产路径常量（编辑器工具共用的唯一来源）
// 勿在各工具内再散落硬编码路径副本——换图/挪目录时漏改一处即静默分叉（v7.0 错位事故同源教训）。
namespace GIC.Editor
{
    internal static class MapPaths
    {
        /// <summary>高清母版（Unity 外，不进 Assets；>16384 编辑器导入会 OOM）</summary>
        public const string 母版 = "Export/all_map_source.jpg";

        /// <summary>编辑器目测用全图副本（取点器临时换上，不进构建）</summary>
        public const string 全图副本 = "Assets/Art/Map/Textures/all_map.jpg";

        /// <summary>场景 MapPlane 常驻低清预览图</summary>
        public const string 预览图 = "Assets/Art/Map/Textures/all_map_preview.jpg";

        public const string 瓦片目录 = "Assets/Art/Map/Tiles";
        public const string MapConfig = "Assets/Resources/Configs/MapConfig.asset";
        public const string MapScreen场景 = "Assets/Scenes/MapScreen.unity";
    }
}
#endif
