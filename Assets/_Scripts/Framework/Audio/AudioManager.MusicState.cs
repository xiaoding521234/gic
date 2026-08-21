using System;
using System.Collections;
using UnityEngine;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Framework
{


    public partial class AudioManager
    {
        #region 音乐状态保存与恢复

        /// <summary>
        /// 音乐生命周期阶段（用于快照/恢复）
        /// </summary>
        private enum MusicPhase
        {
            None,          // 无活跃生命周期（未播放，或自然结束且冷却已过）
            DelayBefore,   // intervalBefore 等待中（曲目尚未起播）
            Playing,       // 正在播放
            Paused,        // 已暂停（PauseMusic）
            IntervalAfter  // 播完后的 intervalAfter 冷却等待中
        }

        /// <summary>
        /// 音乐状态快照
        /// </summary>
        private struct MusicState
        {
            public MusicTrack track;
            public MusicType musicType;
            public MusicPhase phase;
            public float playbackTime;    // Playing/Paused 时的播放进度
            public float pendingInterval; // DelayBefore/IntervalAfter 时的剩余等待秒数
            public Action onComplete;

            public MusicState(MusicTrack track, MusicType musicType, MusicPhase phase, float playbackTime, float pendingInterval, Action onComplete)
            {
                this.track = track;
                this.musicType = musicType;
                this.phase = phase;
                this.playbackTime = playbackTime;
                this.pendingInterval = pendingInterval;
                this.onComplete = onComplete;
            }
        }

        /// <summary>
        /// 保存当前音乐状态并切换到新音乐
        /// </summary>
        public void PushMusicState(MusicTrack newTrack, bool savePlaybackPosition = true)
        {
            // 快照当前音乐的生命周期阶段（含间隔冷却剩余秒数），Pop 时据此复活播放链
            float remainingInterval = GetRemainingIntervalTime();
            MusicState currentState;

            if (currentMusicTrack.clip != null &&
                currentMusicTrack.intervalBefore > 0f &&
                musicSource != null && musicSource.clip != currentMusicTrack.clip &&
                remainingInterval > 0f)
            {
                // intervalBefore 等待期：曲目尚未起播
                currentState = new MusicState(
                    currentMusicTrack, currentMusicType, MusicPhase.DelayBefore,
                    0f, remainingInterval, currentOnCompleteCallback);
            }
            else if (musicSource != null && musicSource.clip != null)
            {
                MusicPhase phase;
                float playbackTime = 0f;

                if (isMusicPaused)
                {
                    phase = MusicPhase.Paused;
                }
                else if (musicSource.isPlaying)
                {
                    phase = MusicPhase.Playing;
                    playbackTime = savePlaybackPosition ? musicSource.time : 0f;
                }
                else if (remainingInterval > 0f && currentMusicTrack.clip == musicSource.clip)
                {
                    // 播完后的 intervalAfter 冷却期（生命周期协程挂起中）
                    phase = MusicPhase.IntervalAfter;
                }
                else
                {
                    phase = MusicPhase.None;
                }

                currentState = new MusicState(
                    currentMusicTrack, currentMusicType, phase,
                    playbackTime, remainingInterval, currentOnCompleteCallback);
            }
            else
            {
                currentState = new MusicState(
                    new MusicTrack(null, MusicType.Relaxed), MusicType.Relaxed,
                    MusicPhase.None, 0f, 0f, null);
            }

            musicStateStack.Push(currentState);

            // 停止当前音乐
            StopCurrentMusic();

            isMusicPaused = false;

            if (musicSource != null && musicSource.isPlaying && savePlaybackPosition)
            {
                musicSource.Pause();
            }

            // 播放新音乐（使用高优先级确保能顶掉）
            PlayMusic(newTrack);
        }

        /// <summary>
        /// 保存当前音乐并切换（简化版本）
        /// </summary>
        public void PushMusicState(AudioClip clip, MusicType musicType = MusicType.Exclusive, bool loop = true, float fadeInTime = 0f)
        {
            PushMusicState(new MusicTrack(clip, musicType, 0f, 0f, loop, fadeInTime));
        }

        /// <summary>
        /// 恢复之前保存的音乐状态
        /// </summary>
        public void PopMusicState(float fadeOutTime = 0f, float fadeInTime = 0.5f)
        {
            if (musicStateStack.Count == 0)
            {
                GICLog.Warn("没有可恢复的音乐状态");
                return;
            }

            MusicState previousState = musicStateStack.Pop();

            // 停止当前音乐
            StopCurrentMusic();
            
            if (fadeOutTime > 0)
            {
                StartFadeOutMusic(fadeOutTime);
            }
            else
            {
                if (musicSource != null)
                {
                    musicSource.Stop();
                }
            }

            // 恢复之前的音乐
            if (previousState.track.clip != null)
            {
                if (fadeOutTime > 0)
                {
                    StartCoroutine(DelayedRestoreMusic(previousState, fadeOutTime, fadeInTime));
                }
                else
                {
                    RestoreMusicState(previousState, fadeInTime);
                }
            }
            else
            {
                if (musicSource != null)
                {
                    musicSource.clip = null;
                }
                currentMusicType = MusicType.Relaxed;
                isMusicPaused = false;
            }
        }

        /// <summary>
        /// 清空所有保存的音乐状态
        /// </summary>
        public void ClearMusicStateStack()
        {
            musicStateStack.Clear();
        }

        /// <summary>
        /// 获取当前保存的音乐状态数量
        /// </summary>
        public int GetMusicStateStackCount()
        {
            return musicStateStack.Count;
        }

        private IEnumerator DelayedRestoreMusic(MusicState state, float delay, float fadeInTime)
        {
            yield return Wait.Seconds(delay);
            RestoreMusicState(state, fadeInTime);
        }

        private void RestoreMusicState(MusicState state, float fadeInTime)
        {
            if (musicSource == null) return;

            // 恢复音乐类型、回调与当前曲目
            currentMusicType = state.musicType;
            currentOnCompleteCallback = state.onComplete;
            currentMusicTrack = state.track;

            switch (state.phase)
            {
                case MusicPhase.Playing:
                    if (fadeInTime > 0)
                    {
                        StartCoroutine(RestoreMusicWithFadeIn(state, fadeInTime));
                    }
                    else
                    {
                        musicSource.clip = state.track.clip;
                        musicSource.time = state.playbackTime;
                        musicSource.loop = false;
                        musicSource.Play();

                        StartMusicLifecycleCoroutines(state.track);
                    }
                    break;

                case MusicPhase.IntervalAfter:
                    // 歌曲已自然播完：接上剩余冷却，冷却结束后继续 onComplete 链/下一轮循环
                    musicSource.clip = state.track.clip;
                    musicSource.loop = false;
                    if (state.pendingInterval > 0f)
                    {
                        GICLog.Info($"恢复音乐冷却：{state.track.clip.name} 剩余 {state.pendingInterval:F1}s");
                    }
                    if (state.track.loop)
                    {
                        musicLoopCoroutine = StartCoroutine(MusicLoopCoroutine(state.track, state.pendingInterval));
                    }
                    else
                    {
                        musicCompletionCoroutine = StartCoroutine(MusicCompletionCoroutine(state.track, state.pendingInterval));
                    }
                    break;

                case MusicPhase.DelayBefore:
                    // 起播前延迟：接上剩余延迟后起播
                    musicLoopCoroutine = StartCoroutine(DelayedMusicCoroutine(state.track, state.pendingInterval));
                    break;

                case MusicPhase.Paused:
                case MusicPhase.None:
                default:
                    // 恢复为已加载未播放状态（等待 ResumeMusic / 保持静默）
                    musicSource.clip = state.track.clip;
                    musicSource.time = state.playbackTime;
                    musicSource.loop = false;
                    break;
            }

            isMusicPaused = false;
        }

        private IEnumerator RestoreMusicWithFadeIn(MusicState state, float duration)
        {
            musicSource.clip = state.track.clip;
            musicSource.time = state.playbackTime;
            musicSource.loop = false;
            musicSource.volume = 0f;
            musicSource.Play();

            float timer = 0f;
            while (timer < duration)
            {
                timer += Time.deltaTime;
                musicSource.volume = Mathf.Lerp(0f, 1f, timer / duration);
                yield return null;
            }
            musicSource.volume = 1f;

            StartMusicLifecycleCoroutines(state.track);
        }

        /// <summary>
        /// 重启曲目的生命周期协程（循环播放或播完回调，含各自的间隔冷却）
        /// </summary>
        private void StartMusicLifecycleCoroutines(MusicTrack track)
        {
            if (track.loop)
            {
                musicLoopCoroutine = StartCoroutine(MusicLoopCoroutine(track));
            }
            else if (track.onComplete != null)
            {
                musicCompletionCoroutine = StartCoroutine(MusicCompletionCoroutine(track));
            }
        }

        /// <summary>
        /// 当前间隔等待（intervalBefore/intervalAfter）的剩余秒数；无活跃等待时为 0
        /// </summary>
        private float GetRemainingIntervalTime()
        {
            if (intervalEndTime < 0f) return 0f;
            float remaining = intervalEndTime - Time.time;
            return remaining > 0f ? remaining : 0f;
        }

        #endregion
    }
}

