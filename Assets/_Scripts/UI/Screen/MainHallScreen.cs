using System.Collections;
using System.Collections.Generic;
using GIC.Data.Event;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using GIC.Framework;
using GIC.Battle;
using GIC.Data;
using GIC.Tool;
namespace GIC.UI
{


    public class MainHallScreen : MonoBehaviour
    {
        [Header("背景")]
        [SerializeField] private SpriteRenderer backgroundRenderer;

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

        private List<RectTransform> leftButtons = new List<RectTransform>();
        private List<RectTransform> rightButtons = new List<RectTransform>();
        private Dictionary<RectTransform, Vector2> originalPositions = new Dictionary<RectTransform, Vector2>();

        private Coroutine exitAnimationCoroutine;
        private bool isExiting = false;

        private Wargame wargame;
        private PositionChangedHandler _positionHandler;
        private SceneActivatedHandler _sceneActivatedHandler;
        private GoBackHandler _goBackHandler;

        private void Start()
        {
            wargame = Wargame.Instance;

            InitializeButtons();
            InitializeAnimation();
            
            // 创建事件处理器
            _positionHandler = new PositionChangedHandler(this);
            _sceneActivatedHandler = new SceneActivatedHandler(this);
            _goBackHandler = new GoBackHandler(this);
            
            // 订阅事件
            EventBusHub.Instance.Subscribe(_positionHandler);
            EventBusHub.Instance.Subscribe(_sceneActivatedHandler);
            EventBusHub.Instance.Subscribe(_goBackHandler);

            UpdateBackground(wargame.PositionManager.CurrentPosition);

            // 播放入场动画
            PlayEnterAnimation();
        }

        private void OnDestroy()
        {
            if (EventBusHub.Instance != null)
            {
                if (_positionHandler != null)
                    EventBusHub.Instance.Unsubscribe(_positionHandler);
                if (_sceneActivatedHandler != null)
                    EventBusHub.Instance.Unsubscribe(_sceneActivatedHandler);
                if (_goBackHandler != null)
                    EventBusHub.Instance.Unsubscribe(_goBackHandler);
            }

            if (_lastBgAddress != null)
                Wargame.Instance?.AssetCache?.Release(_lastBgAddress);
            // 若有正在加载但未完成的新背景，也释放
            if (_pendingBgAddress != null && _pendingBgAddress != _lastBgAddress)
                Wargame.Instance?.AssetCache?.Release(_pendingBgAddress);
        }

        #region 事件处理器
        
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

        #region 事件响应方法
        
        private void OnSceneActivated()
        {
            // 场景被激活时重置并播放入场动画
            ResetAndPlayEnterAnimation();
        }

        private void OnReturnedFromScene(string fromScene)
        {
            GICLog.Info($"从 {fromScene} 返回到大厅");
            // 可以在这里添加返回时的特殊处理
        }
        
        #endregion

        private void ResetAndPlayEnterAnimation()
        {
            // 停止可能正在进行的退出动画
            if (exitAnimationCoroutine != null)
            {
                StopCoroutine(exitAnimationCoroutine);
                exitAnimationCoroutine = null;
            }
            
            StopAllCoroutines();
            
            // 重置按钮位置
            ResetButtonPositions();
            
            // 播放入场动画
            PlayEnterAnimation();
            
            // 重新启用按钮交互
            SetButtonsInteractable(true);
            
            // 更新背景
            if (wargame != null)
            {
                UpdateBackground(wargame.PositionManager.CurrentPosition);
            }
            
            isExiting = false;
        }

        private void PlayEnterAnimation()
        {
            StartCoroutine(AnimateButtonsStaggered());
        }

        private void ResetButtonPositions()
        {
            foreach (var btn in leftButtons)
            {
                if (btn != null && originalPositions.ContainsKey(btn))
                {
                    btn.anchoredPosition = new Vector2(leftStartX, originalPositions[btn].y);
                }
            }

            foreach (var btn in rightButtons)
            {
                if (btn != null && originalPositions.ContainsKey(btn))
                {
                    btn.anchoredPosition = new Vector2(rightStartX, originalPositions[btn].y);
                }
            }
        }

        private string _lastBgAddress;
        private string _pendingBgAddress;

        private void UpdateBackground(PositionName position)
        {
            if (backgroundRenderer == null) return;

            var positionData = wargame.PositionManager.GetPositionData(position);
            if (positionData == null) return;

            RegionName region = positionData.region;

            string regionName = region.ToString();
            string positionName = position.ToString().ToSnakeCase();
            string timeSuffix = TimeUtility.GetTimeSuffix();
            string address = $"PositionBack/{regionName}/{positionName}_{timeSuffix}";

            // 地址相同且精灵已加载 → 跳过重载
            if (address == _lastBgAddress && backgroundRenderer.sprite != null)
                return;

            // 相同地址已在加载中 → 不重复请求
            if (address == _pendingBgAddress)
                return;

            _pendingBgAddress = address;
            string oldAddress = _lastBgAddress;

            wargame.AssetCache?.LoadAsync<Sprite>(address, sprite =>
            {
                if (this == null || backgroundRenderer == null) return;

                // 加载期间地址可能已变（快速切换），丢弃过期结果
                if (_pendingBgAddress != address) return;

                if (sprite != null)
                {
                    backgroundRenderer.sprite = sprite;
                    _lastBgAddress = address;

                    // 新背景已上屏，释放旧背景
                    if (oldAddress != null && oldAddress != address)
                        wargame.AssetCache?.Release(oldAddress);
                }
                else
                {
                    GICLog.Warn($"未找到背景图片: {address}");
                }
            }, LoadPriority.High);
        }

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

        private void InitializeAnimation()
        {
            AddButtonToList(missionButton, leftButtons);
            AddButtonToList(mapButton, leftButtons);
            AddButtonToList(achievementButton, leftButtons);
            AddButtonToList(settingsButton, leftButtons);

            AddButtonToList(wishButton, rightButtons);
            AddButtonToList(backpackButton, rightButtons);
            AddButtonToList(coopButton, rightButtons);
            AddButtonToList(tutorialButton, rightButtons);

            foreach (var btn in leftButtons)
            {
                if (btn != null)
                {
                    originalPositions[btn] = btn.anchoredPosition;
                    btn.anchoredPosition = new Vector2(leftStartX, btn.anchoredPosition.y);
                }
            }

            foreach (var btn in rightButtons)
            {
                if (btn != null)
                {
                    originalPositions[btn] = btn.anchoredPosition;
                    btn.anchoredPosition = new Vector2(rightStartX, btn.anchoredPosition.y);
                }
            }
        }

        private void AddButtonToList(Button button, List<RectTransform> list)
        {
            if (button != null)
                list.Add(button.GetComponent<RectTransform>());
        }

        private IEnumerator AnimateButtonsStaggered()
        {
            for (int i = 0; i < leftButtons.Count; i++)
            {
                var btn = leftButtons[i];
                if (btn != null)
                {
                    StartCoroutine(AnimateSingleButton(btn, leftStartX, originalPositions[btn].x, i * staggerDelay));
                }
            }

            for (int i = 0; i < rightButtons.Count; i++)
            {
                var btn = rightButtons[i];
                if (btn != null)
                {
                    StartCoroutine(AnimateSingleButton(btn, rightStartX, originalPositions[btn].x, i * staggerDelay));
                }
            }

            float maxDelay = Mathf.Max(leftButtons.Count, rightButtons.Count) * staggerDelay;
            yield return new WaitForSeconds(maxDelay + animationDuration);
        }

        private IEnumerator AnimateSingleButton(RectTransform button, float startX, float targetX, float delay)
        {
            if (delay > 0)
                yield return new WaitForSeconds(delay);

            float elapsedTime = 0f;

            while (elapsedTime < animationDuration)
            {
                float t = easeCurve.Evaluate(elapsedTime / animationDuration);
                float x = Mathf.Lerp(startX, targetX, t);
                button.anchoredPosition = new Vector2(x, button.anchoredPosition.y);
                elapsedTime += Time.deltaTime;
                yield return null;
            }

            button.anchoredPosition = new Vector2(targetX, button.anchoredPosition.y);
        }

        private IEnumerator AnimateButtonExit(RectTransform button, float targetX, float delay)
        {
            if (delay > 0)
                yield return new WaitForSeconds(delay);

            float startX = button.anchoredPosition.x;
            float elapsedTime = 0f;

            while (elapsedTime < animationDuration)
            {
                float t = easeCurve.Evaluate(elapsedTime / animationDuration);
                float x = Mathf.Lerp(startX, targetX, t);
                button.anchoredPosition = new Vector2(x, button.anchoredPosition.y);
                elapsedTime += Time.deltaTime;
                yield return null;
            }

            button.anchoredPosition = new Vector2(targetX, button.anchoredPosition.y);
        }

        private void SetButtonsInteractable(bool interactable)
        {
            foreach (var btn in leftButtons)
            {
                var button = btn?.GetComponent<Button>();
                if (button != null) button.interactable = interactable;
            }

            foreach (var btn in rightButtons)
            {
                var button = btn?.GetComponent<Button>();
                if (button != null) button.interactable = interactable;
            }
        }

        // ==================== 场景切换 ====================

        public void ExitToSceneAsync(SceneType scene)
        {
            if (isExiting) return;

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
        
        foreach (var coroutine in exitCoroutines)
        {
            yield return coroutine;
        }
        
        yield return GameScene.Instance.ActivatePreloadedScene(asyncLoad, scene);
        
        isExiting = false;
    }

        // ==================== 按钮回调 ====================

        #region 左侧按钮回调
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

        #region 右侧按钮回调
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



