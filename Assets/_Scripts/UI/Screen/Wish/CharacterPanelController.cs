using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
using GIC.Tool;
namespace GIC.UI
{


    /// <summary>
    /// 角色面板入场/退场动画控制器
    /// - FadeIn: characterImage 从左滑入, starRating 从下滑入, title 从右滑入, 同时淡入
    /// - FadeOut: 反向动画 + 淡出
    /// - 未赋值的 RectTransform 自动跳过位置动画，仅参与透明度渐变
    /// - 设置 unitName 后自动从 UnitConfig 读取数据并填充 UI
    /// </summary>
    public class CharacterPanelController : MonoBehaviour
    {
        [Header("数据")]
        [SerializeField] private UnitName unitName;

        [Header("动画目标（可选，null 则只淡入淡出）")]
        [SerializeField] private RectTransform characterImageRect;
        [SerializeField] private RectTransform starRating;
        [SerializeField] private RectTransform titleArea;

        [Header("内容填充目标（可选，null 则跳过对应填充）")]
        [SerializeField] private Image colorImage;
        [SerializeField] private Image characterImage;
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private Image elementIcon;
        [SerializeField] private Image factionIcon;
        [SerializeField] private TextMeshProUGUI descriptionText;

        [Header("透明度控制")]
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("动画参数")]
        [SerializeField] private float slideDistance = 200f;
        [SerializeField] private float fadeDuration = 0.25f;
        [SerializeField] private AnimationCurve fadeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        public float FadeDuration => fadeDuration;
        public UnitName UnitName => unitName;

        private Vector2 characterTargetPos;
        private Vector2 starRatingTargetPos;
        private Vector2 titleTargetPos;
        private bool targetPositionsReady;
        private bool contentFilled;

        private string _currentAddress;
        private bool _spriteLoadDone;

        [Autowired] private UnitConfig _unitConfig;
        [Autowired] private ElementFactionConfig _iconConfig;
        [Autowired] private AssetCache _assetCache;

        /// <summary>
        /// 懒注入：面板在场景中保存为未激活时 Awake 不会运行，
        /// WishScreen.Awake 的 PreloadSprite 先于本组件注入执行 —— 此处必须兜底注入。
        /// </summary>
        private AssetCache AssetCacheRef
        {
            get
            {
                if (_assetCache == null)
                    Wargame.Instance?.Context?.Inject(this);
                return _assetCache;
            }
        }

        private void Awake()
        {
            Wargame.Instance?.Context?.Inject(this); // 容器已就绪，Awake 注入（未激活时由 AssetCacheRef 兜底）
            SetAlpha(0f);
            FillContentFromConfig();
        }

        /// <summary>
        /// 从 UnitConfig 读取数据，填充 Name、Title、Element 图标、Faction 图标、立绘颜色
        /// </summary>
        private void FillContentFromConfig()
        {
            if (contentFilled) return;
            contentFilled = true;

            var config = _unitConfig != null
                ? _unitConfig
                : Resources.Load<UnitConfig>("Configs/UnitConfig");
            if (config == null) return;

            var data = config.GetUnitData(unitName);
            if (data == null) return;

            // 名称（TextCombiner 统一本地化，语言切换自动刷新）
            if (nameText != null)
                EnsureTextCombiner(nameText).SetSingleEntry(unitName.GetEntry());

            // 称号
            if (titleText != null)
                EnsureTextCombiner(titleText).SetSingleEntry(data.GetTitleEntry());

            // 元素图标
            if (elementIcon != null && _iconConfig != null)
                elementIcon.sprite = _iconConfig.GetElementIconDeep(data.selfElement);

            // 势力图标（取第一个 faction）
            if (factionIcon != null && _iconConfig != null && data.factions?.Length > 0)
                factionIcon.sprite = _iconConfig.GetFactionIcon(data.factions[0]);

            // 立绘背景色 = 元素对应颜色（配置文件）
            if (colorImage != null && _iconConfig != null)
                colorImage.color = _iconConfig.GetElementColor(data.selfElement);

            // 描述
            if (descriptionText != null)
                EnsureTextCombiner(descriptionText).SetSingleEntry(data.GetDescriptionEntry());
        }

        /// <summary>确保目标文本挂有 TextCombiner（本地化统一入口）</summary>
        private static TextCombiner EnsureTextCombiner(TextMeshProUGUI tmp)
        {
            var tc = tmp.GetComponent<TextCombiner>();
            if (tc == null) tc = tmp.gameObject.AddComponent<TextCombiner>();
            return tc;
        }

        public void SetFadeInState()
        {
            EnsureTargetPositions();
            SetAlpha(1f);
            SnapToTarget();
        }

        public void FadeIn()
        {
            EnsureTargetPositions();

            SetToStartPositions();
            SetAlpha(0f);
            gameObject.SetActive(true);

            // 若本身或祖先仍处于未激活（可能在场景切换瞬间），无法启动协程，
            // 直接跳到目标状态兜底，避免 "Coroutine couldn't be started" 报错
            if (!gameObject.activeInHierarchy)
            {
                SnapToTarget();
                SetAlpha(1f);
                return;
            }

            // 按需加载立绘（在 FadeInCoroutine 内部等待，避免被 StopAllCoroutines 中断）
            bool needsLoad = characterImage != null && characterImage.sprite == null;

            StopAllCoroutines();
            StartCoroutine(FadeInCoroutine(needsLoad));
        }

        /// <summary>
        /// 预加载立绘（在旧面板淡出期间调用，提前开始加载 4K 纹理）
        /// 通过 AssetCache 统一管理：命中缓存（如哥伦比娅）则立即回调，否则异步加载
        /// </summary>
        public void PreloadSprite()
        {
            if (characterImage == null || characterImage.sprite != null) return;
            if (_currentAddress != null) return;

            _currentAddress = $"WishArt/{unitName.ToString().ToLower()}";
            _spriteLoadDone = false;

            AssetCacheRef?.LoadAsync<Sprite>(_currentAddress, sprite =>
            {
                if (this != null && characterImage != null && sprite != null)
                    characterImage.sprite = sprite;
                _spriteLoadDone = true;
            }, LoadPriority.High);
        }

        public void FadeOut()
        {
            StopAllCoroutines();
            StartCoroutine(FadeOutCoroutine());
        }

        private void EnsureTargetPositions()
        {
            if (targetPositionsReady) return;
            targetPositionsReady = true;
            Canvas.ForceUpdateCanvases();

            characterTargetPos   = characterImageRect ? characterImageRect.anchoredPosition : Vector2.zero;
            starRatingTargetPos  = starRating          ? starRating.anchoredPosition          : Vector2.zero;
            titleTargetPos       = titleArea           ? titleArea.anchoredPosition           : Vector2.zero;
        }

        private void SetToStartPositions()
        {
            if (characterImageRect != null)
                characterImageRect.anchoredPosition = characterTargetPos + Vector2.left * slideDistance;
            if (starRating != null)
                starRating.anchoredPosition = starRatingTargetPos + Vector2.down * slideDistance;
            if (titleArea != null)
                titleArea.anchoredPosition = titleTargetPos + Vector2.right * slideDistance;
        }

        private IEnumerator FadeInCoroutine(bool loadSprite)
        {
            // 若需要加载且尚未通过 PreloadSprite 预加载，则通过 AssetCache 启动异步加载
            if (loadSprite && _currentAddress == null)
            {
                _currentAddress = $"WishArt/{unitName.ToString().ToLower()}";
                _spriteLoadDone = false;

                AssetCacheRef?.LoadAsync<Sprite>(_currentAddress, sprite =>
                {
                    if (this != null && characterImage != null && sprite != null)
                        characterImage.sprite = sprite;
                    _spriteLoadDone = true;
                }, LoadPriority.High);
            }

            float startTime = Time.realtimeSinceStartup;

            // Phase 1: 位置动画与立绘加载并行，alpha 仅在立绘就绪后跟随
            while (true)
            {
                float elapsed = Time.realtimeSinceStartup - startTime;
                elapsed = Mathf.Min(elapsed, fadeDuration);

                if (elapsed >= fadeDuration)
                    break;

                float t = fadeCurve.Evaluate(elapsed / fadeDuration);

                if (characterImageRect != null)
                    characterImageRect.anchoredPosition = characterTargetPos + Vector2.left * Mathf.LerpUnclamped(slideDistance, 0f, t);
                if (starRating != null)
                    starRating.anchoredPosition = starRatingTargetPos + Vector2.down * Mathf.LerpUnclamped(slideDistance, 0f, t);
                if (titleArea != null)
                    titleArea.anchoredPosition = titleTargetPos + Vector2.right * Mathf.LerpUnclamped(slideDistance, 0f, t);

                // alpha 仅在立绘就绪后才开始淡入，避免空图淡入后立绘瞬间弹出
                if (!loadSprite || (characterImage != null && characterImage.sprite != null))
                    SetAlpha(t);

                yield return null;
            }

            SnapToTarget();

            // Phase 2: 立绘尚未加载完成，等待 AssetCache 回调后单独淡入
            if (loadSprite && characterImage != null && characterImage.sprite == null)
            {
                // 等待回调（成功设 sprite，失败设 _spriteLoadDone=true 且 sprite 仍为 null）
                while (!_spriteLoadDone)
                    yield return null;

                if (characterImage.sprite != null)
                {
                    startTime = Time.realtimeSinceStartup;
                    while (true)
                    {
                        float elapsed = Time.realtimeSinceStartup - startTime;
                        elapsed = Mathf.Min(elapsed, fadeDuration);
                        if (elapsed >= fadeDuration) break;
                        SetAlpha(fadeCurve.Evaluate(elapsed / fadeDuration));
                        yield return null;
                    }
                }
            }

            SetAlpha(1f);
        }

        private IEnumerator FadeOutCoroutine()
        {
            float startAlpha = canvasGroup != null ? canvasGroup.alpha : 1f;
            float startTime = Time.realtimeSinceStartup;

            while (true)
            {
                float elapsed = Time.realtimeSinceStartup - startTime;
                elapsed = Mathf.Min(elapsed, fadeDuration);

                if (elapsed >= fadeDuration)
                    break;

                float t = fadeCurve.Evaluate(elapsed / fadeDuration);

                if (characterImageRect != null)
                    characterImageRect.anchoredPosition = characterTargetPos + Vector2.left * Mathf.LerpUnclamped(0f, slideDistance, t);
                if (starRating != null)
                    starRating.anchoredPosition = starRatingTargetPos + Vector2.down * Mathf.LerpUnclamped(0f, slideDistance, t);
                if (titleArea != null)
                    titleArea.anchoredPosition = titleTargetPos + Vector2.right * Mathf.LerpUnclamped(0f, slideDistance, t);

                SetAlpha(Mathf.LerpUnclamped(startAlpha, 0f, t));
                yield return null;
            }

            SetAlpha(0f);
            gameObject.SetActive(false);
        }

        private void SnapToTarget()
        {
            if (characterImageRect != null) characterImageRect.anchoredPosition = characterTargetPos;
            if (starRating          != null) starRating.anchoredPosition          = starRatingTargetPos;
            if (titleArea           != null) titleArea.anchoredPosition           = titleTargetPos;
        }

        private void SetAlpha(float alpha)
        {
            if (canvasGroup != null) canvasGroup.alpha = alpha;
        }

        private void OnDestroy()
        {
            // 通过 AssetCache 释放引用（引用计数 -1）
            if (_currentAddress != null)
                AssetCacheRef?.Release(_currentAddress);
        }
    }

}


