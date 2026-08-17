using System;
using System.Collections.Generic;
using UnityEngine;

namespace GIC.Data
{
    /// <summary>
    /// 大地图配置（原神式固定世界坐标系）：
    /// - 锚点/区域视野存**绝对世界 XZ 坐标**，扩图后永不需要改
    /// - 地图图片通过 mapOrigin + worldUnitsPerPixel 两个标定参数摆进世界框架
    /// - 扩图/换图流程：替换 all_map.jpg → 手动调整这两个标定参数（使旧内容对齐原位）→
    ///   Tools/地图/应用地图标定（编辑器可视化）。存量锚点/区域数据一律不动
    /// </summary>
    [CreateAssetMenu(fileName = "MapConfig", menuName = "GIC/Map Config")]
    public class MapConfig : ScriptableObject
    {
        [Header("地图标定（固定世界坐标系，扩图后只改这两项）")]
        [Tooltip("原点坐标：地图图片左上角在世界坐标（XZ）中的位置。扩图后调整此值，使旧内容对齐原位（如向北扩 100 单位，则 y 增加 100）")]
        [SerializeField] private Vector2 mapOrigin = new Vector2(-107.52f, 69.12f);

        [Tooltip("坐标单位长度：每图片像素对应的世界单位数。当前图宽 10752px = 215.04 世界单位 → 0.02")]
        [SerializeField] private float worldUnitsPerPixel = 0.02f;

        [SerializeField] private List<RegionData> regions = new();

        private Dictionary<RegionName, RegionData> _cache;

        /// <summary>图片左上角的世界 XZ 坐标</summary>
        public Vector2 MapOrigin => mapOrigin;

        /// <summary>每图片像素的世界单位数</summary>
        public float WorldUnitsPerPixel => worldUnitsPerPixel;

        /// <summary>世界 XZ 坐标 → 图片像素坐标（左上原点，向下为 +y；供编辑器取点工具对照 Photoshop）</summary>
        public Vector2 WorldToPixel(Vector2 worldXZ)
        {
            return new Vector2(
                (worldXZ.x - mapOrigin.x) / worldUnitsPerPixel,
                (mapOrigin.y - worldXZ.y) / worldUnitsPerPixel);
        }

        [Serializable]
        public class RegionData
        {
            public RegionName region;

            [Header("区域视野（3D 相机）")]
            [Tooltip("区域视野中心（固定世界 XZ 坐标，扩图不变）。相机聚焦此点，锚点为空的区域也能定位")]
            public Vector2 viewCenterWorld = Vector2.zero;

            [Tooltip("区域视野尺寸（正交相机垂直半高，世界单位）。0 = 使用相机默认视野尺寸")]
            public float viewHeight;

            [Header("锚点")]
            [Tooltip("锚点位置存固定世界 XZ 坐标（换算公式：worldX = mapOrigin.x + px×单位长度，worldZ = mapOrigin.y + py×单位长度，px/py 为图片左上起像素）")]
            public List<AnchorData> anchors = new();
        }

        [Serializable]
        public class AnchorData
        {
            public PositionName positionName;

            [Tooltip("锚点位置（固定世界 XZ 坐标，扩图不变）")]
            public Vector2 world;

            public AnchorData() { }

            public AnchorData(PositionName name, Vector2 worldXZ)
            {
                positionName = name;
                world = worldXZ;
            }
        }

        public RegionData GetRegion(RegionName region)
        {
            if (_cache == null) BuildCache();
            _cache.TryGetValue(region, out var data);
            return data;
        }

        /// <summary>全部区域数据（全图锚点常驻生成用）</summary>
        public IReadOnlyList<RegionData> AllRegions => regions;

        public void BuildCache()
        {
            _cache = new Dictionary<RegionName, RegionData>();
            foreach (var r in regions)
            {
                if (!_cache.ContainsKey(r.region))
                    _cache.Add(r.region, r);
            }
        }
    }
}
