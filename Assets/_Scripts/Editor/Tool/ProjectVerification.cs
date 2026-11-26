using System;
using System.Collections.Generic;
using System.IO;
using GIC.Battle;
using GIC.Data;
using GIC.Framework;
using UnityEditor;
using UnityEngine;

namespace GIC.Editor
{
    /// <summary>Batch and menu entry points for the same regression checks.</summary>
    public static class ProjectVerification
    {
        public const string SuccessMarker = "[GIC.Verify] PASS";

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("RunBatch requires -batchmode");
            EditorApplication.Exit(RunChecks() ? 0 : 1);
        }

        [MenuItem("Tools/TG/工程回归检查")]
        public static void RunMenu() => RunChecks();

        private static bool RunChecks()
        {
            try
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play Mode before running regressions");
                string buildError = GicBuildPreflight.Validate();
                Require(buildError == null, buildError);
                VerifyBattleStartExchange();
                VerifyPendingBattleLaunch();
                VerifyEventLifecycle();
                VerifySaveCopies();
                var frameSync = ReplayRunner.FrameSyncPipelineSelfTest();
                Debug.Log(frameSync);
                Require(!frameSync.Contains("FAIL") && frameSync.Contains("PASS"), "Frame sync regression failed");
                var replay = ReplayRunner.SelfTest();
                Debug.Log(replay);
                Require(!replay.Contains("FAIL") && replay.Contains("PASS"), "Replay regression failed");
                Debug.Log(SuccessMarker);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[GIC.Verify] FAIL {e}");
                return false;
            }
        }

        public static void VerifyBattleStartExchange()
        {
            string[] seats = { "1", "2" };
            var exchange = new BattleStartExchange("match", seats);
            seats[1] = "3"; // The exchange owns its roster, not the caller's array.
            var online = new HashSet<string> { "1", "2", "3" };
            bool IsOnline(string id) => online.Contains(id);
            ClientDeckEvent Deck(string id, string matchId = "match")
                => new ClientDeckEvent { MatchId = matchId, SourcePlayerID = id };
            ClientBattleReadyEvent Ready(string id, string matchId = "match")
                => new ClientBattleReadyEvent { MatchId = matchId, SourcePlayerID = id };

            exchange.MarkDisconnected("3"); // An unlisted connection leaving is not a launch failure.
            Require(exchange.CanContinue("match", IsOnline) && !exchange.CanContinue("old", IsOnline),
                "A fresh launch could not enter scene loading with decks and ready messages pending");
            Require(!exchange.TryAcceptDeck(Deck("1", "old")) && !exchange.TryMarkReady(Ready("1", "old"))
                && !exchange.TryAcceptDeck(Deck("3")) && !exchange.TryMarkReady(Ready("3")),
                "Stale or unlisted seat advanced the battle exchange");
            var p1Deck = Deck("1");
            p1Deck.Entries.Add(new HandCard(new CardId(ItemName.Mora), 0)); // Depleted deck entry is a no-op.
            Require(exchange.TryMarkReady(Ready("1")) && exchange.TryAcceptDeck(Deck("2"))
                && !exchange.IsReady("match", IsOnline, out bool failed) && !failed,
                "Partial deck/ready exchange did not keep waiting");
            Require(exchange.TryAcceptDeck(p1Deck) && !exchange.TryAcceptDeck(Deck("1"))
                && exchange.GetDeck("1") == p1Deck && exchange.TryMarkReady(Ready("2"))
                && !exchange.TryMarkReady(Ready("2")), "Deck/ready messages were not accepted exactly once");
            Require(exchange.IsReady("match", IsOnline, out _) && !exchange.TryStart("other", IsOnline),
                "All ready seats must only start the matching launch");
            online.Remove("2");
            Require(!exchange.IsReady("match", IsOnline, out failed) && failed && !exchange.TryStart("match", IsOnline),
                "A ready player disconnected before start but was still admitted");
            online.Add("2");
            Require(!exchange.CanContinue("match", IsOnline), "A reused seat revived an aborted launch");

            var interrupted = new BattleStartExchange("interrupted", new[] { "2" });
            interrupted.MarkDisconnected("2");
            Require(!interrupted.CanContinue("interrupted", IsOnline)
                && !interrupted.TryMarkReady(Ready("2", "interrupted"))
                && !interrupted.TryAcceptDeck(Deck("2", "interrupted")),
                "Disconnect during scene handoff still accepted late deck/ready messages");

            var next = new BattleStartExchange("next", new[] { "1" });
            Require(!next.TryMarkReady(Ready("1")) && !next.TryAcceptDeck(Deck("1"))
                && next.TryMarkReady(Ready("1", "next")) && next.TryAcceptDeck(Deck("1", "next"))
                && next.TryStart("next", IsOnline) && !next.TryStart("next", IsOnline),
                "A new launch reused old state or completed twice");
        }

        public static void VerifyPendingBattleLaunch()
        {
            var previous = BattleLaunchConfig.Current;
            try
            {
                var failed = new BattleLaunchConfig();
                var next = new BattleLaunchConfig();
                BattleLaunchConfig.Prepare(failed);
                Require(BattleLaunchConfig.CancelPending(failed) && BattleLaunchConfig.Current == null,
                    "Failed scene switch left a pending battle launch behind");

                BattleLaunchConfig.Prepare(next);
                Require(!BattleLaunchConfig.CancelPending(failed) && BattleLaunchConfig.Take() == next
                    && !BattleLaunchConfig.CancelPending(next) && BattleLaunchConfig.Take() == null,
                    "Old transition changed the new launch or a config was consumed twice");
            }
            finally { BattleLaunchConfig.Prepare(previous); }
        }

        public static void VerifyEventLifecycle()
        {
            foreach (bool immediate in new[] { true, false })
            {
                var bus = new LocalEventBus();
                var calls = new List<string>();
                var panel = new object();
                var reopened = new TraceHandler(10, () => calls.Add("reopened"));
                bus.Subscribe(new TraceHandler(20, () =>
                {
                    calls.Add("close");
                    bus.UnsubscribeAllForOwner(panel);
                    bus.Subscribe(reopened, panel);
                }), panel);
                bus.Subscribe(new TraceHandler(10, () => calls.Add("stale")), panel);
                bus.Subscribe(new TraceHandler(5, () => calls.Add("tail")));

                bus.Send(new BaseEvent { Immediate = immediate });
                bus.Tick();
                bus.Tick();
                Require(string.Join(",", calls) == "close,tail", $"Dispatch skipped or invoked a closed handler (immediate={immediate})");
                calls.Clear();
                bus.Send(new BaseEvent { Immediate = immediate });
                bus.Tick();
                bus.Tick();
                Require(string.Join(",", calls) == "reopened,tail", $"Reopened panel did not receive exactly one callback (immediate={immediate})");
            }
        }

        public static void VerifySaveCopies()
        {
            var directory = Path.Combine(Path.GetTempPath(), "gic-save-check-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "test.json");
                const string oldSave = "{\"saveVersion\":12,\"playerName\":\"backup\"}";
                const string newerSave = "{\"saveVersion\":13,\"futureField\":true}";
                var store = new PlayerSaveFile(path, 12);
                Require(store.Read(false, out _) == PlayerSaveFile.State.Missing, "Missing save was not recognized");
                Require(store.TryWrite(oldSave, false), "Initial save failed");
                Require(store.TryWrite("{\"saveVersion\":12}", true), "Backup rotation failed");
                Require(store.Read(true, out var backup) == PlayerSaveFile.State.Valid && backup.playerName == "backup",
                    "Previous primary save was not backed up");
                string originalBackup = File.ReadAllText(path + ".backup");
                File.WriteAllText(path, "{}");
                Require(store.Read(false, out _) == PlayerSaveFile.State.Invalid, "Invalid save was misclassified as outdated");
                Require(store.TryWrite(oldSave, false) && File.ReadAllText(path + ".backup") == originalBackup,
                    "Restoring a valid backup replaced it with a corrupt primary");
                File.WriteAllText(path, "{}");
                Require(store.TryWrite(oldSave, true) && File.ReadAllText(path + ".backup") == originalBackup,
                    "A newly corrupted primary was rotated over a valid backup");

                File.WriteAllText(path, newerSave);
                Require(store.Read(false, out _) == PlayerSaveFile.State.Newer && !store.TryWrite(oldSave, true)
                        && File.ReadAllText(path) == newerSave && File.ReadAllText(path + ".backup") == originalBackup,
                    "Version downgrade overwrote the primary or backup");
                File.WriteAllText(path, oldSave);
                File.WriteAllText(path + ".backup", newerSave);
                Require(!store.TryWrite(oldSave, true) && File.ReadAllText(path + ".backup") == newerSave,
                    "Version downgrade overwrote the newer backup");
            }
            finally { Directory.Delete(directory, true); }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class TraceHandler : IEventHandler<BaseEvent>
        {
            public int Priority { get; }
            private readonly Action _handle;
            public TraceHandler(int priority, Action handle) { Priority = priority; _handle = handle; }
            public bool CanHandle(BaseEvent evt) => true;
            public void Handle(BaseEvent evt) => _handle();
        }
    }
}
