using System;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Framework
{


    public class NetworkEventBus : NetworkBehaviour
    {
        public string SelfConnectionId
        {
            get
            {
                if (PlayerManager.Instance != null)
                {
                    string id = PlayerManager.Instance.SelfPlayerID;
                    if (id != PlayerID.Offline && !string.IsNullOrEmpty(id)) return id;
                }
                return PlayerID.Offline;
            }
        }

        private void Awake()
        {
            NetworkEventRegistry.Initialize();
        }

        public override void OnStartServer()
        {
            NetworkServer.ReplaceHandler<NetworkEventMessage>(OnServerReceive);
            // Host 模式下 OnStartServer 先于 OnStartClient 执行，
            // 但 OnServerConnect 已开始向 host client 发消息，需提前注册 client handler
            NetworkClient.ReplaceHandler<NetworkEventMessage>(OnClientReceive);
        }

        public override void OnStartClient()
        {
            NetworkClient.ReplaceHandler<NetworkEventMessage>(OnClientReceive);
        }

        /// <summary>
        /// 客户端收到消息
        /// </summary>
        private void OnClientReceive(NetworkEventMessage msg)
        {
            var eventData = NetworkEventRegistry.Unpack(msg);
            if (eventData != null)
                EventBusHub.Instance?.ReceiveNetworkEvent(eventData);
        }

        /// <summary>
        /// 服务器收到客户端消息，根据 EventType 路由
        /// </summary>
        private void OnServerReceive(NetworkConnection conn, NetworkEventMessage msg)
        {
            var eventData = NetworkEventRegistry.Unpack(msg);
            if (eventData == null) return;

            // 用 connectionId 修正发送者
            if (conn is NetworkConnectionToClient clientConn)
                eventData.SourcePlayerID = clientConn.connectionId.ToString();
            else
                eventData.SourcePlayerID = PlayerID.Unknown;

            string json = msg.Json;
            switch (eventData.Type)
            {
                case EventType.All:
                    // 广播给所有客户端 + 服务器自己
                    foreach (var c in NetworkServer.connections.Values)
                        c.Send(new NetworkEventMessage { EventId = msg.EventId, SenderID = msg.SenderID, Json = json });
                    EventBusHub.Instance?.ReceiveNetworkEvent(eventData);
                    break;

                case EventType.OnlyHost:
                    EventBusHub.Instance?.ReceiveNetworkEvent(eventData);
                    break;

                case EventType.None:
                    // 定向发送
                    if (!string.IsNullOrEmpty(msg.TargetID))
                    {
                        foreach (var c in NetworkServer.connections.Values)
                        {
                            if (c.connectionId.ToString() == msg.TargetID)
                            { c.Send(msg); break; }
                        }
                    }
                    EventBusHub.Instance?.ReceiveNetworkEvent(eventData);
                    break;
            }
        }

        #region 发送

        public void SendToAll(BaseEvent eventData)
        {
            if (eventData == null || !eventData.Active) return;
            var msg = NetworkEventRegistry.Pack(eventData, SelfConnectionId);

            if (isServer)
            {
                var localEvent = NetworkEventRegistry.Unpack(msg);
                if (localEvent != null) EventBusHub.Instance?.ReceiveNetworkEvent(localEvent);
                foreach (var conn in NetworkServer.connections.Values)
                    conn.Send(msg);
            }
            else
            {
                NetworkClient.Send(msg);
            }
        }

        public void SendToHost(BaseEvent eventData)
        {
            if (eventData == null || !eventData.Active) return;
            var msg = NetworkEventRegistry.Pack(eventData, SelfConnectionId);

            if (isServer)
            {
                var localEvent = NetworkEventRegistry.Unpack(msg);
                if (localEvent != null) EventBusHub.Instance?.ReceiveNetworkEvent(localEvent);
            }
            else
            {
                NetworkClient.Send(msg);
            }
        }

        public void SendToPlayer(string targetPlayerID, BaseEvent eventData)
        {
            if (eventData == null || !eventData.Active) return;
            var msg = NetworkEventRegistry.Pack(eventData, SelfConnectionId, targetPlayerID: targetPlayerID);

            if (isServer)
            {
                foreach (var conn in NetworkServer.connections.Values)
                {
                    if (conn.connectionId.ToString() == targetPlayerID)
                    { conn.Send(msg); return; }
                }
            }
            else
            {
                NetworkClient.Send(msg);
            }
        }

        public void SendToPlayers(List<string> targetPlayerIDs, BaseEvent eventData)
        {
            if (eventData == null || !eventData.Active) return;
            var msg = NetworkEventRegistry.Pack(eventData, SelfConnectionId);

            if (isServer)
            {
                foreach (var conn in NetworkServer.connections.Values)
                {
                    if (targetPlayerIDs.Contains(conn.connectionId.ToString()))
                        conn.Send(msg);
                }
            }
            else
            {
                NetworkClient.Send(msg);
            }
        }

        #endregion
    }
}


