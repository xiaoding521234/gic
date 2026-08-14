using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using GIC.Data.Event;
using UnityEngine.Localization.Settings;
using GIC.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Framework
{


    /// <summary>
    /// 场景管理器 - 负责场景加载、卸载和内存管理
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameScene : MonoBehaviour
    {
        #region 单例与基础属性

        public static GameScene Instance { get; private set; }
        public float DeltaTime { get; private set; }

        #endregion

        #region 配置参数

        [Header("性能配置")]
        [SerializeField] private bool autoCleanupResources = true;
        [SerializeField] private float cleanupDelay = 0.5f;

        [Header("调试配置")]
        [SerializeField] private bool enableDebugLog = true;
        [SerializeField] private bool showMemoryUsage = false;

        [Header("弹窗")]
        [SerializeField] private PopupManager popupManager;

        public Texture2D cursor;

        #endregion

        #region 私有字段

        // 场景历史记录栈
        private Stack<SceneType> sceneHistory = new Stack<SceneType>();

        // 当前活动的场景类型
        private SceneType currentScene;
        private SceneType currentRootScene;

        // 资源清理协程
        private Coroutine currentLoadCoroutine;

        // 场景切换锁，防止并发操作
        private bool isTransitioning = false;

        #endregion

        #region 公共属性

        public SceneType CurrentScene
        {
            get => currentScene;
            private set => currentScene = value;
        }

        public bool IsTransitioning => isTransitioning;

        #endregion

        #region Unity 生命周期

        private void Awake()
        {
            InitializeSingleton();
            InitializeSceneEvents();
            InitializeGame();

            InitSaveSettings();

        }

        private void Start()
        {
            InitializeStartupScene();
        }

        private void Update()
        {
            DeltaTime = Time.deltaTime;
            Wargame.Instance?.Update(DeltaTime);

            if (showMemoryUsage)
            {
                LogMemoryUsage();
            }
        }

        private void OnDestroy()
        {
            CleanupEventHandlers();
            StopAllCoroutines();
        }

        #endregion

        #region 初始化方法

        private void InitSaveSettings()
        {
            Cursor.SetCursor(cursor, Vector2.zero, CursorMode.Auto);
            Application.runInBackground = true;

            // 应用存档中的显示设置
            var saveManager = Wargame.Instance?.SaveManager;
            if (saveManager?.CurrentSave == null) return;

            var save = saveManager.CurrentSave;

            // 应用语言
            var locales = LocalizationSettings.AvailableLocales.Locales;
            if (save.languageIndex >= 0 && save.languageIndex < locales.Count)
            {
                LocalizationSettings.SelectedLocale = locales[save.languageIndex];
            }

            // 应用帧率
            Application.targetFrameRate = save.frameRate;

            // 应用分辨率
            if (save.resolutionIndex == 0)
            {
                // 全屏
                Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
            }
            else
            {
                // 窗口化指定分辨率
                var resolutions = Screen.resolutions;
                int resIndex = save.resolutionIndex - 1;
                if (resIndex >= 0 && resIndex < resolutions.Length)
                {
                    var res = resolutions[resIndex];
                    Screen.SetResolution(res.width, res.height, FullScreenMode.Windowed);
                }
            }
        }

        private void InitializeSingleton()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);

#if !UNITY_EDITOR
                // Release 构建：抑制 Debug.Log（保留 LogWarning / LogError 用于诊断）
                Debug.unityLogger.filterLogType = LogType.Warning;
#endif
            }
            else if (Instance != this)
            {
                Destroy(gameObject);
            }
            Debug.Log("初始化单例");
        }

        private void InitializeSceneEvents()
        {
            SceneManager.activeSceneChanged += OnActiveSceneChangedInternal;
        }

        private void InitializeGame()
        {
            try
            {
                Debug.Log("开始初始化Wargame");
                Wargame.Instance.Init();

            }
            catch (Exception ex)
            {
                Debug.LogError($"游戏初始化失败: {ex.Message}");
            }
        }

        private void InitializeStartupScene()
        {
            currentRootScene = SceneType.Boot;
            CurrentScene = SceneType.Boot;
            Wargame.Instance?.Start();
            Debug.Log("初始化场景设置，加载 SplashScreen");

            // 从 Boot 场景加载 SplashScreen（Single 模式，Boot 场景被卸载，持久化管理器通过 DontDestroyOnLoad 存活）
            SceneType.SplashScreen.Load();
        }

        private void CleanupEventHandlers()
        {
            SceneManager.activeSceneChanged -= OnActiveSceneChangedInternal;
        }

        #endregion

        #region 事件处理

        private void OnActiveSceneChangedInternal(Scene previousScene, Scene newScene)
        {
            if (EventBusHub.Instance != null)
            {
                EventBusHub.Instance.SendImmediate(new OnSceneActivatedEvent
                {
                    SceneName = newScene.name
                });
            }

            if (enableDebugLog)
            {
                Debug.Log($"场景激活: {previousScene.name} -> {newScene.name}");
            }
        }

        #endregion

        #region 场景加载 - 公共接口

        /// <summary>
        /// 使用配置加载场景
        /// </summary>
        public void LoadSceneWithConfig(SceneType scene)
        {
            if (isTransitioning)
            {
                Debug.LogWarning("场景正在切换中，请稍后再试");
                return;
            }

            StartCoroutine(LoadSceneWithConfigCoroutine(scene));
        }

        /// <summary>
        /// 预加载场景（不激活）
        /// </summary>
        public IEnumerator PreloadScene(SceneType scene, Action<AsyncOperation> onComplete = null)
        {
            AsyncOperation asyncLoad = null;

            try
            {
                asyncLoad = SceneManager.LoadSceneAsync(scene.SceneName, scene.LoadMode);
                if (asyncLoad != null)
                {
                    asyncLoad.allowSceneActivation = false;

                    if (enableDebugLog)
                    {
                        Debug.Log($"开始预加载场景: {scene.SceneName}");
                    }
                }
                else
                {
                    Debug.LogError($"无法预加载场景: {scene.SceneName}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"预加载场景失败: {scene.SceneName}, 错误: {ex.Message}");
            }

            // 让出主线程一帧，确保UI有机会更新
            yield return null;

            onComplete?.Invoke(asyncLoad);
        }

        /// <summary>
        /// 激活预加载的场景
        /// </summary>
        public IEnumerator ActivatePreloadedScene(AsyncOperation asyncLoad, SceneType scene)
        {
            if (asyncLoad == null)
            {
                Debug.LogError("AsyncOperation 为空，无法激活场景");
                yield break;
            }

            if (!ValidateSceneActivation(scene))
            {
                yield break;
            }

            // 等待预加载完成
            while (asyncLoad.progress < 0.9f)
            {
                yield return null;
            }

            // 等待一帧，确保动画完成
            yield return null;

            // 激活场景
            asyncLoad.allowSceneActivation = true;

            // 等待激活完成
            while (!asyncLoad.isDone)
            {
                yield return null;
            }

            // 设置为活动场景
            yield return SetActiveSceneAfterLoad(scene);

            if (enableDebugLog)
            {
                Debug.Log($"场景激活完成: {scene.SceneName}");
            }
        }

        /// <summary>
        /// 返回上一个场景
        /// </summary>
        public bool GoBack()
        {
            if (!CanGoBack())
            {
                return false;
            }

            StartCoroutine(GoBackCoroutine());
            return true;
        }

        /// <summary>
        /// 加载新的根场景（Single模式）
        /// </summary>
        public void LoadNewRootScene(SceneType scene)
        {
            if (scene.LoadMode != LoadSceneMode.Single)
            {
                Debug.LogError("根场景必须使用 Single 加载模式");
                return;
            }

            StartCoroutine(LoadNewRootSceneCoroutine(scene));
        }

        /// <summary>
        /// 卸载场景
        /// </summary>
        public void UnloadSceneAsync(SceneType scene)
        {
            StartCoroutine(UnloadSceneWithEventsCoroutine(scene));
        }

        #endregion

        #region 场景加载 - 协程实现

        private IEnumerator LoadSceneWithConfigCoroutine(SceneType scene)
        {
            isTransitioning = true;

            // 记录历史
            RecordSceneHistory(scene);

            // 加载场景
            yield return LoadSceneAsync(scene);

            // 设置为活动场景
            if (scene.LoadMode == LoadSceneMode.Additive)
            {
                yield return SetActiveSceneAfterLoad(scene);
            }

            isTransitioning = false;
        }

        private IEnumerator LoadSceneAsync(SceneType scene)
        {

            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(scene.SceneName, scene.LoadMode);

            if (asyncLoad == null)
            {
                Debug.LogError($"无法加载场景: {scene.SceneName}");
                yield break;
            }

            while (!asyncLoad.isDone)
            {
                // 可以在这里更新加载进度
                yield return null;
            }

            Scene loadedScene = SceneManager.GetSceneByName(scene.SceneName);
            if (!loadedScene.isLoaded)
            {
                Debug.LogError($"场景加载失败: {scene.SceneName}");
                yield break;
            }

            CurrentScene = scene;
        }

        private IEnumerator SetActiveSceneAfterLoad(SceneType scene)
        {
            // 等待一帧确保场景完全初始化
            yield return null;

            Scene loadedScene = SceneManager.GetSceneByName(scene.SceneName);
            if (loadedScene.isLoaded)
            {
                SceneManager.SetActiveScene(loadedScene);
            }
            else
            {
                Debug.LogError($"无法设置活动场景: {scene.SceneName} 未加载");
            }
        }

        private IEnumerator GoBackCoroutine()
        {
            isTransitioning = true;

            SceneType previousScene = sceneHistory.Pop();
            SceneType sceneToUnload = CurrentScene;

            string fromSceneName = sceneToUnload?.SceneName ?? "Unknown";
            string toSceneName = previousScene.SceneName;

            // 1. 卸载当前场景
            if (ShouldUnloadScene(sceneToUnload))
            {
                yield return UnloadSceneInternal(sceneToUnload.SceneName);
            }

            // 2. 更新当前场景
            CurrentScene = previousScene;

            // 3. 激活上一个场景
            yield return ActivatePreviousScene(previousScene);

            // 4. 清理资源
            if (autoCleanupResources)
            {
                yield return CleanupUnusedResources();
            }

            // 5. 发送事件
            SendGoBackEvent(fromSceneName, toSceneName);

            isTransitioning = false;
        }

        private IEnumerator LoadNewRootSceneCoroutine(SceneType scene)
        {
            isTransitioning = true;

            ClearHistory();
            currentRootScene = scene;
            CurrentScene = scene;

            yield return LoadSceneAsync(scene);

            isTransitioning = false;
        }

        private IEnumerator UnloadSceneWithEventsCoroutine(SceneType scene)
        {
            // 发送卸载前事件
            EventBusHub.Instance?.Send(new OnSceneWillUnloadEvent
            {
                SceneName = scene.SceneName
            });

            yield return UnloadSceneInternal(scene.SceneName);
        }

        private IEnumerator UnloadSceneInternal(string sceneName)
        {
            AsyncOperation asyncUnload = SceneManager.UnloadSceneAsync(sceneName);

            if (asyncUnload == null)
            {
                Debug.LogWarning($"场景可能已卸载或不存在: {sceneName}");
                yield break;
            }

            while (!asyncUnload.isDone)
            {
                yield return null;
            }

            if (enableDebugLog)
            {
                Debug.Log($"场景卸载完成: {sceneName}");
            }
        }

        #endregion

        #region 资源清理

        private IEnumerator CleanupUnusedResources()
        {
            // 等待一帧确保所有对象都被销毁
            yield return null;

            AsyncOperation unloadOp = Resources.UnloadUnusedAssets();

            while (!unloadOp.isDone)
            {
                yield return null;
            }

            // 触发垃圾回收
            GC.Collect();

            if (enableDebugLog)
            {
                Debug.Log("资源清理完成");
            }
        }

        private IEnumerator DelayedCleanup()
        {
            yield return new WaitForSeconds(cleanupDelay);
            yield return CleanupUnusedResources();
        }

        #endregion

        #region 辅助方法

        private void RecordSceneHistory(SceneType scene)
        {
            // 只将 Additive 场景加入历史记录
            if (scene.LoadMode == LoadSceneMode.Additive && CurrentScene != null)
            {
                if (CurrentScene.LoadMode == LoadSceneMode.Single ||
                    CurrentScene.LoadMode == LoadSceneMode.Additive)
                {
                    sceneHistory.Push(CurrentScene);
                }
            }

            // 如果是 Single 模式，更新根场景并清空历史
            if (scene.LoadMode == LoadSceneMode.Single)
            {
                currentRootScene = scene;
                sceneHistory.Clear();
            }
        }

        private bool ValidateSceneActivation(SceneType scene)
        {
            RecordSceneHistory(scene);

            if (scene.LoadMode == LoadSceneMode.Single)
            {
                currentRootScene = scene;
                sceneHistory.Clear();
            }

            CurrentScene = scene;
            return true;
        }

        private bool ShouldUnloadScene(SceneType scene)
        {
            return scene != null && scene.LoadMode == LoadSceneMode.Additive;
        }

        private IEnumerator ActivatePreviousScene(SceneType scene)
        {
            Scene loadedScene = SceneManager.GetSceneByName(scene.SceneName);

            if (loadedScene.isLoaded)
            {
                SceneManager.SetActiveScene(loadedScene);
            }
            else if (scene.LoadMode == LoadSceneMode.Single)
            {
                yield return LoadSceneAsync(scene);
            }
            else
            {
                Debug.LogError($"上一个场景未加载且无法重新加载: {scene.SceneName}");
            }
        }

        private void SendGoBackEvent(string fromScene, string toScene)
        {
            EventBusHub.Instance?.Send(new OnGoBackEvent
            {
                FromScene = fromScene,
                ToScene = toScene
            });
        }

        private bool CanGoBack()
        {
            if (sceneHistory.Count == 0)
            {
                ShowToast("没有上一个场景可返回");
                return false;
            }

            if (isTransitioning)
            {
                ShowToast("场景正在切换中，请稍后再试");
                return false;
            }

            return true;
        }

        private void LogMemoryUsage()
        {
            long totalMemory = GC.GetTotalMemory(false) / 1024 / 1024;
            Debug.Log($"内存使用: {totalMemory} MB");
        }

        #endregion

        #region 弹窗

        public void ShowModalPopup(string message)
        {
            popupManager.ShowModalPopup(message);
        }

        /// <summary>
        /// 本地化弹窗 — 从 PopupText 表中取 key 对应的本地化文本显示
        /// </summary>
        public void ShowModalLocalizedPopup(string key)
        {
            var localized = new UnityEngine.Localization.LocalizedString(TableName.PopupText.ToString(), key);
            popupManager.ShowModalPopup(localized);
        }

        #endregion

        #region 轻提示

        public void ShowToast(string message)
        {
            if (popupManager != null)
                popupManager.ShowToast(message);
            else
                Debug.LogWarning(message);
        }

        public void ShowToast(UnityEngine.Localization.LocalizedString localizedString)
        {
            if (popupManager != null)
                popupManager.ShowToast(localizedString);
        }

        #endregion

        #region 公共查询方法

        public bool IsSceneLoaded(SceneType scene)
        {
            Scene loadedScene = SceneManager.GetSceneByName(scene.SceneName);
            return loadedScene.isLoaded;
        }

        public bool HasPreviousScene()
        {
            return sceneHistory.Count > 0;
        }

        public bool TryPeekPreviousScene(out SceneType previousScene)
        {
            if (sceneHistory.Count == 0)
            {
                previousScene = default;
                return false;
            }

            previousScene = sceneHistory.Peek();
            return true;
        }

        public int GetHistoryCount()
        {
            return sceneHistory.Count;
        }

        public void ClearHistory()
        {
            sceneHistory.Clear();
        }

        public void SetCurrentScene(SceneType scene)
        {
            CurrentScene = scene;
        }

        public bool PopHistory()
        {
            if (sceneHistory.Count == 0)
            {
                return false;
            }

            sceneHistory.Pop();
            return true;
        }

        #endregion
    }
}



