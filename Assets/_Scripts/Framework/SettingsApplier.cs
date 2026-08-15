using UnityEngine;
using UnityEngine.Localization.Settings;
using GIC.Data;

namespace GIC.Framework
{
    /// <summary>
    /// 显示设置应用 — 从存档把语言/帧率/分辨率应用到运行环境。
    /// GameScene 启动时调用；修改职责归 SettingsScreen（直接调 Unity API 即时生效）。
    /// </summary>
    public static class SettingsApplier
    {
        public static void ApplyFromSave(PlayerSaveData save)
        {
            if (save == null) return;

            // 应用语言
            var locales = LocalizationSettings.AvailableLocales.Locales;
            if (save.languageIndex >= 0 && save.languageIndex < locales.Count)
            {
                LocalizationSettings.SelectedLocale = locales[save.languageIndex];
            }

            // 应用帧率
            Application.targetFrameRate = save.frameRate;

            // 应用分辨率
            if (save.resolutionIndex == 0)
            {
                // 全屏
                Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
            }
            else
            {
                // 窗口化指定分辨率
                var resolutions = Screen.resolutions;
                int resIndex = save.resolutionIndex - 1;
                if (resIndex >= 0 && resIndex < resolutions.Length)
                {
                    var res = resolutions[resIndex];
                    Screen.SetResolution(res.width, res.height, FullScreenMode.Windowed);
                }
            }
        }
    }
}
