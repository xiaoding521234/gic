using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GIC.Editor
{
    /// <summary>
    /// PaimonPet 场景动作列表同步（2026-08-24）：把管线产物（Animations/MMD/ 全部 _MMD.anim）
    /// 一键注册进 PaimonPet 的 Animation 组件。管线对输出 clip 是"删旧建新"（GUID 会换），
    /// 场景里的旧引用会断、新动作要手工拖——本工具对齐这个缺口（重跑管线后跑一次即可）。
    /// 幂等：清空重注册；默认 clip 按名字保持原引用，丢失则回退 Standby。
    /// </summary>
    public static class PetSceneSyncTool
    {
        private const string ScenePath = "Assets/Scenes/PaimonPet.unity";
        private const string ClipDir = "Assets/Art/PaimonPet/Animations/MMD";

        [MenuItem("Tools/桌宠/同步 PaimonPet 动作列表")]
        static void Sync()
        {
            var cur = EditorSceneManager.GetActiveScene();
            if (cur.path != ScenePath)
            {
                if (cur.isDirty)
                {
                    Debug.LogError("[PetSceneSync] 当前场景有未保存修改，请先保存再同步（避免静默丢弃）");
                    return;
                }
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            SyncInternal();
        }

        static void SyncInternal()
        {
            var animComps = Object.FindObjectsOfType<Animation>(true);
            if (animComps.Length == 0)
            {
                Debug.LogError("[PetSceneSync] PaimonPet 里没找到 Animation 组件");
                return;
            }
            if (animComps.Length > 1)
                Debug.LogWarning($"[PetSceneSync] 场景里有 {animComps.Length} 个 Animation，取第一个（{animComps[0].name}）");
            var anim = animComps[0];

            var clips = AssetDatabase.FindAssets("t:AnimationClip", new[] { ClipDir })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".anim"))
                .Select(p => AssetDatabase.LoadAssetAtPath<AnimationClip>(p))
                .Where(c => c != null)
                .OrderBy(c => c.name)
                .ToList();
            if (clips.Count == 0)
            {
                Debug.LogError($"[PetSceneSync] {ClipDir} 下没有 clip，请先重跑重定向管线");
                return;
            }

            // 默认 clip 按名字保留（RemoveClip 后引用会失效，先记名再找回同名新资产）
            var keepDefaultName = anim.clip != null ? anim.clip.name : null;

            // 读当前注册名（Animation 无公开枚举 API，走 SerializedObject）
            var oldNames = new List<string>();
            var so = new SerializedObject(anim);
            var arr = so.FindProperty("m_Animations");
            for (int i = 0; i < arr.arraySize; i++)
            {
                var c = arr.GetArrayElementAtIndex(i).objectReferenceValue as AnimationClip;
                if (c != null) oldNames.Add(c.name);
            }
            foreach (var n in oldNames) anim.RemoveClip(n);
            foreach (var c in clips) anim.AddClip(c, c.name);

            var keepDefault = keepDefaultName != null ? clips.FirstOrDefault(c => c.name == keepDefaultName) : null;
            anim.clip = keepDefault
                        ?? clips.FirstOrDefault(c => c.name.EndsWith("Standby_MMD"))
                        ?? clips[0];

            EditorUtility.SetDirty(anim);
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[PetSceneSync] 注册 {clips.Count} 个 clip 到 {anim.name}（旧 {oldNames.Count} 个已清），默认={anim.clip.name}，场景已保存");
        }
    }
}
