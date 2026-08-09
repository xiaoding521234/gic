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

            [Header("地图显示定位")]
            [Tooltip("地图原点：MapImage 在 Content 中的 anchoredPosition。所有锚点均以此为基准，图片尺寸变化时只需改此值")]
            public Vector2 mapImageAnchoredPosition;

            [Tooltip("MapImage 的 sizeDelta（图片尺寸）。图片替换后改此值，锚点自动按比例重新定位")]
            public Vector2 mapImageSize = new Vector2(21504, 13824.5625f);

            [Tooltip("Content 的 anchoredPosition（初始滚动位置）")]
            public Vector2 contentAnchoredPosition;

            [Tooltip("Content 的 sizeDelta（滚动范围）")]
            public Vector2 contentSizeDelta;

            [Header("锚点")]
            [Tooltip("归一化坐标 (0-1)：0=图片左/上，1=图片右/下。图片尺寸变化时自动适配")]
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
