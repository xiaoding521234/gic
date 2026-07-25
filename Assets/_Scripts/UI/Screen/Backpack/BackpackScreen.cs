// ==================== BackpackScreen.cs ====================
using System;
using System.Collections;
using System.Collections.Generic;
using LocalEvents;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class BackpackScreen : MonoBehaviour
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

    private BackpackTab currentTab = BackpackTab.Character;
    private int currentDeckId;
    private HashSet<SaveCardData> currentDeckCards = new();
    private List<Card> spawnedCards = new();
    private CardPool _cardPool;

    private CardManager cardManager;
    private SaveManager saveManager;
    private UnitConfig unitConfig;
    private ItemConfig itemConfig;
    private BackpackCategoryChangedHandler _categoryChangedHandler;
    private DeckChangedHandler _deckChangedHandler;
    private CardClickedInEditHandler _cardClickedHandler;

    private bool isEditMode = false;

    private void Start()
    {
        AudioManager.Instance.PushMusicVolume();

        cardManager = Wargame.Instance.CardManager;
        saveManager = Wargame.Instance.SaveManager;
        unitConfig = cardManager.unitConfig;
        itemConfig = cardManager.itemConfig;

        currentDeckId = saveManager.CurrentSave.currentDeck;

        nextButtonLeft.onClick.AddListener(OnPreviousCategory);
        nextButtonRight.onClick.AddListener(OnNextCategory);
        editDeck.onClick.AddListener(OnToggleEditMode);
        closeButton.onClick.AddListener(OnClose);

        _categoryChangedHandler = new BackpackCategoryChangedHandler(this);
        EventBusHub.Instance.Subscribe(_categoryChangedHandler);

        _deckChangedHandler = new DeckChangedHandler(this);
        EventBusHub.Instance.Subscribe(_deckChangedHandler);

        _cardClickedHandler = new CardClickedInEditHandler(this);
        EventBusHub.Instance.Subscribe(_cardClickedHandler);

        EventBusHub.Instance.Publish(new OnBackpackDeckSyncEvent { DeckId = currentDeckId });

        CachePanelPositions();
        CacheButtonPositions();

        foreach (Transform child in cardContent.transform)
            Destroy(child.gameObject);

        _cardPool = new CardPool(cardPrefab, cardContent.transform);

        SetCategory(BackpackTab.Character, isInit: true);
        RefreshCurrentDeckCache();

        SetPanelsOffScreen();
        SetButtonsOffScreen();
        StartCoroutine(PlaySlideInAnimation());
        StartCoroutine(SpawnCardsWithDelay(BuildDisplayList()));

        CacheEditPanelPosition();
    }

    private void OnDestroy()
    {
        nextButtonLeft.onClick.RemoveListener(OnPreviousCategory);
        nextButtonRight.onClick.RemoveListener(OnNextCategory);
        editDeck.onClick.RemoveListener(OnToggleEditMode);
        closeButton.onClick.RemoveListener(OnClose);

        if (EventBusHub.Instance != null)
        {
            if (_categoryChangedHandler != null) EventBusHub.Instance.Unsubscribe(_categoryChangedHandler);
            if (_deckChangedHandler != null) EventBusHub.Instance.Unsubscribe(_deckChangedHandler);
            if (_cardClickedHandler != null) EventBusHub.Instance.Unsubscribe(_cardClickedHandler);
        }

        _cardPool?.Clear();
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