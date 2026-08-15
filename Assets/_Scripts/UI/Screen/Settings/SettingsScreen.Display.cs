// ==================== SettingsScreen.Display.cs（显示设置：语言/分辨率/帧率） ====================
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using System.Collections;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
using GIC.Tool;
namespace GIC.UI
{


    public partial class SettingsScreen
    {
        private void InitDisplaySettings()
        {
            InitLanguageSetting();
            InitResolutionSetting();
            InitFrameRateSetting();
        }

        private void InitLanguageSetting()
        {
            var locales = LocalizationSettings.AvailableLocales.Locales;
            var languageOptions = new List<TextEntry>();

            string[] languageNames = new string[]
            {
                "璃月（简中）",
                "璃月（繁中）",
                "Mondstadt（English）",
                "稲妻（日語）",
                "Снежная（Русский）"
            };

            int currentIndex = 0;
            for (int i = 0; i < locales.Count && i < languageNames.Length; i++)
            {
                languageOptions.Add(new TextEntry(null, languageNames[i]));
                if (locales[i] == LocalizationSettings.SelectedLocale)
                {
                    currentIndex = i;
                }
            }

            int savedIndex = _saveManager.CurrentSave.languageIndex;
            if (savedIndex >= 0 && savedIndex < languageOptions.Count)
            {
                currentIndex = savedIndex;
            }

            languageSetting.Setup("Language", languageOptions, currentIndex, (index) =>
            {
                if (index >= 0 && index < locales.Count)
                {
                    LocalizationSettings.SelectedLocale = locales[index];
                    _saveManager.CurrentSave.languageIndex = index;
                    _saveManager.SaveGame();
                }
            });
            languageSetting.Initialize();
        }

        private void InitResolutionSetting()
        {
            var resolutions = Screen.resolutions;
            var resolutionOptions = new List<TextEntry>();

            resolutionOptions.Add(new TextEntry(new LocalizedString("UIText", "Fullscreen"), ""));

            for (int i = 0; i < resolutions.Length; i++)
            {
                string resText = $"{resolutions[i].width}x{resolutions[i].height}";
                resolutionOptions.Add(new TextEntry(null, resText));
            }

            int currentIndex = _saveManager.CurrentSave.resolutionIndex;

            resolutionSetting.Setup("Resolution", resolutionOptions, currentIndex, (index) =>
            {
                _saveManager.CurrentSave.resolutionIndex = index;
                _saveManager.SaveGame();

                if (index == 0)
                {
                    Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
                    var desktopRes = Screen.currentResolution;
                    Screen.SetResolution(desktopRes.width, desktopRes.height, FullScreenMode.FullScreenWindow);
                }
                else
                {
                    int resIndex = index - 1;
                    if (resIndex >= 0 && resIndex < resolutions.Length)
                    {
                        var res = resolutions[resIndex];
                        Screen.SetResolution(res.width, res.height, FullScreenMode.Windowed);
                    }
                }
            });
            resolutionSetting.Initialize();
        }

        private void InitFrameRateSetting()
        {
            int[] frameRates = { 60, 120, 144, 165, 240, 300 };
            var frameRateOptions = new List<TextEntry>();

            int currentIndex = 0;
            int currentFrameRate = Application.targetFrameRate;

            for (int i = 0; i < frameRates.Length; i++)
            {
                frameRateOptions.Add(new TextEntry(null, frameRates[i].ToString()));
                if (frameRates[i] == currentFrameRate)
                {
                    currentIndex = i;
                }
            }

            int savedIndex = _saveManager.CurrentSave.frameRate;
            for (int i = 0; i < frameRates.Length; i++)
            {
                if (frameRates[i] == savedIndex)
                {
                    currentIndex = i;
                    break;
                }
            }

            frameRateSetting.Setup("FrameRate", frameRateOptions, currentIndex, (index) =>
            {
                if (index >= 0 && index < frameRates.Length)
                {
                    Application.targetFrameRate = frameRates[index];
                    _saveManager.CurrentSave.frameRate = frameRates[index];
                    _saveManager.SaveGame();
                }
            });
            frameRateSetting.Initialize();
        }
    }
}
