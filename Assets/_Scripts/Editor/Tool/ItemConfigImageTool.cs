#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
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
        private ItemConfig targetConfig;
        private Vector2 scrollPosition;

        [MenuItem("Tools/ItemConfig/自动加载图片")]
        public static void ShowWindow()
        {
            var window = GetWindow<ItemConfigImageTool>("自动加载图片");
            window.minSize = new Vector2(500, 400);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("ItemConfig 图片自动加载工具", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // 选择 ItemConfig
            targetConfig = (ItemConfig)EditorGUILayout.ObjectField("ItemConfig", targetConfig, typeof(ItemConfig), false);

            if (targetConfig == null)
            {
                EditorGUILayout.HelpBox("请拖入 ItemConfig 资源", MessageType.Info);
                return;
            }

            EditorGUILayout.Space();

            // 图片路径设置
            EditorGUILayout.LabelField("图片路径设置", EditorStyles.boldLabel);
            string itemIconPath = EditorGUILayout.TextField("物品图标路径", "Resources/UI/Items/");

            EditorGUILayout.Space();

            // 预览和按钮
            int totalIcons = 0;
            foreach (var itemData in targetConfig.itemDataList)
            {
                if (itemData?.icon != null)
                    totalIcons += itemData.icon.Count;
            }

            EditorGUILayout.LabelField($"待处理物品数量: {targetConfig.itemDataList.Count}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"当前图标总数: {totalIcons}", EditorStyles.boldLabel);

            EditorGUILayout.Space();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            foreach (var itemData in targetConfig.itemDataList)
            {
                if (itemData == null) continue;

                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField($"物品: {itemData.itemID}", EditorStyles.boldLabel);

                // 显示当前状态
                int iconCount = itemData.icon != null ? itemData.icon.Count : 0;
                EditorGUILayout.LabelField($"  图标数量: {iconCount}");

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();

            GUI.enabled = targetConfig != null;
            if (GUILayout.Button("自动加载所有图片", GUILayout.Height(40)))
            {
                LoadAllImages(targetConfig, itemIconPath);
            }

            if (GUILayout.Button("仅加载缺失的图片", GUILayout.Height(30)))
            {
                LoadMissingImages(targetConfig, itemIconPath);
            }

            if (GUILayout.Button("清除所有图标", GUILayout.Height(30)))
            {
                ClearAllIcons(targetConfig);
            }

            GUI.enabled = true;
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

            Debug.Log($"物品图片加载完成: 成功 {successCount}, 失败 {failCount}");
            if (missingFiles.Count > 0)
            {
                Debug.LogWarning($"缺失的图片文件 ({missingFiles.Count}个):\n" + string.Join("\n", missingFiles));
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

            Debug.Log($"物品图片加载完成: 加载 {loadedCount}, 跳过 {skipCount}");
            if (missingFiles.Count > 0)
            {
                Debug.LogWarning($"缺失的图片文件 ({missingFiles.Count}个):\n" + string.Join("\n", missingFiles));
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
                    Debug.LogWarning($"物品 {itemName} 没有任何真实图标，已自动添加 null 占位");
                }
                else
                {
                    Debug.LogError($"物品 {itemName} 连 null 占位图片都找不到！请确保 {iconPath}null 图片存在");
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
                Debug.LogWarning($"未找到图片: {fileName}，使用 null 默认图片");
                return nullSprite;
            }

            Debug.LogWarning($"未找到图片: {fileName}，且未找到 null 默认图片");
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

            Debug.Log($"已清除 {clearedCount} 个物品的图标");
            EditorUtility.DisplayDialog("完成", $"已清除 {clearedCount} 个物品的图标", "确定");
        }
    }
    #endif
}


