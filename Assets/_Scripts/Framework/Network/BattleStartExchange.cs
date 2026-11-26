using System;
using System.Collections.Generic;
using GIC.Data;

namespace GIC.Framework
{
    /// <summary>房主为一场对局冻结的远端座位；卡组和加载就绪可以按任意顺序到达。</summary>
    public sealed class BattleStartExchange
    {
        private sealed class RemoteSeat
        {
            public ClientDeckEvent Deck;
            public bool Ready;
        }

        private readonly Dictionary<string, RemoteSeat> _seats = new(StringComparer.Ordinal);
        private bool _started;
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
            if (!TryGetPendingSeat(upload.MatchId, upload.SourcePlayerID, out var seat) || seat.Deck != null
                || upload.Entries == null || upload.Entries.Exists(card => card == null || card.count < 0)) return false;
            seat.Deck = upload;
            return true;
        }

        public bool TryMarkReady(ClientBattleReadyEvent ready)
        {
            if (!TryGetPendingSeat(ready.MatchId, ready.SourcePlayerID, out var seat) || seat.Ready) return false;
            seat.Ready = true;
            return true;
        }

        public bool IsReady(string matchId, Func<string, bool> isConnected, out bool failed)
        {
            failed = _started || matchId != MatchId;
            if (failed) return false;

            bool waiting = false;
            foreach (var entry in _seats)
            {
                if (!isConnected(entry.Key))
                {
                    failed = true;
                    return false;
                }
                waiting |= !entry.Value.Ready || entry.Value.Deck == null;
            }
            return !waiting;
        }

        public bool TryStart(string matchId, Func<string, bool> isConnected)
        {
            if (!IsReady(matchId, isConnected, out _)) return false;
            _started = true;
            return true;
        }

        private bool TryGetPendingSeat(string matchId, string playerId, out RemoteSeat seat)
        {
            seat = null;
            return !_started && matchId == MatchId && !string.IsNullOrEmpty(playerId)
                && _seats.TryGetValue(playerId, out seat);
        }
    }
}
