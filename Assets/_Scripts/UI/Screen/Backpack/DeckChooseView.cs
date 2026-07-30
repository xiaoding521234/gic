using LocalEvents;
using UnityEngine;
using UnityEngine.UI;

public class DeckChooseView : MonoBehaviour
{
    public Image selectImage;
    public Toggle toggle;
    public int deckID;

    private DeckSyncHandler _syncHandler;

    public void Awake()
    {
        toggle.onValueChanged.AddListener(OnToggleValueChanged);

        _syncHandler = new DeckSyncHandler(this);
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
        if (selectImage != null)
            selectImage.gameObject.SetActive(isOn);

        if (isOn)
        {
            EventBusHub.Instance.SendImmediate(new OnDeckChangedEvent
            {
                DeckId = deckID
            });
        }
    }

    private void SetVisual(bool isOn)
    {
        toggle.SetIsOnWithoutNotify(isOn);
        if (selectImage != null)
            selectImage.gameObject.SetActive(isOn);
    }

    private class DeckSyncHandler : IEventHandler<OnBackpackDeckSyncEvent>
    {
        private readonly DeckChooseView _view;
        public DeckSyncHandler(DeckChooseView view) => _view = view;

        public bool CanHandle(OnBackpackDeckSyncEvent evt)
            => _view != null;  // 所有都处理

        public void Handle(OnBackpackDeckSyncEvent evt)
            => _view.SetVisual(_view.deckID == evt.DeckId);  // 匹配选中，其他取消
    }
}