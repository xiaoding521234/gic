using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 祈愿界面氛围特效：白雾层 + 元素粒子，从右往左飘动
/// </summary>
public class WishAmbienceController : MonoBehaviour
{
    [Header("白雾")]
    [SerializeField] private Image fogImage;
    [SerializeField] private float fogScrollSpeed = 15f;
    [SerializeField] private float fogScrollRange = 1920f;
    [SerializeField] private float fogMaxAlpha = 0.15f;

    [Header("元素粒子")]
    [SerializeField] private RectTransform particleContainer;
    [SerializeField] private Sprite particleSprite;
    [SerializeField] private int particleCount = 12;
    [SerializeField] private float particleSpawnInterval = 0.8f;
    [SerializeField] private float particleMinSpeed = 20f;
    [SerializeField] private float particleMaxSpeed = 60f;
    [SerializeField] private float particleMinSize = 6f;
    [SerializeField] private float particleMaxSize = 18f;
    [SerializeField] private float particleMinDriftY = -15f;
    [SerializeField] private float particleMaxDriftY = 15f;
    [SerializeField] private float particleFadeInDuration = 0.5f;
    [SerializeField] private float particleFadeOutStart = 0.7f;

    [Header("边界")]
    [SerializeField] private float spawnX = 1200f;
    [SerializeField] private float destroyX = -1200f;

    [Header("区域裁剪")]
    [SerializeField] private float topBound = 0f;
    [SerializeField] private float bottomBound = -600f;

    private readonly List<ParticleData> _activeParticles = new();
    private Coroutine _fogCoroutine;
    private Coroutine _spawnCoroutine;
    private Color _currentColor = Color.white;
    private Material _particleMat;

    [System.Serializable]
    private struct ParticleData
    {
        public RectTransform rect;
        public Image image;
        public Vector2 velocity;
        public float lifetime;
        public float age;
        public float startAlpha;
    }

    private void OnEnable()
    {
        StartAmbience();
    }

    private void OnDisable()
    {
        StopAmbience();
    }

    public void StartAmbience()
    {
        if (fogImage != null && _fogCoroutine == null)
        {
            Color c = Color.white;
            c.a = fogMaxAlpha;
            fogImage.color = c;
            _fogCoroutine = StartCoroutine(ScrollFog());
        }

        if (particleContainer != null && particleSprite != null && _spawnCoroutine == null)
        {
            // 创建 Additive 混合材质（发光效果）
            if (_particleMat == null)
            {
                var shader = Shader.Find("UI/Default");
                _particleMat = new Material(shader);
                _particleMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _particleMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
                _particleMat.SetInt("_Blend", 1);
                _particleMat.DisableKeyword("_ALPHATEST_ON");
                _particleMat.EnableKeyword("_ALPHABLEND_ON");
                _particleMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                _particleMat.renderQueue = 3000;
            }

            // 初始预填充：在屏幕内随机位置生成粒子，不等生成间隔
            for (int i = 0; i < particleCount; i++)
            {
                SpawnOneParticle(initial: true);
            }
            _spawnCoroutine = StartCoroutine(SpawnParticles());
        }
    }

    public void StopAmbience()
    {
        if (_fogCoroutine != null) { StopCoroutine(_fogCoroutine); _fogCoroutine = null; }
        if (_spawnCoroutine != null) { StopCoroutine(_spawnCoroutine); _spawnCoroutine = null; }

        foreach (var p in _activeParticles)
        {
            if (p.rect != null) Destroy(p.rect.gameObject);
        }
        _activeParticles.Clear();
    }

    /// <summary>
    /// 设置粒子颜色（传入角色元素的对应颜色）
    /// </summary>
    public void SetElementColor(Color color)
    {
        _currentColor = color;
        // 即时更新已有粒子
        foreach (var p in _activeParticles)
        {
            if (p.image != null)
            {
                float alpha = p.image.color.a;
                p.image.color = new Color(color.r, color.g, color.b, alpha);
            }
        }
    }

    // ==================== 白雾滚动 ====================

    private IEnumerator ScrollFog()
    {
        // 用 Material 的 UV 偏移实现无缝滚动，不会有瞬移跳变
        Material fogMat = null;
        if (fogImage != null)
        {
            fogMat = fogImage.materialForRendering;
            if (fogMat == null || fogMat == fogImage.defaultMaterial)
            {
                // 没有自定义 Material，创建一个实例来修改 UV offset
                fogMat = new Material(fogImage.defaultMaterial);
                fogImage.material = fogMat;
            }
            else
            {
                fogMat = Instantiate(fogMat);
                fogImage.material = fogMat;
            }
        }

        float offset = 0f;
        float uvSpeed = fogScrollSpeed / fogScrollRange;

        while (true)
        {
            offset += Time.deltaTime * uvSpeed;
            if (offset > 1f) offset -= 1f;
            if (fogMat != null)
                fogMat.mainTextureOffset = new Vector2(offset, 0f);
            yield return null;
        }
    }

    // ==================== 粒子生成与更新 ====================

    private IEnumerator SpawnParticles()
    {
        while (true)
        {
            SpawnOneParticle();
            yield return new WaitForSeconds(particleSpawnInterval);
        }
    }

    private void SpawnOneParticle(bool initial = false)
    {
        if (particleContainer == null || particleSprite == null) return;

        var go = new GameObject("AmbientParticle");
        go.transform.SetParent(particleContainer, false);

        var rect = go.AddComponent<RectTransform>();
        float startY = Random.Range(bottomBound, topBound);
        float x = initial ? Random.Range(destroyX + 50f, spawnX - 50f) : spawnX;
        rect.anchoredPosition = new Vector2(x, startY);
        rect.sizeDelta = Vector2.one * Random.Range(particleMinSize, particleMaxSize);

        var img = go.AddComponent<Image>();
        img.sprite = particleSprite;
        img.raycastTarget = false;
        if (_particleMat != null)
            img.material = _particleMat;
        Color c = _currentColor;
        c.a = initial ? Random.Range(0.3f, 0.8f) : 0f;
        img.color = c;

    #if UNITY_EDITOR
        img.name = $"Particle_{_activeParticles.Count}";
    #endif

        float speed = Random.Range(particleMinSpeed, particleMaxSpeed);
        float driftY = Random.Range(particleMinDriftY, particleMaxDriftY);
        float lifetime = Mathf.Abs(x - destroyX) / speed;

        var data = new ParticleData
        {
            rect = rect,
            image = img,
            velocity = new Vector2(-speed, driftY),
            lifetime = lifetime,
            age = 0f,
            startAlpha = Random.Range(0.3f, 0.8f)
        };

        _activeParticles.Add(data);
        StartCoroutine(UpdateParticle(data));
    }

    private IEnumerator UpdateParticle(ParticleData data)
    {
        while (data.age < data.lifetime && data.rect != null)
        {
            data.age += Time.deltaTime;
            float p = data.age / data.lifetime;

            // 移动
            data.rect.anchoredPosition += data.velocity * Time.deltaTime;

            // 淡入
            float alpha;
            if (p < particleFadeInDuration)
            {
                alpha = Mathf.Lerp(0f, data.startAlpha, p / particleFadeInDuration);
            }
            else if (p > particleFadeOutStart)
            {
                alpha = Mathf.Lerp(data.startAlpha, 0f, (p - particleFadeOutStart) / (1f - particleFadeOutStart));
            }
            else
            {
                alpha = data.startAlpha;
            }

            // 缩放呼吸
            float breathe = 1f + Mathf.Sin(data.age * 2f) * 0.1f;
            data.rect.localScale = Vector3.one * breathe;

            data.image.color = new Color(_currentColor.r, _currentColor.g, _currentColor.b, alpha);

            // 超出边界销毁
            if (data.rect.anchoredPosition.x < destroyX)
                break;

            yield return null;
        }

        _activeParticles.Remove(data);
        if (data.rect != null) Destroy(data.rect.gameObject);
    }

    private void OnDestroy()
    {
        StopAmbience();
        if (_particleMat != null)
            Destroy(_particleMat);
    }
}
