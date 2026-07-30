using LocalEvents;
using UnityEngine;
using UnityEngine.UI;

public class ItemCategoryView : MonoBehaviour
{
    public Toggle toggle;
    public Image selectIcon;
    public Image selectLine;
    public BackpackTab tab = BackpackTab.Character;

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
            EventBusHub.Instance.SendImmediate(new OnBackpackCategoryChangedEvent
            {
                Tab = tab
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
            => _view != null;

        public void Handle(OnBackpackCategorySyncEvent evt)
            => _view.SetVisual(_view.tab == evt.Tab);
    }
}
