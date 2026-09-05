using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using GIC.Data.Event;
using UnityEngine.Localization.Settings;
using GIC.Framework;
using GIC.Data;
using GIC.Battle;
using GIC.Tool;
using GIC.Pet;
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

        #region configParams

        [Header("性能配置")]
        [SerializeField] private bool autoCleanupResources = true;

        [Header("调试配置")]
        [SerializeField] private bool enableDebugLog = true;
        [SerializeField] private bool showMemoryUsage = false;

        public Texture2D cursor;

        #endregion

        #region privateField

        // 场景历史记录栈
        private Stack<SceneType> sceneHistory = new Stack<SceneType>();

        // 当前活动的场景类型
        private SceneType currentScene;
        private SceneType currentRootScene;

        // 场景切换锁已由 InputManager.InputLock 替代

        #endregion

        #region publicProperty

        public SceneType CurrentScene
        {
            get => currentScene;
            private set => currentScene = value;
        }

        public bool IsTransitioning
            => InputLocks.HasLock(InputLockReason.SceneTransition);

        #endregion

        #region Unity lifecycle

        private void Awake()
        {
            if (PetMode.Enabled)
            {
                // 桌宠进程：跳过 Wargame 组合根/存档/场景初始化，直接切换到宠物场景（同 exe 双形态，见 docs/19 §5.2）
                EnterPetMode();
                return;
            }

            InitializeSingleton();
            InitializeSceneEvents();
            InitializeGame();   // Wargame.Init 完成，容器就绪
            Context?.Inject(this); // 注入 GameScene 自身的 [Autowired] 字段

            InitSaveSettings();
        }

        private void Start()
        {
            if (PetMode.Enabled)
            {
                return;
            }

            InitializeStartupScene();
        }

        /// <summary>桌宠形态入口：单例声明（桌面已有派蒙则退出）→ 销毁自身并直接加载宠物场景，Boot 其余管理器随场景卸载一并销毁。</summary>
        private void EnterPetMode()
        {
            if (!PetSingleInstance.Acquire())
            {
                Debug.Log("[PetMode] 桌上已有派蒙，本实例退出");
                Application.Quit();
                return;
            }
            Debug.Log("[PetMode] 检测到 --pet-mode，跳过游戏初始化，进入桌宠形态");
            PetSingleInstance.WritePidFile();
            Destroy(gameObject);
            SceneManager.LoadScene("PaimonPet", LoadSceneMode.Single);
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

        private void OnApplicationQuit()
        {
            // 存档兜底（2026-09-05 时机优化）：退出前把标脏未落盘的变更立即写盘
            _saveManager?.SaveGameNow();

            // 设置开启时退出游戏连带关闭派蒙（设置项 closePetOnExit，默认开；关闭则她独立存活；
            // 游戏内形态天然随进程销毁，TryKillPet 对无 pid 文件场景为 no-op——2026-08-27 起两形态共用此钩子）
            if (!PetMode.Enabled && _saveManager?.CurrentSave?.pet.closePetOnExit == true)
            {
                PetSingleInstance.TryKillPet();
            }
        }

        private void OnApplicationPause(bool pause)
        {
            // 存档兜底（2026-09-05 时机优化）：切后台即写盘——移动端后台化后进程随时可能被系统回收，
            // 延迟窗内的未落盘变更必须在挂起前持久化。桌宠形态本组件已自毁，此钩子不触发。
            if (pause && !PetMode.Enabled)
            {
                _saveManager?.SaveGameNow();
            }
        }

        #endregion

        #region initMethod

        // 注入字段：GameScene.Awake 自身完成 Wargame.Init 后注入（InitSaveSettings 调用点在其后）
        [Autowired] private SaveManager _saveManager;

        private ApplicationContext Context => Wargame.Instance?.Context;

        private void InitSaveSettings()
        {
            Cursor.SetCursor(cursor, Vector2.zero, CursorMode.Auto);
            Application.runInBackground = true;

            // 应用存档中的显示设置（语言/帧率/分辨率）
            SettingsApplier.ApplyFromSave(_saveManager?.CurrentSave);
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

            // 派蒙形态分发（docs/19 §6.4，设置"派蒙"栏目）：桌面版=拉起独立进程（原逻辑）；
            // 游戏画面内版=PetInGameHost 本进程内创建实例（其内部抑制桌面拉起）。编辑器内 Launch 为 no-op。
            // 2026-08-28 从 Awake 挪到此处：存档加载在 SaveManager [PostConstruct]（Wargame.Start 内），
            // Awake 时 CurrentSave.pet.petForm 恒为默认 0（桌面版）——设置"游戏内派蒙"重启仍拉桌面版的根因。
            PetInGameHost.StartupDispatch(_saveManager?.CurrentSave?.pet.petForm ?? 0);
            PetProcessLauncher.Launch();

            // 桌宠聊天指令消费宿主（2026-08-30，docs/19 §6.5）：桌面宠进程经 PetIntentIpc 文件通道
            // 转发的指令（游戏时间/打开界面）由本 host 轮询执行。主进程常建（无请求时开销可忽略）；
            // 桌宠进程在 EnterPetMode 已早退，不会走到这里。
            GIC.Pet.Chat.PetIntentIpcHost.EnsureExists();

            GICLog.Info("初始化场景设置，加载 SplashScreen");

            // 从 Boot 场景加载 SplashScreen（Single 模式，Boot 场景被卸载，持久化管理器通过 DontDestroyOnLoad 存活）
            SceneType.SplashScreen.Load();
        }

        private void CleanupEventHandlers()
        {
            SceneManager.activeSceneChanged -= OnActiveSceneChangedInternal;
        }

        #endregion

        #region evtHandle

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

        #region sceneLoad - publicInterface

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

        #endregion

        #region sceneLoad - 协程实现

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

        #region resCleanup

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

        #endregion

        #region helperMethod

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

        #region 弹窗/toast
        // 弹窗与轻提示已收敛到 PopupManager（静态 Instance 直连），GameScene 不再转发
        #endregion

        #region publicQueryMethod

        public bool HasPreviousScene()
        {
            return sceneHistory.Count > 0;
        }

        public void ClearHistory()
        {
            sceneHistory.Clear();
        }

        #endregion
    }
}



