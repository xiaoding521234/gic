// MyNetworkManager.cs - 网络管理器（用 connectionId 标识玩家）
using UnityEngine;
using Mirror;
using System;
using GIC.Data;
using GIC.Pet;
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
        // 传输端口访问统一走 PortTransport 接口（Telepathy TCP / Kcp UDP 均实现该接口）——
        // 2026-10-13 LAN 批：局域网联机改用 Mirror 内建 Telepathy TCP 传输（Minecraft Java 同款哲学：
        // TCP 三次握手对状态型中间盒天然友好，根治「服务端回包被手机侧状态拦截」类单向丢包，docs/14 §151）
        private PortTransport _portTransport;

        // 自定义事件
        public event System.Action OnClientConnectedEvent;
        public event System.Action OnClientDisconnectedEvent;

        public bool IsHost => _hostStarted && NetworkServer.active;
        public int CurrentPort { get; private set; }

        [Autowired] private PlayerManager _playerManager;
        [Autowired] private RoomManager _roomManager;
        [Autowired] private SaveManager _saveManager;

        public override void Awake()
        {
            if (PetMode.Enabled) return; // 桌宠进程无 Wargame 上下文，跳过网络初始化（对象随场景切换销毁）

            gameObject.SetActive(true);
            base.Awake();
            gameObject.SetActive(true);

            _portTransport = transport as PortTransport;
            if (_portTransport == null)
            {
                GICLog.Error("[MyNetworkManager] 传输组件未实现 PortTransport 接口（Telepathy/Kcp 均实现），请检查 NetworkManager 配置");
            }

            if (discovery == null)
                discovery = GetComponent<MyNetworkDiscovery>();

            LanDebugFileLog.InstallGlobalHook(); // vivo Android 16 锁 logcat——全局异常落盘为唯一取证通道
            Wargame.Instance.Context.Inject(this);
            _roomManager?.SetNetworkManager(this);

            GICLog.Info("[MyNetworkManager] 初始化完成");
        }

        public override void Start()
        {
            base.Start();

            if (_roomManager == null)
            {
                GICLog.Error("[MyNetworkManager] RoomManager 不存在！");
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
            LanDebugFileLog.WriteLine($"[JoinRoom] StartClient → {ip}:{port}（传输={transport?.GetType().Name}）");
            StartClient();
            GICLog.Info($"[MyNetworkManager] 正在加入房间: {ip}:{port}");
        }

        public void MarkAsKicked()
        {
            _isKicked = true;
        }

        private void SetPort(int port)
        {
            if (_portTransport != null)
            {
                _portTransport.Port = (ushort)port;
            }
        }

        public int GetCurrentPort()
        {
            if (_portTransport != null)
            {
                return _portTransport.Port;
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

            // 2026-10-13 修复：StartHost 时本回调在 OnStartServer 之后触发（本地客户端），
            // StartDiscovering 内的 StopDiscovery 会把刚绑定的发现服务端套接字
            // 关掉（此前全靠 WaitForHostStart 0.5s 后二次 StartBroadcast 复活，链路脆弱：
            // 手机端实测房间对发现客户端完全不可见）。加入中的客户端也不该扫描——房间列表自己管理发现生命周期。
            LanDebugFileLog.WriteLine($"[OnStartClient] active={Transport.active?.GetType().Name} addr={networkAddress}:{GetCurrentPort()} isConnected={NetworkClient.isConnected}");
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

            _roomManager?.HandleServerConnect(conn);
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            GICLog.Info($"[MyNetworkManager] 玩家断开 connectionId={conn.connectionId}");
            _roomManager?.HandleServerDisconnect(conn);
            base.OnServerDisconnect(conn);
        }

        public override void OnClientConnect()
        {
            base.OnClientConnect();
            LanDebugFileLog.WriteLine($"[OnClientConnect] 触发！服务器={NetworkServer.active}");

            // 提前注册 NetworkEventMessage handler，防止服务器在 OnStartClient 之前发消息
            NetworkClient.ReplaceHandler<NetworkEventMessage>(OnClientReceiveNetworkEvent);

            GICLog.Info($"[MyNetworkManager] ========================================");
            GICLog.Info($"[MyNetworkManager] 已成功连接到服务器");
            GICLog.Info($"[MyNetworkManager]   我的角色: {(NetworkServer.active ? "房主" : "client")}");

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
                string myName = _saveManager?.CurrentSave?.playerName ?? "旅行者";
                EventBusHub.Instance.Send(new SetPlayerNameRequestEvent { PlayerName = myName });
            }

            OnClientConnectedEvent?.Invoke();
        }

        public override void OnClientDisconnect()
        {
            LanDebugFileLog.WriteLine($"[OnClientDisconnect] 触发！主动断开={_isIntentionalDisconnect} 被踢={_isKicked} 服务器={NetworkServer.active} 场景={GameScene.Instance?.CurrentScene}");
            GICLog.Info($"[MyNetworkManager] ========================================");
            GICLog.Info($"[MyNetworkManager] 与服务器断开连接");
            GICLog.Info($"[MyNetworkManager]   主动断开: {_isIntentionalDisconnect}");
            GICLog.Info($"[MyNetworkManager]   被踢: {_isKicked}");
            GICLog.Info($"[MyNetworkManager]   我的ID: {_playerManager?.SelfPlayerID ?? "未设置"}");
            GICLog.Info($"[MyNetworkManager] ========================================");

            // B7 LAN（2026-10-11 联机批）：战斗场景中与 Host 失联（Host 掉线/断网/Host 先退房）——
            // 对局失去权威端无以为继，客户端直接退回大厅（战斗音乐随场景卸载 OnBattleDestroy 停链；
            // 勿加载页遮盖——MainHall 无 Reveal 收尾，Cover 会黑屏 15s 等安全网）
            if (!NetworkServer.active
                && GameScene.Instance != null
                && GameScene.Instance.CurrentScene == SceneType.BattleScreen)
            {
                GICLog.Warn("[MyNetworkManager] 战斗中与 Host 失联，退回大厅");
                try
                {
                    GIC.UI.PopupManager.Instance.ShowToast(new UnityEngine.Localization.LocalizedString(
                        GIC.Data.TableName.PopupText.ToString(), "Coop_HostDisconnected"));
                }
                catch (Exception) { /* 弹层初始化异常不阻断退场 */ }
                GIC.Data.SceneType.MainHall.Load();
            }

            // 只清名册，不退订事件：Handler 常驻订阅（CanHandle 有 NetworkServer.active / Source==Network 守卫），
            // 若此处调 RoomManager.Cleanup() 会在首次断线后杀掉全部 Handler，同会话重连/再建房时名册不再同步
            _playerManager?.ClearPlayers();
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


