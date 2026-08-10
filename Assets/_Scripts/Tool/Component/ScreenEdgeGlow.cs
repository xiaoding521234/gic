using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace GIC.Tool
{
    /// <summary>
    /// 屏幕边缘泛光 — 全屏自定义 Shader（UI/ScreenEdgeGlow）
    /// 片段着色器内按 UV 到最近边缘距离计算 alpha，中心完全透明
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(Image))]
    public class ScreenEdgeGlow : MonoBehaviour
    {
        [Header("泛光参数")]
        [SerializeField] private float maxAlpha = 1.0f;
        [SerializeField] private float duration = 0.8f;
        [SerializeField] private float baseEdgeWidth = 0.08f;
        [SerializeField] private float edgeWidthPerStar = 0.04f;

        private static Shader _glowShader;

        private Material _mat;
        private Image _image;
        private Coroutine _glowCoroutine;

        private void Awake()
        {
            _image = GetComponent<Image>();
            _image.raycastTarget = false;

            if (_glowShader == null)
                _glowShader = Shader.Find("UI/ScreenEdgeGlow");
            if (_glowShader != null)
            {
                _mat = new Material(_glowShader);
                _mat.SetColor("_Color", Color.clear);
                _image.material = _mat;
            }
        }

        private void OnDisable()
        {
            if (_glowCoroutine != null)
            {
                StopCoroutine(_glowCoroutine);
                _glowCoroutine = null;
            }
            if (_mat != null)
                _mat.SetColor("_Color", Color.clear);
        }

        private void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
        }

        /// <summary>
        /// 播放一次泛光，颜色和强度取决于星级
        /// </summary>
        /// <param name="color">星级颜色</param>
        /// <param name="starLevel">星级 (1-5)，影响 alpha 峰值和边缘延伸距离</param>
        public void Play(Color color, int starLevel)
        {
            if (!isActiveAndEnabled || _mat == null) return;
            if (_glowCoroutine != null) StopCoroutine(_glowCoroutine);
            _glowCoroutine = StartCoroutine(GlowCoroutine(color, starLevel));
        }

        private IEnumerator GlowCoroutine(Color color, int starLevel)
        {
            float peakAlpha = Mathf.Min(maxAlpha * (0.3f + starLevel * 0.18f), 1f);
            float edgeWidth = baseEdgeWidth + (starLevel - 1) * edgeWidthPerStar;
            _mat.SetFloat("_EdgeWidth", edgeWidth);

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float alpha = peakAlpha * (1f - t * t);
                _mat.SetColor("_Color", new Color(color.r, color.g, color.b, alpha));
                yield return null;
            }
            _mat.SetColor("_Color", Color.clear);
            _glowCoroutine = null;
        }
    }
}
