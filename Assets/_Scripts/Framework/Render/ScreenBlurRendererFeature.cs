using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Framework
{


    public class ScreenBlurRendererFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class BlurSettings
        {
            [Range(0.1f, 1f)] public float downsample = 0.5f;
            [Range(1f, 30f)]  public float blurRadius  = 5f;
            [Range(1, 8)]     public int   iterations  = 4;
            public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        }

        public BlurSettings settings = new BlurSettings();
        [SerializeField] private Shader blurShader;
        private ScreenBlurPass _blurPass;

        public override void Create()
        {
            _blurPass = new ScreenBlurPass(settings, blurShader)
            {
                renderPassEvent = settings.renderPassEvent
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType == CameraType.Game)
                renderer.EnqueuePass(_blurPass);
        }

        protected override void Dispose(bool disposing)
        {
            _blurPass?.Dispose();
        }
    }

    public class ScreenBlurPass : ScriptableRenderPass
    {
        private readonly ScreenBlurRendererFeature.BlurSettings _settings;
        private Material _blurMaterial;
        private MaterialPropertyBlock _mpb;
        private RTHandle _blurRT;
        private RTHandle _tempRT;
        private int _rtWidth;
        private int _rtHeight;

        private static readonly int BlitTextureID     = Shader.PropertyToID("_BlitTexture");
        private static readonly int BlitTexelSizeID   = Shader.PropertyToID("_BlitTexture_TexelSize");
        private static readonly int BlurSizeID        = Shader.PropertyToID("_BlurSize");
        private static readonly int ScreenBlurTexID   = Shader.PropertyToID("_ScreenBlurTex");

        public ScreenBlurPass(ScreenBlurRendererFeature.BlurSettings settings, Shader blurShader)
        {
            _settings = settings;
            var shader = blurShader != null ? blurShader : Shader.Find("Hidden/ScreenBlur");
            _blurMaterial = CoreUtils.CreateEngineMaterial(shader);
            _mpb = new MaterialPropertyBlock();
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;

            int w = Mathf.Max(2, Mathf.RoundToInt(desc.width  * _settings.downsample));
            int h = Mathf.Max(2, Mathf.RoundToInt(desc.height * _settings.downsample));

            if (w != _rtWidth || h != _rtHeight)
            {
                _rtWidth = w;
                _rtHeight = h;
                _blurRT?.Release();
                _tempRT?.Release();
                _blurRT = RTHandles.Alloc(w, h, 1, DepthBits.None, desc.graphicsFormat, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_ScreenBlurRT");
                _tempRT = RTHandles.Alloc(w, h, 1, DepthBits.None, desc.graphicsFormat, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_ScreenBlurTemp");
            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_blurMaterial == null) return;

            var source = renderingData.cameraData.renderer.cameraColorTargetHandle;
            if (source == null || source.rt == null) return;

            CommandBuffer cmd = CommandBufferPool.Get("ScreenBlur");

            // 1) 降采样：相机画面 → _blurRT
            Blitter.BlitCameraTexture(cmd, source, _blurRT, new Vector4(1f, 1f, 0f, 0f), 0f, false);

            // 2) Kawase 模糊迭代：每次偏移递增，ping-pong
            for (int i = 0; i < _settings.iterations; i++)
            {
                // Kawase 偏移 = (i + 0.5)，由 blurRadius 缩放
                float offset = (i + 0.5f) * (_settings.blurRadius / _settings.iterations);

                // _blurRT → _tempRT
                DrawKawase(cmd, _blurRT, _tempRT, offset);
                // _tempRT → _blurRT
                DrawKawase(cmd, _tempRT, _blurRT, offset);
            }

            // 3) 写入全局纹理
            cmd.SetGlobalTexture(ScreenBlurTexID, _blurRT);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private void DrawKawase(CommandBuffer cmd, RTHandle src, RTHandle dest, float offset)
        {
            _mpb.Clear();
            _mpb.SetTexture(BlitTextureID, src);
            _mpb.SetVector(BlitTexelSizeID, new Vector4(1f / _rtWidth, 1f / _rtHeight, _rtWidth, _rtHeight));
            _mpb.SetFloat(BlurSizeID, offset);

            cmd.SetRenderTarget(dest, 0, CubemapFace.Unknown, 0);
            cmd.SetViewport(new Rect(0, 0, _rtWidth, _rtHeight));
            cmd.DrawProcedural(Matrix4x4.identity, _blurMaterial, 0, MeshTopology.Quads, 4, 1, _mpb);
        }

        public void Dispose()
        {
            _blurRT?.Release();
            _tempRT?.Release();
            if (_blurMaterial != null) CoreUtils.Destroy(_blurMaterial);
        }
    }

}

