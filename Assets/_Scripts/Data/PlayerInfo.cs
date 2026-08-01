// PlayerInfo.cs - 玩家信息数据结构
using System;
using UnityEngine;
using Mirror;
using GIC.Battle;
using GIC.Framework;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
namespace GIC.Data
{


    /// <summary>
    /// 玩家信息
    /// </summary>
    [Serializable]
    public class PlayerInfo
    {
        public string PlayerID;
        public string PlayerName;
        public TeamType Team;
        public PlayerColor Color;
        public SpawnPositionType SpawnPosition;
        public string IPAddress;
        public bool IsHost;
        public bool IsReady;
        public bool IsConnected;

        // 网络连接引用（服务器端使用）
        [NonSerialized]
        public NetworkConnectionToClient connectionToClient;
    }
}



