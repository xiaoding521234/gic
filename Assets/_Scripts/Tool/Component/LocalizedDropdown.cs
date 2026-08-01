using UnityEngine;
using TMPro;
using System.Collections.Generic;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using GIC.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
namespace GIC.Tool
{


    [RequireComponent(typeof(TMP_Dropdown))]
    public class LocalizedDropdown : MonoBehaviour
    {
        [Header("本地化配置")]
        [Tooltip("选项对应的 Entry 列表")]
        public List<TextEntry> optionEntries = new List<TextEntry>();

        private TMP_Dropdown dropdown;
        private List<string> currentOptions = new List<string>();
        private bool isRefreshing = false;

        private void Awake()
        {
            dropdown = GetComponent<TMP_Dropdown>();
        }

        private void Start()
        {
            RefreshOptions();
            LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
        }

        private void OnDestroy()
        {
            LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
        }

        private void OnLocaleChanged(Locale newLocale)
        {
            RefreshOptions();
        }

        public void RefreshOptions()
        {
            if (dropdown == null || optionEntries == null || optionEntries.Count == 0) return;

            isRefreshing = true;

            // 保存当前选中值
            int currentValue = dropdown.value;

            // 清空并重建选项
            dropdown.ClearOptions();
            currentOptions.Clear();

            var options = new List<TMP_Dropdown.OptionData>();

            foreach (var entry in optionEntries)
            {
                string text = GetTextFromEntry(entry);
                currentOptions.Add(text);
                options.Add(new TMP_Dropdown.OptionData(text));
            }

            dropdown.AddOptions(options);

            // 恢复选中值
            if (currentValue >= 0 && currentValue < options.Count)
                dropdown.value = currentValue;
            else
                dropdown.value = 0;

            dropdown.RefreshShownValue();
            isRefreshing = false;
        }

        private string GetTextFromEntry(TextEntry entry)
        {
            if (entry == null) return "";

            // 如果有本地化字符串，获取本地化文本
            if (entry.localizedString != null && !entry.localizedString.IsEmpty)
            {
                return entry.localizedString.GetLocalizedString();
            }

            // 否则使用 leadingSeparator 作为静态文本
            return entry.leadingSeparator ?? "";
        }

        /// <summary>
        /// 从枚举设置选项
        /// </summary>
        public void SetOptionsFromEnum<T>(string tableName = "UIText") where T : System.Enum
        {
            optionEntries.Clear();
            foreach (T value in System.Enum.GetValues(typeof(T)))
            {
                var localizedString = new LocalizedString(tableName, value.ToString());
                optionEntries.Add(new TextEntry(localizedString, ""));
            }
            RefreshOptions();
        }

        /// <summary>
        /// 从字符串数组设置选项（静态文本）
        /// </summary>
        public void SetOptionsFromStrings(string[] options)
        {
            optionEntries.Clear();
            foreach (string option in options)
            {
                optionEntries.Add(new TextEntry(null, option));
            }
            RefreshOptions();
        }

        /// <summary>
        /// 从 Key 列表设置选项
        /// </summary>
        public void SetOptionsFromKeys(List<string> keys, string tableName = "UIText")
        {
            optionEntries.Clear();
            foreach (string key in keys)
            {
                var localizedString = new LocalizedString(tableName, key);
                optionEntries.Add(new TextEntry(localizedString, ""));
            }
            RefreshOptions();
        }

        /// <summary>
        /// 从 TextEntry 列表设置选项
        /// </summary>
        public void SetOptionsFromEntries(List<TextEntry> entries)
        {
            optionEntries.Clear();
            foreach (var entry in entries)
            {
                optionEntries.Add(entry);
            }
            RefreshOptions();
        }

        /// <summary>
        /// 获取当前选中的文本
        /// </summary>
        public string GetCurrentSelectedText()
        {
            if (dropdown == null || dropdown.value < 0 || dropdown.value >= currentOptions.Count)
                return "";
            return currentOptions[dropdown.value];
        }

        /// <summary>
        /// 获取当前选中的索引对应的 Entry
        /// </summary>
        public TextEntry GetCurrentSelectedEntry()
        {
            if (dropdown == null || dropdown.value < 0 || dropdown.value >= optionEntries.Count)
                return null;
            return optionEntries[dropdown.value];
        }

        /// <summary>
        /// 获取当前的 TMP_Dropdown 组件
        /// </summary>
        public TMP_Dropdown Dropdown => dropdown;

        /// <summary>
        /// 获取当前选中值
        /// </summary>
        public int Value
        {
            get => dropdown != null ? dropdown.value : 0;
            set
            {
                if (dropdown != null)
                    dropdown.value = value;
            }
        }

        /// <summary>
        /// 设置选中值（不触发回调）
        /// </summary>
        public void SetValueWithoutNotify(int value)
        {
            if (dropdown != null)
                dropdown.SetValueWithoutNotify(value);
        }

        /// <summary>
        /// 添加选中值变化监听器
        /// </summary>
        public void AddListener(UnityEngine.Events.UnityAction<int> listener)
        {
            if (dropdown != null)
                dropdown.onValueChanged.AddListener(listener);
        }

        /// <summary>
        /// 移除所有监听器
        /// </summary>
        public void RemoveAllListeners()
        {
            if (dropdown != null)
                dropdown.onValueChanged.RemoveAllListeners();
        }

        /// <summary>
        /// 清空选项
        /// </summary>
        public void ClearOptions()
        {
            if (dropdown != null)
                dropdown.ClearOptions();
            optionEntries.Clear();
            currentOptions.Clear();
        }

        /// <summary>
        /// 设置交互性
        /// </summary>
        public void SetInteractable(bool interactable)
        {
            if (dropdown != null)
                dropdown.interactable = interactable;
        }

        /// <summary>
        /// 获取交互性
        /// </summary>
        public bool IsInteractable => dropdown != null && dropdown.interactable;

        /// <summary>
        /// 设置 caption 文本的颜色
        /// </summary>
        public void SetCaptionColor(Color color)
        {
            if (dropdown != null && dropdown.captionText != null)
                dropdown.captionText.color = color;
        }

        /// <summary>
        /// 刷新显示
        /// </summary>
        public void RefreshShownValue()
        {
            if (dropdown != null)
                dropdown.RefreshShownValue();
        }
    }
}


