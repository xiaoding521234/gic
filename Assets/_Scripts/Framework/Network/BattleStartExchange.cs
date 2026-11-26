using System;
using System.Collections.Generic;
using GIC.Data;

namespace GIC.Framework
{
    /// <summary>房主为一场对局冻结的远端座位；卡组和加载就绪可以按任意顺序到达。</summary>
    public sealed class BattleStartExchange
    {
        private enum State { Waiting, Started, Aborted }

        private sealed class RemoteSeat
        {
            public ClientDeckEvent Deck;
            public bool Ready;
        }

        private readonly Dictionary<string, RemoteSeat> _seats = new(StringComparer.Ordinal);
        private State _state;
        public string MatchId { get; }

        public BattleStartExchange(string matchId, IEnumerable<string> remotePlayerIds)
        {
            if (string.IsNullOrEmpty(matchId)) throw new ArgumentException("MatchId is required", nameof(matchId));
            MatchId = matchId;
            foreach (string id in remotePlayerIds) _seats.Add(id, new RemoteSeat());
        }

        public ClientDeckEvent GetDeck(string playerId)
            => _seats.TryGetValue(playerId, out var seat) ? seat.Deck : null;

        public bool TryAcceptDeck(ClientDeckEvent upload)
        {
            if (upload == null || !TryGetPendingSeat(upload.MatchId, upload.SourcePlayerID, out var seat) || seat.Deck != null
                || upload.Entries == null || upload.Entries.Exists(card => card == null || card.count < 0)) return false;
            seat.Deck = upload;
            return true;
        }

        public bool TryMarkReady(ClientBattleReadyEvent ready)
        {
            if (ready == null || !TryGetPendingSeat(ready.MatchId, ready.SourcePlayerID, out var seat) || seat.Ready) return false;
            seat.Ready = true;
            return true;
        }

        /// <summary>冻结座位掉线后，本次开局不可恢复，即使座位 ID 之后被复用。</summary>
        public void MarkDisconnected(string playerId)
        {
            if (_state == State.Waiting && playerId != null && _seats.ContainsKey(playerId)) _state = State.Aborted;
        }

        /// <summary>淡入、切场景、等待就绪均适用；未报卡组或就绪不会导致失败。</summary>
        public bool CanContinue(string matchId, Func<string, bool> isConnected)
        {
            if (_state != State.Waiting || matchId != MatchId) return false;
            foreach (var playerId in _seats.Keys)
            {
                if (isConnected(playerId)) continue;
                MarkDisconnected(playerId);
                return false;
            }
            return true;
        }

        public bool IsReady(string matchId, Func<string, bool> isConnected, out bool failed)
        {
            failed = !CanContinue(matchId, isConnected);
            if (failed) return false;
            foreach (var seat in _seats.Values)
                if (!seat.Ready || seat.Deck == null) return false;
            return true;
        }

        public bool TryStart(string matchId, Func<string, bool> isConnected)
        {
            if (!IsReady(matchId, isConnected, out _)) return false;
            _state = State.Started;
            return true;
        }

        private bool TryGetPendingSeat(string matchId, string playerId, out RemoteSeat seat)
        {
            seat = null;
            return _state == State.Waiting && matchId == MatchId && !string.IsNullOrEmpty(playerId)
                && _seats.TryGetValue(playerId, out seat);
        }
    }
}
