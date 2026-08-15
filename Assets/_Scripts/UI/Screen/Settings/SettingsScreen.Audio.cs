// ==================== SettingsScreen.Audio.cs（声音设置：音量滑条） ====================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
using GIC.Tool;
namespace GIC.UI
{


    public partial class SettingsScreen
    {
        private void InitSoundSettings()
        {
            var audioManager = AudioManager.Instance;

            masterVolumeSetting.Setup("MasterVolume", 0f, 10f, audioManager.GetMasterVolume() * 10f, (value) =>
            {
                audioManager.SetMasterVolume(value / 10f);
            }, showAsPercent: false, stepSize: 1f);
            masterVolumeSetting.Initialize();

            musicVolumeSetting.Setup("MusicVolume", 0f, 10f, audioManager.GetMusicVolume() * 10f, (value) =>
            {
                audioManager.SetMusicVolume(value / 10f);
            }, showAsPercent: false, stepSize: 1f);
            musicVolumeSetting.Initialize();

            sfxVolumeSetting.Setup("SFXVolume", 0f, 10f, audioManager.GetSFXVolume() * 10f, (value) =>
            {
                audioManager.SetSFXVolume(value / 10f);
            }, showAsPercent: false, stepSize: 1f);
            sfxVolumeSetting.Initialize();

            voiceVolumeSetting.Setup("VoiceVolume", 0f, 10f, audioManager.GetVoiceVolume() * 10f, (value) =>
            {
                audioManager.SetVoiceVolume(value / 10f);
            }, showAsPercent: false, stepSize: 1f);
            voiceVolumeSetting.Initialize();
        }
    }
}
