using GIC.Framework;
using GIC.Data;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Data.Event
{
    public class OnPositionChangedEvent : BaseEvent
    {
        public PositionName PositionName;
    }

    /// <summary>
    /// 场景激活事件 - 当场景变为活动场景时触发
    /// </summary>
    public class OnSceneActivatedEvent : BaseEvent
    {
        public string SceneName;
    }
    
    /// <summary>
    /// 场景即将卸载事件
    /// </summary>
    public class OnSceneWillUnloadEvent : BaseEvent
    {
        public string SceneName;
    }
    
    /// <summary>
    /// 返回上一个场景事件
    /// </summary>
    public class OnGoBackEvent : BaseEvent
    {
        public string FromScene;
        public string ToScene;
    }

    /// <summary>
    /// 背包分页切换事件（ItemCategoryView → BackpackScreen）
    /// </summary>
    public class OnBackpackCategoryChangedEvent : BaseEvent
    {
        public BackpackTab Tab;
    }

    /// <summary>
    /// 请求同步分页Toggle（BackpackScreen → ItemCategoryView）
    /// </summary>
    public class OnBackpackCategorySyncEvent : BaseEvent
    {
        public BackpackTab Tab;
    }

    /// <summary>
    /// 卡组切换事件（DeckChooseView → BackpackScreen）
    /// </summary>
    public class OnDeckChangedEvent : BaseEvent
    {
        public int DeckId;
    }

    /// <summary>
    /// 请求同步卡组Toggle（BackpackScreen → DeckChooseView）
    /// </summary>
    public class OnBackpackDeckSyncEvent : BaseEvent
    {
        public int DeckId;
    }

    /// <summary>
    /// 编辑模式下卡片被点击（Card → BackpackScreen）
    /// </summary>
    public class OnCardClickedInEditModeEvent : BaseEvent
    {
        public SaveCardData CardData;
        public bool IsInDeck;
    }

}

