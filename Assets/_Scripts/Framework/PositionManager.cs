using System;
using System.Collections;
using System.Collections.Generic;
using GIC.Data.Event;
using UnityEngine;

using static GIC.Data.PositionConfig;
using GIC.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Framework
{


    [Component]
    public class PositionManager : IWargameManager
    {
        private readonly SaveManager saveManager;
        private readonly PositionConfig positionConfig;
        private AudioManager audioManager;

        public PositionManager(SaveManager saveManager, PositionConfig positionConfig)
        {
            this.saveManager = saveManager;
            this.positionConfig = positionConfig;
        }

        // 音乐间隔时间（秒）
        private const float MUSIC_INTERVAL = 10f;

        // 公共属性
        public PositionName CurrentPosition
        {
            get => (PositionName)saveManager.CurrentSave.currentPosition;
            set
            {
                if (CurrentPosition != value)
                {
                    saveManager.SetPosition(value);
                    OnPositionChanged();
                }
            }
        }

        // 当前位置的详细信息
        public PositionData CurrentPositionData => positionConfig?.GetPositionData(CurrentPosition);

        [PostConstruct]
        public void Init()
        {
            audioManager = AudioManager.Instance;

            // 初始播放当前位置的音乐
            PlayCurrentPositionMusic();

            Debug.Log($"PositionManager 启动完成，当前位置: {CurrentPosition}");
        }

        public void Start() { }

        public void Update(float deltaTime)
        {
        }

        private void OnPositionChanged()
        {
            Debug.Log($"位置改变: {CurrentPosition}");

            // 切换位置时播放新位置的音乐
            PlayCurrentPositionMusic();

            EventBusHub.Instance.SendImmediate(new OnPositionChangedEvent
            {
                PositionName = CurrentPosition
            });
        }

        /// <summary>
        /// 播放当前位置的音乐（根据白天/黑夜自动选择，播放完毕后间隔5秒播放下一首）
        /// </summary>
        public void PlayCurrentPositionMusic()
        {
            if (audioManager == null) return;

            var positionData = CurrentPositionData;
            if (positionData == null) return;

            TimePeriod timePeriod = TimeUtility.GetCurrentTimePeriod();
            AudioClip clip = null;

            switch (timePeriod)
            {
                case TimePeriod.Daytime:
                    clip = positionData.dayAudios?.GetRandomClip();
                    break;
                case TimePeriod.Night:
                    clip = positionData.nightAudios?.GetRandomClip();
                    break;
            }

            if (clip != null)
            {
                audioManager.PlayMusicWithInterval(
                    clip,
                    MusicType.Relaxed,
                    intervalAfter: MUSIC_INTERVAL,
                    loop: false,
                    onComplete: OnCurrentPositionMusicComplete
                );
            }
            else
            {
                Debug.LogWarning($"位置 {CurrentPosition} 在 {timePeriod} 时段没有配置音乐");
            }
        }

        /// <summary>
        /// 当前音乐播放完毕的回调，自动播放下一首
        /// </summary>
        private void OnCurrentPositionMusicComplete()
        {
            var positionData = CurrentPositionData;
            if (positionData == null) return;

            TimePeriod timePeriod = TimeUtility.GetCurrentTimePeriod();
            AudioClip nextClip = null;

            switch (timePeriod)
            {
                case TimePeriod.Daytime:
                    nextClip = positionData.dayAudios?.GetRandomClip();
                    break;
                case TimePeriod.Night:
                    nextClip = positionData.nightAudios?.GetRandomClip();
                    break;
            }

            if (nextClip != null)
            {
                audioManager.PlayMusicWithInterval(
                    nextClip,
                    MusicType.Relaxed,
                    intervalAfter: MUSIC_INTERVAL,
                    loop: false,
                    onComplete: OnCurrentPositionMusicComplete
                );
            }
        }

        /// <summary>
        /// 移动到指定位置
        /// </summary>
        public bool MoveToPosition(PositionName targetPosition)
        {
            var targetData = positionConfig?.GetPositionData(targetPosition);
            if (targetData == null)
            {
                Debug.LogError($"找不到位置: {targetPosition}");
                return false;
            }

            if (!targetData.isUnlocked)
            {
                Debug.Log($"位置未解锁: {targetPosition}");
                return false;
            }

            CurrentPosition = targetPosition;

            Debug.Log($"移动到: {targetPosition}");
            // 场景切换由调用方（MapScreen）负责，这里只更新位置数据
            return true;
        }

        /// <summary>
        /// 解锁位置
        /// </summary>
        public bool UnlockPosition(PositionName position)
        {
            var data = positionConfig?.GetPositionData(position);
            if (data != null && !data.isUnlocked)
            {
                data.isUnlocked = true;
                Debug.Log($"解锁位置: {position}");
                return true;
            }
            return false;
        }

        /// <summary>
        /// 检查位置是否解锁
        /// </summary>
        public bool IsPositionUnlocked(PositionName position)
        {
            var data = positionConfig?.GetPositionData(position);
            return data != null && data.isUnlocked;
        }

        /// <summary>
        /// 获取位置数据
        /// </summary>
        public PositionData GetPositionData(PositionName position)
        {
            return positionConfig?.GetPositionData(position);
        }

        /// <summary>
        /// 获取当前所在区域
        /// </summary>
        public RegionName GetCurrentRegion()
        {
            return CurrentPositionData?.region ?? RegionName.Mondstadt;
        }

        /// <summary>
        /// 获取当前区域的锚点列表
        /// </summary>
        public List<PositionData> GetCurrentRegionPositions()
        {
            var result = new List<PositionData>();
            if (positionConfig == null) return result;

            foreach (var data in positionConfig.mapDataList)
            {
                if (data.region == CurrentPositionData.region)
                    result.Add(data);
            }
            return result;
        }

        /// <summary>
        /// 获取指定区域的锚点列表
        /// </summary>
        public List<PositionData> GetPositionsByRegion(RegionName region)
        {
            var result = new List<PositionData>();
            if (positionConfig == null) return result;

            foreach (var data in positionConfig.mapDataList)
            {
                if (data.region == region)
                    result.Add(data);
            }
            return result;
        }
    }
}



