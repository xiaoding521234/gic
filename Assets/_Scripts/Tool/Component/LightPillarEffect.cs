using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using GIC.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
namespace GIC.Tool
{


    /// <summary>
    /// UGUI 竖直光柱爆发特效
    /// 从中心向上下双向爆发出一道竖直光柱，包含核心光束、外层光晕、中心闪光和双向飞溅粒子
    /// 爆发后光柱持续保持并带轻微脉冲，适合抽卡场景停留欣赏
    ///
    /// 用法：
    /// 1. 在 Canvas 下创建空 GameObject
    /// 2. 挂载本组件
    /// 3. 调用 Play() 或 Play(Color) 播放特效
    /// 4. 调用 Stop() 淡出并清理
    ///
    /// 注意：依赖 Assets/Shaders/UIAdditive.shader（加法混合）。
    /// 若在 Build 中使用，请将 "UI/Additive" shader 添加到
    /// ProjectSettings → Graphics → Always Included Shaders，
    /// 或在 Inspector 中指定 加法材质。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class LightPillarEffect : MonoBehaviour
    {
        #region 配置

        [Header("光柱外观")]
        [SerializeField] private Color 光柱颜色 = new Color(0.957f, 0.263f, 0.212f, 0.957f);
        [SerializeField] private float 光柱宽度 = 200f;
        [SerializeField] private float 光柱高度 = 2000f;
        [SerializeField] private int 光晕层数 = 6;
        [SerializeField] private float 光晕宽度倍数 = 5f;
        [SerializeField, Min(0f)] private float 爆发强度 = 1f;

        [Header("动画时序")]
        [SerializeField] private float 上升时间 = 0.25f;
        [SerializeField] private float 淡出时间 = 0.5f;
        [SerializeField] private float 闪光时间 = 0.3f;
        [SerializeField] private float 粒子延迟 = 0.2f;
        [SerializeField] private float 核心过冲 = 1.08f;

        [Header("持续状态")]
        [SerializeField] private float 脉冲速度 = 3f;
        [SerializeField, Range(0f, 0.5f)] private float 脉冲幅度 = 0.3f;

        [Header("粒子")]
        [SerializeField] private int 粒子数量 = 12;
        [SerializeField] private float 粒子速度 = 500f;
        [SerializeField] private float 粒子大小 = 30f;
        [SerializeField] private float 粒子扩散 = 1f;
        [SerializeField] private float 粒子时长 = 1f;

        [Header("地面闪光")]
        [SerializeField] private float 地面闪光宽度 = 300f;
        [SerializeField] private float 地面闪光高度 = 40f;

        [Header("材质")]
        [SerializeField] private Material 加法材质;

        #endregion

        #region 运行时

        private RectTransform _rectTransform;
        private Material _additiveMat;
        private Texture2D _beamTexture;
        private Texture2D _radialTexture;
        private Sprite _beamSprite;
        private Sprite _radialSprite;

        private readonly List<GameObject> _spawnedObjects = new();
        private readonly List<Image> _sustainedBeams = new();

        #endregion

        #region 生命周期

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            CreateTextures();
            CreateMaterial();
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            ClearSpawned();
            _sustainedBeams.Clear();
        }

        private void OnDestroy()
        {
            if (_additiveMat != null) Destroy(_additiveMat);
            if (_beamTexture != null) Destroy(_beamTexture);
            if (_radialTexture != null) Destroy(_radialTexture);
            if (_beamSprite != null) Destroy(_beamSprite);
            if (_radialSprite != null) Destroy(_radialSprite);
        }

        #endregion

        #region 公开接口

        /// <summary>
        /// 使用 Inspector 中配置的默认颜色播放光柱特效
        /// </summary>
        [ContextMenu("Play")]
        public void Play()
        {
            Play(光柱颜色);
        }

        /// <summary>
        /// 使用指定颜色播放光柱特效
        /// </summary>
        /// <param name="color">光柱主色调</param>
        public void Play(Color color)
        {
            if (!isActiveAndEnabled) return;
            StopAllCoroutines();
            ClearSpawned();
            _sustainedBeams.Clear();
            StartCoroutine(PlaySequence(color));
        }

        /// <summary>
        /// 淡出光柱并清理所有生成元素
        /// </summary>
        [ContextMenu("Stop")]
        public void Stop()
        {
            StopAllCoroutines();
            if (_sustainedBeams.Count > 0)
                StartCoroutine(FadeOutAndClear());
            else
                ClearSpawned();
        }

        #endregion

        #region 动画主流程

        private IEnumerator PlaySequence(Color color)
        {
            // --- 创建视觉元素 ---

            // 地面闪光
            RectTransform groundFlash = CreateImage("GroundFlash", _radialSprite,
                new Vector2(地面闪光宽度, 地面闪光高度));
            groundFlash.pivot = new Vector2(0.5f, 0.5f);
            groundFlash.anchoredPosition = Vector2.zero;
            groundFlash.localScale = Vector3.zero;
            Image groundFlashImg = groundFlash.GetComponent<Image>();
            groundFlashImg.color = new Color(color.r, color.g, color.b, 爆发强度);

            // 外层光晕（多层，逐层加宽）
            var glowImgs = new List<Image>();
            for (int i = 0; i < 光晕层数; i++)
            {
                float layerWidth = 光柱宽度 * (1f + (光晕宽度倍数 - 1f) * (i + 1) / 光晕层数);
                RectTransform glow = CreateImage($"Glow_{i}", _beamSprite, new Vector2(layerWidth, 光柱高度));
                glow.pivot = new Vector2(0.5f, 0.5f);
                glow.anchoredPosition = Vector2.zero;
                glow.localScale = new Vector3(1f, 0f, 1f);
                glowImgs.Add(glow.GetComponent<Image>());
            }

            // 核心光束（pivot 中心，从中心向上下双向生长）
            RectTransform coreBeam = CreateImage("CoreBeam", _beamSprite, new Vector2(光柱宽度, 光柱高度));
            coreBeam.pivot = new Vector2(0.5f, 0.5f);
            coreBeam.anchoredPosition = Vector2.zero;
            coreBeam.localScale = new Vector3(1f, 0f, 1f);
            Image coreBeamImg = coreBeam.GetComponent<Image>();
            coreBeamImg.color = Color.white;

            // --- 启动各动画 ---

            StartCoroutine(AnimateGroundFlash(groundFlashImg, color, 爆发强度));
            _sustainedBeams.Add(coreBeamImg);
            StartCoroutine(AnimateBeam(coreBeamImg, Color.white, 爆发强度, 1f, 0f, true));

            for (int i = 0; i < glowImgs.Count; i++)
            {
                float burstAlpha = 0.4f / (i + 1) * 爆发强度;
                float sustainAlpha = 0.4f / (i + 1);
                float delay = (i + 1) * 0.03f;
                _sustainedBeams.Add(glowImgs[i]);
                StartCoroutine(AnimateBeam(glowImgs[i], color, burstAlpha, sustainAlpha, delay, false));
            }

            // 等待光柱接近顶部后释放粒子
            yield return new WaitForSecondsRealtime(粒子延迟);
            SpawnSparks(color);

            // 粒子自行消亡，光柱持续保持（需调用 Stop() 淡出）
        }

        #endregion

        #region 各部分动画

        /// <summary>
        /// 光柱上升 → 过冲回弹 → 持续脉冲（直到 Stop() 被调用）
        /// </summary>
        private IEnumerator AnimateBeam(Image img, Color color, float burstAlpha, float sustainAlpha, float delay, bool useOvershoot)
        {
            if (delay > 0f)
                yield return new WaitForSecondsRealtime(delay);

            RectTransform rect = img.rectTransform;
            img.color = new Color(color.r, color.g, color.b, burstAlpha);
            rect.localScale = new Vector3(1f, 0f, 1f);

            // 上升
            float t = 0f;
            float targetScale = useOvershoot ? 核心过冲 : 1f;
            while (t < 上升时间)
            {
                t += Time.unscaledDeltaTime;
                float p = t / 上升时间;
                float scaleY = Mathf.LerpUnclamped(0f, targetScale, EaseOutCubic(p));
                // 上升过程中 alpha 从爆发值过渡到持续值
                float alpha = Mathf.Lerp(burstAlpha, sustainAlpha, p);
                img.color = new Color(color.r, color.g, color.b, alpha);
                rect.localScale = new Vector3(1f, scaleY, 1f);
                yield return null;
            }

            // 过冲回弹
            if (useOvershoot && 核心过冲 > 1f)
            {
                t = 0f;
                float settleDur = 0.08f;
                while (t < settleDur)
                {
                    t += Time.unscaledDeltaTime;
                    float p = t / settleDur;
                    float scaleY = Mathf.Lerp(核心过冲, 1f, EaseOutCubic(p));
                    rect.localScale = new Vector3(1f, scaleY, 1f);
                    yield return null;
                }
            }

            rect.localScale = Vector3.one;

            // 持续脉冲（无限循环，由 StopAllCoroutines 中断）
            float pulseT = 0f;
            while (true)
            {
                pulseT += Time.unscaledDeltaTime;
                float pulse = 1f + Mathf.Sin(pulseT * 脉冲速度) * 脉冲幅度;
                img.color = new Color(color.r, color.g, color.b, sustainAlpha * pulse);
                yield return null;
            }
        }

        /// <summary>
        /// 地面闪光：从中心向外扩散并淡出
        /// </summary>
        private IEnumerator AnimateGroundFlash(Image img, Color color, float maxAlpha)
        {
            img.transform.localScale = Vector3.zero;

            float t = 0f;
            while (t < 闪光时间)
            {
                t += Time.unscaledDeltaTime;
                float p = t / 闪光时间;
                float scale = Mathf.LerpUnclamped(0f, 1.6f, EaseOutCubic(p));
                float alpha = Mathf.Lerp(maxAlpha, 0f, p * p);
                img.transform.localScale = Vector3.one * scale;
                img.color = new Color(color.r, color.g, color.b, alpha);
                yield return null;
            }

            img.gameObject.SetActive(false);
        }

        /// <summary>
        /// 淡出所有持续光柱，完成后清理
        /// </summary>
        private IEnumerator FadeOutAndClear()
        {
            var startAlphas = new float[_sustainedBeams.Count];
            var startColors = new Color[_sustainedBeams.Count];
            for (int i = 0; i < _sustainedBeams.Count; i++)
            {
                if (_sustainedBeams[i] != null)
                {
                    startAlphas[i] = _sustainedBeams[i].color.a;
                    startColors[i] = _sustainedBeams[i].color;
                }
                else
                {
                    startAlphas[i] = 0f;
                    startColors[i] = Color.white;
                }
            }

            float t = 0f;
            while (t < 淡出时间)
            {
                t += Time.unscaledDeltaTime;
                float p = t / 淡出时间;
                for (int i = 0; i < _sustainedBeams.Count; i++)
                {
                    if (_sustainedBeams[i] != null)
                    {
                        var c = startColors[i];
                        c.a = Mathf.Lerp(startAlphas[i], 0f, p * p);
                        _sustainedBeams[i].color = c;
                    }
                }
                yield return null;
            }

            _sustainedBeams.Clear();
            ClearSpawned();
        }

        #endregion

        #region 粒子

        private void SpawnSparks(Color color)
        {
            for (int i = 0; i < 粒子数量; i++)
            {
                var go = new GameObject("Spark");
                go.transform.SetParent(_rectTransform, false);

                var img = go.AddComponent<Image>();
                img.sprite = _radialSprite;
                img.raycastTarget = false;
                img.material = _additiveMat;

                RectTransform rect = go.transform as RectTransform;
                rect.sizeDelta = Vector2.one * 粒子大小 * Random.Range(0.6f, 1.4f);
                rect.anchoredPosition = Vector2.zero;
                rect.localScale = Vector3.zero;

                // 方向：上下双向飞溅，带少量水平扩散
                float angle = Random.Range(-粒子扩散, 粒子扩散) * Mathf.PI;
                float ySign = Random.value > 0.5f ? 1f : -1f;
                Vector2 dir = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle) * ySign).normalized;
                float speed = 粒子速度 * Random.Range(0.6f, 1.2f);
                float lifetime = 粒子时长 * Random.Range(0.7f, 1.3f);
                float delay = Random.Range(0f, 0.1f);

                img.color = new Color(color.r, color.g, color.b, 0f);
                StartCoroutine(AnimateSpark(rect, img, dir, speed, lifetime, delay, color, 爆发强度));
                _spawnedObjects.Add(go);
            }
        }

        private IEnumerator AnimateSpark(RectTransform rect, Image img, Vector2 dir, float speed,
            float lifetime, float delay, Color color, float maxAlpha)
        {
            if (delay > 0f)
                yield return new WaitForSecondsRealtime(delay);

            float t = 0f;
            Vector2 pos = Vector2.zero;

            while (t < lifetime && rect != null)
            {
                t += Time.unscaledDeltaTime;
                float p = t / lifetime;

                pos += dir * speed * Time.unscaledDeltaTime;
                // 减速模拟重力
                speed = Mathf.Max(0f, speed - 350f * Time.unscaledDeltaTime);
                rect.anchoredPosition = pos;

                // 先放大再缩小
                float scaleP = p < 0.15f
                    ? Mathf.Lerp(0f, 1f, p / 0.15f)
                    : Mathf.Lerp(1f, 0f, (p - 0.15f) / 0.85f);
                rect.localScale = Vector3.one * scaleP;

                img.color = new Color(color.r, color.g, color.b, Mathf.Lerp(maxAlpha, 0f, p * p));
                yield return null;
            }

            if (rect != null)
                rect.gameObject.SetActive(false);
        }

        #endregion

        #region 辅助方法

        private RectTransform CreateImage(string name, Sprite sprite, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_rectTransform, false);

            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            img.material = _additiveMat;

            RectTransform rect = go.transform as RectTransform;
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;

            _spawnedObjects.Add(go);
            return rect;
        }

        private void ClearSpawned()
        {
            foreach (var go in _spawnedObjects)
            {
                if (go != null) Destroy(go);
            }
            _spawnedObjects.Clear();
        }

        private static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);

        #endregion

        #region 资源创建

        private void CreateMaterial()
        {
            if (加法材质 != null)
            {
                _additiveMat = new Material(加法材质);
                return;
            }

            Shader shader = Shader.Find("UI/Additive");
            if (shader == null)
                shader = Shader.Find("UI/Default");
            if (shader != null)
                _additiveMat = new Material(shader);
        }

        private void CreateTextures()
        {
            // 光柱纹理：水平高斯衰减 + 竖向中心亮两端暗（上下对称）
            int w = 32, h = 128;
            _beamTexture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            _beamTexture.filterMode = FilterMode.Bilinear;
            _beamTexture.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < h; y++)
            {
                float vy = (float)y / (h - 1);
                float vAlpha = 1f - Mathf.Abs(vy - 0.5f) * 2f;
                vAlpha = Mathf.Pow(vAlpha, 0.6f);
                for (int x = 0; x < w; x++)
                {
                    float vx = (x - w * 0.5f) / (w * 0.5f);
                    float hAlpha = Mathf.Exp(-vx * vx * 3f);
                    float alpha = hAlpha * vAlpha;
                    _beamTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            _beamTexture.Apply();
            _beamSprite = Sprite.Create(_beamTexture,
                new Rect(0, 0, w, h),
                new Vector2(w * 0.5f, h * 0.5f), 100f);

            // 径向纹理：圆形渐变（用于地面闪光、粒子）
            int s = 64;
            _radialTexture = new Texture2D(s, s, TextureFormat.RGBA32, false);
            _radialTexture.filterMode = FilterMode.Bilinear;
            _radialTexture.wrapMode = TextureWrapMode.Clamp;
            float center = (s - 1) * 0.5f;
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float dx = (x - center) / center;
                    float dy = (y - center) / center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(1f - dist);
                    alpha = Mathf.Pow(alpha, 1.5f);
                    _radialTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            _radialTexture.Apply();
            _radialSprite = Sprite.Create(_radialTexture,
                new Rect(0, 0, s, s),
                new Vector2(s * 0.5f, s * 0.5f), 100f);
        }

        #endregion
    }

}


