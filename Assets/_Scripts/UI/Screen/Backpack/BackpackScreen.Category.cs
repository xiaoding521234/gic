// ==================== BackpackScreen.Category.cs ====================
using System;
using LocalEvents;
using UnityEngine.Localization;

public partial class BackpackScreen
{
    private static readonly Category[] CategoryOrder = { Category.Character, Category.CommonItem, Category.PreciousItem };

    private void OnPreviousCategory()
    {
        int idx = Array.IndexOf(CategoryOrder, currentCategory);
        SetCategory(CategoryOrder[(idx - 1 + CategoryOrder.Length) % CategoryOrder.Length], isInit: false);
    }

    private void OnNextCategory()
    {
        int idx = Array.IndexOf(CategoryOrder, currentCategory);
        SetCategory(CategoryOrder[(idx + 1) % CategoryOrder.Length], isInit: false);
    }

    private void SetCategory(Category newCategory, bool isInit)
    {
        if (currentCategory == newCategory && !isInit) return;
        currentCategory = newCategory;

        if (categoryText != null)
        {
            categoryText.ClearAllEntries();
            categoryText.AddEntry(new LocalizedString(TableName.UIText.ToString(), newCategory.ToString()));
        }

        EventBusHub.Instance.Publish(new OnBackpackCategorySyncEvent { Category = newCategory });

        if (!isInit) RefreshCardList();
    }

    private void OnClose()
    {
        if (isEditMode)
        {
            ExitEditMode();
        }

        AudioManager.Instance.PopMusicVolume();
        StartCoroutine(CloseWithAnimation());
    }
}