using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using GIC.Data;
using GIC.Framework;

namespace GIC.Battle
{


    /// <summary>
    /// 战斗回放 JSONL v4：记录开局规则、演算输入、表现命令、秒拍和终局 hash、结束拍号。
    /// 单机记录本地决策/上交窗口；联机只记录房主已消费的非空输入批，空拍由结束拍号补齐。
    /// </summary>
    public static class BattleRecorder
    {
        /// <summary>录制开关（replay 指令控制；默认 false）</summary>
        public static bool Enabled;

        private static string _path;
        private static StreamWriter _writer;
        private static int _lastCompletedTick;

        private static string ReplayDirectory => Path.Combine(Application.persistentDataPath, "Replays");

        /// <summary>本局确实有已打开的写入流，不等同于下局录制开关。</summary>
        public static bool IsRecording => _writer != null;

        /// <summary>当前录制文件路径（null=未在录制）</summary>
        public static string CurrentPath => IsRecording ? _path : null;

        // ==================== 录制入口（接线点各一处） ====================

        /// <summary>开局配置录制（BattleScreen 开局装配手牌注册后调）：开新文件写 header 行。
        /// 参数=演算复现所需的全部初始态（卡组构建上游不录——hands 即初始态终点）</summary>
        public static void CaptureSetup(int seed, string mapConfigName, int fogMode,
            List<BattlePlayerSetup> players, Dictionary<string, BattleCell> spawnCells,
            Dictionary<string, List<HandCard>> hands, bool isNetworked = false, string outputDirectory = null)
        {
            if (!Enabled) return;

            EndSession();
            try
            {
                var directory = outputDirectory ?? ReplayDirectory;
                Directory.CreateDirectory(directory);
                _path = Path.Combine(directory,
                    $"battle_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{seed}_{Guid.NewGuid():N}.jsonl"); // DETERMINISM-ALLOW: 诊断文件名，不入模拟
                _writer = new StreamWriter(new FileStream(_path, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
                _writer.AutoFlush = true;
                _lastCompletedTick = 0;
            }
            catch (Exception e)
            {
                StopWriter();
                GICLog.Warn($"[BattleRecorder] 无法打开录制：{e.Message}");
                return;
            }

            var line = new ReplayHeaderLine
            {
                seed = seed,
                mapName = mapConfigName,
                fogMode = fogMode,
                isNetworked = isNetworked,
                players = new List<ReplayPlayerLine>(),
            };
            foreach (var p in players)
            {
                BattleCell cell = default;
                spawnCells?.TryGetValue(p.PlayerId, out cell);
                var pl = new ReplayPlayerLine
                {
                    playerId = p.PlayerId,
                    team = (int)p.Team,
                    spawnX = cell.x,
                    spawnY = cell.y,
                    hand = hands != null && hands.TryGetValue(p.PlayerId, out var hand) ? hand : new List<HandCard>(),
                    isAI = p.IsAI ? 1 : 0,
                    aiDifficulty = (int)p.AIDifficulty,
                    moraIncomePercent = p.MoraIncomePercent,
                };
                line.players.Add(pl);
            }
            Append(line);
            if (_writer != null) GICLog.Info($"[BattleRecorder] 开录 → {Path.GetFileName(_path)}（{players.Count} 名玩家）");
        }

        /// <summary>AI 节拍批录制（RTSFlowController.RunAiBeat 脑批定稿后调）：v2 批内行动全带
        /// submitTick（=节拍 tick），回放器在 AI 节拍窗（%10）按 tick 喂回——不重跑任何脑；
        /// 空批不落行（空批零演算，无对应窗口动作）</summary>
        public static void CaptureActions(int turn, List<ActionData> actions)
        {
            if (_writer == null) return;
            if (actions == null || actions.Count == 0) return;
            Append(new ReplayActionsLine { turn = turn, actions = actions });
        }

        /// <summary>单机上交（含购买）；回放器在当拍确定性步骤之后按 submitTick 和行序消费。</summary>
        public static void CaptureSubmittedAction(ActionData action)
        {
            if (_writer == null || action == null) return;
            Append(new ReplaySubmitLine { action = action });
        }

        /// <summary>录房主实际消费的非空权威批，包含实际结算拍号。</summary>
        public static void CaptureInputBatch(InputBatchMessage batch)
        {
            if (_writer == null || batch.inputs.Count == 0) return;
            Append(new ReplayInputBatchLine { batch = batch });
        }

        /// <summary>Segment 命令流录制（BattleResolver.PushSegment 推送前调）——断言面本体</summary>
        public static void CaptureSegment(Segment segment)
        {
            if (_writer == null || segment == null) return;
            Append(new ReplaySegmentLine { turn = segment.turnNumber, segment = segment });
        }

        /// <summary>终局独立于秒拍采样，胜负成立时落下精确的状态 hash 和拍号。</summary>
        public static void CaptureGameOver(int winnerTeam, int finalTick, int stateHash)
        {
            if (_writer == null) return;
            CaptureCompletedTick(finalTick);
            Append(new ReplayOverLine
            {
                winnerTeam = winnerTeam,
                finalTick = finalTick,
                finalTurn = finalTick / BattleMetrics.TicksPerSecond,
                finalStateHash = stateHash.ToString(CultureInfo.InvariantCulture),
            });
            EndSession();
        }

        /// <summary>tick 状态 hash 录制（决策九十五确定性护栏四件套之一，docs/active/48 §2.2）——
        /// 每秒整拍随快照落一行：后置 LAN 联机双端比对漂移（王者荣耀同款定位手段）；单机回放
        /// 亦可用作「同 seed 同命令流=同 hash」的自证断言。旧回放器缺该行类型=跳过不判（缺省兼容）</summary>
        public static void CaptureTickHash(int tick, int hash)
        {
            if (_writer == null) return;
            Append(new ReplayTickLine { tick = tick, hash = hash });
        }

        public static void CaptureCompletedTick(int tick)
        {
            if (_writer != null) _lastCompletedTick = tick;
        }

        /// <summary>记录最后执行到的拍号，正常停录时不必为每个空输入拍写行。</summary>
        public static void EndSession()
        {
            if (_writer == null) return;
            Append(new ReplayEndLine { tick = _lastCompletedTick });
            StopWriter();
        }

        // ==================== 序列化 ====================

        private static void Append(object line)
        {
            try
            {
                _writer.WriteLine(JsonUtility.ToJson(line));
            }
            catch (Exception e)
            {
                GICLog.Warn($"[BattleRecorder] 落盘失败，录制中止：{e.Message}");
                StopWriter();
            }
        }

        private static void StopWriter()
        {
            var writer = _writer;
            _writer = null;
            _path = null;
            try { writer?.Dispose(); }
            catch (IOException e) { GICLog.Warn($"[BattleRecorder] 录制关闭失败：{e.Message}"); }
        }
    }

    // ==================== JSONL 行 DTO（回放器同文件反序列化） ====================

    [Serializable]
    public class ReplayHeaderLine
    {
        public string kind = "header";
        public const int ExactEndTickFormat = 3;
        public const int FinalStateHashFormat = 4;
        public const int CurrentFormat = FinalStateHashFormat;
        public int formatVersion = CurrentFormat;
        public int seed;
        public string mapName;
        public int fogMode;
        public bool isNetworked;
        public List<ReplayPlayerLine> players;
    }

    [Serializable]
    public class ReplayPlayerLine
    {
        public string playerId;
        public int team;
        public int spawnX;
        public int spawnY;
        public List<HandCard> hand;

        public int isAI;
        public int aiDifficulty = (int)AIDifficulty.Grandmaster;
        public int moraIncomePercent;
    }

    [Serializable]
    public class ReplayActionsLine
    {
        public string kind = "actions";
        public int turn;
        public List<ActionData> actions;
    }

    [Serializable]
    public class ReplaySubmitLine
    {
        /// <summary>玩家/配额脑上交或购买的行动（v2）——action.submitTick=回放消费 tick 锚；
        /// 行序（file 序）=发生时间序，同 tick 内按行序重演</summary>
        public string kind = "submit";
        public ActionData action;
    }

    [Serializable]
    public class ReplayInputBatchLine
    {
        public string kind = "inputBatch";
        public InputBatchMessage batch;
    }

    [Serializable]
    public class ReplaySegmentLine
    {
        public string kind = "segment";
        public int turn;
        public Segment segment;
    }

    [Serializable]
    public class ReplayOverLine
    {
        public string kind = "over";
        public int winnerTeam;
        public int finalTurn;
        public int finalTick;
        public string finalStateHash; // 字符串用于区分字段缺失与合法的 0。
    }

    [Serializable]
    public class ReplayEndLine
    {
        public string kind = "end";
        public int tick;
    }

    [Serializable]
    public class ReplayTickLine
    {
        public string kind = "tick";
        public int tick;
        public int hash;
    }
}
