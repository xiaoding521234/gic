using System;
using System.Collections.Generic;
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
        // 4 已退役（P6 残留审计 D2，docs/active/49：SegmentAck 随回合机 ack 门控删除，编号不复用）
        SubmitInstantAction = 5, // Client→Host：即时行动（片边界 poll）
        // 6 已退役（2026-11-09 残留清理批：TurnEnd 随回合机退役删除，编号不复用）
        Pause = 7,             // Host↔Client：硬停交互点（B1 占位）
        Resume = 8,            // Host↔Client：硬停恢复（B1 占位）
        BattleOver = 9,        // Host→Client：战斗结束（2026-09-25 三轮审查 S10 轻量全灭软停；结算画面=B8）
        // 10 已退役（2026-11-09 残留清理批：TurnPlan 随攻速分桶/执行预览退役删除，编号不复用）

        // ===== 帧同步（F1，docs/active/50 §4）=====
        InputCommand = 11,     // Client→Host：单条输入（玩家行动 + seq/submitTick）
        InputBatch = 12,       // Host→All：Host 定序后的输入批（targetTick + OrderedInput[]）
        HashReport = 13,       // Client→Host：本地状态 hash（每秒）
        DesyncAlert = 14,      // Host→Client：hash 失配诊断（F1 只告警）
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

        /// <summary>F1 帧同步：战斗种子（客户端按同 seed 建 DeterministicRng，RNG 序列与 Host 一致）</summary>
        public int seed;

        /// <summary>F1 帧同步：开局名册（客户端按同序建协议核心，保证 unitId 分配序一致；
        /// 类型在 GIC.Battle 命名空间，同程序集内可跨命名空间引用）</summary>
        public List<GIC.Battle.BattlePlayerSetup> roster;

        /// <summary>迷雾模式（BattleFogMode 枚举值；客户端 Sim 与 Host 同模式）</summary>
        public int fogMode;
    }

    // ==================== 帧同步（F1，docs/active/50 §4） ====================

    [Serializable]
    public class InputCommandMessage
    {
        public string playerId;
        public int seq;          // 玩家内单调递增（Host 去重/诊断，不参与跨玩家排序）
        public int submitTick;   // 客户端本地提交 tick（Host 重定基用）
        public ActionData action;
    }

    [Serializable]
    public class OrderedInput
    {
        public string playerId;
        public int seq;
        public int isAi;         // 1=AI 行动（Host 生成，客户端只消费，不跑脑）
        public ActionData action;
    }

    [Serializable]
    public class InputBatchMessage
    {
        public int tick;         // targetTick：各端在该 tick 应用整批
        public List<OrderedInput> inputs = new List<OrderedInput>();
    }

    [Serializable]
    public class HashReportMessage
    {
        public int tick;
        public int hash;
        public int unitCount;
    }

    [Serializable]
    public class DesyncAlertMessage
    {
        public int tick;
        public int hostHash;
        public int clientHash;
    }

    [Serializable]
    public class SnapshotMessage
    {
        public BattleSnapshot snapshot;

        /// <summary>本选择阶段时限秒（B7 LAN 批）：host 在选择阶段头随快照下发——
        /// 客户端无 Flow 控制器，凭此值本地倒数。即时制无选择倒计时：恒 -1（原倒数/
        /// 归零自动完成选择/host 超时兜底 Pass 链路已随回合机退役，字段保留供既有消息体兼容）。
        /// -1=无时限信息（调试直开/回放等场景，客户端不显示倒计时）</summary>
        public int selectLimitSeconds = -1;
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

    // P6 残留审计 D2：SegmentAck 消息类随 ack 门控退役删除（即时制命令流带 startTick，
// 客户端按绝对时刻播，无需回执；LAN 后置重做时按新协议另立）。

    [Serializable]
    public class SubmitInstantAction
    {
        public ActionData action;
    }

    // （TurnEndMessage 已随回合机退役删除——2026-11-09 残留清理批：秒拍段即回合结束段，
    // 无独立消息；重新立项按即时制命令流另立）

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
    /// 恰好一队全灭时下发；双方同秒互灭不下发（结算语义 B8 定义）</summary>
    [Serializable]
    public class BattleOverMessage
    {
        public int winnerTeam;
    }

    // （TurnPlanMessage/TurnPlanEntry 已随回合机退役删除——2026-11-09 残留清理批；
    // 行动预告/执行预览 UI 已随 P4 批退役，重立项按即时制命令流另立消息）
}
