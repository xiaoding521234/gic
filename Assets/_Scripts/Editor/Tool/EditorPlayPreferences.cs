#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ParrelSync;
using GIC.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Editor
{


    [InitializeOnLoad]
    public class EditorPlayPreferences
    {
        private const string SCENE_PATH = "Assets/Scenes/Boot.unity";

        // ==================== 开关状态（EditorPrefs 持久化） ====================

        private const string KEY_FULLSCREEN = "EditorPlayPreferences_Fullscreen";
        private const string KEY_START_SCENE = "EditorPlayPreferences_StartScene";
        private const string KEY_SYNC_CLONE = "EditorPlayPreferences_SyncClonePlay";

        // 文件信号路径（用于跨进程通信，让 Clone 自动进入/退出 Play）
        private const string PLAY_SIGNAL_DIR = "Temp";
        private const string PLAY_SIGNAL_FILE = "Temp/_clone_play_signal";
        private const string STOP_SIGNAL_FILE = "Temp/_clone_stop_signal";
        private const string REFRESH_SIGNAL_FILE = "Temp/_clone_refresh_signal";
        private const double SIGNAL_TIMEOUT = 5.0;

        /// <summary>是否自动全屏 Game 视图</summary>
        private static bool AutoFullscreen
        {
            get => EditorPrefs.GetBool(KEY_FULLSCREEN, true);
            set => EditorPrefs.SetBool(KEY_FULLSCREEN, value);
        }

        /// <summary>是否从 Boot 场景启动</summary>
        private static bool StartFromBoot
        {
            get => EditorPrefs.GetBool(KEY_START_SCENE, true);
            set => EditorPrefs.SetBool(KEY_START_SCENE, value);
        }

        /// <summary>是否同步启动 Clone 并进入 Play 模式</summary>
        private static bool SyncClonePlay
        {
            get => EditorPrefs.GetBool(KEY_SYNC_CLONE, false);
            set => EditorPrefs.SetBool(KEY_SYNC_CLONE, value);
        }

        // ==================== 初始化 ====================

        static EditorPlayPreferences()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += OnEditorUpdate;
            // 主 Editor 编译完成后通知 Clone 刷新
            UnityEditor.Compilation.CompilationPipeline.compilationFinished += OnCompilationFinished;
        }

        private static void OnCompilationFinished(object obj)
        {
            // 只在主项目（非 Clone）中发送刷新信号
            if (ClonesManager.IsClone()) return;
            if (!SyncClonePlay) return;

            var clonePaths = ClonesManager.GetCloneProjectsPath();
            foreach (var clonePath in clonePaths)
            {
                string signalPath = System.IO.Path.Combine(clonePath, REFRESH_SIGNAL_FILE);
                try
                {
                    string tempDir = System.IO.Path.Combine(clonePath, PLAY_SIGNAL_DIR);
                    if (!System.IO.Directory.Exists(tempDir))
                        System.IO.Directory.CreateDirectory(tempDir);
                    System.IO.File.WriteAllText(signalPath, System.DateTime.UtcNow.ToBinary().ToString());
                }
                catch { /* ignore */ }
            }
        }

        private static double _lastSignalTime;

        private static void OnEditorUpdate()
        {
            // 只在 Clone 项目中运行信号轮询
            if (!ClonesManager.IsClone()) return;
            if (EditorApplication.isCompiling) return;
            if (EditorApplication.isUpdating) return;

            // 检查停止信号（仅在 Play 中才检测，忽略非 Play 状态的残留信号）
            if (EditorApplication.isPlaying && System.IO.File.Exists(STOP_SIGNAL_FILE))
            {
                try
                {
                    System.IO.File.Delete(STOP_SIGNAL_FILE);
                    GICLog.Info("[EditorPlayPreferences] Clone 收到 Stop 信号，退出 Play 模式");
                    EditorApplication.ExitPlaymode();
                    return;
                }
                catch { /* ignore */ }
            }

            // 清理非 Play 状态下的残留 Stop 信号
            if (!EditorApplication.isPlaying && System.IO.File.Exists(STOP_SIGNAL_FILE))
            {
                try { System.IO.File.Delete(STOP_SIGNAL_FILE); } catch { }
            }

            // 检查 Refresh 信号（主 Editor 编译完成后触发）
            if (!EditorApplication.isPlaying && System.IO.File.Exists(REFRESH_SIGNAL_FILE))
            {
                try { System.IO.File.Delete(REFRESH_SIGNAL_FILE); } catch { }
                GICLog.Info("[EditorPlayPreferences] Clone 收到 Refresh 信号，刷新资源");
                UnityEditor.AssetDatabase.Refresh();
            }

            // 检查 Play 信号（不在 Play 中才检测）
            if (EditorApplication.isPlaying) return;

            if (System.IO.File.Exists(PLAY_SIGNAL_FILE))
            {
                try
                {
                    string content = System.IO.File.ReadAllText(PLAY_SIGNAL_FILE);
                    // 信号是 UTC 时间戳（ToBinary），检查是否在 5 秒内
                    if (long.TryParse(content, out long binaryTime))
                    {
                        var signalTime = System.DateTime.FromBinary(binaryTime);
                        if ((System.DateTime.UtcNow - signalTime).TotalSeconds < SIGNAL_TIMEOUT)
                        {
                            GICLog.Info("[EditorPlayPreferences] Clone 收到 Play 信号，进入 Play 模式");
                            System.IO.File.Delete(PLAY_SIGNAL_FILE);
                            EditorSceneManager.OpenScene(SCENE_PATH);
                            EditorApplication.EnterPlaymode();
                            return;
                        }
                    }
                    // 信号过期，删除
                    System.IO.File.Delete(PLAY_SIGNAL_FILE);
                }
                catch { /* ignore */ }
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                if (StartFromBoot)
                {
                    string currentScene = EditorSceneManager.GetActiveScene().path;
                    if (currentScene != SCENE_PATH)
                    {
                        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                            EditorSceneManager.OpenScene(SCENE_PATH);
                        else
                            EditorApplication.isPlaying = false;
                    }
                }
            }
            else if (state == PlayModeStateChange.EnteredPlayMode)
            {
                if (AutoFullscreen)
                {
                    EditorApplication.delayCall += () => SetGameViewFullscreen(true);
                }

                // 同步启动 Clone 并进入 Play 模式
                if (SyncClonePlay && !ClonesManager.IsClone())
                {
                    StartClonePlayMode();
                }
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                if (AutoFullscreen)
                {
                    EditorApplication.delayCall += () => SetGameViewFullscreen(false);
                }

                // 主 Editor 退出 Play → 发送 Stop 信号给 Clone
                if (SyncClonePlay && !ClonesManager.IsClone())
                {
                    StopClonePlayMode();
                }
            }
        }

        private static void SetGameViewFullscreen(bool fullscreen)
        {
            var gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            var gameView = EditorWindow.GetWindow(gameViewType);
            if (gameView != null)
            {
                gameView.maximized = fullscreen;
            }
        }

        // ==================== 菜单：勾选式开关 ====================

        [MenuItem("Tools/编辑器启动/自动全屏", priority = -90)]
        private static void ToggleFullscreen()
        {
            AutoFullscreen = !AutoFullscreen;
            GICLog.Info($"[EditorPlayPreferences] 自动全屏: {(AutoFullscreen ? "开" : "关")}");
        }

        [MenuItem("Tools/编辑器启动/自动全屏", true)]
        private static bool ToggleFullscreenValidate()
        {
            Menu.SetChecked("Tools/编辑器启动/自动全屏", AutoFullscreen);
            return true;
        }

        [MenuItem("Tools/编辑器启动/从 Boot 场景启动", priority = -89)]
        private static void ToggleStartScene()
        {
            StartFromBoot = !StartFromBoot;
            GICLog.Info($"[EditorPlayPreferences] 从 Boot 启动: {(StartFromBoot ? "开" : "关")}");
        }

        [MenuItem("Tools/编辑器启动/从 Boot 场景启动", true)]
        private static bool ToggleStartSceneValidate()
        {
            Menu.SetChecked("Tools/编辑器启动/从 Boot 场景启动", StartFromBoot);
            return true;
        }

        [MenuItem("Tools/编辑器启动/同步 Clone Play", priority = -88)]
        private static void ToggleSyncClone()
        {
            SyncClonePlay = !SyncClonePlay;
            GICLog.Info($"[EditorPlayPreferences] 同步 Clone Play: {(SyncClonePlay ? "开" : "关")}");
        }

        [MenuItem("Tools/编辑器启动/同步 Clone Play", true)]
        private static bool ToggleSyncCloneValidate()
        {
            Menu.SetChecked("Tools/编辑器启动/同步 Clone Play", SyncClonePlay);
            return true;
        }

        // ==================== Clone Play 模式启动 ====================

        private static void StartClonePlayMode()
        {
            var clonePaths = ClonesManager.GetCloneProjectsPath();
            if (clonePaths.Count == 0)
            {
                GICLog.Warn("[EditorPlayPreferences] 未找到 Clone 项目，请先通过 ParrelSync 创建 Clone");
                return;
            }

            foreach (var clonePath in clonePaths)
            {
                // 直接写信号文件，不检查是否在运行
                // Clone 如果开着会自动检测并进入 Play；没开则忽略
                string signalPath = System.IO.Path.Combine(clonePath, PLAY_SIGNAL_FILE);
                try
                {
                    string tempDir = System.IO.Path.Combine(clonePath, PLAY_SIGNAL_DIR);
                    if (!System.IO.Directory.Exists(tempDir))
                        System.IO.Directory.CreateDirectory(tempDir);
                    System.IO.File.WriteAllText(signalPath, System.DateTime.UtcNow.ToBinary().ToString());
                    GICLog.Info($"[EditorPlayPreferences] 已发送 Play 信号给 Clone: {clonePath}");
                }
                catch (System.Exception e)
                {
                    GICLog.Error($"[EditorPlayPreferences] 写信号文件失败: {e.Message}");
                }
            }
        }

        private static void StopClonePlayMode()
        {
            var clonePaths = ClonesManager.GetCloneProjectsPath();
            foreach (var clonePath in clonePaths)
            {
                string signalPath = System.IO.Path.Combine(clonePath, STOP_SIGNAL_FILE);
                try
                {
                    string tempDir = System.IO.Path.Combine(clonePath, PLAY_SIGNAL_DIR);
                    if (!System.IO.Directory.Exists(tempDir))
                        System.IO.Directory.CreateDirectory(tempDir);
                    System.IO.File.WriteAllText(signalPath, System.DateTime.UtcNow.ToBinary().ToString());
                    GICLog.Info($"[EditorPlayPreferences] 已发送 Stop 信号给 Clone: {clonePath}");
                }
                catch (System.Exception e)
                {
                    GICLog.Error($"[EditorPlayPreferences] 写 Stop 信号失败: {e.Message}");
                }
            }
        }
    }
    #endif

}


