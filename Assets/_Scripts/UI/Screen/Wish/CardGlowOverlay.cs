using UnityEngine;
using UnityEngine.UI;

namespace GIC.UI
{
    /// <summary>
    /// 卡片发光叠加层 — Additive 混合，不修改原始 Image 颜色。
    /// 用 SetGlow 控制颜色和亮度，Destroy GameObject 即可清理。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public class CardGlowOverlay : Graphic
    {
        private static Shader _shader;

        private Material _glowMat;

        private static Shader GlowShader
        {
            get
            {
                if (_shader == null)
                    _shader = Shader.Find("UI/CardGlowOverlay");
                return _shader;
            }
        }

        /// <summary>
        /// 在 parent 下创建发光叠加层，自动铺满父级矩形。
        /// </summary>
        public static CardGlowOverlay Create(RectTransform parent)
        {
            if (GlowShader == null)
            {
                Debug.LogError("[CardGlowOverlay] 找不到 Shader: UI/CardGlowOverlay");
                return null;
            }

            var obj = new GameObject("GlowOverlay", typeof(RectTransform), typeof(CanvasRenderer));
            obj.layer = parent.gameObject.layer;
            obj.transform.SetParent(parent, false);

            var rt = obj.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.SetAsLastSibling();

            var overlay = obj.AddComponent<CardGlowOverlay>();
            overlay.color = Color.white;
            overlay.raycastTarget = false;

            overlay._glowMat = new Material(GlowShader);
            overlay._glowMat.SetColor("_GlowColor", new Color(1f, 0.95f, 0.6f, 1f));
            overlay._glowMat.SetFloat("_Intensity", 0f);
            overlay.material = overlay._glowMat;
            overlay.SetAllDirty();

            return overlay;
        }

        /// <summary>
        /// 设置发光颜色和亮度（0=透明，1=标准亮度，>1=更亮）。
        /// </summary>
        public void SetGlow(Color color, float intensity)
        {
            if (_glowMat != null)
            {
                _glowMat.SetColor("_GlowColor", color);
                _glowMat.SetFloat("_Intensity", intensity);
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = rectTransform.rect;
            vh.AddVert(new Vector3(rect.xMin, rect.yMin), color, Vector2.zero);
            vh.AddVert(new Vector3(rect.xMin, rect.yMax), color, new Vector2(0, 1));
            vh.AddVert(new Vector3(rect.xMax, rect.yMax), color, Vector2.one);
            vh.AddVert(new Vector3(rect.xMax, rect.yMin), color, new Vector2(1, 0));
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(0, 2, 3);
        }

        protected override void OnDestroy()
        {
            if (_glowMat != null)
            {
                Destroy(_glowMat);
                _glowMat = null;
            }
            base.OnDestroy();
        }
    }
}
