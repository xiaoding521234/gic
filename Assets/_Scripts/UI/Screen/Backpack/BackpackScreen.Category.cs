// ==================== BackpackScreen.Category.cs ====================
using System;
using LocalEvents;
using UnityEngine.Localization;

public partial class BackpackScreen
{
    private static readonly BackpackTab[] TabOrder =
    {
        BackpackTab.Character,
        BackpackTab.Creation,
        BackpackTab.Equipment,
        BackpackTab.Consumable,
        BackpackTab.Material,
        BackpackTab.Currency,
        BackpackTab.Quest,
    };

    private void OnPreviousCategory()
    {
        int idx = Array.IndexOf(TabOrder, currentTab);
        SetCategory(TabOrder[(idx - 1 + TabOrder.Length) % TabOrder.Length], isInit: false);
    }

    private void OnNextCategory()
    {
        int idx = Array.IndexOf(TabOrder, currentTab);
        SetCategory(TabOrder[(idx + 1) % TabOrder.Length], isInit: false);
    }

    private void SetCategory(BackpackTab newTab, bool isInit)
    {
        if (currentTab == newTab && !isInit) return;
        currentTab = newTab;

        if (categoryText != null)
        {
            categoryText.ClearAllEntries();
            categoryText.AddEntry(new LocalizedString(TableName.UIText.ToString(), newTab.ToString()));
        }

        EventBusHub.Instance.Publish(new OnBackpackCategorySyncEvent { Tab = newTab });

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
