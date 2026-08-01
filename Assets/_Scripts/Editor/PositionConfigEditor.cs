using UnityEditor;
using UnityEngine;
using UnityEditorInternal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Editor
{


    [CustomEditor(typeof(PositionConfig))]
    public class PositionConfigEditor : UnityEditor.Editor
    {
        private const string DAYTIME_PATH = "Assets/Resources/Audios/Daytime";
        private const string NIGHT_PATH = "Assets/Resources/Audios/Night";

        private ReorderableList reorderableList;
        private int contextClickIndex = -1;
        
        private void OnEnable()
        {
            var listProperty = serializedObject.FindProperty("mapDataList");
            
            reorderableList = new ReorderableList(serializedObject, listProperty, true, true, true, true);
            
            reorderableList.drawHeaderCallback = (Rect rect) =>
            {
                EditorGUI.LabelField(rect, "地图数据列表");
            };
            
            reorderableList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                rect.x += 20f;
                rect.width -= 20f;
                
                var element = listProperty.GetArrayElementAtIndex(index);
                var positionProperty = element.FindPropertyRelative("position");
                var audioPrefixProperty = element.FindPropertyRelative("audioPrefix");
                
                var positionValue = (PositionName)positionProperty.intValue;
                string displayName = positionValue.GetInspectorName();
                
                // 如果有前缀，显示前缀
                string prefix = audioPrefixProperty.stringValue;
                string prefixDisplay = string.IsNullOrEmpty(prefix) ? "" : $" [{prefix}]";
                
                Rect summaryRect = new Rect(rect.x, rect.y, rect.width - 60f, EditorGUIUtility.singleLineHeight);
                Rect buttonRect = new Rect(rect.x + rect.width - 55f, rect.y, 55f, EditorGUIUtility.singleLineHeight);
                
                EditorGUI.LabelField(summaryRect, displayName + prefixDisplay);
                
                if (GUI.Button(buttonRect, "编辑"))
                {
                    PositionDataEditorWindow.OpenWindow((PositionConfig)target, index);
                }
                
                if (Event.current.type == UnityEngine.EventType.ContextClick && rect.Contains(Event.current.mousePosition))
                {
                    contextClickIndex = index;
                }
            };
            
            reorderableList.elementHeightCallback = (int index) =>
            {
                return EditorGUIUtility.singleLineHeight + 2f;
            };
        }
        
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            
            contextClickIndex = -1;
            
            reorderableList.DoLayoutList();
            
            if (contextClickIndex >= 0 && Event.current.type == UnityEngine.EventType.ContextClick)
            {
                ShowDefaultContextMenu(contextClickIndex);
            }
            
            // ========== 批量填充工具 ==========
            EditorGUILayout.Space(20);
            EditorGUILayout.LabelField("批量填充工具", EditorStyles.boldLabel);

            if (GUILayout.Button("根据前缀自动填充所有位置的音乐", GUILayout.Height(30)))
            {
                AutoFillAllPositions((PositionConfig)target);
            }

            if (GUILayout.Button("清空所有位置的音乐", GUILayout.Height(30)))
            {
                ClearAllAudios((PositionConfig)target);
            }

            serializedObject.ApplyModifiedProperties();
        }

        #region 批量填充

        private void AutoFillAllPositions(PositionConfig config)
        {
            Dictionary<string, AudioClip> daytimeClips = LoadAllClipsFromFolder(DAYTIME_PATH);
            Dictionary<string, AudioClip> nightClips = LoadAllClipsFromFolder(NIGHT_PATH);

            Debug.Log($"找到 {daytimeClips.Count} 个白天音频, {nightClips.Count} 个晚上音频");

            int filledCount = 0;
            int skippedCount = 0;

            foreach (var positionData in config.mapDataList)
            {
                if (string.IsNullOrEmpty(positionData.audioPrefix))
                {
                    skippedCount++;
                    continue;
                }

                string prefix = positionData.audioPrefix.ToLower();
                bool filled = false;

                var matchedDayClips = FindClipsByPrefix(daytimeClips, prefix);
                if (matchedDayClips.Count > 0)
                {
                    if (positionData.dayAudios == null)
                    {
                        positionData.dayAudios = new AudioClipRandom();
                    }
                    else
                    {
                        positionData.dayAudios.Clear();
                    }

                    foreach (var clip in matchedDayClips)
                    {
                        positionData.dayAudios.AddClip(clip);
                    }
                    filled = true;
                }

                var matchedNightClips = FindClipsByPrefix(nightClips, prefix);
                if (matchedNightClips.Count > 0)
                {
                    if (positionData.nightAudios == null)
                    {
                        positionData.nightAudios = new AudioClipRandom();
                    }
                    else
                    {
                        positionData.nightAudios.Clear();
                    }

                    foreach (var clip in matchedNightClips)
                    {
                        positionData.nightAudios.AddClip(clip);
                    }
                    filled = true;
                }

                if (filled)
                {
                    filledCount++;
                    Debug.Log($"已填充: {positionData.position} (前缀: {prefix}), 白天:{matchedDayClips.Count}首, 晚上:{matchedNightClips.Count}首");
                }
                else
                {
                    Debug.LogWarning($"未找到匹配音频: {positionData.position} (前缀: {prefix})");
                }
            }

            EditorUtility.SetDirty(config);
            Debug.Log($"填充完成: 成功 {filledCount} 个位置, 跳过 {skippedCount} 个位置 (无前缀)");
        }

        private void ClearAllAudios(PositionConfig config)
        {
            foreach (var positionData in config.mapDataList)
            {
                positionData.dayAudios?.Clear();
                positionData.nightAudios?.Clear();
            }

            EditorUtility.SetDirty(config);
            Debug.Log("已清空所有位置的音乐");
        }

        private Dictionary<string, AudioClip> LoadAllClipsFromFolder(string folderPath)
        {
            Dictionary<string, AudioClip> clipDict = new Dictionary<string, AudioClip>();

            if (!Directory.Exists(folderPath))
            {
                Debug.LogWarning($"文件夹不存在: {folderPath}");
                return clipDict;
            }

            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { folderPath });

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);

                if (clip != null)
                {
                    string fileName = Path.GetFileNameWithoutExtension(assetPath).ToLower();

                    if (!clipDict.ContainsKey(fileName))
                    {
                        clipDict.Add(fileName, clip);
                    }
                }
            }

            return clipDict;
        }

        private List<AudioClip> FindClipsByPrefix(Dictionary<string, AudioClip> clipDict, string prefix)
        {
            List<AudioClip> matchedClips = new List<AudioClip>();
            var sortedKeys = clipDict.Keys.OrderBy(k => k);

            foreach (string fileName in sortedKeys)
            {
                if (fileName.StartsWith(prefix))
                {
                    matchedClips.Add(clipDict[fileName]);
                }
            }

            return matchedClips;
        }

        #endregion

        #region 右键菜单

        private void ShowDefaultContextMenu(int index)
        {
            var listProperty = serializedObject.FindProperty("mapDataList");
            if (index >= 0 && index < listProperty.arraySize)
            {
                var element = listProperty.GetArrayElementAtIndex(index);

                var menu = new GenericMenu();

                menu.AddItem(new GUIContent("编辑"), false, () =>
                {
                    PositionDataEditorWindow.OpenWindow((PositionConfig)target, index);
                });

                menu.AddSeparator("");

                menu.AddItem(new GUIContent("复制元素"), false, () => OnDuplicateElement(index));
                menu.AddItem(new GUIContent("删除元素"), false, () => OnDeleteElement(index));

                menu.AddSeparator("");

                AddPropertyContextMenuItems(menu, element);

                menu.ShowAsContext();
                Event.current.Use();
            }
        }

        private void AddPropertyContextMenuItems(GenericMenu menu, SerializedProperty property)
        {
            menu.AddItem(new GUIContent("Copy"), false, () =>
            {
                EditorGUIUtility.systemCopyBuffer = GetPropertyValueAsString(property);
            });

            menu.AddItem(new GUIContent("Paste"), false, () =>
            {
                if (!string.IsNullOrEmpty(EditorGUIUtility.systemCopyBuffer))
                {
                    SetPropertyValueFromString(property, EditorGUIUtility.systemCopyBuffer);
                    serializedObject.ApplyModifiedProperties();
                }
            });
        }

        private string GetPropertyValueAsString(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return property.intValue.ToString();
                case SerializedPropertyType.Float:
                    return property.floatValue.ToString();
                case SerializedPropertyType.String:
                    return property.stringValue;
                case SerializedPropertyType.Boolean:
                    return property.boolValue.ToString();
                case SerializedPropertyType.Enum:
                    return property.intValue.ToString();
                case SerializedPropertyType.Vector2:
                    return $"{property.vector2Value.x},{property.vector2Value.y}";
                case SerializedPropertyType.Vector3:
                    return $"{property.vector3Value.x},{property.vector3Value.y},{property.vector3Value.z}";
                case SerializedPropertyType.ObjectReference:
                    return property.objectReferenceValue != null ? property.objectReferenceValue.name : "null";
                default:
                    return "";
            }
        }

        private void SetPropertyValueFromString(SerializedProperty property, string value)
        {
            try
            {
                switch (property.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        if (int.TryParse(value, out int intValue))
                            property.intValue = intValue;
                        break;
                    case SerializedPropertyType.Float:
                        if (float.TryParse(value, out float floatValue))
                            property.floatValue = floatValue;
                        break;
                    case SerializedPropertyType.String:
                        property.stringValue = value;
                        break;
                    case SerializedPropertyType.Boolean:
                        if (bool.TryParse(value, out bool boolValue))
                            property.boolValue = boolValue;
                        break;
                    case SerializedPropertyType.Enum:
                        if (int.TryParse(value, out int enumValue))
                            property.intValue = enumValue;
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to paste value: {e.Message}");
            }
        }

        private void OnDuplicateElement(int index)
        {
            serializedObject.Update();
            var listProperty = serializedObject.FindProperty("mapDataList");

            if (index >= 0 && index < listProperty.arraySize)
            {
                listProperty.InsertArrayElementAtIndex(index);
                serializedObject.ApplyModifiedProperties();
            }
        }

        private void OnDeleteElement(int index)
        {
            serializedObject.Update();
            var listProperty = serializedObject.FindProperty("mapDataList");

            if (index >= 0 && index < listProperty.arraySize)
            {
                listProperty.DeleteArrayElementAtIndex(index);
                serializedObject.ApplyModifiedProperties();
            }
        }

        #endregion
    }

    // ========== PositionDataEditorWindow 保持不变 ==========

    public class PositionDataEditorWindow : EditorWindow
    {
        private PositionConfig targetConfig;
        private int elementIndex;
        private SerializedObject configSerializedObject;
        private Vector2 scrollPosition;
        private string positionDisplayName;
        private bool dataChanged = false;

        public static void OpenWindow(PositionConfig config, int index)
        {
            var window = GetWindow<PositionDataEditorWindow>("编辑地图数据");
            window.targetConfig = config;
            window.elementIndex = index;
            window.dataChanged = false;

            window.configSerializedObject = new SerializedObject(config);

            var listProp = window.configSerializedObject.FindProperty("mapDataList");
            if (listProp != null && index < listProp.arraySize)
            {
                var element = listProp.GetArrayElementAtIndex(index);
                var positionProp = element.FindPropertyRelative("position");
                var positionValue = (PositionName)positionProp.intValue;
                window.positionDisplayName = positionValue.GetInspectorName();
            }

            float width = 500;
            float height = 900;
            window.minSize = new Vector2(width, 600);
            var mainWindowPos = EditorGUIUtility.GetMainWindowPosition();
            window.position = new Rect(
                mainWindowPos.x + (mainWindowPos.width - width) / 2,
                mainWindowPos.y + (mainWindowPos.height - height) / 2,
                width,
                height);
            window.Show();
        }

        private void OnGUI()
        {
            if (targetConfig == null || configSerializedObject == null)
            {
                EditorGUILayout.LabelField("无数据可编辑");
                return;
            }

            var listProperty = configSerializedObject.FindProperty("mapDataList");

            if (elementIndex < 0 || elementIndex >= listProperty.arraySize)
            {
                EditorGUILayout.LabelField("地图数据不存在，可能已被删除");
                return;
            }

            var element = listProperty.GetArrayElementAtIndex(elementIndex);

            EditorGUILayout.LabelField($"编辑: {positionDisplayName}", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUI.BeginChangeCheck();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            DrawElementFields(element);

            EditorGUILayout.EndScrollView();

            if (EditorGUI.EndChangeCheck())
            {
                dataChanged = true;
                configSerializedObject.SetIsDifferentCacheDirty();
            }

            EditorGUILayout.Space(10);

            if (dataChanged)
            {
                EditorGUILayout.HelpBox("数据已修改，请点击保存", MessageType.Info);
            }

            EditorGUILayout.BeginHorizontal();

            GUI.enabled = dataChanged;
            if (GUILayout.Button("保存", GUILayout.Height(35)))
            {
                ApplyAndSaveChanges();
            }
            GUI.enabled = true;

            if (GUILayout.Button("取消", GUILayout.Height(35)))
            {
                Close();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawElementFields(SerializedProperty element)
        {
            SerializedProperty property = element.Copy();
            SerializedProperty endProperty = element.GetEndProperty();

            bool enterChildren = true;

            while (property.NextVisible(enterChildren) && !SerializedProperty.EqualContents(property, endProperty))
            {
                enterChildren = false;

                EditorGUILayout.BeginHorizontal();

                if (property.isArray && property.propertyType != SerializedPropertyType.String)
                {
                    EditorGUILayout.PropertyField(property, new GUIContent(property.displayName), true);
                }
                else
                {
                    EditorGUILayout.PropertyField(property, new GUIContent(property.displayName));
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(2);
            }
        }

        private void ApplyAndSaveChanges()
        {
            if (configSerializedObject != null)
            {
                configSerializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(targetConfig);
                AssetDatabase.SaveAssets();

                dataChanged = false;
                Debug.Log($"地图数据 '{positionDisplayName}' 已保存");
            }
        }

        private void OnDestroy()
        {
            if (configSerializedObject != null)
            {
                if (dataChanged)
                {
                    bool shouldSave = EditorUtility.DisplayDialog(
                        "未保存的更改",
                        $"是否保存对 '{positionDisplayName}' 的更改？",
                        "保存",
                        "放弃"
                    );

                    if (shouldSave)
                    {
                        configSerializedObject.ApplyModifiedProperties();
                        EditorUtility.SetDirty(targetConfig);
                        AssetDatabase.SaveAssets();
                    }
                }

                configSerializedObject.Dispose();
            }
        }
    }
}


