using UnityEngine.SceneManagement;
using GIC.Framework;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Data
{


    /// <summary>
    /// 场景类型 - 替代传统枚举，支持携带多个配置参数
    /// </summary>
    public sealed class SceneType
    {
        public string SceneName { get; }
        public LoadSceneMode LoadMode { get; }
        
        private SceneType(string sceneName, LoadSceneMode loadMode)
        {
            SceneName = sceneName;
            LoadMode = loadMode;
        }

        // ==================== 场景实例定义 ====================
        // P4 瘦身定案（2026-09-13，UI 重构收官）：本类=根场景加载器 + 弹层**地址 token** 双职责——
        // .Load() 仅对根场景合法（Boot/Splash/MainHall/Battle + 场景制弹层 Map）；
        // Wish/Coop/Backpack/Settings 四条目已无 .unity 文件，仅作 MainHall 按钮/宠物聊天的
        // 寻址 token 存续（Screens.FromSceneName 解析到 PrefabHost 面板，docs/23 §3.1/D12）。
        public static readonly SceneType Boot = new("Boot", LoadSceneMode.Single);
        public static readonly SceneType SplashScreen = new("SplashScreen", LoadSceneMode.Single);
        public static readonly SceneType MainHall = new("MainHall", LoadSceneMode.Single);
        public static readonly SceneType MapScreen = new("MapScreen", LoadSceneMode.Additive);
        public static readonly SceneType WishScreen = new("WishScreen", LoadSceneMode.Additive);          // 地址 token（场景已删）
        public static readonly SceneType CoopScreen = new("CoopScreen", LoadSceneMode.Additive);          // 地址 token（场景已删）
        public static readonly SceneType BackpackScreen = new("BackpackScreen", LoadSceneMode.Additive);  // 地址 token（场景已删）
        public static readonly SceneType SettingsScreen = new("SettingsScreen", LoadSceneMode.Additive);  // 地址 token（场景已删）
        public static readonly SceneType BattleScreen = new("BattleScreen", LoadSceneMode.Single);

        // ==================== 便利方法 ====================
        
        /// <summary>
        /// 加载此场景
        /// </summary>
        public void Load()
        {
            GameScene.Instance.LoadSceneWithConfig(this);
        }

        public static implicit operator string(SceneType scene) => scene.SceneName;
        public override string ToString() => SceneName;
    }
}


