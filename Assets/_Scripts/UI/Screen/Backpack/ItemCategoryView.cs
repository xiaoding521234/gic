using LocalEvents;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;

public class ItemCategoryView : MonoBehaviour
{
    public Toggle toggle;
    public Image selectIcon;
    public Image selectLine;
    public Category category = Category.Character;

    private CategorySyncHandler _syncHandler;

    public void Awake()
    {
        toggle.onValueChanged.AddListener(OnToggleValueChanged);

        _syncHandler = new CategorySyncHandler(this);
        EventBusHub.Instance.Subscribe(_syncHandler);
    }

    public void OnDestroy()
    {
        toggle.onValueChanged.RemoveListener(OnToggleValueChanged);

        if (EventBusHub.Instance != null && _syncHandler != null)
            EventBusHub.Instance.Unsubscribe(_syncHandler);
    }

    private void OnToggleValueChanged(bool isOn)
    {
        selectIcon.gameObject.SetActive(isOn);
        selectLine.gameObject.SetActive(isOn);

        if (isOn)
        {
            EventBusHub.Instance.Publish(new OnBackpackCategoryChangedEvent
            {
                Category = category
            });
        }
    }

    private void SetVisual(bool isOn)
    {
        toggle.SetIsOnWithoutNotify(isOn);
        selectIcon.gameObject.SetActive(isOn);
        selectLine.gameObject.SetActive(isOn);
    }

    private class CategorySyncHandler : IEventHandler<OnBackpackCategorySyncEvent>
    {
        private readonly ItemCategoryView _view;
        public CategorySyncHandler(ItemCategoryView view) => _view = view;

        public bool CanHandle(OnBackpackCategorySyncEvent evt)
            => _view != null; // 所有都处理

        public void Handle(OnBackpackCategorySyncEvent evt)
            => _view.SetVisual(_view.category == evt.Category); // 匹配的选中，其他取消
    }
}

// 用于背包分页
public enum Category
{
    [InspectorName("角色")]
    Character=0,
    [InspectorName("普通物品")]
    CommonItem=1,
    [InspectorName("珍贵物品")]
    PreciousItem=2,
}

/// <summary>
/// Category 扩展方法
/// </summary>
public static class CategoryExtensions
{
    /// <summary>
    /// 获取类别在本地化表中的 Entry Key
    /// </summary>
    private static string GetEntryKey(this Category category)
    {
        return category.ToString();
    }

    /// <summary>
    /// 创建用于本地化系统的 LocalizedString 对象
    /// </summary>
    private static LocalizedString GetLocalizedString(this Category category)
    {
        return new LocalizedString(TableName.UIText.ToString(), category.GetEntryKey());
    }

    /// <summary>
    /// 创建 Entry 对象（用于 TextCombiner）
    /// </summary>
    /// <param name="leadingSeparator">前置连接符</param>
    public static TextEntry GetEntry(this Category category, string leadingSeparator = "")
    {
        LocalizedString localizedString = category.GetLocalizedString();
        return new TextEntry(localizedString, leadingSeparator);
    }
}