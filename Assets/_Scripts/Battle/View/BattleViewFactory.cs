using UnityEngine;
using TMPro;

namespace GIC.Battle
{
    /// <summary>
    /// 战斗世界层程序化视觉工厂（2026-09-18 统一化批次：世界 Quad 基元 + Unlit 材质 + 世界 TMP 字体的唯一出口，
    /// 收口 UnitView×3 + BattleHud×2 的手搓样板与 4 处 Shader.Find 重复，docs/14 §63）。
    /// 材质生命周期归调用方：独立材质须持有引用并在 OnDestroy Destroy（Destroy 物体不销材质，§63①）。
    /// </summary>
    public static class BattleViewFactory
    {
        private static Shader _unlitShader;

        /// <summary>URP Unlit（已核验在 GraphicsSettings→Always Included Shaders 清单内，构建不被剥离，docs/14 §63③）</summary>
        public static Shader UnlitShader
        {
            get
            {
                if (_unlitShader == null)
                {
                    _unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
                    if (_unlitShader == null) _unlitShader = Shader.Find("Unlit/Color");
                }
                return _unlitShader;
            }
        }

        private static TMP_FontAsset _worldTextFont;

        /// <summary>世界空间 TMP 字体（zh-cn SDF——世界 TMP 无 UGUI 默认字体链，须显式指定防 tofu，docs/14 §62①③）</summary>
        public static TMP_FontAsset WorldTextFont =>
            _worldTextFont != null ? _worldTextFont
            : (_worldTextFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/zh-cn SDF"));

        /// <summary>新建 Unlit 纯色材质（调用方负责持有与 OnDestroy 释放）</summary>
        public static Material CreateUnlitMaterial(Color color)
        {
            var material = new Material(UnlitShader);
            material.color = color;
            return material;
        }

        /// <summary>新建 Unlit 半透明材质（URP Unlit 透明态全套写入——等价编辑器 Surface=Transparent 的
        /// ShaderGUI 落值；不设 _Surface 时 OutputAlpha 把 alpha 强制成 1，半透明失效。
        /// 首个消费者=瞄准高亮推荐/不推荐分色，2026-09-23。调用方负责持有与 OnDestroy 释放）</summary>
        public static Material CreateTransparentUnlitMaterial(Color color)
        {
            var material = new Material(UnlitShader);
            material.color = color;
            material.SetFloat("_Surface", 1f);     // 透明面（IsSurfaceTypeTransparent→alpha 输出有效）
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); // 透明面跳过 SSAO 洗色（UnlitForwardPass 同名分支）
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return material;
        }

        private static Shader _chromaKeyShader;

        /// <summary>立牌动画视频 ChromaKey shader（GIC/Battle/ChromaKeyVideo，B-S3 视频路线）——
        /// 已登记 GraphicsSettings Always Included Shaders 防构建剥离（同 URP Unlit 清单纪律，docs/14 §63③）；
        /// 找不到返回 null（调用方自然回落静态立牌）</summary>
        public static Shader ChromaKeyShader
        {
            get
            {
                if (_chromaKeyShader == null)
                {
                    _chromaKeyShader = Shader.Find("GIC/Battle/ChromaKeyVideo");
                    if (_chromaKeyShader == null)
                        Debug.LogWarning("[BattleViewFactory] ChromaKeyVideo shader 未找到（未导入/被剥离），立牌动画视频将回落静态立牌");
                }
                return _chromaKeyShader;
            }
        }

        /// <summary>新建立牌动画视频 ChromaKey 材质（消费 VideoPlayer→RT 的绿幕画面运行时抠色；
        /// _Color 承载尸体灰/冻结冰色/受击闪红 tint（UnitView.RefreshTint 写入，同 SpriteRenderer.color 语义）。
        /// 调用方负责持有与 OnDestroy 释放）</summary>
        public static Material CreateChromaKeyMaterial()
        {
            return new Material(ChromaKeyShader);
        }

        // 瞄准格底图（白芯+内嵌黑边环；运行时生成免资产文件）
        private static Texture2D _aimCellTexture;

        /// <summary>瞄准格底图（128×128，白芯+向内黑边环 12px，Clamp+Bilinear）：黑边 rgb=0 乘任意
        /// tint 恒黑——推荐/不推荐两色共享同一张；填充区 rgb=1×tint=色块本色；全图 alpha=1
        /// （透明度由材质 _BaseColor.a 承载）。2026-09-24 拍板：色块带内嵌黑边更明显。</summary>
        public static Texture2D AimCellTexture
        {
            get
            {
                if (_aimCellTexture != null) return _aimCellTexture;
                const int size = 128, border = 12;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "BattleAimCellTexture",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        bool rim = x < border || y < border || x >= size - border || y >= size - border;
                        byte v = rim ? (byte)0 : (byte)255;
                        pixels[y * size + x] = new Color32(v, v, v, 255);
                    }
                }
                tex.SetPixels32(pixels);
                tex.Apply(false, true);
                _aimCellTexture = tex;
                return tex;
            }
        }

        /// <summary>新建瞄准格材质（白芯黑边底图 × tint 纯色，透明配方复用 CreateTransparentUnlitMaterial。
        /// 调用方负责持有与 OnDestroy 释放）</summary>
        public static Material CreateAimCellMaterial(Color color)
        {
            var material = CreateTransparentUnlitMaterial(color);
            material.mainTexture = AimCellTexture;
            return material;
        }

        /// <summary>世界层纯色 Quad（基元 + 去 Collider + 挂材质；姿态/缩放由调用方补——既有调用点世界系/本地系两种用法）</summary>
        public static GameObject CreateQuad(Transform parent, string name, Material sharedMaterial)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            UnityEngine.Object.Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(parent, false);
            quad.GetComponent<MeshRenderer>().sharedMaterial = sharedMaterial;
            return quad;
        }

        /// <summary>世界层纯色圆面片（程序化扇面 mesh：中心顶点+24 段圆周，直径 1、法线 -Z 与 Quad 同向；
        /// 单位受击圆柱的底座圆盘可视化——视觉即判定，直径=BattleMetrics.UnitCylinderDiameter，docs/18 决策二。
        /// localScale x/y 语义与 Quad 一致（旋转 90° 平铺后=地面直径），支持非均匀缩放出椭圆（尸体压扁）</summary>
        public static GameObject CreateDisc(Transform parent, string name, Material sharedMaterial, int segments = 24)
        {
            var vertices = new Vector3[segments + 1];
            var triangles = new int[segments * 3];
            vertices[0] = Vector3.zero;
            for (int i = 0; i < segments; i++)
            {
                float angle = 2f * Mathf.PI * i / segments;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * 0.5f, Mathf.Sin(angle) * 0.5f, 0f);
                int next = i + 1 == segments ? 1 : i + 2;
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = next;
                triangles[i * 3 + 2] = i + 1;
            }

            var mesh = new Mesh
            {
                vertices = vertices,
                triangles = triangles,
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = sharedMaterial;
            return go;
        }
    }
}
