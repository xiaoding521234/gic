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
    /// 游戏内时间变更事件（TimeUtility.SetGameTime/ResetToSystemTime 广播——派蒙对话 set_game_time
    /// 工具触发；大厅背景与位置音乐订阅方据此切换白天/夜晚表现）
    /// </summary>
    public class OnGameTimeChangedEvent : BaseEvent
    {
        public TimePeriod NewPeriod;
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
    /// 编辑模式下卡片被点击（Card → BackpackScreen）
    /// </summary>
    public class OnCardClickedInEditModeEvent : BaseEvent
    {
        public SaveCardData CardData;
        public bool IsInDeck;
    }

    /// <summary>
    /// 派蒙形态变更事件（PetInGameHost.HotSwitchForm 广播——设置界面"派蒙"栏下拉订阅刷新显示：
    /// 三连击手势/桌宠 IPC 接管等外部切换后，打开着的设置界面不会自动重读存档，下拉显示旧值
    /// 且点同项不触发 onValueChanged=点了没反应陷阱，2026-09-01）
    /// </summary>
    public class OnPetFormChangedEvent : BaseEvent
    {
        public int NewForm;
    }

    /// <summary>
    /// 存档写盘失败事件（SaveManager.DoSave catch 分支广播——PopupManager 订阅弹轻提示，
    /// 磁盘满/权限异常时玩家可感知而非静默丢进度。连续失败由发送侧防抖只发一次，2026-09-05）
    /// </summary>
    public class OnSaveFailedEvent : BaseEvent
    {
    }

}

