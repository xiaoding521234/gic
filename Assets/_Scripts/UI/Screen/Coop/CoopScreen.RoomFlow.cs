// ==================== CoopScreen.RoomFlow.cs（房间状态机 + 建房/离房流程 + 网络回调） ====================
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;
using Mirror;
using GIC.Framework;
using GIC.Battle;
using GIC.Data;
using GIC.Tool;
namespace GIC.UI
{


    public partial class CoopScreen
    {
        internal enum RoomState { DisconnectedClient, Host, ConnectedClient }
        internal RoomState _currentState = RoomState.DisconnectedClient;

        // 建房中标志（防重复点击+锁退出；唯一写点=SetCreatingUI，池化复位在 OnShow）
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
        /// 建房流程（带中央反馈）：列表页转等待态（隐藏房间列表+中央显示「正在创建房间…」+锁退出）→
        /// 先让反馈渲染一帧 → 再吃 StartHost 的同步尖峰（端口探测循环）→ 0.5s 轮询等主机就绪 →
        /// 成功即切换动画进房间页；超时复位列表页并弹 Coop_HostStartTimeout。
        /// </summary>
        private IEnumerator CreateRoomWithFeedback()
        {
            SetCreatingUI(true);
            yield return null;

            _network.StartHost();
            _hostStartRetries = 0;
            Invoke(nameof(WaitForHostStart), 0.5f);
        }

        /// <summary>
        /// 建房等待态 UI（2026-09-13 用户拍板：反馈文字上列表页中央、不上按钮；建房中不可退出界面）：
        /// 隐藏房间列表 + 中央文字（EmptyRoomHint）切 CreatingRoom ↔ SearchingServers +
        /// 返回/创建按钮禁点。OnLeaveRoomClick 以 _startingHost 拦截（覆盖 ESC/返回全路径），
        /// 本方法同时是 _startingHost 的唯一写点。
        /// </summary>
        private void SetCreatingUI(bool creating)
        {
            _startingHost = creating;
            var scroll = ServerListScrollView;
            if (scroll != null) scroll.SetActive(!creating);
            if (leaveRoomButton != null) leaveRoomButton.interactable = !creating;
            if (createRoomButton != null) createRoomButton.interactable = !creating;

            if (emptyRoomHintObj == null || _emptyRoomHint == null) return;
            emptyRoomHintObj.gameObject.SetActive(true);
            _emptyRoomHint.SetSingleEntry(new LocalizedString("UIText",
                creating ? "CreatingRoom" : "SearchingServers"));
        }

        /// <summary>房间列表滚动视图（ServerListScroll）：从 serverListContent 向上解析所属 ScrollRect——
        /// CoopScreen 未对其建序列化字段（prefab 结构自明的内部层级，勿为它加 prefab 接线）</summary>
        private GameObject ServerListScrollView
        {
            get
            {
                if (_serverListScroll != null) return _serverListScroll;
                if (serverListContent != null)
                    _serverListScroll = serverListContent.GetComponentInParent<ScrollRect>()?.gameObject;
                return _serverListScroll;
            }
        }
        private GameObject _serverListScroll;

        private int _hostStartRetries;
        private const int MAX_HOST_START_RETRIES = 10;

        void WaitForHostStart()
        {
            if (NetworkServer.active)
            {
                SetCreatingUI(false); // 复位列表页（随后被切换动画送走）
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
                SetCreatingUI(false);
                GICLog.Error("[CoopScreen] 主机启动超时，请检查端口是否被占用");
                PopupManager.Instance.ShowToast(new LocalizedString(TableName.PopupText.ToString(), "Coop_HostStartTimeout"));
            }
        }

        void OnStartClick()
        {
            if (_currentState != RoomState.Host) return;
            if (isClosing) return; // 已在开局转场/关闭中

            if (!ValidateStartConditions()) return; // 未通过：轻提示已弹原因

            StartCoroutine(StartGameTransition());
        }

        /// <summary>
        /// 开局就绪校验（B7 LAN 全员就绪门扩展，2026-09-13 用户拍板加轻提示）：
        /// 人数超地图出生区上限 / 出生点冲突（Random 不参与，运行期落位不算冲突） /
        /// 颜色冲突 / 有玩家未准备——任一未过弹 PopupText 轻提示说明原因。
        /// 校验序=结构性问题（容量→出生点→颜色）先于状态问题（准备）。
        /// </summary>
        private bool ValidateStartConditions()
        {
            var players = new List<PlayerInfo>(_playerManager.GetAllPlayers());

            var map = GetSelectedMapConfig();
            if (map != null && players.Count > map.spawnZones.Count)
            {
                ShowStartBlockedToast("Coop_PlayerLimitExceeded");
                return false;
            }

            if (players.Where(p => p.SpawnPosition != SpawnPositionType.Random)
                       .GroupBy(p => p.SpawnPosition).Any(g => g.Count() > 1))
            {
                ShowStartBlockedToast("Coop_SpawnConflict");
                return false;
            }

            if (players.GroupBy(p => p.Color).Any(g => g.Count() > 1))
            {
                ShowStartBlockedToast("Coop_ColorConflict");
                return false;
            }

            if (players.Any(p => !p.IsReady))
            {
                ShowStartBlockedToast("Coop_NotAllReady");
                return false;
            }

            return true;
        }

        private void ShowStartBlockedToast(string key)
            => PopupManager.Instance.ShowToast(new LocalizedString(TableName.PopupText.ToString(), key));

        /// <summary>
        /// 开局转场（B1 单人开局：真人 + AI 补位对手，docs/active/22 §5；B7 LAN 时改为广播开战配置，
        /// 各端加载同一战斗场景，Host 权威，docs/18 决策一）：
        /// 加载页盖住 StopHost 拆除尖峰（0.2s 淡入，按所选地图换势力徽标+随机词条）→
        /// **Additive 根切换**（GameScene.SwitchRootScene：新根 Additive 加载+旧根 UnloadAsync 分帧
        /// 卸载，2026-09-13 用户拍板"Additive 卸载拆分"——激活帧卸载风暴根治，加载页动画全程流畅）→
        /// BattleScreen.Start 装配协程完毕 Reveal 揭幕。
        /// isClosing 同时拦下 StopHost 断连回调触发的切换动画（转场无需面板动画）。
        /// 锁与标志随根切换清栈入池的 OnDisable PopAll / 下次 RaiseShow 自清；
        /// 本协程宿主（本面板）会在清栈时被禁用——末行之后不得再有依赖代码
        /// （yield return 的嵌套协程宿主=GameScene，不受本面板禁用影响）。
        /// </summary>
        private IEnumerator StartGameTransition()
        {
            isClosing = true;
            InputLocks.Push(this, InputLockReason.Closing);
            // 开局即淡出当前音乐（2026-09-14 用户拍板）：转场加载页只留进度旋律，位置曲链随停；
            // 回大厅由 PositionManager 的 MainHall 激活钩子复活位置曲。StopMusic 不动音量/状态栈
            // ——本屏 PushMusicVolume 的配对 pop 随根切换 OnDisable 自洽
            AudioManager.Instance.StopMusic(0.5f);
            SceneFadeOverlay.Cover(GetSelectedMapConfig(), 0.2f);
            yield return new WaitForSeconds(0.2f); // 等加载页铺满再拆网络

            StopCurrentConnection();
            // 只写开局配置（不触发加载）；BattleScreen.Start 激活后 Take() 消费
            BattleLaunchConfig.Prepare(BattleLaunchConfig.BuildSinglePlayer(GetSelectedMapConfigName()));

            // 主动释放 Closing 防 OnDisable 保险丝走"泄漏锁警告"噪音路径；
            // SwitchRootScene 首行同步压入 SceneTransition 锁（本行同帧执行）→ 无缝接管。
            // 协程宿主必须是 GameScene：SwitchRootScene 中途 PoolAllForRootSwitch 会 SetActive(false)
            // 本面板——挂本面板的协程（含 StartCoroutine 嵌套）会被腰斩，旧根卸载+放锁永不执行
            InputLocks.Pop(this, InputLockReason.Closing);
            yield return GameScene.Instance.StartCoroutine(GameScene.Instance.SwitchRootScene(SceneType.BattleScreen));
            // 本行仅供断言续行性（正常路径 GameScene 宿主协程自洽完成，本面板协程可能已随禁用终止）
        }

        void OnLeaveRoomClick()
        {
            if (isClosing) return;
            if (_startingHost) return; // 建房中不可退出界面（2026-09-13 用户拍板；ESC/返回全经此路径）

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
