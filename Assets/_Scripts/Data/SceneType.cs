using UnityEngine.SceneManagement;

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
    public static readonly SceneType BattleScreen = new("BattleScreen", LoadSceneMode.Additive);

    // ==================== 便利方法 ====================
    
    /// <summary>
    /// 加载此场景
    /// </summary>
    public void Load()
    {
        GameScene.Instance.LoadSceneWithConfig(this);
    }

    /// <summary>
    /// 卸载此场景（仅对 Additive 模式加载的场景有效）
    /// </summary>
    public void Unload()
    {
        GameScene.Instance.UnloadSceneAsync(this);
    }

    /// <summary>
    /// 获取所有场景实例
    /// </summary>
    public static System.Collections.Generic.IEnumerable<SceneType> GetAllScenes()
    {
        yield return Boot;
        yield return SplashScreen;
        yield return MainHall;
        yield return MapScreen;
        yield return WishScreen;
        yield return CoopScreen;
        yield return BattleScreen;
    }

    public static implicit operator string(SceneType scene) => scene.SceneName;
    public override string ToString() => SceneName;
}