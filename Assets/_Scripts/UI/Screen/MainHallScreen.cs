using System.Collections;
using System.Collections.Generic;
using GIC.Data.Event;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using GIC.Framework;
using GIC.Data;
namespace GIC.UI
{


    /// <summary>
    /// 主厅 — 大厅背景、左右功能按钮与入场/退出动画
    /// 拆分文件：Animation（按钮动画）、Background（背景加载）
    /// </summary>
    public partial class MainHallScreen : MonoBehaviour
    {
        [Header("背景")]
        [SerializeField] private SpriteRenderer backgroundRenderer;
        [SerializeField] private VideoPlayer backgroundVideoPlayer;

        [Header("左侧按钮")]
        [SerializeField] private Button missionButton;
        [SerializeField] private Button mapButton;
        [SerializeField] private Button achievementButton;
        [SerializeField] private Button settingsButton;

        [Header("右侧按钮")]
        [SerializeField] private Button wishButton;
        [SerializeField] private Button backpackButton;
        [SerializeField] private Button coopButton;
        [SerializeField] private Button tutorialButton;

        [Header("动画设置")]
        [SerializeField] private float animationDuration = 0.35f;
        [SerializeField] private float staggerDelay = 0.06f;
        [SerializeField] private float leftStartX = -500f;
        [SerializeField] private float rightStartX = 500f;
        [SerializeField] private AnimationCurve easeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("淡出动画设置")]
        [SerializeField] private bool reverseStaggerOnExit = true;
        [SerializeField] private float exitStaggerOffset = 0.01f;

        // 按钮动画状态（Animation partial 与退出流程共用）
        private List<RectTransform> leftButtons = new List<RectTransform>();
        private List<RectTransform> rightButtons = new List<RectTransform>();
        private Dictionary<RectTransform, Vector2> originalPositions = new Dictionary<RectTransform, Vector2>();

        private Coroutine exitAnimationCoroutine;
        private bool isExiting = false;

        /// <summary>P2 面板分支标记：按钮已滑出待复位（面板关→OnGoBackEvent 复位；场景路径 OnSceneActivated 复位，标志互斥防双动画）</summary>
        private bool _buttonsOut = false;

        private Wargame wargame;
        [Autowired] private PositionManager _positionManager;
        [Autowired] private AssetCache _assetCache;
        private PositionChangedHandler _positionHandler;
        private SceneActivatedHandler _sceneActivatedHandler;
        private GoBackHandler _goBackHandler;
        private GameTimeChangedHandler _gameTimeHandler;

        private void Start()
        {
            wargame = Wargame.Instance;
            wargame?.Context?.Inject(this);

            InitializeButtons();
            InitializeAnimation();

            // 创建事件处理器
            _positionHandler = new PositionChangedHandler(this);
            _sceneActivatedHandler = new SceneActivatedHandler(this);
            _goBackHandler = new GoBackHandler(this);
            _gameTimeHandler = new GameTimeChangedHandler(this);

            // 订阅事件
            EventBusHub.Instance.Subscribe(_positionHandler, this);
            EventBusHub.Instance.Subscribe(_sceneActivatedHandler, this);
            EventBusHub.Instance.Subscribe(_goBackHandler, this);
            EventBusHub.Instance.Subscribe(_gameTimeHandler, this);

            UpdateBackground(_positionManager.CurrentPosition);

            // 预载大地图初始视野瓦片（Persistent）：打开地图时缓存命中，首帧即清晰（不先糊后清）
            MapTilePreloader.PreloadInitialTiles(_positionManager, _assetCache);

            // 播放入场动画
            PlayEnterAnimation();
        }

        private void OnDestroy()
        {
            // owner 登记制：一行退订全部事件
            EventBusHub.Instance?.UnsubscribeOwner(this);

            if (_lastBgAddress != null)
                _assetCache?.Release(_lastBgAddress);
            // 若有正在加载但未完成的新背景，也释放
            if (_pendingBgAddress != null && _pendingBgAddress != _lastBgAddress)
                _assetCache?.Release(_pendingBgAddress);

            // 清理视频背景资源
            OnDestroyBackground();
        }

        #region evtHandler

        private class PositionChangedHandler : IEventHandler<OnPositionChangedEvent>
        {
            private MainHallScreen _screen;

            public PositionChangedHandler(MainHallScreen screen)
            {
                _screen = screen;
            }

            public bool CanHandle(OnPositionChangedEvent evt)
            {
                return _screen != null && _screen.gameObject.activeInHierarchy;
            }

            public void Handle(OnPositionChangedEvent evt)
            {
                _screen.UpdateBackground(evt.PositionName);
                // 传送后初始区域已变，补预载新区域瓦片
                MapTilePreloader.PreloadInitialTiles(_screen._positionManager, _screen._assetCache);
            }
        }

        private class SceneActivatedHandler : IEventHandler<OnSceneActivatedEvent>
        {
            private MainHallScreen _screen;

            public SceneActivatedHandler(MainHallScreen screen)
            {
                _screen = screen;
            }

            public bool CanHandle(OnSceneActivatedEvent evt)
            {
                return _screen != null && evt.SceneName == _screen.gameObject.scene.name;
            }

            public void Handle(OnSceneActivatedEvent evt)
            {
                _screen.OnSceneActivated();
            }
        }

        /// <summary>游戏内时间变更（派蒙对话 set_game_time 工具，2026-08-29）：重载背景——
        /// 时段切换立即生效（白天↔夜晚换图/换视频）；同时段内调时间同地址自动跳过重载</summary>
        private class GameTimeChangedHandler : IEventHandler<OnGameTimeChangedEvent>
        {
            private MainHallScreen _screen;

            public GameTimeChangedHandler(MainHallScreen screen)
            {
                _screen = screen;
            }

            public bool CanHandle(OnGameTimeChangedEvent evt)
            {
                return _screen != null && _screen.gameObject.activeInHierarchy;
            }

            public void Handle(OnGameTimeChangedEvent evt)
            {
                _screen.UpdateBackground(_screen._positionManager.CurrentPosition);
            }
        }

        private class GoBackHandler : IEventHandler<OnGoBackEvent>
        {
            private MainHallScreen _screen;

            public GoBackHandler(MainHallScreen screen)
            {
                _screen = screen;
            }

            public bool CanHandle(OnGoBackEvent evt)
            {
                return _screen != null && evt.ToScene == _screen.gameObject.scene.name;
            }

            public void Handle(OnGoBackEvent evt)
            {
                _screen.OnReturnedFromScene(evt.FromScene);
            }
        }

        #endregion

        #region evtResponseMethod

        private void OnSceneActivated()
        {
            // 场景被激活时重置并播放入场动画（场景路径的按钮复位入口）
            _buttonsOut = false;
            ResetAndPlayEnterAnimation();
        }

        private void OnReturnedFromScene(string fromScene)
        {
            GICLog.Info($"从 {fromScene} 返回到大厅");
            // P2 面板分支：面板关闭不切换场景（OnSceneActivated 不触发），
            // 按钮复位由本钩子驱动；_buttonsOut 标志与场景路径互斥（防双动画）
            if (_buttonsOut)
            {
                _buttonsOut = false;
                ResetAndPlayEnterAnimation();
            }
        }

        #endregion

        private void InitializeButtons()
        {
            if (missionButton != null) missionButton.onClick.AddListener(OnMissionButtonClick);
            if (mapButton != null) mapButton.onClick.AddListener(OnMapButtonClick);
            if (achievementButton != null) achievementButton.onClick.AddListener(OnAchievementButtonClick);
            if (settingsButton != null) settingsButton.onClick.AddListener(OnSettingsButtonClick);

            if (wishButton != null) wishButton.onClick.AddListener(OnWishButtonClick);
            if (backpackButton != null) backpackButton.onClick.AddListener(OnBackpackButtonClick);
            if (coopButton != null) coopButton.onClick.AddListener(OnCoopButtonClick);
            if (tutorialButton != null) tutorialButton.onClick.AddListener(OnTutorialButtonClick);
        }

        // ==================== 场景切换 ====================

        public void ExitToSceneAsync(SceneType scene)
        {
            if (isExiting) return;

            // P2 面板分支（docs/23 §3.5）：prefab 面板即时实例化，按钮退场交错动画并行播放
            //（无需预载/激活场景）；面板关闭后经 OnGoBackEvent→OnReturnedFromScene 复位按钮
            var id = Screens.FromSceneName(scene.SceneName);
            if (id != null && id.Host == ScreenHostKind.Prefab)
            {
                isExiting = true;
                _buttonsOut = true;
                SetButtonsInteractable(false);
                UIManager.Instance.Open(id);
                StartCoroutine(ExitButtonsOnlyCoroutine());
                return;
            }

            SetButtonsInteractable(false);
            StartCoroutine(ExitWithPreloadCoroutine(scene));
        }

        private IEnumerator ExitWithPreloadCoroutine(SceneType scene)
        {
            isExiting = true;

            AsyncOperation asyncLoad = null;

            //  启动预加载协程
            yield return StartCoroutine(GameScene.Instance.PreloadScene(scene, op => asyncLoad = op));

            if (asyncLoad == null)
            {
                GICLog.Error("无法预加载场景");
                isExiting = false;
                SetButtonsInteractable(true);
                yield break;
            }

            // 播放退出动画
            List<Coroutine> exitCoroutines = new List<Coroutine>();
            CollectExitCoroutines(exitCoroutines);

            foreach (var coroutine in exitCoroutines)
            {
                yield return coroutine;
            }

            yield return GameScene.Instance.ActivatePreloadedScene(asyncLoad, scene);

            isExiting = false;
        }

        /// <summary>P2 面板分支的按钮退场：同款交错动画但不激活场景；复位由 OnGoBackEvent 钩子负责</summary>
        private IEnumerator ExitButtonsOnlyCoroutine()
        {
            List<Coroutine> exitCoroutines = new List<Coroutine>();
            CollectExitCoroutines(exitCoroutines);

            foreach (var coroutine in exitCoroutines)
            {
                yield return coroutine;
            }

            isExiting = false; // 按钮退完即可再次接受跳转（面板已开，无需守场景激活）
        }

        /// <summary>收集左右按钮的退场动画协程（reverseStaggerOnExit 逆序交错）</summary>
        private void CollectExitCoroutines(List<Coroutine> exitCoroutines)
        {
            for (int i = 0; i < leftButtons.Count; i++)
            {
                var btn = leftButtons[i];
                if (btn != null)
                {
                    float delay = reverseStaggerOnExit
                        ? (leftButtons.Count - 1 - i) * staggerDelay + exitStaggerOffset
                        : i * staggerDelay;

                    exitCoroutines.Add(StartCoroutine(AnimateButtonExit(btn, leftStartX, delay)));
                }
            }

            for (int i = 0; i < rightButtons.Count; i++)
            {
                var btn = rightButtons[i];
                if (btn != null)
                {
                    float delay = reverseStaggerOnExit
                        ? (rightButtons.Count - 1 - i) * staggerDelay + exitStaggerOffset
                        : i * staggerDelay;

                    exitCoroutines.Add(StartCoroutine(AnimateButtonExit(btn, rightStartX, delay)));
                }
            }
        }

        // ==================== 按钮回调 ====================

        #region leftBtnCallback
        private void OnMissionButtonClick()
        {
            GICLog.Info("打开任务界面");
        }

        private void OnMapButtonClick()
        {
            ExitToSceneAsync(SceneType.MapScreen);
        }

        private void OnAchievementButtonClick()
        {
            GICLog.Info("打开成就界面");
        }

        private void OnSettingsButtonClick()
        {
            ExitToSceneAsync(SceneType.SettingsScreen);
        }
        #endregion

        #region rightBtnCallback
        private void OnWishButtonClick()
        {
            ExitToSceneAsync(SceneType.WishScreen);
        }

        private void OnBackpackButtonClick()
        {
            ExitToSceneAsync(SceneType.BackpackScreen);
        }

        private void OnCoopButtonClick()
        {
            ExitToSceneAsync(SceneType.CoopScreen);
        }

        private void OnTutorialButtonClick()
        {
            GICLog.Info("打开教程界面");
        }
        #endregion
    }
}
