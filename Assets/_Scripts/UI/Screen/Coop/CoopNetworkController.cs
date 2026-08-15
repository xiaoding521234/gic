using System.Collections.Generic;
using Mirror;
using Mirror.Discovery;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
using GIC.Tool;
namespace GIC.UI
{


    /// <summary>
    /// 联机网络控制器 — 从 CoopScreen 分离出的网络发现/连接逻辑
    /// </summary>
    public class CoopNetworkController
    {
        private readonly MyNetworkManager _networkManager;
        private readonly MyNetworkDiscovery _discovery;

        private readonly Dictionary<string, ServerResponse> _foundServers = new();
        private bool _isDiscovering;
        private float _discoveryStartTime;
        private const float DiscoveryTimeout = 3f;

        public bool IsDiscovering => _isDiscovering;
        public bool IsTimedOut => _isDiscovering && Time.time - _discoveryStartTime > DiscoveryTimeout;
        public IReadOnlyDictionary<string, ServerResponse> FoundServers => _foundServers;

        public CoopNetworkController(MyNetworkManager networkManager, MyNetworkDiscovery discovery)
        {
            _networkManager = networkManager;
            _discovery = discovery;
        }

        public void StartDiscovery()
        {
            _isDiscovering = true;
            _discoveryStartTime = Time.time;
            _discovery.StartDiscovering();
        }

        public void StopDiscovery()
        {
            _isDiscovering = false;
            _discovery.StopDiscovering();
        }

        public void StartBroadcast()
        {
            if (_discovery != null)
                _discovery.StartBroadcast();
        }

        public void StopBroadcast()
        {
            if (_discovery != null)
                _discovery.StopBroadcast();
        }

        public string GetLocalIP() => _discovery?.GetLocalIPAddress() ?? "127.0.0.1";

        public void StartHost()
        {
            StopDiscovery();
            _networkManager.StartHostMode();
        }

        public void JoinRoom(string ip, int port)
        {
            StopDiscovery();
            _networkManager.StopHost();
            _networkManager.JoinRoom(ip, port);
        }

        public void LeaveRoom()
        {
            StopBroadcast();
            _networkManager.StopHost();
        }

        public void DisconnectClient()
        {
            if (NetworkClient.isConnected)
                NetworkClient.Disconnect();
        }

        public void HandleServerFound(ServerResponse response)
        {
            string key = $"{response.EndPoint.Address}:{response.uri.Port}";
            if (_foundServers.ContainsKey(key)) return;

            _foundServers[key] = response;
            _isDiscovering = false;
        }

        public void ClearServers() => _foundServers.Clear();

        public int GetCurrentPort() => _networkManager?.GetCurrentPort() ?? 0;
    }

}


