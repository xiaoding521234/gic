using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement.AsyncOperations;
using TMPro;
using System.Collections.Generic;
using System.Text;
using System;

public class TextCombiner : MonoBehaviour
{

    [Header("条目列表")]
    public List<TextEntry> entries = new List<TextEntry>();

    [Header("字体设置")]
    [Tooltip("本地化字体资产表")]
    private LocalizedAssetTable fontTable = new LocalizedAssetTable(TableName.UIAssets.ToString());

    [Tooltip("字体在资产表中的Key")]
    private string fontEntryKey = "MainFont";

    [Header("全局设置")]
    public string globalPrefix = "";
    public string globalSuffix = "";
    public bool skipEmpty = true;

    public TMP_Text textComponent;
    private Dictionary<string, string> resolvedValues = new Dictionary<string, string>();
    private TMP_FontAsset currentFont;

    void Awake()
    {
        if (textComponent == null)
        {
            textComponent = GetComponent<TMP_Text>();
        }
        
        if (textComponent == null)
        {
            
            Debug.LogError($"TMP_TextCombiner: 在 {gameObject.name} 上未找到 TMP_Text 组件", this);
            return;
        }

        // 注册条目更新回调
        for (int i = 0; i < entries.Count; i++)
        {
            int index = i;
            var entry = entries[i];
            if (entry.localizedString == null)
            {
                if (!string.IsNullOrEmpty(entry.leadingSeparator))
                {
                }
                else
                {
                    Debug.LogWarning($"TextCombiner [{gameObject.name}]: 条目 {i} 是空的（既无 localizedString 也无文本内容），请检查是否误添加了空 Entry", this);
                }
                continue;
            }
            if (entry.localizedString.IsEmpty)
            {
                Debug.LogWarning($"TextCombiner [{gameObject.name}]: 条目 {i} 的 localizedString 未配置（Table 或 Key 为空），请检查 Inspector", this);
                continue;
            }
            string key = entry.localizedString.TableEntryReference.Key;
            long keyId = entry.localizedString.TableEntryReference.KeyId;
            string tableName = entry.localizedString.TableReference.TableCollectionName;
            Debug.Log($"TextCombiner [{gameObject.name}] 条目 {i}: Table={tableName}, Key={key}, KeyId={keyId}");

            entry.localizedString.StringChanged += (value) => OnEntryUpdated(index, value);
        }
    }

    void Start()
    {
        RefreshAll();
    }

    public void RefreshAll()
    {
        LoadFont();
        foreach (var entry in entries)
        {
            if (entry.localizedString != null && !entry.localizedString.IsEmpty)
            {
                entry.localizedString.RefreshString();
            }
        }
    }

    public string GetCombinedText()
    {
        return textComponent != null ? textComponent.text : "";
    }

    /// <summary>
    /// 添加一个本地化条目
    /// </summary>
    /// <param name="localizedString">本地化字符串引用</param>
    /// <param name="leadingSeparator">前置连接符（支持\n换行）</param>
    public void AddEntry(LocalizedString localizedString, string leadingSeparator = "")
    {
        var entry = new TextEntry(localizedString, leadingSeparator);
        AddEntry(entry);
    }

    /// <summary>
    /// 添加一个已构造好的 Entry 对象
    /// </summary>
    /// <param name="entry">要添加的条目</param>
    public void AddEntry(TextEntry entry)
    {
        int index = entries.Count;
        entries.Add(entry);

        // 如果是有效的本地化条目，注册回调并刷新
        if (entry.localizedString != null && !entry.localizedString.IsEmpty)
        {
            entry.localizedString.StringChanged += (value) => OnEntryUpdated(index, value);
            entry.localizedString.RefreshString();
        }
        else
        {
            // 静态条目直接刷新显示
            UpdateDisplay();
        }
    }

    /// <summary>
    /// 添加一个静态文本条目（不需要本地化）
    /// </summary>
    /// <param name="staticText">静态文本内容</param>
    public void AddStaticEntry(string staticText)
    {
        // 创建一个没有 localizedString 的 Entry，静态文本作为 leadingSeparator
        var entry = new TextEntry(null, staticText);
        int index = entries.Count;
        entries.Add(entry);
        // 静态文本不需要注册回调，直接刷新显示
        UpdateDisplay();
    }

    /// <summary>
    /// 移除指定索引的条目
    /// </summary>
    /// <param name="index">要移除的条目索引</param>
    public void RemoveEntry(int index)
    {
        if (index < 0 || index >= entries.Count)
        {
            Debug.LogWarning($"TMP_TextCombiner: 移除条目失败，索引 {index} 越界");
            return;
        }

        var entry = entries[index];

        // 清理回调
        if (entry.localizedString != null && !entry.localizedString.IsEmpty)
        {
            entry.localizedString.StringChanged -= null;
        }

        // 清理缓存值
        string key = GetEntryKey(index);
        resolvedValues.Remove(key);

        // 移除条目
        entries.RemoveAt(index);

        // 重新注册索引 >= index 的条目的回调（因为索引变了）
        RebuildCallbacks();

        UpdateDisplay();
    }

    /// <summary>
    /// 清空所有条目
    /// </summary>
    public void ClearAllEntries()
    {
        // 清理所有回调
        foreach (var entry in entries)
        {
            if (entry.localizedString != null)
                entry.localizedString.StringChanged -= null;
        }

        entries.Clear();
        resolvedValues.Clear();
        UpdateDisplay();
    }

    /// <summary>
    /// 清空所有现有条目，然后只添加指定的单个条目
    /// </summary>
    /// <param name="entry">要设置为唯一条目的Entry对象</param>
    public void SetSingleEntry(TextEntry entry)
    {
        if (entry == null)
        {
            Debug.LogWarning("TextCombiner.SetSingleEntry: entry 为空", this);
            return;
        }

        // 清空所有现有条目
        ClearAllEntries();

        // 添加新条目
        int index = entries.Count;
        entries.Add(entry);

        // 如果是有效的本地化条目，注册回调并刷新
        if (entry.localizedString != null && !entry.localizedString.IsEmpty)
        {
            entry.localizedString.StringChanged += (value) => OnEntryUpdated(index, value);
            entry.localizedString.RefreshString();
        }
        else if (!string.IsNullOrEmpty(entry.leadingSeparator))
        {
            // 纯连接符的情况（没有本地化字符串但有连接符）
            UpdateDisplay();
        }
        else
        {
            // 空条目
            UpdateDisplay();
        }
    }

    /// <summary>
    /// 清空所有现有条目，然后只添加指定的单个本地化字符串
    /// </summary>
    /// <param name="localizedString">本地化字符串引用</param>
    /// <param name="leadingSeparator">前置连接符</param>
    public void SetSingleEntry(LocalizedString localizedString, string leadingSeparator = "")
    {
        TextEntry entry = new TextEntry(localizedString, leadingSeparator);
        SetSingleEntry(entry);
    }

    /// <summary>
    /// 清空所有现有条目，然后只添加指定的单个静态文本
    /// </summary>
    /// <param name="staticText">静态文本内容</param>
    public void SetSingleEntry(string staticText)
    {
        // 创建一个没有本地化引用的 Entry，静态文本作为 leadingSeparator 存储
        TextEntry entry = new TextEntry(null, staticText);
        SetSingleEntry(entry);
    }

    /// <summary>
    /// 重建所有回调（在移除条目后调用，修正索引偏移）
    /// </summary>
    private void RebuildCallbacks()
    {
        // 先清除所有回调
        foreach (var entry in entries)
        {
            if (entry.localizedString != null)
                entry.localizedString.StringChanged -= null;
        }

        resolvedValues.Clear();

        // 重新注册
        for (int i = 0; i < entries.Count; i++)
        {
            int index = i;
            var entry = entries[index];
            if (entry.localizedString != null && !entry.localizedString.IsEmpty)
            {
                entry.localizedString.StringChanged += (value) => OnEntryUpdated(index, value);
                // 主动刷新一次获取当前值
                entry.localizedString.RefreshString();
            }
        }
    }

    private void LoadFont()
    {
        if (fontTable == null || string.IsNullOrEmpty(fontEntryKey))
            return;

        var tableOp = fontTable.GetTableAsync();
        tableOp.Completed += (op) =>
        {
            if (op.Status == AsyncOperationStatus.Succeeded && op.Result != null)
            {
                var entry = op.Result.GetEntry(fontEntryKey);
                if (entry != null && !entry.IsEmpty)
                {
                    var assetOp = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<TMP_FontAsset>(entry.Guid);
                    assetOp.Completed += (assetHandle) =>
                    {
                        if (assetHandle.Status == AsyncOperationStatus.Succeeded)
                        {
                            currentFont = assetHandle.Result;
                            ApplyFont();
                        }
                    };
                }
            }
        };
    }

    private void ApplyFont()
    {
        if (currentFont != null && textComponent != null)
        {
            textComponent.font = currentFont;
            if (currentFont.material != null)
            {
                textComponent.fontMaterial = currentFont.material;
            }
        }
    }

    private void OnEntryUpdated(int index, string value)
    {
        if (index < 0 || index >= entries.Count) return;
        string key = GetEntryKey(index);
        resolvedValues[key] = value;
        UpdateDisplay();
    }

    private string GetEntryKey(int index)
    {
        var entry = entries[index];

        // 使用索引确保唯一性
        if (entry.localizedString != null && !entry.localizedString.IsEmpty)
        {
            string key = entry.localizedString.TableEntryReference.Key;
            long keyId = entry.localizedString.TableEntryReference.KeyId;
            string tableName = entry.localizedString.TableReference.TableCollectionName;

            // 组合 table + key + index 确保每个条目有唯一键
            if (!string.IsNullOrEmpty(key))
                return $"{tableName}_{key}_{index}";

            if (keyId != 0)
                return $"{tableName}_id_{keyId}_{index}";
        }

        return $"entry_{index}";
    }


    private void UpdateDisplay()
    {
        if (textComponent == null) return;

        ApplyFont();

        StringBuilder sb = new StringBuilder();
        if (!string.IsNullOrEmpty(globalPrefix))
            sb.Append(globalPrefix);

        bool isFirstVisible = true;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            string key = GetEntryKey(i);
            string value;

            if (!resolvedValues.TryGetValue(key, out value) || string.IsNullOrEmpty(value))
            {
                if (!string.IsNullOrEmpty(entry.leadingSeparator))
                {
                    value = entry.leadingSeparator;
                }
            }

            if (skipEmpty && string.IsNullOrEmpty(value))
                continue;

            if (!isFirstVisible)
            {
                if (value != entry.leadingSeparator)
                {
                    sb.Append(entry.leadingSeparator);
                }
            }
            else
            {
                isFirstVisible = false;
            }

            sb.Append(value);
        }

        if (!string.IsNullOrEmpty(globalSuffix))
            sb.Append(globalSuffix);

        textComponent.text = sb.ToString();
    }

    void OnDestroy()
    {
        if (entries != null)
        {
            foreach (var entry in entries)
            {
                if (entry.localizedString != null)
                    entry.localizedString.StringChanged -= null;
            }
        }
    }
}

[System.Serializable]
public class TextEntry
{
    [Tooltip("本地化字符串引用")]
    public LocalizedString localizedString;

    [Tooltip("在当前条目之前插入的连接符（支持\\n换行）")]
    public string leadingSeparator = "";

    public TextEntry(LocalizedString localizedString, string leadingSeparator = "")
    {
        this.localizedString = localizedString;
        this.leadingSeparator = leadingSeparator;
    }

    public string GetLocalizedString()
    {
        return localizedString != null ? localizedString.GetLocalizedString() : "";
    }
}