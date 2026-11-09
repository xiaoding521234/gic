// MirrorBattleTransport.cs - 战斗传输 Mirror 实现（B7 LAN，2026-10-11 联机批）
// docs/22 §4/§8：B1-B6 本地模式走 LocalBattleTransport；B7 换本实现，上层（BattleSession 路由/
// 序列化）零改动。铁律保持：战斗消息不走 EventBusHub，走独立 Mirror 消息类型 BattleNetMessage。
using System;
using System.Collections.Generic;
using Mirror;
using GIC.Framework;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 战斗消息的 Mirror 网络信封（独立于房间的 NetworkEventMessage——战斗专用通道）：
    /// type=BattleMessageType 枚举值 + JSON 载荷（与 LocalBattleTransport 同构：双端反序列化代码不变）。
    /// struct=Mirror RegisterHandler 泛型约束要求（where T : struct, NetworkMessage）。
    /// </summary>
    public struct BattleNetMessage : NetworkMessage
    {
        public int type;
        public string json;
    }

    /// <summary>
    /// IBattleTransport 的 Mirror 实现：
    /// - HostSend（Host→全部客户端）：逐远端连接 Send + host 本机视图直投（host 也是观众/玩家）；
    /// - ClientSend（客户端→Host）：host 本机=直调（无网络往返）；远端客户端=NetworkClient.Send；
    /// - RegisterHostHandler 携带发送方标识（SegmentAck 按连接计数用，B7 LAN 多客户端门控）。
    /// Mirror 处理器注册为静态路由（一进程一局）：消息早到（会话未建）先入待发队列，
    /// 客户端路由注册时冲队（握手门控之外的双保险，保序）。
    /// </summary>
    public class MirrorBattleTransport : IBattleTransport
    {
        // 进程内当前活动传输（Mirror 处理器是全局静态表——路由目标必须是静态可达）
        private static MirrorBattleTransport _current;

        // 早到消息队列（客户端路由未注册前收到的 Host 下行，注册时冲队；容量上限防病态堆积）
        private static readonly Queue<BattleNetMessage> _earlyClientQueue = new Queue<BattleNetMessage>();
        private const int EarlyQueueCapacity = 64;

        private Action<BattleMessageType, string, string> _hostHandler;   // (type, json, senderId)
        private Action<BattleMessageType, string> _clientHandler;
        private readonly bool _isHost;

        public MirrorBattleTransport(bool isHost)
        {
            _isHost = isHost;
            _current = this;
            RegisterMirrorHandlers();
        }

        // ==================== Mirror 静态注册（替换语义，再开局重注册不叠加） ====================

        private void RegisterMirrorHandlers()
        {
            // 服务器侧：收客户端上行（行动上交/ack/即时行动）
            NetworkServer.ReplaceHandler<BattleNetMessage>(OnServerMessage, false);
            // 客户端侧：收 Host 下行（开局/快照/片命令块/秒拍结束段）
            NetworkClient.ReplaceHandler<BattleNetMessage>(OnClientMessage, false);
        }

        private static void OnServerMessage(NetworkConnectionToClient conn, BattleNetMessage msg)
        {
            if (_current == null) return;
            _current._hostHandler?.Invoke((BattleMessageType)msg.type, msg.json,
                conn is LocalConnectionToClient ? "local" : conn.connectionId.ToString());
        }

        private static void OnClientMessage(BattleNetMessage msg)
        {
            if (_current == null || _current._clientHandler == null)
            {
                // 会话未建（理论上被开局握手挡住）：入队待冲，超容弃尾告警
                if (_earlyClientQueue.Count >= EarlyQueueCapacity)
                {
                    GICLog.Warn($"[MirrorBattleTransport] 早到消息队列溢出，丢弃 type={msg.type}");
                    return;
                }
                _earlyClientQueue.Enqueue(msg);
                return;
            }
            _current._clientHandler.Invoke((BattleMessageType)msg.type, msg.json);
        }

        // ==================== IBattleTransport ====================

        public void HostSend(BattleMessageType type, object message)
        {
            var envelope = new BattleMessageEnvelope
            {
                messageType = type,
                json = BattleJsonSerializer.Instance.Serialize(message),
            };
            // 双层信封序列化持续验证可序列化性（同 LocalBattleTransport 纪律）
            BattleJsonSerializer.Instance.Serialize(envelope);

            var netMsg = new BattleNetMessage { type = (int)type, json = envelope.json };

            // 逐远端连接下发（host 本地连接 connectionId=0 直投，不走 Mirror——双投=host 视图双收）
            if (NetworkServer.active)
            {
                foreach (var conn in NetworkServer.connections.Values)
                {
                    if (conn is LocalConnectionToClient) continue;
                    conn.Send(netMsg);
                }
            }
            else
            {
                GICLog.Warn("[MirrorBattleTransport] 非 Host 端调用 HostSend，忽略");
            }

            _clientHandler?.Invoke(type, envelope.json); // host 本机客户端视图直投
        }

        public void ClientSend(BattleMessageType type, object message)
        {
            var envelope = new BattleMessageEnvelope
            {
                messageType = type,
                json = BattleJsonSerializer.Instance.Serialize(message),
            };
            BattleJsonSerializer.Instance.Serialize(envelope);

            if (NetworkServer.active)
            {
                // host 本机的客户端视图：上交直投本机 Host 路由（零网络往返）
                _hostHandler?.Invoke(type, envelope.json, "local");
            }
            else if (NetworkClient.active)
            {
                NetworkClient.Send(new BattleNetMessage { type = (int)type, json = envelope.json });
            }
            else
            {
                GICLog.Warn("[MirrorBattleTransport] 无活动连接，ClientSend 丢弃");
            }
        }

        public void RegisterHostHandler(Action<BattleMessageType, string, string> handler)
            => _hostHandler = handler;

        public void RegisterClientHandler(Action<BattleMessageType, string> handler)
        {
            _clientHandler = handler;
            // 冲早到队列（保序：入队即序，先到先投）
            while (_earlyClientQueue.Count > 0)
            {
                var msg = _earlyClientQueue.Dequeue();
                _clientHandler.Invoke((BattleMessageType)msg.type, msg.json);
            }
        }

        // ==================== 连接查询（BattleSession 期望 ack 数注入用） ====================

        /// <summary>远端真人连接数（剔除 host 本地连接；非服务器端=0）</summary>
        public static int RemoteClientCount()
        {
            if (!NetworkServer.active) return 0;
            int remote = 0;
            foreach (var conn in NetworkServer.connections.Values)
                if (conn is not LocalConnectionToClient) remote++;
            return remote;
        }
    }
}
