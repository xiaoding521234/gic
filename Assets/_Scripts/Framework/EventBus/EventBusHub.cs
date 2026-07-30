// EventBusHub.cs - 中枢类，统一对外接口
using Mirror;
using UnityEngine;

public class EventBusHub : MonoBehaviour
{
    public static EventBusHub Instance { get; private set; }

    [Header("设置")]
    [SerializeField] private bool _dontDestroyOnLoad = true;

    [Header("组件引用")]
    [SerializeField] private NetworkEventBus _networkEventBus;

    public LocalEventBus LocalEventBus { get; private set; }
    public NetworkEventBus NetworkEventBus => _networkEventBus;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            if (_dontDestroyOnLoad)
                DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // 初始化本地事件总线
        LocalEventBus = new LocalEventBus();

        Debug.Log("[EventBusHub] 初始化完成");
    }

    private void FixedUpdate()
    {
        LocalEventBus?.Tick();
    }

    #region 核心路由逻辑

    /// <summary>
    /// 自动填充 SourcePlayerID
    /// </summary>
    private void FillSourcePlayerID(BaseEvent eventData)
    {
        // 已填写有效 ID 则不覆盖
        if (!string.IsNullOrEmpty(eventData.SourcePlayerID) && eventData.SourcePlayerID != PlayerID.Unknown)
            return;

        string senderId = _networkEventBus?.SelfConnectionId;

        // 备用：从 PlayerManager 获取
        if (string.IsNullOrEmpty(senderId) || senderId == PlayerID.Offline)
            senderId = PlayerManager.Instance?.SelfPlayerID;

        if (!string.IsNullOrEmpty(senderId) && senderId != PlayerID.Offline)
            eventData.SourcePlayerID = senderId;
        else
            eventData.SourcePlayerID = PlayerID.Unknown;
    }

    [Header("调试")]
    [SerializeField] private bool _enableDebugLog = false;

    public void Publish(BaseEvent eventData)
    {
        if (eventData == null || !eventData.Active)
        {
            Debug.LogWarning("[EventBusHub] SendEvent: 事件无效或未激活");
            return;
        }

        FillSourcePlayerID(eventData);
        eventData.Source = EventSource.Local;

        switch (eventData.Type)
        {
            case EventType.Local:
                if (_enableDebugLog)
                    Debug.Log($"[EventBusHub] SendEvent: 本地事件 Type={eventData.GetType().Name}, SourcePlayerID={eventData.SourcePlayerID}");
                LocalEventBus?.Send(eventData);
                break;

            case EventType.All:
            case EventType.OnlyHost:
                if (_enableDebugLog)
                    Debug.Log($"[EventBusHub] SendEvent: 网络事件 Type={eventData.GetType().Name}, EventType={eventData.Type}, SourcePlayerID={eventData.SourcePlayerID}");
                if (_networkEventBus != null)
                {
                    if (eventData.Type == EventType.All)
                        _networkEventBus.SendToAll(eventData);
                    else
                        _networkEventBus.SendToHost(eventData);
                }
                else
                {
                    Debug.LogError("[EventBusHub] RouteToNetwork: NetworkEventBus 未找到");
                }
                break;

            default:
                Debug.LogWarning($"[EventBusHub] SendEvent: 未知事件类型 Type={eventData.GetType().Name}, EventType={eventData.Type}");
                break;
        }
    }

    public void PublishToPlayer(string targetPlayerID, BaseEvent eventData)
    {
        if (eventData == null || !eventData.Active)
        {
            Debug.LogWarning("[EventBusHub] SendEventToPlayer: 事件无效或未激活");
            return;
        }

        if (_networkEventBus == null)
        {
            Debug.LogError("[EventBusHub] SendEventToPlayer: NetworkEventBus 未找到");
            return;
        }

        eventData.Source = EventSource.Local;
        FillSourcePlayerID(eventData);

        Debug.Log($"[EventBusHub] SendEventToPlayer: TargetID={targetPlayerID}, Type={eventData.GetType().Name}, SourcePlayerID={eventData.SourcePlayerID}");

        _networkEventBus.SendToPlayer(targetPlayerID, eventData);
    }

    public void PublishToPlayers(System.Collections.Generic.List<string> targetPlayerIDs, BaseEvent eventData)
    {
        if (eventData == null || !eventData.Active)
        {
            Debug.LogWarning("[EventBusHub] SendEventToPlayers: 事件无效或未激活");
            return;
        }

        if (_networkEventBus == null)
        {
            Debug.LogError("[EventBusHub] SendEventToPlayers: NetworkEventBus 未找到");
            return;
        }

        eventData.Source = EventSource.Local;
        FillSourcePlayerID(eventData);

        Debug.Log($"[EventBusHub] SendEventToPlayers: Targets=[{string.Join(",", targetPlayerIDs)}], Type={eventData.GetType().Name}, SourcePlayerID={eventData.SourcePlayerID}");

        _networkEventBus.SendToPlayers(targetPlayerIDs, eventData);
    }

    public void ReceiveNetworkEvent(BaseEvent eventData)
    {
        if (eventData == null || !eventData.Active)
        {
            Debug.LogWarning("[EventBusHub] ReceiveNetworkEvent: 事件无效或未激活");
            return;
        }

        eventData.Source = EventSource.Network;

        Debug.Log($"[EventBusHub] ReceiveNetworkEvent: Type={eventData.GetType().Name}, SourcePlayerID={eventData.SourcePlayerID}, Immediate={eventData.Immediate}");

        if (eventData.Immediate)
            LocalEventBus?.ReceiveNetworkEventImmediate(eventData);
        else
            LocalEventBus?.ReceiveNetworkEvent(eventData);
    }

    #endregion

    #region 公共静态 API

    /// <summary>
    /// 发布事件 — 自动判断本地/网络路由
    /// </summary>
    public void Send(BaseEvent eventData) => Instance?.Publish(eventData);

    /// <summary>
    /// 发布本地事件 — 立即执行，不进入队列
    /// </summary>
    public void SendImmediate(BaseEvent eventData)
    {
        if (eventData == null) return;
        eventData.Immediate = true;
        eventData.Type = EventType.Local;
        Instance?.Publish(eventData);
    }

    public void SendToPlayer(string targetPlayerID, BaseEvent eventData)
    {
        Instance?.PublishToPlayer(targetPlayerID, eventData);
    }

    public void SendToPlayers(System.Collections.Generic.List<string> targetPlayerIDs, BaseEvent eventData)
    {
        Instance?.PublishToPlayers(targetPlayerIDs, eventData);
    }

    public void Subscribe<T>(IEventHandler<T> handler) where T : BaseEvent
    {
        Instance?.LocalEventBus?.Subscribe(handler);
    }

    public void Unsubscribe<T>(IEventHandler<T> handler) where T : BaseEvent
    {
        Instance?.LocalEventBus?.Unsubscribe(handler);
    }

    public void SetLockState(EventBusLockState state) => Instance?.LocalEventBus?.SetLockState(state);

    public void ReleaseLock(EventBusLockState state) => Instance?.LocalEventBus?.ReleaseLock(state);

    public void ForceUnlock() => Instance?.LocalEventBus?.ForceUnlock();

    public void CancelEvents<T>() where T : BaseEvent => Instance?.LocalEventBus?.CancelEvents<T>();

    public void ClearAllWaitingEvents() => Instance?.LocalEventBus?.ClearAllWaitingEvents();

    #endregion
}