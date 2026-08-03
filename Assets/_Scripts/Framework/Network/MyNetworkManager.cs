// MyNetworkManager.cs - 网络管理器（用 connectionId 标识玩家）
using UnityEngine;
using Mirror;
using kcp2k;
using System;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Framework
{


    public class MyNetworkManager : NetworkManager
    {
        [Header("战棋设置")]
        public int maxPlayers = 6;
        public int basePort = 7777;
        public int maxPortAttempts = 100;

        private bool _hostStarted = false;
        private bool _isKicked = false;
        private bool _isIntentionalDisconnect = false;

        public MyNetworkDiscovery discovery;
        private KcpTransport _kcpTransport;

        // 自定义事件
        public event System.Action OnClientConnectedEvent;
        public event System.Action OnClientDisconnectedEvent;
        public event System.Action<string> OnSelfPlayerIDReceived;

        public bool IsHost => _hostStarted && NetworkServer.active;
        public int CurrentPort { get; private set; }

        private PlayerManager _playerManager;

        public override void Awake()
        {
            gameObject.SetActive(true);
            base.Awake();
            gameObject.SetActive(true);

            _kcpTransport = transport as KcpTransport;
            if (_kcpTransport == null)
            {
                Debug.LogError("[MyNetworkManager] 传输组件不是 KcpTransport，请检查 NetworkManager 配置");
            }

            if (discovery == null)
                discovery = GetComponent<MyNetworkDiscovery>();

            _playerManager = Wargame.Instance.PlayerManager;
            _playerManager?.SetNetworkManager(this);

            Debug.Log("[MyNetworkManager] 初始化完成");
        }

        public override void Start()
        {
            base.Start();

            if (_playerManager == null)
            {
                Debug.LogError("[MyNetworkManager] PlayerManager 不存在！");
            }

            Debug.Log("[MyNetworkManager] 启动完成，等待用户操作");
        }

        // ==================== 公共方法 ====================

        public void StartHostMode()
        {
            if (NetworkServer.active || NetworkClient.isConnected)
            {
                Debug.Log("[MyNetworkManager] 检测到已有连接，先停止");
                StopHost();
                _hostStarted = false;
            }

            int port = basePort;
            while (port < basePort + maxPortAttempts && !_hostStarted)
            {
                if (TryStartHostOnPort(port))
                {
                    _hostStarted = true;
                    CurrentPort = port;
                    Debug.Log($"[MyNetworkManager] 主机模式启动成功，端口: {port}");
                }
                else
                {
                    port++;
                }
            }

            if (!_hostStarted)
            {
                Debug.LogError($"[MyNetworkManager] 无法启动主机模式，所有端口被占用");
            }
        }

        private bool TryStartHostOnPort(int port)
        {
            try
            {
                SetPort(port);
                StartHost();
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[MyNetworkManager] 端口 {port} 启动失败: {ex.Message}");
                return false;
            }
        }

        public void JoinRoom(string ip, int port)
        {
            if (_hostStarted && NetworkServer.active)
            {
                Debug.Log("[MyNetworkManager] 先停止主机模式再加入其他房间");
                StopHost();
                _hostStarted = false;
            }

            if (NetworkClient.isConnected)
            {
                Debug.Log("[MyNetworkManager] 先断开当前客户端连接");
                NetworkClient.Disconnect();
            }

            networkAddress = ip;
            SetPort(port);
            StartClient();
            Debug.Log($"[MyNetworkManager] 正在加入房间: {ip}:{port}");
        }

        public void MarkAsKicked()
        {
            _isKicked = true;
        }

        private void SetPort(int port)
        {
            if (_kcpTransport != null)
            {
                _kcpTransport.Port = (ushort)port;
            }
        }

        public int GetCurrentPort()
        {
            if (_kcpTransport != null)
            {
                return _kcpTransport.Port;
            }
            return CurrentPort;
        }

        // ==================== Mirror 回调 ====================

        public override void OnStartServer()
        {
            base.OnStartServer();
            _hostStarted = true;
            CurrentPort = GetCurrentPort();

            if (discovery != null)
            {
                discovery.StopDiscovering();
                discovery.StartBroadcast();
            }

            Debug.Log($"[MyNetworkManager] ========================================");
            Debug.Log($"[MyNetworkManager] 服务器启动");
            Debug.Log($"[MyNetworkManager]   端口: {CurrentPort}");
            Debug.Log($"[MyNetworkManager]   最大玩家: {maxPlayers}");
            Debug.Log($"[MyNetworkManager]   等待玩家连接...");
            Debug.Log($"[MyNetworkManager] ========================================");
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            _hostStarted = false;

            if (discovery != null)
            {
                discovery.StopBroadcast();
                discovery.StopDiscovering();
            }

            Debug.Log("[MyNetworkManager] 服务器停止");
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            if (discovery != null)
            {
                discovery.StartDiscovering();
            }

            Debug.Log("[MyNetworkManager] 客户端启动，正在连接...");
            Debug.Log($"[MyNetworkManager]   目标地址: {networkAddress}:{GetCurrentPort()}");
        }

        public override void OnStopClient()
        {
            base.OnStopClient();

            if (discovery != null)
            {
                discovery.StopDiscovering();
            }

            Debug.Log("[MyNetworkManager] 客户端停止");
        }

        public override void OnServerConnect(NetworkConnectionToClient conn)
        {
            base.OnServerConnect(conn);

            if (numPlayers >= maxPlayers)
            {
                conn.Disconnect();
                Debug.Log($"[MyNetworkManager] 连接被拒绝：房间已满 ({numPlayers}/{maxPlayers})");
                return;
            }

            Debug.Log($"[MyNetworkManager] 新连接 connectionId={conn.connectionId}, 地址={conn.address}, 当前在线={numPlayers}/{maxPlayers}");

            _playerManager?.HandleServerConnect(conn);
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            Debug.Log($"[MyNetworkManager] 玩家断开 connectionId={conn.connectionId}");
            _playerManager?.HandleServerDisconnect(conn);
            base.OnServerDisconnect(conn);
        }

        public override void OnClientConnect()
        {
            base.OnClientConnect();

            // 提前注册 NetworkEventMessage handler，防止服务器在 OnStartClient 之前发消息
            NetworkClient.ReplaceHandler<NetworkEventMessage>(OnClientReceiveNetworkEvent);

            Debug.Log($"[MyNetworkManager] ========================================");
            Debug.Log($"[MyNetworkManager] 已成功连接到服务器");
            Debug.Log($"[MyNetworkManager]   我的角色: {(NetworkServer.active ? "房主" : "客户端")}");

            if (NetworkServer.active)
            {
                Debug.Log($"[MyNetworkManager]   我的ID: {_playerManager?.SelfPlayerID}");
            }
            else
            {
                Debug.Log($"[MyNetworkManager]   我的ID: 等待服务器分配... (当前: {_playerManager?.SelfPlayerID})");
            }

            Debug.Log($"[MyNetworkManager] ========================================");

            _isKicked = false;
            _isIntentionalDisconnect = false;

            // 客户端连接后发送自己的名称给服务器
            if (!NetworkServer.active)
            {
                string myName = Wargame.Instance?.SaveManager?.CurrentSave?.playerName ?? "旅行者";
                EventBusHub.Instance.Send(new SetPlayerNameRequestEvent { PlayerName = myName });
            }

            OnClientConnectedEvent?.Invoke();
        }

        public override void OnClientDisconnect()
        {
            Debug.Log($"[MyNetworkManager] ========================================");
            Debug.Log($"[MyNetworkManager] 与服务器断开连接");
            Debug.Log($"[MyNetworkManager]   主动断开: {_isIntentionalDisconnect}");
            Debug.Log($"[MyNetworkManager]   被踢: {_isKicked}");
            Debug.Log($"[MyNetworkManager]   我的ID: {_playerManager?.SelfPlayerID ?? "未设置"}");
            Debug.Log($"[MyNetworkManager] ========================================");

            _playerManager?.ClearPlayers();
            _playerManager?.Cleanup();
            OnClientDisconnectedEvent?.Invoke();

            _hostStarted = false;
            _isKicked = false;
            _isIntentionalDisconnect = false;

            base.OnClientDisconnect();

            Debug.Log("[MyNetworkManager] 已断开连接，回到未连接状态");
        }

        public int OnlinePlayers => NetworkServer.connections.Count;

        private void OnClientReceiveNetworkEvent(NetworkEventMessage msg)
        {
            var eventData = NetworkEventRegistry.Unpack(msg);
            if (eventData != null)
                EventBusHub.Instance?.ReceiveNetworkEvent(eventData);
        }
    }
}


