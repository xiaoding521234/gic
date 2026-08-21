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
            if (save == null)
            {
                // 无存档：至少强制默认全屏（防共享注册表残留的窗口尺寸）
                ApplyDefaultFullscreen();
                return;
            }

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
                ApplyDefaultFullscreen();
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

        /// <summary>
        /// 显式设为桌面原生分辨率全屏。只切 fullScreenMode 不设分辨率会继承注册表里的上次窗口尺寸——
        /// 桌宠子进程（同 exe --pet-mode，480x720 窗口化）会把竖屏尺寸写入共享的 Screenmanager 注册表，
        /// 主进程启动若不强制分辨率就会竖屏启动。
        /// </summary>
        public static void ApplyDefaultFullscreen()
        {
            var rs = Screen.resolutions;
            var native = rs.Length > 0 ? rs[rs.Length - 1] : new Resolution { width = 1920, height = 1080 };
            Screen.SetResolution(native.width, native.height, FullScreenMode.FullScreenWindow);
        }
    }
}
