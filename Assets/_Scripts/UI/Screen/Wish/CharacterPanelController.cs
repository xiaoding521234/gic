using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
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

        private AsyncOperationHandle<Sprite> _wishArtHandle;
        private bool _spriteFromCache;

        private void Awake()
        {
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

            var configManager = Wargame.Instance?.ConfigManager;
            var config = configManager != null
                ? configManager.GetUnitConfig()
                : Resources.Load<UnitConfig>("Configs/UnitConfig");
            if (config == null) return;

            var data = config.GetUnitData(unitName);
            if (data == null) return;

            // 名称
            if (nameText != null)
                nameText.text = unitName.GetEntry().GetLocalizedString();

            // 称号
            if (titleText != null)
                titleText.text = data.GetTitleEntry().GetLocalizedString();

            // 元素图标
            var iconConfig = Wargame.Instance?.ConfigManager?.GetElementFactionIconConfig();
            if (elementIcon != null && iconConfig != null)
                elementIcon.sprite = iconConfig.GetElementIconDeep(data.selfElement);

            // 势力图标（取第一个 faction）
            if (factionIcon != null && iconConfig != null && data.factions?.Length > 0)
                factionIcon.sprite = iconConfig.GetFactionIcon(data.factions[0]);

            // 立绘背景色 = 元素对应颜色
            if (colorImage != null)
                colorImage.color = ElementColor.GetColor(data.selfElement);

            // 描述
            if (descriptionText != null)
                descriptionText.text = data.GetDescriptionEntry().GetLocalizedString();
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
        /// 优先查 WishArtPreloader 常驻缓存（如哥伦比娅），命中则直接赋值无需等待
        /// </summary>
        public void PreloadSprite()
        {
            if (characterImage == null || characterImage.sprite != null) return;
            if (_wishArtHandle.IsValid()) return;

            // 优先查常驻缓存
            if (WishArtPreloader.TryGetHandle(unitName, out var cachedHandle))
            {
                _wishArtHandle = cachedHandle;
                _spriteFromCache = true;
                return;
            }

            string address = $"WishArt/{unitName.ToString().ToLower()}";
            _wishArtHandle = Addressables.LoadAssetAsync<Sprite>(address);
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
            // 若需要加载且尚未通过 PreloadSprite 预加载，则启动异步加载
            if (loadSprite && !_wishArtHandle.IsValid())
            {
                string address = $"WishArt/{unitName.ToString().ToLower()}";
                _wishArtHandle = Addressables.LoadAssetAsync<Sprite>(address);
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

                // 立绘加载完成则赋值
                if (loadSprite && characterImage != null && characterImage.sprite == null
                    && _wishArtHandle.IsValid() && _wishArtHandle.IsDone)
                {
                    if (_wishArtHandle.Status == AsyncOperationStatus.Succeeded)
                        characterImage.sprite = _wishArtHandle.Result;
                    else
                        Debug.LogWarning($"无法加载立绘: WishArt/{unitName.ToString().ToLower()}");
                    loadSprite = false;
                }

                // alpha 仅在立绘就绪后才开始淡入，避免空图淡入后立绘瞬间弹出
                if (!loadSprite || (characterImage != null && characterImage.sprite != null))
                    SetAlpha(t);

                yield return null;
            }

            SnapToTarget();

            // Phase 2: 立绘尚未加载完成，等待加载后单独淡入
            if (loadSprite && characterImage != null && characterImage.sprite == null
                && _wishArtHandle.IsValid())
            {
                while (!_wishArtHandle.IsDone)
                    yield return null;

                if (_wishArtHandle.Status == AsyncOperationStatus.Succeeded)
                    characterImage.sprite = _wishArtHandle.Result;
                else
                    Debug.LogWarning($"无法加载立绘: WishArt/{unitName.ToString().ToLower()}");

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
            // 常驻缓存的 handle 由 WishArtPreloader 管理，不在此释放
            if (_spriteFromCache) return;

            if (_wishArtHandle.IsValid())
                Addressables.Release(_wishArtHandle);
        }
    }

}


