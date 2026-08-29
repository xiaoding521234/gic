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
        #region sfxPlay

        public void PlaySFX(AudioClip clip, float volumeScale = 1f)
        {
            if (clip == null) return;

            AudioSource availableSource = GetAvailableSFXSource();
            if (availableSource != null)
            {
                availableSource.clip = clip;
                availableSource.volume = volumeScale;
                availableSource.Play();
            }
        }

        public void PlaySFXOneShot(AudioClip clip, float volumeScale = 1f)
        {
            if (clip != null && sfxSource != null)
            {
                sfxSource.PlayOneShot(clip, volumeScale);
            }
        }

        private AudioSource GetAvailableSFXSource()
        {
            foreach (var source in sfxSourcePool)
            {
                if (!source.isPlaying)
                {
                    return source;
                }
            }

            AudioSource newSource = CreatePooledSFXSource();
            return newSource;
        }

        private AudioSource CreatePooledSFXSource()
        {
            GameObject sfxObject = new GameObject("SFX_Source_" + sfxSourcePool.Count);
            sfxObject.transform.SetParent(transform);
            AudioSource source = sfxObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            SetAudioSource2D(source);

            if (audioMixer != null)
            {
                var groups = audioMixer.FindMatchingGroups("SFX");
                if (groups.Length > 0)
                {
                    source.outputAudioMixerGroup = groups[0];
                }
            }

            sfxSourcePool.Add(source);
            return source;
        }

        private void CleanupUnusedSFXSources()
        {
            for (int i = sfxSourcePool.Count - 1; i >= SFX_POOL_SIZE; i--)
            {
                if (!sfxSourcePool[i].isPlaying)
                {
                    if (sfxSourcePool[i] != null && sfxSourcePool[i].gameObject != null)
                    {
                        Destroy(sfxSourcePool[i].gameObject);
                    }
                    sfxSourcePool.RemoveAt(i);
                }
            }
        }

        #endregion
    }
}

