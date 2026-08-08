using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace GIC.Battle
{
    /// <summary>
    /// 卡片光带特效 — 从左下往右上的光带扫过卡面
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public class CardLightBandEffect : MonoBehaviour
    {
        [Header("光带设置")]
        [SerializeField] private float 持续时间 = 0.3f;
        [SerializeField] private Color 光带颜色 = new Color(1f, 0.95f, 0.6f, 1f);
        [SerializeField] private float 光带宽度 = 0.15f;

        private static Shader _bandShader;

        private LightBandGraphic _bandGraphic;
        private Material _bandMaterial;
        private Coroutine _coroutine;

        private static Shader BandShader
        {
            get
            {
                if (_bandShader == null)
                    _bandShader = Shader.Find("UI/CardLightBand");
                return _bandShader;
            }
        }

        /// <summary>
        /// 播放光带扫过特效（正向：左下→右上）
        /// </summary>
        public void Play()
        {
            PlayBand(true, 光带颜色);
        }

        /// <summary>
        /// 播放光带扫过特效（正向），指定颜色
        /// </summary>
        public void Play(Color color)
        {
            PlayBand(true, color);
        }

        /// <summary>
        /// 播放光带扫过特效（反向：右上→左下）
        /// </summary>
        public void PlayReverse()
        {
            PlayBand(false, 光带颜色);
        }

        private void PlayBand(bool forward, Color color)
        {
            if (BandShader == null)
            {
                Debug.LogError("[CardLightBandEffect] 找不到 Shader: UI/CardLightBand");
                return;
            }

            if (_coroutine != null)
                StopCoroutine(_coroutine);

            _coroutine = StartCoroutine(BandCoroutine(forward, color));
        }

        private IEnumerator BandCoroutine(bool forward, Color color)
        {
            CreateBandGraphic(color);

            float elapsed = 0f;
            const float startProgress = -0.3f;
            const float endProgress = 1.3f;

            while (elapsed < 持续时间)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / 持续时间;
                float eased = t * t * (3f - 2f * t);
                float progress = forward
                    ? Mathf.Lerp(startProgress, endProgress, eased)
                    : Mathf.Lerp(endProgress, startProgress, eased);
                _bandMaterial.SetFloat("_Progress", progress);
                yield return null;
            }

            CleanupBandGraphic();
            _coroutine = null;
        }

        private void CreateBandGraphic(Color color)
        {
            if (_bandGraphic != null) return;

            GameObject obj = new GameObject("LightBandEffect", typeof(RectTransform), typeof(CanvasRenderer));
            obj.layer = gameObject.layer;
            obj.transform.SetParent(transform, false);

            RectTransform rt = obj.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.SetAsLastSibling();

            _bandGraphic = obj.AddComponent<LightBandGraphic>();
            _bandGraphic.color = Color.white;
            _bandGraphic.raycastTarget = false;

            _bandMaterial = new Material(BandShader);
            _bandMaterial.SetColor("_GlowColor", color);
            _bandMaterial.SetFloat("_BandWidth", 光带宽度);
            _bandMaterial.SetFloat("_Progress", -0.3f);
            _bandGraphic.material = _bandMaterial;
            _bandGraphic.SetAllDirty();
        }

        private void CleanupBandGraphic()
        {
            if (_bandGraphic != null)
            {
                Destroy(_bandGraphic.gameObject);
                _bandGraphic = null;
            }
            if (_bandMaterial != null)
            {
                Destroy(_bandMaterial);
                _bandMaterial = null;
            }
        }

        private void OnDisable()
        {
            if (_coroutine != null)
            {
                StopCoroutine(_coroutine);
                _coroutine = null;
            }
            CleanupBandGraphic();
        }

        private void OnDestroy()
        {
            CleanupBandGraphic();
        }
    }

    /// <summary>
    /// 自定义 Graphic，直接生成全屏 quad，不依赖 Sprite
    /// </summary>
    public class LightBandGraphic : Graphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect rect = rectTransform.rect;
            float x0 = rect.xMin;
            float y0 = rect.yMin;
            float x1 = rect.xMax;
            float y1 = rect.yMax;

            // UV 0,0 → 1,1，shader 用 UV 计算光带位置
            vh.AddVert(new Vector3(x0, y0), color, new Vector2(0, 0));
            vh.AddVert(new Vector3(x0, y1), color, new Vector2(0, 1));
            vh.AddVert(new Vector3(x1, y1), color, new Vector2(1, 1));
            vh.AddVert(new Vector3(x1, y0), color, new Vector2(1, 0));

            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(0, 2, 3);
        }
    }
}
