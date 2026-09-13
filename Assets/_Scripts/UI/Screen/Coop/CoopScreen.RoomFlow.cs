// ==================== CoopScreen.RoomFlow.cs（房间状态机 + 建房/离房流程 + 网络回调） ====================
using System.Collections;
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

        // 建房中标志（防重复点击；池化复位在 OnShow）
        private bool _startingHost;

        #region connManage

        private void StopCurrentConnection()
        {
            if (NetworkServer.active || NetworkClient.isConnected)
                _netMgr?.StopHost();
        }

        private void SetRoomState(RoomState newState)
        {
            var oldState = _currentState;

            // 同态重复刷新：切换动画进行中直接吞掉——运行中动画的 applyState 会在正确时序收口本次
            // 刷新（离房路径"网络回调+直调"双触达 ReturnToDiscovery 实证：直刷会中途 SetActive+
            // 复位半途动画=房间页退场被腰斩+列表页整屏闪现再重扫=抖动）；无动画进行时同态照常即时刷新
            if (oldState == newState && _switchRoutine != null) return;

            _currentState = newState;

            // 视图切换（列表↔房间）走过渡动画；同态刷新/关闭中/开局转场中（isClosing）即时刷新
            bool panelSwitch = (oldState == RoomState.DisconnectedClient)
                             != (newState == RoomState.DisconnectedClient);
            if (panelSwitch && !isClosing && isActiveAndEnabled)
            {
                if (_switchRoutine != null) StopCoroutine(_switchRoutine); // 快速连续切换：中断旧过渡重启
                var from = oldState == RoomState.DisconnectedClient ? serverListPanel : roomPanel;
                var to = newState == RoomState.DisconnectedClient ? serverListPanel : roomPanel;
                _switchRoutine = StartCoroutine(SwitchPanelRoutine(from, to, RefreshUI));
            }
            else
            {
                RefreshUI();
            }
        }

        private void RefreshUI()
        {
            bool isDisconnected = _currentState == RoomState.DisconnectedClient;
            bool isHost = _currentState == RoomState.Host;

            serverListPanel.SetActive(isDisconnected);
            roomPanel.SetActive(!isDisconnected);

            // 两面板+返回钮每次状态刷新即归零动画残留（切换动画被连续状态变化打断后停在半途，
            // 重新激活时靠此恢复完成态；切换过渡内在新面板设起始态前执行，顺序无冲突）
            SnapAllPanelsToRest();

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
            if (_currentState == RoomState.Host || _startingHost) return;
            StartCoroutine(CreateRoomWithFeedback());
        }

        /// <summary>
        /// 建房流程（带加载反馈）：按钮转「创建中」并禁点 → 先让反馈渲染一帧 →
        /// 再吃 StartHost 的同步尖峰（端口探测循环）→ 0.5s 轮询等主机就绪 → 成功即切换动画进房间页。
        /// </summary>
        private IEnumerator CreateRoomWithFeedback()
        {
            _startingHost = true;
            SetCreateRoomBusy(true);
            yield return null;

            _network.StartHost();
            _hostStartRetries = 0;
            Invoke(nameof(WaitForHostStart), 0.5f);
        }

        /// <summary>建房按钮 busy 态：禁点 + 文案 CreateRoom ↔ CreatingRoom（UIText 9065）</summary>
        private void SetCreateRoomBusy(bool busy)
        {
            if (createRoomButton == null) return;
            createRoomButton.interactable = !busy;
            _createRoomText?.SetSingleEntry(busy
                ? new LocalizedString("UIText", "CreatingRoom")
                : new LocalizedString("UIText", "CreateRoom"));
        }

        private int _hostStartRetries;
        private const int MAX_HOST_START_RETRIES = 10;

        void WaitForHostStart()
        {
            if (NetworkServer.active)
            {
                _startingHost = false;
                SetCreateRoomBusy(false); // 复位按钮（随后列表页被切换动画送走）
                SetRoomState(RoomState.Host); // → 列表↔房间切换动画
                _network.StartBroadcast();

                // 预热开局加载页：建房等待期吃掉首次实例化+TMP 字体初始化的 ~0.5s 同步开销，
                // 开局转场 Cover 帧不再尖峰（2026-09-13 帧实测拍板"加载不阻塞"）
                SceneFadeOverlay.PreWarm();
            }
            else if (_hostStartRetries < MAX_HOST_START_RETRIES)
            {
                _hostStartRetries++;
                Invoke(nameof(WaitForHostStart), 0.5f);
            }
            else
            {
                _startingHost = false;
                SetCreateRoomBusy(false);
                GICLog.Error("[CoopScreen] 主机启动超时，请检查端口是否被占用");
                PopupManager.Instance.ShowToast(new LocalizedString(TableName.PopupText.ToString(), "Coop_HostStartTimeout"));
            }
        }

        void OnStartClick()
        {
            if (_currentState != RoomState.Host) return;
            if (isClosing) return; // 已在开局转场/关闭中

            // 就绪校验（B7 LAN：全员就绪门）
            foreach (var player in _playerManager.GetAllPlayers())
                if (!player.IsReady) return;

            StartCoroutine(StartGameTransition());
        }

        /// <summary>
        /// 开局转场（B1 单人开局：真人 + AI 补位对手，docs/22 §5；B7 LAN 时改为广播开战配置，
        /// 各端加载同一战斗场景，Host 权威，docs/18 决策一）：
        /// 加载页盖住 StopHost 拆除尖峰（0.2s 淡入，按所选地图换势力徽标+随机词条）→
        /// **预载战斗场景**（LoadSceneAsync 分帧加载，加载页动画全程流畅不冻结——2026-09-13
        /// 用户拍板"加载不阻塞"，模式照 MainHallScreen.ExitWithPreloadCoroutine 先例）→ 激活 →
        /// BattleScreen.Start 装配完毕 Reveal 揭幕。
        /// isClosing 同时拦下 StopHost 断连回调触发的切换动画（转场无需面板动画）。
        /// 锁与标志随 Single 加载自动入池的 OnDisable PopAll / 下次 RaiseShow 自清。
        /// </summary>
        private IEnumerator StartGameTransition()
        {
            isClosing = true;
            InputLocks.Push(this, InputLockReason.Closing);
            SceneFadeOverlay.Cover(GetSelectedMapConfig(), 0.2f);
            yield return new WaitForSeconds(0.2f); // 等加载页铺满再拆网络

            StopCurrentConnection();
            // 只写开局配置（不触发加载）；BattleScreen.Start 激活后 Take() 消费
            BattleLaunchConfig.Prepare(BattleLaunchConfig.BuildSinglePlayer(GetSelectedMapConfigName()));

            // 预载战斗场景：后台分帧加载场景资产，期间加载页动画（徽标缓转/填充扫描）持续渲染
            AsyncOperation asyncLoad = null;
            yield return StartCoroutine(GameScene.Instance.PreloadScene(SceneType.BattleScreen, op => asyncLoad = op));
            if (asyncLoad == null)
            {
                InputLocks.Pop(this, InputLockReason.Closing);
                isClosing = false;
                GICLog.Error("[CoopScreen] 战斗场景预载失败");
                SceneFadeOverlay.Reveal(0.2f);
                yield break;
            }

            // 主动释放 Closing 防 OnDisable 保险丝走"泄漏锁警告"噪音路径；
            // ActivatePreloadedScene 首行同步压入 SceneTransition 锁（本行同帧执行）→ 无缝接管
            InputLocks.Pop(this, InputLockReason.Closing);
            yield return GameScene.Instance.ActivatePreloadedScene(asyncLoad, SceneType.BattleScreen);
        }

        void OnLeaveRoomClick()
        {
            if (isClosing) return;

            switch (_currentState)
            {
                case RoomState.DisconnectedClient:
                    // 标准关闭模板：防重入 + Closing 锁 + 音乐恢复 + 退场动画 + PopToPrevious 收尾
                    CloseScreen(PlayExitAnimation);
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
