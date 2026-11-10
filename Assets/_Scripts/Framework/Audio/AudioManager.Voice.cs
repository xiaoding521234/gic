using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Framework
{


    public partial class AudioManager
    {
        #region voicePlay

        public void PlayVoice(UnitName unitName, AudioClip clip, float volumeScale = 1f)
        {
            if (clip == null) return;

            StopVoice(unitName);

            AudioSource availableSource = GetAvailableVoiceSource();
            if (availableSource != null)
            {
                availableSource.pitch = 1f; // 复位：一次性语音会改 pitch，复用池源时不残留
                availableSource.clip = clip;
                availableSource.volume = volumeScale;
                availableSource.Play();

                unitVoiceMap[unitName] = availableSource;
            }
        }

        public void StopVoice(UnitName unitName)
        {
            if (unitVoiceMap.TryGetValue(unitName, out AudioSource source))
            {
                if (source != null && source.isPlaying)
                {
                    source.Stop();
                    source.clip = null;
                }
                unitVoiceMap.Remove(unitName);
            }
        }

        public void StopAllVoices()
        {
            foreach (var kvp in unitVoiceMap)
            {
                if (kvp.Value != null)
                {
                    kvp.Value.Stop();
                    kvp.Value.clip = null;
                }
            }
            unitVoiceMap.Clear();

            if (voiceSource != null && voiceSource.isPlaying)
            {
                voiceSource.Stop();
                voiceSource.clip = null;
            }
        }

        public bool IsVoicePlaying(UnitName unitName)
        {
            if (unitVoiceMap.TryGetValue(unitName, out AudioSource source))
            {
                return source != null && source.isPlaying;
            }
            return false;
        }

        public bool IsAnyVoicePlaying()
        {
            foreach (var kvp in unitVoiceMap)
            {
                if (kvp.Value != null && kvp.Value.isPlaying)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 一次性语音（战斗语音调度器专用，2026-11-10）：走同一 Voice 池与混音通道，
        /// 但不登记 unitVoiceMap、不按单位去重——「什么时候播、播谁」由调度器统一裁决，
        /// 这里只负责出声（含音高微扰）。**不触碰音乐**（用户拍板：语音不影响音乐）。
        /// </summary>
        public void PlayVoiceOneShot(AudioClip clip, float volumeScale = 1f, float pitch = 1f)
        {
            if (clip == null) return;

            AudioSource source = GetAvailableVoiceSource();
            if (source == null) return;

            source.clip = clip;
            source.volume = volumeScale;
            source.pitch = Mathf.Clamp(pitch, 0.5f, 2f);
            source.Play();
        }

        public void PlayVoiceExclusive(AudioClip clip, float volumeScale = 1f)
        {
            if (clip == null || voiceSource == null) return;

            StopAllVoices();

            voiceSource.clip = clip;
            voiceSource.volume = volumeScale;
            voiceSource.Play();
        }

        public void StopVoiceExclusive()
        {
            if (voiceSource != null && voiceSource.isPlaying)
            {
                voiceSource.Stop();
                voiceSource.clip = null;
            }
        }

        public bool IsVoiceExclusivePlaying()
        {
            return voiceSource != null && voiceSource.isPlaying;
        }

        public int GetActiveVoiceCount()
        {
            CleanupFinishedVoices();
            return unitVoiceMap.Count;
        }

        private void CleanupFinishedVoices()
        {
            var finishedUnits = new List<UnitName>();

            foreach (var kvp in unitVoiceMap)
            {
                if (kvp.Value == null || !kvp.Value.isPlaying)
                {
                    finishedUnits.Add(kvp.Key);
                }
            }

            foreach (var unit in finishedUnits)
            {
                unitVoiceMap.Remove(unit);
            }
        }

        private AudioSource GetAvailableVoiceSource()
        {
            foreach (var source in voiceSourcePool)
            {
                if (source != null && !source.isPlaying)
                {
                    return source;
                }
            }

            AudioSource newSource = CreatePooledVoiceSource();
            return newSource;
        }

        private AudioSource CreatePooledVoiceSource()
        {
            GameObject voiceObject = new GameObject("Voice_Source_" + voiceSourcePool.Count);
            voiceObject.transform.SetParent(transform);
            AudioSource source = voiceObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            SetAudioSource2D(source);

            if (audioMixer != null)
            {
                var groups = audioMixer.FindMatchingGroups("Voice");
                if (groups.Length > 0)
                {
                    source.outputAudioMixerGroup = groups[0];
                }
            }

            voiceSourcePool.Add(source);
            return source;
        }

        #endregion
    }
}


