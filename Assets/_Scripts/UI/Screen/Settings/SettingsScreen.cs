// ==================== SettingsScreen.cs（字段声明 + 启动流程 + 关闭） ====================
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


    /// <summary>
    /// 设置界面 — partial 拆分：
    /// 主文件（字段+Start+本地化+关闭）/ Animation（入场退场动画）/ Navigation（面板切换）
    /// / Display（显示设置）/ Audio（声音设置）/ Control（操作设置）/ Account（账户设置+输入弹窗）
    /// </summary>
    public partial class SettingsScreen : ScreenBase
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
        public KeyBindingSettingItem closeUIPrimaryKeyItem;
        public KeyBindingSettingItem closeUISecondaryKeyItem;
        public KeyBindingSettingItem confirmPrimaryKeyItem;
        public KeyBindingSettingItem confirmSecondaryKeyItem;

        [Header("账户设置")]
        public GameObject accountSettings;
        public ButtonSettingItem playerNameSetting;
        public ButtonSettingItem commandSetting;

        [Header("其它设置")]
        public GameObject otherSettings;
        public DropdownSettingItem petCloseSetting;

        [Header("动画")]
        [SerializeField] private float panelSlideDuration = 0.2f;
        [SerializeField] private AnimationCurve slideCurve = new AnimationCurve(
            new Keyframe(0, 0, 2f, 2f),
            new Keyframe(1, 1, 0f, 0f)
        );
        [SerializeField] private float topPanelSlideOffset = 80f;
        [SerializeField] private float leftPanelSlideOffset = 100f;
        [SerializeField] private float centerFadeOffset = 50f;

        [Header("输入弹窗")]
        [SerializeField] private InputPopupDialog inputPopupPrefab;

        // 导航面板/按钮/文本集合（Start 填充，Navigation partial 使用）
        private List<GameObject> settingPanels = new List<GameObject>();
        private List<Button> navButtons = new List<Button>();
        private List<TextCombiner> navTexts = new List<TextCombiner>();
        private List<RectTransform> navRects = new List<RectTransform>();

        void Start()
        {
            RegisterClosableSelf();

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
            InitControlSettings();
            InitAccountSettings();
            InitOtherSettings();

            // 缓存动画位置
            CacheAnimationPositions();

            // 播放进入动画
            PlayEnterAnimation();

            // 延迟一帧初始化选中效果位置
            StartCoroutine(InitSelectPosition());
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

        // ── IClosable 实现（标准关闭模板 + 退场动画） ──
        public override void Close()
        {
            CloseScreen(PlayExitAnimationCoroutine);
        }
    }
}
