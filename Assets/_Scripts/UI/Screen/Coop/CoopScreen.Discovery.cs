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
using GIC.Tool;
namespace GIC.UI
{


    public partial class CoopScreen
    {
        #region roomDiscovery

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
            if (_startingHost) return; // 建房中：中央文字归 SetCreatingUI 持有（「正在创建房间…」），自动刷新勿覆盖

            // 居中提示仅空列表时显示（有房间时隐藏，2026-09-13 与建房中央反馈一并收口）
            bool listEmpty = !_network.FoundServers.Any();
            emptyRoomHintObj.gameObject.SetActive(listEmpty);
            if (listEmpty)
                _emptyRoomHint?.SetSingleEntry(new LocalizedString("UIText", "NoServersFound"));
        }

        void ClearServerList()
        {
            for (int i = 0; i < serverListContent.childCount; i++)
                Destroy(serverListContent.GetChild(i).gameObject);
        }

        #endregion

        #region 地图选择

        private List<BattleMapConfig> _battleMaps = new List<BattleMapConfig>();

        /// <summary>
        /// 地图下拉填充为真实可用的战场清单（Resources/Configs 下的 BattleMapConfig）。
        /// 2026-10-11 联机批：选图权威化——下拉仅 host 可操作，变更经 SetMapRequestEvent 上报服务器、
        /// 广播 UpdateRoomInfoEvent 全端同步（各端下拉只读镜像权威状态）；GetSelectedMapConfig 一律
        /// 按 RoomManager.RoomMapConfigName 解析（下拉本地索引仅为显示）。
        /// </summary>
        void InitBoardDropdown()
        {
            if (boardDropdown == null) return;
            // host：选图上报权威；客户端：无监听（下拉不可交互，仅同步显示）
            boardDropdown.onValueChanged.AddListener(index =>
            {
                if (_currentState != RoomState.Host || _roomManager == null) return;
                var map = MapAt(index);
                if (map != null)
                    EventBusHub.Instance.Send(new SetMapRequestEvent { MapConfigName = map.name });
                UpdateRoomPanel();
            });
            boardDropdown.ClearOptions();

            _battleMaps = new List<BattleMapConfig>(Resources.LoadAll<BattleMapConfig>("Configs"));
            var options = new List<string>();
            foreach (var map in _battleMaps)
                options.Add(string.IsNullOrEmpty(map.mapName) ? map.name : map.mapName);

            if (options.Count == 0)
            {
                // 兜底：无地图资产时禁用开始按钮并提示（正常不该发生）
                options.Add("-");
                if (startButton != null) startButton.interactable = false;
                GICLog.Warn("[CoopScreen] 未找到任何 BattleMapConfig（Resources/Configs）");
            }

            boardDropdown.AddOptions(options);
            boardDropdown.value = 0;
            boardDropdown.RefreshShownValue();
        }

        /// <summary>按下拉索引取地图资产（越界 null）</summary>
        internal BattleMapConfig MapAt(int index)
        {
            if (_battleMaps == null || index < 0 || index >= _battleMaps.Count) return null;
            return _battleMaps[index];
        }

        /// <summary>
        /// 当前选中的地图配置（**权威解析**=RoomManager.RoomMapConfigName 按名匹配本地清单；
        /// 权威名缺失/未匹配回落下拉索引——纯本机兜底，2026-10-11 联机批）
        /// </summary>
        internal BattleMapConfig GetSelectedMapConfig()
        {
            if (_battleMaps == null || _battleMaps.Count == 0) return null;
            string authoritative = _roomManager != null ? _roomManager.RoomMapConfigName : null;
            if (!string.IsNullOrEmpty(authoritative))
            {
                foreach (var map in _battleMaps)
                    if (map.name == authoritative) return map;
            }
            int index = boardDropdown != null ? boardDropdown.value : 0;
            if (index < 0 || index >= _battleMaps.Count) index = 0;
            return _battleMaps[index];
        }

        /// <summary>
        /// 当前选中的地图资产名（权威名直出；权威缺失回落下拉索引资产名）
        /// </summary>
        internal string GetSelectedMapConfigName()
        {
            var map = GetSelectedMapConfig();
            return map != null ? map.name : BattleLaunchConfig.DefaultMapName;
        }

        /// <summary>下拉选中同步到权威地图名（进房间/权威变化时；无通知防回环）</summary>
        internal void SyncBoardDropdownToAuthority()
        {
            if (boardDropdown == null || _battleMaps == null) return;
            string authoritative = _roomManager != null ? _roomManager.RoomMapConfigName : null;
            if (string.IsNullOrEmpty(authoritative))
            {
                if (boardDropdown.value != 0) boardDropdown.SetValueWithoutNotify(0);
                return;
            }
            for (int i = 0; i < _battleMaps.Count; i++)
            {
                if (_battleMaps[i].name == authoritative)
                {
                    if (boardDropdown.value != i) boardDropdown.SetValueWithoutNotify(i);
                    return;
                }
            }
        }

        #endregion
    }
}
