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

        public bool IsHost => _hostStarted && NetworkServer.active;
        public int CurrentPort { get; private set; }

        [Autowired] private PlayerManager _playerManager;

        public override void Awake()
        {
            gameObject.SetActive(true);
            base.Awake();
            gameObject.SetActive(true);

            _kcpTransport = transport as KcpTransport;
            if (_kcpTransport == null)
            {
                GICLog.Error("[MyNetworkManager] 传输组件不是 KcpTransport，请检查 NetworkManager 配置");
            }

            if (discovery == null)
                discovery = GetComponent<MyNetworkDiscovery>();

            Wargame.Instance.Context.Inject(this);
            _playerManager?.SetNetworkManager(this);

            GICLog.Info("[MyNetworkManager] 初始化完成");
        }

        public override void Start()
        {
            base.Start();

            if (_playerManager == null)
            {
                GICLog.Error("[MyNetworkManager] PlayerManager 不存在！");
            }

            GICLog.Info("[MyNetworkManager] 启动完成，等待用户操作");
        }

        // ==================== 公共方法 ====================

        public void StartHostMode()
        {
            if (NetworkServer.active || NetworkClient.isConnected)
            {
                GICLog.Info("[MyNetworkManager] 检测到已有连接，先停止");
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
                    GICLog.Info($"[MyNetworkManager] 主机模式启动成功，端口: {port}");
                }
                else
                {
                    port++;
                }
            }

            if (!_hostStarted)
            {
                GICLog.Error($"[MyNetworkManager] 无法启动主机模式，所有端口被占用");
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
                GICLog.Warn($"[MyNetworkManager] 端口 {port} 启动失败: {ex.Message}");
                return false;
            }
        }

        public void JoinRoom(string ip, int port)
        {
            if (_hostStarted && NetworkServer.active)
            {
                GICLog.Info("[MyNetworkManager] 先停止主机模式再加入其他房间");
                StopHost();
                _hostStarted = false;
            }

            if (NetworkClient.isConnected)
            {
                GICLog.Info("[MyNetworkManager] 先断开当前客户端连接");
                NetworkClient.Disconnect();
            }

            networkAddress = ip;
            SetPort(port);
            StartClient();
            GICLog.Info($"[MyNetworkManager] 正在加入房间: {ip}:{port}");
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

            GICLog.Info($"[MyNetworkManager] ========================================");
            GICLog.Info($"[MyNetworkManager] 服务器启动");
            GICLog.Info($"[MyNetworkManager]   端口: {CurrentPort}");
            GICLog.Info($"[MyNetworkManager]   最大玩家: {maxPlayers}");
            GICLog.Info($"[MyNetworkManager]   等待玩家连接...");
            GICLog.Info($"[MyNetworkManager] ========================================");
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

            GICLog.Info("[MyNetworkManager] 服务器停止");
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            if (discovery != null)
            {
                discovery.StartDiscovering();
            }

            GICLog.Info("[MyNetworkManager] 客户端启动，正在连接...");
            GICLog.Info($"[MyNetworkManager]   目标地址: {networkAddress}:{GetCurrentPort()}");
        }

        public override void OnStopClient()
        {
            base.OnStopClient();

            if (discovery != null)
            {
                discovery.StopDiscovering();
            }

            GICLog.Info("[MyNetworkManager] 客户端停止");
        }

        public override void OnServerConnect(NetworkConnectionToClient conn)
        {
            base.OnServerConnect(conn);

            if (numPlayers >= maxPlayers)
            {
                conn.Disconnect();
                GICLog.Info($"[MyNetworkManager] 连接被拒绝：房间已满 ({numPlayers}/{maxPlayers})");
                return;
            }

            GICLog.Info($"[MyNetworkManager] 新连接 connectionId={conn.connectionId}, 地址={conn.address}, 当前在线={numPlayers}/{maxPlayers}");

            _playerManager?.HandleServerConnect(conn);
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            GICLog.Info($"[MyNetworkManager] 玩家断开 connectionId={conn.connectionId}");
            _playerManager?.HandleServerDisconnect(conn);
            base.OnServerDisconnect(conn);
        }

        public override void OnClientConnect()
        {
            base.OnClientConnect();

            // 提前注册 NetworkEventMessage handler，防止服务器在 OnStartClient 之前发消息
            NetworkClient.ReplaceHandler<NetworkEventMessage>(OnClientReceiveNetworkEvent);

            GICLog.Info($"[MyNetworkManager] ========================================");
            GICLog.Info($"[MyNetworkManager] 已成功连接到服务器");
            GICLog.Info($"[MyNetworkManager]   我的角色: {(NetworkServer.active ? "房主" : "客户端")}");

            if (NetworkServer.active)
            {
                GICLog.Info($"[MyNetworkManager]   我的ID: {_playerManager?.SelfPlayerID}");
            }
            else
            {
                GICLog.Info($"[MyNetworkManager]   我的ID: 等待服务器分配... (当前: {_playerManager?.SelfPlayerID})");
            }

            GICLog.Info($"[MyNetworkManager] ========================================");

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
            GICLog.Info($"[MyNetworkManager] ========================================");
            GICLog.Info($"[MyNetworkManager] 与服务器断开连接");
            GICLog.Info($"[MyNetworkManager]   主动断开: {_isIntentionalDisconnect}");
            GICLog.Info($"[MyNetworkManager]   被踢: {_isKicked}");
            GICLog.Info($"[MyNetworkManager]   我的ID: {_playerManager?.SelfPlayerID ?? "未设置"}");
            GICLog.Info($"[MyNetworkManager] ========================================");

            _playerManager?.ClearPlayers();
            _playerManager?.Cleanup();
            OnClientDisconnectedEvent?.Invoke();

            _hostStarted = false;
            _isKicked = false;
            _isIntentionalDisconnect = false;

            base.OnClientDisconnect();

            GICLog.Info("[MyNetworkManager] 已断开连接，回到未连接状态");
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


