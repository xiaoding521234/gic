using System;
using System.Collections.Generic;
using Mirror;
using GIC.Framework;
using GIC.Data;

namespace GIC.Battle
{
    /// <summary>战斗专用消息；不与房间事件共用路由。</summary>
    public struct BattleNetMessage : NetworkMessage
    {
        public string matchId;
        public int type;
        public string json;
    }

    /// <summary>把权威输入与本机表现分开；客户端完成装配才发就绪，不缓存跨场景消息。</summary>
    public sealed class MirrorBattleTransport : IBattleTransport, IDisposable
    {
        private static MirrorBattleTransport _current;

        private readonly string _matchId;
        private readonly bool _isHost;
        private readonly HashSet<string> _remotePlayers;
        private Action<BattleMessageType, string, string> _hostHandler;
        private Action<BattleMessageType, string> _clientHandler;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private long _sentMessages;
        private long _sentPayloadBytes;
#endif

        public MirrorBattleTransport(bool isHost, string matchId, IEnumerable<string> remotePlayers = null)
        {
            if (string.IsNullOrEmpty(matchId)) throw new ArgumentException("联机对局缺少 MatchId", nameof(matchId));
            _current?.Dispose();
            _isHost = isHost;
            _matchId = matchId;
            _remotePlayers = remotePlayers != null ? new HashSet<string>(remotePlayers, StringComparer.Ordinal) : null;
            _current = this;
            if (isHost)
                NetworkServer.ReplaceHandler<BattleNetMessage>(OnServerMessage);
            else
                NetworkClient.ReplaceHandler<BattleNetMessage>(OnClientMessage);
        }

        private void OnServerMessage(NetworkConnectionToClient conn, BattleNetMessage msg)
        {
            var type = (BattleMessageType)msg.type;
            if (_current != this || conn is LocalConnectionToClient || msg.matchId != _matchId || !IsUplink(type)) return;
            string playerId = conn.connectionId.ToString();
            if (_remotePlayers?.Contains(playerId) == true)
                _hostHandler?.Invoke(type, msg.json, playerId);
        }

        private void OnClientMessage(BattleNetMessage msg)
        {
            var type = (BattleMessageType)msg.type;
            if (_current == this && msg.matchId == _matchId && IsSimulationDownlink(type))
                _clientHandler?.Invoke(type, msg.json);
        }

        /// <summary>联机客户端自行演算表现，网络仅传开局、输入、终局与诊断。</summary>
        private static bool IsSimulationDownlink(BattleMessageType type)
            => type == BattleMessageType.BattleStart || type == BattleMessageType.InputBatch
               || type == BattleMessageType.BattleOver || type == BattleMessageType.DesyncAlert;

        private static bool IsUplink(BattleMessageType type)
            => type == BattleMessageType.InputCommand || type == BattleMessageType.HashReport;

        public void HostSend(BattleMessageType type, object message)
        {
            if (_current != this || !_isHost || !NetworkServer.active) return;
            string json = BattleJsonSerializer.Instance.Serialize(message);
            if (IsSimulationDownlink(type))
            {
                var packet = new BattleNetMessage { matchId = _matchId, type = (int)type, json = json };
                foreach (var conn in NetworkServer.connections.Values)
                {
                    if (conn is LocalConnectionToClient || _remotePlayers?.Contains(conn.connectionId.ToString()) != true)
                        continue;
                    conn.Send(packet);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    CountSend(json);
#endif
                }
            }
            if (type != BattleMessageType.InputBatch && type != BattleMessageType.DesyncAlert)
                _clientHandler?.Invoke(type, json);
        }

        public void ClientSend(BattleMessageType type, object message)
        {
            if (_current != this || _isHost || !NetworkClient.isConnected || !IsUplink(type)) return;
            string json = BattleJsonSerializer.Instance.Serialize(message);
            NetworkClient.Send(new BattleNetMessage { matchId = _matchId, type = (int)type, json = json });
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CountSend(json);
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void CountSend(string json)
        {
            _sentMessages++;
            _sentPayloadBytes += System.Text.Encoding.UTF8.GetByteCount(json);
        }
#endif

        public void RegisterHostHandler(Action<BattleMessageType, string, string> handler) => _hostHandler = handler;

        public void RegisterClientHandler(Action<BattleMessageType, string> handler) => _clientHandler = handler;

        public void Dispose()
        {
            if (_current != this) return;
            _current = null;
            if (_isHost) NetworkServer.UnregisterHandler<BattleNetMessage>();
            else NetworkClient.UnregisterHandler<BattleNetMessage>();
            _hostHandler = null;
            _clientHandler = null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            GICLog.Info($"[BattleTransport] {(_isHost ? "Host" : "Client")} 发送 {_sentMessages} 条，JSON 载荷 {_sentPayloadBytes} B（未计协议开销）");
#endif
        }
    }
}
