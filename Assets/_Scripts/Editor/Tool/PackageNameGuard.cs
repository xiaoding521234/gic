#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace GIC.Editor
{
    /// <summary>
    /// Android 包名守卫 — 根治"编辑器切平台后 ProjectSettings 包名被重置为模板默认值"问题。
    ///
    /// 根因（2026-08-21 定位，docs/14 §12）：Tuanjie 包名存储两处——
    /// expectedBundleIdentifier（= PlayerSettings.bundleIdentifier，Inspector Package Name 写这里，
    /// 也是 applicationIdentifier 映射表缺条目时的回退值）+ applicationIdentifier 按平台映射表。
    /// 切平台时引擎用映射表条目同步 expectedBundleIdentifier；本项目映射表自模板时代只存着
    /// com.DefaultCompany.2DProject（Standalone 条目，Android 条目缺失），每次切回 Standalone
    /// 就把正确包名污染成模板默认值。
    ///
    /// 修复分层：①映射表两平台条目已写对（消灭污染源）②本守卫启动时校验自愈（覆盖未知写入路径）
    /// ③QuickAPKBuilder 构建前再断言（最终防线）。
    /// 若将来要改包名：改本文件常量 + Tools/包名校验/立即校验并修复。
    /// </summary>
    [InitializeOnLoad]
    public static class PackageNameGuard
    {
        private const string KEY_LAST_FIXED = "PackageNameGuard_LastFixedValue";

        public const string CorrectPackageName = "com.HGAME.gic";

        static PackageNameGuard()
        {
            // 延迟到编辑器空闲首帧，避开平台切换/域重载期间的序列化竞争
            EditorApplication.delayCall += EnsureCorrectPackageName;
        }

        [MenuItem("Tools/包名校验/立即校验并修复")]
        public static void EnsureCorrectPackageNameMenu()
        {
            EnsureCorrectPackageName();
            EditorUtility.DisplayDialog("包名校验",
                $"当前包名: {ReadBundleIdentifier()}\napplicationIdentifier 映射表已确保为 {CorrectPackageName}",
                "确定");
        }

        private static void EnsureCorrectPackageName()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            bool dirty = false;

            // 1) expectedBundleIdentifier / PlayerSettings.bundleIdentifier（Tuanjie 特有，Inspector 写这里）
            var bundleId = ReadBundleIdentifier();
            if (bundleId != CorrectPackageName)
            {
                // 同值去重：避免修复未持久化时每轮域重载刷警告
                if (SessionState.GetString(KEY_LAST_FIXED, "") != bundleId)
                {
                    Debug.LogWarning($"[PackageNameGuard] 包名被重置为 {bundleId ?? "(null)"}，自动修复为 {CorrectPackageName}");
                    SessionState.SetString(KEY_LAST_FIXED, bundleId ?? "(null)");
                }
                WriteBundleIdentifier(CorrectPackageName);
                dirty = true;
            }
            else
            {
                SessionState.EraseString(KEY_LAST_FIXED);
            }

            // 2) applicationIdentifier 映射表（切平台同步的污染源）
            if (PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android) != CorrectPackageName)
            {
                Debug.LogWarning($"[PackageNameGuard] applicationIdentifier[Android] 漂移，自动修复为 {CorrectPackageName}");
                PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, CorrectPackageName);
                dirty = true;
            }
            if (PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Standalone) != CorrectPackageName)
            {
                Debug.LogWarning($"[PackageNameGuard] applicationIdentifier[Standalone] 漂移，自动修复为 {CorrectPackageName}");
                PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Standalone, CorrectPackageName);
                dirty = true;
            }

            if (dirty) AssetDatabase.SaveAssets();
        }

        private static string ReadBundleIdentifier()
        {
            var prop = typeof(PlayerSettings).GetProperty("bundleIdentifier",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            return prop?.GetValue(null) as string;
        }

        private static void WriteBundleIdentifier(string value)
        {
            var prop = typeof(PlayerSettings).GetProperty("bundleIdentifier",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            prop?.SetValue(null, value, null);
        }
    }
}
#endif
