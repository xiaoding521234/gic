// RoomManager.cs - 房间管理器（连接/断开流程 + 房间网络事件处理 + 颜色分配）
using System;
using Mirror;
using GIC.Battle;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
namespace GIC.Framework
{


    /// <summary>
    /// 房间管理器：联机房间流程编排（连接/断开、玩家进出同步、房间状态修改请求、踢人、颜色分配）。
    /// 玩家名册数据与查询在 PlayerManager（数据层），本类只做网络流程，不持有玩家数据。
    /// </summary>
    [Component]
    public class RoomManager
    {
        private readonly SaveManager saveManager;
        private readonly PlayerManager playerManager;

        public RoomManager(SaveManager saveManager, PlayerManager playerManager)
        {
            this.saveManager = saveManager;
            this.playerManager = playerManager;
        }

        // 网络引用
        private MyNetworkManager _networkManager;

        /// <summary>由 MyNetworkManager 在 Awake 中反向注入</summary>
        public void SetNetworkManager(MyNetworkManager networkManager)
        {
            _networkManager = networkManager;
        }

        // ==================== 事件 ====================

        /// <summary>本端被踢出房间（收到服务器 KickedFromRoomEvent 时触发）</summary>
        public event Action OnKickedFromRoom;

        // ==================== Handler 引用（用于取消订阅） ====================

        private SetTeamHandler _setTeamHandler;
        private SetColorHandler _setColorHandler;
        private SetSpawnHandler _setSpawnHandler;
        private ToggleReadyHandler _toggleReadyHandler;
        private KickPlayerHandler _kickPlayerHandler;
        private AddPlayerHandler _addPlayerHandler;
        private RemovePlayerHandler _removePlayerHandler;
        private UpdatePlayerInfoHandler _updatePlayerInfoHandler;
        private KickedFromRoomHandler _kickedFromRoomHandler;
        private SetSelfPlayerHandler _setSelfPlayerHandler;
        private SetPlayerNameHandler _setPlayerNameHandler;

        [PostConstruct]
        public void Init()
        {
            // 创建 Handler 实例并保存引用
            CreateHandlers();

            // 订阅网络事件
            SubscribeEvents();

            GICLog.Info("[RoomManager] 启动完成，等待网络初始化");
        }

        private void CreateHandlers()
        {
            _setTeamHandler = new SetTeamHandler(this);
            _setColorHandler = new SetColorHandler(this);
            _setSpawnHandler = new SetSpawnHandler(this);
            _toggleReadyHandler = new ToggleReadyHandler(this);
            _kickPlayerHandler = new KickPlayerHandler(this);
            _addPlayerHandler = new AddPlayerHandler(this);
            _removePlayerHandler = new RemovePlayerHandler(this);
            _updatePlayerInfoHandler = new UpdatePlayerInfoHandler(this);
            _kickedFromRoomHandler = new KickedFromRoomHandler(this);
            _setSelfPlayerHandler = new SetSelfPlayerHandler(this);
            _setPlayerNameHandler = new SetPlayerNameHandler(this);
        }

        private void SubscribeEvents()
        {
            // 订阅服务器端收到的客户端请求
            EventBusHub.Instance.Subscribe<SetTeamRequestEvent>(_setTeamHandler, this);
            EventBusHub.Instance.Subscribe<SetColorRequestEvent>(_setColorHandler, this);
            EventBusHub.Instance.Subscribe<SetSpawnRequestEvent>(_setSpawnHandler, this);
            EventBusHub.Instance.Subscribe<ToggleReadyRequestEvent>(_toggleReadyHandler, this);
            EventBusHub.Instance.Subscribe<KickPlayerRequestEvent>(_kickPlayerHandler, this);

            // 订阅网络事件（所有客户端收到）
            EventBusHub.Instance.Subscribe<AddPlayerEvent>(_addPlayerHandler, this);
            EventBusHub.Instance.Subscribe<RemovePlayerEvent>(_removePlayerHandler, this);
            EventBusHub.Instance.Subscribe<UpdatePlayerInfoEvent>(_updatePlayerInfoHandler, this);
            EventBusHub.Instance.Subscribe<KickedFromRoomEvent>(_kickedFromRoomHandler, this);
            EventBusHub.Instance.Subscribe<SetSelfPlayerEvent>(_setSelfPlayerHandler, this);
            EventBusHub.Instance.Subscribe<SetPlayerNameRequestEvent>(_setPlayerNameHandler, this);
        }

        // ==================== 连接流程 ====================

        /// <summary>
        /// 处理服务器端新连接（由 MyNetworkManager.OnServerConnect 调用）
        /// 封装玩家注册 + 事件同步全套逻辑
        /// </summary>
        public void HandleServerConnect(NetworkConnectionToClient conn)
        {
            string playerID = conn.connectionId.ToString();
            bool isHost = playerID == PlayerID.Host;
            string playerName = isHost
                ? (saveManager?.CurrentSave?.playerName ?? "旅行者")
                : ("玩家" + conn.connectionId);

            playerManager.RegisterPlayer(playerID, playerName, TeamType.A,
                          GetNextAvailableColor(), isHost, conn.address, conn);

            // 1. 告诉新客户端它自己的 PlayerID
            EventBusHub.Instance.PublishToPlayer(playerID, new SetSelfPlayerEvent { TargetPlayerID = playerID });

            // Host 自己也设置 ID（服务器不会收到发给自己的 TargetRpc）
            if (isHost) playerManager.SetSelfPlayerID(playerID);

            // 2. 通知新客户端所有已有玩家
            foreach (var player in playerManager.GetAllPlayers())
            {
                if (player.PlayerID != playerID)
                    EventBusHub.Instance.PublishToPlayer(playerID, new AddPlayerEvent { PlayerInfo = player });
            }

            // 3. 广播新玩家加入
            var newPlayerInfo = playerManager.GetPlayerInfo(playerID);
            if (newPlayerInfo != null)
                EventBusHub.Instance.Send(new AddPlayerEvent { PlayerInfo = newPlayerInfo });
        }

        /// <summary>
        /// 处理服务器端断开连接（由 MyNetworkManager.OnServerDisconnect 调用）
        /// </summary>
        public void HandleServerDisconnect(NetworkConnectionToClient conn)
        {
            string playerID = conn.connectionId.ToString();
            playerManager.UnregisterPlayer(playerID);
            EventBusHub.Instance.Send(new RemovePlayerEvent { TargetPlayerID = playerID });
        }

        // ==================== 颜色分配 ====================

        private PlayerColor GetNextAvailableColor()
        {
            int usedCount = playerManager.GetPlayerCount();
            var colors = (PlayerColor[])Enum.GetValues(typeof(PlayerColor));
            var color = colors[usedCount % colors.Length];
            GICLog.Info($"[RoomManager] 分配颜色: {color} (第 {usedCount + 1} 位玩家)");
            return color;
        }

        // ==================== 清理 ====================

        public void Cleanup()
        {
            // owner 登记制：一行退订 SubscribeEvents 中登记的全部订阅
            // 注意：仅供应用关闭时调用。EventHandler 常驻订阅（CanHandle 有 NetworkServer.active / Source==Network 守卫），
            // 若在断线时调用会导致重连后名册不再同步（2026-08-15 修复的 bug）。
            EventBusHub.Instance?.UnsubscribeOwner(this);
        }


        // ==================== 内部事件处理器 ====================

        /// <summary>
        /// 更新请求处理器模板 — CanHandle（服务器端检查）+ Handle（修改属性 → 广播更新）
        /// 子类只需实现 GetPlayerID 和 ApplyChange
        /// </summary>
        private abstract class PlayerInfoUpdateRequestHandler<T> : IEventHandler<T> where T : BaseEvent
        {
            protected readonly RoomManager _room;
            protected PlayerInfoUpdateRequestHandler(RoomManager room) => _room = room;

            public bool CanHandle(T evt) => NetworkServer.active;

            public void Handle(T evt)
            {
                string pid = GetPlayerID(evt);
                GICLog.Info($"[RoomManager.{GetType().Name}] 收到请求: PlayerID={pid}");

                ApplyChange(evt, pid);

                var info = _room.playerManager.GetPlayerInfo(pid);
                if (info != null)
                    EventBusHub.Instance.Send(new UpdatePlayerInfoEvent { UpdatedInfo = info });
                else
                    GICLog.Warn($"[RoomManager.{GetType().Name}] 未找到玩家: PlayerID={pid}");
            }

            protected abstract string GetPlayerID(T evt);
            protected abstract void ApplyChange(T evt, string playerID);
        }

        // --- 服务器端请求处理器 ---

        private class SetTeamHandler : PlayerInfoUpdateRequestHandler<SetTeamRequestEvent>
        {
            public SetTeamHandler(RoomManager m) : base(m) { }
            protected override string GetPlayerID(SetTeamRequestEvent e) => e.TargetPlayerID;
            protected override void ApplyChange(SetTeamRequestEvent e, string pid) => _room.playerManager.SetPlayerTeam(pid, e.Team);
        }

        private class SetColorHandler : PlayerInfoUpdateRequestHandler<SetColorRequestEvent>
        {
            public SetColorHandler(RoomManager m) : base(m) { }
            protected override string GetPlayerID(SetColorRequestEvent e) => e.TargetPlayerID;
            protected override void ApplyChange(SetColorRequestEvent e, string pid) => _room.playerManager.SetPlayerColor(pid, e.Color);
        }

        private class SetSpawnHandler : PlayerInfoUpdateRequestHandler<SetSpawnRequestEvent>
        {
            public SetSpawnHandler(RoomManager m) : base(m) { }
            protected override string GetPlayerID(SetSpawnRequestEvent e) => e.PlayerID;
            protected override void ApplyChange(SetSpawnRequestEvent e, string pid) => _room.playerManager.SetPlayerSpawnPosition(pid, e.SpawnPosition);
        }

        private class ToggleReadyHandler : PlayerInfoUpdateRequestHandler<ToggleReadyRequestEvent>
        {
            public ToggleReadyHandler(RoomManager m) : base(m) { }
            protected override string GetPlayerID(ToggleReadyRequestEvent e) => e.TargetPlayerID;
            protected override void ApplyChange(ToggleReadyRequestEvent e, string pid) => _room.playerManager.ToggleReady(pid);
        }

        private class KickPlayerHandler : IEventHandler<KickPlayerRequestEvent>
        {
            private readonly RoomManager _room;
            public KickPlayerHandler(RoomManager m) => _room = m;
            public bool CanHandle(KickPlayerRequestEvent e) => NetworkServer.active;

            public void Handle(KickPlayerRequestEvent e)
            {
                var info = _room.playerManager.GetPlayerInfo(e.TargetPlayerID);
                if (info == null) return;

                if (info.connectionToClient != null)
                {
                    EventBusHub.Instance.PublishToPlayer(e.TargetPlayerID, new KickedFromRoomEvent());
                    info.connectionToClient.Disconnect();
                }

                _room.playerManager.UnregisterPlayer(e.TargetPlayerID);
                EventBusHub.Instance.Send(new RemovePlayerEvent { TargetPlayerID = e.TargetPlayerID });
            }
        }

        // --- 网络同步事件处理器 ---

        private class AddPlayerHandler : IEventHandler<AddPlayerEvent>
        {
            private readonly RoomManager _room;
            public AddPlayerHandler(RoomManager m) => _room = m;
            public bool CanHandle(AddPlayerEvent e) => e.Source == EventSource.Network;

            public void Handle(AddPlayerEvent e)
            {
                if (_room.playerManager.GetPlayerInfo(e.PlayerInfo.PlayerID) != null) return;
                _room.playerManager.RegisterPlayer(e.PlayerInfo.PlayerID, e.PlayerInfo.PlayerName,
                                   e.PlayerInfo.Team, e.PlayerInfo.Color,
                                   e.PlayerInfo.IsHost, e.PlayerInfo.IPAddress);
            }
        }

        private class RemovePlayerHandler : IEventHandler<RemovePlayerEvent>
        {
            private readonly RoomManager _room;
            public RemovePlayerHandler(RoomManager m) => _room = m;
            public bool CanHandle(RemovePlayerEvent e) => e.Source == EventSource.Network;
            public void Handle(RemovePlayerEvent e) => _room.playerManager.UnregisterPlayer(e.TargetPlayerID);
        }

        private class UpdatePlayerInfoHandler : IEventHandler<UpdatePlayerInfoEvent>
        {
            private readonly RoomManager _room;
            public UpdatePlayerInfoHandler(RoomManager m) => _room = m;
            public bool CanHandle(UpdatePlayerInfoEvent e) => e.Source == EventSource.Network;
            public void Handle(UpdatePlayerInfoEvent e) => _room.playerManager.UpdatePlayerInfo(e.UpdatedInfo.PlayerID, e.UpdatedInfo);
        }

        private class KickedFromRoomHandler : IEventHandler<KickedFromRoomEvent>
        {
            private readonly RoomManager _room;
            public KickedFromRoomHandler(RoomManager m) => _room = m;
            public bool CanHandle(KickedFromRoomEvent e) => e.Source == EventSource.Network;

            public void Handle(KickedFromRoomEvent e)
            {
                _room._networkManager?.MarkAsKicked();
                _room.OnKickedFromRoom?.Invoke();
            }
        }

        private class SetSelfPlayerHandler : IEventHandler<SetSelfPlayerEvent>
        {
            private readonly RoomManager _room;
            public SetSelfPlayerHandler(RoomManager m) => _room = m;
            public bool CanHandle(SetSelfPlayerEvent e) => e.Source == EventSource.Network;
            public void Handle(SetSelfPlayerEvent e) => _room.playerManager.SetSelfPlayerID(e.TargetPlayerID);
        }

        private class SetPlayerNameHandler : IEventHandler<SetPlayerNameRequestEvent>
        {
            private readonly RoomManager _room;
            public SetPlayerNameHandler(RoomManager m) => _room = m;
            public bool CanHandle(SetPlayerNameRequestEvent e) => NetworkServer.active;
            public void Handle(SetPlayerNameRequestEvent e)
            {
                string pid = e.SourcePlayerID;
                if (string.IsNullOrEmpty(pid) || pid == PlayerID.Unknown) pid = _room.playerManager.SelfPlayerID;
                _room.playerManager.SetPlayerName(pid, e.PlayerName);
                var info = _room.playerManager.GetPlayerInfo(pid);
                if (info != null)
                    EventBusHub.Instance.Send(new UpdatePlayerInfoEvent { UpdatedInfo = info });
            }
        }
    }
}
