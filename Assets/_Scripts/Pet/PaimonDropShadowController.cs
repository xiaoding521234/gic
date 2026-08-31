using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace GIC.Pet
{
    /// <summary>
    /// 桌宠屏幕投影阴影（docs/19 §5.10，2026-08-24 主流方案重做）：
    /// 2026 主流桌宠/Web 通用做法 = CSS drop-shadow / DWM 窗口阴影同款管线——
    /// 剪影渲到小 RT → 分离高斯模糊 → 按屏幕像素偏移合成到本体后方。
    /// 旧"双物体法线外扩硬壳"（PaimonDropShadow.shader）只有硬边实心影，已废弃。
    ///
    /// 结构（全部运行时自建，场景只挂本组件）：
    ///   _DropShadow 影子壳（复制本体 SMR 共享骨骼，挂剪影材质，PaimonShadow 层）
    ///   → _ShadowCam（主相机子物体，禁自动渲染，LateUpdate 手动 Render 到剪影 RT）
    ///   → 双向 17-tap 高斯 Blit×两轮 → _ShadowComposite 全屏 Quad（主相机正前方，
    ///   Background 队列最先画，本体 Opaque 后画自然盖住重叠区）。
    /// 零灯光/零阴影贴图/零后处理，与 §5.9 桌宠完全无光照定案兼容。
    /// </summary>
    public class PaimonDropShadowController : MonoBehaviour
    {
        private const string ShadowLayerName = "PaimonShadow";

        [Header("引用")]
        [InspectorName("影子渲染器")]
        [SerializeField] private SkinnedMeshRenderer shadowRenderer; // _DropShadow（缺省按名找）
        [InspectorName("主相机")]
        [SerializeField] private Camera mainCam;                  // 缺省 Camera.main
        [InspectorName("模糊材质")]
        [SerializeField] private Material blurTemplate;              // PaimonShadowBlur.mat（模板，运行时克隆后改参）
        [InspectorName("合成材质")]
        [SerializeField] private Material compositeTemplate;              // PaimonShadowComposite.mat（模板，运行时克隆后改参）

        // 注：不能用 Shader.Find("Hidden/...") 加载内部 shader——构建期无引用会被裁剪致 null（2026-08-24 构建实测）。

        [Header("外观")]
        [InspectorName("阴影颜色")]
        [SerializeField] private Color shadowColor = new Color(0f, 0f, 0f, 0.6f); // Alpha=浓度
        [InspectorName("阴影偏移像素")]
        [SerializeField] private Vector2 shadowOffsetPx = new Vector2(0f, -24f);  // x 向右为正，y 向上为正（负=影子下垂，光源在上方）
        [InspectorName("模糊半径像素")]
        [SerializeField, Range(0f, 60f)] private float blurRadiusPx = 14f;      // 阴影贴图 texel 为单位，越大越柔（内部按 0.66×拆两轮模糊，总量即此值）
        [InspectorName("阴影贴图高度")]
        [SerializeField, Range(64, 2048)] private int shadowTexHeight = 512;       // 宽度随相机 aspect 自动换算
        [InspectorName("启用")]
        [SerializeField] private bool enableShadow = true;

        private Camera shadowCam;
        private RenderTexture rtA, rtB;   // A=剪影/最终模糊结果，B=模糊中转
        private Material blurMat, compositeMat;   // 运行时克隆自 blurTemplate/compositeTemplate
        private Transform quad;
        private int shadowLayer = -1;

        void Awake()
        {
            // 影子壳可含多个 SMR（GI 壳=_DropShadow 节点下 Body+Cloak 两副本；MMD 旧壳=节点自身单 SMR）——
            // 按 _DropShadow 节点收集全部 Renderer 逐个进阴影层；节点缺失时退回序列化引用（2026-08-25 GI 多渲染器改造）
            var shellNode = transform.Find("_DropShadow");
            Renderer[] shellRenderers;
            if (shellNode != null)
            {
                shellRenderers = shellNode.GetComponentsInChildren<Renderer>(true);
                if (shadowRenderer == null) shadowRenderer = shellNode.GetComponentInChildren<SkinnedMeshRenderer>(true);
            }
            else if (shadowRenderer != null)
                shellRenderers = new[] { (Renderer)shadowRenderer };
            else
                shellRenderers = null;
            if (mainCam == null) mainCam = Camera.main;
            shadowLayer = LayerMask.NameToLayer(ShadowLayerName);

            if (shellRenderers == null || shellRenderers.Length == 0 || mainCam == null || shadowLayer < 0 || blurTemplate == null || compositeTemplate == null)
            {
                Debug.LogWarning($"[PaimonShadow] 初始化失败：壳渲染器={(shellRenderers != null && shellRenderers.Length > 0)} 主相机={(mainCam != null)} layer={ShadowLayerName}({shadowLayer}) 模糊={(blurTemplate != null)} 合成={(compositeTemplate != null)}，阴影禁用");
                enableShadow = false;
                return;
            }

            // 防线：场景接线漂移也能工作——影子壳全部渲染器进专属层 + 主相机剔除该层
            foreach (var r in shellRenderers) r.gameObject.layer = shadowLayer;
            mainCam.cullingMask &= ~(1 << shadowLayer);

            blurMat = new Material(blurTemplate);
            compositeMat = new Material(compositeTemplate);

            var camGo = new GameObject("_ShadowCam");
            camGo.hideFlags = HideFlags.DontSave;
            camGo.transform.SetParent(mainCam.transform, false);
            camGo.transform.localPosition = Vector3.zero;
            camGo.transform.localRotation = Quaternion.identity;
            shadowCam = camGo.AddComponent<Camera>();
            shadowCam.enabled = false; // 禁自动渲染，LateUpdate 手动 Render 保证时序：剪影→模糊→主相机合成
            shadowCam.clearFlags = CameraClearFlags.SolidColor;
            shadowCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            shadowCam.cullingMask = 1 << shadowLayer;
            shadowCam.allowHDR = false;
            shadowCam.allowMSAA = false;
            shadowCam.depth = mainCam.depth - 1f;

            var quadGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quadGo.name = "_ShadowComposite";
            quadGo.hideFlags = HideFlags.DontSave;
            Destroy(quadGo.GetComponent<Collider>());
            quadGo.transform.SetParent(mainCam.transform, false);
            quadGo.transform.localRotation = Quaternion.identity;
            var mr = quadGo.GetComponent<MeshRenderer>();
            mr.sharedMaterial = compositeMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            quad = quadGo.transform;

            Debug.Log("[PaimonShadow] 高斯投影阴影已初始化");
        }

        void OnDestroy()
        {
            ReleaseRTs();
            if (shadowCam != null) Destroy(shadowCam.gameObject);
            if (quad != null) Destroy(quad.gameObject);
            if (blurMat != null) Destroy(blurMat);
            if (compositeMat != null) Destroy(compositeMat);
        }

        void OnDisable() { if (quad != null) quad.gameObject.SetActive(false); }
        void OnEnable() { if (quad != null && enableShadow) quad.gameObject.SetActive(true); }

        void LateUpdate()
        {
            if (!enableShadow) { if (quad.gameObject.activeSelf) quad.gameObject.SetActive(false); return; }
            if (!quad.gameObject.activeSelf) quad.gameObject.SetActive(true);

            EnsureRTs();
            SyncShadowCamera();

            // 剪影 → 双向高斯 ×两轮（动画在 LateUpdate 前已结算，本帧蒙皮即最终姿势）。
            // 每轮半径=总半径×0.66：两轮方差相加 ≈ 旧单轮 9-tap 的 σ（≈0.42×总半径），模糊量/观感不变；
            // 17-tap 细间距（radius/8）+ 二次迭代把第一轮残余台阶再抹平——旧 9-tap 间距 radius/4 的
            // 平台状阶梯条带即"影子像多个格子"的根因（2026-08-24）
            shadowCam.Render();
            blurMat.SetFloat("_BlurRadius", blurRadiusPx * 0.66f);
            blurMat.SetVector("_TexelSize", new Vector4(1f / rtA.width, 1f / rtA.height, 0f, 0f));
            Graphics.Blit(rtA, rtB, blurMat, 0);
            Graphics.Blit(rtB, rtA, blurMat, 1);
            Graphics.Blit(rtA, rtB, blurMat, 0);
            Graphics.Blit(rtB, rtA, blurMat, 1);

            // 合成参数（逐帧写，Inspector 调整即时生效）
            compositeMat.SetColor("_Color", shadowColor);
            compositeMat.SetVector("_OffsetUV", new Vector4(
                shadowOffsetPx.x / Screen.width, shadowOffsetPx.y / Screen.height, 0f, 0f));

            // 全屏 Quad 贴合近裁面（透视/正交都兼容；缩放窗口改 aspect 时逐帧跟随）
            float d = mainCam.nearClipPlane + 0.05f;
            quad.localPosition = new Vector3(0f, 0f, d);
            float h = mainCam.orthographic
                ? 2f * mainCam.orthographicSize
                : 2f * d * Mathf.Tan(mainCam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float w = h * mainCam.aspect;
            quad.localScale = new Vector3(w * 1.02f, h * 1.02f, 1f);
        }

        /// <summary>影子相机逐帧与主相机同姿同投影（桌宠缩放/窗口变化都会改 aspect）</summary>
        private void SyncShadowCamera()
        {
            shadowCam.orthographic = mainCam.orthographic;
            shadowCam.orthographicSize = mainCam.orthographicSize;
            shadowCam.fieldOfView = mainCam.fieldOfView;
            shadowCam.nearClipPlane = mainCam.nearClipPlane;
            shadowCam.farClipPlane = mainCam.farClipPlane;
            shadowCam.aspect = mainCam.aspect;
        }

        private void EnsureRTs()
        {
            int h = Mathf.Max(64, shadowTexHeight);
            int w = Mathf.Max(64, Mathf.RoundToInt(h * Mathf.Max(0.05f, mainCam.aspect)));
            if (rtA != null && rtA.width == w && rtA.height == h) return;

            ReleaseRTs();
            rtA = NewRT(w, h);
            rtB = NewRT(w, h);
            if (shadowCam != null) shadowCam.targetTexture = rtA;
            if (compositeMat != null) compositeMat.mainTexture = rtA;
            Debug.Log($"[PaimonShadow] 阴影 RT {w}x{h}");
        }

        private static RenderTexture NewRT(int w, int h)
        {
            var rt = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1
            };
            rt.Create();
            return rt;
        }

        private void ReleaseRTs()
        {
            if (rtA != null) { rtA.Release(); Destroy(rtA); rtA = null; }
            if (rtB != null) { rtB.Release(); Destroy(rtB); rtB = null; }
        }
    }
}
