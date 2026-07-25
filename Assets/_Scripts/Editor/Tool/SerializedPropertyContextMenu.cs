// ============================================
// 公共工具类 - 右键菜单和序列化属性操作
// ============================================

using System;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// SerializedProperty 右键菜单和复制粘贴工具
/// </summary>
public static class SerializedPropertyContextMenu
{
    /// <summary>
    /// 为 GenericMenu 添加标准的 SerializedProperty 菜单项
    /// </summary>
    public static void AddStandardItems(GenericMenu menu, SerializedProperty property, Action onModified = null)
    {
        menu.AddItem(new GUIContent("Copy"), false, () =>
        {
            EditorGUIUtility.systemCopyBuffer = SerializePropertyValue(property);
        });

        menu.AddItem(new GUIContent("Paste"), false, () =>
        {
            if (!string.IsNullOrEmpty(EditorGUIUtility.systemCopyBuffer))
            {
                DeserializePropertyValue(property, EditorGUIUtility.systemCopyBuffer);
                property.serializedObject.ApplyModifiedProperties();
                onModified?.Invoke();
            }
        });

        if (property.isArray || property.hasVisibleChildren)
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
                        property.serializedObject.ApplyModifiedProperties();
                        onModified?.Invoke();
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Failed to paste element: {e.Message}");
                    }
                }
            });
        }
    }

    private static string SerializePropertyValue(SerializedProperty property)
    {
        switch (property.propertyType)
        {
            case SerializedPropertyType.Integer: return property.intValue.ToString();
            case SerializedPropertyType.Float: return property.floatValue.ToString();
            case SerializedPropertyType.String: return property.stringValue;
            case SerializedPropertyType.Boolean: return property.boolValue.ToString();
            case SerializedPropertyType.Enum: return property.enumValueIndex.ToString();
            case SerializedPropertyType.Vector2:
                return $"{property.vector2Value.x},{property.vector2Value.y}";
            case SerializedPropertyType.Vector3:
                return $"{property.vector3Value.x},{property.vector3Value.y},{property.vector3Value.z}";
            case SerializedPropertyType.ObjectReference:
                return property.objectReferenceValue != null ? property.objectReferenceValue.name : "null";
            default: return "";
        }
    }

    private static void DeserializePropertyValue(SerializedProperty property, string value)
    {
        try
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    if (int.TryParse(value, out int iv)) property.intValue = iv; break;
                case SerializedPropertyType.Float:
                    if (float.TryParse(value, out float fv)) property.floatValue = fv; break;
                case SerializedPropertyType.String:
                    property.stringValue = value; break;
                case SerializedPropertyType.Boolean:
                    if (bool.TryParse(value, out bool bv)) property.boolValue = bv; break;
                case SerializedPropertyType.Enum:
                    if (int.TryParse(value, out int ev)) property.enumValueIndex = ev; break;
                case SerializedPropertyType.Vector2:
                    var v2 = value.Split(',');
                    if (v2.Length == 2 && float.TryParse(v2[0], out float x2) && float.TryParse(v2[1], out float y2))
                        property.vector2Value = new Vector2(x2, y2); break;
                case SerializedPropertyType.Vector3:
                    var v3 = value.Split(',');
                    if (v3.Length == 3 && float.TryParse(v3[0], out float x3) && float.TryParse(v3[1], out float y3) && float.TryParse(v3[2], out float z3))
                        property.vector3Value = new Vector3(x3, y3, z3); break;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to paste value: {e.Message}");
        }
    }
}

// ============================================
// 可排序列表包装器 - 统一管理 ReorderableList + 右键菜单
// ============================================

/// <summary>
/// 带右键菜单支持的 ReorderableList 配置
/// </summary>
public class ReorderableListWithMenu
{
    public ReorderableList List { get; private set; }
    public int ContextClickIndex { get; set; } = -1;

    private readonly SerializedObject serializedObject;
    private readonly string propertyPath;
    private readonly Action<int> onEdit;
    private readonly Action onChanged;

    public ReorderableListWithMenu(SerializedObject so, SerializedProperty property,
        bool draggable, bool displayHeader, bool displayAdd, bool displayRemove,
        string headerText, Action<Rect, int> drawElement, float elementHeight,
        Action<int> onEdit = null, Action onChanged = null,
        ReorderableList.AddCallbackDelegate onAdd = null, 
        ReorderableList.RemoveCallbackDelegate onRemove = null,
        ReorderableList.ReorderCallbackDelegate onReorder = null)
    {
        this.serializedObject = so;
        this.propertyPath = property.propertyPath;
        this.onEdit = onEdit;
        this.onChanged = onChanged;

        List = new ReorderableList(so, property, draggable, displayHeader, displayAdd, displayRemove);

        List.drawHeaderCallback = (Rect rect) =>
        {
            EditorGUI.LabelField(rect, headerText);
        };

        List.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
        {
            drawElement(rect, index);

            // 检测右键点击
            if (Event.current.type == UnityEngine.EventType.ContextClick && rect.Contains(Event.current.mousePosition))
            {
                ContextClickIndex = index;
            }
        };

        List.elementHeightCallback = (int index) => elementHeight;

        if (onAdd != null) List.onAddCallback = onAdd;
        if (onRemove != null) List.onRemoveCallback = onRemove;
        if (onReorder != null) List.onReorderCallback = onReorder;
    }

    public void DoLayoutList()
    {
        ContextClickIndex = -1;
        List.DoLayoutList();
    }

    public SerializedProperty GetContextElement()
    {
        if (ContextClickIndex < 0) return null;
        var prop = serializedObject.FindProperty(propertyPath);
        if (ContextClickIndex < prop.arraySize)
            return prop.GetArrayElementAtIndex(ContextClickIndex);
        return null;
    }

    public void ShowContextMenu(Action<int> additionalItems = null)
    {
        if (ContextClickIndex < 0) return;

        var prop = serializedObject.FindProperty(propertyPath);
        if (ContextClickIndex >= prop.arraySize) return;

        var element = prop.GetArrayElementAtIndex(ContextClickIndex);
        var menu = new GenericMenu();

        if (onEdit != null)
        {
            int idx = ContextClickIndex;
            menu.AddItem(new GUIContent("编辑"), false, () => onEdit(idx));
            menu.AddSeparator("");
        }

        menu.AddItem(new GUIContent("复制元素"), false, () =>
        {
            serializedObject.Update();
            prop.InsertArrayElementAtIndex(ContextClickIndex);
            serializedObject.ApplyModifiedProperties();
            onChanged?.Invoke();
        });

        menu.AddItem(new GUIContent("删除元素"), false, () =>
        {
            serializedObject.Update();
            prop.DeleteArrayElementAtIndex(ContextClickIndex);
            serializedObject.ApplyModifiedProperties();
            onChanged?.Invoke();
        });

        menu.AddSeparator("");
        SerializedPropertyContextMenu.AddStandardItems(menu, element, onChanged);
        additionalItems?.Invoke(ContextClickIndex);

        menu.ShowAsContext();
        Event.current.Use();
    }
}