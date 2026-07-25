using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;

public class ButtonSettingItem : SettingItem
{
    [Header("Button组件")]
    [SerializeField] private Button button;
    [SerializeField] private TextCombiner valueText;

    private System.Action onClick;
    private System.Action<string> onValueConfirmed;
    private string currentValue;
    private string defaultValue;

    public void Setup(string labelKey, string defaultValue, System.Action onClick, System.Action<string> onValueConfirmed)
    {
        labelText.SetSingleEntry(new LocalizedString("UIText", labelKey));
        this.defaultValue = defaultValue;
        this.onClick = onClick;
        this.onValueConfirmed = onValueConfirmed;

        button.onClick.AddListener(OnButtonClicked);
    }

    public override void Initialize()
    {
        currentValue = LoadValue();
        valueText.SetSingleEntry(currentValue);
    }

    private void OnButtonClicked()
    {
        onClick?.Invoke();
    }

    /// <summary>
    /// 外部调用，更新按钮显示的文本
    /// </summary>
    public void UpdateValue(string newValue)
    {
        currentValue = newValue;
        valueText.SetSingleEntry(newValue);
        onValueConfirmed?.Invoke(newValue);
        SaveValue(newValue);
    }

    public override void ApplyValue()
    {
        onValueConfirmed?.Invoke(currentValue);
    }

    public override void ResetToDefault()
    {
        currentValue = defaultValue;
        valueText.SetSingleEntry(defaultValue);
    }

    protected virtual void SaveValue(string value) { }
    protected virtual string LoadValue() => defaultValue;
}