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
        private DeckChangedHandler _deckChangedHandler;
        private CardClickedInEditHandler _cardClickedHandler;

        private bool isEditMode = false;

        // ── IClosable 实现 ──
        public override void Close() => OnClose();

        private void Start()
        {
            PushMusicVolumeSafe();
            RegisterClosableSelf();

            currentDeckId = saveManager.CurrentSave.progress.currentDeck;

            nextButtonLeft.onClick.AddListener(OnPreviousCategory);
            nextButtonRight.onClick.AddListener(OnNextCategory);
            editDeck.onClick.AddListener(OnToggleEditMode);
            closeButton.onClick.AddListener(OnClose);

            _categoryChangedHandler = new BackpackCategoryChangedHandler(this);
            EventBusHub.Instance.Subscribe(_categoryChangedHandler, this);

            _deckChangedHandler = new DeckChangedHandler(this);
            EventBusHub.Instance.Subscribe(_deckChangedHandler, this);

            _cardClickedHandler = new CardClickedInEditHandler(this);
            EventBusHub.Instance.Subscribe(_cardClickedHandler, this);

            EventBusHub.Instance.SendImmediate(new OnBackpackDeckSyncEvent { DeckId = currentDeckId });

            CachePanelPositions();
            CacheButtonPositions();
            CacheTabViews();

            for (int i = 0; i < cardContent.transform.childCount; i++)
                Destroy(cardContent.transform.GetChild(i).gameObject);

            _cardPool = new CardPool(cardPrefab, cardContent.transform);

            if (deckContent != null)
                _deckCardPool = new CardPool(cardPrefab, deckContent);

            SetCategory(BackpackTab.Character, isInit: true);
            RefreshCurrentDeckCache();

            SetPanelsOffScreen();
            SetButtonsOffScreen();
            StartCoroutine(PlaySlideInAnimation());
            _spawnCoroutine = StartCoroutine(SpawnCardsWithDelay(BuildDisplayList()));

            CacheEditPanelPosition();
        }

        protected override void OnDestroy()
        {
            nextButtonLeft.onClick.RemoveListener(OnPreviousCategory);
            nextButtonRight.onClick.RemoveListener(OnNextCategory);
            editDeck.onClick.RemoveListener(OnToggleEditMode);
            closeButton.onClick.RemoveListener(OnClose);

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


