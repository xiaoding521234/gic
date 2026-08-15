// PlayerManager.cs - 玩家数据管理器（名册数据层：玩家增删改查 + 自身 PlayerID）
using System;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using GIC.Battle;
using GIC.Data;
using GIC.Tool;
namespace GIC.Framework
{


    /// <summary>
    /// 玩家数据管理器：持有全体玩家名册与自身 PlayerID，负责数据增删改查与变更事件。
    /// 联机房间流程（连接/断开、网络事件处理、颜色分配）在 RoomManager。
    /// </summary>
    [Component]
    public class PlayerManager
    {
        // 自身 PlayerID（由服务器通过 SetSelfPlayerEvent 设置，RoomManager.SetSelfPlayerHandler 调用）
        private string _selfPlayerID = PlayerID.Offline;

        // 所有玩家的信息
        private readonly Dictionary<string, PlayerInfo> _allPlayers = new();

        /// <summary>
        /// 获取自身 PlayerID（connectionId）
        /// 服务器端：由 RoomManager.HandleServerConnect 设置
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
                GICLog.Warn($"[PlayerManager] SetSelfPlayerID: 无效的 ID: {playerID}");
                return;
            }

            if (_selfPlayerID != playerID)
            {
                string oldID = _selfPlayerID;
                _selfPlayerID = playerID;
                GICLog.Info($"[PlayerManager] SelfPlayerID 已设置: {oldID} -> {playerID}");
                OnSelfPlayerIDChanged?.Invoke(playerID);
            }
        }

        // ==================== 事件 ====================

        // 玩家数量变化事件
        public event Action<int> OnPlayerCountChanged;

        // 玩家信息更新事件（用于刷新 UI）
        public event Action<string, PlayerInfo> OnPlayerInfoUpdated;

        // 自身 PlayerID 变化事件
        public event Action<string> OnSelfPlayerIDChanged;

        // ==================== 玩家名册管理 ====================

        public void ClearPlayers()
        {
            _allPlayers.Clear();
        }

        /// <summary>
        /// 注册玩家（由 RoomManager 的连接流程与 AddPlayerHandler 调用）
        /// </summary>
        public void RegisterPlayer(string playerID, string playerName, TeamType team, PlayerColor color,
                                  bool isHost, string ipAddress, NetworkConnectionToClient conn = null)
        {
            if (_allPlayers.ContainsKey(playerID))
            {
                GICLog.Warn($"[PlayerManager] 玩家已存在: {playerID}");
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

            GICLog.Info($"[PlayerManager] 注册玩家: {playerID} ({playerName}) - 队伍: {team} - 颜色: {color} - 房主: {isHost} - 是否是自己: {IsSelfPlayer(playerID)}");

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
                GICLog.Warn($"[PlayerManager] 尝试移除不存在的玩家: {playerID}");
                return;
            }

            var info = _allPlayers[playerID];
            string playerName = info.PlayerName;

            _allPlayers.Remove(playerID);

            GICLog.Info($"[PlayerManager] 移除玩家: {playerID} ({playerName})");

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

        // ==================== 查询方法 ====================

        public PlayerInfo GetSelfPlayerInfo()
        {
            return GetPlayerInfo(SelfPlayerID);
        }

        public PlayerInfo GetPlayerInfo(string playerID)
        {
            _allPlayers.TryGetValue(playerID, out var info);

            if (info == null)
            {
                GICLog.Warn($"[PlayerManager] 未找到玩家: {playerID}");
            }

            return info;
        }

        public IEnumerable<PlayerInfo> GetAllPlayers()
        {
            return _allPlayers.Values;
        }

        public int GetPlayerCount() => _allPlayers.Count;

        public bool IsSelfPlayer(string playerID)
        {
            return playerID == SelfPlayerID;
        }

        // ==================== 属性修改 ====================

        public void SetPlayerName(string playerID, string newName)
        {
            if (string.IsNullOrEmpty(newName)) return;
            if (_allPlayers.TryGetValue(playerID, out var info))
            {
                info.PlayerName = newName;
                _allPlayers[playerID] = info;
                GICLog.Info($"[PlayerManager] 玩家 {playerID} 设置名称: {newName}");
                OnPlayerInfoUpdated?.Invoke(playerID, info);
            }
        }

        public void SetPlayerTeam(string playerID, TeamType team)
        {
            if (_allPlayers.TryGetValue(playerID, out var info))
            {
                var oldTeam = info.Team;
                info.Team = team;
                _allPlayers[playerID] = info;
                GICLog.Info($"[PlayerManager] 玩家 {playerID} 更换队伍: {oldTeam} -> {team}");
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
                GICLog.Info($"[PlayerManager] 玩家 {playerID} 更换颜色: {oldColor} -> {color}");
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
                GICLog.Info($"[PlayerManager] 玩家 {playerID} 更换出生点: {oldSpawn} -> {spawnPosition}");
                OnPlayerInfoUpdated?.Invoke(playerID, info);
            }
        }

        public void ToggleReady(string playerID)
        {
            if (_allPlayers.TryGetValue(playerID, out var info))
            {
                info.IsReady = !info.IsReady;
                _allPlayers[playerID] = info;
                GICLog.Info($"[PlayerManager] 玩家 {playerID} 准备状态: {info.IsReady}");
                OnPlayerInfoUpdated?.Invoke(playerID, info);
            }
        }

        // ==================== 队伍查询 ====================

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

        // ==================== 颜色查询 ====================

        public Color GetPlayerColor(string playerID)
        {
            var info = GetPlayerInfo(playerID);
            return info?.Color.ToColor() ?? Color.white;
        }

        // ==================== 调试 ====================

        public void DebugPrintAllPlayers()
        {
            GICLog.Info($"[PlayerManager] ========== 玩家列表 ({_allPlayers.Count}人) ==========");
            GICLog.Info($"[PlayerManager]   我的ID: {SelfPlayerID}");
            GICLog.Info($"[PlayerManager]   我的信息: {(GetSelfPlayerInfo() != null ? GetSelfPlayerInfo().PlayerName : "未找到")}");
            foreach (var kvp in _allPlayers)
            {
                var info = kvp.Value;
                GICLog.Info($"[PlayerManager]   {(IsSelfPlayer(info.PlayerID) ? "→ [我] " : "   ")}ID: {info.PlayerID} | 名称: {info.PlayerName} | 队伍: {info.Team} | 颜色: {info.Color} | 准备: {info.IsReady} | 房主: {info.IsHost} | IP: {info.IPAddress}");
            }
            GICLog.Info("[PlayerManager] ==================================");
        }
    }
}
