using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Battle;
using GIC.UI;
using GIC.Tool;

namespace GIC.UI
{
    /// <summary>
    /// 祈愿动画控制器 — 控制卡道出卡、命运之线射击、倒计时、结果展示
    /// UI 层级在场景中提前建好，通过 SerializeField 引用
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public partial class WishDrawController : MonoBehaviour
    {
        [Header("卡牌预制体")]
        [SerializeField] private GameObject cardPrefab;

        [Header("场景 UI 引用")]
        [SerializeField] private GameObject drawRoot;
        [SerializeField] private RectTransform cardTrack;
        [SerializeField] private RectTransform cardSpawnPoint;
        [SerializeField] private RectTransform cardEndPoint;
        [SerializeField] private RectTransform fateLineContainer;
        [SerializeField] private RectTransform countdownBar;
        [SerializeField] private RectTransform countdownBarFill;
        [SerializeField] private RectTransform resultContainer;
        [SerializeField] private RectTransform finalDisplayContainer;
        [SerializeField] private CanvasGroup finalDisplayCanvasGroup;

        [Header("卡道")]
        [SerializeField] private float cardMoveDuration = 0.75f;
        [SerializeField] private float cardSpawnInterval = 0.09f;
        [SerializeField] private float cardWidth = 100f;
        [SerializeField] private float cardHeight = 150f;

        [Header("命运之线")]
        [SerializeField] private float fateLineSpeed = 8000f;
        [SerializeField] private float fateLineThickness = 3f;
        [SerializeField] private Color fateLineColor = Color.white;

        [Header("倒计时")]
        [SerializeField] private float clickTimeLimit = 1.5f;

        [Header("结果展示")]
        [SerializeField] private float resultCardSize = 80f;
        [SerializeField] private float resultCardSpacing = 10f;
        [SerializeField] private float holdCardSize = 280f;

        [Header("最终展示")]
        [SerializeField] private float finalCardSize = 160f;

        [Header("音效")]
        [SerializeField] private AudioClip shootSFX;
        [SerializeField] private AudioClip cardHitSFX;
        [SerializeField] private float sfxVolume = 0.8f;

        [Header("星辉雨")]
        [SerializeField] private RectTransform starglitterRainContainer;
        [SerializeField] private Sprite starglitterSprite;

        [Header("星辉进度条")]
        [SerializeField] private RectTransform starglitterProgressBar;
        [SerializeField] private RectTransform starglitterProgressFill;

        [Header("相遇之线")]
        [SerializeField] private Color encounterLineColor = new Color(1f, 0.85f, 0.3f, 1f);

        /// <summary>抽卡流程是否进行中（防重入）</summary>
        public bool IsWishInProgress => _isActive;

        #region 运行时状态

        protected WishManager _wishManager;
        protected WishPoolConfig _pool;
        protected List<WishResult> _results;
        protected int _totalShots;
        protected int _shotsCompleted;

        protected List<WishTrackCard> _activeCards = new();

        protected float _currentTimer;
        protected bool _isInCooldown;
        protected float _cooldownTimer;
        protected float _totalCooldownTime;
        protected bool _isActive;

        protected List<RectTransform> _resultCards = new();
        protected List<GameObject> _cardPool = new();
        protected readonly List<Coroutine> _holdThenFlyCoroutines = new();
        protected int _holdThenFlyRemaining;

        protected bool _isEncounterShot;
        protected bool _isEncounterAnimating;

        // 星级→候选列表预缓存
        protected Dictionary<int, List<UnitName>> _unitsByStar;
        protected Dictionary<int, List<ItemName>> _itemsByStar;

        // FateLine Shader 静态缓存
        protected static Shader _fateLineShader;

        // 屏幕边缘泛光
        protected ScreenEdgeGlow _edgeGlow;

        // 进度条流彩协程
        protected Coroutine _progressBarGlowCoroutine;

        #endregion

        #region 星辉雨数据

        protected readonly List<GameObject> _starglitterPool = new();
        protected struct StarglitterDropData
        {
            public RectTransform rt;
            public Image img;
            public Vector2 startPos;
            public float fallHeight;
            public float startRot;
            public float endRot;
            public float duration;
            public float elapsed;
        }
        protected readonly List<StarglitterDropData> _activeDrops = new();

        #endregion

        private void Awake()
        {
            if (drawRoot != null) drawRoot.SetActive(false);
            CreateEdgeGlow();
        }

        #region 公共入口

        /// <summary>
        /// 开始祈愿流程
        /// </summary>
        public void StartWish(WishManager manager, WishPoolConfig pool, int count)
        {
            _wishManager = manager;
            _pool = pool;
            _totalShots = count;
            _shotsCompleted = 0;
            _isActive = true;

            if (!_wishManager.ConsumePrimogem(count))
            {
                Debug.LogWarning("[WishDrawController] 原石不足");
                _isActive = false;
                return;
            }

            _results = new List<WishResult>();
            BuildStarLevelCache();

            ClearResultCards();
            ClearTrackCards();

            if (drawRoot != null) drawRoot.SetActive(true);
            cardTrack.gameObject.SetActive(true);
            countdownBar.gameObject.SetActive(true);
            resultContainer.gameObject.SetActive(true);
            finalDisplayContainer.gameObject.SetActive(false);

            Canvas.ForceUpdateCanvases();
            cardSpawnPoint.anchoredPosition = new Vector2(0, -cardTrack.rect.height * 0.5f - cardHeight);
            cardEndPoint.anchoredPosition = new Vector2(0, cardTrack.rect.height * 0.5f + cardHeight);

            int initialCount = Mathf.CeilToInt(cardMoveDuration / cardSpawnInterval);
            for (int i = 0; i < initialCount; i++)
                SpawnTrackCard(initialProgress: (float)i / initialCount);

            StartCoroutine(SpawnCardsCoroutine());
            StartCoroutine(InputCoroutine());

            _currentTimer = clickTimeLimit;
            _isInCooldown = false;
            _cooldownTimer = 0f;

            UpdateStarglitterProgressBar();
        }

        #endregion

        #region 输入与射击

        private IEnumerator InputCoroutine()
        {
            yield return null;

            while (_isActive && _shotsCompleted < _totalShots)
            {
                if (_isInCooldown)
                {
                    if (!_isEncounterAnimating)
                    {
                        _cooldownTimer -= Time.deltaTime;
                        UpdateCountdownBar(1f - _cooldownTimer / _totalCooldownTime);

                        if (_cooldownTimer <= 0f)
                        {
                            _isInCooldown = false;
                            _currentTimer = clickTimeLimit;
                        }
                    }
                }
                else
                {
                    _currentTimer -= Time.deltaTime;
                    UpdateCountdownBar(_currentTimer / clickTimeLimit);

                    if (_currentTimer <= 0f || Input.GetMouseButtonDown(0))
                        Shoot();
                }

                yield return null;
            }

            if (_shotsCompleted >= _totalShots)
            {
                while (_isEncounterAnimating)
                    yield return null;

                _wishManager.SaveGame();
                StartCoroutine(ShowFinalDisplay());
            }
        }

        private void Shoot()
        {
            if (_isInCooldown || _shotsCompleted >= _totalShots) return;

            _shotsCompleted++;

            _isEncounterShot = _wishManager.IsEncounterReady();
            if (_isEncounterShot) _wishManager.ConsumeEncounter();

            if (shootSFX != null)
                AudioManager.Instance?.PlaySFX(shootSFX, sfxVolume);

            StartCoroutine(FateLineCoroutine(_isEncounterShot));

            _isInCooldown = true;

            int starLevel = RevealAndPopResultCard(_shotsCompleted - 1);

            if (_isEncounterShot)
                _cooldownTimer = StarVisualConfig.BaseShotCooldown;
            else
                _cooldownTimer = StarVisualConfig.GetShotCooldown(starLevel);
            _totalCooldownTime = _cooldownTimer;

            UpdateStarglitterProgressBar();
        }

        private int RevealAndPopResultCard(int index)
        {
            WishTrackCard centerCard = FindCardNearestCenter();
            if (centerCard == null)
            {
                SpawnTrackCard();
                centerCard = FindCardNearestCenter();
                if (centerCard == null) return 1;
            }

            _activeCards.Remove(centerCard);
            centerCard.SetPaused(true);

            var card = centerCard.GetComponent<Card>();
            var rect = centerCard.GetComponent<RectTransform>();

            centerCard.transform.SetParent(resultContainer, true);
            rect.localScale = Vector3.one * (holdCardSize / 160f);
            _resultCards.Add(rect);

            if (_isEncounterShot && card != null && card.saveCardData != null)
            {
                StartCoroutine(EncounterRevealCoroutine(card, rect, index));
                return 5;
            }

            // ── 普通射击 ──
            int starLevel = 1;
            if (card != null && card.saveCardData != null)
            {
                starLevel = card.saveCardData.StarLevel;
                var result = BuildResultFromCard(card);
                AddResultAndStarglitter(result);
                card.PlayLightBand();
            }

            PlayCardHitEffects(starLevel);
            StartHoldThenFly(rect, index, starLevel);
            UpdateStarglitterProgressBar();
            return starLevel;
        }

        #endregion

        #region Helper 方法（消除重复代码）

        /// <summary>
        /// 从 card.saveCardData 构造 WishResult（含 sprite 获取）
        /// </summary>
        protected WishResult BuildResultFromCard(Card card)
        {
            bool isUnit = card.saveCardData.cardType == CardType.Unit;
            return new WishResult(
                card.saveCardData.id,
                card.saveCardData.StarLevel,
                card.saveCardData.cardType,
                isUnit
                    ? _wishManager.GetUnitConfig().GetUnitData(card.saveCardData.id.AsUnitName())?.GetCard(0)
                    : _wishManager.GetItemConfig().GetItemData(card.saveCardData.id.AsItemName())?.GetIcon(0),
                false
            );
        }

        /// <summary>
        /// 播放卡片命中特效：光带 + 光柱 + 边缘泛光
        /// </summary>
        protected void PlayCardHitEffects(int starLevel, Card card = null)
        {
            if (card != null) card.PlayLightBand();
            StartCoroutine(GlowBurstCoroutine(starLevel));
            _edgeGlow?.Play(StarVisualConfig.GetStarColor(starLevel), starLevel);
        }

        /// <summary>
        /// 计算左侧竖向排列的 Y 坐标
        /// </summary>
        protected float ComputeResultY(int index)
        {
            float startY = -(_totalShots - 1) * (resultCardSize + resultCardSpacing) * 0.5f;
            return startY + index * (resultCardSize + resultCardSpacing);
        }

        /// <summary>
        /// 启动停留+飞行协程，自动计算位置和停留时间
        /// </summary>
        protected void StartHoldThenFly(RectTransform rect, int index, int starLevel)
        {
            Vector2 targetPos = new Vector2(0f, ComputeResultY(index));
            float holdTime = StarVisualConfig.GetHoldTime(starLevel);
            _holdThenFlyRemaining++;
            _holdThenFlyCoroutines.Add(StartCoroutine(HoldThenFlyAndCount(rect, targetPos, holdTime)));
        }

        /// <summary>
        /// 入账 + 星辉雨
        /// </summary>
        protected void AddResultAndStarglitter(WishResult result)
        {
            _wishManager.AddResultToInventory(result, out int starglitter);
            result.starglitterAmount = starglitter;
            _results.Add(result);
            if (starglitter > 0)
                StartCoroutine(StarglitterRainCoroutine(starglitter));
        }

        #endregion

        #region 清理

        private void OnDisable()
        {
            _isActive = false;
            StopHoldThenFlyCoroutines();
            ClearTrackCards();
            ClearCardPool();
            ClearResultCards();
            ClearStarglitterPool();
        }

        protected void StopHoldThenFlyCoroutines()
        {
            foreach (var c in _holdThenFlyCoroutines)
            {
                if (c != null) StopCoroutine(c);
            }
            _holdThenFlyCoroutines.Clear();
            _holdThenFlyRemaining = 0;
        }

        protected void ClearTrackCards()
        {
            _activeCards.Clear();
        }

        protected void ClearCardPool()
        {
            foreach (var card in _cardPool)
            {
                if (card != null) Destroy(card);
            }
            _cardPool.Clear();
        }

        protected void ClearResultCards()
        {
            foreach (var rect in _resultCards)
            {
                if (rect != null) Destroy(rect.gameObject);
            }
            _resultCards.Clear();
        }

        protected void ClearStarglitterPool()
        {
            foreach (var go in _starglitterPool)
            {
                if (go != null) Destroy(go);
            }
            _starglitterPool.Clear();
            _activeDrops.Clear();
        }

        #endregion
    }
}
