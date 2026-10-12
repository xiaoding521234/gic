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

        /// <summary>AI 玩家难度档（2026-10-12 选择器批，AI-4：房间 AI 行可选、JsonUtility 按 int 序列化；
        /// 真人玩家本字段无意义恒默认）</summary>
        public AIDifficulty AIDifficulty = AIDifficulty.Grandmaster;

        /// <summary>AI 玩家人格原型名（Resources/Configs/AI/Persona_{名}——SO 引用不走 JsonUtility 存名，
        /// 进局内 BattleScreen 按名换资产；默认=经济家）</summary>
        public string AIPersona = "经济家";

        // 网络连接引用（服务器端使用）
        [NonSerialized]
        public NetworkConnectionToClient connectionToClient;
    }
}



