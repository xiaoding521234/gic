using UnityEngine;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
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
        // EditorWindow 序列化字段，脚本重编译后保留选择
        [SerializeField] private TMP_FontAsset newFontAsset;

        [MenuItem("Tools/批量替换 TMP 字体", priority = -20)]
        public static void ShowWindow()
        {
            GetWindow<TMPFontReplacer>("批量替换TMP字体");
        }

        // CreateGUI 为按名调用的魔法方法（Tuanjie 中非虚方法），不加 override
        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingLeft = 10;
            root.style.paddingRight = 10;
            root.style.paddingTop = 8;

            ConfigEditorUITK.ApplyGameFont(root);

            root.Add(ConfigEditorUITK.CreateTitleRow("批量替换 TMP 字体", 0));

            var objField = new ObjectField("目标字体")
            {
                objectType = typeof(TMP_FontAsset),
                allowSceneObjects = false,
            };
            objField.value = newFontAsset;
            root.Add(objField);

            Button replaceBtn = null;
            void SyncEnabled() => replaceBtn?.SetEnabled(newFontAsset != null);
            objField.RegisterValueChangedCallback(e =>
            {
                newFontAsset = (TMP_FontAsset)e.newValue;
                SyncEnabled();
            });

            replaceBtn = ConfigEditorUITK.CreatePrimaryButton("一键替换所有 Prefab 和场景", () =>
            {
                if (newFontAsset == null) return;
                if (EditorUtility.DisplayDialog("确认替换",
                    $"确定要把所有 TMP 字体重置为 {newFontAsset.name} 吗？\n此操作支持撤销。", "确定", "取消"))
                {
                    ReplaceAll();
                }
            });
            replaceBtn.style.height = 40;
            replaceBtn.style.marginTop = 12;
            root.Add(replaceBtn);
            SyncEnabled();
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
