#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// UnitConfig 图片自动加载工具
/// </summary>
public class UnitConfigImageTool : EditorWindow
{
    private UnitConfig targetConfig;
    private Vector2 scrollPosition;

    [MenuItem("Tools/UnitConfig/自动加载图片")]
    public static void ShowWindow()
    {
        var window = GetWindow<UnitConfigImageTool>("自动加载图片");
        window.minSize = new Vector2(500, 400);
        window.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("UnitConfig 图片自动加载工具", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // 选择 UnitConfig
        targetConfig = (UnitConfig)EditorGUILayout.ObjectField("UnitConfig", targetConfig, typeof(UnitConfig), false);

        if (targetConfig == null)
        {
            EditorGUILayout.HelpBox("请拖入 UnitConfig 资源", MessageType.Info);
            return;
        }

        EditorGUILayout.Space();

        // 图片路径设置
        EditorGUILayout.LabelField("图片路径设置", EditorStyles.boldLabel);
        string avatarPath = EditorGUILayout.TextField("头像路径", "Resources/UI/Avatars/");
        string cardPath = EditorGUILayout.TextField("卡片路径", "Resources/UI/Cards/");
        string nameCardPath = EditorGUILayout.TextField("名卡路径", "Resources/UI/NameCards/");

        // 添加命名方式选项
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("命名方式", EditorStyles.boldLabel);
        bool useSnakeCase = EditorGUILayout.Toggle("使用 snake_case (例如: intertwined_fate)", true);

        EditorGUILayout.Space();

        // 预览和按钮
        EditorGUILayout.LabelField($"待处理角色数量: {targetConfig.unitDataList.Count}", EditorStyles.boldLabel);
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        foreach (var unitData in targetConfig.unitDataList)
        {
            if (unitData == null) continue;

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField($"角色: {unitData.unitName}", EditorStyles.boldLabel);

            // 显示当前状态
            bool hasAvatar = unitData.avatar != null;
            bool hasNameCard = unitData.nameCard != null;
            int cardCount = unitData.cards != null ? unitData.cards.Count : 0;

            EditorGUILayout.LabelField($"  头像: {(hasAvatar ? "✓" : "✗")}");
            EditorGUILayout.LabelField($"  名卡: {(hasNameCard ? "✓" : "✗")}");
            EditorGUILayout.LabelField($"  卡片皮肤数: {cardCount}");

            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();

        GUI.enabled = targetConfig != null;
        if (GUILayout.Button("自动加载所有图片", GUILayout.Height(40)))
        {
            LoadAllImages(targetConfig, avatarPath, cardPath, nameCardPath, useSnakeCase);
        }

        if (GUILayout.Button("仅加载缺失的图片", GUILayout.Height(30)))
        {
            LoadMissingImages(targetConfig, avatarPath, cardPath, nameCardPath, useSnakeCase);
        }

        GUI.enabled = true;
    }

    private void LoadAllImages(UnitConfig config, string avatarPath, string cardPath, string nameCardPath, bool useSnakeCase)
    {
        int successCount = 0;
        int failCount = 0;
        var missingFiles = new List<string>();

        foreach (var unitData in config.unitDataList)
        {
            if (unitData == null) continue;

            bool success = LoadImagesForUnit(unitData, avatarPath, cardPath, nameCardPath, missingFiles, useSnakeCase);
            if (success)
                successCount++;
            else
                failCount++;
        }

        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();

        Debug.Log($"图片加载完成: 成功 {successCount}, 失败 {failCount}");
        if (missingFiles.Count > 0)
        {
            Debug.LogWarning($"缺失的图片文件 ({missingFiles.Count}个):\n" + string.Join("\n", missingFiles));
        }

        EditorUtility.DisplayDialog("完成", $"图片加载完成\n成功: {successCount}\n失败: {failCount}\n缺失文件数: {missingFiles.Count}", "确定");
    }

    private void LoadMissingImages(UnitConfig config, string avatarPath, string cardPath, string nameCardPath, bool useSnakeCase)
    {
        int loadedCount = 0;
        int skipCount = 0;
        var missingFiles = new List<string>();

        foreach (var unitData in config.unitDataList)
        {
            if (unitData == null) continue;

            bool needsUpdate = false;

            // 检查头像
            if (unitData.avatar == null)
            {
                var avatar = LoadSprite(avatarPath, GetFileName(unitData.unitName.ToString(), useSnakeCase), missingFiles);
                if (avatar != null)
                {
                    unitData.avatar = avatar;
                    loadedCount++;
                }
                needsUpdate = true;
            }

            // 检查名卡
            if (unitData.nameCard == null)
            {
                var nameCard = LoadSprite(nameCardPath, GetFileName(unitData.unitName.ToString(), useSnakeCase), missingFiles);
                if (nameCard != null)
                {
                    unitData.nameCard = nameCard;
                    loadedCount++;
                }
                needsUpdate = true;
            }

            // 检查卡片皮肤
            if (unitData.cards == null || unitData.cards.Count == 0)
            {
                bool cardLoaded = LoadCardsForUnit(unitData, cardPath, missingFiles, useSnakeCase);
                if (cardLoaded)
                    loadedCount++;
                needsUpdate = true;
            }

            if (!needsUpdate)
                skipCount++;
        }

        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();

        Debug.Log($"图片加载完成: 加载 {loadedCount}, 跳过 {skipCount}");
        if (missingFiles.Count > 0)
        {
            Debug.LogWarning($"缺失的图片文件 ({missingFiles.Count}个):\n" + string.Join("\n", missingFiles));
        }

        EditorUtility.DisplayDialog("完成", $"图片加载完成\n加载: {loadedCount}\n跳过: {skipCount}\n缺失文件数: {missingFiles.Count}", "确定");
    }

    private bool LoadImagesForUnit(UnitConfig.UnitData unitData, string avatarPath, string cardPath, string nameCardPath, List<string> missingFiles, bool useSnakeCase)
    {
        bool allSuccess = true;

        // 加载头像
        string avatarName = GetFileName(unitData.unitName.ToString(), useSnakeCase);
        var avatar = LoadSprite(avatarPath, avatarName, missingFiles);
        if (avatar != null)
            unitData.avatar = avatar;
        else
            allSuccess = false;

        // 加载名卡
        var nameCard = LoadSprite(nameCardPath, avatarName, missingFiles);
        if (nameCard != null)
            unitData.nameCard = nameCard;
        else
            allSuccess = false;

        // 加载卡片皮肤
        bool cardSuccess = LoadCardsForUnit(unitData, cardPath, missingFiles, useSnakeCase);
        if (!cardSuccess)
            allSuccess = false;

        return allSuccess;
    }

    private bool LoadCardsForUnit(UnitConfig.UnitData unitData, string cardPath, List<string> missingFiles, bool useSnakeCase)
    {
        string baseName = GetFileName(unitData.unitName.ToString(), useSnakeCase);

        if (unitData.cards == null)
            unitData.cards = new List<Sprite>();
        else
            unitData.cards.Clear();

        // 尝试加载皮肤 0, 1, 2...
        int skinIndex = 0;
        bool hasMore = true;
        List<Sprite> tempCards = new List<Sprite>(); // 临时存储找到的真实卡片

        while (hasMore)
        {
            string fileName = skinIndex == 0 ? baseName : $"{baseName}_{skinIndex}";
            var sprite = LoadSprite(cardPath, fileName, missingFiles);

            if (sprite != null)
            {
                bool isNull = IsNullSprite(sprite, cardPath);

                if (!isNull)
                {
                    // 真实卡片，添加到临时列表并继续查找下一个皮肤
                    tempCards.Add(sprite);
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

        // 将找到的真实卡片添加到 unitData.cards
        if (tempCards.Count > 0)
        {
            unitData.cards.AddRange(tempCards);
        }
        else
        {
            // 完全没有找到任何真实卡片，尝试加载 null 默认图片作为保底
            Sprite nullSprite = LoadSprite(cardPath, "null", missingFiles);
            if (nullSprite != null)
            {
                unitData.cards.Add(nullSprite);
                missingFiles?.Add($"{cardPath}{baseName}_主卡片");
                Debug.LogWarning($"角色 {unitData.unitName} 没有任何真实卡片，已自动添加 null 占位");
            }
            else
            {
                Debug.LogError($"角色 {unitData.unitName} 连 null 占位图片都找不到！请确保 {cardPath}null 图片存在");
            }
        }

        return unitData.cards.Count > 0;
    }

    private string GetFileName(string input, bool useSnakeCase)
    {
        if (useSnakeCase)
            return input.ToSnakeCase();
        return input;
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
                return sprite; // 返回 null 图片，但已记录缺失
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

        // 检查是否是 null 图片（支持 png、jpg 等格式）
        return assetPath != null && (
            assetPath.Contains($"{nullSpritePath}.png") ||
            assetPath.Contains($"{nullSpritePath}.jpg") ||
            assetPath.Contains($"{nullSpritePath}.jpeg") ||
            assetPath.Contains($"{nullSpritePath}.psd")
        );
    }
}
#endif