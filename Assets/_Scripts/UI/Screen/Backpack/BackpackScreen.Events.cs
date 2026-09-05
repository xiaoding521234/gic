// ==================== BackpackScreen.Events.cs ====================
using GIC.Data.Event;
using GIC.Battle;
using GIC.Framework;
using GIC.Data;
using GIC.Tool;
namespace GIC.UI
{


    public partial class BackpackScreen
    {
        private class BackpackCategoryChangedHandler : IEventHandler<OnBackpackCategoryChangedEvent>
        {
            private readonly BackpackScreen _screen;
            public BackpackCategoryChangedHandler(BackpackScreen screen) => _screen = screen;
            public bool CanHandle(OnBackpackCategoryChangedEvent evt)
                => _screen != null && _screen.gameObject.activeInHierarchy;
            public void Handle(OnBackpackCategoryChangedEvent evt)
                => _screen.SetCategory(evt.Tab, isInit: false);
        }

        private class CardClickedInEditHandler : IEventHandler<OnCardClickedInEditModeEvent>
        {
            private readonly BackpackScreen _screen;
            public CardClickedInEditHandler(BackpackScreen screen) => _screen = screen;
            public bool CanHandle(OnCardClickedInEditModeEvent evt)
                => _screen != null && _screen.isEditMode && _screen.gameObject.activeInHierarchy;
            public void Handle(OnCardClickedInEditModeEvent evt)
                => _screen.OnEditCardClicked(evt.CardData, evt.IsInDeck);
        }
    }
}


