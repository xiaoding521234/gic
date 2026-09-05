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
            // InitOtherSettings（petCloseSetting 连带关闭）2026-08-27 挪入"派蒙"栏 → SettingsScreen.Pet.cs
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

            int savedIndex = _saveManager.CurrentSave.settings.languageIndex;
            if (savedIndex >= 0 && savedIndex < languageOptions.Count)
            {
                currentIndex = savedIndex;
            }

            languageSetting.Setup("Language", languageOptions, currentIndex, (index) =>
            {
                if (index >= 0 && index < locales.Count)
                {
                    LocalizationSettings.SelectedLocale = locales[index];
                    _saveManager.Modify(s => s.settings.languageIndex = index);   // 统一变更入口（2026-09-05 Modify 迁移）
                }
            });
            languageSetting.Initialize();
        }

        private void InitResolutionSetting()
        {
            // 去重列表与 SettingsApplier.ApplyFromSave 共用（存档 resolutionIndex 两端映射同一列表，
            // 机制见 SettingsApplier.GetUniqueResolutions 注释——不去重=每档分辨率出现两次）
            var resolutions = SettingsApplier.GetUniqueResolutions();
            var resolutionOptions = new List<TextEntry>();

            resolutionOptions.Add(new TextEntry(new LocalizedString("UIText", "Fullscreen"), ""));

            // 移动端只有全屏（分辨率/窗口化是桌面概念——移动端画面恒原生全屏，
            // 压扁防护见 SettingsApplier.ApplyDefaultFullscreen 头注释）
            if (!Application.isMobilePlatform)
            {
                for (int i = 0; i < resolutions.Count; i++)
                {
                    resolutionOptions.Add(new TextEntry(null, $"{resolutions[i].width}x{resolutions[i].height}"));
                }
            }

            // clamp：存档索引可能越界（去重后列表变短/移动端列表只剩全屏），DropdownSettingItem 不做越界钳制
            int currentIndex = Mathf.Clamp(_saveManager.CurrentSave.settings.resolutionIndex, 0, resolutionOptions.Count - 1);

            resolutionSetting.Setup("Resolution", resolutionOptions, currentIndex, (index) =>
            {
                _saveManager.Modify(s => s.settings.resolutionIndex = index);   // 统一变更入口（2026-09-05 Modify 迁移）

                // 移动端不做运行时 SetResolution（压扁防护，机制见 SettingsApplier.ApplyDefaultFullscreen 头注释）
                if (Application.isMobilePlatform) return;

                if (index == 0)
                {
                    Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
                    var desktopRes = Screen.currentResolution;
                    Screen.SetResolution(desktopRes.width, desktopRes.height, FullScreenMode.FullScreenWindow);
                }
                else
                {
                    int resIndex = index - 1;
                    if (resIndex >= 0 && resIndex < resolutions.Count)
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

            int savedIndex = _saveManager.CurrentSave.settings.frameRate;
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
                    _saveManager.Modify(s => s.settings.frameRate = frameRates[index]);   // 统一变更入口（2026-09-05 Modify 迁移）
                }
            });
            frameRateSetting.Initialize();
        }
    }
}
