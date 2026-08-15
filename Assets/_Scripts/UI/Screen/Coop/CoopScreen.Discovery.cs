// ==================== CoopScreen.Discovery.cs（房间发现 + 服务器列表 + 地图选择下拉） ====================
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;
using TMPro;
using Mirror.Discovery;
using GIC.Framework;
using GIC.Battle;
using GIC.Data;
using GIC.Data.Event;
using GIC.Tool;
namespace GIC.UI
{


    public partial class CoopScreen
    {
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
