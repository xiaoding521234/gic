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

        /// <summary>TMP_Dropdown 惰性获取（2026-08-27 根治 inactive 面板条目失效）：
        /// 旧实现仅 Awake 缓存——非激活面板（SettingsScreen 各分栏克隆体）里的 Awake 从未跑，
        /// dropdown 恒 null → RefreshOptions/SetOptionsFromEntries/SetValueWithoutNotify 静默跳过
        /// → 选项不重建/监听不挂/点了没反应（2026-08-27 派蒙形态下拉首测踩坑实证）。
        /// RequireComponent 保证组件在则 TMP 必在，GetComponent 廉价安全。</summary>
        private TMP_Dropdown Dd => dropdown != null ? dropdown : (dropdown = GetComponent<TMP_Dropdown>());

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
            if (Dd == null || optionEntries == null || optionEntries.Count == 0) return;

            // 保存当前选中值
            int currentValue = Dd.value;

            // 清空并重建选项
            Dd.ClearOptions();
            currentOptions.Clear();

            var options = new List<TMP_Dropdown.OptionData>();

            foreach (var entry in optionEntries)
            {
                string text = GetTextFromEntry(entry);
                currentOptions.Add(text);
                options.Add(new TMP_Dropdown.OptionData(text));
            }

            Dd.AddOptions(options);

            // 恢复选中值——必须走 WithoutNotify：Dd.value 的 setter 每次都 Invoke onValueChanged
            // （值不变也发）。2026-08-28 实证危害：设置界面各分栏面板首次激活时本组件 Start 才跑，
            // RefreshOptions 误触发下拉回调——派蒙形态下拉"点开派蒙栏目就热切换形态"即此路径。
            if (currentValue >= 0 && currentValue < options.Count)
                Dd.SetValueWithoutNotify(currentValue);
            else
                Dd.SetValueWithoutNotify(0);

            Dd.RefreshShownValue();
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
            if (Dd == null || Dd.value < 0 || Dd.value >= currentOptions.Count)
                return "";
            return currentOptions[Dd.value];
        }

        /// <summary>
        /// 获取当前选中的索引对应的 Entry
        /// </summary>
        public TextEntry GetCurrentSelectedEntry()
        {
            if (Dd == null || Dd.value < 0 || Dd.value >= optionEntries.Count)
                return null;
            return optionEntries[Dd.value];
        }

        /// <summary>
        /// 获取当前的 TMP_Dropdown 组件（惰性——见 Dd 属性注释，inactive 面板场景同样可用）
        /// </summary>
        public TMP_Dropdown Dropdown => Dd;

        /// <summary>
        /// 获取当前选中值
        /// </summary>
        public int Value
        {
            get => Dd != null ? Dd.value : 0;
            set => Dd.value = value;
        }

        /// <summary>
        /// 设置选中值（不触发回调）
        /// </summary>
        public void SetValueWithoutNotify(int value)
        {
            Dd.SetValueWithoutNotify(value);
        }

        /// <summary>
        /// 添加选中值变化监听器
        /// </summary>
        public void AddListener(UnityEngine.Events.UnityAction<int> listener)
        {
            Dd.onValueChanged.AddListener(listener);
        }

        /// <summary>
        /// 移除所有监听器
        /// </summary>
        public void RemoveAllListeners()
        {
            Dd.onValueChanged.RemoveAllListeners();
        }

        /// <summary>
        /// 清空选项
        /// </summary>
        public void ClearOptions()
        {
            Dd.ClearOptions();
            optionEntries.Clear();
            currentOptions.Clear();
        }

        /// <summary>
        /// 设置交互性
        /// </summary>
        public void SetInteractable(bool interactable)
        {
            Dd.interactable = interactable;
        }

        /// <summary>
        /// 获取交互性
        /// </summary>
        public bool IsInteractable => Dd != null && Dd.interactable;

        /// <summary>
        /// 设置 caption 文本的颜色
        /// </summary>
        public void SetCaptionColor(Color color)
        {
            if (Dd.captionText != null)
                Dd.captionText.color = color;
        }

        /// <summary>
        /// 刷新显示
        /// </summary>
        public void RefreshShownValue()
        {
            Dd.RefreshShownValue();
        }
    }
}


