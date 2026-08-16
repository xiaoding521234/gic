using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

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
        [SerializeField] private float 粒子延迟 = 0f;
        [SerializeField] private float 核心过冲 = 1.08f;

        [Header("持续状态")]
        [SerializeField] private float 脉冲速度 = 3f;
        [SerializeField, Range(0f, 0.5f)] private float 脉冲幅度 = 0.3f;

        [Header("粒子")]
        [SerializeField] private int 粒子数量 = 30;
        [SerializeField] private float 粒子速度 = 1200f;
        [SerializeField] private float 粒子大小 = 25f;
        [SerializeField] private float 粒子扩散 = 1.5f;
        [SerializeField] private float 粒子时长 = 0.8f;
        [SerializeField] private float 粒子不透明度 = 1f;

        [Header("地面闪光")]
        [SerializeField] private float 地面闪光宽度 = 300f;
        [SerializeField] private float 地面闪光高度 = 40f;

        [Header("材质")]
        [SerializeField] private Material 加法材质;

        private Coroutine _playSequenceCoroutine;
        private readonly List<Coroutine> _activeCoroutines = new();

        #endregion

        #region 运行时

        private RectTransform _rectTransform;
        private Material _additiveMat;

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
                RectTransform glow = CreateImage($"Glow_{i}", _sharedBeamSprite, new Vector2(layerWidth, 光柱高度));
                glow.pivot = new Vector2(0.5f, 0.5f);
                glow.anchoredPosition = Vector2.zero;
                glow.localScale = new Vector3(1f, 0f, 1f);
                glowImgs.Add(glow.GetComponent<Image>());
            }

            // 核心光束（pivot 中心，从中心向上下双向生长）
            RectTransform coreBeam = CreateImage("CoreBeam", _sharedBeamSprite, new Vector2(光柱宽度, 光柱高度));
            coreBeam.pivot = new Vector2(0.5f, 0.5f);
            coreBeam.anchoredPosition = Vector2.zero;
            coreBeam.localScale = new Vector3(1f, 0f, 1f);
            Image coreBeamImg = coreBeam.GetComponent<Image>();
            coreBeamImg.color = Color.white;

            // --- 启动各动画 ---

            Track(AnimateGroundFlash(groundFlashImg, color, 爆发强度));
            _sustainedBeams.Add(coreBeamImg);
            Track(AnimateBeam(coreBeamImg, Color.white, 爆发强度, 1f, 0f, true));

            for (int i = 0; i < glowImgs.Count; i++)
            {
                float burstAlpha = 0.6f / (i * 0.5f + 1f) * 爆发强度;
                float sustainAlpha = 0.5f / (i * 0.5f + 1f);
                float delay = (i + 1) * 0.03f;
                _sustainedBeams.Add(glowImgs[i]);
                Track(AnimateBeam(glowImgs[i], color, burstAlpha, sustainAlpha, delay, false));
            }

            // 等待光柱接近顶部后释放粒子
            yield return new WaitForSecondsRealtime(粒子延迟);
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

        #endregion
    }
}
