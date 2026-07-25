using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 原神风格抽卡揭示特效控制器（适配 160×240 竖向卡片）
/// 流程: 黑屏 → 竖向光柱爆发(CardGlow) → 星级色闪光 → 粒子飞溅 → 卡片揭示 → 星级弹出
/// </summary>
public class WishEffectController : MonoBehaviour
{
    [Header("全屏遮罩")]
    [SerializeField] private Image blackOverlay;

    [Header("竖向光柱（CardGlow shader）")]
    [SerializeField] private Image glowBurstImage;
    [SerializeField] private Material glowMaterial;
    [SerializeField] private float glowBurstDuration = 0.5f;
    [SerializeField] private float glowMaxIntensity = 5f;

    [Header("星级颜色闪光")]
    [SerializeField] private Image colorFlash;
    [SerializeField] private float flashDuration = 0.3f;

    [Header("星辰粒子")]
    [SerializeField] private RectTransform particleContainer;
    [SerializeField] private Sprite starSprite;
    [SerializeField] private int particleCount = 30;
    [SerializeField] private float particleSpeed = 800f;
    [SerializeField] private float particleLifetime = 1.5f;
    [SerializeField] private float particleSize = 12f;
    [SerializeField] private float verticalBias = 0.6f;

    [Header("卡片揭示")]
    [SerializeField] private Image cardImage;
    [SerializeField] private CanvasGroup cardCanvasGroup;
    [SerializeField] private Image cardBackImage;
    [SerializeField] private float revealDuration = 0.6f;
    [SerializeField] private float revealZoomFrom = 1.5f;

    [Header("星级显示")]
    [SerializeField] private TextMeshProUGUI starRatingText;
    [SerializeField] private CanvasGroup starRatingCanvasGroup;
    [SerializeField] private float starPopDuration = 0.3f;
    [SerializeField] private float starPopScale = 1.2f;

    [Header("音效")]
    [SerializeField] private AudioClip burstSFX;
    [SerializeField] private AudioClip revealSFX;
    [SerializeField] private float sfxVolume = 0.8f;

    [Header("时序")]
    [SerializeField] private float blackScreenHold = 0.3f;
    [SerializeField] private float afterBurstDelay = 0.1f;
    [SerializeField] private float afterRevealDelay = 0.2f;

    private List<GameObject> _activeParticles = new();
    private Material _glowInst;

    private void Awake()
    {
        if (glowMaterial != null)
            _glowInst = new Material(glowMaterial);
    }

    /// <summary>
    /// 播放完整的抽卡揭示特效
    /// </summary>
    /// <param name="cardSprite">卡面精灵</param>
    /// <param name="starLevel">星级 (1-5)</param>
    public void Play(Sprite cardSprite, int starLevel)
    {
        gameObject.SetActive(true);
        StartCoroutine(PlaySequence(cardSprite, starLevel));
    }

    private IEnumerator PlaySequence(Sprite cardSprite, int starLevel)
    {
        Color starColor = StarColor.GetStarColor(starLevel);

        // 初始状态
        SetBlackOverlay(1f);
        SetColorFlash(0f, starColor);
        SetCardReveal(0f, revealZoomFrom, cardSprite, starColor);
        SetStarRating(0f, 0f);

        // 1. 黑屏停留
        yield return new WaitForSecondsRealtime(blackScreenHold);

        // 2. 竖向光柱爆发 + 音效
        if (burstSFX != null)
            AudioManager.Instance?.PlaySFX(burstSFX, sfxVolume);

        // 3. 星级色闪光（与光柱同时）
        StartCoroutine(AnimateColorFlash(starColor));
        yield return StartCoroutine(AnimateGlowBurst(starColor));

        // 4. 星辰粒子飞溅
        SpawnStarParticles(starColor);

        // 5. 黑屏淡出 + 卡片揭示
        if (revealSFX != null)
            AudioManager.Instance?.PlaySFX(revealSFX, sfxVolume);

        yield return new WaitForSecondsRealtime(afterBurstDelay);

        StartCoroutine(AnimateBlackOverlay(1f, 0f, revealDuration));
        yield return StartCoroutine(AnimateCardReveal(starColor));

        // 6. 星级弹出
        yield return new WaitForSecondsRealtime(afterRevealDelay);
        yield return StartCoroutine(AnimateStarRating(starLevel, starColor));

        // 等待粒子结束
        float remaining = particleLifetime - revealDuration - afterRevealDelay - starPopDuration;
        if (remaining > 0f)
            yield return new WaitForSecondsRealtime(remaining);
    }

    /// <summary>
    /// 结束特效，淡出并隐藏
    /// </summary>
    public void Hide()
    {
        StartCoroutine(HideCoroutine());
    }

    private IEnumerator HideCoroutine()
    {
        float fadeTime = 0.3f;
        StartCoroutine(AnimateBlackOverlay(0f, 1f, fadeTime));

        if (cardCanvasGroup != null)
        {
            float start = cardCanvasGroup.alpha;
            float t = 0f;
            while (t < fadeTime)
            {
                t += Time.unscaledDeltaTime;
                cardCanvasGroup.alpha = Mathf.Lerp(start, 0f, t / fadeTime);
                yield return null;
            }
        }

        ClearParticles();
        gameObject.SetActive(false);
    }

    // ==================== 竖向光柱爆发 ====================

    private IEnumerator AnimateGlowBurst(Color starColor)
    {
        if (glowBurstImage == null) yield break;

        glowBurstImage.gameObject.SetActive(true);

        // 用实例化 Material 动态控制 shader 参数
        if (_glowInst != null)
        {
            glowBurstImage.material = _glowInst;
            _glowInst.SetColor("_GlowColor", starColor);
            _glowInst.SetFloat("_GlowIntensity", glowMaxIntensity);
        }

        // 光柱本身从 0 缩放到目标
        glowBurstImage.transform.localScale = Vector3.one * 0.1f;
        glowBurstImage.color = new Color(starColor.r, starColor.g, starColor.b, 1f);

        float t = 0f;
        while (t < glowBurstDuration)
        {
            t += Time.unscaledDeltaTime;
            float p = t / glowBurstDuration;

            // 缩放：快速放大
            float scale = Mathf.LerpUnclamped(0.1f, 1.5f, EaseOutCubic(p));
            glowBurstImage.transform.localScale = Vector3.one * scale;

            // shader 强度衰减
            if (_glowInst != null)
                _glowInst.SetFloat("_GlowIntensity", Mathf.Lerp(glowMaxIntensity, 0f, p * p));

            // 整体 alpha 衰减
            glowBurstImage.color = new Color(starColor.r, starColor.g, starColor.b, Mathf.Lerp(1f, 0f, p * p));

            yield return null;
        }

        glowBurstImage.gameObject.SetActive(false);
    }

    // ==================== 颜色闪光 ====================

    private IEnumerator AnimateColorFlash(Color color)
    {
        if (colorFlash == null) yield break;

        colorFlash.gameObject.SetActive(true);

        float t = 0f;
        float halfDuration = flashDuration * 0.5f;

        while (t < halfDuration)
        {
            t += Time.unscaledDeltaTime;
            colorFlash.color = new Color(color.r, color.g, color.b, t / halfDuration * 0.6f);
            yield return null;
        }

        t = 0f;
        while (t < halfDuration)
        {
            t += Time.unscaledDeltaTime;
            colorFlash.color = new Color(color.r, color.g, color.b, Mathf.Lerp(0.6f, 0f, t / halfDuration));
            yield return null;
        }

        colorFlash.gameObject.SetActive(false);
    }

    // ==================== 星辰粒子（竖向偏置） ====================

    private void SpawnStarParticles(Color color)
    {
        if (particleContainer == null || starSprite == null) return;

        for (int i = 0; i < particleCount; i++)
        {
            var go = new GameObject("StarParticle");
            go.transform.SetParent(particleContainer, false);
            go.transform.localPosition = Vector3.zero;

            var img = go.AddComponent<Image>();
            img.sprite = starSprite;
            img.color = color;
            img.raycastTarget = false;
            (img.transform as RectTransform).sizeDelta = Vector2.one * particleSize * Random.Range(0.6f, 1.4f);

            // 竖向偏置：上下飞溅多于左右
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            var dir = new Vector2(
                Mathf.Cos(angle) * (1f - verticalBias),
                Mathf.Sin(angle) * (1f + verticalBias)
            ).normalized;
            float speed = particleSpeed * Random.Range(0.5f, 1.2f);

            StartCoroutine(AnimateParticle(go, dir, speed, particleLifetime * Random.Range(0.7f, 1.3f), color));

            _activeParticles.Add(go);
        }
    }

    private IEnumerator AnimateParticle(GameObject go, Vector2 dir, float speed, float lifetime, Color color)
    {
        float t = 0f;
        var rect = go.transform as RectTransform;
        Vector2 pos = Vector2.zero;
        float initScale = rect.localScale.x;

        while (t < lifetime && go != null)
        {
            t += Time.unscaledDeltaTime;
            float p = t / lifetime;

            pos += dir * speed * Time.unscaledDeltaTime;
            rect.anchoredPosition = pos;

            // 缩放：先放大再缩小
            float scaleP = p < 0.2f ? Mathf.Lerp(0f, initScale, p / 0.2f) : Mathf.Lerp(initScale, 0f, (p - 0.2f) / 0.8f);
            rect.localScale = Vector3.one * scaleP;

            // 旋转
            rect.Rotate(0, 0, 360f * Time.unscaledDeltaTime);

            // 淡出
            var img = go.GetComponent<Image>();
            if (img != null)
                img.color = new Color(color.r, color.g, color.b, Mathf.Lerp(1f, 0f, p * p));

            yield return null;
        }

        if (go != null) Destroy(go);
    }

    private void ClearParticles()
    {
        foreach (var p in _activeParticles)
        {
            if (p != null) Destroy(p);
        }
        _activeParticles.Clear();
    }

    // ==================== 卡片揭示 ====================

    private IEnumerator AnimateCardReveal(Color tint)
    {
        if (cardImage == null || cardCanvasGroup == null) yield break;

        float t = 0f;
        while (t < revealDuration)
        {
            t += Time.unscaledDeltaTime;
            float p = t / revealDuration;

            float alpha = Mathf.Lerp(0f, 1f, p);
            float scale = Mathf.Lerp(revealZoomFrom, 1f, EaseOutCubic(p));

            cardCanvasGroup.alpha = alpha;
            cardImage.transform.localScale = Vector3.one * scale;

            // 早期带星级色调，逐渐恢复正常
            if (p < 0.5f)
            {
                float tintAmount = Mathf.Lerp(0.5f, 0f, p / 0.5f);
                cardImage.color = Color.Lerp(Color.white, tint, tintAmount);
                if (cardBackImage != null)
                    cardBackImage.color = Color.Lerp(StarColor.GetStarColor(5), tint, tintAmount);
            }
            else
            {
                cardImage.color = Color.white;
            }

            yield return null;
        }

        cardCanvasGroup.alpha = 1f;
        cardImage.transform.localScale = Vector3.one;
        cardImage.color = Color.white;
    }

    // ==================== 星级弹出 ====================

    private IEnumerator AnimateStarRating(int starLevel, Color color)
    {
        if (starRatingText == null || starRatingCanvasGroup == null) yield break;

        string stars = new string('★', starLevel);
        starRatingText.text = stars;
        starRatingText.color = color;

        starRatingText.gameObject.SetActive(true);

        float t = 0f;
        while (t < starPopDuration)
        {
            t += Time.unscaledDeltaTime;
            float p = t / starPopDuration;

            // 弹性缩放：先超出再回弹
            float scale;
            if (p < 0.5f)
                scale = Mathf.Lerp(0f, starPopScale, p / 0.5f);
            else
                scale = Mathf.Lerp(starPopScale, 1f, (p - 0.5f) / 0.5f);

            starRatingCanvasGroup.alpha = Mathf.Lerp(0f, 1f, p);
            starRatingText.transform.localScale = Vector3.one * scale;

            yield return null;
        }

        starRatingCanvasGroup.alpha = 1f;
        starRatingText.transform.localScale = Vector3.one;
    }

    // ==================== 黑屏遮罩 ====================

    private IEnumerator AnimateBlackOverlay(float from, float to, float duration)
    {
        if (blackOverlay == null) yield break;

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float p = t / duration;
            blackOverlay.color = new Color(0, 0, 0, Mathf.Lerp(from, to, p));
            yield return null;
        }

        blackOverlay.color = new Color(0, 0, 0, to);
    }

    // ==================== 状态设置 ====================

    private void SetBlackOverlay(float alpha)
    {
        if (blackOverlay != null)
            blackOverlay.color = new Color(0, 0, 0, alpha);
    }

    private void SetColorFlash(float alpha, Color color)
    {
        if (colorFlash != null)
        {
            colorFlash.color = new Color(color.r, color.g, color.b, alpha);
            colorFlash.gameObject.SetActive(alpha > 0f);
        }
    }

    private void SetCardReveal(float alpha, float scale, Sprite sprite, Color tint)
    {
        if (cardImage != null)
        {
            cardImage.sprite = sprite;
            cardImage.color = Color.Lerp(Color.white, tint, 0.5f);
            cardImage.transform.localScale = Vector3.one * scale;
        }
        if (cardCanvasGroup != null)
            cardCanvasGroup.alpha = alpha;
    }

    private void SetStarRating(float alpha, float scale)
    {
        if (starRatingCanvasGroup != null)
            starRatingCanvasGroup.alpha = alpha;
        if (starRatingText != null)
        {
            starRatingText.transform.localScale = Vector3.one * scale;
            starRatingText.gameObject.SetActive(alpha > 0f);
        }
    }

    // ==================== 工具 ====================

    private static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);

    private void OnDisable()
    {
        ClearParticles();
    }

    private void OnDestroy()
    {
        if (_glowInst != null)
            Destroy(_glowInst);
    }
}
