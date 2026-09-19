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
