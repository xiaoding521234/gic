using System;
using System.Collections;
using UnityEngine;

public partial class AudioManager
{
    #region 音量控制

    public void SetMasterVolume(float volume)
    {
        masterVolume = Mathf.Clamp01(volume);
        ApplyVolume(MASTER_VOLUME_PARAM, masterVolume);
        SaveVolumeSettings();
    }

    public void SetMusicVolume(float volume)
    {
        musicVolume = Mathf.Clamp01(volume);
        if (musicVolumeReductionCount > 0)
        {
            ApplyVolume(MUSIC_VOLUME_PARAM, musicVolume * reducedMusicVolumeScale);
        }
        else
        {
            ApplyVolume(MUSIC_VOLUME_PARAM, musicVolume);
        }
        SaveVolumeSettings();
    }

    public void SetSFXVolume(float volume)
    {
        sfxVolume = Mathf.Clamp01(volume);
        ApplyVolume(SFX_VOLUME_PARAM, sfxVolume);
        SaveVolumeSettings();
    }

    public void SetVoiceVolume(float volume)
    {
        voiceVolume = Mathf.Clamp01(volume);
        ApplyVolume(VOICE_VOLUME_PARAM, voiceVolume);
        SaveVolumeSettings();
    }

    public float GetMasterVolume() => masterVolume;
    public float GetMusicVolume() => musicVolume;
    public float GetSFXVolume() => sfxVolume;
    public float GetVoiceVolume() => voiceVolume;

    public void PushMusicVolume(bool immediate = false)
    {
        musicVolumeReductionCount++;

        if (musicVolumeReductionCount == 1)
        {
            float targetVolume = musicVolume * reducedMusicVolumeScale;
            SetMusicVolumeInternal(targetVolume, immediate);
        }
    }

    public void PopMusicVolume(bool immediate = false)
    {
        if (musicVolumeReductionCount > 0)
        {
            musicVolumeReductionCount--;
        }

        if (musicVolumeReductionCount == 0)
        {
            SetMusicVolumeInternal(musicVolume, immediate);
        }
    }

    public void ResetMusicVolumeReduction()
    {
        if (musicVolumeReductionCount > 0)
        {
            musicVolumeReductionCount = 0;
            SetMusicVolumeInternal(musicVolume, true);
        }
    }

    public void SetReducedMusicVolumeScale(float scale)
    {
        reducedMusicVolumeScale = Mathf.Clamp01(scale);

        if (musicVolumeReductionCount > 0)
        {
            float targetVolume = musicVolume * reducedMusicVolumeScale;
            SetMusicVolumeInternal(targetVolume, true);
        }
    }

    public float GetCurrentMusicVolume()
    {
        if (musicVolumeReductionCount > 0)
        {
            return musicVolume * reducedMusicVolumeScale;
        }
        return musicVolume;
    }

    /// <summary>
    /// 从存档加载音量设置并立即应用
    /// </summary>
    public void LoadVolumeSettings()
    {
        var saveManager = Wargame.Instance?.SaveManager;
        if (saveManager?.CurrentSave == null) return;

        var save = saveManager.CurrentSave;

        masterVolume = save.masterVolume;
        musicVolume = save.bgmVolume;
        sfxVolume = save.sfxVolume;
        voiceVolume = save.voiceVolume;

        ApplyAllVolumes();

        Debug.Log($"音量设置已加载: 总={masterVolume:F2} 音乐={musicVolume:F2} 音效={sfxVolume:F2} 语音={voiceVolume:F2}");
    }

    /// <summary>
    /// 保存当前音量设置到存档
    /// </summary>
    private void SaveVolumeSettings()
    {
        var saveManager = Wargame.Instance?.SaveManager;
        if (saveManager?.CurrentSave == null) return;

        var save = saveManager.CurrentSave;
        save.masterVolume = masterVolume;
        save.bgmVolume = musicVolume;
        save.sfxVolume = sfxVolume;
        save.voiceVolume = voiceVolume;

        saveManager.SaveGame();
    }

    private void SetMusicVolumeInternal(float targetVolume, bool immediate)
    {
        if (immediate)
        {
            ApplyVolume(MUSIC_VOLUME_PARAM, targetVolume);
        }
        else
        {
            if (musicVolumeTransitionCoroutine != null)
            {
                StopCoroutine(musicVolumeTransitionCoroutine);
            }
            musicVolumeTransitionCoroutine = StartCoroutine(SmoothMusicVolumeTransition(targetVolume, 0.5f));
        }
    }

    private IEnumerator SmoothMusicVolumeTransition(float targetVolume, float duration)
    {
        float currentDB;
        if (!audioMixer.GetFloat(MUSIC_VOLUME_PARAM, out currentDB))
        {
            currentDB = 0f;
        }
        float startVolume = Mathf.Pow(10f, currentDB / 20f);

        float timer = 0f;
        while (timer < duration)
        {
            timer += Time.deltaTime;
            float t = timer / duration;
            float currentVolume = Mathf.Lerp(startVolume, targetVolume, t);
            ApplyVolume(MUSIC_VOLUME_PARAM, currentVolume);
            yield return null;
        }

        ApplyVolume(MUSIC_VOLUME_PARAM, targetVolume);
    }

    #endregion
}