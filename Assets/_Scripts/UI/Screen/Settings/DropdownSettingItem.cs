using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Localization;

public class DropdownSettingItem : SettingItem
{
    [Header("Dropdown组件")]
    [SerializeField] private LocalizedDropdown dropdown;

    private System.Action<int> onValueChanged;
    private int defaultValue;

    public void Setup(string labelKey, List<TextEntry> options, int defaultValue, System.Action<int> onValueChanged)
    {
        labelText.SetSingleEntry(new LocalizedString("UIText", labelKey));
        this.defaultValue = defaultValue;
        this.onValueChanged = onValueChanged;

        dropdown.SetOptionsFromEntries(options);
        dropdown.AddListener(OnDropdownChanged);
    }

    public override void Initialize()
    {
        dropdown.SetValueWithoutNotify(LoadValue());
    }

    private void OnDropdownChanged(int index)
    {
        onValueChanged?.Invoke(index);
        SaveValue(index);
    }

    public override void ApplyValue()
    {
        onValueChanged?.Invoke(dropdown.Value);
    }

    public override void ResetToDefault()
    {
        dropdown.SetValueWithoutNotify(defaultValue);
    }

    public void SetValue(int index)
    {
        dropdown.SetValueWithoutNotify(index);
    }

    protected virtual void SaveValue(int value) { }
    protected virtual int LoadValue() => defaultValue;
}