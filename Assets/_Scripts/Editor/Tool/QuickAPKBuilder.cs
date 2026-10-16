#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
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
            BuildWithAutoRetry(testBuild: true, finalSilent: false);
        }

        [MenuItem(MENU_BASE + "正式导出 (IL2CPP/ARM64 发布)", priority = -69)]
        public static void BuildReleaseAPK()
        {
            BuildWithAutoRetry(testBuild: false, finalSilent: false);
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

        // ==================== 异步调度入口（AI 长任务协议：毫秒级返回，判建只认日志标记） ====================

        private static bool _releaseQueued;

        /// <summary>异步调度正式构建——update 一次性调度替代 delayCall，排队守卫防叠场（gic-editor-longtask 协议）</summary>
        public static void ScheduleReleaseBuild()
        {
            if (_releaseQueued) return;
            _releaseQueued = true;
            GICLog.Info("[QuickBuild] START");
            经update调度(() =>
            {
                _releaseQueued = false;
                try
                {
                    var ok = BuildWithAutoRetry(testBuild: false, finalSilent: true);
                    GICLog.Info(ok ? "[QuickBuild] RESULT OK" : "[QuickBuild] RESULT FAIL");
                }
                catch (System.Exception ex)
                {
                    GICLog.Error($"[QuickBuild] RESULT FAIL {ex.GetType().Name}: {ex.Message}");
                }
            });
        }

        private static void 经update调度(System.Action 动作)
        {
            EditorApplication.CallbackFunction cb = null;
            cb = () => { EditorApplication.update -= cb; 动作(); };
            EditorApplication.update += cb;
        }

        // ==================== SDK 停摆自愈重试（2026-10-17，三例 CDN 间歇病固化） ====================

        private const int SDK停摆自动重试上限 = 2;   // 首次尝试之外最多再试 2 次（三例实证杀后重试一把过，留一次余量）

        /// <summary>带 SDK 停摆自愈的构建主入口：QuickBuildWatchdog 盯「Detecting Android SDK」步的
        /// sdkmanager java，CPU 冻结即击杀并自动点掉引擎「Build failure」弹窗；BuildPlayer 返回失败后此处自动重试。
        /// 中间尝试一律静默（silent=true），只有最后一次尝试按 finalSilent 决定是否弹结果框。
        /// 菜单路径同步执行（编辑器预期冻结）、调度路径异步执行——重试语义两者一致。</summary>
        private static bool BuildWithAutoRetry(bool testBuild, bool finalSilent)
        {
            QuickBuildWatchdog.Begin();
            try
            {
                var ok = BuildAPK(testBuild, finalSilent);
                var retry = 0;
                while (!ok && QuickBuildWatchdog.KilledThisAttempt && retry < SDK停摆自动重试上限)
                {
                    retry++;
                    var isLast = retry >= SDK停摆自动重试上限;
                    GICLog.Warn($"[QuickBuild] Detecting Android SDK 停摆已被看门狗击杀——自动重试 {retry}/{SDK停摆自动重试上限}");
                    QuickBuildWatchdog.EndAttempt();   // 清击杀旗标+重快照 java 基线，继续盯下一次尝试
                    ok = BuildAPK(testBuild, silent: isLast ? finalSilent : true);
                }
                if (retry > 0)
                    GICLog.Info(ok ? $"[QuickBuild] 自动重试成功（重试 {retry} 次）" : $"[QuickBuild] 自动重试 {retry} 次后仍失败——放弃");
                return ok;
            }
            finally
            {
                QuickBuildWatchdog.Stop();
            }
        }

        private static bool BuildAPK(bool testBuild, bool silent = false)
        {
            var outputPath = EditorPrefs.GetString("QuickAPKBuilder_OutputPath", DefaultOutputPath);

            // 构建前置校验（2026-10-14 LAN 第九批事故防护）：
            // ①磁盘态——BuildPlayer 的增量缓存按磁盘文件哈希键控；
            // ②编辑器打开态——构建管线会把「打开中场景的未保存内存层级态」备份（Temp/__Backupscenes）
            //   并参与场景序列化，2026-10-14 实证：未保存的层级误关被打进 01:17 的包，磁盘检查对此盲区。
            // 任一不健康即中止构建（netIdentity 恒 null → 真机客户端一切 EventBus 网络发送 NRE、卡「正在加入」）。
            var sceneError = ValidateBootSceneNetworkObjects() ?? ValidateBootSceneInMemory();
            if (sceneError != null)
            {
                GICLog.Error($"[QuickAPKBuilder] 构建中止——{sceneError}");
                if (!silent) EditorUtility.DisplayDialog("构建中止", sceneError + "\n\n请修正后保存场景 (Ctrl+S) 再重试构建。", "确定");
                return false;
            }

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
                    if (!silent) EditorUtility.DisplayDialog("错误", "切换到 Android 平台失败", "确定");
                    return false;
                }
                GICLog.Info("[QuickAPKBuilder] 已切换到 Android 平台");
            }

            // 包名守卫：切平台可能触发 Tuanjie 包名同步（详见 PackageNameGuard / docs/14 §12），构建前强制断言
            var identifier = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);
            if (identifier != PackageNameGuard.CorrectPackageName)
            {
                GICLog.Info($"[QuickAPKBuilder] Android 包名 {identifier} 异常，已修复为 {PackageNameGuard.CorrectPackageName}");
                PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, PackageNameGuard.CorrectPackageName);
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
                    if (!silent) EditorUtility.DisplayDialog("错误", "Build Settings 中没有启用的场景", "确定");
                    return false;
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

                var buildStartTime = System.DateTime.Now;
                var report = BuildPipeline.BuildPlayer(options);

                if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
                {
                    var sizeMB = new System.IO.FileInfo(outputPath).Length / 1024f / 1024f;
                    // 假成功检测（2026-10-14 实证）：增量缓存全命中时 player data 不重建、APK 不重写
                    // （移动产物保留旧 mtime）——「构建成功」但装机的还是上次产物。此处显式告知。
                    if (System.IO.File.GetLastWriteTime(outputPath) < buildStartTime)
                    {
                        GICLog.Warn("[QuickAPKBuilder] 增量缓存全命中：APK 未被重写（仍为上次构建产物）。若你改了内容却没生效，请确认改动已落盘（保存场景），或清理 Library/Bee 缓存强制全量重建。");
                        if (!silent) EditorUtility.DisplayDialog("构建成功（产物未更新）",
                            $"APK 未被重写——增量缓存判定输入零变化，装机包仍是上次构建产物。\n\n输出: {outputPath}\n大小: {sizeMB:F1}MB\n\n若刚改过内容，请保存改动后重试。", "确定");
                    }
                    else
                    {
                        GICLog.Info($"[QuickAPKBuilder] ✅ 构建成功! 大小: {sizeMB:F1}MB, 耗时: {report.summary.totalTime.TotalSeconds:F0}秒");
                        if (!silent) EditorUtility.DisplayDialog("构建成功",
                            $"APK 已导出到:\n{outputPath}\n\n大小: {sizeMB:F1}MB\n耗时: {report.summary.totalTime.TotalSeconds:F0}秒",
                            "确定");
                    }
                    return true;
                }
                else
                {
                    GICLog.Error($"[QuickAPKBuilder] ❌ 构建失败! Result: {report.summary.result}");
                    if (!silent) EditorUtility.DisplayDialog("构建失败", $"构建结果: {report.summary.result}", "确定");
                    return false;
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

        /// <summary>
        /// 构建前置校验：编辑器打开中的 Boot 场景的内存层级态。
        /// 构建管线序列化打开中场景时带上未保存的内存态（2026-10-14 实证）——内存态失活同样进包。
        /// Boot 未在编辑器中打开时跳过（构建走磁盘/导入态，磁盘校验已覆盖）。
        /// </summary>
        private static string ValidateBootSceneInMemory()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var sc = SceneManager.GetSceneAt(i);
                if (!sc.IsValid() || !sc.isLoaded || !sc.path.EndsWith("/Boot.unity", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                Transform neb = null, nm = null;
                foreach (var root in sc.GetRootGameObjects())
                {
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.parent != null && t.parent.name == "NetworkManager" && t.name == "NetworkEventBus") neb = t;
                        if (t.parent == null && t.name == "NetworkManager") nm = t;
                    }
                }

                if (neb == null)
                    return "编辑器打开的 Boot 场景中找不到 NetworkManager 下的 NetworkEventBus 物体";
                if (!neb.gameObject.activeSelf)
                    return "编辑器打开的 Boot 场景中 NetworkEventBus 物体处于失活状态（未保存的层级修改会随构建进包）";
                if (neb.GetComponent<Mirror.NetworkIdentity>() == null)
                    return "编辑器打开的 Boot 场景中 NetworkEventBus 物体缺少 NetworkIdentity 组件";
                if (neb.GetComponent<NetworkEventBus>() == null)
                    return "编辑器打开的 Boot 场景中 NetworkEventBus 物体缺少 NetworkEventBus 组件";
                if (nm == null)
                    return "编辑器打开的 Boot 场景中找不到 NetworkManager 根物体";
                if (!nm.gameObject.activeSelf)
                    return "编辑器打开的 Boot 场景中 NetworkManager 物体处于失活状态（父失活会连带子物体 Awake 不执行）";

                return null;
            }
            return null;
        }

        /// <summary>
        /// 构建前置校验：Boot.unity 关键网络物体（磁盘态）必须激活且组件齐全。
        /// 直接读磁盘文本（与 BuildPlayer 取材口径一致），不触碰编辑器当前打开的场景。
        /// </summary>
        private static string ValidateBootSceneNetworkObjects()
        {
            const string bootScenePath = "Assets/Scenes/Boot.unity";
            if (!System.IO.File.Exists(bootScenePath))
                return $"找不到 Boot 场景文件: {bootScenePath}";

            string yaml = System.IO.File.ReadAllText(bootScenePath);

            string niGuid = AssetDatabase.AssetPathToGUID("Assets/Mirror/Core/NetworkIdentity.cs");
            string nebGuid = AssetDatabase.AssetPathToGUID("Assets/_Scripts/Framework/EventBus/NetworkEventBus.cs");
            if (string.IsNullOrEmpty(niGuid) || string.IsNullOrEmpty(nebGuid))
                return "NetworkIdentity / NetworkEventBus 脚本 GUID 解析失败（脚本文件缺失?）";

            string nebBlock = FindGameObjectBlock(yaml, "NetworkEventBus");
            if (nebBlock == null)
                return "Boot.unity 中找不到 NetworkEventBus 物体";
            if (GetActiveSelf(nebBlock) != 1)
                return "Boot.unity 的 NetworkEventBus 物体处于失活状态（activeSelf=0）";
            if (!BlockHasComponentWithGuid(yaml, nebBlock, niGuid))
                return "Boot.unity 的 NetworkEventBus 物体上缺少 NetworkIdentity 组件";
            if (!BlockHasComponentWithGuid(yaml, nebBlock, nebGuid))
                return "Boot.unity 的 NetworkEventBus 物体上缺少 NetworkEventBus 组件";

            string nmBlock = FindGameObjectBlock(yaml, "NetworkManager");
            if (nmBlock == null)
                return "Boot.unity 中找不到 NetworkManager 物体";
            if (GetActiveSelf(nmBlock) != 1)
                return "Boot.unity 的 NetworkManager 物体处于失活状态（父失活会连带子物体 Awake 不执行）";

            return null;
        }

        /// <summary>按名字在场景 YAML 中找 GameObject 块（--- !u!1 &id 至下一个 --- 文档），名字须整行精确匹配</summary>
        private static string FindGameObjectBlock(string yaml, string goName)
        {
            int search = 0;
            while (true)
            {
                int goStart = yaml.IndexOf("--- !u!1 &", search, System.StringComparison.Ordinal);
                if (goStart < 0) return null;
                int nextDoc = yaml.IndexOf("\n--- !u!", goStart + 1, System.StringComparison.Ordinal);
                string block = nextDoc < 0 ? yaml.Substring(goStart) : yaml.Substring(goStart, nextDoc - goStart);

                int nameIdx = block.IndexOf("m_Name: " + goName, System.StringComparison.Ordinal);
                if (nameIdx >= 0)
                {
                    int after = nameIdx + "m_Name: ".Length + goName.Length;
                    if (after >= block.Length || block[after] == '\n' || block[after] == '\r')
                        return block;
                }
                search = goStart + 1;
            }
        }

        /// <summary>GO 块的 m_IsActive 值（0/1），字段缺失或异常返回 -1（按失败处理）</summary>
        private static int GetActiveSelf(string goBlock)
        {
            int i = goBlock.IndexOf("m_IsActive: ", System.StringComparison.Ordinal);
            if (i < 0) return -1;
            char c = goBlock[i + "m_IsActive: ".Length];
            if (c != '0' && c != '1') return -1;
            return c - '0';
        }

        /// <summary>GO 块组件列表里是否存在挂指定脚本 GUID 的 MonoBehaviour（Transform 等 !u!4 组件自然跳过）</summary>
        private static bool BlockHasComponentWithGuid(string yaml, string goBlock, string scriptGuid)
        {
            int comp = goBlock.IndexOf("m_Component:", System.StringComparison.Ordinal);
            if (comp < 0) return false;
            int compEnd = goBlock.IndexOf("m_Layer:", comp, System.StringComparison.Ordinal);
            if (compEnd < 0) return false;
            string compSection = goBlock.Substring(comp, compEnd - comp);

            int pos = 0;
            while (true)
            {
                int fid = compSection.IndexOf("fileID: ", pos, System.StringComparison.Ordinal);
                if (fid < 0) return false;
                int brace = compSection.IndexOf("}", fid, System.StringComparison.Ordinal);
                if (brace < 0) return false;
                string fileId = compSection.Substring(fid + "fileID: ".Length, brace - fid - "fileID: ".Length).Trim();
                pos = brace + 1;

                int doc = yaml.IndexOf("--- !u!114 &" + fileId, System.StringComparison.Ordinal);
                if (doc < 0) continue;
                int after = doc + "--- !u!114 &".Length + fileId.Length;
                if (after < yaml.Length && yaml[after] != '\n' && yaml[after] != '\r') continue;

                int docNext = yaml.IndexOf("\n--- !u!", doc + 1, System.StringComparison.Ordinal);
                string docBlock = docNext < 0 ? yaml.Substring(doc) : yaml.Substring(doc, docNext - doc);
                if (docBlock.Contains("guid: " + scriptGuid + ","))
                    return true;
            }
        }
    }
}
#endif
