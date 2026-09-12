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
        public static readonly SceneType Boot = new("Boot", LoadSceneMode.Single);
        public static readonly SceneType SplashScreen = new("SplashScreen", LoadSceneMode.Single);
        public static readonly SceneType MainHall = new("MainHall", LoadSceneMode.Single);
        public static readonly SceneType MapScreen = new("MapScreen", LoadSceneMode.Additive);
        public static readonly SceneType WishScreen = new("WishScreen", LoadSceneMode.Additive);
        public static readonly SceneType CoopScreen = new("CoopScreen", LoadSceneMode.Additive);
        public static readonly SceneType BackpackScreen = new("BackpackScreen", LoadSceneMode.Additive);
        public static readonly SceneType SettingsScreen = new("SettingsScreen", LoadSceneMode.Additive);
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


