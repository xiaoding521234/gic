using System.Collections.Generic;
using GIC.Framework;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Data
{


    // 网络事件包装类
    [System.Serializable]
    public class NetworkEventData
    {
        public string EventType;                // 事件类型名称
        public string EventJson;                // 序列化后的事件数据
        public string SenderID;                 // 发送者ID
        public string TargetID;                 // 目标玩家ID
        public List<string> TargetIDs;          // 目标玩家ID列表
    }
}


