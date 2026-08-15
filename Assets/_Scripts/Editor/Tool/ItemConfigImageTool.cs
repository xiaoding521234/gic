#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using System.IO;
using System.Collections.Generic;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Editor
{


    /// <summary>
    /// ItemConfig 图片自动加载工具
    /// </summary>
    public class ItemConfigImageTool : EditorWindow
    {
        [SerializeField] private ItemConfig targetConfig;
        // 旧 IMGUI 版 TextField 传常量导致编辑不生效，改为序列化字段持久化
        [SerializeField] private string itemIconPath = "Resources/UI/Items/";

        private VisualElement statsContainer;
        private VisualElement itemListContainer;

        [MenuItem("Tools/ItemConfig/自动加载图片")]
        public static void ShowWindow()
        {
            var window = GetWindow<ItemConfigImageTool>("自动加载图片");
            window.minSize = new Vector2(500, 500);
            window.Show();
        }

        // CreateGUI 为按名调用的魔法方法（Tuanjie 中非虚方法），不加 override
        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingLeft = 10;
            root.style.paddingRight = 10;
            root.style.paddingTop = 8;

            root.Add(ConfigEditorUITK.CreateTitleRow("ItemConfig 图片自动加载工具", 0));

            var objField = new ObjectField("ItemConfig")
            {
                objectType = typeof(ItemConfig),
                allowSceneObjects = false,
            };
            objField.value = targetConfig;
            objField.RegisterValueChangedCallback(e =>
            {
                targetConfig = (ItemConfig)e.newValue;
                RefreshOverview();
                buttonsContainer.SetEnabled(targetConfig != null);
            });
            root.Add(objField);

            if (targetConfig == null)
            {
                root.Add(new HelpBox("请拖入 ItemConfig 资源", HelpBoxMessageType.Info));
                return;
            }

            root.Add(ConfigEditorUITK.CreateSectionHeader("图片路径设置"));
            var pathField = new TextField("物品图标路径");
            pathField.value = itemIconPath;
            pathField.RegisterValueChangedCallback(e => itemIconPath = e.newValue);
            root.Add(pathField);

            statsContainer = new VisualElement();
            statsContainer.style.marginTop = 8;
            root.Add(statsContainer);

            var scroll = new ScrollView();
            scroll.style.flexGrow = 1f;
            scroll.style.marginTop = 6;
            itemListContainer = new VisualElement();
            scroll.Add(itemListContainer);
            root.Add(scroll);

            buttonsContainer = new VisualElement();
            buttonsContainer.Add(ConfigEditorUITK.CreatePrimaryButton("自动加载所有图片", () =>
            {
                LoadAllImages(targetConfig, itemIconPath);
                RefreshOverview();
            }));
            buttonsContainer[0].style.height = 40;
            buttonsContainer.Add(ConfigEditorUITK.CreateToolButton("仅加载缺失的图片", () =>
            {
                LoadMissingImages(targetConfig, itemIconPath);
                RefreshOverview();
            }));
            buttonsContainer.Add(ConfigEditorUITK.CreateToolButton("清除所有图标", () =>
            {
                ClearAllIcons(targetConfig);
                RefreshOverview();
            }, danger: true));
            root.Add(buttonsContainer);

            RefreshOverview();
        }

        private VisualElement buttonsContainer;

        private void RefreshOverview()
        {
            if (statsContainer == null || itemListContainer == null) return;

            statsContainer.Clear();
            itemListContainer.Clear();

            if (targetConfig == null) return;

            int totalIcons = 0;
            foreach (var itemData in targetConfig.itemDataList)
            {
                if (itemData?.icon != null)
                    totalIcons += itemData.icon.Count;
            }

            AddStatLabel($"待处理物品数量: {targetConfig.itemDataList.Count}");
            AddStatLabel($"当前图标总数: {totalIcons}");

            foreach (var itemData in targetConfig.itemDataList)
            {
                if (itemData == null) continue;

                int iconCount = itemData.icon != null ? itemData.icon.Count : 0;

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.paddingLeft = 6;
                row.style.paddingTop = 3;
                row.style.paddingBottom = 3;
                row.style.borderBottomWidth = 1;
                row.style.borderBottomColor = new Color(0.4f, 0.4f, 0.4f, 0.25f);

                var name = new Label($"物品: {itemData.itemID}");
                name.style.flexGrow = 1f;
                row.Add(name);

                var count = new Label($"图标数量: {iconCount}");
                count.style.color = new Color(0.6f, 0.6f, 0.6f);
                row.Add(count);

                itemListContainer.Add(row);
            }
        }

        private void AddStatLabel(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 13;
            label.style.color = new Color(0.85f, 0.87f, 0.9f);
            statsContainer.Add(label);
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
    #endif

}


