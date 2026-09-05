using System.Collections.Generic;
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
            if (save.settings.languageIndex >= 0 && save.settings.languageIndex < locales.Count)
            {
                LocalizationSettings.SelectedLocale = locales[save.settings.languageIndex];
            }

            // 应用帧率
            Application.targetFrameRate = save.settings.frameRate;

            // 应用分辨率（桌面专属）：移动端原生全屏恒定，运行时 SetResolution 会拉伸压扁——
            // 见 ApplyDefaultFullscreen 头注释
            if (!Application.isMobilePlatform)
            {
                if (save.settings.resolutionIndex == 0)
                {
                    // 全屏
                    ApplyDefaultFullscreen();
                }
                else
                {
                    // 窗口化指定分辨率（索引=设置下拉的序，两端必须共用同一去重列表——见 GetUniqueResolutions）
                    var resolutions = GetUniqueResolutions();
                    int resIndex = save.settings.resolutionIndex - 1;
                    if (resIndex >= 0 && resIndex < resolutions.Count)
                    {
                        var res = resolutions[resIndex];
                        Screen.SetResolution(res.width, res.height, FullScreenMode.Windowed);
                    }
                }
            }
        }

        /// <summary>
        /// 显式设为桌面原生分辨率全屏。只切 fullScreenMode 不设分辨率会继承注册表里的上次窗口尺寸——
        /// 桌宠子进程（同 exe --pet-mode，480x720 窗口化）会把竖屏尺寸写入共享的 Screenmanager 注册表，
        /// 主进程启动若不强制分辨率就会竖屏启动。
        /// 移动端恒跳过（2026-09-02 真机实证压扁根因）：手机面板是竖屏原生而游戏横屏锁定，
        /// Android 上 Screen.resolutions 报告的分辨率与横屏显示宽高比失配（空表时还会回退 1920×1080），
        /// SetResolution 强设失配分辨率会被拉伸铺满整屏 = 画面严重压扁。移动端画面恒原生全屏，
        /// 分辨率/窗口化是桌面概念。
        /// </summary>
        public static void ApplyDefaultFullscreen()
        {
            if (Application.isMobilePlatform) return;

            var rs = Screen.resolutions;
            var native = rs.Length > 0 ? rs[rs.Length - 1] : new Resolution { width = 1920, height = 1080 };
            Screen.SetResolution(native.width, native.height, FullScreenMode.FullScreenWindow);
        }

        /// <summary>去重后的可选分辨率列表（2026-09-02 修复"每种分辨率在下拉出现两次"）。
        /// Screen.resolutions 常含同尺寸不同刷新率的重复项（Windows 多刷新率屏、高刷手机均会），
        /// 设置下拉按"宽x高"文本展示——不去重=每档出现多次。设置界面下拉与 ApplyFromSave 必须
        /// 共用本列表：存档 resolutionIndex 是下拉索引，两端列表不一致=重启后套错分辨率。</summary>
        public static List<Resolution> GetUniqueResolutions()
        {
            var result = new List<Resolution>();
            var seen = new HashSet<string>();
            foreach (var r in Screen.resolutions)
            {
                if (seen.Add($"{r.width}x{r.height}")) result.Add(r);
            }
            return result;
        }
    }
}
