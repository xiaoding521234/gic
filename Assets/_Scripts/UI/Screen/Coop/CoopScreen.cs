// CoopScreen.cs - 联机界面
using System.Collections.Generic;
using System.Linq;
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


    public class CoopScreen : MonoBehaviour, IClosable
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

        private enum RoomState { DisconnectedClient, Host, ConnectedClient }
        private RoomState _currentState = RoomState.DisconnectedClient;
        private bool _isClosing = false;

        // ── IClosable 实现 ──
        void IClosable.Close() => OnLeaveRoomClick();

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

        void Awake()
        {
            _netMgr = FindObjectOfType<MyNetworkManager>();
            _discovery = FindObjectOfType<MyNetworkDiscovery>();
            Wargame.Instance.Context.Inject(this);
            _network = new CoopNetworkController(_netMgr, _discovery, _playerManager);

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
            AudioManager.Instance.PushMusicVolume();

            Wargame.Instance?.InputManager?.RegisterClosable(this);

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

        void OnDestroy()
        {
            Wargame.Instance?.InputManager?.UnregisterClosable(this);
            // 兜底：释放本类持有的全部输入锁
            InputLocks.PopAll(this);
            UnbindPlayerEvents();
            UnbindNetworkEvents();
            UnbindDiscoveryEvents();
            CancelInvoke();
        }

        #region 按钮绑定

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
            if (_playerManager == null) return;
            _playerManager.OnPlayerCountChanged += OnPlayerCountChanged;
            _playerManager.OnPlayerInfoUpdated += OnPlayerInfoUpdated;
            _playerManager.OnKickedFromRoom += OnKickedFromRoom;
        }

        private void BindNetworkEvents()
        {
            if (_netMgr == null) return;
            _netMgr.OnClientConnectedEvent += OnClientConnected;
            _netMgr.OnClientDisconnectedEvent += OnClientDisconnected;
        }

        private void UnbindPlayerEvents()
        {
            if (_playerManager == null) return;
            _playerManager.OnPlayerCountChanged -= OnPlayerCountChanged;
            _playerManager.OnPlayerInfoUpdated -= OnPlayerInfoUpdated;
            _playerManager.OnKickedFromRoom -= OnKickedFromRoom;
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

        #region 连接管理

        private void StopCurrentConnection()
        {
            if (NetworkServer.active || NetworkClient.isConnected)
                _netMgr?.StopHost();
        }

        private void SetRoomState(RoomState newState)
        {
            _currentState = newState;
            RefreshUI();
        }

        private void RefreshUI()
        {
            bool isDisconnected = _currentState == RoomState.DisconnectedClient;
            bool isHost = _currentState == RoomState.Host;

            serverListPanel.SetActive(isDisconnected);
            roomPanel.SetActive(!isDisconnected);

            if (startButton) startButton.gameObject.SetActive(isHost);
            if (leaveRoomButton && _leaveRoomText != null)
            {
                if (isDisconnected)
                    _leaveRoomText.SetSingleEntry(new LocalizedString("UIText", "Back"));
                else
                    _leaveRoomText.SetSingleEntry(new LocalizedString("UIText", "LeaveRoom"));
            }
            if (createRoomButton) createRoomButton.gameObject.SetActive(isDisconnected);

            if (_roomTitle != null)
            {
                _roomTitle.SetSingleEntry(isHost
                    ? new LocalizedString("UIText", "MyRoom")
                    : new LocalizedString("UIText", "OpponentRoom"));
            }

            if (emptyRoomHintObj)
                emptyRoomHintObj.gameObject.SetActive(isDisconnected);

            if (!isDisconnected)
            {
                UpdateRoomPanel();
                RefreshPlayerList();
            }
        }

        #endregion

        #region 按钮事件

        void OnCreateRoomClick()
        {
            if (_currentState == RoomState.Host) return;

            _network.StartHost();
            _hostStartRetries = 0;
            Invoke(nameof(WaitForHostStart), 0.5f);
        }

        private int _hostStartRetries;
        private const int MAX_HOST_START_RETRIES = 10;

        void WaitForHostStart()
        {
            if (NetworkServer.active)
            {
                SetRoomState(RoomState.Host);
                _network.StartBroadcast();
            }
            else if (_hostStartRetries < MAX_HOST_START_RETRIES)
            {
                _hostStartRetries++;
                Invoke(nameof(WaitForHostStart), 0.5f);
            }
            else
            {
                Debug.LogError("[CoopScreen] 主机启动超时，请检查端口是否被占用");
                GameScene.Instance?.ShowToast("主机启动超时，请检查端口是否被占用");
            }
        }

        void OnStartClick()
        {
            if (_currentState != RoomState.Host) return;

            foreach (var player in _playerManager.GetAllPlayers())
                if (!player.IsReady) return;
        }

        void OnLeaveRoomClick()
        {
            if (_isClosing) return;

            switch (_currentState)
            {
                case RoomState.DisconnectedClient:
                    _isClosing = true;
                    AudioManager.Instance.PopMusicVolume();
                    // 无退场动画 — 转场期间输入由 GoBackCoroutine 的 SceneTransition 锁封锁
                    GameScene.Instance.GoBack();
                    break;
                case RoomState.Host:
                    _network.LeaveRoom();
                    ReturnToDiscovery();
                    break;
                case RoomState.ConnectedClient:
                    _network.DisconnectClient();
                    ReturnToDiscovery();
                    break;
            }
        }

        #endregion

        #region 网络回调

        void OnClientConnected()
        {
            if (!NetworkServer.active)
            {
                SetRoomState(RoomState.ConnectedClient);
                _network.StopDiscovery();
            }
        }

        void OnClientDisconnected()
        {
            if (_currentState != RoomState.DisconnectedClient)
                ReturnToDiscovery();
        }

        void OnKickedFromRoom()
        {
            _network.LeaveRoom();
            ReturnToDiscovery();
        }

        private void ReturnToDiscovery()
        {
            bool wasAlreadyDisconnected = _currentState == RoomState.DisconnectedClient;
            SetRoomState(RoomState.DisconnectedClient);
            ClearPlayerList();
            // 防止重复调用（StopHost 内部回调与 OnLeaveRoomClick 都会触发）
            if (!wasAlreadyDisconnected)
                Invoke(nameof(InitializeDiscovery), 0.5f);
        }

        #endregion

        #region PlayerManager 事件

        void OnPlayerCountChanged(int count) { if (_currentState != RoomState.DisconnectedClient) RefreshPlayerList(); }

        void OnPlayerInfoUpdated(string playerID, PlayerInfo updatedInfo)
        {
            if (_currentState == RoomState.DisconnectedClient) return;
            for (int i = 0; i < playerListContent.childCount; i++)
            {
                var rowUI = playerListContent.GetChild(i).GetComponent<PlayerRowView>();
                if (rowUI != null && rowUI.PlayerID == playerID)
                { rowUI.RefreshFromPlayerInfo(updatedInfo); return; }
            }
            RefreshPlayerList();
        }

        #endregion

        #region UI 更新

        void UpdateRoomPanel()
        {
            if (_roomIP != null)
            {
                _roomIP.ClearAllEntries();
                _roomIP.AddStaticEntry($"IP: {_network.GetLocalIP()}");
            }

            if (_roomPort != null)
            {
                _roomPort.ClearAllEntries();
                _roomPort.AddEntry(new LocalizedString("UIText", "Port"));
                _roomPort.AddStaticEntry($": {_network.GetCurrentPort()}");
            }

            if (_roomBoard != null && boardDropdown && boardDropdown.options.Count > boardDropdown.value)
            {
                _roomBoard.ClearAllEntries();
                _roomBoard.AddEntry(new LocalizedString("UIText", "MapLabel"));
                _roomBoard.AddStaticEntry($": {boardDropdown.options[boardDropdown.value].text}");
            }
        }

        void RefreshPlayerList()
        {
            ClearPlayerList();
            if (_playerManager == null) return;
            foreach (var player in _playerManager.GetAllPlayers())
            {
                var row = Instantiate(playerRowPrefab, playerListContent);
                var rowUI = row.GetComponent<PlayerRowView>();
                if (rowUI) rowUI.Setup(player);
            }
        }

        void ClearPlayerList()
        {
            for (int i = 0; i < playerListContent.childCount; i++)
                Destroy(playerListContent.GetChild(i).gameObject);
        }

        #endregion

        #region 房间发现

        void InitializeDiscovery()
        {
            if (_currentState == RoomState.DisconnectedClient)
                _network.StartDiscovery();

            UpdateEmptyRoomHint();
            CancelInvoke(nameof(AutoRefreshServers));
            InvokeRepeating(nameof(AutoRefreshServers), 5f, 5f);
        }

        void OnServerFound(ServerResponse response)
        {
            if (_currentState != RoomState.DisconnectedClient) return;

            string ip = response.EndPoint.Address.ToString();
            int port = response.uri.Port;
            string key = $"{ip}:{port}";
            if (_network.FoundServers.ContainsKey(key)) return;

            _network.HandleServerFound(response);
            AddServerButton(response);
            UpdateEmptyRoomHint();
        }

        void AddServerButton(ServerResponse response)
        {
            var buttonObj = Instantiate(serverButtonPrefab, serverListContent);
            var button = buttonObj.GetComponent<Button>();
            var tmp = buttonObj.GetComponentInChildren<TextMeshProUGUI>();

            string ip = response.EndPoint.Address.ToString();
            int port = response.uri.Port;

            // 从 MyNetworkDiscovery.DiscoveredRooms 获取房间显示信息
            string hostName;
            int currentPlayers, maxPlayers;
            bool hasInfo = MyNetworkDiscovery.DiscoveredRooms.TryGetValue(response.serverId, out var info);
            if (hasInfo)
            {
                hostName = info.HostName;
                currentPlayers = info.CurrentPlayers;
                maxPlayers = info.MaxPlayers;
            }
            else
            {
                hostName = $"{ip}:{port}";
                currentPlayers = 0;
                maxPlayers = 0;
            }

            if (tmp != null)
            {
                var tc = tmp.GetComponent<TextCombiner>();
                if (tc == null) tc = tmp.gameObject.AddComponent<TextCombiner>();
                tc.ClearAllEntries();
                tc.AddStaticEntry(hostName);

                if (hasInfo)
                {
                    tc.AddEntry(new LocalizedString("UIText", "Room"), " ");
                    tc.AddStaticEntry($"  ({currentPlayers}/{maxPlayers})");
                }
                else
                {
                    tc.AddEntry(new LocalizedString("UIText", "Room"), " (");
                    tc.AddStaticEntry($"{ip}:{port})");
                }
            }

            if (button) button.onClick.AddListener(() => JoinServer(ip, port));
        }

        void JoinServer(string ip, int port) => _network.JoinRoom(ip, port);

        void AutoRefreshServers()
        {
            if (_currentState == RoomState.DisconnectedClient)
                RefreshServers();
        }

        void RefreshServers()
        {
            _network.ClearServers();
            ClearServerList();
            MyNetworkDiscovery.DiscoveredRooms.Clear();
            _network.StartDiscovery();
            UpdateEmptyRoomHint();
        }

        void StopDiscoveryAndUpdateUI()
        {
            _network.StopDiscovery();
            UpdateEmptyRoomHint();
        }

        void UpdateEmptyRoomHint()
        {
            if (!emptyRoomHintObj || _currentState != RoomState.DisconnectedClient) return;

            if (!_network.FoundServers.Any())
            {
                _emptyRoomHint?.SetSingleEntry(new LocalizedString("UIText", "NoServersFound"));
            }
        }

        void ClearServerList()
        {
            for (int i = 0; i < serverListContent.childCount; i++)
                Destroy(serverListContent.GetChild(i).gameObject);
        }

        #endregion

        #region 地图选择

        void InitBoardDropdown()
        {
            if (boardDropdown == null) return;
            boardDropdown.onValueChanged.AddListener(_ => UpdateRoomPanel());
            boardDropdown.ClearOptions();
            var options = new List<string>
            {
                new LocalizedString("UIText", "Board1").GetLocalizedString(),
                new LocalizedString("UIText", "Board2").GetLocalizedString(),
                new LocalizedString("UIText", "Board3").GetLocalizedString()
            };
            boardDropdown.AddOptions(options);
        }

        #endregion
    }
}
