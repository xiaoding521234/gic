using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
using GIC.Tool;
namespace GIC.UI
{


    /// <summary>
    /// 毛玻璃背景组件。
    /// ScreenBlurRendererFeature 每帧将模糊后的画面写入全局 _ScreenBlurTex，
    /// 此脚本每帧从全局取纹理并设到材质实例上，确保 UI shader 能正确采样。
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class UIBlurCapture : MonoBehaviour
    {
        [Header("叠加颜色")]
        public Color tint = new(0, 0, 0, 0.5f);

        private Material _mat;
        private Image _img;

        private static readonly int ScreenBlurTexID = Shader.PropertyToID("_ScreenBlurTex");
        private static readonly int TintID = Shader.PropertyToID("_Tint");

        private void OnEnable()
        {
            _img = GetComponent<Image>();
            _mat = _img.materialForRendering != null
                ? new Material(_img.materialForRendering)
                : null;

            if (_mat != null)
            {
                _mat.SetColor(TintID, tint);
                _img.material = _mat;
            }
        }

        private void Update()
        {
            if (_mat == null) return;

            // 每帧从全局获取模糊纹理，设到材质实例上
            var blurTex = Shader.GetGlobalTexture(ScreenBlurTexID);
            if (blurTex != null)
                _mat.SetTexture(ScreenBlurTexID, blurTex);
        }

        private void OnDisable()
        {
            if (_mat != null)
            {
                if (Application.isPlaying)
                    Destroy(_mat);
                _mat = null;
            }
        }

        private void OnDestroy()
        {
            if (_mat != null && Application.isPlaying)
                Destroy(_mat);
        }
    }

}


