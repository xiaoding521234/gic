using UnityEditor;
using UnityEngine;
using UnityEditorInternal;
using System;

[CustomEditor(typeof(ItemConfig))]
public class ItemConfigEditor : Editor
{
    private ReorderableList reorderableList;
    private int contextClickIndex = -1;

    private void OnEnable()
    {
        var listProperty = serializedObject.FindProperty("itemDataList");

        reorderableList = new ReorderableList(serializedObject, listProperty, true, true, true, true);

        reorderableList.drawHeaderCallback = (Rect rect) =>
        {
            EditorGUI.LabelField(rect, "物品数据列表");
        };

        reorderableList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
        {
            rect.x += 20f;
            rect.width -= 20f;

            var element = listProperty.GetArrayElementAtIndex(index);
            var itemIDProperty = element.FindPropertyRelative("itemID");
            var starLevelProperty = element.FindPropertyRelative("starLevel");
            var subTypeProperty = element.FindPropertyRelative("subType");

            var itemIDValue = (ItemName)itemIDProperty.intValue;
            var starLevel = starLevelProperty.intValue;

            string displayName = itemIDValue.GetInspectorName();
            string starIcons = new string('★', starLevel);
            string firstTag = GetSubTypeDisplayName(subTypeProperty);

            string title = $"[{firstTag}] {displayName} {starIcons}";

            Rect summaryRect = new Rect(rect.x, rect.y, rect.width - 60f, EditorGUIUtility.singleLineHeight);
            Rect buttonRect = new Rect(rect.x + rect.width - 55f, rect.y, 55f, EditorGUIUtility.singleLineHeight);

            EditorGUI.LabelField(summaryRect, title);

            if (GUI.Button(buttonRect, "编辑"))
            {
                ItemDataEditorWindow.OpenWindow((ItemConfig)target, index);
            }

            // 检测右键点击，但不立即处理
            if (Event.current.type == UnityEngine.EventType.ContextClick && rect.Contains(Event.current.mousePosition))
            {
                contextClickIndex = index;
                // 不调用 Event.current.Use()，让事件继续传递
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

        // 处理右键菜单
        if (contextClickIndex >= 0 && Event.current.type == UnityEngine.EventType.ContextClick)
        {
            ShowDefaultContextMenu(contextClickIndex);
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void ShowDefaultContextMenu(int index)
    {
        var listProperty = serializedObject.FindProperty("itemDataList");
        if (index >= 0 && index < listProperty.arraySize)
        {
            var element = listProperty.GetArrayElementAtIndex(index);

            // 创建一个 GenericMenu
            var menu = new GenericMenu();

            // 添加自定义菜单项
            menu.AddItem(new GUIContent("编辑"), false, () =>
            {
                ItemDataEditorWindow.OpenWindow((ItemConfig)target, index);
            });

            menu.AddSeparator("");

            menu.AddItem(new GUIContent("复制元素"), false, () => OnDuplicateElement(index));
            menu.AddItem(new GUIContent("删除元素"), false, () => OnDeleteElement(index));

            menu.AddSeparator("");

            // 添加 SerializedProperty 的默认菜单项
            AddPropertyContextMenuItems(menu, element);

            menu.ShowAsContext();
            Event.current.Use();
        }
    }

    private void AddPropertyContextMenuItems(GenericMenu menu, SerializedProperty property)
    {
        // 复制属性值
        menu.AddItem(new GUIContent("Copy"), false, () =>
        {
            EditorGUIUtility.systemCopyBuffer = GetPropertyValueAsString(property);
        });

        // 粘贴属性值
        menu.AddItem(new GUIContent("Paste"), false, () =>
        {
            if (!string.IsNullOrEmpty(EditorGUIUtility.systemCopyBuffer))
            {
                SetPropertyValueFromString(property, EditorGUIUtility.systemCopyBuffer);
                serializedObject.ApplyModifiedProperties();
            }
        });

        // 如果是数组或复杂类型，添加复制/粘贴整个元素
        if (property.isArray || property.hasChildren)
        {
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Copy Element"), false, () =>
            {
                EditorGUIUtility.systemCopyBuffer = EditorJsonUtility.ToJson(property, prettyPrint: false);
            });

            menu.AddItem(new GUIContent("Paste Element"), false, () =>
            {
                if (!string.IsNullOrEmpty(EditorGUIUtility.systemCopyBuffer))
                {
                    try
                    {
                        EditorJsonUtility.FromJsonOverwrite(EditorGUIUtility.systemCopyBuffer, property);
                        serializedObject.ApplyModifiedProperties();
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Failed to paste element: {e.Message}");
                    }
                }
            });
        }
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
                return property.enumValueIndex.ToString();
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
                        property.enumValueIndex = enumValue;
                    break;
                case SerializedPropertyType.Vector2:
                    var v2Parts = value.Split(',');
                    if (v2Parts.Length == 2 &&
                        float.TryParse(v2Parts[0], out float v2x) &&
                        float.TryParse(v2Parts[1], out float v2y))
                        property.vector2Value = new Vector2(v2x, v2y);
                    break;
                case SerializedPropertyType.Vector3:
                    var v3Parts = value.Split(',');
                    if (v3Parts.Length == 3 &&
                        float.TryParse(v3Parts[0], out float v3x) &&
                        float.TryParse(v3Parts[1], out float v3y) &&
                        float.TryParse(v3Parts[2], out float v3z))
                        property.vector3Value = new Vector3(v3x, v3y, v3z);
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
        var listProperty = serializedObject.FindProperty("itemDataList");

        if (index >= 0 && index < listProperty.arraySize)
        {
            listProperty.InsertArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();
        }
    }

    private void OnDeleteElement(int index)
    {
        serializedObject.Update();
        var listProperty = serializedObject.FindProperty("itemDataList");

        if (index >= 0 && index < listProperty.arraySize)
        {
            listProperty.DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();
        }
    }

    private string GetSubTypeDisplayName(SerializedProperty subTypeProperty)
    {
        if (subTypeProperty == null)
            return "无";

        int enumIndex = subTypeProperty.enumValueIndex;
        var enumValues = Enum.GetValues(typeof(ItemSubType));
        var subTypeValue = (ItemSubType)enumValues.GetValue(enumIndex);
        return subTypeValue.GetInspectorName();
    }
}

/// <summary>
/// 改进的独立物品数据编辑窗口
/// </summary>
public class ItemDataEditorWindow : EditorWindow
{
    private ItemConfig targetConfig;
    private int elementIndex;
    private SerializedObject configSerializedObject;
    private Vector2 scrollPosition;
    private string itemDisplayName;
    private bool dataChanged = false;

    public static void OpenWindow(ItemConfig config, int index)
    {
        var window = GetWindow<ItemDataEditorWindow>("编辑物品数据");
        window.targetConfig = config;
        window.elementIndex = index;
        window.dataChanged = false;

        // 创建新的 SerializedObject
        window.configSerializedObject = new SerializedObject(config);

        // 获取物品名称用于标题
        var listProp = window.configSerializedObject.FindProperty("itemDataList");
        if (listProp != null && index < listProp.arraySize)
        {
            var element = listProp.GetArrayElementAtIndex(index);
            var itemIDProp = element.FindPropertyRelative("itemID");
            var itemIDValue = (ItemName)itemIDProp.intValue;
            window.itemDisplayName = itemIDValue.GetInspectorName();
        }

        // 窗口居中显示并增加高度
        float width = 500;
        float height = 900;
        window.minSize = new Vector2(width, 600);
        // 获取 Unity Editor 主窗口的位置和大小
        var mainWindowPos = EditorGUIUtility.GetMainWindowPosition();
        // 相对于主窗口居中
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

        var listProperty = configSerializedObject.FindProperty("itemDataList");

        if (elementIndex < 0 || elementIndex >= listProperty.arraySize)
        {
            EditorGUILayout.LabelField("物品数据不存在，可能已被删除");
            return;
        }

        var element = listProperty.GetArrayElementAtIndex(elementIndex);

        EditorGUILayout.LabelField($"编辑: {itemDisplayName}", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        // 开始检测变更
        EditorGUI.BeginChangeCheck();

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        // 手动绘制每个字段，这样我们可以更好地控制布局
        DrawElementFields(element);

        EditorGUILayout.EndScrollView();

        // 检测是否有变更
        if (EditorGUI.EndChangeCheck())
        {
            dataChanged = true;
            // 标记为已修改，但不立即应用（让用户决定何时保存）
            configSerializedObject.SetIsDifferentCacheDirty();
        }

        EditorGUILayout.Space(10);

        // 显示状态信息
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
        // 使用 Copy 遍历属性，提供更好的控制
        SerializedProperty property = element.Copy();
        SerializedProperty endProperty = element.GetEndProperty();

        bool enterChildren = true;
        bool firstProperty = true;

        while (property.NextVisible(enterChildren) && !SerializedProperty.EqualContents(property, endProperty))
        {
            enterChildren = false;

            // 为不同类型提供定制化显示
            EditorGUILayout.BeginHorizontal();

            // 对于数组类型，使用更大的区域
            if (property.isArray && property.propertyType != SerializedPropertyType.String)
            {
                EditorGUILayout.PropertyField(property, new GUIContent(property.displayName), true);
            }
            else
            {
                EditorGUILayout.PropertyField(property, new GUIContent(property.displayName));
            }

            EditorGUILayout.EndHorizontal();

            // 在字段之间添加小间距
            if (!firstProperty)
            {
                EditorGUILayout.Space(2);
            }
            firstProperty = false;
        }
    }

    private void ApplyAndSaveChanges()
    {
        if (configSerializedObject != null)
        {
            // 应用所有修改
            configSerializedObject.ApplyModifiedProperties();

            // 确保资源被保存
            EditorUtility.SetDirty(targetConfig);
            AssetDatabase.SaveAssets();

            dataChanged = false;

            Debug.Log($"物品数据 '{itemDisplayName}' 已保存");
        }
    }

    private void OnDestroy()
    {
        if (configSerializedObject != null)
        {
            if (dataChanged)
            {
                // 询问用户是否保存更改
                bool shouldSave = EditorUtility.DisplayDialog(
                    "未保存的更改",
                    $"是否保存对 '{itemDisplayName}' 的更改？",
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

/// <summary>
/// 自定义 Element 类的 Inspector 显示
/// </summary>
[CustomPropertyDrawer(typeof(Element))]
public class ElementDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var originalPosition = position;

        float dragHandleWidth = 15f;
        position.x += dragHandleWidth;
        position.width -= dragHandleWidth;

        var elementTypeProperty = property.FindPropertyRelative("elementType");
        var stacksProperty = property.FindPropertyRelative("stacks");

        int enumIndex = elementTypeProperty.enumValueIndex;
        var enumValues = System.Enum.GetValues(typeof(ElementType));
        var elementTypeValue = (ElementType)enumValues.GetValue(enumIndex);
        string elementName = elementTypeValue.GetShortName();

        int stacks = stacksProperty.intValue;

        string customLabel = $"{stacks}{elementName}";

        float labelWidth = 60f;
        float fieldWidth = (position.width - labelWidth) / 2;

        Rect labelRect = new Rect(position.x, position.y, labelWidth, position.height);
        Rect typeRect = new Rect(position.x + labelWidth, position.y, fieldWidth, position.height);
        Rect stacksRect = new Rect(position.x + labelWidth + fieldWidth, position.y, fieldWidth, position.height);

        EditorGUI.LabelField(labelRect, customLabel);
        EditorGUI.PropertyField(typeRect, elementTypeProperty, GUIContent.none);
        EditorGUI.PropertyField(stacksRect, stacksProperty, GUIContent.none);
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUIUtility.singleLineHeight;
    }
}