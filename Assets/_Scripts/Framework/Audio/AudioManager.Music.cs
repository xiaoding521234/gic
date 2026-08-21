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
        #region 音乐播放

        // 当前播放的音乐信息
        private MusicType currentMusicType = MusicType.Relaxed;
        private Action currentOnCompleteCallback;

        /// <summary>
        /// 播放音乐（简单版本）
        /// </summary>
        public void PlayMusic(AudioClip clip, MusicType musicType = MusicType.Relaxed, bool loop = true, float fadeInTime = 0f, Action onComplete = null)
        {
            PlayMusic(new MusicTrack(clip, musicType, 0f, 0f, loop, fadeInTime, onComplete));
        }

        /// <summary>
        /// 播放音乐（完整版本）
        /// 根据MusicType优先级决定是否顶掉当前音乐
        /// </summary>
        public void PlayMusic(MusicTrack track)
        {
            if (track.clip == null || musicSource == null) return;

            // 检查优先级：如果新音乐优先级低于当前音乐，丢弃
            if (track.musicType < currentMusicType)
            {
                GICLog.Info($"音乐被丢弃：{track.clip.name}（优先级{track.musicType} < 当前{currentMusicType}）");
                return;
            }

            // 如果优先级相同且正在播放同一首，跳过
            if (track.musicType == currentMusicType && 
                musicSource.isPlaying && 
                musicSource.clip == track.clip)
            {
                GICLog.Info($"同一首音乐已在播放：{track.clip.name}");
                return;
            }

            // 停止当前音乐
            StopCurrentMusic();
            
            // 更新当前音乐信息
            currentMusicType = track.musicType;
            currentOnCompleteCallback = track.loop ? null : track.onComplete;
            currentMusicTrack = track;

            // 开始播放
            if (track.intervalBefore > 0f)
            {
                StartDelayedMusic(track);
            }
            else
            {
                StartMusicPlayback(track);
            }
        }

        /// <summary>
        /// 播放带间隔的音乐
        /// </summary>
        public void PlayMusicWithInterval(
            AudioClip clip,
            MusicType musicType = MusicType.Relaxed,
            float intervalBefore = 0f,
            float intervalAfter = 0f,
            bool loop = true,
            float fadeInTime = 0f,
            Action onComplete = null)
        {
            PlayMusic(new MusicTrack(clip, musicType, intervalBefore, intervalAfter, loop, fadeInTime, onComplete));
        }

        /// <summary>
        /// 停止音乐（带淡出）
        /// </summary>
        public void StopMusic(float fadeOutTime = 0f)
        {
            StopCurrentMusic();
            
            if (musicSource == null) return;

            if (fadeOutTime > 0)
            {
                StartFadeOutMusic(fadeOutTime);
            }
            else
            {
                musicSource.Stop();
                musicSource.clip = null;
            }

            // 重置状态
            currentMusicType = MusicType.Relaxed;
            currentOnCompleteCallback = null;
        }

        /// <summary>
        /// 停止所有音乐（包括循环协程），但不重置MusicType
        /// </summary>
        private void StopCurrentMusic()
        {
            if (musicLoopCoroutine != null)
            {
                StopCoroutine(musicLoopCoroutine);
                musicLoopCoroutine = null;
            }

            if (musicCompletionCoroutine != null)
            {
                StopCoroutine(musicCompletionCoroutine);
                musicCompletionCoroutine = null;
            }

            // 间隔冷却等待随生命周期协程一并终止，清除时间戳
            intervalEndTime = -1f;

            // 触发之前的回调（如果被中断）
            if (currentOnCompleteCallback != null && musicSource != null && musicSource.isPlaying)
            {
                GICLog.Info("音乐被中断，不触发完成回调");
                currentOnCompleteCallback = null;
            }
        }

        /// <summary>
        /// 暂停音乐
        /// </summary>
        public void PauseMusic()
        {
            if (musicSource != null && musicSource.isPlaying)
            {
                musicSource.Pause();
            }
        }

        /// <summary>
        /// 恢复音乐
        /// </summary>
        public void ResumeMusic()
        {
            if (musicSource != null && musicSource.clip != null)
            {
                musicSource.UnPause();
            }
        }

        /// <summary>
        /// 获取当前播放的音乐类型
        /// </summary>
        public MusicType GetCurrentMusicType() => currentMusicType;

        /// <summary>
        /// 判断音乐是否正在播放
        /// </summary>
        public bool IsMusicPlaying()
        {
            return musicSource != null && musicSource.isPlaying;
        }

        /// <summary>
        /// 获取当前播放的音乐片段
        /// </summary>
        public AudioClip GetCurrentMusicClip()
        {
            return musicSource != null ? musicSource.clip : null;
        }

        /// <summary>
        /// 设置音乐音调
        /// </summary>
        public void SetMusicPitch(float pitch)
        {
            if (musicSource != null)
            {
                musicSource.pitch = Mathf.Clamp(pitch, 0.1f, 3f);
            }
        }

        /// <summary>
        /// 重置音乐音调
        /// </summary>
        public void ResetMusicPitch()
        {
            if (musicSource != null)
            {
                musicSource.pitch = 1f;
            }
        }

        #endregion

        #region 内部播放逻辑

        private Coroutine musicCompletionCoroutine;

        private void StartDelayedMusic(MusicTrack track)
        {
            musicLoopCoroutine = StartCoroutine(DelayedMusicCoroutine(track));
        }

        private IEnumerator DelayedMusicCoroutine(MusicTrack track, float remainingInterval = 0f)
        {
            // remainingInterval > 0：从快照恢复，只等剩余延迟
            float wait = remainingInterval > 0f ? remainingInterval : track.intervalBefore;
            intervalEndTime = Time.time + wait;
            yield return Wait.Seconds(wait);
            intervalEndTime = -1f;
            StartMusicPlayback(track);
        }

        private void StartMusicPlayback(MusicTrack track)
        {
            if (track.fadeInTime > 0)
            {
                StartFadeInMusic(track);
            }
            else
            {
                musicSource.clip = track.clip;
                musicSource.loop = false; // 使用自定义循环逻辑
                musicSource.Play();
            }

            // 循环播放或带播完回调：启动生命周期协程（间隔冷却时间戳由协程内部维护）
            StartMusicLifecycleCoroutines(track);
        }

        private IEnumerator MusicLoopCoroutine(MusicTrack track, float remainingInterval = 0f)
        {
            // remainingInterval > 0：从快照恢复，歌曲已播完，跳过等播完只等剩余间隔
            bool resumedFromInterval = remainingInterval > 0f;

            while (true)
            {
                if (resumedFromInterval)
                {
                    resumedFromInterval = false;

                    intervalEndTime = Time.time + remainingInterval;
                    yield return Wait.Seconds(remainingInterval);
                    intervalEndTime = -1f;
                }
                else
                {
                    // 等待当前音乐播放完毕
                    yield return new WaitWhile(() => musicSource != null && musicSource.isPlaying);

                    // 如果被停止或更换音乐，退出循环
                    if (musicSource == null || musicSource.clip != track.clip)
                        yield break;

                    // 等待后置间隔
                    if (track.intervalAfter > 0f)
                    {
                        intervalEndTime = Time.time + track.intervalAfter;
                        yield return Wait.Seconds(track.intervalAfter);
                        intervalEndTime = -1f;

                        // 再次检查是否被中断
                        if (musicSource == null || musicSource.clip != track.clip)
                            yield break;
                    }
                }

                // 重新播放
                musicSource.clip = track.clip;
                musicSource.loop = false;
                musicSource.Play();
            }
        }

        /// <summary>
        /// 音乐播放完毕回调协程（不循环时使用）
        /// </summary>
        private IEnumerator MusicCompletionCoroutine(MusicTrack track, float remainingInterval = 0f)
        {
            if (remainingInterval <= 0f)
            {
                // 等待音乐播放完毕（从间隔快照恢复时跳过：歌曲已播完）
                yield return new WaitWhile(() => musicSource != null && musicSource.isPlaying);
            }

            // 等待后置间隔（remainingInterval > 0 时只等剩余冷却）
            if (track.intervalAfter > 0f || remainingInterval > 0f)
            {
                float wait = remainingInterval > 0f ? remainingInterval : track.intervalAfter;
                intervalEndTime = Time.time + wait;
                yield return Wait.Seconds(wait);
                intervalEndTime = -1f;
            }

            // 确认没有被中断（clip没有被改变）
            if (musicSource != null && musicSource.clip == track.clip && !musicSource.isPlaying)
            {
                GICLog.Info($"音乐播放完毕：{track.clip.name}");
                
                // 触发回调
                track.onComplete?.Invoke();
                
                // 清除回调引用
                if (currentOnCompleteCallback == track.onComplete)
                {
                    currentOnCompleteCallback = null;
                }
            }
        }

        private void StartFadeInMusic(MusicTrack track)
        {
            if (musicFadeCoroutine != null)
            {
                StopCoroutine(musicFadeCoroutine);
            }
            musicFadeCoroutine = StartCoroutine(FadeInMusicCoroutine(track));
        }

        private IEnumerator FadeInMusicCoroutine(MusicTrack track)
        {
            musicSource.clip = track.clip;
            musicSource.loop = false;
            musicSource.volume = 0f;
            musicSource.Play();

            float timer = 0f;
            while (timer < track.fadeInTime)
            {
                timer += Time.deltaTime;
                musicSource.volume = Mathf.Lerp(0f, 1f, timer / track.fadeInTime);
                yield return null;
            }
            musicSource.volume = 1f;
        }

        private void StartFadeOutMusic(float duration)
        {
            if (musicFadeCoroutine != null)
            {
                StopCoroutine(musicFadeCoroutine);
            }
            musicFadeCoroutine = StartCoroutine(FadeOutMusicCoroutine(duration));
        }

        private IEnumerator FadeOutMusicCoroutine(float duration)
        {
            float startVolume = musicSource.volume;
            float timer = 0f;
            while (timer < duration)
            {
                timer += Time.deltaTime;
                musicSource.volume = Mathf.Lerp(startVolume, 0f, timer / duration);
                yield return null;
            }
            musicSource.Stop();
            musicSource.clip = null;
            musicSource.volume = startVolume;
        }

        #endregion
    }
}

