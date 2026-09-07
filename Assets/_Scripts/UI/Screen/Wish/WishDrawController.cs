using System;
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
    /// 祈愿动画控制器 — 表现层协调者
    /// 业务逻辑由 WishFlowController 预计算，本类只负责按序播放动画。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public partial class WishDrawController : MonoBehaviour
    {
        /// <summary>抽卡存盘后回调（通知外部刷新货币显示）</summary>
        public event Action OnWishComplete;

        /// <summary>单发射击业务结算完成回调（预计算完毕即触发，早于揭示动画）——
        /// 派蒙反应监听用（AI 代抽=PetWishAutoRunner；玩家手抽=PetWishPlayerObserver）</summary>
        public event Action<WishShotResult> OnShotPlanned;

        /// <summary>纠缠之线瞄准期开始回调（参数=1-based 即将射击的发数；在瞄准首帧而非 StartWish
        /// 同步触发——订阅方 PetWishAutoRunner/PetWishPlayerObserver 都在 StartWish 返回后才挂上）——
        /// 派蒙瞄准反应监听用（AI 代抽=多等 4 秒+动作+说话；玩家手抽=动作+说话，2026-09-07 拍板）</summary>
        public event Action<int> OnIntertwinedAimStart;

        /// <summary>本轮抽卡是否 AI 自动抽卡（PetWishPlayerObserver 据此区分"玩家自己抽"与
        /// "派蒙代抽"——只对玩家手抽做反应，AI 抽的反应由 PetWishAutoRunner 负责，防串场）</summary>
        public bool IsAutoDraw => _autoShoot;

        [Header("卡牌预制体")]
        [SerializeField] private GameObject cardPrefab;

        [Header("自动模式（AI 抽卡）")]
        [Tooltip("随机点击窗口（秒）：自动模式下每发在倒计时内随机时刻'手点'开枪（模拟玩家节奏，不卡点倒计时归零）。窗口须小于倒计时秒；超界时倒计时归零兜底")]
        [InspectorName("随机点击窗口秒")]
        [SerializeField] private Vector2 autoClickWindowSec = new Vector2(0.4f, 1.35f);

        [Tooltip("纠缠之线射击时自动模式的额外等待秒（2026-09-07 拍板：AI 在 12 秒瞄准仪式上多等 4 秒——给圆环风暴与派蒙瞄准反应留时间，随机点击窗口整体后移，仍钳在射击时限内）")]
        [InspectorName("纠缠仪式AI多等秒")]
        [SerializeField] private float intertwinedAutoExtraWaitSec = 4f;

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
        [SerializeField] private float fateLineThickness = 3f;
        [SerializeField] private Color fateLineColor = Color.white;

        [Header("倒计时")]
        [SerializeField] private float clickTimeLimit = 1.5f;

        [Tooltip("纠缠之线射击的时间限制——背包有纠缠之缘、下一发为纠缠之线时生效（2026-09-06 拍板 12 秒，给玩家更多瞄准时间）")]
        [InspectorName("纠缠之线射击时限秒")]
        [SerializeField] private float intertwinedShotTimeLimit = 12f;

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

        [Header("纠缠之线")]
        [SerializeField] private Color intertwinedLineColor = new Color(1f, 0.45f, 0.72f, 1f);

        [Tooltip("纠缠之线射击线加厚倍率（主线粗细 = 命运之线厚度 × 此值）")]
        [InspectorName("纠缠之线加厚倍率")]
        [SerializeField] private float intertwinedLineThicknessMultiplier = 2.5f;

        [Tooltip("纠缠之线飞行时长秒（相遇/普通为 0.2s；纠缠更慢更重）")]
        [InspectorName("纠缠之线飞行时长秒")]
        [SerializeField] private float intertwinedLineDuration = 0.3f;

        [Tooltip("纠缠开火全屏粉闪的峰值 alpha（0 = 关闭粉闪）")]
        [InspectorName("纠缠开火粉闪峰值")]
        [SerializeField] private float intertwinedFlashAlpha = 0.3f;

        [Header("纠缠圆环风暴（瞄准仪式）")]
        [Tooltip("圆环生成起始间隔秒（频率从起始间隔线性爬到峰值间隔）")]
        [InspectorName("环起始生成间隔秒")]
        [SerializeField] private float ringSpawnIntervalStart = 0.55f;

        [Tooltip("圆环生成峰值间隔秒（爬升结束后保持此频率直到开火）")]
        [InspectorName("环峰值生成间隔秒")]
        [SerializeField] private float ringSpawnIntervalPeak = 0.12f;

        [Tooltip("生成频率从起始爬到峰值所需秒数（拍板：3 秒达到最高）")]
        [InspectorName("频率爬升秒数")]
        [SerializeField] private float ringRampSeconds = 3f;

        [Tooltip("圆环出生尺寸（px）")]
        [InspectorName("环起始尺寸")]
        [SerializeField] private float ringStartSize = 140f;

        [Tooltip("圆环扩散到最大尺寸（px，超屏幕制造冲击感）")]
        [InspectorName("环最终尺寸")]
        [SerializeField] private float ringEndSize = 2200f;

        [Tooltip("单个圆环从出生到消失的秒数")]
        [InspectorName("环生命周期秒")]
        [SerializeField] private float ringLifetime = 1.5f;

        [Tooltip("圆环最高不透明度")]
        [InspectorName("环峰值透明度")]
        [SerializeField] private float ringPeakAlpha = 0.6f;

        [Tooltip("环自旋基础速度（度/秒）；每个环随机顺/逆时针、速度 ±30%")]
        [InspectorName("环自旋速度")]
        [SerializeField] private float ringSpinSpeed = 240f;

        [Tooltip("圆环染色（乘在素材色上；白=显示素材原色）")]
        [InspectorName("环染色")]
        [SerializeField] private Color ringTintColor = Color.white;

        [Tooltip("纠缠瞄准期全屏暗幕 alpha：压暗背景凸显圆环（背景图偏亮，纯加色环会被洗白）；0=关闭暗幕")]
        [InspectorName("瞄准暗幕强度")]
        [SerializeField] private float intertwinedVeilAlpha = 0.5f;

        /// <summary>抽卡流程是否进行中（供 StartDraw 防重入检查）</summary>
        public bool IsWishInProgress => _isWishActive;

        /// <summary>纠缠之线颜色（氛围粒子脉冲等外部表现取色用）</summary>
        public Color IntertwinedLineColor => intertwinedLineColor;

        #region runtimeState

        protected WishManager _wishManager;
        protected WishPoolConfig _pool;
        protected WishFlowController _flow;
        private bool _isWishActive;
        private bool _autoShoot;       // AI 自动抽卡模式：倒计时内随机时刻自动开枪（随 StartWish 参数重置）
        private float _nextAutoClickElapsed; // 本发的随机"点击"时刻（倒计时已流逝秒数；每发重掷）
        private bool _pendingIntertwinedAimAnnounce; // 纠缠瞄准事件待发标记（InputCoroutine 首帧/冷却次帧发出）

        protected List<WishTrackCard> _activeCards = new();

        protected float _currentTimer;
        protected bool _isInCooldown;
        protected float _cooldownTimer;
        protected float _totalCooldownTime;

        protected List<RectTransform> _resultCards = new();
        protected List<GameObject> _cardPool = new();
        protected readonly List<Coroutine> _holdThenFlyCoroutines = new();
        protected int _holdThenFlyRemaining;

        // 星级→候选列表预缓存（由 _flow 提供）
        protected Dictionary<int, List<UnitName>> _unitsByStar;
        protected Dictionary<int, List<ItemName>> _itemsByStar;

        // FateLine Shader 静态缓存
        protected static Shader _fateLineShader;

        // 屏幕边缘泛光
        protected ScreenEdgeGlow _edgeGlow;

        // 倒计时条原色缓存（纠缠瞄准期染粉，恢复用）
        protected Color _originalCountdownColor;
        protected bool _hasOriginalCountdownColor;

        [Autowired] private InputManager _inputManager;

        // 进度条流彩协程
        protected Coroutine _progressBarGlowCoroutine;

        // 当前正在播放的 step 协程
        protected Coroutine _stepsCoroutine;

        #endregion

        #region stardustRainData

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
            Wargame.Instance?.Context?.Inject(this); // 容器已就绪，Awake 注入
            if (drawRoot != null) drawRoot.SetActive(false);
            CreateEdgeGlow();
            CreateIntertwinedFlash();
            CreateIntertwinedCeremonyOverlays();
            CreateStar5VideoOverlay();
        }

        private void OnDestroy()
        {
            OnDestroyStar5Video();
            if (_fateLineMaterial != null)
            {
                Destroy(_fateLineMaterial);
                _fateLineMaterial = null;
            }
        }

        #region publicEntry

        /// <summary>
        /// 开始祈愿流程（原石已由入口层 WishScreen.StartDraw/TryStartAutoDraw 扣除——表现层不做货币写操作）
        /// </summary>
        /// <param name="autoShoot">自动射击（AI 抽卡）：倒计时余 自动射击提前秒 时自动开枪，不等玩家点击</param>
        public void StartWish(WishManager manager, WishPoolConfig pool, int count, bool autoShoot = false)
        {
            _autoShoot = autoShoot;
            _wishManager = manager;
            _pool = pool;
            _flow = new WishFlowController(manager, pool);
            _flow.StartFlow(count);
            _isWishActive = true;

            // 抽卡全程锁定输入 — CloseUI 等动作派发暂停
            InputLocks.Push(this, InputLockReason.WishInProgress);

            // 从 flow 获取星级缓存
            _unitsByStar = new Dictionary<int, List<UnitName>>();
            _itemsByStar = new Dictionary<int, List<ItemName>>();
            for (int star = 1; star <= 5; star++)
            {
                _unitsByStar[star] = _flow.GetCachedUnitsByStar(star);
                _itemsByStar[star] = _flow.GetCachedItemsByStar(star);
            }

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

            _currentTimer = GetCurrentShotTimeLimit();
            _isInCooldown = false;
            _cooldownTimer = 0f;
            _pendingIntertwinedAimAnnounce = false; // 上一轮中止可能残留待发标记
            RollAutoClickDelay(); // 自动模式第一发的随机"点击"时刻
            UpdateCountdownTint();
            if (_flow.UpcomingLineType == WishLineType.Intertwined)
            {
                StartIntertwinedCeremony();
                _pendingIntertwinedAimAnnounce = true; // 由 InputCoroutine 首帧发事件（订阅方此刻未挂上）
            }

            UpdateStarglitterProgressBar();

            PreloadStar5Video();
        }

        #endregion

        #region 输入与射击

        private IEnumerator InputCoroutine()
        {
            yield return null;

            while (_flow.CurrentState != WishFlowController.State.Idle &&
                   _flow.CurrentState != WishFlowController.State.Finished)
            {
                if (_isInCooldown)
                {
                    // 揭示动画中冻结倒计时
                    if (_flow.CurrentState == WishFlowController.State.Drawing)
                    {
                        _cooldownTimer -= Time.deltaTime;
                        UpdateCountdownBar(1f - _cooldownTimer / _totalCooldownTime);

                        if (_cooldownTimer <= 0f)
                        {
                            _isInCooldown = false;
                            _currentTimer = GetCurrentShotTimeLimit(); // 纠缠之线 12 秒，其余常规时限
                            RollAutoClickDelay(); // 下一发的随机"点击"时刻
                            UpdateCountdownTint();
                            if (_flow.UpcomingLineType == WishLineType.Intertwined)
                            {
                                StartIntertwinedCeremony();
                                _pendingIntertwinedAimAnnounce = true; // 次发瞄准开始：下一帧发事件
                            }
                        }
                    }
                }
                else
                {
                    // 纠缠瞄准事件：延到本协程首帧/冷却结束次帧发——订阅方（AutoRunner/PlayerObserver）
                    // 都在 StartWish 返回后才挂订阅，StartWish 里同步发会漏掉第一发
                    if (_pendingIntertwinedAimAnnounce)
                    {
                        _pendingIntertwinedAimAnnounce = false;
                        OnIntertwinedAimStart?.Invoke(_flow.ShotsCompleted + 1);
                    }

                    _currentTimer -= Time.deltaTime;
                    UpdateCountdownBar(_currentTimer / GetCurrentShotTimeLimit());

                    // 自动射击：随机时刻"手点"（AI 抽卡模拟玩家节奏）；玩家点击仍可提前；倒计时归零兜底
                    if (_currentTimer <= 0f || IsConfirmPressed()
                        || (_autoShoot && (GetCurrentShotTimeLimit() - _currentTimer) >= _nextAutoClickElapsed))
                        Shoot();
                }

                yield return null;
            }

            if (_flow.CurrentState == WishFlowController.State.Finished)
            {
                // 等待最后的 step 协程完成
                while (_stepsCoroutine != null)
                    yield return null;

                _wishManager.SaveGame();
                OnWishComplete?.Invoke();
                StartCoroutine(ShowFinalDisplay());
            }
        }

        /// <summary>掷本发的随机"点击"时刻（自动模式）：模拟玩家在倒计时内随机时刻手点开枪，
        /// 每发独立重掷——节奏不机械。窗口钳在 (0, 当前射击时限) 内防越界。
        /// 纠缠之线（12 秒仪式）：窗口整体后移"纠缠仪式AI多等秒"（2026-09-07 拍板 +4 秒）——
        /// AI 看圆环风暴酝酿片刻再开火，也给派蒙瞄准反应留出播放时间。</summary>
        void RollAutoClickDelay()
        {
            float limit = GetCurrentShotTimeLimit();
            float extra = _flow != null && _flow.UpcomingLineType == WishLineType.Intertwined
                ? intertwinedAutoExtraWaitSec : 0f;
            float min = Mathf.Max(0.05f, Mathf.Min(autoClickWindowSec.x, autoClickWindowSec.y)) + extra;
            float max = Mathf.Min(Mathf.Max(autoClickWindowSec.x, autoClickWindowSec.y) + extra, limit - 0.05f);
            _nextAutoClickElapsed = max > min ? UnityEngine.Random.Range(min, max) : min;
        }

        /// <summary>本发射击时限：下一发为纠缠之线（背包有纠缠之缘）时 12 秒，否则常规 1.5 秒——
        /// 倒计时重置/进度条分母/AI 点击窗口共用。Drawing 状态下背包不变，时限在整发内恒定。</summary>
        private float GetCurrentShotTimeLimit() =>
            _flow != null && _flow.UpcomingLineType == WishLineType.Intertwined
                ? intertwinedShotTimeLimit
                : clickTimeLimit;

        /// <summary>
        /// 确认输入是否按下 — 鼠标左键 + Confirm 动作绑定的按键（支持重绑定）
        /// </summary>
        private bool IsConfirmPressed()
        {
            if (Input.GetMouseButtonDown(0)) return true;

            var im = _inputManager;
            if (im == null) return false;

            for (int slot = 0; slot < 2; slot++)
            {
                var key = im.GetKey(KeyAction.Confirm, slot);
                if (key != KeyCode.None && Input.GetKeyDown(key))
                    return true;
            }
            return false;
        }

        private void Shoot()
        {
            if (_isInCooldown || _flow.CurrentState != WishFlowController.State.Drawing) return;

            // 纠缠瞄准仪式收尾：暗幕淡出、圆环停发（存活环自然播完淡出）
            StopIntertwinedCeremony();

            if (shootSFX != null)
                AudioManager.Instance?.PlaySFX(shootSFX, sfxVolume);

            // 纠缠之线开火：全屏粉闪（比相遇更强烈的开场冲击）
            if (_flow.UpcomingLineType == WishLineType.Intertwined)
                PlayIntertwinedFlash();

            StartCoroutine(FateLineCoroutine(_flow.UpcomingLineType));

            _isInCooldown = true;

            // 找到中心卡
            WishTrackCard centerCard = FindCardNearestCenter();
            if (centerCard == null)
            {
                SpawnTrackCard();
                centerCard = FindCardNearestCenter();
            }

            if (centerCard == null)
            {
                _cooldownTimer = StarVisualConfig.GetShotCooldown(1);
                _totalCooldownTime = _cooldownTimer;
                return;
            }

            _activeCards.Remove(centerCard);
            centerCard.SetPaused(true);

            var card = centerCard.GetComponent<Card>();
            var rect = centerCard.GetComponent<RectTransform>();

            centerCard.transform.SetParent(resultContainer, true);
            rect.localScale = Vector3.one * (holdCardSize / 160f);
            _resultCards.Add(rect);

            // 预计算射击结果（全部业务逻辑在此完成）
            int shotIndex = _flow.ShotsCompleted;
            var shotResult = _flow.PlanShot(card, shotIndex);

            // 单发结果事件（AI 自动抽卡反应；null=守卫拦截的异常发）
            if (shotResult != null)
                OnShotPlanned?.Invoke(shotResult);

            // 按序播放动画
            _stepsCoroutine = StartCoroutine(PlayStepsCoroutine(shotResult, card, rect, shotIndex));

            // 设置冷却
            int starLevel = shotResult?.finalStarLevel ?? 1;
            if (shotResult != null && shotResult.isEncounter)
                _cooldownTimer = StarVisualConfig.BaseShotCooldown;
            else
                _cooldownTimer = StarVisualConfig.GetShotCooldown(starLevel);
            _totalCooldownTime = _cooldownTimer;

            UpdateStarglitterProgressBar();
        }

        /// <summary>
        /// 统一步骤播放器 — 按预计算的 steps 有序播放动画
        /// </summary>
        private IEnumerator PlayStepsCoroutine(WishShotResult result, Card card, RectTransform rect, int shotIndex)
        {
            if (result == null)
            {
                _stepsCoroutine = null;
                yield break;
            }

            Coroutine shakeGlow = null;
            bool hasGlow = card.glowImage != null;

            foreach (var step in result.steps)
            {
                switch (step.type)
                {
                    case WishRevealStep.StepType.Star5Video:
                        // 立即隐藏卡片，防止视频播放前泄露抽卡结果
                        var cardCG = card.GetComponent<CanvasGroup>();
                        if (cardCG == null)
                            cardCG = card.gameObject.AddComponent<CanvasGroup>();
                        cardCG.alpha = 0f;

                        yield return PlayStar5TransitionCoroutine();

                        // 视频结束后恢复卡片可见
                        cardCG.alpha = 1f;
                        break;

                    case WishRevealStep.StepType.CardEffects:
                        PlayCardHitEffects(step.starLevel, card);
                        break;

                    case WishRevealStep.StepType.StarglitterRain:
                        if (step.starglitterAmount > 0)
                            RequestStarglitterRain(step.starglitterAmount);
                        UpdateStarglitterProgressBar();
                        break;

                    case WishRevealStep.StepType.EncounterShakeStart:
                        if (hasGlow) card.glowImage.gameObject.SetActive(true);
                        shakeGlow = StartCoroutine(CardShakeGlowCoroutine(
                            rect, card.glowImage, step.baseStarLevel, step.upgradeCount));
                        break;

                    case WishRevealStep.StepType.EncounterUpgrade:
                        if (step.shakeDelay > 0f)
                            yield return Wait.Seconds(step.shakeDelay);

                        // 换卡显示
                        if (step.cardData != null)
                            card.Init(step.cardData, null);

                        // 特效（相遇/纠缠同款星级色表现——2026-09-07 拍板：纠缠差异化一律走叠加特效，不改此表现）
                        PlayCardHitEffects(step.starLevel, card);

                        // 星辉雨
                        if (step.starglitterAmount > 0)
                        {
                            RequestStarglitterRain(step.starglitterAmount);
                            UpdateStarglitterProgressBar();
                        }
                        break;

                    case WishRevealStep.StepType.EncounterShakeEnd:
                        if (shakeGlow != null) StopCoroutine(shakeGlow);
                        if (hasGlow && card.glowImage != null)
                        {
                            var endGlowColor = StarVisualConfig.EncounterGlowColor;
                            card.glowImage.color = new Color(endGlowColor.r, endGlowColor.g, endGlowColor.b, 0f);
                            card.glowImage.gameObject.SetActive(false);
                        }
                        // 恢复位置
                        rect.localEulerAngles = Vector3.zero;
                        break;

                    case WishRevealStep.StepType.HoldThenFly:
                        StartHoldThenFly(rect, shotIndex, step.starLevel);
                        // 覆盖冷却时长
                        _cooldownTimer = StarVisualConfig.GetHoldTime(step.starLevel) + 0.5f;
                        _totalCooldownTime = _cooldownTimer;
                        UpdateStarglitterProgressBar();
                        break;
                }
            }

            _flow.OnRevealComplete();
            _stepsCoroutine = null;
        }

        #endregion

        #region Helper 方法

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
            float startY = -(_flow.TotalShots - 1) * (resultCardSize + resultCardSpacing) * 0.5f;
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

        #endregion

        #region cleanup

        private void OnDisable()
        {
            _isWishActive = false;
            _autoShoot = false;
            InputLocks.Pop(this, InputLockReason.WishInProgress);
            _flow?.Reset();
            StopHoldThenFlyCoroutines();
            ClearTrackCards();
            ClearCardPool();
            ClearResultCards();
            ClearStarglitterPool();
            CleanupStar5Video();
            CleanupFateLines();
            CleanupIntertwinedFlash();
            CleanupIntertwinedCeremony();
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
            _isRainActive = false;
            _rainTotal = 0;
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
