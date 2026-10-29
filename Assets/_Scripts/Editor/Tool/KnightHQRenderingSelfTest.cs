#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using GIC.Battle;
using UnityEditor;
using UnityEngine;

namespace GIC.Editor
{
    /// <summary>骑士团总部渲染的编辑器离线断言；不进入 Play，不改动正式场景。</summary>
    public static class KnightHQRenderingSelfTest
    {
        private const string PrefabPath = "Assets/Art/KnightHQ/KnightHQ.prefab";
        private const string ShaderName = "GIC/Battle/GIBuilding";
        private static readonly List<string> Failures = new List<string>();

        [MenuItem("Tools/TG/骑士团总部渲染自检")]
        public static bool RunAll()
        {
            Failures.Clear();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Check(prefab != null, "总部 prefab 存在");
            if (prefab == null) return Report();

            Check(Mathf.Abs(Mathf.DeltaAngle(prefab.transform.localEulerAngles.y, 90f)) < 0.01f,
                "prefab 默认 yaw=90（本地正门 +X → 玩家 -Z）");

            var host = new GameObject("KnightHQRenderingSelfTest");
            try
            {
                var view = BuildingModelView.Create(host.transform, prefab, 1f);
                var model = host.transform.GetChild(0);
                Check(Quaternion.Angle(model.localRotation, prefab.transform.localRotation) < 0.01f,
                    "Create 保留 prefab 的默认朝向");
                Check(Vector3.Dot(model.rotation * Vector3.right, Vector3.back) > 0.999f,
                    "实例正门 +X 朝向玩家 -Z");

                view.SetTint(new Color(1f, 0.25f, 0.5f, 1f));
                var materials = new HashSet<Material>();
                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                    foreach (var material in renderer.sharedMaterials)
                        if (material != null) materials.Add(material);
                foreach (var material in materials)
                    Check(material.GetColor("_BaseColor") == new Color(1f, 0.25f, 0.5f, 1f),
                        $"状态 tint 写入克隆材质：{material.name}");

                view.SetDissolveAlpha(0.5f);
                foreach (var material in materials)
                {
                    Check(Mathf.Approximately(material.GetFloat("_Surface"), 1f), $"消散切透明：{material.name}");
                    Check(Mathf.Approximately(material.GetFloat("_ZWrite"), 0f), $"消散关深度写入：{material.name}");
                    Check(Mathf.Approximately(material.GetColor("_BaseColor").a, 0.5f), $"消散 alpha=0.5：{material.name}");
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
            }

            AssertMaterial("KnightHQ_Assembly", "Area_MdBuild_Column01_Diffuse", "_SecondaryTex");
            AssertMaterial("KnightHQ_Assembly", "Area_MdBuild_KnightHQ02_Assembly01_SMBE", "_DetailMask");
            AssertMaterial("KnightHQ_Assembly", "Area_MdBuild_Column01_SMBE", "_SecondaryDetailMask");
            AssertMaterial("KnightHQ_Assembly", "Area_MdBuild_Column01_Normal", "_SecondaryBumpMap");
            AssertMaterial("KnightHQ_Window", "Area_MdBuild_Window_A_Normal", "_BumpMap");
            AssertMaterial("KnightHQ_Window", "Area_MdBuild_Window_A_SMBE", "_DetailMask");
            AssertMaterial("KnightHQ_Roof05", "Area_MdBuild_House_Roof04_Height", "_POMHeightMap");
            AssertMaterial("KnightHQ_Wall09", "Area_MdBuild_Wall09_Height", "_POMHeightMap");
            AssertMaterial("KnightHQ_CityWall09", "Area_Mdbuild_Wall06_Height", "_POMHeightMap");
            AssertMaterial("KnightHQ_Ground15", "Area_MdGroundMix_Diffuse", "_SecondaryTex");
            AssertMaterial("KnightHQ_Ground15", "Area_Mdcity_Ground15_Height", "_POMHeightMap");
            AssertMaterial("KnightHQ_Ground15", "Area_Mdcity_Ground15_SMBE", "_DetailMask");
            AssertMaterial("KnightHQ_Wall08", "Area_MdBuild_Wall08_SMBE", "_DetailMask");
            AssertMaterial("KnightHQ_Stage01", "Area_Mdbuild_Stage01_SMBE", "_DetailMask");

            foreach (var name in new[] { "Assembly", "Window", "Roof05", "Wall08", "Wall09", "CityWall09", "Ground15", "Stage01" })
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Art/KnightHQ/KnightHQ_{name}.mat");
                Check(material != null && material.shader.name == ShaderName, $"原神建筑 shader：{name}");
            }
            var window = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/KnightHQ/KnightHQ_Window.mat");
            Check(window != null && Mathf.Approximately(window.GetFloat("_EmissionStrength"), 4f), "窗户官方发光强度=4");

            var nightMethod = typeof(BuildingModelView).GetMethod("NightTargetForMinutes",
                BindingFlags.Public | BindingFlags.Static);
            Check(nightMethod != null, "昼夜窗灯换算函数存在");
            if (nightMethod != null)
            {
                foreach (var sample in new[] { (360, 1f), (480, 0f), (1199, 0f), (1200, 1f) })
                    Check(Mathf.Approximately((float)nightMethod.Invoke(null, new object[] { sample.Item1 }), sample.Item2),
                        $"昼夜窗灯 minutes={sample.Item1} → {sample.Item2}");
            }
            return Report();
        }

        private static void AssertMaterial(string materialName, string textureName, string property)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Art/KnightHQ/{materialName}.mat");
            Check(material != null && material.GetTexture(property) != null &&
                  material.GetTexture(property).name == textureName,
                $"{materialName}.{property}={textureName}");
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) Failures.Add(message);
        }

        private static bool Report()
        {
            if (Failures.Count == 0)
            {
                Debug.Log("KNIGHT_HQ_RENDERING_PASS");
                return true;
            }
            foreach (var failure in Failures) Debug.LogError("KNIGHT_HQ_ASSERT " + failure);
            Debug.LogError($"KNIGHT_HQ_RENDERING_FAIL={Failures.Count}");
            return false;
        }
    }
}
#endif
