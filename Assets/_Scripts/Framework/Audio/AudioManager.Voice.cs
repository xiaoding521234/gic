using System;
using System.Collections.Generic;
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
        #region 语音播放

        public void PlayVoice(UnitName unitName, AudioClip clip, float volumeScale = 1f)
        {
            if (clip == null) return;

            StopVoice(unitName);

            AudioSource availableSource = GetAvailableVoiceSource();
            if (availableSource != null)
            {
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


