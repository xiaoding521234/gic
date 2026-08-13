using UnityEngine;
using UnityEngine.UI;
using GIC.Data;

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
        private Vector4 _maskUV = new Vector4(0, 0, 1, 1);

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
        /// maskSprite 用于让发光遵循卡面圆角形状（如 cardBack.sprite）。
        /// </summary>
        public static CardGlowOverlay Create(RectTransform parent, Sprite maskSprite = null)
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
            overlay._glowMat.SetColor("_GlowColor", StarVisualConfig.EncounterGlowColor);
            overlay._glowMat.SetFloat("_Intensity", 0f);

            // 传入卡面 Sprite 做 alpha mask，使发光遵循圆角形状
            if (maskSprite != null)
            {
                overlay._glowMat.SetTexture("_MainTex", maskSprite.texture);
                var uv = UV4FromSprite(maskSprite);
                overlay._maskUV = uv;
            }

            overlay.material = overlay._glowMat;
            overlay.SetAllDirty();

            return overlay;
        }

        /// <summary>
        /// 从 Sprite 计算 UV 矩形（处理 tight/packed sprite 的 UV 范围）
        /// </summary>
        private static Vector4 UV4FromSprite(Sprite sprite)
        {
            if (sprite.packed)
            {
                var uvMin = sprite.textureRectOffset;
                var tex = sprite.texture;
                return new Vector4(
                    sprite.textureRect.xMin / tex.width,
                    sprite.textureRect.yMin / tex.height,
                    sprite.textureRect.xMax / tex.width,
                    sprite.textureRect.yMax / tex.height);
            }
            return new Vector4(0, 0, 1, 1);
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
            var uv = _maskUV;
            vh.AddVert(new Vector3(rect.xMin, rect.yMin), color, new Vector2(uv.x, uv.y));
            vh.AddVert(new Vector3(rect.xMin, rect.yMax), color, new Vector2(uv.x, uv.w));
            vh.AddVert(new Vector3(rect.xMax, rect.yMax), color, new Vector2(uv.z, uv.w));
            vh.AddVert(new Vector3(rect.xMax, rect.yMin), color, new Vector2(uv.z, uv.y));
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
