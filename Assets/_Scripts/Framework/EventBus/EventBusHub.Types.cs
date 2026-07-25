// EventTypes.cs - 所有共享的类型定义
using System;
using System.Collections.Generic;

// 锁状态枚举
public enum EventBusLockState
{
    None = 0,
    Animation = 1,
    Highest = 2
}

// 优先级枚举
public static class EventPriority
{
    public const int Lowest = 0;
    public const int Low = 2;
    public const int PrinciplesSub = 3;
    public const int Normal = 5;
    public const int High = 8;
    public const int Highest = 10;
    public const int System = 100;
    public const int Critical = 1000;
}

// 事件处理状态
public enum EventProcessState
{
    Waiting,
    Processing,
    Completed,
    Cancelled
}


public enum EventType
{
    Local,
    All,
    OnlyHost,
    None,
}

public enum EventSource
{
    Local,
    Network
}
