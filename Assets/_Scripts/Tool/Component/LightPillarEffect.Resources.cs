using UnityEngine;

namespace GIC.Tool
{
    /// <summary>
    /// 光柱特效 — 共享资源（程序化纹理/Sprite）与实例材质创建
    /// </summary>
    public partial class LightPillarEffect
    {
        // 纹理和 Sprite 是无状态纯像素数据，所有实例共享，避免每次射击重建
        private static Texture2D _sharedBeamTexture;
        private static Sprite _sharedBeamSprite;
        private static Texture2D _sharedRadialTexture;
        private static Sprite _sharedRadialSprite;

        #region resCreate

        private void CreateMaterial()
        {
            if (addMat != null)
            {
                _additiveMat = new Material(addMat);
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
            if (_sharedBeamTexture != null) return;

            // 光柱纹理：水平高斯衰减 + 竖向中心亮两端暗（上下对称）
            int w = 32, h = 128;
            _sharedBeamTexture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            _sharedBeamTexture.filterMode = FilterMode.Bilinear;
            _sharedBeamTexture.wrapMode = TextureWrapMode.Clamp;
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
                    _sharedBeamTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            _sharedBeamTexture.Apply();
            _sharedBeamSprite = Sprite.Create(_sharedBeamTexture,
                new Rect(0, 0, w, h),
                new Vector2(w * 0.5f, h * 0.5f), 100f);

            // 径向纹理：圆形渐变（用于地面闪光、粒子）
            int s = 64;
            _sharedRadialTexture = new Texture2D(s, s, TextureFormat.RGBA32, false);
            _sharedRadialTexture.filterMode = FilterMode.Bilinear;
            _sharedRadialTexture.wrapMode = TextureWrapMode.Clamp;
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
                    _sharedRadialTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            _sharedRadialTexture.Apply();
            _sharedRadialSprite = Sprite.Create(_sharedRadialTexture,
                new Rect(0, 0, s, s),
                new Vector2(s * 0.5f, s * 0.5f), 100f);
        }

        private static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);

        #endregion
    }
}
