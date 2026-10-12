// RoomManager.cs - 房间管理器（连接/断开流程 + 房间网络事件处理 + 颜色分配）
using System;
using System.Collections.Generic;
using Mirror;
using GIC.Battle;
using GIC.Data;
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

        /// <summary>房间权威地图变化（服务器记录+广播 UpdateRoomInfoEvent 后触发；UI 同步下拉/预览）</summary>
        public event Action<string> OnRoomMapChanged;

        // ==================== 房间状态（服务器权威，2026-10-11 联机批） ====================

        /// <summary>房间当前权威地图资产名（host 下拉选图经 SetMapRequestEvent 写入；各端经
        /// UpdateRoomInfoEvent 同步）。初始=默认图。</summary>
        public string RoomMapConfigName { get; private set; } = BattleLaunchConfig.DefaultMapName;

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
        private AddAIPlayerHandler _addAIPlayerHandler;
        private SetAIConfigHandler _setAIConfigHandler;
        private SetMapHandler _setMapHandler;
        private UpdateRoomInfoHandler _updateRoomInfoHandler;
        private ClientDeckHandler _clientDeckHandler;
        private ClientBattleReadyHandler _clientBattleReadyHandler;

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
            _addAIPlayerHandler = new AddAIPlayerHandler(this);
            _setAIConfigHandler = new SetAIConfigHandler(this);
            _setMapHandler = new SetMapHandler(this);
            _updateRoomInfoHandler = new UpdateRoomInfoHandler(this);
            _clientDeckHandler = new ClientDeckHandler(this);
            _clientBattleReadyHandler = new ClientBattleReadyHandler(this);
        }

        private void SubscribeEvents()
        {
            // 订阅服务器端收到的客户端请求
            EventBusHub.Instance.Subscribe<SetTeamRequestEvent>(_setTeamHandler, this);
            EventBusHub.Instance.Subscribe<SetColorRequestEvent>(_setColorHandler, this);
            EventBusHub.Instance.Subscribe<SetSpawnRequestEvent>(_setSpawnHandler, this);
            EventBusHub.Instance.Subscribe<ToggleReadyRequestEvent>(_toggleReadyHandler, this);
            EventBusHub.Instance.Subscribe<KickPlayerRequestEvent>(_kickPlayerHandler, this);
            EventBusHub.Instance.Subscribe<AddAIPlayerRequestEvent>(_addAIPlayerHandler, this);
            EventBusHub.Instance.Subscribe<SetAIConfigRequestEvent>(_setAIConfigHandler, this);
            EventBusHub.Instance.Subscribe<SetMapRequestEvent>(_setMapHandler, this);
            EventBusHub.Instance.Subscribe<ClientDeckEvent>(_clientDeckHandler, this);
            EventBusHub.Instance.Subscribe<ClientBattleReadyEvent>(_clientBattleReadyHandler, this);

            // 订阅网络事件（所有客户端收到）
            EventBusHub.Instance.Subscribe<AddPlayerEvent>(_addPlayerHandler, this);
            EventBusHub.Instance.Subscribe<RemovePlayerEvent>(_removePlayerHandler, this);
            EventBusHub.Instance.Subscribe<UpdatePlayerInfoEvent>(_updatePlayerInfoHandler, this);
            EventBusHub.Instance.Subscribe<KickedFromRoomEvent>(_kickedFromRoomHandler, this);
            EventBusHub.Instance.Subscribe<SetSelfPlayerEvent>(_setSelfPlayerHandler, this);
            EventBusHub.Instance.Subscribe<SetPlayerNameRequestEvent>(_setPlayerNameHandler, this);
            EventBusHub.Instance.Subscribe<UpdateRoomInfoEvent>(_updateRoomInfoHandler, this);
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

            playerManager.RegisterPlayer(playerID, playerName, GetBalancedTeam(),
                          GetNextAvailableColor(), isHost, conn.address, conn: conn);

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

            // 3. 同步房间权威地图（加入即知当前选图——下拉/预览就位，2026-10-11 联机批；
            //    老端不掉线重连时也拿得到）
            EventBusHub.Instance.PublishToPlayer(playerID,
                new UpdateRoomInfoEvent { MapConfigName = RoomMapConfigName });

            // 4. 广播新玩家加入
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

        // ==================== 颜色/队伍分配（2026-10-11 联机批改良） ====================

        /// <summary>首个未占用颜色（旧按人数取模在玩家换色后必撞色；全满回落取模兜底）</summary>
        private PlayerColor GetNextAvailableColor()
        {
            var used = playerManager.GetUsedColors();
            var colors = (PlayerColor[])Enum.GetValues(typeof(PlayerColor));
            foreach (var c in colors)
            {
                if (!used.Contains(c))
                {
                    GICLog.Info($"[RoomManager] 分配颜色: {c}");
                    return c;
                }
            }
            var fallback = colors[playerManager.GetPlayerCount() % colors.Length];
            GICLog.Info($"[RoomManager] 颜色全占用，回落取模: {fallback}");
            return fallback;
        }

        /// <summary>人数较少的队伍（新人/新 AI 默认落位；同数取 A）——host 在 A 时第二人自动落 B，
        /// 满足开局门「至少两个不同队伍」无需手动改队</summary>
        private TeamType GetBalancedTeam()
        {
            int a = playerManager.GetPlayersInTeam(TeamType.A).Count;
            int b = playerManager.GetPlayersInTeam(TeamType.B).Count;
            return a <= b ? TeamType.A : TeamType.B;
        }

        // ==================== AI 玩家（2026-10-11 联机批） ====================

        /// <summary>
        /// 登记一名 AI 玩家（服务器侧；host 经 AddAIPlayerRequestEvent 触发）：
        /// 空连接玩家——ID=AI_N（与 connectionId 数字串天然不撞）、队伍=自动平衡落位（host 可改）、
        /// 恒就绪；移除=KickPlayerRequestEvent（KickPlayerHandler 对 null 连接已有守卫，走同一路径）。
        /// </summary>
        private void AddAIPlayer()
        {
            int n = 1;
            while (playerManager.GetPlayerInfo("AI_" + n) != null) n++;
            string id = "AI_" + n;

            playerManager.RegisterPlayer(id, "AI 玩家" + n, GetBalancedTeam(),
                GetNextAvailableColor(), isHost: false, ipAddress: "AI", isAI: true);

            var info = playerManager.GetPlayerInfo(id);
            if (info != null)
                EventBusHub.Instance.Send(new AddPlayerEvent { PlayerInfo = info });
        }

        // ==================== 客户端卡组上报存储（B7 LAN 批） ====================

        // 各真人玩家上报的卡组（ClientDeckEvent 收存；BattleScreen 为远端玩家构建手牌时消费）
        private readonly Dictionary<string, List<HandCard>> _uploadedDecks
            = new Dictionary<string, List<HandCard>>();

        // 战斗就绪握手（ClientBattleReadyEvent 收存；host 开局广播前等齐判据）
        private readonly HashSet<string> _battleReadyPlayers = new HashSet<string>();

        /// <summary>玩家是否已上报卡组（host 开局装配等待判据）</summary>
        public bool HasUploadedDeck(string playerID) => _uploadedDecks.ContainsKey(playerID);

        /// <summary>取玩家上报卡组（无上报返回 null——调用方回落 host 卡组）</summary>
        public List<HandCard> GetUploadedDeck(string playerID)
            => _uploadedDecks.TryGetValue(playerID, out var deck) ? deck : null;

        /// <summary>玩家是否已发战斗就绪握手（host 开局等待判据）</summary>
        public bool IsBattleReady(string playerID) => _battleReadyPlayers.Contains(playerID);

        /// <summary>是否远端真人（host 视角：非 AI、有活动网络连接且非本机连接）——就绪/卡组等待名单用</summary>
        public bool IsRemoteHuman(string playerID)
        {
            var info = playerManager.GetPlayerInfo(playerID);
            return info != null && !info.IsAI
                && info.connectionToClient != null
                && !(info.connectionToClient is Mirror.LocalConnectionToClient);
        }

        /// <summary>清空卡组上报+就绪握手（host 每次开局前调用，防跨局残留）</summary>
        public void ResetBattleExchange()
        {
            _uploadedDecks.Clear();
            _battleReadyPlayers.Clear();
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
                                   e.PlayerInfo.IsHost, e.PlayerInfo.IPAddress,
                                   e.PlayerInfo.IsAI); // AI 身份随入册广播同步（2026-10-12 选择器批补——原漏传=客户端行丢 AI 身份）
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

        /// <summary>增加 AI 玩家请求（服务器侧登记空连接玩家并广播）</summary>
        private class AddAIPlayerHandler : IEventHandler<AddAIPlayerRequestEvent>
        {
            private readonly RoomManager _room;
            public AddAIPlayerHandler(RoomManager m) => _room = m;
            public bool CanHandle(AddAIPlayerRequestEvent e) => NetworkServer.active;
            public void Handle(AddAIPlayerRequestEvent e) => _room.AddAIPlayer();
        }

        /// <summary>AI 玩家配置请求（2026-10-12 选择器批）：host 在 AI 行点选难度/人格——
        /// 沿 PlayerInfoUpdateRequestHandler 家族（改 PlayerInfo→广播 UpdatePlayerInfoEvent）</summary>
        private class SetAIConfigHandler : PlayerInfoUpdateRequestHandler<SetAIConfigRequestEvent>
        {
            public SetAIConfigHandler(RoomManager m) : base(m) { }
            protected override string GetPlayerID(SetAIConfigRequestEvent e) => e.TargetPlayerID;
            protected override void ApplyChange(SetAIConfigRequestEvent e, string pid)
                => _room.playerManager.SetAIConfig(pid, e.AIDifficulty, e.AIPersona);
        }

        /// <summary>设置房间地图请求（服务器记录权威地图并广播 UpdateRoomInfoEvent）</summary>
        private class SetMapHandler : IEventHandler<SetMapRequestEvent>
        {
            private readonly RoomManager _room;
            public SetMapHandler(RoomManager m) => _room = m;
            public bool CanHandle(SetMapRequestEvent e) => NetworkServer.active;
            public void Handle(SetMapRequestEvent e)
            {
                if (string.IsNullOrEmpty(e.MapConfigName)) return;
                EventBusHub.Instance.Send(new UpdateRoomInfoEvent { MapConfigName = e.MapConfigName });
            }
        }

        /// <summary>房间信息更新（各端收）：同步权威地图名并通知 UI（下拉选中+地图预览刷新）</summary>
        private class UpdateRoomInfoHandler : IEventHandler<UpdateRoomInfoEvent>
        {
            private readonly RoomManager _room;
            public UpdateRoomInfoHandler(RoomManager m) => _room = m;
            public bool CanHandle(UpdateRoomInfoEvent e) => e.Source == EventSource.Network;
            public void Handle(UpdateRoomInfoEvent e)
            {
                _room.RoomMapConfigName = e.MapConfigName;
                _room.OnRoomMapChanged?.Invoke(e.MapConfigName);
            }
        }

        /// <summary>客户端卡组上报（host 收存，BattleScreen 为远端玩家建手牌时消费）</summary>
        private class ClientDeckHandler : IEventHandler<ClientDeckEvent>
        {
            private readonly RoomManager _room;
            public ClientDeckHandler(RoomManager m) => _room = m;
            public bool CanHandle(ClientDeckEvent e) => NetworkServer.active;
            public void Handle(ClientDeckEvent e)
            {
                string pid = e.SourcePlayerID;
                if (string.IsNullOrEmpty(pid) || pid == PlayerID.Unknown) pid = _room.playerManager.SelfPlayerID;
                if (string.IsNullOrEmpty(pid)) return;
                _room._uploadedDecks[pid] = e.Entries ?? new List<HandCard>();
                GICLog.Info($"[RoomManager] 收到玩家 {pid} 卡组上报: {e.Entries?.Count ?? 0} 条");
            }
        }

        /// <summary>客户端战斗就绪握手（host 收存，开局广播前等齐判据）</summary>
        private class ClientBattleReadyHandler : IEventHandler<ClientBattleReadyEvent>
        {
            private readonly RoomManager _room;
            public ClientBattleReadyHandler(RoomManager m) => _room = m;
            public bool CanHandle(ClientBattleReadyEvent e) => NetworkServer.active;
            public void Handle(ClientBattleReadyEvent e)
            {
                string pid = e.SourcePlayerID;
                if (string.IsNullOrEmpty(pid) || pid == PlayerID.Unknown) pid = _room.playerManager.SelfPlayerID;
                if (string.IsNullOrEmpty(pid)) return;
                _room._battleReadyPlayers.Add(pid);
                GICLog.Info($"[RoomManager] 玩家 {pid} 战斗就绪握手到达");
            }
        }
    }
}
