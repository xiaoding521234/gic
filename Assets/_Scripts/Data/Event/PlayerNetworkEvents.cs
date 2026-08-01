// PlayerNetworkEvents.cs - 玩家网络相关事件
using System;
using UnityEngine;
using GIC.Framework;
using GIC.Battle;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
namespace GIC.Data
{


    // ==================== 玩家管理事件 ====================

    /// <summary>
    /// 添加玩家事件（服务器 -> 所有客户端 + 指定客户端）
    /// </summary>
    public class AddPlayerEvent : BaseEvent
    {
        public PlayerInfo PlayerInfo;
        public bool ToAllClients = true; // true: 发给所有客户端, false: 发给指定客户端
        
        public AddPlayerEvent()
        {
            Type = GIC.Framework.EventType.All;
            Immediate = true;
        }
    }

    /// <summary>
    /// 移除玩家事件（服务器 -> 所有客户端）
    /// </summary>
    public class RemovePlayerEvent : BaseEvent
    {
        public string TargetPlayerID;
        
        public RemovePlayerEvent()
        {
            Type = GIC.Framework.EventType.All;
            Immediate = true;
        }
    }

    /// <summary>
    /// 更新玩家信息事件（服务器 -> 所有客户端）
    /// </summary>
    public class UpdatePlayerInfoEvent : BaseEvent
    {
        public PlayerInfo UpdatedInfo;
        
        public UpdatePlayerInfoEvent()
        {
            Type = GIC.Framework.EventType.All;
            Immediate = true;
        }
    }

    /// <summary>
    /// 被踢出通知事件（服务器 -> 指定客户端）
    /// </summary>
    public class KickedFromRoomEvent : BaseEvent
    {
        public KickedFromRoomEvent()
        {
            Type = GIC.Framework.EventType.None; // 发给特定玩家
            Immediate = true;
        }
    }

    // ==================== 客户端请求事件（客户端 -> 服务器） ====================

    /// <summary>
    /// 设置队伍请求
    /// </summary>
    public class SetTeamRequestEvent : BaseEvent
    {
        public string TargetPlayerID;
        public TeamType Team;
        
        public SetTeamRequestEvent()
        {
            Type = GIC.Framework.EventType.OnlyHost;
        }
    }

    /// <summary>
    /// 设置颜色请求
    /// </summary>
    public class SetColorRequestEvent : BaseEvent
    {
        public string TargetPlayerID;
        public PlayerColor Color;
        
        public SetColorRequestEvent()
        {
            Type = GIC.Framework.EventType.OnlyHost;
        }
    }

    /// <summary>
    /// 设置出生点请求
    /// </summary>
    public class SetSpawnRequestEvent : BaseEvent
    {
        public string PlayerID;
        public SpawnPositionType SpawnPosition;
        
        public SetSpawnRequestEvent()
        {
            Type = GIC.Framework.EventType.OnlyHost;
        }
    }

    /// <summary>
    /// 切换准备状态请求
    /// </summary>
    public class ToggleReadyRequestEvent : BaseEvent
    {
        public string TargetPlayerID;
        
        public ToggleReadyRequestEvent()
        {
            Type = GIC.Framework.EventType.OnlyHost;
        }
    }

    /// <summary>
    /// 踢出玩家请求
    /// </summary>
    public class KickPlayerRequestEvent : BaseEvent
    {
        public string TargetPlayerID;
        
        public KickPlayerRequestEvent()
        {
            Type = GIC.Framework.EventType.OnlyHost;
        }
    }

    /// <summary>
    /// 服务器告知客户端其自身的 PlayerID (connectionId)
    /// </summary>
    public class SetSelfPlayerEvent : BaseEvent
    {
        public string TargetPlayerID;
        
        public SetSelfPlayerEvent()
        {
            Type = GIC.Framework.EventType.None;   // 发给特定玩家
            Immediate = true;        // 立即执行
        }
    }
}




