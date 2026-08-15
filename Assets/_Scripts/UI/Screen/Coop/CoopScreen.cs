// CoopScreen.cs - 联机界面（字段声明 + 生命周期 + 事件绑定）
// partial 拆分：主文件（本文件）/ RoomFlow（状态机+房间流程）/ PlayerList（玩家行刷新）/ Discovery（房间发现）
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Serialization;
using UnityEngine.UI;
using TMPro;
using Mirror;
using Mirror.Discovery;
using GIC.Framework;
using GIC.Battle;
using GIC.Data;
using GIC.Data.Event;
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
        }

        void Start()
        {
            StopCurrentConnection();
            PushMusicVolumeSafe();
            RegisterClosableSelf();

            BindButtonEvents();
            BindDiscoveryEvents();
            BindPlayerEvents();
            BindNetworkEvents();
            InitBoardDropdown();

            SetRoomState(RoomState.DisconnectedClient);
            Invoke(nameof(InitializeDiscovery), 0.5f);
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
            UnbindDiscoveryEvents();
            CancelInvoke();

            // 基类收尾：注销可关闭 + PopAll 输入锁 + 音乐 pop 兜底
            base.OnDestroy();
        }

        #region 事件绑定

        private void BindButtonEvents()
        {
            if (leaveRoomButton) leaveRoomButton.onClick.AddListener(OnLeaveRoomClick);
            if (startButton) startButton.onClick.AddListener(OnStartClick);
            if (createRoomButton) createRoomButton.onClick.AddListener(OnCreateRoomClick);
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
    }
}
