// ==================== CoopScreen.PlayerList.cs（玩家行列表刷新 + 房间信息面板） ====================
using UnityEngine;
using UnityEngine.Localization;
using GIC.Framework;
using GIC.Battle;
using GIC.Data;
using GIC.Data.Event;
using GIC.Tool;
namespace GIC.UI
{


    public partial class CoopScreen
    {
        #region PlayerManager evt

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
    }
}
