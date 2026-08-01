// PlayerManager.cs - 玩家管理器（普通类，实现 IWargameManager）
using System;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using GIC.Battle;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
namespace GIC.Framework
{


    /// <summary>
    /// 玩家管理器（普通类，由 Wargame 统一管理生命周期）
    /// </summary>
    public class PlayerManager : IWargameManager
    {
        // 单例由 Wargame 持有
        public static PlayerManager Instance { get; private set; }

        // 自身 PlayerID（由服务器通过 SetSelfPlayerEvent 设置）
        private string _selfPlayerID = PlayerID.Offline;

        /// <summary>
        /// 获取自身 PlayerID（connectionId）
        /// 服务器端：由 MyNetworkManager.OnServerConnect 设置
        /// 客户端：由 SetSelfPlayerHandler 从服务器发来的事件中设置
        /// </summary>
        public string SelfPlayerID
        {
            get { return _selfPlayerID; }
        }

        /// <summary>
        /// 设置自身 PlayerID
        /// </summary>
        public void SetSelfPlayerID(string playerID)
        {
            if (string.IsNullOrEmpty(playerID) || playerID == PlayerID.Offline || playerID == "Client")
            {
                Debug.LogWarning($"[PlayerManager] SetSelfPlayerID: 无效的 ID: {playerID}");
                return;
            }

            if (_selfPlayerID != playerID)
            {
                string oldID = _selfPlayerID;
                _selfPlayerID = playerID;
                Debug.Log($"[PlayerManager] SelfPlayerID 已设置: {oldID} -> {playerID}");
                OnSelfPlayerIDChanged?.Invoke(playerID);
            }
        }

        // 所有玩家的信息
        private readonly Dictionary<string, PlayerInfo> _allPlayers = new();

        // 网络引用
        private MyNetworkManager _networkManager;

        /// <summary>由 MyNetworkManager 在 Awake 中反向注入</summary>
        public void SetNetworkManager(MyNetworkManager networkManager)
        {
            _networkManager = networkManager;
        }

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

        // ==================== 事件 ====================

        // 玩家数量变化事件
        public event Action<int> OnPlayerCountChanged;

        // 玩家信息更新事件（用于刷新 UI）
        public event Action<string, PlayerInfo> OnPlayerInfoUpdated;

        // 被踢出事件
        public event Action OnKickedFromRoom;

        // 自身 PlayerID 变化事件
        public event Action<string> OnSelfPlayerIDChanged;

        // 缓存的上一帧 PlayerID，用于检测变化
        private string _cachedSelfPlayerID = PlayerID.Offline;

        // ==================== IWargameManager 实现 ====================

        public void Start()
        {
            Instance = this;

            // 创建 Handler 实例并保存引用
            CreateHandlers();

            // 订阅网络事件
            SubscribeEvents();

            Debug.Log("[PlayerManager] 启动完成，等待网络初始化");
        }

        public void Update(float deltaTime)
        {

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
        }

        private void SubscribeEvents()
        {
            // 订阅服务器端收到的客户端请求
            EventBusHub.Instance.Subscribe<SetTeamRequestEvent>(_setTeamHandler);
            EventBusHub.Instance.Subscribe<SetColorRequestEvent>(_setColorHandler);
            EventBusHub.Instance.Subscribe<SetSpawnRequestEvent>(_setSpawnHandler);
            EventBusHub.Instance.Subscribe<ToggleReadyRequestEvent>(_toggleReadyHandler);
            EventBusHub.Instance.Subscribe<KickPlayerRequestEvent>(_kickPlayerHandler);

            // 订阅网络事件（所有客户端收到）
            EventBusHub.Instance.Subscribe<AddPlayerEvent>(_addPlayerHandler);
            EventBusHub.Instance.Subscribe<RemovePlayerEvent>(_removePlayerHandler);
            EventBusHub.Instance.Subscribe<UpdatePlayerInfoEvent>(_updatePlayerInfoHandler);
            EventBusHub.Instance.Subscribe<KickedFromRoomEvent>(_kickedFromRoomHandler);
            EventBusHub.Instance.Subscribe<SetSelfPlayerEvent>(_setSelfPlayerHandler);
        }

        // ==================== 玩家管理 ====================

        public void ClearPlayers()
        {
            _allPlayers.Clear();
        }

        /// <summary>
        /// 处理服务器端新连接（由 MyNetworkManager.OnServerConnect 调用）
        /// 封装玩家注册 + 事件同步全套逻辑
        /// </summary>
        public void HandleServerConnect(NetworkConnectionToClient conn)
        {
            string playerID = conn.connectionId.ToString();
            bool isHost = playerID == PlayerID.Host;
            string playerName = isHost ? "旅行者" : ("玩家" + conn.connectionId);

            RegisterPlayer(playerID, playerName, TeamType.A,
                          GetNextAvailableColorPublic(), isHost, conn.address, conn);

            // 1. 告诉新客户端它自己的 PlayerID
            EventBusHub.Instance.PublishToPlayer(playerID, new SetSelfPlayerEvent { TargetPlayerID = playerID });

            // Host 自己也设置 ID（服务器不会收到发给自己的 TargetRpc）
            if (isHost) SetSelfPlayerID(playerID);

            // 2. 通知新客户端所有已有玩家
            foreach (var player in GetAllPlayers())
            {
                if (player.PlayerID != playerID)
                    EventBusHub.Instance.PublishToPlayer(playerID, new AddPlayerEvent { PlayerInfo = player });
            }

            // 3. 广播新玩家加入
            var newPlayerInfo = GetPlayerInfo(playerID);
            if (newPlayerInfo != null)
                EventBusHub.Instance.Send(new AddPlayerEvent { PlayerInfo = newPlayerInfo });
        }

        /// <summary>
        /// 处理服务器端断开连接（由 MyNetworkManager.OnServerDisconnect 调用）
        /// </summary>
        public void HandleServerDisconnect(NetworkConnectionToClient conn)
        {
            string playerID = conn.connectionId.ToString();
            UnregisterPlayer(playerID);
            EventBusHub.Instance.Send(new RemovePlayerEvent { TargetPlayerID = playerID });
        }

        public void HandleClientDisconnect()
        {
            ClearPlayers();
        }

        /// <summary>
        /// 注册玩家（内部方法，外部通过 HandleServerConnect 调用）
        /// </summary>
        private void RegisterPlayer(string playerID, string playerName, TeamType team, PlayerColor color,
                                  bool isHost, string ipAddress, NetworkConnectionToClient conn = null)
        {
            if (_allPlayers.ContainsKey(playerID))
            {
                Debug.LogWarning($"[PlayerManager] 玩家已存在: {playerID}");
                return;
            }

            var playerInfo = new PlayerInfo
            {
                PlayerID = playerID,
                PlayerName = playerName,
                Team = team,
                Color = color,
                SpawnPosition = SpawnPositionType.Random,
                IPAddress = ipAddress,
                IsHost = isHost,
                IsReady = false,
                IsConnected = true,
                connectionToClient = conn
            };

            _allPlayers[playerID] = playerInfo;

            Debug.Log($"[PlayerManager] 注册玩家: {playerID} ({playerName}) - 队伍: {team} - 颜色: {color} - 房主: {isHost} - 是否是自己: {IsSelfPlayer(playerID)}");

            OnPlayerCountChanged?.Invoke(_allPlayers.Count);
            OnPlayerInfoUpdated?.Invoke(playerID, playerInfo);
        }

        /// <summary>
        /// 注销玩家
        /// </summary>
        public void UnregisterPlayer(string playerID)
        {
            if (!_allPlayers.ContainsKey(playerID))
            {
                Debug.LogWarning($"[PlayerManager] 尝试移除不存在的玩家: {playerID}");
                return;
            }

            var info = _allPlayers[playerID];
            string playerName = info.PlayerName;

            _allPlayers.Remove(playerID);

            Debug.Log($"[PlayerManager] 移除玩家: {playerID} ({playerName})");

            OnPlayerCountChanged?.Invoke(_allPlayers.Count);
        }

        /// <summary>
        /// 更新玩家信息
        /// </summary>
        public void UpdatePlayerInfo(string playerID, PlayerInfo newInfo)
        {
            if (_allPlayers.TryGetValue(playerID, out var existingInfo))
            {
                var conn = existingInfo.connectionToClient;
                newInfo.connectionToClient = conn;

                _allPlayers[playerID] = newInfo;
                OnPlayerInfoUpdated?.Invoke(playerID, newInfo);
            }
        }

        public PlayerInfo GetSelfPlayerInfo()
        {
            return GetPlayerInfo(SelfPlayerID);
        }

        public PlayerInfo GetPlayerInfo(string playerID)
        {
            _allPlayers.TryGetValue(playerID, out var info);

            if (info == null)
            {
                Debug.LogWarning($"[PlayerManager] 未找到玩家: {playerID}");
            }

            return info;
        }

        public IEnumerable<PlayerInfo> GetAllPlayers()
        {
            return _allPlayers.Values;
        }

        public int GetPlayerCount() => _allPlayers.Count;

        // ==================== 属性修改 ====================

        public void SetPlayerTeam(string playerID, TeamType team)
        {
            if (_allPlayers.TryGetValue(playerID, out var info))
            {
                var oldTeam = info.Team;
                info.Team = team;
                _allPlayers[playerID] = info;
                Debug.Log($"[PlayerManager] 玩家 {playerID} 更换队伍: {oldTeam} -> {team}");
                OnPlayerInfoUpdated?.Invoke(playerID, info);
            }
        }

        public void SetPlayerColor(string playerID, PlayerColor color)
        {
            if (_allPlayers.TryGetValue(playerID, out var info))
            {
                var oldColor = info.Color;
                info.Color = color;
                _allPlayers[playerID] = info;
                Debug.Log($"[PlayerManager] 玩家 {playerID} 更换颜色: {oldColor} -> {color}");
                OnPlayerInfoUpdated?.Invoke(playerID, info);
            }
        }

        public void SetPlayerSpawnPosition(string playerID, SpawnPositionType spawnPosition)
        {
            if (_allPlayers.TryGetValue(playerID, out var info))
            {
                var oldSpawn = info.SpawnPosition;
                info.SpawnPosition = spawnPosition;
                _allPlayers[playerID] = info;
                Debug.Log($"[PlayerManager] 玩家 {playerID} 更换出生点: {oldSpawn} -> {spawnPosition}");
                OnPlayerInfoUpdated?.Invoke(playerID, info);
            }
        }

        public void ToggleReady(string playerID)
        {
            if (_allPlayers.TryGetValue(playerID, out var info))
            {
                info.IsReady = !info.IsReady;
                _allPlayers[playerID] = info;
                Debug.Log($"[PlayerManager] 玩家 {playerID} 准备状态: {info.IsReady}");
                OnPlayerInfoUpdated?.Invoke(playerID, info);
            }
        }

        // ==================== 队伍管理 ====================

        public List<string> GetPlayersInTeam(TeamType team)
        {
            var result = new List<string>();
            foreach (var kvp in _allPlayers)
            {
                if (kvp.Value.Team == team)
                {
                    result.Add(kvp.Key);
                }
            }
            return result;
        }

        public bool IsSameTeam(string playerID1, string playerID2)
        {
            var info1 = GetPlayerInfo(playerID1);
            var info2 = GetPlayerInfo(playerID2);

            if (info1 == null || info2 == null) return false;
            return info1.Team == info2.Team;
        }

        public bool IsEnemy(string playerID1, string playerID2)
        {
            return !IsSameTeam(playerID1, playerID2);
        }

        // ==================== 颜色管理 ====================

        public Color GetPlayerColor(string playerID)
        {
            var info = GetPlayerInfo(playerID);
            return info?.Color.ToColor() ?? Color.white;
        }

        public PlayerColor GetNextAvailableColorPublic()
        {
            return GetNextAvailableColor();
        }

        private PlayerColor GetNextAvailableColor()
        {
            int usedCount = _allPlayers.Count;
            var colors = (PlayerColor[])Enum.GetValues(typeof(PlayerColor));
            var color = colors[usedCount % colors.Length];
            Debug.Log($"[PlayerManager] 分配颜色: {color} (第 {usedCount + 1} 位玩家)");
            return color;
        }

        // ==================== 查询方法 ====================

        public bool IsSelfPlayer(string playerID)
        {
            return playerID == SelfPlayerID;
        }

        public bool IsOwnUnit(Unit unit)
        {
            if (unit == null) return false;
            var identity = unit.GetUnitComponent<UnitIdentity>();
            return identity != null && identity.OwnerPlayerID.ToString() == SelfPlayerID;
        }

        public bool CanControlUnit(Unit unit)
        {
            if (unit == null) return false;
            var identity = unit.GetUnitComponent<UnitIdentity>();
            if (identity == null) return false;

            if (identity.OwnerPlayerID.ToString() == SelfPlayerID) return true;
            return IsSameTeam(SelfPlayerID, identity.OwnerPlayerID.ToString());
        }

        // ==================== 调试 ====================

        public void DebugPrintAllPlayers()
        {
            Debug.Log($"[PlayerManager] ========== 玩家列表 ({_allPlayers.Count}人) ==========");
            Debug.Log($"[PlayerManager]   我的ID: {SelfPlayerID}");
            Debug.Log($"[PlayerManager]   我的信息: {(GetSelfPlayerInfo() != null ? GetSelfPlayerInfo().PlayerName : "未找到")}");
            foreach (var kvp in _allPlayers)
            {
                var info = kvp.Value;
                Debug.Log($"[PlayerManager]   {(IsSelfPlayer(info.PlayerID) ? "→ [我] " : "   ")}ID: {info.PlayerID} | 名称: {info.PlayerName} | 队伍: {info.Team} | 颜色: {info.Color} | 准备: {info.IsReady} | 房主: {info.IsHost} | IP: {info.IPAddress}");
            }
            Debug.Log("[PlayerManager] ==================================");
        }

        // ==================== 清理 ====================

        public void Cleanup()
        {
            // 使用保存的引用精准取消订阅
            EventBusHub.Instance.Unsubscribe<SetTeamRequestEvent>(_setTeamHandler);
            EventBusHub.Instance.Unsubscribe<SetColorRequestEvent>(_setColorHandler);
            EventBusHub.Instance.Unsubscribe<SetSpawnRequestEvent>(_setSpawnHandler);
            EventBusHub.Instance.Unsubscribe<ToggleReadyRequestEvent>(_toggleReadyHandler);
            EventBusHub.Instance.Unsubscribe<KickPlayerRequestEvent>(_kickPlayerHandler);
            EventBusHub.Instance.Unsubscribe<AddPlayerEvent>(_addPlayerHandler);
            EventBusHub.Instance.Unsubscribe<RemovePlayerEvent>(_removePlayerHandler);
            EventBusHub.Instance.Unsubscribe<UpdatePlayerInfoEvent>(_updatePlayerInfoHandler);
            EventBusHub.Instance.Unsubscribe<KickedFromRoomEvent>(_kickedFromRoomHandler);
            EventBusHub.Instance.Unsubscribe<SetSelfPlayerEvent>(_setSelfPlayerHandler);

            Instance = null;
        }


        // ==================== 内部事件处理器 ====================

        /// <summary>
        /// 更新请求处理器模板 — CanHandle（服务器端检查）+ Handle（修改属性 → 广播更新）
        /// 子类只需实现 GetPlayerID 和 ApplyChange
        /// </summary>
        private abstract class PlayerInfoUpdateRequestHandler<T> : IEventHandler<T> where T : BaseEvent
        {
            protected readonly PlayerManager _mgr;
            protected PlayerInfoUpdateRequestHandler(PlayerManager manager) => _mgr = manager;

            public bool CanHandle(T evt) => NetworkServer.active;

            public void Handle(T evt)
            {
                string pid = GetPlayerID(evt);
                Debug.Log($"[PlayerManager.{GetType().Name}] 收到请求: PlayerID={pid}");

                ApplyChange(evt, pid);

                var info = _mgr.GetPlayerInfo(pid);
                if (info != null)
                    EventBusHub.Instance.Send(new UpdatePlayerInfoEvent { UpdatedInfo = info });
                else
                    Debug.LogWarning($"[PlayerManager.{GetType().Name}] 未找到玩家: PlayerID={pid}");
            }

            protected abstract string GetPlayerID(T evt);
            protected abstract void ApplyChange(T evt, string playerID);
        }

        // --- 服务器端请求处理器 ---

        private class SetTeamHandler : PlayerInfoUpdateRequestHandler<SetTeamRequestEvent>
        {
            public SetTeamHandler(PlayerManager m) : base(m) { }
            protected override string GetPlayerID(SetTeamRequestEvent e) => e.TargetPlayerID;
            protected override void ApplyChange(SetTeamRequestEvent e, string pid) => _mgr.SetPlayerTeam(pid, e.Team);
        }

        private class SetColorHandler : PlayerInfoUpdateRequestHandler<SetColorRequestEvent>
        {
            public SetColorHandler(PlayerManager m) : base(m) { }
            protected override string GetPlayerID(SetColorRequestEvent e) => e.TargetPlayerID;
            protected override void ApplyChange(SetColorRequestEvent e, string pid) => _mgr.SetPlayerColor(pid, e.Color);
        }

        private class SetSpawnHandler : PlayerInfoUpdateRequestHandler<SetSpawnRequestEvent>
        {
            public SetSpawnHandler(PlayerManager m) : base(m) { }
            protected override string GetPlayerID(SetSpawnRequestEvent e) => e.PlayerID;
            protected override void ApplyChange(SetSpawnRequestEvent e, string pid) => _mgr.SetPlayerSpawnPosition(pid, e.SpawnPosition);
        }

        private class ToggleReadyHandler : PlayerInfoUpdateRequestHandler<ToggleReadyRequestEvent>
        {
            public ToggleReadyHandler(PlayerManager m) : base(m) { }
            protected override string GetPlayerID(ToggleReadyRequestEvent e) => e.TargetPlayerID;
            protected override void ApplyChange(ToggleReadyRequestEvent e, string pid) => _mgr.ToggleReady(pid);
        }

        private class KickPlayerHandler : IEventHandler<KickPlayerRequestEvent>
        {
            private readonly PlayerManager _mgr;
            public KickPlayerHandler(PlayerManager m) => _mgr = m;
            public bool CanHandle(KickPlayerRequestEvent e) => NetworkServer.active;

            public void Handle(KickPlayerRequestEvent e)
            {
                var info = _mgr.GetPlayerInfo(e.TargetPlayerID);
                if (info == null) return;

                if (info.connectionToClient != null)
                {
                    EventBusHub.Instance.PublishToPlayer(e.TargetPlayerID, new KickedFromRoomEvent());
                    info.connectionToClient.Disconnect();
                }

                _mgr.UnregisterPlayer(e.TargetPlayerID);
                EventBusHub.Instance.Send(new RemovePlayerEvent { TargetPlayerID = e.TargetPlayerID });
            }
        }

        // --- 网络同步事件处理器 ---

        private class AddPlayerHandler : IEventHandler<AddPlayerEvent>
        {
            private readonly PlayerManager _mgr;
            public AddPlayerHandler(PlayerManager m) => _mgr = m;
            public bool CanHandle(AddPlayerEvent e) => e.Source == EventSource.Network;

            public void Handle(AddPlayerEvent e)
            {
                if (_mgr.GetPlayerInfo(e.PlayerInfo.PlayerID) != null) return;
                _mgr.RegisterPlayer(e.PlayerInfo.PlayerID, e.PlayerInfo.PlayerName,
                                   e.PlayerInfo.Team, e.PlayerInfo.Color,
                                   e.PlayerInfo.IsHost, e.PlayerInfo.IPAddress);
            }
        }

        private class RemovePlayerHandler : IEventHandler<RemovePlayerEvent>
        {
            private readonly PlayerManager _mgr;
            public RemovePlayerHandler(PlayerManager m) => _mgr = m;
            public bool CanHandle(RemovePlayerEvent e) => e.Source == EventSource.Network;
            public void Handle(RemovePlayerEvent e) => _mgr.UnregisterPlayer(e.TargetPlayerID);
        }

        private class UpdatePlayerInfoHandler : IEventHandler<UpdatePlayerInfoEvent>
        {
            private readonly PlayerManager _mgr;
            public UpdatePlayerInfoHandler(PlayerManager m) => _mgr = m;
            public bool CanHandle(UpdatePlayerInfoEvent e) => e.Source == EventSource.Network;
            public void Handle(UpdatePlayerInfoEvent e) => _mgr.UpdatePlayerInfo(e.UpdatedInfo.PlayerID, e.UpdatedInfo);
        }

        private class KickedFromRoomHandler : IEventHandler<KickedFromRoomEvent>
        {
            private readonly PlayerManager _mgr;
            public KickedFromRoomHandler(PlayerManager m) => _mgr = m;
            public bool CanHandle(KickedFromRoomEvent e) => e.Source == EventSource.Network;

            public void Handle(KickedFromRoomEvent e)
            {
                _mgr._networkManager?.MarkAsKicked();
                _mgr.OnKickedFromRoom?.Invoke();
            }
        }

        private class SetSelfPlayerHandler : IEventHandler<SetSelfPlayerEvent>
        {
            private readonly PlayerManager _mgr;
            public SetSelfPlayerHandler(PlayerManager m) => _mgr = m;
            public bool CanHandle(SetSelfPlayerEvent e) => e.Source == EventSource.Network;
            public void Handle(SetSelfPlayerEvent e) => _mgr.SetSelfPlayerID(e.TargetPlayerID);
        }
    }
}



