using Mirror;
using GIC.Framework;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Data
{


    /// <summary>
    /// 网络事件消息 — ushort EventId + 单次 JSON，避免双重序列化
    /// </summary>
    public struct NetworkEventMessage : NetworkMessage
    {
        public ushort EventId;      // 事件类型 ID（NetworkEventRegistry 分配）
        public string SenderID;     // 发送者 PlayerID
        public string TargetID;     // 单个目标（SendToPlayer 用）
        public string Json;         // 事件数据 JSON（单次序列化）
    }
}

