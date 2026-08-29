// LocalEventBus.cs - 纯本地事件处理
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Framework
{


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

        public int QueuedEventCount => _eventQueue.Count;
        public bool HasQueuedEvents => _eventQueue.Count > 0;
        public EventBusLockState CurrentLockState => _currentLockState;
        public bool IsLocked => _currentLockState != EventBusLockState.None;

        #region evtHandle

        /// <summary>
        /// 每个 FixedUpdate 调用一次，处理一个 handler
        /// </summary>
        public void Tick()
        {
            ProcessOneHandlerPerFrame();
        }

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
                GICLog.Warn($"[LocalEventBus] 事件循环检测: {type.Name}");
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
                        GICLog.Error($"[LocalEventBus] 处理事件出错: {ex.Message}\n{ex.StackTrace}");
                    }

                    if (!eventData.Active)
                    {
                        queued.State = EventProcessState.Cancelled;
                        return;
                    }

                    // 没有更多handler了，立即标记完成（不浪费一个tick）
                    if (queued.CurrentHandlerIndex >= list.Count)
                    {
                        queued.State = EventProcessState.Completed;
                    }
                    // 还有更多handler，等下一个tick处理
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

        #region sendApi

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
                    GICLog.Error($"[LocalEventBus] 立即执行出错: {ex.Message}");
                }
            }
        }

        #endregion

        #region subManage

        public void Subscribe<T>(IEventHandler<T> eventHandler) where T : BaseEvent
        {
            if (eventHandler == null)
            {
                GICLog.Error("[LocalEventBus] 尝试注册空的 IEventHandler");
                return;
            }

            var type = typeof(T);
            var wrappedHandler = new EventHandlerWrapper<T>(eventHandler);
            SubscribeInternal(type, wrappedHandler, eventHandler.Priority, owner: null);
        }

        /// <summary>
        /// 带 owner 登记：UnsubscribeAllForOwner(owner) 可一次性退订该 owner 的全部订阅，
        /// 避免 MonoBehaviour 在 OnDestroy 手工逐个 Unsubscribe（漏一个就泄漏）。
        /// </summary>
        public void Subscribe<T>(IEventHandler<T> eventHandler, object owner) where T : BaseEvent
        {
            if (eventHandler == null)
            {
                GICLog.Error("[LocalEventBus] 尝试注册空的 IEventHandler");
                return;
            }

            var type = typeof(T);
            var wrappedHandler = new EventHandlerWrapper<T>(eventHandler);
            SubscribeInternal(type, wrappedHandler, eventHandler.Priority, owner);
        }

        private void SubscribeInternal(Type eventType, BaseEventHandler eventHandler, int priority, object owner)
        {
            if (priority < 0) priority = 0;

            var handlerInfo = new HandlerInfo
            {
                EventHandler = eventHandler,
                Priority = priority,
                Order = _subscriptionCounter++,
                Owner = owner
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

        /// <summary>
        /// 退订 owner 登记的全部订阅（OnDestroy/Cleanup 一行兜底）。
        /// 返回移除数量，0 = 无登记（可能漏传 owner，便于排查）。
        /// </summary>
        public int UnsubscribeAllForOwner(object owner)
        {
            if (owner == null) return 0;

            int removed = 0;
            List<Type> emptyTypes = null;

            foreach (var kv in _handlers)
            {
                var list = kv.Value;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (ReferenceEquals(list[i].Owner, owner))
                    {
                        list.RemoveAt(i);
                        removed++;
                    }
                }

                if (list.Count == 0)
                {
                    emptyTypes ??= new List<Type>();
                    emptyTypes.Add(kv.Key);
                }
            }

            if (emptyTypes != null)
                foreach (var t in emptyTypes)
                    _handlers.Remove(t);

            if (removed > 0)
                PruneInvalidatedQueuedEvents();

            return removed;
        }

        /// <summary>订阅清空后，把队列中已无处理者的事件标记完成，避免 Tick 空转</summary>
        private void PruneInvalidatedQueuedEvents()
        {
            var node = _eventQueue.First;
            while (node != null)
            {
                var queued = node.Value;
                var type = queued.EventData.GetType();
                bool noHandlers = !_handlers.TryGetValue(type, out var list) || list.Count == 0;

                if (queued.State == EventProcessState.Waiting && noHandlers)
                    queued.State = EventProcessState.Completed;

                node = node.Next;
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

        #region lockManage

        public bool SetLockState(EventBusLockState newState)
        {
            int currentPriority = LockPriorityMap[_currentLockState];
            int newPriority = LockPriorityMap[newState];

            if (newPriority >= currentPriority)
            {
                _currentLockState = newState;
                return true;
            }

            GICLog.Warn($"[LocalEventBus] 无法设置锁状态 {newState}，当前锁 {_currentLockState} 优先级更高");
            return false;
        }

        public bool ReleaseLock(EventBusLockState state)
        {
            if (_currentLockState == state)
            {
                _currentLockState = EventBusLockState.None;
                return true;
            }

            GICLog.Warn($"[LocalEventBus] 无法释放锁 {state}，当前锁状态为 {_currentLockState}");
            return false;
        }

        public void ForceUnlock()
        {
            _currentLockState = EventBusLockState.None;
        }

        #endregion

        #region evtDiscard

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

        #region internalType

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
            public object Owner; // 订阅登记人（可选），UnsubscribeAllForOwner 用
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
}


