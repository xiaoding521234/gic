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
    public class WishDrawController : MonoBehaviour
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
        [SerializeField] private float baseShotCooldown = 0.3f;
        [SerializeField] private float cooldownPerStar = 0.15f;

        [Header("结果展示")]
        [SerializeField] private float resultCardSize = 80f;
        [SerializeField] private float resultCardSpacing = 10f;

        [Header("最终展示")]
        [SerializeField] private float finalCardSize = 160f;

        [Header("音效")]
        [SerializeField] private AudioClip shootSFX;
        [SerializeField] private AudioClip cardHitSFX;
        [SerializeField] private float sfxVolume = 0.8f;

        // 运行时状态
        private WishManager _wishManager;
        private WishPoolConfig _pool;
        private List<WishResult> _results;
        private int _totalShots;
        private int _shotsCompleted;

        private List<WishTrackCard> _activeCards = new();

        private float _currentTimer;
        private bool _isInCooldown;
        private float _cooldownTimer;
        private bool _isActive;

        private List<RectTransform> _resultCards = new();
        private List<GameObject> _cardPool = new();
        private readonly List<Coroutine> _holdThenFlyCoroutines = new();

        // 星级→候选列表预缓存（每次抽卡流程开始时构建，避免逐卡遍历全表）
        private Dictionary<int, List<UnitName>> _unitsByStar;
        private Dictionary<int, List<ItemName>> _itemsByStar;

        // FateLine Shader 静态缓存（避免每次射击 Shader.Find）
        private static Shader _fateLineShader;

        // 屏幕边缘泛光
        private ScreenEdgeGlow _edgeGlow;

        private void Awake()
        {
            // 未抽卡时隐藏所有祈愿动画 UI
            if (drawRoot != null) drawRoot.SetActive(false);
            CreateEdgeGlow();
        }

        /// <summary>
        /// 创建屏幕边缘泛光 Image（drawRoot 的子物体，随 drawRoot 显隐）
        /// </summary>
        private void CreateEdgeGlow()
        {
            if (drawRoot == null) return;

            var glowObj = new GameObject("ScreenEdgeGlow", typeof(RectTransform), typeof(Image));
            glowObj.transform.SetParent(drawRoot.transform, false);
            glowObj.layer = drawRoot.layer;

            var rect = glowObj.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _edgeGlow = glowObj.AddComponent<ScreenEdgeGlow>();
        }

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

            // 不再预抽——实际结果由玩家射中的卡决定
            _results = new List<WishResult>();

            // 预构建星级→候选列表缓存，避免每张卡道卡都遍历全表
            BuildStarLevelCache();

            ClearResultCards();
            ClearTrackCards();

            // 显示祈愿 UI
            if (drawRoot != null) drawRoot.SetActive(true);
            cardTrack.gameObject.SetActive(true);
            countdownBar.gameObject.SetActive(true);
            resultContainer.gameObject.SetActive(true);
            finalDisplayContainer.gameObject.SetActive(false);

            // 布局计算完成后再设置出卡点/终点位置
            Canvas.ForceUpdateCanvases();
            cardSpawnPoint.anchoredPosition = new Vector2(0, -cardTrack.rect.height * 0.5f - cardHeight);
            cardEndPoint.anchoredPosition = new Vector2(0, cardTrack.rect.height * 0.5f + cardHeight);

            // 初始填满卡道（像老虎机一样一开始就有卡片在移动）
            int initialCount = Mathf.CeilToInt(cardMoveDuration / cardSpawnInterval);
            for (int i = 0; i < initialCount; i++)
            {
                SpawnTrackCard(initialProgress: (float)i / initialCount);
            }

            StartCoroutine(SpawnCardsCoroutine());
            StartCoroutine(InputCoroutine());

            _currentTimer = clickTimeLimit;
            _isInCooldown = false;
            _cooldownTimer = 0f;
        }

        private IEnumerator SpawnCardsCoroutine()
        {
            while (_isActive)
            {
                SpawnTrackCard();
                yield return new WaitForSeconds(cardSpawnInterval);
            }
        }

        /// <summary>
        /// 预构建星级→候选列表缓存，避免每张卡道卡都遍历全表
        /// </summary>
        private void BuildStarLevelCache()
        {
            var unitConfig = _wishManager.GetUnitConfig();
            var itemConfig = _wishManager.GetItemConfig();

            _unitsByStar = new Dictionary<int, List<UnitName>>();
            _itemsByStar = new Dictionary<int, List<ItemName>>();

            for (int star = 1; star <= 5; star++)
            {
                _unitsByStar[star] = _pool.GetUnitsByStar(unitConfig, star);
                _itemsByStar[star] = _pool.GetItemsByStar(itemConfig, star);
            }
        }

        private void SpawnTrackCard(float initialProgress = 0f)
        {
            GameObject cardObj = GetPooledCard();
            cardObj.SetActive(true);
            cardObj.transform.SetParent(cardTrack, false);
            cardObj.transform.SetAsFirstSibling();

            // 卡道上的卡全部用随机结果显示（不是实际抽卡结果）
            var trackResult = DrawRandomTrackCard();
            InitDisplayCard(cardObj, trackResult);

            var trackCard = cardObj.GetComponent<WishTrackCard>();
            if (trackCard == null)
                trackCard = cardObj.AddComponent<WishTrackCard>();

            trackCard.Init(cardSpawnPoint.anchoredPosition, cardEndPoint.anchoredPosition, cardMoveDuration, initialProgress);

            _activeCards.Add(trackCard);
        }

        /// <summary>
        /// 随机抽取一张卡道展示用结果（仅供卡道动画，不写入存档）
        /// </summary>
        private WishResult DrawRandomTrackCard()
        {
            int starLevel = _pool.RollStarLevel();
            bool isUnit = _pool.RollIsUnit();

            CardId cardId;
            Sprite sprite = null;

            if (isUnit)
            {
                var candidates = GetCachedUnitsByStar(ref starLevel);
                if (candidates.Count == 0)
                {
                    isUnit = false;
                }
                else
                {
                    var unitName = candidates[Random.Range(0, candidates.Count)];
                    cardId = new CardId(unitName);
                    var unitData = _wishManager.GetUnitConfig().GetUnitData(unitName);
                    sprite = unitData?.GetCard(0);
                    return new WishResult(cardId, starLevel, CardType.Unit, sprite, false);
                }
            }

            // 物品卡
            {
                var itemCandidates = GetCachedItemsByStar(ref starLevel);
                if (itemCandidates.Count == 0)
                {
                    cardId = new CardId(ItemName.Mora);
                    var itemData = _wishManager.GetItemConfig().GetItemData(ItemName.Mora);
                    sprite = itemData?.GetIcon(0);
                    starLevel = itemData?.starLevel ?? 1;
                }
                else
                {
                    var itemName = itemCandidates[Random.Range(0, itemCandidates.Count)];
                    cardId = new CardId(itemName);
                    var itemData = _wishManager.GetItemConfig().GetItemData(itemName);
                    sprite = itemData?.GetIcon(0);
                    starLevel = itemData?.starLevel ?? starLevel;
                }
                return new WishResult(cardId, starLevel, CardType.Item, sprite, false);
            }
        }

        /// <summary>
        /// 从缓存中查找指定星级的角色列表，空则降星
        /// </summary>
        private List<UnitName> GetCachedUnitsByStar(ref int starLevel)
        {
            var candidates = _unitsByStar[starLevel];
            if (candidates.Count > 0) return candidates;

            for (int s = starLevel - 1; s >= 1; s--)
            {
                candidates = _unitsByStar[s];
                if (candidates.Count > 0) { starLevel = s; return candidates; }
            }
            return candidates;
        }

        /// <summary>
        /// 从缓存中查找指定星级的物品列表，空则降星
        /// </summary>
        private List<ItemName> GetCachedItemsByStar(ref int starLevel)
        {
            var candidates = _itemsByStar[starLevel];
            if (candidates.Count > 0) return candidates;

            for (int s = starLevel - 1; s >= 1; s--)
            {
                candidates = _itemsByStar[s];
                if (candidates.Count > 0) { starLevel = s; return candidates; }
            }
            return candidates;
        }

        private IEnumerator InputCoroutine()
        {
            // 等待一帧，让按钮点击事件消费完，避免首次 Shoot 被触发
            yield return null;

            while (_isActive && _shotsCompleted < _totalShots)
            {
                if (_isInCooldown)
                {
                    _cooldownTimer -= Time.deltaTime;
                    UpdateCountdownBar(0f);

                    if (_cooldownTimer <= 0f)
                    {
                        _isInCooldown = false;
                        _currentTimer = clickTimeLimit;
                    }
                }
                else
                {
                    _currentTimer -= Time.deltaTime;
                    UpdateCountdownBar(_currentTimer / clickTimeLimit);

                    if (_currentTimer <= 0f)
                    {
                        Shoot();
                    }
                    else if (Input.GetMouseButtonDown(0))
                    {
                        Shoot();
                    }
                }

                yield return null;
            }

            if (_shotsCompleted >= _totalShots)
            {
                // 所有结果已写入内存，统一存盘一次
                _wishManager.SaveGame();
                yield return new WaitForSeconds(0.5f);
                StartCoroutine(ShowFinalDisplay());
            }
        }

        private void Shoot()
        {
            if (_isInCooldown || _shotsCompleted >= _totalShots) return;

            _shotsCompleted++;

            if (shootSFX != null)
                AudioManager.Instance?.PlaySFX(shootSFX, sfxVolume);

            StartCoroutine(FateLineCoroutine());

            _isInCooldown = true;

            // 立即弹出中心卡（射中什么就是什么，不替换内容）
            int starLevel = RevealAndPopResultCard(_shotsCompleted - 1);

            // 星级越高冷却越长，让高星结果停留更久
            _cooldownTimer = baseShotCooldown + starLevel * cooldownPerStar;
        }

        private int RevealAndPopResultCard(int index)
        {
            // 找到最接近卡道中心的卡牌
            WishTrackCard centerCard = FindCardNearestCenter();
            if (centerCard == null)
            {
                // 卡道上没卡，立即生成一张再用
                SpawnTrackCard();
                centerCard = FindCardNearestCenter();
                if (centerCard == null) return 1;
            }

            // 从卡道移除但保持世界位置
            _activeCards.Remove(centerCard);
            centerCard.SetPaused(true);

            // 射中什么卡就是什么卡——读取该卡的 SaveCardData 作为实际结果
            var card = centerCard.GetComponent<Card>();
            int starLevel = 1;
            if (card != null && card.saveCardData != null)
            {
                starLevel = card.saveCardData.StarLevel;
                var result = new WishResult(
                    card.saveCardData.id,
                    starLevel,
                    card.saveCardData.cardType,
                    card.saveCardData.cardType == CardType.Unit
                        ? _wishManager.GetUnitConfig().GetUnitData(card.saveCardData.id.AsUnitName())?.GetCard(0)
                        : _wishManager.GetItemConfig().GetItemData(card.saveCardData.id.AsItemName())?.GetIcon(0),
                    false
                );
                _results.Add(result);
                _wishManager.AddResultToInventory(result);

                // 播放光带特效（默认金色）
                card.PlayLightBand();
            }

            // 播放光柱爆发特效（星级颜色，强度随星级）
            StartCoroutine(GlowBurstCoroutine(starLevel));

            // 播放屏幕边缘泛光（星级颜色 + 星级强度 + 星级延伸距离）
            Color starColor = StarColor.GetStarColor(starLevel);
            _edgeGlow?.Play(starColor, starLevel);

            // 转移到 resultContainer，保持世界位置不变（视觉上卡片不动）
            var rect = centerCard.GetComponent<RectTransform>();
            centerCard.transform.SetParent(resultContainer, true);
            rect.localScale = Vector3.one * (resultCardSize / 160f);

            _resultCards.Add(rect);

            // 计算左侧竖向排列目标位置
            float startY = -(_totalShots - 1) * (resultCardSize + resultCardSpacing) * 0.5f;
            float targetY = startY + index * (resultCardSize + resultCardSpacing);
            Vector2 targetPos = new Vector2(0f, targetY);

            // 先在中心停留（星级越高停留越久），再飞到左侧目标位置
            float holdTime = 0.3f + (starLevel - 1) * 0.175f; // 1★=0.3s, 5★=1.0s
            _holdThenFlyCoroutines.Add(StartCoroutine(HoldThenFly(rect, targetPos, holdTime)));

            return starLevel;
        }

        private IEnumerator HoldThenFly(RectTransform rect, Vector2 targetPos, float holdTime)
        {
            // 停留让玩家看清抽到了什么
            yield return new WaitForSeconds(holdTime);

            // 飞到左侧目标位置
            Vector2 startPos = rect.anchoredPosition;
            float duration = 0.4f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                rect.anchoredPosition = Vector2.Lerp(startPos, targetPos, eased);
                yield return null;
            }
            rect.anchoredPosition = targetPos;
        }

        /// <summary>
        /// 停止所有仍在运行的 HoldThenFly 协程，避免与 ShowFinalDisplay 的 FlyToPosition 冲突
        /// </summary>
        private void StopHoldThenFlyCoroutines()
        {
            foreach (var c in _holdThenFlyCoroutines)
            {
                if (c != null) StopCoroutine(c);
            }
            _holdThenFlyCoroutines.Clear();
        }

        /// <summary>
        /// 光柱爆发特效——在卡道中心播放，颜色和强度取决于星级
        /// </summary>
        private IEnumerator GlowBurstCoroutine(int starLevel)
        {
            Color starColor = StarColor.GetStarColor(starLevel);

            // 创建临时光柱特效对象，放在卡道父级、卡道前面（sibling index < cardTrack）
            var pillarObj = new GameObject("GlowBurst", typeof(RectTransform));
            pillarObj.layer = cardTrack.gameObject.layer;
            pillarObj.transform.SetParent(cardTrack.parent, false);
            // 插入到 cardTrack 前面（渲染在卡道后面）
            int trackIndex = cardTrack.GetSiblingIndex();
            pillarObj.transform.SetSiblingIndex(trackIndex);

            var pillarRect = pillarObj.GetComponent<RectTransform>();
            pillarRect.anchorMin = new Vector2(0.5f, 0.5f);
            pillarRect.anchorMax = new Vector2(0.5f, 0.5f);
            pillarRect.pivot = new Vector2(0.5f, 0.5f);
            pillarRect.anchoredPosition = Vector2.zero;
            pillarRect.sizeDelta = Vector2.zero;

            var pillar = pillarObj.AddComponent<LightPillarEffect>();

            // 星级颜色传入，光柱特效内部会使用该颜色
            pillar.Play(starColor);

            // 等待光柱上升完成
            yield return new WaitForSeconds(0.5f);

            // 淡出光柱（Stop 只停 LightPillarEffect 自己的协程，不影响本协程）
            pillar.Stop();

            // 等待淡出完成再销毁
            yield return new WaitForSeconds(0.6f);
            Destroy(pillarObj);
        }

        private IEnumerator FlyToPosition(RectTransform rect, Vector2 targetPos)
        {
            Vector2 startPos = rect.anchoredPosition;
            float duration = 0.4f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                rect.anchoredPosition = Vector2.Lerp(startPos, targetPos, eased);
                yield return null;
            }
            rect.anchoredPosition = targetPos;
        }

        /// <summary>
        /// 找到卡道上最接近中心的卡牌
        /// </summary>
        private WishTrackCard FindCardNearestCenter()
        {
            WishTrackCard nearest = null;
            float minDist = float.MaxValue;

            foreach (var card in _activeCards)
            {
                if (card == null || !card.gameObject.activeInHierarchy) continue;
                // 确保卡在 cardTrack 下（不是已转移到 resultContainer 的卡）
                if (card.transform.parent != cardTrack) continue;
                var rect = card.GetComponent<RectTransform>();
                if (rect == null) continue;

                float dist = Mathf.Abs(rect.anchoredPosition.y);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = card;
                }
            }

            return nearest;
        }

        private IEnumerator FateLineCoroutine()
        {
            var lineObj = new GameObject("FateLine", typeof(RectTransform), typeof(Image));
            lineObj.transform.SetParent(fateLineContainer, false);
            lineObj.layer = fateLineContainer.gameObject.layer;

            var lineImg = lineObj.GetComponent<Image>();
            lineImg.color = fateLineColor;
            lineImg.raycastTarget = false;

            if (_fateLineShader == null)
                _fateLineShader = Shader.Find("UI/FateLine");
            if (_fateLineShader != null)
                lineImg.material = new Material(_fateLineShader);

            var lineRect = lineObj.GetComponent<RectTransform>();

            float trackWidth = cardTrack.rect.width;
            lineRect.sizeDelta = new Vector2(trackWidth * 0.6f, fateLineThickness);
            lineRect.anchoredPosition = Vector2.zero;

            Vector2 startPos = new Vector2(trackWidth * 0.5f + lineRect.rect.width * 0.5f, 0f);
            Vector2 endPos = new Vector2(-trackWidth * 0.5f - lineRect.rect.width * 0.5f, 0f);
            lineRect.anchoredPosition = startPos;

            float duration = 0.2f;
            float elapsed = 0f;
            bool hitPlayed = false;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                lineRect.anchoredPosition = Vector2.Lerp(startPos, endPos, t);

                if (!hitPlayed && t >= 0.5f)
                {
                    hitPlayed = true;
                    if (cardHitSFX != null)
                        AudioManager.Instance?.PlaySFX(cardHitSFX, sfxVolume);
                }

                yield return null;
            }

            if (lineImg.material != null) Destroy(lineImg.material);
            Destroy(lineObj);
        }

        private void UpdateCountdownBar(float ratio)
        {
            if (countdownBarFill == null) return;
            ratio = Mathf.Clamp01(ratio);
            countdownBarFill.localScale = new Vector3(ratio, 1f, 1f);
        }

        private IEnumerator ShowFinalDisplay()
        {
            _isActive = false;

            // 停止所有仍在飞行的 HoldThenFly 协程，避免与 FlyToPosition 冲突导致卡片位置跳变
            StopHoldThenFlyCoroutines();

            cardTrack.gameObject.SetActive(false);
            countdownBar.gameObject.SetActive(false);

            finalDisplayContainer.gameObject.SetActive(true);
            finalDisplayCanvasGroup.alpha = 0f;

            // 把已收集的结果卡从 resultContainer 转移到 finalDisplayContainer，横向排列
            float startX = -(_totalShots - 1) * (finalCardSize + resultCardSpacing) * 0.5f;
            for (int i = 0; i < _resultCards.Count; i++)
            {
                var rect = _resultCards[i];
                if (rect == null) continue;
                rect.transform.SetParent(finalDisplayContainer, true);
                float x = startX + i * (finalCardSize + resultCardSpacing);
                rect.localScale = Vector3.one * (finalCardSize / 160f);
                // 飞到中间横向排列位置
                StartCoroutine(FlyToPosition(rect, new Vector2(x, 0f)));
            }

            float fadeElapsed = 0f;
            float fadeDuration = 0.3f;
            while (fadeElapsed < fadeDuration)
            {
                fadeElapsed += Time.deltaTime;
                finalDisplayCanvasGroup.alpha = fadeElapsed / fadeDuration;
                yield return null;
            }
            finalDisplayCanvasGroup.alpha = 1f;

            yield return new WaitUntil(() => Input.GetMouseButtonDown(0));

            fadeElapsed = 0f;
            while (fadeElapsed < fadeDuration)
            {
                fadeElapsed += Time.deltaTime;
                finalDisplayCanvasGroup.alpha = 1f - fadeElapsed / fadeDuration;
                yield return null;
            }

            finalDisplayContainer.gameObject.SetActive(false);
            resultContainer.gameObject.SetActive(false);
            if (drawRoot != null) drawRoot.SetActive(false);

            ClearTrackCards();
            ClearCardPool();
        }

        private GameObject GetPooledCard()
        {
            foreach (var card in _cardPool)
            {
                if (!card.activeInHierarchy)
                    return card;
            }

            var obj = CreateCardGameObject();
            _cardPool.Add(obj);
            obj.SetActive(false);
            return obj;
        }

        private GameObject CreateCardGameObject()
        {
            var obj = Instantiate(cardPrefab, cardTrack, false);
            obj.layer = cardTrack.gameObject.layer;
            obj.name = "WishCard";
            var rect = obj.GetComponent<RectTransform>();
            // 用 scale 统一缩放，不改 sizeDelta（避免 ContentSizeFitter/TMP 字体不跟随的问题）
            rect.localScale = Vector3.one * (cardWidth / rect.sizeDelta.x);

            // 禁用交互，卡道卡不需要点击，跳过 FadeIn 避免半透明
            var card = obj.GetComponent<Card>();
            if (card != null)
                card.SetViewType(ViewType.OnlyDisplay);

            return obj;
        }

        private void ClearTrackCards()
        {
            _activeCards.Clear();
        }

        private void ClearCardPool()
        {
            foreach (var card in _cardPool)
            {
                if (card != null) Destroy(card);
            }
            _cardPool.Clear();
        }

        /// <summary>
        /// 用祈愿结果初始化展示卡牌
        /// </summary>
        private void InitDisplayCard(GameObject cardObj, WishResult result)
        {
            var card = cardObj.GetComponent<Card>();
            if (card == null) return;

            // 物品按 countPerServing 显示数量，角色固定 1
            int count = 1;
            if (result.cardType == CardType.Item && _wishManager != null)
            {
                var itemData = _wishManager.GetItemConfig().GetItemData(result.cardId.AsItemName());
                if (itemData != null)
                    count = itemData.countPerServing;
            }

            var saveData = new SaveCardData
            {
                id = result.cardId,
                count = count,
                skin = 0,
            };

            card.Init(saveData, null);
        }

        private void ClearResultCards()
        {
            foreach (var rect in _resultCards)
            {
                if (rect != null) Destroy(rect.gameObject);
            }
            _resultCards.Clear();
        }

        private void OnDisable()
        {
            _isActive = false;
            StopHoldThenFlyCoroutines();
            ClearTrackCards();
            ClearCardPool();
            ClearResultCards();
        }
    }

    /// <summary>
    /// 卡道上移动的单张卡牌
    /// </summary>
    public class WishTrackCard : MonoBehaviour
    {
        private RectTransform _rect;
        private Vector2 _startPos;
        private Vector2 _targetPos;
        private float _duration;
        private float _elapsed;
        private bool _paused;

        public void Init(Vector2 startPos, Vector2 targetPos, float duration, float initialProgress = 0f)
        {
            _rect = GetComponent<RectTransform>();
            _startPos = startPos;
            _targetPos = targetPos;
            _duration = duration;
            _elapsed = initialProgress * duration;
            _paused = false;
        }

        public void SetPaused(bool paused)
        {
            _paused = paused;
        }

        private void Update()
        {
            if (_rect == null || _paused) return;
            _elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_elapsed / _duration);
            _rect.anchoredPosition = Vector2.Lerp(_startPos, _targetPos, t);

            if (t >= 1f)
                gameObject.SetActive(false);
        }
    }
}
