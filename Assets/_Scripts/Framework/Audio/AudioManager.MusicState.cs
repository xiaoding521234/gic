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
        /// 音乐状态快照
        /// </summary>
        private struct MusicState
        {
            public MusicTrack track;
            public MusicType musicType;
            public float playbackTime;
            public bool wasPlaying;
            public Action onComplete;

            public MusicState(MusicTrack track, MusicType musicType, float playbackTime, bool wasPlaying, Action onComplete)
            {
                this.track = track;
                this.musicType = musicType;
                this.playbackTime = playbackTime;
                this.wasPlaying = wasPlaying;
                this.onComplete = onComplete;
            }
        }

        /// <summary>
        /// 保存当前音乐状态并切换到新音乐
        /// </summary>
        public void PushMusicState(MusicTrack newTrack, bool savePlaybackPosition = true)
        {
            // 保存当前音乐状态
            MusicState currentState;
            
            if (musicSource != null && musicSource.clip != null)
            {
                currentState = new MusicState(
                    currentMusicTrack,
                    currentMusicType,
                    savePlaybackPosition && musicSource.isPlaying ? musicSource.time : 0f,
                    musicSource.isPlaying,
                    currentOnCompleteCallback
                );
            }
            else
            {
                currentState = new MusicState(
                    new MusicTrack(null, MusicType.Relaxed),
                    MusicType.Relaxed,
                    0f,
                    false,
                    null
                );
            }
            
            musicStateStack.Push(currentState);
            
            // 停止当前音乐
            StopCurrentMusic();
            
            // 保存暂停状态
            bool wasPaused = isMusicPaused;
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
            yield return new WaitForSeconds(delay);
            RestoreMusicState(state, fadeInTime);
        }

        private void RestoreMusicState(MusicState state, float fadeInTime)
        {
            if (musicSource == null) return;

            // 恢复音乐类型和回调
            currentMusicType = state.musicType;
            currentOnCompleteCallback = state.onComplete;

            if (state.wasPlaying)
            {
                currentMusicTrack = state.track;
                
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

                    if (state.track.loop)
                    {
                        musicLoopCoroutine = StartCoroutine(MusicLoopCoroutine(state.track));
                    }
                    else if (state.track.onComplete != null)
                    {
                        musicCompletionCoroutine = StartCoroutine(MusicCompletionCoroutine(state.track));
                    }
                }
            }
            else
            {
                musicSource.clip = state.track.clip;
                musicSource.time = state.playbackTime;
                musicSource.loop = false;
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

            if (state.track.loop)
            {
                musicLoopCoroutine = StartCoroutine(MusicLoopCoroutine(state.track));
            }
            else if (state.track.onComplete != null)
            {
                musicCompletionCoroutine = StartCoroutine(MusicCompletionCoroutine(state.track));
            }
        }

        #endregion
    }
}

