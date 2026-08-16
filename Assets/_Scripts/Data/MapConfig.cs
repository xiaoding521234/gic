using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Framework;
using GIC.UI;

namespace GIC.Data
{
    [CreateAssetMenu(fileName = "MapConfig", menuName = "GIC/Map Config")]
    public class MapConfig : ScriptableObject
    {
        [SerializeField] private List<RegionData> regions = new();

        private Dictionary<RegionName, RegionData> _cache;

        [Serializable]
        public class RegionData
        {
            public RegionName region;

            [Header("区域视野（3D 相机）")]
            [Tooltip("区域视野中心（归一化 0-1：0=地图左/上，1=地图右/下）。相机聚焦此点，锚点为空的区域也能定位")]
            public Vector2 viewCenter = new Vector2(0.5f, 0.5f);

            [Tooltip("区域视野尺寸（正交相机垂直半高，世界单位）。0 = 使用相机默认视野尺寸")]
            public float viewHeight;

            [Header("锚点")]
            [Tooltip("归一化坐标 (0-1)：0=地图左/上，1=地图右/下。地图尺寸变化时自动适配")]
            public List<AnchorData> anchors = new();
        }

        [Serializable]
        public class AnchorData
        {
            public PositionName positionName;
            [Range(0, 1)] public float normalizedX;
            [Range(0, 1)] public float normalizedY;

            public AnchorData() { }

            public AnchorData(PositionName name, float x, float y)
            {
                positionName = name;
                normalizedX = x;
                normalizedY = y;
            }
        }

        public RegionData GetRegion(RegionName region)
        {
            if (_cache == null) BuildCache();
            _cache.TryGetValue(region, out var data);
            return data;
        }

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
