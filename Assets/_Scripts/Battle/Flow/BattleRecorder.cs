using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using GIC.Data;
using GIC.Framework;

namespace GIC.Battle
{


    /// <summary>
    /// 战斗回放录制器（**v2 即时制 tick 格式**，决策九十五 P6 批，docs/active/48 §8 拍板 7——
    /// v1 回合格式随回合机归档不迁移；v1 原案 2026-10-20=docs/18 决策八十四）：
    /// 「初始配置 + 全部演算输入（AI 节拍批/玩家与配额脑上交/购买）+ Host 演算的 Segment 命令流 +
    /// 每秒状态 hash」逐行落盘 JSONL——回放器（Editor/Tool/ReplayRunner v2）以同 seed/同初始配置
    /// headless 逐 tick 重演：命令流逐条 diff + tick hash 自证 = 演算层
    /// （TurnResolver/EffectCompiler/BattleSimState/Buff/DamagePipeline/RTSMovementSystem）回归安全网
    /// + 后置 LAN desync 调试底座。
    ///
    /// 录制面语义（v2）：
    /// - **全演算输入录制**：AI 节拍批（RTSFlowController.RunAiBeat 眷属+伙伴脑批）=kind=actions 行
    ///   （批内行动带 submitTick=节拍 tick）；玩家/配额脑上交+购买=kind=submit 行
    ///   （钩点=RTSFlowController.SubmitAndResolve〔校验通过后〕与 BuyCardExecutor）。
    ///   脑层不在断言面——脑改动后命令流差异属预期，需重录基线；断言只对演算层漂移报警（噪声最小）。
    /// - 断言面 = Segment（命令流本体，TurnResolver.PushSegment 钩）+ tick hash（RTSFlowController
    ///   RunSecondBeat 钩，确定性护栏四）；TurnPlan/TurnEnd 派生数据不录。
    /// - 初始手牌逐条录制（含初始资源卡）——卡组构建/存档变化不影响回放（初始态全量快照式）。
    ///
    /// 开关：调试指令 `replay on/off/status/list`（BattleReplayCommands）；默认关、发布版不落盘。
    /// </summary>
    public static class BattleRecorder
    {
        /// <summary>录制开关（replay 指令控制；默认 false）</summary>
        public static bool Enabled;

        private static string _path;
        private static bool _hasHeader;

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
                $"battle_{DateTime.Now:yyyyMMdd_HHmmss}_{seed}.jsonl"); // DETERMINISM-ALLOW: 录制文件名（诊断，不入模拟）
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

        /// <summary>AI 节拍批录制（RTSFlowController.RunAiBeat 脑批定稿后调）：v2 批内行动全带
        /// submitTick（=节拍 tick），回放器在 AI 节拍窗（%10）按 tick 喂回——不重跑任何脑；
        /// 空批不落行（空批零演算，无对应窗口动作）</summary>
        public static void CaptureActions(int turn, List<ActionData> actions)
        {
            if (!Enabled || !_hasHeader) return;
            if (actions == null || actions.Count == 0) return;
            Append(new ReplayActionsLine { turn = turn, actions = actions });
        }

        /// <summary>上交行动录制（v2 新增钩点，RTSFlowController.SubmitAndResolve 校验通过后调）——
        /// 覆盖真人/配额脑/经即时通道回流的非购买行动（玩家级 Deploy/UseItem/Equip 与 Move/Skill/Pass）。
        /// 行动带 submitTick（BattleSession.SubmitAction 客户端侧盖章、SubmitAndResolve Host 侧兜底盖章）
        /// ——回放器在「submit 窗」（当拍确定性步骤之后）按 tick 依次消费，file 序=时间序</summary>
        public static void CaptureSubmittedAction(ActionData action)
        {
            if (!Enabled || !_hasHeader || action == null) return;
            Append(new ReplaySubmitLine { action = action });
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
        public int formatVersion = 2; // v2=tick 格式（决策九十五拍板 7：v1 回合格式归档不迁移）
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
    public class ReplaySubmitLine
    {
        /// <summary>玩家/配额脑上交或购买的行动（v2）——action.submitTick=回放消费 tick 锚；
        /// 行序（file 序）=发生时间序，同 tick 内按行序重演</summary>
        public string kind = "submit";
        public ActionData action;
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
