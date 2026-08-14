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

        // 场景切换锁已由 InputManager.InputLock 替代

        #endregion

        #region 公共属性

        public SceneType CurrentScene
        {
            get => currentScene;
            private set => currentScene = value;
        }

        public bool IsTransitioning
            => InputLocks.HasLock(InputLockReason.SceneTransition);

        #endregion

        #region Unity 生命周期

        private void Awake()
        {
            InitializeSingleton();
            InitializeSceneEvents();
            InitializeGame();   // Wargame.Init 完成，容器就绪
            Context?.Inject(this); // 注入 GameScene 自身的 [Autowired] 字段

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
            // 兜底：释放本类持有的全部输入锁（正常流程早已配对 pop）
            InputLocks.PopAll(this);
        }

        #endregion

        #region 初始化方法

        // 注入字段：GameScene.Awake 自身完成 Wargame.Init 后注入（InitSaveSettings 调用点在其后）
        [Autowired] private SaveManager _saveManager;

        private ApplicationContext Context => Wargame.Instance?.Context;

        private void InitSaveSettings()
        {
            Cursor.SetCursor(cursor, Vector2.zero, CursorMode.Auto);
            Application.runInBackground = true;

            // 应用存档中的显示设置
            var saveManager = _saveManager;
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
                // Release 构建：日志策略统一由 GICLog 接管（压制 Info，保留 Warning/Error）
                GICLog.ConfigureForRelease();
#endif
            }
            else if (Instance != this)
            {
                Destroy(gameObject);
            }
            GICLog.Info("初始化单例");
        }

        private void InitializeSceneEvents()
        {
            SceneManager.activeSceneChanged += OnActiveSceneChangedInternal;
        }

        private void InitializeGame()
        {
            try
            {
                GICLog.Info("开始初始化Wargame");
                Wargame.Instance.Init();

            }
            catch (Exception ex)
            {
                GICLog.Error($"游戏初始化失败: {ex.Message}");
            }
        }

        private void InitializeStartupScene()
        {
            currentRootScene = SceneType.Boot;
            CurrentScene = SceneType.Boot;
            Wargame.Instance?.Start();
            GICLog.Info("初始化场景设置，加载 SplashScreen");

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
                GICLog.Info($"场景激活: {previousScene.name} -> {newScene.name}");
            }
        }

        #endregion

        #region 场景加载 - 公共接口

        /// <summary>
        /// 使用配置加载场景
        /// </summary>
        public void LoadSceneWithConfig(SceneType scene)
        {
            // 只在真正的场景切换期间拦截（入场动画等其它输入锁不阻塞导航）
            if (InputLocks.HasLock(InputLockReason.SceneTransition))
            {
                GICLog.Warn("场景正在切换中，请稍后再试");
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
                        GICLog.Info($"开始预加载场景: {scene.SceneName}");
                    }
                }
                else
                {
                    GICLog.Error($"无法预加载场景: {scene.SceneName}");
                }
            }
            catch (Exception ex)
            {
                GICLog.Error($"预加载场景失败: {scene.SceneName}, 错误: {ex.Message}");
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
                GICLog.Error("AsyncOperation 为空，无法激活场景");
                yield break;
            }

            if (!ValidateSceneActivation(scene))
            {
                yield break;
            }

            // 场景切换锁 — 防止切换期间输入干扰
            InputLocks.Push(this, InputLockReason.SceneTransition);

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

            // 额外等待一帧，确保新场景的 Start() 已执行（RegisterClosable + InputLock pop 已生效）
            yield return null;

            InputLocks.Pop(this, InputLockReason.SceneTransition);

            if (enableDebugLog)
            {
                GICLog.Info($"场景激活完成: {scene.SceneName}");
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
                GICLog.Error("根场景必须使用 Single 加载模式");
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
            InputLocks.Push(this, InputLockReason.SceneTransition);

            // 记录历史
            RecordSceneHistory(scene);

            // 加载场景
            yield return LoadSceneAsync(scene);

            // 设置为活动场景
            if (scene.LoadMode == LoadSceneMode.Additive)
            {
                yield return SetActiveSceneAfterLoad(scene);
            }

            // 额外等待一帧，确保新场景的 Start() 已执行
            yield return null;

            InputLocks.Pop(this, InputLockReason.SceneTransition);
        }

        private IEnumerator LoadSceneAsync(SceneType scene)
        {

            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(scene.SceneName, scene.LoadMode);

            if (asyncLoad == null)
            {
                GICLog.Error($"无法加载场景: {scene.SceneName}");
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
                GICLog.Error($"场景加载失败: {scene.SceneName}");
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
                GICLog.Error($"无法设置活动场景: {scene.SceneName} 未加载");
            }
        }

        private IEnumerator GoBackCoroutine()
        {
            InputLocks.Push(this, InputLockReason.SceneTransition);

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

            InputLocks.Pop(this, InputLockReason.SceneTransition);
        }

        private IEnumerator LoadNewRootSceneCoroutine(SceneType scene)
        {
            InputLocks.Push(this, InputLockReason.SceneTransition);

            ClearHistory();
            currentRootScene = scene;
            CurrentScene = scene;

            yield return LoadSceneAsync(scene);

            InputLocks.Pop(this, InputLockReason.SceneTransition);
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
                GICLog.Warn($"场景可能已卸载或不存在: {sceneName}");
                yield break;
            }

            while (!asyncUnload.isDone)
            {
                yield return null;
            }

            if (enableDebugLog)
            {
                GICLog.Info($"场景卸载完成: {sceneName}");
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
                GICLog.Info("资源清理完成");
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
                GICLog.Error($"上一个场景未加载且无法重新加载: {scene.SceneName}");
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
                return false;
            }

            return true;
        }

        private void LogMemoryUsage()
        {
            long totalMemory = GC.GetTotalMemory(false) / 1024 / 1024;
            GICLog.Info($"内存使用: {totalMemory} MB");
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
                GICLog.Warn(message);
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



