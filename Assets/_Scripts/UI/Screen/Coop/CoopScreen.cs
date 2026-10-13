// CoopScreen.cs - 联机界面（字段声明 + 生命周期 + 事件绑定）
// partial 拆分：主文件（本文件）/ RoomFlow（状态机+房间流程）/ PlayerList（玩家行刷新）/ Discovery（房间发现）
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Serialization;
using UnityEngine.UI;
using TMPro;
using Mirror;
using Mirror.Discovery;
using GIC.Framework;
using GIC.Data;
using GIC.Tool;
namespace GIC.UI
{


    public partial class CoopScreen : ScreenBase
    {
        [Header("房间详情")]
        public GameObject roomPanel;
        [FormerlySerializedAs("roomTitleText")] public TextMeshProUGUI roomTitleObj;
        [FormerlySerializedAs("roomIPText")] public TextMeshProUGUI roomIPObj;
        [FormerlySerializedAs("roomPortText")] public TextMeshProUGUI roomPortObj;
        [FormerlySerializedAs("roomBoardText")] public TextMeshProUGUI roomBoardObj;
        public Transform playerListContent;
        public GameObject playerRowPrefab;
        public Button leaveRoomButton;
        public Button startButton;
        public Button createRoomButton;

        [Header("房间正式化（2026-10-11 联机批）")]
        [Tooltip("地图预览（房间右上角，RA2 式地形小图+出生点标记）")]
        public BattleMapPreviewUI mapPreview;
        [Tooltip("增加AI玩家按钮（仅 host 可见；点击登记一名 AI 玩家）")]
        public Button addAIPlayerButton;

        [Header("房间列表")]
        public GameObject serverListPanel;
        public GameObject serverButtonPrefab;
        public Transform serverListContent;
        [FormerlySerializedAs("emptyRoomHint")] public TextMeshProUGUI emptyRoomHintObj;

        [Header("地图选择")]
        public TMP_Dropdown boardDropdown;

        private CoopNetworkController _network;
        [Autowired] private PlayerManager _playerManager;
        [Autowired] private RoomManager _roomManager;
        private MyNetworkManager _netMgr;
        private MyNetworkDiscovery _discovery;

        private TextCombiner _roomTitle;
        private TextCombiner _roomIP;
        private TextCombiner _roomPort;
        private TextCombiner _roomBoard;
        private TextCombiner _emptyRoomHint;
        private TextCombiner _leaveRoomText;
        private TextCombiner _createRoomText;
        private TextCombiner _startGameText;
        private TextCombiner _addAIText;

        // 开战广播接收（host 回环+客户端统一入口；2026-10-11 联机批）
        private StartBattleEventHandler _startBattleHandler;

        // ── IClosable 实现 ──
        public override void Close() => OnLeaveRoomClick();

        private void EnsureTextCombiners()
        {
            _roomTitle = EnsureTC(roomTitleObj);
            _roomIP = EnsureTC(roomIPObj);
            _roomPort = EnsureTC(roomPortObj);
            _roomBoard = EnsureTC(roomBoardObj);
            _emptyRoomHint = EnsureTC(emptyRoomHintObj);

            if (_leaveRoomText == null && leaveRoomButton != null)
            {
                var tmp = leaveRoomButton.GetComponentInChildren<TextMeshProUGUI>();
                if (tmp != null) _leaveRoomText = EnsureTC(tmp);
            }

            if (_createRoomText == null && createRoomButton != null)
            {
                var tmp = createRoomButton.GetComponentInChildren<TextMeshProUGUI>();
                if (tmp != null) _createRoomText = EnsureTC(tmp);
            }

            if (_startGameText == null && startButton != null)
            {
                var tmp = startButton.GetComponentInChildren<TextMeshProUGUI>();
                if (tmp != null) _startGameText = EnsureTC(tmp);
            }

            if (_addAIText == null && addAIPlayerButton != null)
            {
                var tmp = addAIPlayerButton.GetComponentInChildren<TextMeshProUGUI>();
                if (tmp != null) _addAIText = EnsureTC(tmp);
            }
        }

        private static TextCombiner EnsureTC(TextMeshProUGUI tmp)
        {
            if (tmp == null) return null;
            var tc = tmp.GetComponent<TextCombiner>();
            if (tc == null) tc = tmp.gameObject.AddComponent<TextCombiner>();
            return tc;
        }

        protected override void Awake()
        {
            base.Awake(); // 注入（Boot 链路容器已就绪）

            _netMgr = FindObjectOfType<MyNetworkManager>();
            _discovery = FindObjectOfType<MyNetworkDiscovery>();
            _network = new CoopNetworkController(_netMgr, _discovery);

            ClearPlayerList();
            ClearServerList();
            EnsureTextCombiners();
            _emptyRoomHint?.SetSingleEntry(new LocalizedString("UIText", "SearchingServers"));
            _createRoomText?.SetSingleEntry(new LocalizedString("UIText", "CreateRoom"));
            _startGameText?.SetSingleEntry(new LocalizedString("UIText", "StartGame"));
            _leaveRoomText?.SetSingleEntry(new LocalizedString("UIText", "Back"));
            _addAIText?.SetSingleEntry(new LocalizedString("UIText", "Coop_AddAIPlayer"));
            // 动画目标位缓存由面板上的 GlassPanelAnimator 在自身 Awake 自动完成（§39）
        }

        // prefab 面板：静态身份（场景名寻址在面板实例化进宿主场景后失效，P2 定则）
        protected override ScreenId Id => Screens.Coop;

        /// <summary>
        /// 一次性装配（池化生命周期，docs/14 §38）：按钮监听与下拉初始化只做一次——
        /// 池化面板不销毁，AddListener/InitBoardDropdown 重复执行必叠。
        /// C# 网络事件（玩家/房间/网络）按可见期语义放 OnShow 绑定、OnDisable 解绑。
        /// </summary>
        protected override void OnInit()
        {
            BindButtonEvents();
            InitBoardDropdown();
            _startBattleHandler = new StartBattleEventHandler(this);
        }

        /// <summary>
        /// 每次打开（池化生命周期）：断开残留连接 + 音乐 + 房间状态机复位（旧场景制靠重载天然复位）
        /// + 可见期事件绑定 + 发现流程启动 + 入场动画。OnDisable 对称解绑（隐藏期不吃联机事件）。
        /// </summary>
        protected override void OnShow(object args)
        {
            // 池化复位先行：状态直置必须早于 StopCurrentConnection——
            // 停服的断连回调据此判定为空闲态，不误触列表↔房间的切换动画（屏级入场动画才是本帧主角）；
            // 建房/加入等待态复位（标志+列表可见+按钮+中央文字）兜上次会话 CancelInvoke 中断流程的残留
            _currentState = RoomState.DisconnectedClient;
            _joining = false;
            _joinRetries = 0;
            SetCreatingUI(false);

            StopCurrentConnection();
            PushMusicVolumeSafe();

            SetRoomState(RoomState.DisconnectedClient); // 同态 → RefreshUI 即时刷新，无切换动画

            BindDiscoveryEvents();
            BindPlayerEvents();
            BindNetworkEvents();
            BindRoomEvents();

            Invoke(nameof(InitializeDiscovery), 0.5f);

            // 入场动画（docs/14 §37 ①）：起始态同帧设置防首帧闪现——公共组件驱动（锁/守卫在基类包装）
            PlayEnterAnimation();
        }

        /// <summary>
        /// 池化可见期解绑（对称 OnShow 的绑定；预热渲染态两帧未绑定过，解绑为安全空操作）。
        /// 隐藏期不接联机/房间事件——防不可见 UI 流（如被踢提示弹在大厅上）。
        /// </summary>
        protected override void OnDisable()
        {
            CancelInvoke();
            UnbindDiscoveryEvents();   // 停广播+停扫描（面板关闭路径均自 DisconnectedClient 态，即空闲态）
            UnbindPlayerEvents();
            UnbindNetworkEvents();
            UnbindRoomEvents();
            base.OnDisable();          // 出栈注销+可关闭注销+PopAll 锁保险丝
        }

        void Update()
        {
            if (_network != null && _network.IsTimedOut)
                StopDiscoveryAndUpdateUI();
        }

        protected override void OnDestroy()
        {
            UnbindPlayerEvents();
            UnbindNetworkEvents();
            UnbindRoomEvents();
            UnbindDiscoveryEvents();
            CancelInvoke();

            // 基类收尾：注销可关闭 + PopAll 输入锁 + 音乐 pop 兜底
            base.OnDestroy();
        }

        #region evtBind

        private void BindButtonEvents()
        {
            if (leaveRoomButton) leaveRoomButton.onClick.AddListener(OnLeaveRoomClick);
            if (startButton) startButton.onClick.AddListener(OnStartClick);
            if (createRoomButton) createRoomButton.onClick.AddListener(OnCreateRoomClick);
            if (addAIPlayerButton) addAIPlayerButton.onClick.AddListener(OnAddAIPlayerClick);
        }

        private void BindDiscoveryEvents()
        {
            if (_discovery) _discovery.OnServerFound.AddListener(OnServerFound);
        }

        private void BindPlayerEvents()
        {
            // 名册数据事件来自 PlayerManager；被踢事件来自 RoomManager
            if (_playerManager != null)
            {
                _playerManager.OnPlayerCountChanged += OnPlayerCountChanged;
                _playerManager.OnPlayerInfoUpdated += OnPlayerInfoUpdated;
            }
            if (_roomManager != null)
            {
                _roomManager.OnKickedFromRoom += OnKickedFromRoom;
            }
        }

        /// <summary>房间正式化事件（2026-10-11 联机批）：开战广播（host 回环+客户端统一转场入口）+
        /// 权威地图变化（下拉同步+预览刷新）——可见期订阅、OnDisable 对称解绑</summary>
        private void BindRoomEvents()
        {
            if (_startBattleHandler != null)
                EventBusHub.Instance.Subscribe<StartBattleEvent>(_startBattleHandler, this);
            if (_roomManager != null)
                _roomManager.OnRoomMapChanged += OnRoomMapChangedHandler;
        }

        private void UnbindRoomEvents()
        {
            if (_startBattleHandler != null)
                EventBusHub.Instance?.Unsubscribe<StartBattleEvent>(_startBattleHandler);
            if (_roomManager != null)
                _roomManager.OnRoomMapChanged -= OnRoomMapChangedHandler;
        }

        /// <summary>增加 AI 玩家按钮（仅 host 可见可点）：登记空连接 AI 玩家（服务器侧分配
        /// ID/颜色/队伍，恒就绪）；点击 AI 行名字=移除（Kick 链既有守卫）</summary>
        private void OnAddAIPlayerClick()
        {
            if (_currentState != RoomState.Host) return;
            EventBusHub.Instance.Send(new AddAIPlayerRequestEvent());
        }

        /// <summary>权威地图变化：同步下拉选中（无通知防回环）→ 房间信息/预览/玩家行（出生点
        /// 选项数随图变）全量刷新</summary>
        private void OnRoomMapChangedHandler(string mapConfigName)
        {
            if (boardDropdown != null && _battleMaps != null)
            {
                for (int i = 0; i < _battleMaps.Count; i++)
                {
                    if (_battleMaps[i].name == mapConfigName)
                    {
                        if (boardDropdown.value != i)
                            boardDropdown.SetValueWithoutNotify(i);
                        break;
                    }
                }
            }
            if (_currentState != RoomState.DisconnectedClient)
            {
                UpdateRoomPanel();
                RefreshRoomVisuals();
            }
        }

        private void BindNetworkEvents()
        {
            if (_netMgr == null) return;
            _netMgr.OnClientConnectedEvent += OnClientConnected;
            _netMgr.OnClientDisconnectedEvent += OnClientDisconnected;
        }

        private void UnbindPlayerEvents()
        {
            if (_playerManager != null)
            {
                _playerManager.OnPlayerCountChanged -= OnPlayerCountChanged;
                _playerManager.OnPlayerInfoUpdated -= OnPlayerInfoUpdated;
            }
            if (_roomManager != null)
            {
                _roomManager.OnKickedFromRoom -= OnKickedFromRoom;
            }
        }

        private void UnbindNetworkEvents()
        {
            if (_netMgr == null) return;
            _netMgr.OnClientConnectedEvent -= OnClientConnected;
            _netMgr.OnClientDisconnectedEvent -= OnClientDisconnected;
        }

        private void UnbindDiscoveryEvents()
        {
            if (_discovery == null) return;
            _discovery.OnServerFound.RemoveListener(OnServerFound);
            _discovery.StopBroadcast();
            _discovery.StopDiscovering();
        }

        #endregion

        // ==================== 嵌套事件处理器 ====================

        /// <summary>开战广播接收（host 回环+客户端到达双路径，Source==Network 统一成立）：
        /// 路由到 RoomFlow.OnStartBattleReceived——全端统一转场入口（2026-10-11 联机批）</summary>
        private class StartBattleEventHandler : IEventHandler<StartBattleEvent>
        {
            private readonly CoopScreen _screen;
            public StartBattleEventHandler(CoopScreen screen) => _screen = screen;
            public bool CanHandle(StartBattleEvent e) => e.Source == EventSource.Network;
            public void Handle(StartBattleEvent e) => _screen.OnStartBattleReceived(e);
        }
    }
}
