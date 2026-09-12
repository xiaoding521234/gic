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

        // prefab 面板：静态身份（场景名寻址在面板实例化进宿主场景后失效，P2 定则）
        protected override ScreenId Id => Screens.Settings;

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
        public Button petButton;
        public TextCombiner petButtonText;
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

        [Header("派蒙设置（docs/19 §6.4，2026-08-27）")]
        public GameObject petSettings;
        public DropdownSettingItem petFormSetting;
        public DropdownSettingItem petProviderSetting; // 2026-08-29 对话模型供应商（多供应商支持）
        public DropdownSettingItem petCloseSetting; // 2026-08-27 从"其它"栏挪入"派蒙"栏
        public ButtonSettingItem petApiKeySetting;  // 2026-08-28 对话 API Key（玩家自输，加密存储）

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

            // 收集所有设置面板（与导航按钮顺序一致：派蒙=第 5 栏，其它=第 6 栏）
            settingPanels.Add(displaySettings);
            settingPanels.Add(soundSettings);
            settingPanels.Add(controlSettings);
            settingPanels.Add(accountSettings);
            settingPanels.Add(petSettings);
            settingPanels.Add(otherSettings);

            // 收集所有导航按钮
            navButtons.Add(displayButton);
            navButtons.Add(soundButton);
            navButtons.Add(controlButton);
            navButtons.Add(accountButton);
            navButtons.Add(petButton);
            navButtons.Add(otherButton);

            // 收集所有导航文本
            navTexts.Add(displayButtonText);
            navTexts.Add(soundButtonText);
            navTexts.Add(controlButtonText);
            navTexts.Add(accountButtonText);
            navTexts.Add(petButtonText);
            navTexts.Add(otherButtonText);

            // 收集所有导航按钮的 RectTransform
            navRects.Add(displayButton.GetComponent<RectTransform>());
            navRects.Add(soundButton.GetComponent<RectTransform>());
            navRects.Add(controlButton.GetComponent<RectTransform>());
            navRects.Add(accountButton.GetComponent<RectTransform>());
            navRects.Add(petButton.GetComponent<RectTransform>());
            navRects.Add(otherButton.GetComponent<RectTransform>());

            // 绑定导航按钮
            displayButton.onClick.AddListener(() => SwitchPanel(0));
            soundButton.onClick.AddListener(() => SwitchPanel(1));
            controlButton.onClick.AddListener(() => SwitchPanel(2));
            accountButton.onClick.AddListener(() => SwitchPanel(3));
            petButton.onClick.AddListener(() => SwitchPanel(4));
            otherButton.onClick.AddListener(() => SwitchPanel(5));

            // 绑定关闭按钮
            closeButton.onClick.AddListener(Close);

            // 初始化本地化文本
            InitLocalizedTexts();

            // 初始化各项设置
            InitDisplaySettings();
            InitSoundSettings();
            InitControlSettings();
            InitAccountSettings();
            InitPetSettings();
            // "其它"栏 2026-08-27 起暂无条目（petClose 挪入派蒙栏）——面板保留占位

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
            petButtonText.SetSingleEntry(new LocalizedString("UIText", "Paimon"));
            otherButtonText.SetSingleEntry(new LocalizedString("UIText", "Other"));
        }

        // ── IClosable 实现（标准关闭模板 + 退场动画） ──
        public override void Close()
        {
            CloseScreen(PlayExitAnimationCoroutine);
        }
    }
}
