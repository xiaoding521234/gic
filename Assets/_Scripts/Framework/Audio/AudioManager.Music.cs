using System;
using System.Collections;
using UnityEngine;
namespace GIC.Framework
{


    public partial class AudioManager
    {
        #region musicPlay

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
        /// 淡出当前曲目后起播新曲目（昼夜时段跨界立即切换用，2026-09-22）：旧曲按 fadeOutTime 淡出，
        /// 完毕后新曲按 track.fadeInTime 淡入（单音乐源顺序淡变，无交叉重叠）。
        /// 无在播曲目（间隔冷却中/静默）时跳过淡出直接起播（等价 PlayMusic）。
        /// 优先级/同曲跳过语义与 PlayMusic 一致。
        /// </summary>
        public void SwitchMusicWithFade(MusicTrack track, float fadeOutTime)
        {
            if (track.clip == null || musicSource == null) return;

            // 优先级：如果新音乐优先级低于当前音乐，丢弃
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

            bool wasPlaying = musicSource.isPlaying;
            StopCurrentMusic();

            // 残留淡变协程（上一轮淡入/跨界淡出）先行终止并音量归位——本请求接管音频
            if (musicFadeCoroutine != null)
            {
                StopCoroutine(musicFadeCoroutine);
                musicFadeCoroutine = null;
                musicSource.volume = 1f;
            }

            // 更新当前音乐信息（淡出期间元数据已属新曲——期间若被 Push/Pop，快照/恢复以新曲为准）
            currentMusicType = track.musicType;
            currentOnCompleteCallback = track.loop ? null : track.onComplete;
            currentMusicTrack = track;

            if (wasPlaying && fadeOutTime > 0f)
            {
                musicFadeCoroutine = StartCoroutine(FadeOutThenPlayCoroutine(track, fadeOutTime));
            }
            else if (track.intervalBefore > 0f)
            {
                StartDelayedMusic(track);
            }
            else
            {
                StartMusicPlayback(track);
            }
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
            playbackEpoch++; // 播放权换代：跨界淡出协程据此在起播前放弃（见字段注释）

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

        #region internalPlayLogic

        private Coroutine musicCompletionCoroutine;

        // 播放权世代（2026-09-22 跨界淡切）：StopCurrentMusic 每次递增。FadeOutThenPlayCoroutine
        // 淡出结束起播前校验——期间被任何接管（StopMusic 硬停/PlayMusic/Push/Pop）则放弃起播。
        // 必要性：既有 FadeOutMusicCoroutine 结束只 Stop 不播天然安全；跨界协程结束会起播新曲，
        // 不校验则"淡出期间退出战斗 StopMusic(0)"后残留曲会在退出后冒出来（且带活 onComplete 链）。
        private int playbackEpoch;

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
            // 残留淡出协程先行终止：新曲起播后它仍会按自己的时长把音量拖到 0 并在结束时
            // Stop+清 clip，误杀刚起播的新曲（2026-09-14 战斗无声实锤：开局 StopMusic(0.5f)
            // 淡出未走完时战斗曲已开播——PreWarm 后转场<0.5s 竞态；完成回调协程见 clip 不
            // 匹配静默退场，轮换链无声死亡）。StartFadeInMusic 已有同款终止，此处补齐非淡入
            // 路径；音量归位（非淡入态源音量恒 1）
            if (musicFadeCoroutine != null)
            {
                StopCoroutine(musicFadeCoroutine);
                musicFadeCoroutine = null;
                if (musicSource != null) musicSource.volume = 1f;
            }

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
            musicFadeCoroutine = null; // 自然结束清引用（同 FadeOutMusicCoroutine）
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
            musicFadeCoroutine = null; // 自然结束清引用：防字段持已完成协程的假引用误导现场取证
        }

        /// <summary>
        /// 跨界淡出→起播（SwitchMusicWithFade 的协程体，2026-09-22）：旧曲淡出到 0 → 起播新曲
        /// （fadeInTime>0 时经 StartMusicPlayback 自带淡入）。世代校验双保险：淡出途中被接管
        /// （唯一不杀本协程的路径=StopMusic(0) 硬停）→ 立即放弃，音量按「非淡入态源音量恒 1」
        /// 不变量归位，接管方/下一次起播拿到正确音量。
        /// </summary>
        private IEnumerator FadeOutThenPlayCoroutine(MusicTrack track, float fadeOutTime)
        {
            int epoch = playbackEpoch;
            float startVolume = musicSource.volume;
            float timer = 0f;
            while (timer < fadeOutTime)
            {
                if (musicSource == null) yield break;
                if (playbackEpoch != epoch)
                {
                    musicSource.volume = 1f;
                    musicFadeCoroutine = null;
                    yield break;
                }
                timer += Time.deltaTime;
                musicSource.volume = Mathf.Lerp(startVolume, 0f, timer / fadeOutTime);
                yield return null;
            }
            if (musicSource == null) yield break;
            if (playbackEpoch != epoch)
            {
                musicSource.volume = 1f;
                musicFadeCoroutine = null;
                yield break;
            }
            musicSource.Stop();
            musicSource.clip = null;
            musicSource.volume = 1f;
            musicFadeCoroutine = null;
            if (track.intervalBefore > 0f)
            {
                StartDelayedMusic(track);
            }
            else
            {
                StartMusicPlayback(track);
            }
        }

        #endregion
    }
}

