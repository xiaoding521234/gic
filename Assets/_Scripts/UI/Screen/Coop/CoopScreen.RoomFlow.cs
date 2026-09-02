// ==================== CoopScreen.RoomFlow.cs（房间状态机 + 建房/离房流程 + 网络回调） ====================
using UnityEngine;
using UnityEngine.Localization;
using Mirror;
using GIC.Framework;
using GIC.Battle;
using GIC.Data;
using GIC.Data.Event;
using GIC.Tool;
namespace GIC.UI
{


    public partial class CoopScreen
    {
        internal enum RoomState { DisconnectedClient, Host, ConnectedClient }
        internal RoomState _currentState = RoomState.DisconnectedClient;

        #region connManage

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
                GICLog.Error("[CoopScreen] 主机启动超时，请检查端口是否被占用");
                PopupManager.Instance.ShowToast(new LocalizedString(TableName.PopupText.ToString(), "Coop_HostStartTimeout"));
            }
        }

        void OnStartClick()
        {
            if (_currentState != RoomState.Host) return;

            // TODO: 全员就绪后开始游戏（战斗场景加载），目前仅校验准备状态
            foreach (var player in _playerManager.GetAllPlayers())
                if (!player.IsReady) return;
        }

        void OnLeaveRoomClick()
        {
            if (isClosing) return;

            switch (_currentState)
            {
                case RoomState.DisconnectedClient:
                    isClosing = true;
                    PopMusicSafe();
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

        #region networkCallback

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
    }
}
