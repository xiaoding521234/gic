// ==================== BackpackScreen.cs ====================
using System;
using System.Collections;
using System.Collections.Generic;
using GIC.Data.Event;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Battle;
using GIC.Data;
using GIC.Tool;
namespace GIC.UI
{


    public partial class BackpackScreen : ScreenBase
    {
        [Header("卡片展示")]
        public GameObject cardContent;
        public ToggleGroup cardToggleGroup;
        public GameObject cardPrefab;

        [Header("类别切换")]
        public Button nextButtonLeft;
        public Button nextButtonRight;
        public TextCombiner categoryText;

        [Header("编辑")]
        public Button editDeck;
        public Button closeButton;
        public GameObject editDetailsPanel;//进入编辑模式时，从下往上滑入 退出时反之
        public Button returnButton;//点击以退出编辑模式
        public TextMeshProUGUI countText;//显示数量 例如 2/8
        [SerializeField] private Transform deckContent; // 编辑面板内卡组内容容器
        [SerializeField] private float deckCardScale = 0.5f;

        [Header("卡组切换（王者荣耀式长条按钮 + 管理面板）")]
        public Button deckSwitchButton;// 长条卡组按钮：显示当前卡组编号+名称，点击弹出卡组管理面板
        public TextMeshProUGUI deckBarNumberText;// 长条按钮上的编号文本
        public TextMeshProUGUI deckBarNameText;// 长条按钮上的名称文本
        public DeckSwitchPanel deckSwitchPanel;// 卡组管理面板（切换/改名/拖拽排序/复制粘贴/密语）

        [Header("详情面板")]
        public SkillDetailView skillDetailView;
        public CardDetailView cardDetailView;

        [Header("动画")]
        public GameObject topPanel;
        public GameObject bottomPanel;
        public CanvasGroup centerCanvasGroup;
        public CanvasGroup cardDetailCanvasGroup;
        [SerializeField] private float panelSlideDuration = 0.2f;
        [SerializeField]
        private AnimationCurve slideCurve = new AnimationCurve(
            new Keyframe(0, 0, 2f, 2f),
            new Keyframe(1, 1, 0f, 0f)
        );
        [SerializeField] private float panelSlideOffset = 100f;
        [SerializeField] private float buttonSlideOffset = 80f;

        [Header("标签指示线")]
        [SerializeField] private Transform tabContainer;
        [SerializeField] private RectTransform sharedSelectLine;
        [SerializeField] private float lineSlideDuration = 0.2f;
        private List<ItemCategoryView> _tabViews = new();
        private Coroutine _lineCoroutine;

        private BackpackTab currentTab = BackpackTab.Character;
        private int currentDeckId;
        private HashSet<SaveCardData> currentDeckCards = new();
        private List<Card> spawnedCards = new();
        private List<Card> deckSpawnedCards = new();
        private CardPool _cardPool;
        private CardPool _deckCardPool;
        private Coroutine _spawnCoroutine;

        [Autowired] private CardManager cardManager;
        [Autowired] private SaveManager saveManager;
        [Autowired] private UnitConfig unitConfig;
        [Autowired] private ItemConfig itemConfig;
        private BackpackCategoryChangedHandler _categoryChangedHandler;
        private CardClickedInEditHandler _cardClickedHandler;

        private bool isEditMode = false;

        // ── IClosable 实现 ──
        public override void Close() => OnClose();

        // prefab 面板：静态身份（场景名寻址在面板实例化进宿主场景后失效，P2 定则）
        protected override ScreenId Id => Screens.Backpack;

        /// <summary>
        /// 一次性装配（池化生命周期，docs/14 §38：面板不销毁，旧 Start 的 wiring 落此只跑一次）。
        /// </summary>
        protected override void OnInit()
        {
            nextButtonLeft.onClick.AddListener(OnPreviousCategory);
            nextButtonRight.onClick.AddListener(OnNextCategory);
            editDeck.onClick.AddListener(OnToggleEditMode);
            closeButton.onClick.AddListener(OnClose);
            if (deckSwitchButton != null)
                deckSwitchButton.onClick.AddListener(OpenDeckPanel);

            _categoryChangedHandler = new BackpackCategoryChangedHandler(this);
            EventBusHub.Instance.Subscribe(_categoryChangedHandler, this);

            _cardClickedHandler = new CardClickedInEditHandler(this);
            EventBusHub.Instance.Subscribe(_cardClickedHandler, this);

            CachePanelPositions();
            CacheButtonPositions();
            CacheTabViews();

            for (int i = 0; i < cardContent.transform.childCount; i++)
                Destroy(cardContent.transform.GetChild(i).gameObject);

            _cardPool = new CardPool(cardPrefab, cardContent.transform);

            if (deckContent != null)
                _deckCardPool = new CardPool(cardPrefab, deckContent);

            CacheEditPanelPosition();
        }

        /// <summary>
        /// 每次打开（池化生命周期）：旧 Start 的"每开一次"部分——数据重读/起始态/入场动画/状态复位。
        /// 起始态设置必须在此（激活同帧，docs/14 §37：防首帧闪现）。
        /// </summary>
        protected override void OnShow(object args)
        {
            PushMusicVolumeSafe();

            // 起始态（docs/14 §37）：面板/按钮离屏 + 毛玻璃 fill=0 + CanvasGroup 复位
            SetPanelsOffScreen();
            SetButtonsOffScreen();
            if (BlurBackdrop != null) BlurBackdrop.fillAmount = 0f;
            if (centerCanvasGroup != null) centerCanvasGroup.alpha = 1f;
            if (cardDetailCanvasGroup != null) cardDetailCanvasGroup.alpha = 1f;

            // 池化状态复位（旧场景制靠场景重载天然复位，池化须手动）：
            // 分类回角色页、标签线贴位、编辑面板隐藏、卡组管理面板与技能详情面板关闭
            SetCategory(BackpackTab.Character, isInit: true);
            SnapSelectLineTo(BackpackTab.Character);
            if (editDetailsPanel != null) editDetailsPanel.SetActive(false);
            if (deckSwitchPanel != null && deckSwitchPanel.gameObject.activeSelf) deckSwitchPanel.Close();
            if (skillDetailView != null) skillDetailView.ClosePanel();

            // 数据重读（存档可能已变）：RefreshCardList=释放旧卡+重读卡组缓存+按当前分类重生成
            RefreshCardList();
            UpdateDeckBar();

            StartCoroutine(PlaySlideInAnimation());
        }

        protected override void OnDestroy()
        {
            nextButtonLeft.onClick.RemoveListener(OnPreviousCategory);
            nextButtonRight.onClick.RemoveListener(OnNextCategory);
            editDeck.onClick.RemoveListener(OnToggleEditMode);
            closeButton.onClick.RemoveListener(OnClose);
            if (deckSwitchButton != null)
                deckSwitchButton.onClick.RemoveListener(OpenDeckPanel);

            _cardPool?.Clear();
            _deckCardPool?.Clear();

            // 基类收尾：注销可关闭 + PopAll 输入锁 + UnsubscribeOwner + 音乐 pop 兜底
            base.OnDestroy();
        }

        private void CacheEditPanelPosition()
        {
            if (editDetailsPanel != null)
            {
                editDetailsPanelRect = editDetailsPanel.GetComponent<RectTransform>();
                if (editDetailsPanelRect != null)
                {
                    editDetailsPanelTargetPos = editDetailsPanelRect.anchoredPosition;
                }
                editDetailsPanel.SetActive(false); // 初始隐藏
            }
        }
    }
}


