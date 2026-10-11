// ==================== CoopScreen.PlayerList.cs（玩家行列表刷新 + 房间信息面板 + 地图预览联动） ====================
using UnityEngine;
using UnityEngine.Localization;
using GIC.Framework;
using GIC.Data;
using GIC.Tool;
namespace GIC.UI
{


    public partial class CoopScreen
    {
        #region PlayerManager evt

        void OnPlayerCountChanged(int count)
        {
            if (_currentState != RoomState.DisconnectedClient) RefreshRoomVisuals();
        }

        void OnPlayerInfoUpdated(string playerID, PlayerInfo updatedInfo)
        {
            if (_currentState == RoomState.DisconnectedClient) return;
            for (int i = 0; i < playerListContent.childCount; i++)
            {
                var rowUI = playerListContent.GetChild(i).GetComponent<PlayerRowView>();
                if (rowUI != null && rowUI.PlayerID == playerID)
                { rowUI.RefreshFromPlayerInfo(updatedInfo); break; }
            }
            // 出生点/颜色变化 → 地图预览标记同步（轻刷新——纹理/标记零重建）
            mapPreview?.RefreshPlayersOnly(_playerManager?.GetAllPlayers());
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

        /// <summary>房间可视件全量刷新（名册行+地图预览）——状态切换/地图变化/名册增删统一入口
        /// （2026-10-11 联机批：行出生点选项数与预览标记都吃当前权威地图）</summary>
        internal void RefreshRoomVisuals()
        {
            RefreshPlayerList();
            mapPreview?.Refresh(GetSelectedMapConfig(), _playerManager?.GetAllPlayers());
        }

        void RefreshPlayerList()
        {
            ClearPlayerList();
            if (_playerManager == null) return;
            int zoneCount = CurrentSpawnZoneCount();
            foreach (var player in _playerManager.GetAllPlayers())
            {
                var row = Instantiate(playerRowPrefab, playerListContent);
                var rowUI = row.GetComponent<PlayerRowView>();
                if (rowUI) rowUI.Setup(player, zoneCount);
            }
        }

        /// <summary>当前权威地图出生区数（出生下拉选项源；空图兜底 8=枚举上限）</summary>
        internal int CurrentSpawnZoneCount()
        {
            var map = GetSelectedMapConfig();
            return map != null && map.spawnZones != null ? map.spawnZones.Count : 8;
        }

        void ClearPlayerList()
        {
            for (int i = 0; i < playerListContent.childCount; i++)
                Destroy(playerListContent.GetChild(i).gameObject);
        }

        #endregion
    }
}
