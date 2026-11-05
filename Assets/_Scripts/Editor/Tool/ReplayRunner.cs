using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using GIC.Data;
using GIC.Framework;
using GIC.Battle;

namespace GIC.Editor
{


    /// <summary>
    /// 回放回归工具 v2（决策九十五 P6 批，docs/active/48 §8 拍板 7——v1 回合格式随回合机归档不迁移；
    /// v1 原案 2026-10-20=docs/18 决策八十四）：
    /// 消费 BattleRecorder 的 JSONL 录制（即时制 tick 格式）——编辑器域 headless 重建 BattleSimState
    /// （同 seed / 同初始配置），按录制的行动流逐 tick 重演，**每拍窗口序与
    /// RTSFlowController.TickAdvance 逐窗口同构**：
    ///   ① 移动推进（RTSMovementSystem.AdvanceTick→ResolveMoveArrivals）
    ///   ② AI 节拍窗（%10：RevealFog 决策时态账本同步 + 按 tick 喂录制 AI 批——不重跑任何脑）
    ///   ③ 秒拍（%20：ResolveSecondBeat——秒拍族+效果拍 %40）
    ///   ④ hash 自证（录制 tick hash 行同点位比对，确定性护栏四）
    ///   ⑤ submit 窗（录制的玩家/配额脑上交+购买按 submitTick 依次重演——file 序=时间序；
    ///      购买走 BuyCardExecutor 即时执行器，其余直喂演算）
    ///
    /// 断言面三件：Segment 命令流逐条 diff（主）+ tick hash 自证（秒拍状态全量）+ 终局对照（胜方+终局拍）。
    /// 用途：演算层（TurnResolver/EffectCompiler/BattleSimState/Buff/DamagePipeline/RTSMovementSystem）
    /// 改动后的回归安全网 + 后置 LAN desync 调试底座。脑层不在断言面——脑改动需重录基线。
    ///
    /// 用法：录制（游戏内 `replay on` 打一局，或观察局 harness）→ 菜单 Tools/TG/回放回归。
    /// 自测=合成录制+回放全链闭环（不进 Play，验证录制器写盘↔回放器读盘重建↔tick 窗口序）。
    /// </summary>
    public static class ReplayRunner
    {
        private static string ReplayDir => Path.Combine(Application.persistentDataPath, "Replays");

        // ==================== 菜单入口 ====================

        [MenuItem("Tools/TG/回放回归（最新录制）")]
        public static void RunLatest()
        {
            if (!Directory.Exists(ReplayDir))
            {
                ReportFail("无录制目录", $"先开录制（游戏内 replay on）打一局：{ReplayDir}");
                return;
            }
            var files = Directory.GetFiles(ReplayDir, "*.jsonl");
            if (files.Length == 0)
            {
                ReportFail("无录制文件", $"先开录制（游戏内 replay on）打一局：{ReplayDir}");
                return;
            }
            Array.Sort(files); // 文件名含时间戳=按局序
            var path = files[files.Length - 1];
            var summary = Run(path);
            ReportResult(summary, Path.GetFileName(path));
        }

        [MenuItem("Tools/TG/回放回归（选择文件…）")]
        public static void RunPick()
        {
            var path = EditorUtility.OpenFilePanel("选择回放录制", ReplayDir, "jsonl");
            if (string.IsNullOrEmpty(path)) return;
            var summary = Run(path);
            ReportResult(summary, Path.GetFileName(path));
        }

        /// <summary>F0-5 双跑确定性：同 seed 同命令连续跑两遍，摘要逐字比对——
        /// 捕捉字典遍历序/静态状态泄漏等「录制基线之外」的非确定性（Run 摘要只含计数与断言文本，无耗时字段）。</summary>
        [MenuItem("Tools/TG/回放回归（双跑确定性）")]
        public static void RunDoubleLatest()
        {
            if (!Directory.Exists(ReplayDir))
            {
                ReportFail("无录制目录", $"先开录制（游戏内 replay on）打一局：{ReplayDir}");
                return;
            }
            var files = Directory.GetFiles(ReplayDir, "*.jsonl");
            if (files.Length == 0)
            {
                ReportFail("无录制文件", $"先开录制（游戏内 replay on）打一局：{ReplayDir}");
                return;
            }
            Array.Sort(files); // 文件名含时间戳=按局序
            var path = files[files.Length - 1];

            var first = Run(path);
            var second = Run(path);
            bool bothPass = !first.Contains("FAIL") && !second.Contains("FAIL");
            bool identical = string.Equals(first, second, StringComparison.Ordinal);

            var sb = new StringBuilder();
            if (bothPass && identical)
            {
                sb.Append("PASS：双跑确定性（摘要逐字一致）");
                sb.Append($"\n{first}");
            }
            else
            {
                sb.Append("FAIL：双跑不一致或存在断言失败");
                sb.Append($"\n第一跑：{(first.Contains("FAIL") ? "FAIL" : "PASS")}");
                sb.Append($"\n第二跑：{(second.Contains("FAIL") ? "FAIL" : "PASS")}");
                sb.Append($"\n--- 第一跑 ---\n{first}");
                sb.Append($"\n--- 第二跑 ---\n{second}");
            }
            ReportResult(sb.ToString(), Path.GetFileName(path));
        }

        [MenuItem("Tools/TG/回放回归（自测·合成局闭环）")]
        public static void SelfTestMenu()
        {
            var summary = SelfTest();
            ReportResult(summary, "SelfTest（合成局）");
        }

        // ==================== 主流程 ====================

        /// <summary>执行回放回归（编辑器域同步 headless；异常包住防半途 GameObject 泄漏）</summary>
        public static string Run(string jsonlPath)
        {
            GameObject logicRoot = null;
            try
            {
                var recording = ParseFile(jsonlPath);
                if (recording.Header == null)
                    return "FAIL：录制缺 header 行（文件损坏或非录制文件）";
                if (recording.Header.formatVersion < 2)
                    return "FAIL：v1 回合格式已归档不迁移（决策九十五拍板 7）——请用即时制版本重新录制（replay on 打一局）";

                // —— AI 难度注册（天理档经济作弊=Host 侧结算差异源：回合末摩拉发放/建筑收入乘倍率）——
                //    重演前注册进 AIPlayerConfig；旧录制缺字段=类默认棋圣（无作弊）零漂移
                foreach (var pl in recording.Header.players)
                {
                    if (pl.isAI == 0) continue;
                    AIPlayerConfig.SetDifficulty(pl.playerId, (AIDifficulty)pl.aiDifficulty);
                }

                // —— 编辑器域配置容器（演算层经 Wargame.Instance?.Context?.Get<ItemConfig>() 读配置；
                //    编辑器域 Boot 未跑容器为空——最小容器注入：仅 ConfigManager（Bean 链），
                //    勿跑 Wargame.Init 全家桶〔SaveManager 等有副作用〕）
                EnsureEditorConfigs();

                // —— 重建初始态（与 BattleScreen 开局装配逐参同构）——
                logicRoot = new GameObject("ReplayLogicRoot") { hideFlags = HideFlags.HideAndDontSave };
                var sim = BuildSimFromHeader(recording.Header, logicRoot);
                var transport = new CollectingTransport();
                var resolver = new TurnResolver(sim, transport, new FakeResolverHost());

                // —— 录制流索引：AI 批按 tick 建索引（一节拍至多一批）；submit 按行序（file 序=时间序）——
                var aiBatchByTick = new Dictionary<int, List<ActionData>>();
                foreach (var batch in recording.AiBatches)
                {
                    if (batch.actions == null || batch.actions.Count == 0) continue;
                    int batchTick = batch.actions[0].submitTick;
                    if (batchTick <= 0)
                        batchTick = batch.turn * BattleMetrics.TicksPerSecond; // 未盖章兜底（理论不可达：RunAiBeat 必盖章）
                    if (batchTick % BattleMetrics.AiBeatTicks != 0)
                        return $"FAIL：AI 行动批 tick {batchTick} 不在节拍窗（%{BattleMetrics.AiBeatTicks}）——录制异常";
                    if (aiBatchByTick.ContainsKey(batchTick))
                        return $"FAIL：tick {batchTick} 出现多批 AI 行动——录制异常";
                    aiBatchByTick[batchTick] = batch.actions;
                }
                var submits = new Queue<ReplaySubmitLine>(recording.Submits);

                int maxTick = 0;
                foreach (var t in aiBatchByTick.Keys) maxTick = Math.Max(maxTick, t);
                foreach (var s in recording.Submits)
                    if (s.action != null) maxTick = Math.Max(maxTick, s.action.submitTick);
                foreach (var t in recording.HashByTick.Keys) maxTick = Math.Max(maxTick, t);
                if (recording.Over != null)
                    maxTick = Math.Max(maxTick, recording.Over.finalTurn * BattleMetrics.TicksPerSecond);
                if (maxTick <= 0)
                    return "FAIL：录制无任何行动/hash 行（空局或未开战）";
                // 尾拍余量：**仅终局录制给**——终局未复现时跑满暴露差异（段 diff/未消费行兜底报警）；
                // 未终局录制（中途退出）精确跑 maxTick——余量会多产秒拍段=假「段数不一致」
                //（自测实锤：2 秒合成局+40t 余量多产 2 个 TURN-END 段）
                if (recording.Over != null)
                    maxTick += BattleMetrics.TicksPerSecond * 2;

                // —— headless tick 重演（窗口序见类头注释；终局判据与 live 同源 RTSFlowController.CheckBattleOver）——
                var over = new OverTracker();
                int hashCompared = 0;
                int hashMismatchTick = -1;
                int recordedHash = 0, replayedHash = 0;
                int tick = 0;
                while (tick < maxTick)
                {
                    tick++;
                    sim.CurrentTick = tick;

                    // ① 移动推进（tick 整数速度累积器——确定性原样）
                    var arrivals = RTSMovementSystem.AdvanceTick(sim);
                    if (arrivals != null && arrivals.Count > 0)
                    {
                        resolver.ResolveMoveArrivals(arrivals);
                        over.Check(sim, tick);
                    }

                    // ② AI 节拍窗（%10）——live RunAiBeat 同点位（RevealFog=决策时态账本同步；批不重跑脑）
                    if (!over.Ended && tick % BattleMetrics.AiBeatTicks == 0)
                    {
                        sim.RevealFog();
                        if (aiBatchByTick.TryGetValue(tick, out var batch) && batch != null && batch.Count > 0)
                            resolver.ResolveAiActions(batch);
                        over.Check(sim, tick);
                    }

                    // ③ 秒拍（%20：秒拍族+效果拍 %40）
                    if (!over.Ended && tick % BattleMetrics.TicksPerSecond == 0)
                    {
                        bool effectBeat = tick % BattleMetrics.EffectBeatTicks == 0;
                        resolver.ResolveSecondBeat(tick / BattleMetrics.TicksPerSecond, effectBeat);
                        over.Check(sim, tick);
                    }

                    // ④ hash 自证（live RunSecondBeat CaptureTickHash 同点位：秒拍后、submit 窗前——
                    //    录制行存在即比对〔live 在该拍秒后未终局才落 hash 行〕，终局拍错位在此显形）
                    if (recording.HashByTick.TryGetValue(tick, out var expectedHash))
                    {
                        hashCompared++;
                        int actual = RTSFlowController.ComputeStateHash(sim);
                        if (actual != expectedHash && hashMismatchTick < 0)
                        {
                            hashMismatchTick = tick;
                            recordedHash = expectedHash;
                            replayedHash = actual;
                        }
                    }

                    // ⑤ submit 窗（当拍内上交的行动——file 序=时间序；购买走即时执行器，其余直喂演算。
                    //    录制行动=live 校验通过集（钩点在 SubmitAndResolve）——拒绝行动不上盘，直喂无防线缺口）
                    while (!over.Ended && submits.Count > 0 && submits.Peek().action != null
                           && submits.Peek().action.submitTick <= tick)
                    {
                        var sub = submits.Dequeue();
                        if (sub.action.actionType == ActionType.BuyCard)
                            BuyCardExecutor.TryResolve(sim, transport, sub.action);
                        else
                            resolver.ResolveSubmittedAction(sub.action);
                        over.Check(sim, tick);
                    }

                    if (over.Ended) break;
                }

                // ==================== 断言面三件 ====================

                var sb = new StringBuilder();

                // 一：Segment 命令流逐条 diff（主断言面）
                sb.Append(DiffSegments(recording.Segments, transport.Segments));

                // 二：tick hash 自证（确定性护栏四——秒拍状态全量比对）
                if (hashMismatchTick >= 0)
                    sb.Append($"\nFAIL：状态 hash 漂移首现 tick {hashMismatchTick}（录制 {recordedHash} vs 回放 {replayedHash}）");
                else
                    sb.Append($"\nhash 自证 PASS：{hashCompared} 拍逐拍一致");

                // 三：终局对照（胜方+终局拍）
                if (recording.Over != null)
                {
                    if (!over.Ended)
                        sb.Append("\nFAIL：录制已终局但回放未终局（命令流/状态先漂移或终局条件未复现）");
                    else if ((int)over.Winner.Value == recording.Over.winnerTeam)
                    {
                        sb.Append($"\n终局一致：胜方队伍 {recording.Over.winnerTeam}（回放 tick {over.Tick}）");
                        int finalSecond = over.Tick / BattleMetrics.TicksPerSecond;
                        if (finalSecond != recording.Over.finalTurn)
                            sb.Append($"\nFAIL：终局拍不一致（录制第 {recording.Over.finalTurn} 秒 vs 回放第 {finalSecond} 秒）");
                    }
                    else
                        sb.Append($"\nFAIL：终局不一致（录制胜方 {recording.Over.winnerTeam} vs 回放 {over.Winner}）");
                }
                else
                {
                    sb.Append(over.Ended
                        ? $"\n注：录制无 over 行但回放终局（胜方 {over.Winner}）——录制收笔竞态属预期"
                        : "\n注：录制未终局（中途退出）——段 diff 与 hash 断言仍有效");
                }

                if (submits.Count > 0)
                    sb.Append($"\nFAIL：{submits.Count} 条上交行动未消费（回放提前终局或 submitTick 错位）");

                return sb.ToString();
            }
            catch (Exception e)
            {
                return $"FAIL：回放异常 {e.GetType().Name}: {e.Message}\n{e.StackTrace}";
            }
            finally
            {
                if (logicRoot != null) UnityEngine.Object.DestroyImmediate(logicRoot); // 单位全挂其下连带回收
            }
        }

        /// <summary>终局追踪（判据单源=RTSFlowController.CheckBattleOver——与 live CheckBattleEnded 同口径）</summary>
        private sealed class OverTracker
        {
            public TeamType? Winner;
            public int Tick = -1;
            public bool Ended => Winner.HasValue;

            public void Check(BattleSimState sim, int tick)
            {
                if (Ended) return;
                var w = RTSFlowController.CheckBattleOver(sim);
                if (w.HasValue)
                {
                    Winner = w;
                    Tick = tick;
                }
            }
        }

        // ==================== 自测（合成局闭环） ====================

        /// <summary>编辑器域自测：合成录制（双核心对峙 2 秒，走 BattleRecorder 真实录制路径：
        /// header+秒拍资源发放段+tick hash 行）→ Run 回放 diff。验证「录制器写盘 ↔ 回放器读盘重建
        /// ↔ tick 窗口序」编解码闭环；不覆盖真局装配侧（BattleScreen 接线——交实机/观察局复测）。</summary>
        public static string SelfTest()
        {
            EnsureEditorConfigs();
            var unitConfig = Wargame.Instance.Context.Get<UnitConfig>();
            if (unitConfig == null) return "FAIL：编辑器域容器无 UnitConfig（EnsureEditorConfigs 失败）";

            var mapConfig = Resources.Load<BattleMapConfig>("Configs/" + BattleLaunchConfig.DefaultMapName);
            if (mapConfig == null || mapConfig.spawnZones == null || mapConfig.spawnZones.Count < 2)
                return "FAIL：默认地图出生区不足 2（自测前置不满足）";

            const int seed = 20261105;
            var setups = new List<BattlePlayerSetup>
            {
                new BattlePlayerSetup { PlayerId = BattleDebugPlayerIds.P1, DisplayName = "自测P1", Team = TeamType.A },
                new BattlePlayerSetup { PlayerId = BattleDebugPlayerIds.P2, DisplayName = "自测P2", Team = TeamType.B },
            };
            var spawnCells = new Dictionary<string, BattleCell>
            {
                { BattleDebugPlayerIds.P1, mapConfig.spawnZones[0].center },
                { BattleDebugPlayerIds.P2, mapConfig.spawnZones[1].center },
            };
            HandCard MoraCard() => new HandCard(new CardId(ItemName.Mora), 200);
            HandCard StaminaCard() => new HandCard(new CardId(ItemName.Stamina), 60);
            var hands = new Dictionary<string, List<HandCard>>
            {
                { BattleDebugPlayerIds.P1, new List<HandCard> { MoraCard(), StaminaCard() } },
                { BattleDebugPlayerIds.P2, new List<HandCard> { MoraCard(), StaminaCard() } },
            };

            // —— 合成录制局：CaptureSetup 开文件 → BuildSimFromHeader 同段重建 → 手推 2 秒 tick
            //    （秒拍 2 拍：资源发放段+hash 行——录制侧与回放侧走同一份重建代码，自测验证的
            //    正是「写盘格式 ↔ 读盘重建 ↔ 窗口序」闭环）
            BattleRecorder.Enabled = true;
            BattleRecorder.CaptureSetup(seed, BattleLaunchConfig.DefaultMapName, (int)BattleFogMode.None,
                setups, spawnCells, hands);
            var path = BattleRecorder.CurrentPath;

            var header = new ReplayHeaderLine
            {
                seed = seed,
                mapName = BattleLaunchConfig.DefaultMapName,
                fogMode = (int)BattleFogMode.None,
                players = new List<ReplayPlayerLine>(),
            };
            foreach (var s in setups)
            {
                header.players.Add(new ReplayPlayerLine
                {
                    playerId = s.PlayerId,
                    team = (int)s.Team,
                    spawnX = spawnCells[s.PlayerId].x,
                    spawnY = spawnCells[s.PlayerId].y,
                    hand = hands[s.PlayerId],
                });
            }

            GameObject logicRoot = null;
            try
            {
                logicRoot = new GameObject("SelfTestRecordRoot") { hideFlags = HideFlags.HideAndDontSave };
                var sim = BuildSimFromHeader(header, logicRoot);
                var transport = new CollectingTransport();
                var resolver = new TurnResolver(sim, transport, new FakeResolverHost());
                const int totalTicks = 2 * BattleMetrics.TicksPerSecond;
                for (int tick = 1; tick <= totalTicks; tick++)
                {
                    sim.CurrentTick = tick;
                    var arrivals = RTSMovementSystem.AdvanceTick(sim);
                    if (arrivals != null && arrivals.Count > 0)
                        resolver.ResolveMoveArrivals(arrivals);
                    if (tick % BattleMetrics.TicksPerSecond != 0) continue;
                    bool effectBeat = tick % BattleMetrics.EffectBeatTicks == 0;
                    resolver.ResolveSecondBeat(tick / BattleMetrics.TicksPerSecond, effectBeat);
                    // 与 live RunSecondBeat CaptureTickHash 同点位（秒拍后）——合成局自记 hash 行
                    BattleRecorder.CaptureTickHash(tick, RTSFlowController.ComputeStateHash(sim));
                }
            }
            finally
            {
                if (logicRoot != null) UnityEngine.Object.DestroyImmediate(logicRoot);
            }
            BattleRecorder.EndSession();
            BattleRecorder.Enabled = false;

            if (path == null) return "FAIL：合成录制未产出文件（CaptureSetup 异常？）";
            return "自测（2 秒合成局：双核心+秒拍发放段+hash 自证）→ " + Run(path);
        }

        // ==================== sim 重建（Run 与 SelfTest 共用） ====================

        /// <summary>按录制 header 重建 BattleSimState（与 BattleScreen 开局装配逐参同构：
        /// 名册注册 → 协议核心 spawn〔名册序=unitId 分配序与录制局一致〕→ 手牌注入〔三参
        /// GainCard=录制局 BattleScreen 同款调用〕）。logicRoot 由调用方持有负责销毁。</summary>
        private static BattleSimState BuildSimFromHeader(ReplayHeaderLine header, GameObject logicRoot)
        {
            var mapConfig = Resources.Load<BattleMapConfig>("Configs/" + header.mapName);
            if (mapConfig == null) throw new InvalidOperationException($"地图配置不存在 {header.mapName}");
            var map = mapConfig.BuildData();
            if (map == null) throw new InvalidOperationException("地图数据非法");

            var sim = new BattleSimState(map, header.seed);
            sim.LogicRoot = logicRoot.transform;
            sim.FogMode = (BattleFogMode)header.fogMode;

            var unitConfig = Wargame.Instance.Context.Get<UnitConfig>();
            foreach (var pl in header.players)
            {
                sim.RegisterPlayer(pl.playerId);
                sim.SetPlayerTeam(pl.playerId, (TeamType)pl.team);
            }
            foreach (var pl in header.players)
            {
                var coreData = unitConfig.GetUnitData(UnitName.ProtocolCore);
                var core = UnitFactory.CreateUnitWithData(coreData);
                if (core == null) throw new InvalidOperationException("协议核心创建失败（UnitConfig 链）");
                core.transform.SetParent(logicRoot.transform, false);
                sim.RegisterUnit(core, pl.playerId, (TeamType)pl.team,
                    new BattleCell(pl.spawnX, pl.spawnY));
            }
            foreach (var pl in header.players)
            {
                sim.RegisterHand(pl.playerId, new List<HandCard>());
                if (pl.hand == null) continue;
                foreach (var h in pl.hand)
                    sim.GainCard(pl.playerId, h.AsCardId(), h.count);
            }
            return sim;
        }

        // ==================== 录制解析 ====================

        private class Recording
        {
            public ReplayHeaderLine Header;
            public List<ReplayActionsLine> AiBatches = new List<ReplayActionsLine>(); // file 序=时间序
            public List<ReplaySubmitLine> Submits = new List<ReplaySubmitLine>();       // file 序=时间序
            public List<Segment> Segments = new List<Segment>();                        // 断言面（命令流本体）
            public Dictionary<int, int> HashByTick = new Dictionary<int, int>();        // 断言面（秒拍状态）
            public ReplayOverLine Over;
        }

        private static Recording ParseFile(string path)
        {
            var rec = new Recording();
            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                // kind 前缀嗅探（行首字段序固定——kind 是首字段）
                if (line.StartsWith("{\"kind\":\"actions\""))
                {
                    var a = JsonUtility.FromJson<ReplayActionsLine>(line);
                    if (a != null && a.actions != null && a.actions.Count > 0) rec.AiBatches.Add(a);
                }
                else if (line.StartsWith("{\"kind\":\"submit\""))
                {
                    var s = JsonUtility.FromJson<ReplaySubmitLine>(line);
                    if (s != null && s.action != null) rec.Submits.Add(s);
                }
                else if (line.StartsWith("{\"kind\":\"segment\""))
                {
                    var s = JsonUtility.FromJson<ReplaySegmentLine>(line);
                    if (s != null && s.segment != null) rec.Segments.Add(s.segment);
                }
                else if (line.StartsWith("{\"kind\":\"tick\""))
                {
                    var t = JsonUtility.FromJson<ReplayTickLine>(line);
                    if (t != null) rec.HashByTick[t.tick] = t.hash;
                }
                else if (line.StartsWith("{\"kind\":\"over\""))
                {
                    rec.Over = JsonUtility.FromJson<ReplayOverLine>(line);
                }
                else if (line.StartsWith("{\"kind\":\"header\""))
                {
                    rec.Header = JsonUtility.FromJson<ReplayHeaderLine>(line);
                }
            }
            return rec;
        }

        // ==================== diff ====================

        private static string DiffSegments(List<Segment> recorded, List<Segment> replayed)
        {
            int totalCmds = 0;
            foreach (var s in recorded) totalCmds += s.commands?.Count ?? 0;

            var sb = new StringBuilder();
            if (recorded.Count != replayed.Count)
                sb.Append($"\n段数不一致：录制 {recorded.Count} vs 回放 {replayed.Count}");

            int n = Math.Min(recorded.Count, replayed.Count);
            for (int i = 0; i < n; i++)
            {
                var a = JsonUtility.ToJson(recorded[i]);
                var b = JsonUtility.ToJson(replayed[i]);
                if (a == b) continue;
                sb.Append($"\n首个差异：第 {i + 1} 段（录制 turn={recorded[i].turnNumber} slice={recorded[i].sliceIndex}"
                          + $" {recorded[i].commands?.Count ?? 0} 命令 vs 回放 turn={replayed[i].turnNumber}"
                          + $" slice={replayed[i].sliceIndex} {replayed[i].commands?.Count ?? 0} 命令）");
                sb.Append("\n录制: " + Snippet(a));
                sb.Append("\n回放: " + Snippet(b));
                break; // 只报首个差异（避免刷屏）
            }

            return sb.Length == 0
                ? $"PASS：{recorded.Count} 段 / {totalCmds} 命令逐条一致"
                : "FAIL：命令流漂移" + sb;
        }

        /// <summary>差异定位：截首个不同字符的上下文窗口</summary>
        private static string Snippet(string json)
        {
            return json.Length <= 160 ? json : json.Substring(0, 160) + "…";
        }

        // ==================== 编辑器域配置容器 ====================

        /// <summary>编辑器域最小容器：Register&lt;ConfigManager&gt; + [Bean] 链 + 静态访问层 Initialize，
        /// 反射注入 Wargame.Instance.Context（纯 C# 单例裸件）。已注入时跳过（幂等）。</summary>
        private static void EnsureEditorConfigs()
        {
            var wg = Wargame.Instance; // Singleton<T> 纯 C#：首次访问自动 new 裸件（Context=null）
            if (wg.Context != null) return;

            var ctx = new ApplicationContext();
            ctx.Register<ConfigManager>();
            ctx.ProcessConfigurations(); // [Bean]：UnitConfig/ItemConfig/... 从 Resources 加载并注册
            ctx.InjectAll();             // ConfigManager 自身 [Autowired]（_unitConfig/_itemConfig/_cardResolver）
            ctx.Get<ConfigManager>().Init(); // [PostConstruct]：静态访问层 Initialize（ElementFaction/BattlePalette/AITuning/CardConfigResolver/StarVisual）

            typeof(Wargame).GetProperty("Context", BindingFlags.Public | BindingFlags.Instance)
                .SetValue(wg, ctx, null);
            GICLog.Info("[ReplayRunner] 编辑器域配置容器已注入（ConfigManager Bean 链）");
        }

        // ==================== 内部件 ====================

        /// <summary>回放收集传输：只收 Segment 消息（断言面），其余 Host 下行丢弃</summary>
        private class CollectingTransport : IBattleTransport
        {
            public readonly List<Segment> Segments = new List<Segment>();

            public void HostSend(BattleMessageType type, object message)
            {
                if (type == BattleMessageType.Segment && message is SegmentMessage sm && sm.segment != null)
                    Segments.Add(sm.segment);
            }

            public void ClientSend(BattleMessageType type, object message) { }
            public void RegisterHostHandler(Action<BattleMessageType, string, string> handler) { }
            public void RegisterClientHandler(Action<BattleMessageType, string> handler) { }
        }

        /// <summary>回放宿主：ack 门控已随回合机退役（接口仅剩推送钩子，docs/active/49 D2）</summary>
        private class FakeResolverHost : ITurnResolverHost
        {
            public void NotifySegmentPushed(int turnNumber, int sliceIndex) { }
        }

        private static void ReportResult(string summary, string fileName)
        {
            var pass = !summary.Contains("FAIL"); // v2 多断言面汇总：任一 FAIL 即整体 FAIL（PASS 字样不遮蔽）
            Debug.Log($"[ReplayRunner] {fileName} → {summary}");
            EditorUtility.DisplayDialog(pass ? "回放回归 PASS" : "回放回归 FAIL",
                $"{fileName}\n\n{summary}", "确定");
        }

        private static void ReportFail(string title, string message)
        {
            Debug.LogWarning($"[ReplayRunner] {title}：{message}");
            EditorUtility.DisplayDialog(title, message, "确定");
        }
    }
}
