using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;

public class SliderSettingItem : SettingItem
{
    [Header("Slider组件")]
    [SerializeField] private Slider slider;
    [SerializeField] private TextCombiner valueText;

    private System.Action<float> onValueChanged;
    private float defaultValue;
    private bool showAsPercent;
    private float stepSize = 0f;

    public void Setup(string labelKey, float min, float max, float defaultValue, System.Action<float> onValueChanged, bool showAsPercent = false, float stepSize = 0f)
    {
        labelText.SetSingleEntry(new LocalizedString("UIText", labelKey));
        slider.minValue = min;
        slider.maxValue = max;
        slider.wholeNumbers = stepSize == 1f;
        this.defaultValue = defaultValue;
        this.onValueChanged = onValueChanged;
        this.showAsPercent = showAsPercent;
        this.stepSize = stepSize;

        slider.onValueChanged.AddListener(OnSliderChanged);

        float snappedValue = SnapToStep(defaultValue);
        slider.SetValueWithoutNotify(snappedValue);
        UpdateValueDisplay(snappedValue);
    }

    private void Start()
    {
        // TextCombiner 可能在本帧才初始化完毕，再次刷新显示
        if (slider != null)
        {
            UpdateValueDisplay(slider.value);
        }
    }

    public override void Initialize()
    {
        float value = LoadValue();
        value = SnapToStep(value);
        slider.SetValueWithoutNotify(value);
        UpdateValueDisplay(slider.value);
    }

    private void OnSliderChanged(float value)
    {
        value = SnapToStep(value);

        if (Mathf.Abs(slider.value - value) > 0.001f)
        {
            slider.SetValueWithoutNotify(value);
        }

        UpdateValueDisplay(value);
        onValueChanged?.Invoke(value);
        SaveValue(value);
    }

    private float SnapToStep(float value)
    {
        if (stepSize <= 0f) return value;
        return Mathf.Round(value / stepSize) * stepSize;
    }

    private void UpdateValueDisplay(float value)
    {
        if (valueText != null)
        {
            if (showAsPercent)
            {
                valueText.SetSingleEntry($"{(int)(value * 100)}%");
            }
            else
            {
                valueText.SetSingleEntry(stepSize == 1f ? $"{(int)value}" : $"{value:F1}");
            }
        }
    }

    public override void ApplyValue()
    {
        onValueChanged?.Invoke(slider.value);
    }

    public override void ResetToDefault()
    {
        slider.value = defaultValue;
        UpdateValueDisplay(defaultValue);
    }

    public void SetValue(float value)
    {
        value = SnapToStep(value);
        slider.SetValueWithoutNotify(value);
        UpdateValueDisplay(value);
    }

    protected virtual void SaveValue(float value) { }
    protected virtual float LoadValue() => defaultValue;
}