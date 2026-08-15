// ============================================
// ItemConfigEditor - UI Toolkit 版 Inspector 与编辑窗口
// ============================================

using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using GIC.Framework;
using GIC.Data;
using GIC.Tool;

namespace GIC.Editor
{
    [CustomEditor(typeof(ItemConfig))]
    public class ItemConfigEditor : ConfigInspectorBase
    {
        protected override string ListPropertyName => "itemDataList";
        protected override string ListHeaderTitle => "物品数据列表";

        protected override string GetElementName(SerializedProperty element)
            => ((ItemName)element.FindPropertyRelative("itemID").intValue).GetInspectorName();

        protected override string GetElementBadge(SerializedProperty element)
            => GetSubTypeDisplayName(element.FindPropertyRelative("subType"));

        protected override int GetElementStars(SerializedProperty element)
            => element.FindPropertyRelative("starLevel").intValue;

        protected override void EditElement(int index)
            => ItemDataEditorWindow.OpenWindow((ItemConfig)target, index);

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

    // ========== 编辑窗口：继承 ElementDataEditorWindowBase（全字段滚动 + 保存到磁盘） ==========

    public class ItemDataEditorWindow : ElementDataEditorWindowBase
    {
        protected override string ListPropertyName => "itemDataList";
        protected override string WindowTitle => "编辑物品数据";
        protected override string MissingDataMessage => "物品数据不存在，可能已被删除";

        protected override string ReadDisplayName(SerializedProperty element)
            => ((ItemName)element.FindPropertyRelative("itemID").intValue).GetInspectorName();

        protected override int ReadStarLevel(SerializedProperty element)
            => element.FindPropertyRelative("starLevel").intValue;

        public static void OpenWindow(ItemConfig config, int index)
        {
            var window = GetWindow<ItemDataEditorWindow>("编辑物品数据");
            window.OpenInternal(config, index, 500f, 900f);
        }
    }

    /// <summary>
    /// 自定义 Element 类的 Inspector 显示（IMGUI + UI Toolkit 双模式）
    /// </summary>
    [CustomPropertyDrawer(typeof(Element))]
    public class ElementDrawer : PropertyDrawer
    {
        // ========== IMGUI 版（默认 Inspector 回退等 IMGUI 上下文使用） ==========

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
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

        // ========== UI Toolkit 版（配置窗口 PropertyField 使用） ==========

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            var customLabel = new Label();
            customLabel.style.width = 60;
            row.Add(customLabel);

            var typeField = new PropertyField(property.FindPropertyRelative("elementType"), "");
            var stacksField = new PropertyField(property.FindPropertyRelative("stacks"), "");
            typeField.style.flexGrow = 1f;
            stacksField.style.flexGrow = 1f;
            row.Add(typeField);
            row.Add(stacksField);

            void UpdateLabel()
            {
                var elementTypeValue = (ElementType)property.FindPropertyRelative("elementType").intValue;
                int stacks = property.FindPropertyRelative("stacks").intValue;
                customLabel.text = $"{stacks}{elementTypeValue.GetShortName()}";
            }

            typeField.RegisterValueChangeCallback(_ => UpdateLabel());
            stacksField.RegisterValueChangeCallback(_ => UpdateLabel());
            UpdateLabel();

            row.Bind(property.serializedObject);
            return row;
        }
    }
}
