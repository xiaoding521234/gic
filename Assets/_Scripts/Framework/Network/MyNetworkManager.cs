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

#if UNITY_EDITOR
            // 2026-10-15 LAN 第十二批（终极根因）：Mirror 构建期 [PostProcessScene] 会把「场景路径哈希」
            // 打进场景物体 sceneId 高 32 位再烤进包（实测 gic01a APK 内 NEB sceneId=0xB97EAECEC3AF1A49）；
            // 本机 Tuanjie 编辑器进 Play 不执行该步（探针实锤 live 高位恒 0）——编辑器 host 的 spawn 消息
            // 携带无哈希 sceneId，构建客户端按「带哈希」键索引 → 永远 Spawn scene object not found →
            // NEB 失活 → 客户端→服务器事件全 NRE（上游同型问题=Mirror issue #2519：编辑器↔独立端互连必炸）。
            // 编辑器侧 Boot 加载时补打同一哈希（同函数同路径输入=确定一致、幂等）；构建内已烤好正确值
            // 勿动（#if 隔离）。必须在 base.Awake() 之前——DDOL 会把子物体移出 Boot 场景、scene.path 失效。
            foreach (var ni in FindObjectsOfType<Mirror.NetworkIdentity>(true))
                if (Mirror.Utils.IsSceneObject(ni))
                {
                    ni.SetSceneIdSceneHashPartInternal();
                    GICLog.Info($"[MyNetworkManager] 编辑器场景哈希补打: {ni.name} sceneId=0x{ni.sceneId:X}");
                }
#endif

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

        // 2026-10-15 LAN 第十四批终案：本 override（就绪即 RebuildObservers 补发）整体移除。
        // ①占位玩家（第十三批）已让 Mirror 原生 replay 生效：AddPlayerForConnection→SetClientReady
        //   →SpawnObserversForConnection 以「ObjectSpawnStarted→逐物体 spawn→ObjectSpawnFinished」
        //   正确时序补发（ObjectSpawnStarted 先行触发客户端 PrepareToSpawnSceneObjects 建字典）。
        // ②本 override 的 RebuildObservers→AddObserver→ShowForConnection 会**抢先**直发场景物体
        //   （不带 ObjectSpawnStarted 前置包）——客户端此刻字典未建立→Spawn scene object not found；
        //   且 AddObserver 已把连接抢注进 observers→原生 replay 循环到该物体时幂等 return 不再重发
        //   →物体的 spawn 消息只发过一次（死在字典建立前）→NEB 永不激活。两批修复互相踩踏的实证：
        //   01:22 会话 [SceneCensus] spawnableObjects.Count=0 + NEB 全部过滤条件通过却不在字典。

        /// <summary>占位玩家对象（2026-10-15 LAN 第十三批）：本作无玩家棋子，但 Mirror 的标准生命周期
        /// （SetClientReady 的 identity 门 → AddPlayerForConnection → 原生场景物体 replay）全部围绕
        /// 「连接有玩家对象」设计（上游 issue #4083 佐证）——autoCreatePlayer=1 + 空 NetworkPlayer 占位
        /// prefab（无渲染无逻辑不可见）让全链走 Mirror 原生流程，结构性绕开无玩家对象缺口。
        /// 占位对象 DDOL：房间→战斗场景切换时不被卸载，避免 conn.identity 悬空。</summary>
        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            base.OnServerAddPlayer(conn);
            if (conn.identity != null)
                DontDestroyOnLoad(conn.identity.gameObject);
        }

        public override void OnClientConnect()
        {
            // 2026-10-15 LAN 第十一批勘正（真根因）：无玩家客户端的就绪上报其实由 base.OnClientConnect
            // 完成——本作无 onlineScene → clientLoadedScene=false → base 自调 NetworkClient.Ready()。
            // 但 Mirror 的 static NetworkClient.ready 跨会话残留不复位（同进程当过 host 后再 join、
            // 断线重连均不清零——实测都会让 base 跳过上报 → 服务器 isReady 永假 → OnServerReady 补发
            // 也进不去），故 base 前强制清零；原先 base 之后的显式 Ready() 是重复调用（报 already ready），移除。
            if (!NetworkServer.active && NetworkClient.ready)
                NetworkClient.ready = false;

            base.OnClientConnect();

            LanDebugFileLog.WriteLine($"[OnClientConnect] 触发！服务器={NetworkServer.active} ready={NetworkClient.ready}");

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

            OnClientConnectedEvent?.Invoke();
        }

        /// <summary>
        /// 客户端场景网络物体激活完成（NetworkEventBus.OnStartClient 回调）——
        /// 此时 netIdentity 已就绪，是客户端首发网络事件的最早安全点（名字上报）。
        /// </summary>
        public void OnNetworkSceneObjectsReady()
        {
            if (NetworkServer.active) return; // host 自己就是服务器，无需上报

            LanDebugFileLog.WriteLine("[OnNetworkSceneObjectsReady] 场景网络物体已激活，上报玩家名");
            string myName = _saveManager?.CurrentSave?.playerName ?? "旅行者";
            EventBusHub.Instance.Send(new SetPlayerNameRequestEvent { PlayerName = myName });
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


