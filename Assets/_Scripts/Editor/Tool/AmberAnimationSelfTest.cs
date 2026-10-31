#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GIC.Editor
{
    /// <summary>安柏原生 Humanoid 动画资产与场景接线的编辑器离线断言。</summary>
    public static class AmberAnimationSelfTest
    {
        private const string BodyRoot = "Assets/Art/AmberTMR/MuscleClips/Rebuilt/";
        private static readonly List<string> Failures = new List<string>();
        private static readonly string[] Required =
        {
            "Ani_Avatar_Girl_Standby",
            "Ani_Avatar_Girl_Bow_WeaponStandby",
            "Ani_Avatar_Girl_RunBS",
            "Ani_Avatar_Girl_SprintCycle+Mirror",
            "Ani_Avatar_Girl_FlyNormal",
            "Ani_Avatar_Girl_Bow_Ambor_Attack_05",
            "Ani_Avatar_Girl_Bow_Ambor_ElementalArt_Short",
            "Ani_Avatar_Girl_Bow_Ambor_Show_01"
        };

        [MenuItem("Tools/TG/安柏动画资产自检")]
        public static bool RunAssets()
        {
            Failures.Clear();
            CheckAssets();
            return Report("AMBER_ANIMATION_ASSETS");
        }

        [MenuItem("Tools/TG/安柏动画全量自检")]
        public static bool RunAll()
        {
            Failures.Clear();
            CheckAssets();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            EditorSceneManager.OpenScene("Assets/Scenes/AmberTest.unity");
            var panel = Object.FindObjectOfType<AmberTestPanel>();
            Check(panel != null, "AmberTestPanel 存在");
            if (panel != null)
            {
                var list = new SerializedObject(panel).FindProperty("动作列表");
                Check(list != null && list.isArray && list.arraySize == Required.Length,
                    $"显式动作条目={Required.Length}");
                if (list != null && list.isArray)
                    for (int i = 0; i < Mathf.Min(list.arraySize, Required.Length); i++)
                    {
                        var entry = list.GetArrayElementAtIndex(i);
                        var display = entry.FindPropertyRelative("显示名")?.stringValue;
                        var body = entry.FindPropertyRelative("身体动画")?.objectReferenceValue as AnimationClip;
                        var physics = entry.FindPropertyRelative("物理动画")?.objectReferenceValue as AnimationClip;
                        Check(body != null && body.humanMotion, $"身体动画有效：{display}");
                        Check(body != null && AssetDatabase.GetAssetPath(body) == BodyRoot + Required[i] + ".anim",
                            $"身体动画路径匹配：{display}");
                        Check(physics != null && physics.legacy, $"物理动画为 legacy：{display}");
                    }
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/AmberTMR/AmberTMR.prefab");
            Check(prefab != null && prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 5,
                "完整模型仍为 5 个蒙皮部件");
            return Report("AMBER_ANIMATION_ALL");
        }

        private static void CheckAssets()
        {
            foreach (var name in Required)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(BodyRoot + name + ".anim");
                Check(clip != null, $"重建身体 clip 存在：{name}");
                if (clip != null)
                    Check(clip.humanMotion && clip.length > 0.01f,
                        $"重建身体 clip 为有效 Humanoid：{name}");
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) Failures.Add(message);
        }

        private static bool Report(string marker)
        {
            if (Failures.Count == 0)
            {
                Debug.Log(marker + "_PASS");
                return true;
            }

            foreach (var failure in Failures) Debug.LogError(marker + "_ASSERT " + failure);
            Debug.LogError(marker + $"_FAIL={Failures.Count}");
            return false;
        }
    }
}
#endif
