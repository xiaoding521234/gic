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

        private Sprite LoadCharacterSprite()
        {
            return Resources.Load<Sprite>($"UI/Wish/{unitName.ToString().ToLower()}");
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

            // 按需加载立绘（延迟到真正显示时才加载，避免 7 张 4K 同时进内存）
            if (characterImage != null && characterImage.sprite == null)
                characterImage.sprite = LoadCharacterSprite();

            SetToStartPositions();
            SetAlpha(0f);
            gameObject.SetActive(true);

            StopAllCoroutines();
            StartCoroutine(FadeInCoroutine());
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

        private IEnumerator FadeInCoroutine()
        {
            float startTime = Time.realtimeSinceStartup;

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

                SetAlpha(t);
                yield return null;
            }

            SnapToTarget();
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
    }

}


