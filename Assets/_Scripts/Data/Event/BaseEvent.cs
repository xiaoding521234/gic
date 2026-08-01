// 基础事件
using System;
using GIC.Framework;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Data
{


    public class BaseEvent
    {
        public string SourcePlayerID = PlayerID.Unknown;
        public bool Active = true;
        public EventType Type = EventType.Local;
        public bool IgnoreAnimationLock = false;
        public bool Immediate = false;

        [NonSerialized]
        public EventSource Source = EventSource.Local;


    }
}


