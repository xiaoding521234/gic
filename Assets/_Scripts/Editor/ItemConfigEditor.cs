// ============================================
// ItemConfigEditor - UI Toolkit 版 Inspector 与编辑窗口
// ============================================

using System;
using System.Collections.Generic;
using System.IO;
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
        private const string IconPathKey = "ItemConfigEditor_IconPath";
        private const string DefaultIconPath = "Resources/UI/Items/";

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

        // ==================== 批量填充工具（Inspector 底部） ====================

        protected override VisualElement BuildTools(SerializedObject so)
        {
            var section = ConfigEditorUITK.CreateToolsSection("批量填充工具");

            var pathField = new TextField("物品图标路径") { value = EditorPrefs.GetString(IconPathKey, DefaultIconPath) };
            pathField.RegisterValueChangedCallback(e => EditorPrefs.SetString(IconPathKey, e.newValue));
            section.Add(pathField);

            string iconPath = pathField.value;
            var config = (ItemConfig)target;

            section.Add(ConfigEditorUITK.CreateToolButton("自动加载所有图片", () => LoadAllImages(config, iconPath)));
            section.Add(ConfigEditorUITK.CreateToolButton("仅加载缺失的图片", () => LoadMissingImages(config, iconPath)));
            section.Add(ConfigEditorUITK.CreateToolButton("清除所有图标", () => ClearAllIcons(config), danger: true));
            return section;
        }

        private void LoadAllImages(ItemConfig config, string iconPath)
        {
            int successCount = 0;
            int failCount = 0;
            var missingFiles = new List<string>();

            foreach (var itemData in config.itemDataList)
            {
                if (itemData == null) continue;

                bool success = LoadImagesForItem(itemData, iconPath, missingFiles);
                if (success)
                    successCount++;
                else
                    failCount++;
            }

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            GICLog.Info($"物品图片加载完成: 成功 {successCount}, 失败 {failCount}");
            if (missingFiles.Count > 0)
            {
                GICLog.Warn($"缺失的图片文件 ({missingFiles.Count}个):\n" + string.Join("\n", missingFiles));
            }

            EditorUtility.DisplayDialog("完成", $"图片加载完成\n成功: {successCount}\n失败: {failCount}\n缺失文件数: {missingFiles.Count}", "确定");
        }

        private void LoadMissingImages(ItemConfig config, string iconPath)
        {
            int loadedCount = 0;
            int skipCount = 0;
            var missingFiles = new List<string>();

            foreach (var itemData in config.itemDataList)
            {
                if (itemData == null) continue;

                bool hasIcon = itemData.icon != null && itemData.icon.Count > 0;

                if (!hasIcon)
                {
                    bool success = LoadImagesForItem(itemData, iconPath, missingFiles);
                    if (success)
                        loadedCount++;
                }
                else
                {
                    skipCount++;
                }
            }

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            GICLog.Info($"物品图片加载完成: 加载 {loadedCount}, 跳过 {skipCount}");
            if (missingFiles.Count > 0)
            {
                GICLog.Warn($"缺失的图片文件 ({missingFiles.Count}个):\n" + string.Join("\n", missingFiles));
            }

            EditorUtility.DisplayDialog("完成", $"图片加载完成\n加载: {loadedCount}\n跳过: {skipCount}\n缺失文件数: {missingFiles.Count}", "确定");
        }

        private bool LoadImagesForItem(ItemConfig.ItemData itemData, string iconPath, List<string> missingFiles)
        {
            string itemName = itemData.itemID.ToString().ToSnakeCase();

            if (itemData.icon == null)
                itemData.icon = new List<Sprite>();
            else
                itemData.icon.Clear();

            // 尝试加载皮肤 0, 1, 2...（支持多个图标变体）
            int skinIndex = 0;
            bool hasMore = true;
            List<Sprite> tempIcons = new List<Sprite>(); // 临时存储找到的真实图片

            while (hasMore)
            {
                string fileName = skinIndex == 0 ? itemName : $"{itemName}_{skinIndex}";
                var sprite = LoadSprite(iconPath, fileName, missingFiles);

                if (sprite != null)
                {
                    bool isNull = IsNullSprite(sprite, iconPath);

                    if (!isNull)
                    {
                        // 真实图片，添加到临时列表并继续查找下一个皮肤
                        tempIcons.Add(sprite);
                        skinIndex++;
                    }
                    else
                    {
                        // 找到了 null 图片，停止继续查找（但不添加）
                        hasMore = false;
                    }
                }
                else
                {
                    hasMore = false;
                }
            }

            // 将找到的真实图片添加到 itemData.icon
            if (tempIcons.Count > 0)
            {
                itemData.icon.AddRange(tempIcons);
            }
            else
            {
                // 完全没有找到任何真实图片，尝试加载 null 默认图片作为保底
                Sprite nullSprite = LoadSprite(iconPath, "null", missingFiles);
                if (nullSprite != null)
                {
                    itemData.icon.Add(nullSprite);
                    missingFiles?.Add($"{iconPath}{itemName}_主图标");
                    GICLog.Warn($"物品 {itemName} 没有任何真实图标，已自动添加 null 占位");
                }
                else
                {
                    GICLog.Error($"物品 {itemName} 连 null 占位图片都找不到！请确保 {iconPath}null 图片存在");
                }
            }

            return itemData.icon.Count > 0;
        }

        private Sprite LoadSprite(string folderPath, string fileName, List<string> missingFiles)
        {
            // 构建完整路径（去掉 Resources/ 前缀）
            string cleanPath = folderPath.Replace("Resources/", "");

            // 尝试直接加载
            string fullPath = $"{cleanPath}{fileName}";
            var sprite = Resources.Load<Sprite>(fullPath);

            if (sprite != null)
            {
                // 检查是否是 null 占位图片
                if (IsNullSprite(sprite, folderPath))
                {
                    missingFiles?.Add($"{folderPath}{fileName}");
                    return sprite;
                }
                return sprite;
            }

            // 尝试在子文件夹中查找
            string resourcesPath = Application.dataPath + "/Resources/" + cleanPath;
            if (Directory.Exists(resourcesPath))
            {
                string[] subFolders = Directory.GetDirectories(resourcesPath);
                foreach (string subFolder in subFolders)
                {
                    string folderName = Path.GetFileName(subFolder);
                    string subPath = $"{cleanPath}{folderName}/{fileName}";
                    sprite = Resources.Load<Sprite>(subPath);
                    if (sprite != null)
                    {
                        if (IsNullSprite(sprite, folderPath))
                        {
                            missingFiles?.Add($"{folderPath}{fileName}");
                            return sprite;
                        }
                        return sprite;
                    }
                }
            }

            // 尝试加载 null 默认图片
            Sprite nullSprite = Resources.Load<Sprite>($"{cleanPath}null");
            if (nullSprite != null)
            {
                missingFiles?.Add($"{folderPath}{fileName}");
                GICLog.Warn($"未找到图片: {fileName}，使用 null 默认图片");
                return nullSprite;
            }

            GICLog.Warn($"未找到图片: {fileName}，且未找到 null 默认图片");
            return null;
        }

        private bool IsNullSprite(Sprite sprite, string folderPath)
        {
            if (sprite == null) return false;

            string cleanPath = folderPath.Replace("Resources/", "");
            string nullSpritePath = $"{cleanPath}null";

            // 通过资源路径判断是否为 null 图片
            string assetPath = AssetDatabase.GetAssetPath(sprite);

            // 检查是否是 null 图片（支持多种图片格式）
            return assetPath != null && (
                assetPath.Contains($"{nullSpritePath}.png") ||
                assetPath.Contains($"{nullSpritePath}.jpg") ||
                assetPath.Contains($"{nullSpritePath}.jpeg") ||
                assetPath.Contains($"{nullSpritePath}.psd")
            );
        }

        private void ClearAllIcons(ItemConfig config)
        {
            if (!EditorUtility.DisplayDialog("确认清除", "确定要清除所有物品的图标吗？此操作不可撤销！", "确定", "取消"))
                return;

            int clearedCount = 0;
            foreach (var itemData in config.itemDataList)
            {
                if (itemData != null && itemData.icon != null)
                {
                    itemData.icon.Clear();
                    clearedCount++;
                }
            }

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            GICLog.Info($"已清除 {clearedCount} 个物品的图标");
            EditorUtility.DisplayDialog("完成", $"已清除 {clearedCount} 个物品的图标", "确定");
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
