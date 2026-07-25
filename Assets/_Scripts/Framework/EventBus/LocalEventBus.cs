// LocalEventBus.cs - 纯本地事件处理
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class LocalEventBus
{
    private Dictionary<Type, List<HandlerInfo>> _handlers = new();
    private long _subscriptionCounter = 0;

    private LinkedList<QueuedEvent> _eventQueue = new();

    private EventBusLockState _currentLockState = EventBusLockState.None;
    private static readonly Dictionary<EventBusLockState, int> LockPriorityMap = new()
    {
        { EventBusLockState.None, 0 },
        { EventBusLockState.Animation, 1 },
        { EventBusLockState.Highest, 2 }
    };

    private HashSet<BaseEvent> _processingEvents = new();

    private int _targetFrameRate = 60;
    private float _fixedTimeStep;
    private float _accumulatedTime;
    private float _lastProcessTime;

    public int QueuedEventCount => _eventQueue.Count;
    public bool HasQueuedEvents => _eventQueue.Count > 0;
    public EventBusLockState CurrentLockState => _currentLockState;
    public bool IsLocked => _currentLockState != EventBusLockState.None;

    public LocalEventBus()
    {
        InitializeFixedFrameRate();
    }

    #region 帧率控制

    public void InitializeFixedFrameRate()
    {
        _fixedTimeStep = 1f / _targetFrameRate;
        _accumulatedTime = 0f;
        _lastProcessTime = Time.realtimeSinceStartup;
    }

    public void SetTargetFrameRate(int frameRate)
    {
        if (frameRate <= 0) return;
        _targetFrameRate = frameRate;
        InitializeFixedFrameRate();
    }

    public void Update()
    {
        ProcessEventsWithFixedRate();
    }

    private void ProcessEventsWithFixedRate()
    {
        float currentTime = Time.realtimeSinceStartup;
        float deltaTime = currentTime - _lastProcessTime;
        deltaTime = Mathf.Min(deltaTime, 0.1f);

        _accumulatedTime += deltaTime;
        _lastProcessTime = currentTime;

        while (_accumulatedTime >= _fixedTimeStep)
        {
            ProcessOneHandlerPerFrame();
            _accumulatedTime -= _fixedTimeStep;

            if (_eventQueue.Count == 0)
            {
                _accumulatedTime = 0;
                break;
            }
        }
    }

    #endregion

    #region 事件处理

    private void ProcessOneHandlerPerFrame()
    {
        if (_currentLockState == EventBusLockState.Highest) return;

        CleanupInvalidEvents();

        var firstNode = _eventQueue.First;
        if (firstNode == null) return;

        var queued = firstNode.Value;

        if (_currentLockState == EventBusLockState.Animation)
        {
            if (!queued.EventData.IgnoreAnimationLock) return;
        }

        ProcessOneHandler(queued);

        if (queued.State == EventProcessState.Completed || queued.State == EventProcessState.Cancelled)
        {
            _eventQueue.RemoveFirst();
        }
    }

    private void CleanupInvalidEvents()
    {
        while (_eventQueue.First != null)
        {
            var queued = _eventQueue.First.Value;

            if (queued.State == EventProcessState.Cancelled || queued.State == EventProcessState.Completed)
            {
                _eventQueue.RemoveFirst();
                continue;
            }

            if (!queued.EventData.Active)
            {
                queued.State = EventProcessState.Cancelled;
                _eventQueue.RemoveFirst();
                continue;
            }

            var type = queued.EventData.GetType();
            if (!_handlers.TryGetValue(type, out var list) || list.Count == 0)
            {
                queued.State = EventProcessState.Completed;
                _eventQueue.RemoveFirst();
                continue;
            }

            break;
        }
    }

    private void ProcessOneHandler(QueuedEvent queued)
    {
        var eventData = queued.EventData;
        var type = eventData.GetType();

        queued.State = EventProcessState.Processing;
        queued.ExecutedHandlers ??= new HashSet<BaseEventHandler>();

        if (_processingEvents.Contains(eventData))
        {
            Debug.LogWarning($"[LocalEventBus] 事件循环检测: {type.Name}");
            queued.State = EventProcessState.Completed;
            return;
        }

        _processingEvents.Add(eventData);

        try
        {
            if (!_handlers.TryGetValue(type, out var list) || list.Count == 0)
            {
                queued.State = EventProcessState.Completed;
                return;
            }

            while (queued.CurrentHandlerIndex < list.Count)
            {
                var handlerInfo = list[queued.CurrentHandlerIndex];
                queued.CurrentHandlerIndex++;

                var eventHandler = handlerInfo.EventHandler;

                if (queued.ExecutedHandlers.Contains(eventHandler)) continue;

                if (!eventData.Active)
                {
                    queued.State = EventProcessState.Cancelled;
                    return;
                }

                if (!eventHandler.CanHandle(eventData)) continue;

                queued.ExecutedHandlers.Add(eventHandler);

                try
                {
                    eventHandler.Handle(eventData);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[LocalEventBus] 处理事件出错: {ex.Message}\n{ex.StackTrace}");
                }

                if (!eventData.Active)
                {
                    queued.State = EventProcessState.Cancelled;
                }

                return;
            }

            queued.State = EventProcessState.Completed;
        }
        finally
        {
            _processingEvents.Remove(eventData);
        }
    }

    #endregion

    #region 发送API

    public void Send(BaseEvent eventData)
    {
        if (!eventData.Active) return;

        eventData.Source = EventSource.Local;

        if (eventData.Immediate)
        {
            ExecuteImmediate(eventData);
            return;
        }

        var queued = new QueuedEvent
        {
            EventData = eventData,
            Source = EventSource.Local
        };

        _eventQueue.AddLast(queued);
    }

    public void SendBatch(IEnumerable<BaseEvent> events)
    {
        if (events == null) return;

        foreach (var evt in events)
        {
            Send(evt);
        }
    }

    public void ReceiveNetworkEvent(BaseEvent eventData)
    {
        if (!eventData.Active) return;

        eventData.Source = EventSource.Network;

        if (eventData.Immediate)
        {
            ExecuteImmediate(eventData);
            return;
        }

        var queued = new QueuedEvent
        {
            EventData = eventData,
            Source = EventSource.Network
        };

        _eventQueue.AddLast(queued);
    }

    public void ReceiveNetworkEventImmediate(BaseEvent eventData)
    {
        eventData.Source = EventSource.Network;
        ExecuteImmediate(eventData);
    }

    private void ExecuteImmediate(BaseEvent eventData)
    {
        var type = eventData.GetType();
        if (!_handlers.TryGetValue(type, out var list)) return;

        foreach (var handlerInfo in list)
        {
            if (!eventData.Active) break;

            var eventHandler = handlerInfo.EventHandler;
            if (!eventHandler.CanHandle(eventData)) continue;

            try
            {
                eventHandler.Handle(eventData);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LocalEventBus] 立即执行出错: {ex.Message}");
            }
        }
    }

    #endregion

    #region 订阅管理

    public void Subscribe<T>(IEventHandler<T> eventHandler) where T : BaseEvent
    {
        if (eventHandler == null)
        {
            Debug.LogError("[LocalEventBus] 尝试注册空的 IEventHandler");
            return;
        }

        var type = typeof(T);
        var wrappedHandler = new EventHandlerWrapper<T>(eventHandler);
        SubscribeInternal(type, wrappedHandler, eventHandler.Priority);
    }

    private void SubscribeInternal(Type eventType, BaseEventHandler eventHandler, int priority)
    {
        if (priority < 0) priority = 0;

        var handlerInfo = new HandlerInfo
        {
            EventHandler = eventHandler,
            Priority = priority,
            Order = _subscriptionCounter++
        };

        if (!_handlers.TryGetValue(eventType, out var list))
        {
            list = new List<HandlerInfo>();
            _handlers[eventType] = list;
        }

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].EventHandler.Equals(eventHandler))
            {
                list[i].Priority = priority;
                SortHandlersByPriority(list);
                return;
            }
        }

        list.Add(handlerInfo);
        SortHandlersByPriority(list);
    }

    public void Unsubscribe<T>(IEventHandler<T> eventHandler) where T : BaseEvent
    {
        if (eventHandler == null) return;

        var type = typeof(T);
        if (_handlers.TryGetValue(type, out var list))
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].EventHandler is EventHandlerWrapper<T> wrapper &&
                    wrapper.InnerHandler.Equals(eventHandler))
                {
                    list.RemoveAt(i);
                    break;
                }
            }

            if (list.Count == 0)
                _handlers.Remove(type);
        }
    }

    public void ClearSubscriptions<T>() where T : BaseEvent
    {
        _handlers.Remove(typeof(T));
    }

    public void ClearAllSubscriptions()
    {
        _handlers.Clear();
        _subscriptionCounter = 0;
    }

    private void SortHandlersByPriority(List<HandlerInfo> list)
    {
        list.Sort((a, b) =>
        {
            int priorityCompare = b.Priority.CompareTo(a.Priority);
            if (priorityCompare != 0) return priorityCompare;
            return a.Order.CompareTo(b.Order);
        });
    }

    #endregion

    #region 锁管理

    public bool SetLockState(EventBusLockState newState)
    {
        int currentPriority = LockPriorityMap[_currentLockState];
        int newPriority = LockPriorityMap[newState];

        if (newPriority >= currentPriority)
        {
            _currentLockState = newState;
            return true;
        }

        Debug.LogWarning($"[LocalEventBus] 无法设置锁状态 {newState}，当前锁 {_currentLockState} 优先级更高");
        return false;
    }

    public bool ReleaseLock(EventBusLockState state)
    {
        if (_currentLockState == state)
        {
            _currentLockState = EventBusLockState.None;
            return true;
        }

        Debug.LogWarning($"[LocalEventBus] 无法释放锁 {state}，当前锁状态为 {_currentLockState}");
        return false;
    }

    public void ForceUnlock()
    {
        _currentLockState = EventBusLockState.None;
    }

    #endregion

    #region 事件废弃

    public void CancelEvents<T>() where T : BaseEvent
    {
        var type = typeof(T);
        CancelEvents(e => e.GetType() == type);
    }

    public void CancelEvents(Func<BaseEvent, bool> predicate)
    {
        var node = _eventQueue.First;
        while (node != null)
        {
            var queued = node.Value;
            if (queued.State == EventProcessState.Waiting && predicate(queued.EventData))
            {
                queued.State = EventProcessState.Cancelled;
            }
            node = node.Next;
        }
    }

    public void ClearAllWaitingEvents()
    {
        _eventQueue.Clear();
    }

    #endregion

    #region 内部类型

    public abstract class BaseEventHandler
    {
        public abstract bool CanHandle(BaseEvent evt);
        public abstract void Handle(BaseEvent evt);
        public abstract int Priority { get; }
    }

    private class HandlerInfo
    {
        public BaseEventHandler EventHandler;
        public int Priority;
        public long Order;
    }

    private class QueuedEvent
    {
        public BaseEvent EventData;
        public EventProcessState State = EventProcessState.Waiting;
        public int CurrentHandlerIndex = 0;
        public HashSet<BaseEventHandler> ExecutedHandlers;
        public EventSource Source;
    }

    private class EventHandlerWrapper<T> : BaseEventHandler where T : BaseEvent
    {
        public IEventHandler<T> InnerHandler { get; }

        public EventHandlerWrapper(IEventHandler<T> innerHandler)
        {
            InnerHandler = innerHandler;
        }

        public override int Priority => InnerHandler.Priority;

        public override bool CanHandle(BaseEvent evt)
        {
            return evt is T typedEvent && InnerHandler.CanHandle(typedEvent);
        }

        public override void Handle(BaseEvent evt)
        {
            if (evt is T typedEvent)
            {
                InnerHandler.Handle(typedEvent);
            }
        }

        public override bool Equals(object obj)
        {
            return obj is EventHandlerWrapper<T> other && InnerHandler.Equals(other.InnerHandler);
        }

        public override int GetHashCode()
        {
            return InnerHandler.GetHashCode();
        }
    }

    #endregion
}