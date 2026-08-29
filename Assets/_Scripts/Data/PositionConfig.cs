using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using GIC.Framework;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Data
{

    /// <summary>
    /// 位置背景媒体类型
    /// </summary>
    public enum PositionMediaType
    {
        Image = 0,
        Video = 1
    }

    /// <summary>
    /// 背景音乐主题（对应 Assets/Resources/Audios/Daytime|Night/ 下的文件名前缀）。
    /// 新增音频主题时：① 加枚举值 ② 音频文件按 {snake_case}_daytime_0.ogg 命名放入对应目录
    /// </summary>
    public enum AudioTheme
    {
        [InspectorName("无")] None = 0,
        [InspectorName("雷波岛")] LempoIsle = 1,
        [InspectorName("那夏镇")] NashaTown = 2,
        [InspectorName("至冬堡")] SnezhnayaCastle = 3,
    }

    /// <summary>
    /// 位置背景媒体配置（数据驱动：在 Inspector 中配置每个时段用图片还是视频）
    /// </summary>
    [System.Serializable]
    public class PositionMedia
    {
        [Tooltip("Image = 静态图片（普通锚点），Video = 循环视频（城市等重要锚点）。有视频时优先使用视频")]
        public PositionMediaType mediaType = PositionMediaType.Image;
    }

    [CreateAssetMenu(fileName = "PositionConfig", menuName = "Game/PositionConfig")]
    public class PositionConfig : ScriptableObject
    {
        [System.Serializable]
        public class PositionData
        {
            public PositionName position = PositionName.StormterrorLair;
            public RegionName region = RegionName.Mondstadt;

            public List<string> taskNames;
            public AudioTheme audioTheme = AudioTheme.None;
            public AudioClipRandom dayAudios;
            public AudioClipRandom nightAudios;
            public bool isUnlocked = false;

            [Header("背景媒体（数据驱动）")]
            [Tooltip("白天背景媒体类型")]
            public PositionMedia dayMedia = new PositionMedia();
            [Tooltip("夜晚背景媒体类型")]
            public PositionMedia nightMedia = new PositionMedia();

            #region localization Entry 获取方法

            /// <summary>
            /// 获取地点名称的 Entry（用于 TextCombiner）
            /// </summary>
            public TextEntry GetNameEntry()
            {
                return position.GetEntry();
            }

            /// <summary>
            /// 获取地点所属区域的 Entry（用于 TextCombiner）
            /// </summary>
            public TextEntry GetRegionEntry()
            {
                return region.GetEntry();
            }

            /// <summary>
            /// 获取地点描述的 Entry（用于 TextCombiner）
            /// </summary>
            public TextEntry GetDescriptionEntry()
            {
                var localizedString = new LocalizedString(TableName.PositionName.ToString(), position.ToString());
                return new TextEntry(localizedString, "");
            }

            #endregion

            #region bgMediaAddr（dataDriven）

            /// <summary>
            /// 获取指定时段的媒体类型
            /// </summary>
            public PositionMediaType GetMediaType(TimePeriod timePeriod)
            {
                return (timePeriod == TimePeriod.Daytime ? dayMedia : nightMedia).mediaType;
            }

            /// <summary>
            /// 获取指定时段的背景 Addressables 地址。
            /// 图片: PositionBack/{region}/{position}_{daytime|night}
            /// 视频: PositionVideo/{region}/{position}_{daytime|night}
            /// </summary>
            public string GetBackgroundAddress(TimePeriod timePeriod)
            {
                var media = timePeriod == TimePeriod.Daytime ? dayMedia : nightMedia;
                string regionStr = region.ToString();
                string positionStr = position.ToString().ToSnakeCase();
                string timeSuffix = timePeriod == TimePeriod.Daytime ? "daytime" : "night";
                string prefix = media.mediaType == PositionMediaType.Video ? "PositionVideo" : "PositionBack";
                return $"{prefix}/{regionStr}/{positionStr}_{timeSuffix}";
            }

            #endregion
        }

        public List<PositionData> mapDataList;
        private Dictionary<PositionName, PositionData> dataCache;

        public void BuildCache()
        {
            dataCache = new Dictionary<PositionName, PositionData>();
            foreach (var data in mapDataList)
            {
                if (!dataCache.ContainsKey(data.position))
                {
                    dataCache.Add(data.position, data);
                }
            }
        }

        public PositionData GetPositionData(PositionName position)
        {
            if (dataCache == null) BuildCache();

            if (dataCache.TryGetValue(position, out var data))
                return data;

            GICLog.Warn($"[PositionConfig] 未找到位置: {position}");
            return null;
        }
    }
}



