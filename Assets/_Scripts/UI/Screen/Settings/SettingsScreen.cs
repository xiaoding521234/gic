using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using System.Collections;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
using GIC.Tool;
namespace GIC.UI
{


    public class SettingsScreen : MonoBehaviour
    {
        [Autowired] private SaveManager _saveManager;

        [Header("顶部")]
        public GameObject topPanel;
        public TextCombiner titleText;
        public Button closeButton;

        [Header("左侧导航")]
        public GameObject leftPanel;
        public RectTransform select;
        public Button displayButton;
        public TextCombiner displayButtonText;
        public Button soundButton;
        public TextCombiner soundButtonText;
        public Button controlButton;
        public TextCombiner controlButtonText;
        public Button accountButton;
        public TextCombiner accountButtonText;
        public Button otherButton;
        public TextCombiner otherButtonText;

        [Header("中央内容区")]
        public GameObject centerPanel;
        public CanvasGroup centerGroup;

        [Header("显示设置")]
        public GameObject displaySettings;
        public DropdownSettingItem languageSetting;
        public DropdownSettingItem resolutionSetting;
        public DropdownSettingItem frameRateSetting;

        [Header("声音设置")]
        public GameObject soundSettings;
        public SliderSettingItem masterVolumeSetting;
        public SliderSettingItem musicVolumeSetting;
        public SliderSettingItem sfxVolumeSetting;
        public SliderSettingItem voiceVolumeSetting;

        [Header("操作设置")]
        public GameObject controlSettings;

        [Header("账户设置")]
        public GameObject accountSettings;
        public ButtonSettingItem playerNameSetting;
        public ButtonSettingItem commandSetting;

        [Header("其它设置")]
        public GameObject otherSettings;

        [Header("动画")]
        [SerializeField] private float panelSlideDuration = 0.2f;
        [SerializeField] private AnimationCurve slideCurve = new AnimationCurve(
            new Keyframe(0, 0, 2f, 2f),
            new Keyframe(1, 1, 0f, 0f)
        );
        [SerializeField] private float topPanelSlideOffset = 80f;
        [SerializeField] private float leftPanelSlideOffset = 100f;
        [SerializeField] private float centerFadeOffset = 50f;

        private List<GameObject> settingPanels = new List<GameObject>();
        private List<Button> navButtons = new List<Button>();
        private List<TextCombiner> navTexts = new List<TextCombiner>();
        private List<RectTransform> navRects = new List<RectTransform>();

        private static readonly Color SELECTED_COLOR = "FFD780".FromHex();
        private static readonly Color UNSELECTED_COLOR = "FFFFFF".FromHex();

        private Coroutine moveCoroutine;
        private const float MOVE_DURATION = 0.2f;

        // 动画缓存
        private RectTransform topPanelRect;
        private RectTransform leftPanelRect;
        private RectTransform centerPanelRect;
        private Vector2 topPanelTargetPos;
        private Vector2 leftPanelTargetPos;
        private Vector2 centerPanelTargetPos;
        private bool animationsCached = false;

        void Start()
        {
            Wargame.Instance.Context.Inject(this);

            // 收集所有设置面板
            settingPanels.Add(displaySettings);
            settingPanels.Add(soundSettings);
            settingPanels.Add(controlSettings);
            settingPanels.Add(accountSettings);
            settingPanels.Add(otherSettings);

            // 收集所有导航按钮
            navButtons.Add(displayButton);
            navButtons.Add(soundButton);
            navButtons.Add(controlButton);
            navButtons.Add(accountButton);
            navButtons.Add(otherButton);

            // 收集所有导航文本
            navTexts.Add(displayButtonText);
            navTexts.Add(soundButtonText);
            navTexts.Add(controlButtonText);
            navTexts.Add(accountButtonText);
            navTexts.Add(otherButtonText);

            // 收集所有导航按钮的 RectTransform
            navRects.Add(displayButton.GetComponent<RectTransform>());
            navRects.Add(soundButton.GetComponent<RectTransform>());
            navRects.Add(controlButton.GetComponent<RectTransform>());
            navRects.Add(accountButton.GetComponent<RectTransform>());
            navRects.Add(otherButton.GetComponent<RectTransform>());

            // 绑定导航按钮
            displayButton.onClick.AddListener(() => SwitchPanel(0));
            soundButton.onClick.AddListener(() => SwitchPanel(1));
            controlButton.onClick.AddListener(() => SwitchPanel(2));
            accountButton.onClick.AddListener(() => SwitchPanel(3));
            otherButton.onClick.AddListener(() => SwitchPanel(4));

            // 绑定关闭按钮
            closeButton.onClick.AddListener(Close);

            // 初始化本地化文本
            InitLocalizedTexts();

            // 初始化各项设置
            InitDisplaySettings();
            InitSoundSettings();
            InitAccountSettings();

            // 缓存动画位置
            CacheAnimationPositions();

            // 播放进入动画
            PlayEnterAnimation();

            // 延迟一帧初始化选中效果位置
            StartCoroutine(InitSelectPosition());
        }

        #region 动画

        private void CacheAnimationPositions()
        {
            if (animationsCached) return;

            if (topPanel != null)
            {
                topPanelRect = topPanel.GetComponent<RectTransform>();
                if (topPanelRect != null)
                    topPanelTargetPos = topPanelRect.anchoredPosition;
            }

            if (leftPanel != null)
            {
                leftPanelRect = leftPanel.GetComponent<RectTransform>();
                if (leftPanelRect != null)
                    leftPanelTargetPos = leftPanelRect.anchoredPosition;
            }

            if (centerPanel != null)
            {
                centerPanelRect = centerPanel.GetComponent<RectTransform>();
                if (centerPanelRect != null)
                    centerPanelTargetPos = centerPanelRect.anchoredPosition;
            }

            animationsCached = true;
        }

        private void PlayEnterAnimation()
        {
            // 设置初始位置
            if (topPanelRect != null)
                topPanelRect.anchoredPosition = topPanelTargetPos + Vector2.up * topPanelSlideOffset;

            if (leftPanelRect != null)
                leftPanelRect.anchoredPosition = leftPanelTargetPos + Vector2.left * leftPanelSlideOffset;

            if (centerPanelRect != null)
                centerPanelRect.anchoredPosition = centerPanelTargetPos + Vector2.down * centerFadeOffset;

            if (centerGroup != null)
                centerGroup.alpha = 0f;

            StartCoroutine(PlayEnterAnimationCoroutine());
        }

        private IEnumerator PlayEnterAnimationCoroutine()
        {
            float elapsed = 0f;

            while (elapsed < panelSlideDuration)
            {
                elapsed += Time.deltaTime;
                float t = slideCurve.Evaluate(elapsed / panelSlideDuration);

                if (topPanelRect != null)
                {
                    topPanelRect.anchoredPosition = Vector2.Lerp(
                        topPanelTargetPos + Vector2.up * topPanelSlideOffset,
                        topPanelTargetPos,
                        t
                    );
                }

                if (leftPanelRect != null)
                {
                    leftPanelRect.anchoredPosition = Vector2.Lerp(
                        leftPanelTargetPos + Vector2.left * leftPanelSlideOffset,
                        leftPanelTargetPos,
                        t
                    );
                }

                if (centerPanelRect != null)
                {
                    centerPanelRect.anchoredPosition = Vector2.Lerp(
                        centerPanelTargetPos + Vector2.down * centerFadeOffset,
                        centerPanelTargetPos,
                        t
                    );
                }

                if (centerGroup != null)
                {
                    centerGroup.alpha = Mathf.Lerp(0f, 1f, t);
                }

                yield return null;
            }

            // 确保最终位置
            if (topPanelRect != null)
                topPanelRect.anchoredPosition = topPanelTargetPos;
            if (leftPanelRect != null)
                leftPanelRect.anchoredPosition = leftPanelTargetPos;
            if (centerPanelRect != null)
                centerPanelRect.anchoredPosition = centerPanelTargetPos;
            if (centerGroup != null)
                centerGroup.alpha = 1f;
        }

        private IEnumerator PlayExitAnimationCoroutine()
        {
            float elapsed = 0f;
            float slideOutDuration = panelSlideDuration * 0.7f;

            float startCenterAlpha = centerGroup != null ? centerGroup.alpha : 1f;

            while (elapsed < slideOutDuration)
            {
                elapsed += Time.deltaTime;
                float t = slideCurve.Evaluate(elapsed / slideOutDuration);

                if (topPanelRect != null)
                {
                    topPanelRect.anchoredPosition = Vector2.Lerp(
                        topPanelTargetPos,
                        topPanelTargetPos + Vector2.up * topPanelSlideOffset,
                        t
                    );
                }

                if (leftPanelRect != null)
                {
                    leftPanelRect.anchoredPosition = Vector2.Lerp(
                        leftPanelTargetPos,
                        leftPanelTargetPos + Vector2.left * leftPanelSlideOffset,
                        t
                    );
                }

                if (centerPanelRect != null)
                {
                    centerPanelRect.anchoredPosition = Vector2.Lerp(
                        centerPanelTargetPos,
                        centerPanelTargetPos + Vector2.down * centerFadeOffset,
                        t
                    );
                }

                if (centerGroup != null)
                {
                    centerGroup.alpha = Mathf.Lerp(startCenterAlpha, 0f, t);
                }

                yield return null;
            }

            if (centerGroup != null) centerGroup.alpha = 0f;

            GameScene.Instance.GoBack();
        }

        #endregion

        private IEnumerator InitSelectPosition()
        {
            yield return null;

            if (select != null && navRects.Count > 0 && navRects[0] != null)
            {
                select.position = navRects[0].position;
                select.sizeDelta = navRects[0].sizeDelta;
            }

            currentPanelIndex = 0;
            for (int i = 0; i < navTexts.Count; i++)
            {
                if (navTexts[i] != null && navTexts[i].textComponent != null)
                {
                    navTexts[i].textComponent.color = (i == 0) ? SELECTED_COLOR : UNSELECTED_COLOR;
                }
            }
            for (int i = 0; i < settingPanels.Count; i++)
            {
                if (settingPanels[i] != null)
                {
                    settingPanels[i].SetActive(i == 0);
                }
            }
        }

        private void InitLocalizedTexts()
        {
            titleText.SetSingleEntry(new LocalizedString("UIText", "Settings"));
            displayButtonText.SetSingleEntry(new LocalizedString("UIText", "Display"));
            soundButtonText.SetSingleEntry(new LocalizedString("UIText", "Sound"));
            controlButtonText.SetSingleEntry(new LocalizedString("UIText", "Control"));
            accountButtonText.SetSingleEntry(new LocalizedString("UIText", "Account"));
            otherButtonText.SetSingleEntry(new LocalizedString("UIText", "Other"));
        }

        #region 显示设置

        private void InitDisplaySettings()
        {
            InitLanguageSetting();
            InitResolutionSetting();
            InitFrameRateSetting();
        }

        private void InitLanguageSetting()
        {
            var locales = LocalizationSettings.AvailableLocales.Locales;
            var languageOptions = new List<TextEntry>();

            string[] languageNames = new string[]
            {
                "璃月（简中）",
                "璃月（繁中）",
                "Mondstadt（English）",
                "稲妻（日語）",
                "Снежная（Русский）"
            };

            int currentIndex = 0;
            for (int i = 0; i < locales.Count && i < languageNames.Length; i++)
            {
                languageOptions.Add(new TextEntry(null, languageNames[i]));
                if (locales[i] == LocalizationSettings.SelectedLocale)
                {
                    currentIndex = i;
                }
            }

            int savedIndex = _saveManager.CurrentSave.languageIndex;
            if (savedIndex >= 0 && savedIndex < languageOptions.Count)
            {
                currentIndex = savedIndex;
            }

            languageSetting.Setup("Language", languageOptions, currentIndex, (index) =>
            {
                if (index >= 0 && index < locales.Count)
                {
                    LocalizationSettings.SelectedLocale = locales[index];
                    _saveManager.CurrentSave.languageIndex = index;
                    _saveManager.SaveGame();
                }
            });
            languageSetting.Initialize();
        }

        private void InitResolutionSetting()
        {
            var resolutions = Screen.resolutions;
            var resolutionOptions = new List<TextEntry>();

            resolutionOptions.Add(new TextEntry(new LocalizedString("UIText", "Fullscreen"), ""));

            for (int i = 0; i < resolutions.Length; i++)
            {
                string resText = $"{resolutions[i].width}x{resolutions[i].height}";
                resolutionOptions.Add(new TextEntry(null, resText));
            }

            int currentIndex = _saveManager.CurrentSave.resolutionIndex;

            resolutionSetting.Setup("Resolution", resolutionOptions, currentIndex, (index) =>
            {
                _saveManager.CurrentSave.resolutionIndex = index;
                _saveManager.SaveGame();

                if (index == 0)
                {
                    Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
                    var desktopRes = Screen.currentResolution;
                    Screen.SetResolution(desktopRes.width, desktopRes.height, FullScreenMode.FullScreenWindow);
                }
                else
                {
                    int resIndex = index - 1;
                    if (resIndex >= 0 && resIndex < resolutions.Length)
                    {
                        var res = resolutions[resIndex];
                        Screen.SetResolution(res.width, res.height, FullScreenMode.Windowed);
                    }
                }
            });
            resolutionSetting.Initialize();
        }

        private void InitFrameRateSetting()
        {
            int[] frameRates = { 60, 120, 144, 165, 240, 300 };
            var frameRateOptions = new List<TextEntry>();

            int currentIndex = 0;
            int currentFrameRate = Application.targetFrameRate;

            for (int i = 0; i < frameRates.Length; i++)
            {
                frameRateOptions.Add(new TextEntry(null, frameRates[i].ToString()));
                if (frameRates[i] == currentFrameRate)
                {
                    currentIndex = i;
                }
            }

            int savedIndex = _saveManager.CurrentSave.frameRate;
            for (int i = 0; i < frameRates.Length; i++)
            {
                if (frameRates[i] == savedIndex)
                {
                    currentIndex = i;
                    break;
                }
            }

            frameRateSetting.Setup("FrameRate", frameRateOptions, currentIndex, (index) =>
            {
                if (index >= 0 && index < frameRates.Length)
                {
                    Application.targetFrameRate = frameRates[index];
                    _saveManager.CurrentSave.frameRate = frameRates[index];
                    _saveManager.SaveGame();
                }
            });
            frameRateSetting.Initialize();
        }

        #endregion

        #region 声音设置

        private void InitSoundSettings()
        {
            var audioManager = AudioManager.Instance;

            masterVolumeSetting.Setup("MasterVolume", 0f, 10f, audioManager.GetMasterVolume() * 10f, (value) =>
            {
                audioManager.SetMasterVolume(value / 10f);
            }, showAsPercent: false, stepSize: 1f);
            masterVolumeSetting.Initialize();

            musicVolumeSetting.Setup("MusicVolume", 0f, 10f, audioManager.GetMusicVolume() * 10f, (value) =>
            {
                audioManager.SetMusicVolume(value / 10f);
            }, showAsPercent: false, stepSize: 1f);
            musicVolumeSetting.Initialize();

            sfxVolumeSetting.Setup("SFXVolume", 0f, 10f, audioManager.GetSFXVolume() * 10f, (value) =>
            {
                audioManager.SetSFXVolume(value / 10f);
            }, showAsPercent: false, stepSize: 1f);
            sfxVolumeSetting.Initialize();

            voiceVolumeSetting.Setup("VoiceVolume", 0f, 10f, audioManager.GetVoiceVolume() * 10f, (value) =>
            {
                audioManager.SetVoiceVolume(value / 10f);
            }, showAsPercent: false, stepSize: 1f);
            voiceVolumeSetting.Initialize();
        }

        #endregion

        #region 账户设置

        private void InitAccountSettings()
        {
            var saveManager = _saveManager;
            string playerName = saveManager.CurrentSave.playerName;

            playerNameSetting.Setup("Name", playerName,
                onClick: () =>
                {
                    ShowInputPanel(playerNameSetting, playerName, (newName) =>
                    {
                        saveManager.CurrentSave.playerName = newName;
                        saveManager.SaveGame();
                        playerNameSetting.UpdateValue(newName);
                    });
                },
                onValueConfirmed: null
            );
            playerNameSetting.Initialize();

            // TODO: 指令功能暂未实现，仅预留输入弹窗入口
            commandSetting.Setup("Command", "",
                onClick: () =>
                {
                    ShowInputPanel(commandSetting, "", (newCommand) =>
                    {
                        Debug.Log($"玩家指令: {newCommand}");
                    });
                },
                onValueConfirmed: null,
                placeholderKey: "Input"
            );
            commandSetting.Initialize();
        }

        #endregion

        #region 面板切换

        private int currentPanelIndex = -1;

        private void SwitchPanel(int index)
        {
            if (currentPanelIndex == index) return;

            for (int i = 0; i < navTexts.Count; i++)
            {
                if (navTexts[i] != null && navTexts[i].textComponent != null)
                {
                    navTexts[i].textComponent.color = (i == index) ? SELECTED_COLOR : UNSELECTED_COLOR;
                }
            }

            MoveSelectTo(index);

            for (int i = 0; i < settingPanels.Count; i++)
            {
                if (settingPanels[i] != null)
                {
                    settingPanels[i].SetActive(i == index);
                }
            }

            currentPanelIndex = index;
        }

        private void MoveSelectTo(int index)
        {
            if (select == null || index < 0 || index >= navRects.Count) return;
            if (navRects[index] == null) return;

            if (moveCoroutine != null)
            {
                StopCoroutine(moveCoroutine);
            }
            moveCoroutine = StartCoroutine(SmoothMoveSelect(navRects[index]));
        }

        private IEnumerator SmoothMoveSelect(RectTransform target)
        {
            Vector2 startPos = select.position;
            Vector2 targetPos = target.position;
            Vector2 startSize = select.sizeDelta;
            Vector2 targetSize = target.sizeDelta;

            float elapsed = 0f;
            while (elapsed < MOVE_DURATION)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / MOVE_DURATION;
                t = 1f - Mathf.Pow(1f - t, 3f);

                select.position = Vector2.Lerp(startPos, targetPos, t);
                select.sizeDelta = Vector2.Lerp(startSize, targetSize, t);
                yield return null;
            }

            select.position = targetPos;
            select.sizeDelta = targetSize;
        }

        #endregion

        #region 输入弹窗

        [SerializeField] private InputPopupDialog inputPopupPrefab;
        private InputPopupDialog inputPopupInstance;

        private void ShowInputPanel(ButtonSettingItem settingItem, string currentValue, Action<string> callback)
        {
            if (inputPopupPrefab == null) return;

            if (inputPopupInstance == null)
            {
                inputPopupInstance = Instantiate(inputPopupPrefab, transform);
            }

            string titleKey = settingItem == playerNameSetting ? "ModifyName" : "InputCommand";
            inputPopupInstance.Show(titleKey, currentValue, callback);
        }

        #endregion

        public void Close()
        {
            StartCoroutine(PlayExitAnimationCoroutine());
        }
    }
}


