// PlayerNetworkEvents.cs - 玩家网络相关事件
using System.Collections.Generic;
using GIC.Framework;
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
            Immediate = true;
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
            Immediate = true;
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
            Immediate = true;
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
            Immediate = true;
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
            Immediate = true;
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

    /// <summary>
    /// 客户端设置自己名称的请求（客户端 -> 服务器）
    /// </summary>
    public class SetPlayerNameRequestEvent : BaseEvent
    {
        public string PlayerName;

        public SetPlayerNameRequestEvent()
        {
            Type = GIC.Framework.EventType.OnlyHost;
            Immediate = true;
        }
    }

    // ==================== 房间正式化事件（2026-10-11 联机批） ====================

    /// <summary>
    /// 增加 AI 玩家请求（host 本地发起 -> 服务器登记；无网络连接方，AI=空连接玩家）
    /// </summary>
    public class AddAIPlayerRequestEvent : BaseEvent
    {
        public AddAIPlayerRequestEvent()
        {
            Type = GIC.Framework.EventType.OnlyHost;
            Immediate = true;
        }
    }

    /// <summary>
    /// 设置房间地图请求（host 下拉选图 -> 服务器记录 -> 广播 UpdateRoomInfoEvent）
    /// </summary>
    public class SetMapRequestEvent : BaseEvent
    {
        public string MapConfigName;

        public SetMapRequestEvent()
        {
            Type = GIC.Framework.EventType.OnlyHost;
            Immediate = true;
        }
    }

    /// <summary>
    /// 房间信息更新（服务器 -> 所有客户端）：当前权威地图名（各端同步下拉选中+地图预览）
    /// </summary>
    public class UpdateRoomInfoEvent : BaseEvent
    {
        public string MapConfigName;

        public UpdateRoomInfoEvent()
        {
            Type = GIC.Framework.EventType.All;
            Immediate = true;
        }
    }

    // ==================== 开战与卡组（B7 LAN 批） ====================

    /// <summary>
    /// 开战广播（服务器 -> 所有客户端）：host 校验通过后下发同一份开局配置（地图名/种子/名册），
    /// 各端转场加载同一战斗场景（docs/18 决策一：Host 权威；同 seed+同命令序列=同战局）。
    /// 名册 PlayerInfo 含 IsAI/Team/Color/SpawnPosition——颜色/队伍/出生点真实进局内。
    /// </summary>
    public class StartBattleEvent : BaseEvent
    {
        public string MapConfigName;
        public int Seed;
        public List<PlayerInfo> Players = new List<PlayerInfo>();

        public StartBattleEvent()
        {
            Type = GIC.Framework.EventType.All;
            Immediate = true;
        }
    }

    /// <summary>
    /// 客户端卡组上报（客户端 -> 服务器，OnlyHost）：收到 StartBattleEvent 后各真人客户端
    /// 回报自己存档当前卡组（host 用它在局内为该玩家构建手牌；AI 玩家用 host 当前卡组）。
    /// 复用 HandCard 协议结构（cardType/value/count 三元组，JsonUtility 直过——与快照手牌同构）。
    /// </summary>
    public class ClientDeckEvent : BaseEvent
    {
        public string PlayerID;
        public List<HandCard> Entries = new List<HandCard>();

        public ClientDeckEvent()
        {
            Type = GIC.Framework.EventType.OnlyHost;
            Immediate = true;
        }
    }

    /// <summary>
    /// 客户端战斗就绪握手（客户端 -> 服务器，OnlyHost）：LAN 客户端装配完毕（HUD 绑定完成、
    /// BattleNetMessage 路由就绪）后发出——host 收齐全部远端真人握手才广播开局（BattleStart），
    /// Host 永不跑在客户端前面（传输层另有早到队列双保险）。
    /// </summary>
    public class ClientBattleReadyEvent : BaseEvent
    {
        public ClientBattleReadyEvent()
        {
            Type = GIC.Framework.EventType.OnlyHost;
            Immediate = true;
        }
    }
}




