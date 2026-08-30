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

        // 当前曲目按哪个时段选的（游戏内时间变更事件判定用——同时段调时间不打断当前曲）
        private TimePeriod? _currentMusicPeriod;
        private GameTimeChangedHandler _gameTimeHandler;

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

            // 游戏内时间变更（派蒙对话 set_game_time 工具，2026-08-29）：时段变了立即换当前
            // 位置的时段曲（对齐 OnPositionChanged 的立即换曲语义）。Manager 与容器同寿命，
            // 订阅常驻不退订（RoomManager 同款）。主进程形态才有意义——桌面宠进程不初始化容器。
            _gameTimeHandler = new GameTimeChangedHandler(this);
            EventBusHub.Instance.Subscribe(_gameTimeHandler, this);

            // 初始播放当前位置的音乐
            PlayCurrentPositionMusic();

            GICLog.Info($"PositionManager 启动完成，当前位置: {CurrentPosition}");
        }

        /// <summary>游戏内时间变更处理器：时段变了才重新选曲（白天→夜晚/夜晚→白天立即切；
        /// 白天内 14 点调 15 点等同时段变更不打断正在播的曲子）</summary>
        private class GameTimeChangedHandler : IEventHandler<OnGameTimeChangedEvent>
        {
            private readonly PositionManager _manager;

            public GameTimeChangedHandler(PositionManager manager)
            {
                _manager = manager;
            }

            public bool CanHandle(OnGameTimeChangedEvent evt)
            {
                return _manager != null;
            }

            public void Handle(OnGameTimeChangedEvent evt)
            {
                if (_manager._currentMusicPeriod == evt.NewPeriod) return;
                _manager.PlayCurrentPositionMusic();
            }
        }

        public void Start() { }

        public void Update(float deltaTime)
        {
        }

        private void OnPositionChanged()
        {
            GICLog.Info($"位置改变: {CurrentPosition}");

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
            _currentMusicPeriod = timePeriod;
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
                GICLog.Warn($"位置 {CurrentPosition} 在 {timePeriod} 时段没有配置音乐");
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
                GICLog.Error($"找不到位置: {targetPosition}");
                return false;
            }

            if (!targetData.isUnlocked)
            {
                GICLog.Info($"位置未解锁: {targetPosition}");
                return false;
            }

            CurrentPosition = targetPosition;

            GICLog.Info($"移动到: {targetPosition}");
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
                GICLog.Info($"解锁位置: {position}");
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



