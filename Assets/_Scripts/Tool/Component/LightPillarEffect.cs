using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;

namespace GIC.Tool
{
    /// <summary>
    /// UGUI 竖直光柱爆发特效（主组件 — 配置/生命周期/编排）
    /// 从中心向上下双向爆发出一道竖直光柱，包含核心光束、外层光晕、中心闪光和双向飞溅粒子
    /// 爆发后光柱持续保持并带轻微脉冲，适合抽卡场景停留欣赏
    ///
    /// 用法：
    /// 1. 在 Canvas 下创建空 GameObject
    /// 2. 挂载本组件
    /// 3. 调用 Play() 或 Play(Color) 播放特效
    /// 4. 调用 Stop() 淡出并清理
    ///
    /// 拆分文件：Animations（光柱/地面闪光动画）、Sparks（飞溅粒子）、Resources（共享纹理与材质）
    ///
    /// 注意：依赖 Assets/Shaders/UIAdditive.shader（加法混合）。
    /// 若在 Build 中使用，请将 "UI/Additive" shader 添加到
    /// ProjectSettings → Graphics → Always Included Shaders，
    /// 或在 Inspector 中指定 加法材质。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public partial class LightPillarEffect : MonoBehaviour
    {
        #region config

        [Header("光柱外观")]
        [InspectorName("光柱颜色")]
        [SerializeField] private Color pillarColor = new Color(0.957f, 0.263f, 0.212f, 0.957f);
        [InspectorName("光柱宽度")]
        [SerializeField] private float pillarWidth = 200f;
        [InspectorName("光柱高度")]
        [SerializeField] private float pillarHeight = 2000f;
        [InspectorName("光晕层数")]
        [SerializeField] private int glowLayers = 6;
        [InspectorName("光晕宽度倍数")]
        [SerializeField] private float glowWidthMult = 5f;
        [InspectorName("爆发强度")]
        [SerializeField, Min(0f)] private float burstIntensity = 1f;

        [Header("动画时序")]
        [InspectorName("上升时间")]
        [SerializeField] private float riseTime = 0.25f;
        [InspectorName("淡出时间")]
        [SerializeField] private float fadeOutTime = 0.5f;
        [InspectorName("闪光时间")]
        [SerializeField] private float flashTime = 0.3f;
        [InspectorName("粒子延迟")]
        [SerializeField] private float particleDelay = 0f;
        [InspectorName("核心过冲")]
        [SerializeField] private float coreOvershoot = 1.08f;

        [Header("持续状态")]
        [InspectorName("脉冲速度")]
        [SerializeField] private float pulseSpeed = 3f;
        [InspectorName("脉冲幅度")]
        [SerializeField, Range(0f, 0.5f)] private float pulseAmp = 0.3f;

        [Header("粒子")]
        [InspectorName("粒子数量")]
        [SerializeField] private int particleCount = 30;
        [InspectorName("粒子速度")]
        [SerializeField] private float particleSpeed = 1200f;
        [InspectorName("粒子大小")]
        [SerializeField] private float particleSize = 25f;
        [InspectorName("粒子扩散")]
        [SerializeField] private float particleSpread = 1.5f;
        [InspectorName("粒子时长")]
        [SerializeField] private float particleDuration = 0.8f;
        [InspectorName("粒子不透明度")]
        [SerializeField] private float particleOpacity = 1f;

        [Header("地面闪光")]
        [InspectorName("地面闪光宽度")]
        [SerializeField] private float groundFlashWidth = 300f;
        [InspectorName("地面闪光高度")]
        [SerializeField] private float groundFlashHeight = 40f;

        [Header("材质")]
        [InspectorName("加法材质")]
        [SerializeField] private Material addMat;

        private Coroutine _playSequenceCoroutine;
        private readonly List<Coroutine> _activeCoroutines = new();

        #endregion

        #region runtime

        private RectTransform _rectTransform;
        private Material _additiveMat;

        private readonly List<GameObject> _spawnedObjects = new();
        private readonly List<Image> _sustainedBeams = new();

        #endregion

        #region lifecycle

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
        }

        #endregion

        #region openInterface

        /// <summary>
        /// 使用 Inspector 中配置的默认颜色播放光柱特效
        /// </summary>
        [ContextMenu("Play")]
        public void Play()
        {
            Play(pillarColor);
        }

        /// <summary>
        /// 使用指定颜色播放光柱特效
        /// </summary>
        /// <param name="color">光柱主色调</param>
        public void Play(Color color)
        {
            if (!isActiveAndEnabled) return;
            // 停掉自己的所有协程
            foreach (var c in _activeCoroutines)
            {
                if (c != null) StopCoroutine(c);
            }
            _activeCoroutines.Clear();
            _sustainedBeams.Clear();
            ClearSpawned();
            _playSequenceCoroutine = Track(PlaySequence(color));
        }

        /// <summary>
        /// 淡出光柱并清理所有生成元素
        /// </summary>
        [ContextMenu("Stop")]
        public void Stop()
        {
            // 停掉自己的所有协程
            foreach (var c in _activeCoroutines)
            {
                if (c != null) StopCoroutine(c);
            }
            _activeCoroutines.Clear();
            _playSequenceCoroutine = null;

            if (_sustainedBeams.Count > 0)
                _activeCoroutines.Add(StartCoroutine(FadeOutAndClear()));
            else
                ClearSpawned();
        }

        private Coroutine Track(IEnumerator routine)
        {
            var c = StartCoroutine(routine);
            _activeCoroutines.Add(c);
            return c;
        }

        #endregion

        #region 动画主流程

        private IEnumerator PlaySequence(Color color)
        {
            // --- 创建视觉元素 ---

            // 地面闪光
            RectTransform groundFlash = CreateImage("GroundFlash", _sharedRadialSprite,
                new Vector2(groundFlashWidth, groundFlashHeight));
            groundFlash.pivot = new Vector2(0.5f, 0.5f);
            groundFlash.anchoredPosition = Vector2.zero;
            groundFlash.localScale = Vector3.zero;
            Image groundFlashImg = groundFlash.GetComponent<Image>();
            groundFlashImg.color = new Color(color.r, color.g, color.b, burstIntensity);

            // 外层光晕（多层，逐层加宽）
            var glowImgs = new List<Image>();
            for (int i = 0; i < glowLayers; i++)
            {
                float layerWidth = pillarWidth * (1f + (glowWidthMult - 1f) * (i + 1) / glowLayers);
                RectTransform glow = CreateImage($"Glow_{i}", _sharedBeamSprite, new Vector2(layerWidth, pillarHeight));
                glow.pivot = new Vector2(0.5f, 0.5f);
                glow.anchoredPosition = Vector2.zero;
                glow.localScale = new Vector3(1f, 0f, 1f);
                glowImgs.Add(glow.GetComponent<Image>());
            }

            // 核心光束（pivot 中心，从中心向上下双向生长）
            RectTransform coreBeam = CreateImage("CoreBeam", _sharedBeamSprite, new Vector2(pillarWidth, pillarHeight));
            coreBeam.pivot = new Vector2(0.5f, 0.5f);
            coreBeam.anchoredPosition = Vector2.zero;
            coreBeam.localScale = new Vector3(1f, 0f, 1f);
            Image coreBeamImg = coreBeam.GetComponent<Image>();
            coreBeamImg.color = Color.white;

            // --- 启动各动画 ---

            Track(AnimateGroundFlash(groundFlashImg, color, burstIntensity));
            _sustainedBeams.Add(coreBeamImg);
            Track(AnimateBeam(coreBeamImg, Color.white, burstIntensity, 1f, 0f, true));

            for (int i = 0; i < glowImgs.Count; i++)
            {
                float burstAlpha = 0.6f / (i * 0.5f + 1f) * burstIntensity;
                float sustainAlpha = 0.5f / (i * 0.5f + 1f);
                float delay = (i + 1) * 0.03f;
                _sustainedBeams.Add(glowImgs[i]);
                Track(AnimateBeam(glowImgs[i], color, burstAlpha, sustainAlpha, delay, false));
            }

            // 等待光柱接近顶部后释放粒子
            yield return new WaitForSecondsRealtime(particleDelay);
            SpawnSparks(color);

            // 粒子自行消亡，光柱持续保持（需调用 Stop() 淡出）
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
            while (t < fadeOutTime)
            {
                t += Time.unscaledDeltaTime;
                float p = t / fadeOutTime;
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

        #region helperMethod

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

        #endregion
    }
}
