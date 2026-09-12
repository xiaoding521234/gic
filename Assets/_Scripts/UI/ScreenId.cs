// ScreenId.cs - Screen 身份注册表（UIManager 统一栈的寻址基础，docs/23 §3.1）
// 对齐 SceneType 模式：静态实例 + 便利工厂；P1 全场景制，P2/P3 逐屏翻 PrefabHost 时调用点零改动。
using System.Collections.Generic;
using GIC.Data;
namespace GIC.UI
{


    /// <summary>UI 层级（docs/23 §3.2）：Fullscreen=一级全屏界面；Popup=模态弹窗；Toast=轻提示；Top=转场遮罩预留</summary>
    public enum UILayer
    {
        Fullscreen = 0,
        Popup = 1,
        Toast = 2,
        Top = 3,
    }

    /// <summary>宿主类型：Prefab=面板实例化（P2+）；Scene=Additive 场景弹层；RootScene=根场景（context，不入栈）</summary>
    public enum ScreenHostKind
    {
        Prefab = 0,
        Scene = 1,
        RootScene = 2,
    }

    /// <summary>
    /// Screen 身份：名称 + 层级 + 宿主 + 资源指向（场景或 prefab 路径二选一）。
    /// 不可变；注册表见 <see cref="Screens"/>。
    /// </summary>
    public sealed class ScreenId
    {
        public string Name { get; }
        public UILayer Layer { get; }
        public ScreenHostKind Host { get; }
        /// <summary>Prefab 宿主的 Resources 路径（如 "Prefabs/UIPanels/Xxx"）；其余宿主为 null</summary>
        public string PrefabPath { get; }
        /// <summary>Scene/RootScene 宿主关联的场景类型；其余宿主为 null</summary>
        public SceneType Scene { get; }

        private ScreenId(string name, UILayer layer, ScreenHostKind host, string prefabPath, SceneType scene)
        {
            Name = name;
            Layer = layer;
            Host = host;
            PrefabPath = prefabPath;
            Scene = scene;
        }

        /// <summary>根场景（context：Splash/MainHall/Battle）——不入 UIManager 栈</summary>
        public static ScreenId Root(string name, SceneType sceneType)
            => new(name, UILayer.Fullscreen, ScreenHostKind.RootScene, null, sceneType);

        /// <summary>Additive 场景弹层（3D 内容界面：MapScreen）</summary>
        public static ScreenId Overlay(string name, SceneType sceneType)
            => new(name, UILayer.Fullscreen, ScreenHostKind.Scene, null, sceneType);

        /// <summary>prefab 面板（P2+：纯 UI 弹层）</summary>
        public static ScreenId Prefab(string name, string prefabPath)
            => new(name, UILayer.Fullscreen, ScreenHostKind.Prefab, prefabPath, null);

        public override string ToString() => Name;
    }

    /// <summary>
    /// Screen 注册表。P1 全场景制；P2 起 Settings 翻 Prefab、P3 其余逐屏翻——
    /// 翻 Host 只改本表一行 + 建 prefab + 删场景，调用点零改动（docs/23 §3.1）。
    /// </summary>
    public static class Screens
    {
        // ── 根场景（context，不入栈） ──
        public static readonly ScreenId Splash = ScreenId.Root("Splash", SceneType.SplashScreen);
        public static readonly ScreenId MainHall = ScreenId.Root("MainHall", SceneType.MainHall);
        public static readonly ScreenId Battle = ScreenId.Root("Battle", SceneType.BattleScreen);

        // ── 弹层 ──
        public static readonly ScreenId Map = ScreenId.Overlay("Map", SceneType.MapScreen);          // 保持场景制（3D 内容，docs/23 D1）
        // P2 已翻 PrefabHost（2026-09-12）：场景删除；MainHall/PetGameBridge 按 SceneType 地址经 _bySceneName 仍命中本条
        public static readonly ScreenId Settings = ScreenId.Prefab("Settings", "Prefabs/UIPanels/SettingsScreen");
        public static readonly ScreenId Backpack = ScreenId.Overlay("Backpack", SceneType.BackpackScreen); // P3→Prefab
        public static readonly ScreenId Wish = ScreenId.Overlay("Wish", SceneType.WishScreen);        // P3→Prefab
        public static readonly ScreenId Coop = ScreenId.Overlay("Coop", SceneType.CoopScreen);        // P3→Prefab

        private static readonly Dictionary<string, ScreenId> _bySceneName = new()
        {
            { SceneType.SplashScreen.SceneName, Splash },
            { SceneType.MainHall.SceneName, MainHall },
            { SceneType.BattleScreen.SceneName, Battle },
            { SceneType.MapScreen.SceneName, Map },
            { SceneType.SettingsScreen.SceneName, Settings },
            { SceneType.BackpackScreen.SceneName, Backpack },
            { SceneType.WishScreen.SceneName, Wish },
            { SceneType.CoopScreen.SceneName, Coop },
        };

        /// <summary>
        /// 按场景名查身份（MainHall/PetGameBridge 持 SceneType 地址解析用；场景制 Screen 注册寻址同走）。
        /// prefab 面板的 ScreenBase.Id 直接重写静态身份，不经此表。未知返回 null（UIManager 匿名入栈）。
        /// </summary>
        public static ScreenId FromSceneName(string sceneName)
            => sceneName != null && _bySceneName.TryGetValue(sceneName, out var id) ? id : null;
    }
}
