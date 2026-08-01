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


    [CreateAssetMenu(fileName = "PositionConfig", menuName = "Game/PositionConfig")]
    public class PositionConfig : ScriptableObject
    {
        [System.Serializable]
        public class PositionData
        {
            public PositionName position = PositionName.StormterrorLair;
            public RegionName region = RegionName.Mondstadt;

            public List<string> taskNames;
            public string audioPrefix;
            public AudioClipRandom dayAudios;
            public AudioClipRandom nightAudios;
            public bool isUnlocked = false;

            #region 本地化 Entry 获取方法

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

            Debug.LogWarning($"[PositionConfig] 未找到位置: {position}");
            return null;
        }
    }
}



