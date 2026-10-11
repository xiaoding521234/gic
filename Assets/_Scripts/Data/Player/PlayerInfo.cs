// PlayerInfo.cs - 玩家信息数据结构
using System;
using Mirror;
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

        /// <summary>AI 玩家（2026-10-11 联机房间正式化批：host 经「增加AI玩家」按钮登记的空连接玩家，
        /// 无 NetworkConnection——踢出/移除链对 null 连接已有守卫；进局内由 Host 侧配额脑驱动）</summary>
        public bool IsAI;

        // 网络连接引用（服务器端使用）
        [NonSerialized]
        public NetworkConnectionToClient connectionToClient;
    }
}



