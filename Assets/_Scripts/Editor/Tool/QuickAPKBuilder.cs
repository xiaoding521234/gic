#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using GIC.Framework;

namespace GIC.Editor
{
    /// <summary>
    /// 快速导出 APK — 自动切换到 Android 平台构建，确保 Addressables 打包正确纹理格式
    /// 用法: Tools/导出 APK/快速导出 (测试)
    /// </summary>
    public static class QuickAPKBuilder
    {
        private const string MENU_BASE = "Tools/导出 APK/";

        private const string DefaultOutputPath = "D:/Tuanjie_test/gic.apk";

        [MenuItem(MENU_BASE + "快速导出 (Mono/ARMv7 测试)", priority = -70)]
        public static void BuildTestAPK()
        {
            BuildAPK(testBuild: true);
        }

        [MenuItem(MENU_BASE + "正式导出 (IL2CPP/ARM64 发布)", priority = -69)]
        public static void BuildReleaseAPK()
        {
            BuildAPK(testBuild: false);
        }

        [MenuItem(MENU_BASE + "设置导出路径", priority = -68)]
        public static void SetOutputPath()
        {
            var current = EditorPrefs.GetString("QuickAPKBuilder_OutputPath", DefaultOutputPath);
            var path = EditorUtility.SaveFilePanel("选择 APK 导出路径", System.IO.Path.GetDirectoryName(current), System.IO.Path.GetFileName(current), "apk");
            if (!string.IsNullOrEmpty(path))
            {
                EditorPrefs.SetString("QuickAPKBuilder_OutputPath", path);
                GICLog.Info($"[QuickAPKBuilder] 导出路径已设为: {path}");
            }
        }

        private static void BuildAPK(bool testBuild)
        {
            var outputPath = EditorPrefs.GetString("QuickAPKBuilder_OutputPath", DefaultOutputPath);

            // 保存当前活动平台
            var originalTarget = EditorUserBuildSettings.activeBuildTarget;
            var originalTargetGroup = BuildPipeline.GetBuildTargetGroup(originalTarget);

            // 如果不在 Android 平台，需要切换（Addressables bundle 按活动平台打包，不切换会导致纹理格式错误）
            if (originalTarget != BuildTarget.Android)
            {
                GICLog.Info($"[QuickAPKBuilder] 当前平台: {originalTarget}，切换到 Android...");
                var switchOk = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
                if (!switchOk)
                {
                    EditorUtility.DisplayDialog("错误", "切换到 Android 平台失败", "确定");
                    return;
                }
                GICLog.Info("[QuickAPKBuilder] 已切换到 Android 平台");
            }

            // 保存当前 scripting backend 和 architecture
            var originalBackend = PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android);
            var originalArch = PlayerSettings.Android.targetArchitectures;
            var originalStripping = PlayerSettings.GetManagedStrippingLevel(BuildTargetGroup.Android);

            // 设置构建配置
            if (testBuild)
            {
                PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.Mono2x);
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7;
                PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Android, ManagedStrippingLevel.Disabled);
            }
            else
            {
                PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Android, ManagedStrippingLevel.Low);
            }

            try
            {
                // 收集场景
                var scenes = new System.Collections.Generic.List<string>();
                foreach (var scene in EditorBuildSettings.scenes)
                {
                    if (scene.enabled)
                        scenes.Add(scene.path);
                }

                if (scenes.Count == 0)
                {
                    EditorUtility.DisplayDialog("错误", "Build Settings 中没有启用的场景", "确定");
                    return;
                }

                // 构建
                var options = new BuildPlayerOptions
                {
                    scenes = scenes.ToArray(),
                    locationPathName = outputPath,
                    target = BuildTarget.Android,
                    options = BuildOptions.None
                };

                GICLog.Info($"[QuickAPKBuilder] 开始构建 ({(testBuild ? "测试" : "发布")}) → {outputPath}");
                GICLog.Info($"  Backend: {PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android)}");
                GICLog.Info($"  Architecture: {PlayerSettings.Android.targetArchitectures}");
                GICLog.Info($"  Scenes: {scenes.Count}");
                GICLog.Info($"  Output: {outputPath}");

                var report = BuildPipeline.BuildPlayer(options);

                if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
                {
                    var sizeMB = new System.IO.FileInfo(outputPath).Length / 1024f / 1024f;
                    GICLog.Info($"[QuickAPKBuilder] ✅ 构建成功! 大小: {sizeMB:F1}MB, 耗时: {report.summary.totalTime.TotalSeconds:F0}秒");
                    EditorUtility.DisplayDialog("构建成功",
                        $"APK 已导出到:\n{outputPath}\n\n大小: {sizeMB:F1}MB\n耗时: {report.summary.totalTime.TotalSeconds:F0}秒",
                        "确定");
                }
                else
                {
                    GICLog.Error($"[QuickAPKBuilder] ❌ 构建失败! Result: {report.summary.result}");
                    EditorUtility.DisplayDialog("构建失败", $"构建结果: {report.summary.result}", "确定");
                }
            }
            finally
            {
                // 恢复原始构建设置
                PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, originalBackend);
                PlayerSettings.Android.targetArchitectures = originalArch;
                PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Android, originalStripping);
                GICLog.Info($"[QuickAPKBuilder] 已恢复原始构建设置: Backend={originalBackend}, Arch={originalArch}, Stripping={originalStripping}");

                // 不切回原平台 — 避免触发第二次纹理重导入
                if (originalTarget != BuildTarget.Android)
                    GICLog.Info($"[QuickAPKBuilder] 编辑器当前已留在 Android 平台（未切回 {originalTarget}，避免重复重导入纹理）");
            }
        }
    }
}
#endif
