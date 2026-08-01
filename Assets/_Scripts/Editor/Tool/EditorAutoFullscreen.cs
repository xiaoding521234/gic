#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using GIC.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Editor
{


    [InitializeOnLoad]
    public class EditorAutoFullscreen
    {
        private const string SCENE_PATH = "Assets/Scenes/Boot.unity";

        // ==================== 开关状态（EditorPrefs 持久化） ====================

        private const string KEY_FULLSCREEN = "EditorAutoFullscreen_Fullscreen";
        private const string KEY_START_SCENE = "EditorAutoFullscreen_StartScene";

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

        // ==================== 初始化 ====================

        static EditorAutoFullscreen()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
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
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                if (AutoFullscreen)
                {
                    EditorApplication.delayCall += () => SetGameViewFullscreen(false);
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

        // ==================== 菜单：自动全屏 ====================

        [MenuItem("Tools/编辑器启动/自动全屏")]
        private static void EnableFullscreen()
        {
            AutoFullscreen = true;
            Debug.Log("[EditorAutoFullscreen] 自动全屏: 开");
        }

        [MenuItem("Tools/编辑器启动/自动全屏", true)]
        private static bool EnableFullscreenValidate()
        {
            Menu.SetChecked("Tools/编辑器启动/自动全屏", AutoFullscreen);
            return !AutoFullscreen;
        }

        [MenuItem("Tools/编辑器启动/不全屏")]
        private static void DisableFullscreen()
        {
            AutoFullscreen = false;
            Debug.Log("[EditorAutoFullscreen] 自动全屏: 关");
        }

        [MenuItem("Tools/编辑器启动/不全屏", true)]
        private static bool DisableFullscreenValidate()
        {
            Menu.SetChecked("Tools/编辑器启动/不全屏", !AutoFullscreen);
            return AutoFullscreen;
        }

        // ==================== 菜单：从 Boot 启动 ====================

        [MenuItem("Tools/编辑器启动/从 Boot 场景启动")]
        private static void EnableStartScene()
        {
            StartFromBoot = true;
            Debug.Log("[EditorAutoFullscreen] 从 Boot 启动: 开");
        }

        [MenuItem("Tools/编辑器启动/从 Boot 场景启动", true)]
        private static bool EnableStartSceneValidate()
        {
            Menu.SetChecked("Tools/编辑器启动/从 Boot 场景启动", StartFromBoot);
            return !StartFromBoot;
        }

        [MenuItem("Tools/编辑器启动/从当前场景启动")]
        private static void DisableStartScene()
        {
            StartFromBoot = false;
            Debug.Log("[EditorAutoFullscreen] 从 Boot 启动: 关");
        }

        [MenuItem("Tools/编辑器启动/从当前场景启动", true)]
        private static bool DisableStartSceneValidate()
        {
            Menu.SetChecked("Tools/编辑器启动/从当前场景启动", !StartFromBoot);
            return StartFromBoot;
        }
    }
    #endif

}


