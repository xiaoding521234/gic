using System;
namespace GIC.Data
{


    /// <summary>
    /// 战斗传输消息类型（信封路由用）
    /// </summary>
    public enum BattleMessageType
    {
        BattleStart = 0,        // Host→Client：对局开始（地形全量 + 初始快照）
        Snapshot = 1,          // Host→Client：选择阶段头全量
        SubmitAction = 2,      // Client→Host：行动选择上交
        Segment = 3,           // Host→Client：片命令块推流
        SegmentAck = 4,        // Client→Host：片播放完成确认
        SubmitInstantAction = 5, // Client→Host：即时行动（片边界 poll）
        TurnEnd = 6,           // Host→Client：回合结束
        Pause = 7,             // Host↔Client：硬停交互点（B1 占位）
        Resume = 8,            // Host↔Client：硬停恢复（B1 占位）
        BattleOver = 9,        // Host→Client：战斗结束（2026-09-25 三轮审查 S10 轻量全灭软停；结算画面=B8）
    }

    /// <summary>
    /// 传输信封（类型 + JSON 载荷；本地模式也走本格式以验证可序列化性）
    /// </summary>
    [Serializable]
    public class BattleMessageEnvelope
    {
        public BattleMessageType messageType;
        public string json;
    }

    [Serializable]
    public class BattleStartMessage
    {
        public BattleMapData map;
        public BattleSnapshot initialSnapshot;
    }

    [Serializable]
    public class SnapshotMessage
    {
        public BattleSnapshot snapshot;
    }

    [Serializable]
    public class SubmitActionRequest
    {
        public ActionData action;
    }

    [Serializable]
    public class SegmentMessage
    {
        public Segment segment;
    }

    [Serializable]
    public class SegmentAck
    {
        public int turnNumber;
        public int sliceIndex;
    }

    [Serializable]
    public class SubmitInstantAction
    {
        public ActionData action;
    }

    [Serializable]
    public class TurnEndMessage
    {
        public int turnNumber;
    }

    [Serializable]
    public class PauseMessage
    {
        public string reason;
    }

    [Serializable]
    public class ResumeMessage
    {
        public string reason;
    }

    /// <summary>战斗结束消息（S10 轻量软停）：winnerTeam=存活方队伍（TeamType 枚举值）——
    /// 恰好一队全灭时下发；双方同回合互灭不下发（结算语义 B8 定义）</summary>
    [Serializable]
    public class BattleOverMessage
    {
        public int winnerTeam;
    }
}
