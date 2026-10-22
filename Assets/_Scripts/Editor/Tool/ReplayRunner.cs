using System;
using System.Collections;
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
    /// 回放回归工具（2026-10-20 Replay harness 批，docs/18 决策八十四）：
    /// 消费 BattleRecorder 的 JSONL 录制——编辑器域 headless 重建 BattleSimState
    /// （同 seed / 同初始配置 / 同每回合全量行动序列），逐回合同步泵 TurnResolver 演算并 diff 命令流。
    ///
    /// 用途（docs/11 已拍板立项）：
    /// - **演算层回归安全网**：改动 TurnResolver / EffectCompiler / BattleSimState / Buff / DamagePipeline
    ///   后，验证「同输入 → 同命令流」（逐条 Segment JSON 对比）——平衡改动/重构的回归底座；
    /// - **B7 LAN desync 调试底座**：双端命令流不一致时录制对比定位首个漂移点。
    ///
    /// 录制面语义（BattleRecorder 头注释）：全行动录制（脑层不在断言面——AI 脑改动需重录基线）；
    /// 断言面=Segment 命令流；初始态=手牌全量录制（卡组构建上游不录）。
    ///
    /// 用法：录制（游戏内 `replay on` 打一局）→ 菜单 Tools/TG/回放回归（最新录制）。
    /// 自测=合成录制+回放全链闭环（不进 Play，验证录制器写盘↔回放器读盘重建）。
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

        [MenuItem("Tools/TG/回放回归（自测·合成局闭环）")]
        public static void SelfTestMenu()
        {
            var summary = SelfTest();
            ReportResult(summary, "SelfTest（合成局）");
        }

        // ==================== 主流程 ====================

        /// <summary>执行回放回归（编辑器域同步；异常包住防半途 GameObject 泄漏）</summary>
        public static string Run(string jsonlPath)
        {
            GameObject logicRoot = null;
            try
            {
                var recording = ParseFile(jsonlPath);
                if (recording.Header == null)
                    return "FAIL：录制缺 header 行（文件损坏或非录制文件）";
                if (recording.ActionsByTurn.Count == 0)
                    return "FAIL：录制无行动行（空局？）";

                // —— 编辑器域配置容器（演算层经 Wargame.Instance?.Context?.Get<ItemConfig>() 读配置；
                // 编辑器域 Boot 未跑容器为空——最小容器注入：仅 ConfigManager（Bean 链），
                // 勿跑 Wargame.Init 全家桶〔SaveManager 等有副作用〕）
                EnsureEditorConfigs();

                // —— 重建初始态（与 BattleScreen 开局装配逐参同构）——
                logicRoot = new GameObject("ReplayLogicRoot") { hideFlags = HideFlags.HideAndDontSave };
                var sim = BuildSimFromHeader(recording.Header, logicRoot);

                // —— 逐回合回放（headless 泵：FakeHost ack 恒真 → 协程零等待同步跑完）——
                var transport = new CollectingTransport();
                var resolver = new TurnResolver(sim, transport, new FakeResolverHost());
                var turns = new List<int>(recording.ActionsByTurn.Keys);
                turns.Sort();
                foreach (var turn in turns)
                {
                    sim.RevealFog(); // 录制局 BeginSelectPhase 单钩同口径（迷雾账本同步——爆发破雾元能依赖）
                    // 购买即时行动（购买批 2026-10-22）：选择阶段先于配额行动发生——按行序前置重演
                    //（校验+状态+迷你段广播全同链，购买段进 CollectingTransport 与录制段 diff）
                    var recorded = recording.ActionsByTurn[turn];
                    var purchases = new List<ActionData>();
                    var quota = new List<ActionData>();
                    foreach (var a in recorded)
                    {
                        if (a != null && a.actionType == ActionType.BuyCard) purchases.Add(a);
                        else quota.Add(a);
                    }
                    foreach (var purchase in purchases)
                        BuyCardExecutor.TryResolve(sim, transport, purchase);
                    Pump(resolver.ResolveTurnCoroutine(turn, quota));
                }

                // —— diff：回放 Segment 序列 vs 录制 Segment 序列（JsonUtility 字段序稳定）——
                var diff = DiffSegments(recording.Segments, transport.Segments);

                // —— 终局对照（录制有 over 行时）——
                string overCheck = "";
                if (recording.Over != null)
                {
                    int replayWinner = DeriveWinner(sim);
                    overCheck = replayWinner == recording.Over.winnerTeam
                        ? $"\n终局一致：胜方队伍 {replayWinner}（回放第 {recording.Over.finalTurn} 回合）"
                        : $"\n终局不一致：录制胜方 {recording.Over.winnerTeam} vs 回放 {replayWinner}";
                }

                return diff + overCheck;
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

        /// <summary>与 TurnFlowController.CheckBattleOver 同口径推导胜方（-999=无胜方/未终局）</summary>
        private static int DeriveWinner(BattleSimState sim)
        {
            if (!sim.HasAnyRegisteredCore()) return -999;
            TeamType? coreAliveTeam = null;
            foreach (var pid in sim.PlayerIds)
            {
                var team = sim.GetTeamOf(pid);
                if (coreAliveTeam == team) continue;
                if (!sim.TeamHasLivingCore(team)) continue;
                if (coreAliveTeam.HasValue) return -999; // 两队核心都存活=未终局
                coreAliveTeam = team;
            }
            return coreAliveTeam.HasValue ? (int)coreAliveTeam.Value : -999;
        }

        // ==================== 自测（合成局闭环） ====================

        /// <summary>编辑器域自测：合成录制（双 Pass 玩家×4 回合，走 BattleRecorder 真实录制路径）
        /// → Run 回放 diff。验证「录制器写盘 ↔ 回放器读盘重建」编解码闭环与 resolver 确定性；
        /// 不覆盖真局装配侧（BattleScreen 接线——交实机复测）。</summary>
        public static string SelfTest()
        {
            EnsureEditorConfigs();
            var unitConfig = Wargame.Instance.Context.Get<UnitConfig>();
            if (unitConfig == null) return "FAIL：编辑器域容器无 UnitConfig（EnsureEditorConfigs 失败）";

            var mapConfig = Resources.Load<BattleMapConfig>("Configs/" + BattleLaunchConfig.DefaultMapName);
            if (mapConfig == null || mapConfig.spawnZones == null || mapConfig.spawnZones.Count < 2)
                return "FAIL：默认地图出生区不足 2（自测前置不满足）";

            const int seed = 20261020;
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

            // —— 合成录制局：CaptureSetup 开文件 → 装配（BuildSimFromHeader 同一段代码）→ 逐回合
            //    Pump（CaptureActions/CaptureSegment 由录制器在真实调用点自动触发）——
            //    录制侧与回放侧走同一份重建代码，自测验证的正是「写盘格式 ↔ 读盘重建」闭环
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
                for (int turn = 1; turn <= 4; turn++)
                {
                    sim.RevealFog();
                    var actions = new List<ActionData>
                    {
                        new ActionData { playerId = BattleDebugPlayerIds.P1, actionType = ActionType.Pass, turnNumber = turn },
                        new ActionData { playerId = BattleDebugPlayerIds.P2, actionType = ActionType.Pass, turnNumber = turn },
                    };
                    BattleRecorder.CaptureActions(turn, actions);
                    Pump(resolver.ResolveTurnCoroutine(turn, actions));
                }
            }
            finally
            {
                if (logicRoot != null) UnityEngine.Object.DestroyImmediate(logicRoot);
            }
            BattleRecorder.EndSession();
            BattleRecorder.Enabled = false;

            if (path == null) return "FAIL：合成录制未产出文件（CaptureSetup 异常？）";
            return "自测（4 回合双 Pass 合成局，含回合末资源发放段）→ " + Run(path);
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
            public Dictionary<int, List<ActionData>> ActionsByTurn = new Dictionary<int, List<ActionData>>();
            public List<Segment> Segments = new List<Segment>();
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
                    if (a != null && a.actions != null) rec.ActionsByTurn[a.turn] = a.actions;
                }
                else if (line.StartsWith("{\"kind\":\"segment\""))
                {
                    var s = JsonUtility.FromJson<ReplaySegmentLine>(line);
                    if (s != null && s.segment != null) rec.Segments.Add(s.segment);
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

        // ==================== 协程泵（同步驱动嵌套 IEnumerator） ====================

        /// <summary>同步泵协程：嵌套 IEnumerator 递归推进——ack 恒真时 PushSegmentAndWaitAck
        /// 的等待 while 零进入、无任何帧/时间依赖，整局演算一次 Pump 跑完</summary>
        private static void Pump(IEnumerator routine)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            while (stack.Count > 0)
            {
                var current = stack.Peek();
                if (current.MoveNext())
                {
                    if (current.Current is IEnumerator nested && !ReferenceEquals(nested, current))
                        stack.Push(nested);
                }
                else
                {
                    stack.Pop();
                }
            }
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

        /// <summary>恒真 ack 宿主：PushSegmentAndWaitAck 的等待循环零进入（headless 同步泵前提）</summary>
        private class FakeResolverHost : ITurnResolverHost
        {
            public void NotifySegmentPushed(int turnNumber, int sliceIndex) { }
            public bool HasSegmentAck(int turnNumber, int sliceIndex) => true;
        }

        private static void ReportResult(string summary, string fileName)
        {
            var pass = summary.Contains("PASS");
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
