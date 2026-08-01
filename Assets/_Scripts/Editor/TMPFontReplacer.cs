using UnityEngine;
using UnityEditor;
using TMPro;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Editor
{


    public class TMPFontReplacer : EditorWindow
    {
        public TMP_FontAsset newFontAsset;

        [MenuItem("Tools/批量替换 TMP 字体")]
        public static void ShowWindow()
        {
            GetWindow<TMPFontReplacer>("批量替换TMP字体");
        }

        private void OnGUI()
        {
            GUILayout.Label("批量替换所有预制体和场景中的 TMP 字体", EditorStyles.boldLabel);
            newFontAsset = (TMP_FontAsset)EditorGUILayout.ObjectField("目标字体", newFontAsset, typeof(TMP_FontAsset), false);

            EditorGUI.BeginDisabledGroup(newFontAsset == null);
            if (GUILayout.Button("一键替换所有 Prefab 和场景"))
            {
                if (EditorUtility.DisplayDialog("确认替换",
                    $"确定要把所有 TMP 字体重置为 {newFontAsset.name} 吗？\n此操作支持撤销。", "确定", "取消"))
                {
                    ReplaceAll();
                }
            }
            EditorGUI.EndDisabledGroup();
        }

        private void ReplaceAll()
        {
            Undo.IncrementCurrentGroup();
            int count = 0;

            // 1. 替换所有 Prefab 中的 TMP_Text
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
            foreach (string guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                bool modified = false;
                // 需要实例化才能修改组件属性
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                TMP_Text[] tmpComponents = instance.GetComponentsInChildren<TMP_Text>(true);
                foreach (TMP_Text tmp in tmpComponents)
                {
                    Undo.RecordObject(tmp, "Replace TMP Font");
                    tmp.font = newFontAsset;
                    count++;
                    modified = true;
                }

                if (modified)
                {
                    PrefabUtility.SaveAsPrefabAsset(instance, path);
                }
                DestroyImmediate(instance);
            }

            // 2. 替换当前打开场景中所有的 TMP_Text
            TMP_Text[] sceneTMPs = FindObjectsOfType<TMP_Text>(true);
            foreach (TMP_Text tmp in sceneTMPs)
            {
                Undo.RecordObject(tmp, "Replace TMP Font");
                tmp.font = newFontAsset;
                count++;
            }

            Undo.SetCurrentGroupName("批量替换 TMP 字体");
            EditorUtility.DisplayDialog("完成", $"已成功替换 {count} 个 TMP 文本组件。\n可随时使用 Ctrl+Z 撤销。", "好的");
        }
    }
}

