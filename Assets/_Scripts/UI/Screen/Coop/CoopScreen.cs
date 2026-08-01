// CoopScreen.cs - 联机界面
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
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


    public class CoopScreen : MonoBehaviour
    {
        [Header("房间详情")]
        public GameObject roomPanel;
        public TextMeshProUGUI roomTitleText;
        public TextMeshProUGUI roomIPText;
        public TextMeshProUGUI roomPortText;
        public TextMeshProUGUI roomBoardText;
        public Transform playerListContent;
        public GameObject playerRowPrefab;
        public Button leaveRoomButton;
        public Button startButton;
        public Button createRoomButton;

        [Header("房间列表")]
        public GameObject serverListPanel;
        public GameObject serverButtonPrefab;
        public Transform serverListContent;
        public TextMeshProUGUI emptyRoomHint;

        [Header("地图选择")]
        public TMP_Dropdown boardDropdown;

        private CoopNetworkController _network;
        private PlayerManager _playerManager;

        private enum RoomState { DisconnectedClient, Host, ConnectedClient }
        private RoomState _currentState = RoomState.DisconnectedClient;

        void Awake()
        {
            var netMgr = FindObjectOfType<MyNetworkManager>();
            var discovery = FindObjectOfType<MyNetworkDiscovery>();
            _playerManager = Wargame.Instance.PlayerManager;
            _network = new CoopNetworkController(netMgr, discovery, _playerManager);

            ClearPlayerList();
            ClearServerList();
            if (emptyRoomHint) emptyRoomHint.text = "世界树搜索中……";
        }

        void Start()
        {
            StopCurrentConnection();
            AudioManager.Instance.PushMusicVolume();

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
            if (_network.IsTimedOut)
                StopDiscoveryAndUpdateUI();
        }

        void OnDestroy()
        {
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
            var discovery = FindObjectOfType<MyNetworkDiscovery>();
            if (discovery) discovery.OnServerFound.AddListener(OnServerFound);
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
            var netMgr = FindObjectOfType<MyNetworkManager>();
            if (netMgr == null) return;
            netMgr.OnClientConnectedEvent += OnClientConnected;
            netMgr.OnClientDisconnectedEvent += OnClientDisconnected;
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
            var netMgr = FindObjectOfType<MyNetworkManager>();
            if (netMgr == null) return;
            netMgr.OnClientConnectedEvent -= OnClientConnected;
            netMgr.OnClientDisconnectedEvent -= OnClientDisconnected;
        }

        private void UnbindDiscoveryEvents()
        {
            var discovery = FindObjectOfType<MyNetworkDiscovery>();
            if (discovery == null) return;
            discovery.OnServerFound.RemoveListener(OnServerFound);
            discovery.StopBroadcast();
            discovery.StopDiscovering();
        }

        #endregion

        #region 连接管理

        private void StopCurrentConnection()
        {
            if (NetworkServer.active || NetworkClient.isConnected)
                FindObjectOfType<MyNetworkManager>().StopHost();
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
            if (leaveRoomButton)
                leaveRoomButton.GetComponentInChildren<TextMeshProUGUI>().text =
                    isDisconnected ? "返回" : "离开房间";
            if (createRoomButton) createRoomButton.gameObject.SetActive(isDisconnected);

            if (roomTitleText)
                roomTitleText.text = isHost ? "我的房间" : "对方房间";

            if (emptyRoomHint)
                emptyRoomHint.gameObject.SetActive(isDisconnected);

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
            Invoke(nameof(WaitForHostStart), 0.5f);
        }

        void WaitForHostStart()
        {
            if (NetworkServer.active)
            {
                SetRoomState(RoomState.Host);
                _network.StartBroadcast();
            }
            else Invoke(nameof(WaitForHostStart), 0.5f);
        }

        void OnStartClick()
        {
            if (_currentState != RoomState.Host) return;

            foreach (var player in _playerManager.GetAllPlayers())
                if (!player.IsReady) return;
        }

        void OnLeaveRoomClick()
        {
            switch (_currentState)
            {
                case RoomState.DisconnectedClient:
                    AudioManager.Instance.PopMusicVolume();
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
            if (roomIPText) roomIPText.text = $"IP: {_network.GetLocalIP()}";
            if (roomPortText) roomPortText.text = $"端口: {_network.GetCurrentPort()}";
            if (roomBoardText && boardDropdown && boardDropdown.options.Count > boardDropdown.value)
                roomBoardText.text = $"地图: {boardDropdown.options[boardDropdown.value].text}";
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
            var text = buttonObj.GetComponentInChildren<TextMeshProUGUI>();

            string ip = response.EndPoint.Address.ToString();
            int port = response.uri.Port;
            if (text) text.text = $"房间 ({ip}:{port})";
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
            if (!emptyRoomHint || _currentState != RoomState.DisconnectedClient) return;

            if (!_network.FoundServers.Any())
            {
                emptyRoomHint.text = "世界树未发现其它降临者";
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
            boardDropdown.AddOptions(new List<string> { "棋盘1", "棋盘2", "棋盘3" });
        }

        #endregion
    }
}



