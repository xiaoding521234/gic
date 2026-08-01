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


    public static class QuickSceneOpener
    {
        private const string MENU_BASE = "Tools/快速打开场景/";

        [MenuItem(MENU_BASE + "Boot", priority = 0)]
        private static void OpenBoot() => OpenScene("Assets/Scenes/Boot.unity");

        [MenuItem(MENU_BASE + "Boot", true)]
        private static bool OpenBootValidate()
        {
            Menu.SetChecked(MENU_BASE + "Boot", EditorSceneManager.GetActiveScene().path == "Assets/Scenes/Boot.unity");
            return true;
        }

        [MenuItem(MENU_BASE + "SplashScreen", priority = 1)]
        private static void OpenSplash() => OpenScene("Assets/Scenes/SplashScreen.unity");

        [MenuItem(MENU_BASE + "SplashScreen", true)]
        private static bool OpenSplashValidate()
        {
            Menu.SetChecked(MENU_BASE + "SplashScreen", EditorSceneManager.GetActiveScene().path == "Assets/Scenes/SplashScreen.unity");
            return true;
        }

        [MenuItem(MENU_BASE + "MainHall", priority = 2)]
        private static void OpenMainHall() => OpenScene("Assets/Scenes/MainHall.unity");

        [MenuItem(MENU_BASE + "MainHall", true)]
        private static bool OpenMainHallValidate()
        {
            Menu.SetChecked(MENU_BASE + "MainHall", EditorSceneManager.GetActiveScene().path == "Assets/Scenes/MainHall.unity");
            return true;
        }

        [MenuItem(MENU_BASE + "CoopScreen", priority = 3)]
        private static void OpenCoop() => OpenScene("Assets/Scenes/CoopScreen.unity");

        [MenuItem(MENU_BASE + "CoopScreen", true)]
        private static bool OpenCoopValidate()
        {
            Menu.SetChecked(MENU_BASE + "CoopScreen", EditorSceneManager.GetActiveScene().path == "Assets/Scenes/CoopScreen.unity");
            return true;
        }

        [MenuItem(MENU_BASE + "BackpackScreen", priority = 4)]
        private static void OpenBackpack() => OpenScene("Assets/Scenes/BackpackScreen.unity");

        [MenuItem(MENU_BASE + "BackpackScreen", true)]
        private static bool OpenBackpackValidate()
        {
            Menu.SetChecked(MENU_BASE + "BackpackScreen", EditorSceneManager.GetActiveScene().path == "Assets/Scenes/BackpackScreen.unity");
            return true;
        }

        [MenuItem(MENU_BASE + "MapScreen", priority = 5)]
        private static void OpenMap() => OpenScene("Assets/Scenes/MapScreen.unity");

        [MenuItem(MENU_BASE + "MapScreen", true)]
        private static bool OpenMapValidate()
        {
            Menu.SetChecked(MENU_BASE + "MapScreen", EditorSceneManager.GetActiveScene().path == "Assets/Scenes/MapScreen.unity");
            return true;
        }

        [MenuItem(MENU_BASE + "WishScreen", priority = 6)]
        private static void OpenWish() => OpenScene("Assets/Scenes/WishScreen.unity");

        [MenuItem(MENU_BASE + "WishScreen", true)]
        private static bool OpenWishValidate()
        {
            Menu.SetChecked(MENU_BASE + "WishScreen", EditorSceneManager.GetActiveScene().path == "Assets/Scenes/WishScreen.unity");
            return true;
        }

        [MenuItem(MENU_BASE + "SettingsScreen", priority = 7)]
        private static void OpenSettings() => OpenScene("Assets/Scenes/SettingsScreen.unity");

        [MenuItem(MENU_BASE + "SettingsScreen", true)]
        private static bool OpenSettingsValidate()
        {
            Menu.SetChecked(MENU_BASE + "SettingsScreen", EditorSceneManager.GetActiveScene().path == "Assets/Scenes/SettingsScreen.unity");
            return true;
        }

        private static void OpenScene(string path)
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(path);
        }
    }
    #endif

}


