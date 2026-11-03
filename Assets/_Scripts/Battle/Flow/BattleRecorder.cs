using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using GIC.Data;
using GIC.Framework;

namespace GIC.Battle
{


    /// <summary>
    /// 战斗回放录制器（2026-10-20 Replay/回归 harness 批，docs/18 决策八十四）：
    /// 「初始配置 + 每回合全量行动（真人+AI+眷属+伙伴）+ Host 演算的 Segment 命令流」逐行落盘 JSONL
    /// ——回放器（Editor/Tool/ReplayRunner）以同 seed/同初始配置/同行动序列 headless 重演算，
    /// 命令流逐条 diff = 演算层（TurnResolver/EffectCompiler/BattleSimState/Buff/DamagePipeline）回归安全网
    /// + B7 LAN desync 调试底座。
    ///
    /// 录制面语义：
    /// - **全行动录制**（非仅真人行动）——AI 配额脑（MonoBehaviour 场景件）与眷属/伙伴脑的改动不在
    ///   断言面：脑改动后命令流差异属预期，需重录基线；断言只对演算层漂移报警（噪声最小）。
    /// - 断言面 = TurnResolver 出口三件里的 Segment（命令流本体）；TurnPlan/TurnEnd 派生数据不录。
    /// - 初始手牌逐条录制（含初始资源卡）——卡组构建/存档变化不影响回放（初始态全量快照式）。
    /// - 即时行动队列（EnqueueInstantAction，连携/契约预留）当前无生产消费方，未录；实装时扩展。
    ///
    /// 开关：调试指令 `replay on/off/status/list`（BattleReplayCommands）；默认关、发布版不落盘。
    /// </summary>
    public static class BattleRecorder
    {
        /// <summary>录制开关（replay 指令控制；默认 false）</summary>
        public static bool Enabled;

        private static string _path;
        private static bool _hasHeader;

        /// <summary>即时行动缓冲（购买批 2026-10-22，docs/45 §7 回放）：选择阶段发生的即时行动
        /// （BuyCard）先入当回合缓冲——购买先于配额行动提交、且全部完成于开演前，CaptureActions 时
        /// 按时序前置于配额行动合并落盘（回放器按行序先重演购买再喂演算）</summary>
        private static readonly Dictionary<int, List<ActionData>> _pendingInstant = new Dictionary<int, List<ActionData>>();

        private static string ReplayDirectory => Path.Combine(Application.persistentDataPath, "Replays");

        /// <summary>当前录制文件路径（null=未在录制）</summary>
        public static string CurrentPath => _hasHeader ? _path : null;

        // ==================== 录制入口（接线点各一处） ====================

        /// <summary>开局配置录制（BattleScreen 开局装配手牌注册后调）：开新文件写 header 行。
        /// 参数=演算复现所需的全部初始态（卡组构建上游不录——hands 即初始态终点）</summary>
        public static void CaptureSetup(int seed, string mapConfigName, int fogMode,
            List<BattlePlayerSetup> players, Dictionary<string, BattleCell> spawnCells,
            Dictionary<string, List<HandCard>> hands)
        {
            if (!Enabled) return;

            Directory.CreateDirectory(ReplayDirectory);
            _path = Path.Combine(ReplayDirectory,
                $"battle_{DateTime.Now:yyyyMMdd_HHmmss}_{seed}.jsonl");
            _hasHeader = true;

            var line = new ReplayHeaderLine
            {
                seed = seed,
                mapName = mapConfigName,
                fogMode = fogMode,
                players = new List<ReplayPlayerLine>(),
            };
            foreach (var p in players)
            {
                var pl = new ReplayPlayerLine
                {
                    playerId = p.PlayerId,
                    team = (int)p.Team,
                    spawnX = spawnCells != null && spawnCells.TryGetValue(p.PlayerId, out var cell) ? cell.x : 0,
                    spawnY = spawnCells != null && spawnCells.TryGetValue(p.PlayerId, out var cell2) ? cell2.y : 0,
                    hand = hands != null && hands.TryGetValue(p.PlayerId, out var hand) ? hand : new List<HandCard>(),
                    isAI = p.IsAI ? 1 : 0, // 凌晨批议题⑥：难度回放注册门（天理经济作弊=Host 侧结算差异源）
                    aiDifficulty = (int)p.AIDifficulty, // 同上：重演前注册，否则回合末发放/建筑收入倍率回落 100% 必 diff
                };
                line.players.Add(pl);
            }
            Append(line);
            GICLog.Info($"[BattleRecorder] 开录 → {Path.GetFileName(_path)}（{players.Count} 名玩家）");
        }

        /// <summary>每回合全量行动录制（TurnFlowController.TryBeginResolve 行动合并定稿后调）：
        /// 全部玩家的行动 + 眷属自主 + 伙伴自主（回放按此直喂 TurnResolver，不重跑任何脑）。
        /// 即时行动（购买）按发生序前置并入（选择阶段先于配额提交——回放先重演购买）</summary>
        public static void CaptureActions(int turn, List<ActionData> actions)
        {
            if (!Enabled || !_hasHeader) return;
            if (_pendingInstant.TryGetValue(turn, out var buffered) && buffered.Count > 0)
            {
                var merged = new List<ActionData>(buffered);
                if (actions != null && actions.Count > 0) merged.AddRange(actions);
                actions = merged;
                _pendingInstant.Remove(turn);
            }
            if (actions == null || actions.Count == 0) return;
            Append(new ReplayActionsLine { turn = turn, actions = actions });
        }

        /// <summary>即时行动录制（购买批：BuyCardExecutor 结算成功后调）——入当回合缓冲，
        /// 由 CaptureActions 定稿时合并（购买段=CaptureSegment 即时落盘——命令流断言面）</summary>
        public static void CaptureInstantAction(int turn, ActionData action)
        {
            if (!Enabled || !_hasHeader || action == null) return;
            if (!_pendingInstant.TryGetValue(turn, out var list))
            {
                list = new List<ActionData>();
                _pendingInstant[turn] = list;
            }
            list.Add(action);
        }

        /// <summary>Segment 命令流录制（TurnResolver.PushSegmentAndWaitAck 推送前调）——断言面本体</summary>
        public static void CaptureSegment(Segment segment)
        {
            if (!Enabled || !_hasHeader || segment == null) return;
            Append(new ReplaySegmentLine { turn = segment.turnNumber, segment = segment });
        }

        /// <summary>终局录制（TurnFlowController 胜负广播处调）；无 over 行=识别未终局录制（回放仍可用）</summary>
        public static void CaptureGameOver(int winnerTeam, int finalTurn)
        {
            if (!Enabled || !_hasHeader) return;
            Append(new ReplayOverLine { winnerTeam = winnerTeam, finalTurn = finalTurn });
        }

        /// <summary>tick 状态 hash 录制（决策九十五确定性护栏四件套之一，docs/active/48 §2.2）——
        /// 每秒整拍随快照落一行：后置 LAN 联机双端比对漂移（王者荣耀同款定位手段）；单机回放
        /// 亦可用作「同 seed 同命令流=同 hash」的自证断言。旧回放器缺该行类型=跳过不判（缺省兼容）</summary>
        public static void CaptureTickHash(int tick, int hash)
        {
            if (!Enabled || !_hasHeader) return;
            Append(new ReplayTickLine { tick = tick, hash = hash });
        }

        /// <summary>收笔（BattleScreen.OnDestroy 调——中途退出/正常终局统一兜底；空录制删文件）</summary>
        public static void EndSession()
        {
            if (!_hasHeader) return;
            _hasHeader = false;
            _path = null;
            _pendingInstant.Clear();
        }

        // ==================== 序列化 ====================

        private static void Append(object line)
        {
            try
            {
                File.AppendAllText(_path, JsonUtility.ToJson(line) + "\n");
            }
            catch (Exception e)
            {
                GICLog.Warn($"[BattleRecorder] 落盘失败，录制中止：{e.Message}");
                _hasHeader = false;
            }
        }
    }

    // ==================== JSONL 行 DTO（回放器同文件反序列化） ====================

    [Serializable]
    public class ReplayHeaderLine
    {
        public string kind = "header";
        public int formatVersion = 1;
        public int seed;
        public string mapName;
        public int fogMode;
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

        /// <summary>是否 AI 玩家（凌晨批议题⑥）：回放器据此注册难度。旧录制缺省 0=真人→不注册
        ///（AIPlayerConfig 未注册回落棋圣=无经济作弊零漂移）</summary>
        public int isAI;

        /// <summary>AI 难度档（凌晨批议题⑥）：天理=经济作弊型（Host 侧结算差异）——回放器重演前
        /// 注册进 AIPlayerConfig。类默认棋圣=旧录制（无此字段）零漂移</summary>
        public int aiDifficulty = (int)AIDifficulty.Grandmaster;
    }

    [Serializable]
    public class ReplayActionsLine
    {
        public string kind = "actions";
        public int turn;
        public List<ActionData> actions;
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
    }

    [Serializable]
    public class ReplayTickLine
    {
        public string kind = "tick";
        public int tick;
        public int hash;
    }
}
