using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 调试玩家 ID（本地双开控制调试模式：双端输入 UI 共用一进程）
    /// </summary>
    public static class BattleDebugPlayerIds
    {
        public const string P1 = "P1";
        public const string P2 = "P2";
    }

    /// <summary>
    /// 一场对局的组合根：
    /// - 本地模式（B1-B6）：Host（逻辑）与 Client（表现）同进程，LocalBattleTransport 直通；
    /// - LAN 模式：Host 封存逐拍输入；客户端也运行 Sim/Flow，各端用本地模拟驱动表现。
    /// 铁律：战斗逻辑不走 EventBusHub，一切消息经 IBattleTransport 专用通道。
    /// Roster（开局名册）双端同构：房间选的队伍/颜色/出生点进局内的单源（颜色解析/队伍判定消费）。
    /// </summary>
    public class BattleSession : IDisposable
    {
        public BattleSimState Sim { get; private set; }
        public IBattleTransport Transport { get; private set; }
        public BattlePlayer Player { get; private set; }
        public RTSFlowController Flow { get; private set; }

        /// <summary>开局名册（房间 PlayerInfo 全量投影；null=调试直开）——队伍/颜色/出生点局内单源</summary>
        public List<BattlePlayerSetup> Roster { get; private set; }

        /// <summary>LAN 对局（host 或 client 端）——本地直开为 false</summary>
        public bool IsNetworked { get; private set; }

        /// <summary>Host 侧（本地模式/LAN host=true；LAN client=false——装配分支与相位源切换判据）</summary>
        public bool IsHostSide { get; private set; }

        /// <summary>装配时确定本端座位；纯本地房间开战拆线后仍保留房主座位。</summary>
        public string LocalPlayerId { get; private set; }

        private readonly BattleMessageRouter _hostRouter = new BattleMessageRouter();
        private readonly BattleMessageRouter _clientRouter = new BattleMessageRouter();
        /// <summary>F2 本地 Sim 回环路由（客户端 BattleResolver 产出的 Segment/Snapshot → View）</summary>
        private readonly BattleMessageRouter _localRouter = new BattleMessageRouter();
        private readonly Dictionary<string, HashSet<int>> _purchaseUnlocks = new Dictionary<string, HashSet<int>>();
        private Transform _logicRoot;
        private bool _disposed;

        /// <summary>客户端开局数据无法重建时，由场景结束等待并返回大厅。</summary>
        public string StartupError { get; private set; }

        // ==================== 相位/计时门面（HUD 唯一订阅口，B7 LAN） ====================
        // host/本地=RTSFlowController 权威；LAN client=BattlePlayer 消息派生。HUD 不再直接触 Flow。

        /// <summary>阶段变化（Selecting/Finished；参数=阶段、逻辑秒）</summary>
        public event Action<BattlePhase, int> OnPhaseChanged;

        public BattlePhase Phase => Flow != null ? Flow.Phase
            : (Player != null ? Player.ClientPhase : BattlePhase.Idle);

        public float SelectRemainingSeconds => Flow != null ? Flow.SelectRemainingSeconds
            : (Player != null ? Player.ClientSelectRemainingSeconds : -1f);

        /// <summary>战斗时钟（分钟）：host=Flow 权威值；client 兼容派生=按快照秒数推导
        /// （06:00 起每秒+1 分钟，决策九十五）</summary>
        public int BattleTimeMinutes => Flow != null ? Flow.BattleTimeMinutes
            : RTSFlowController.BattleClockStartMinutes
              + (BattleMetrics.BattleClockMinutesPerSecond * Mathf.Max(0, (Player?.LatestSnapshot?.turnNumber ?? 1) - 1));

        /// <summary>需人工提交行动的玩家数（决策六十七 N0 自动暂停判据单源，docs/active/43 §10）：
        /// Roster 非 AI 计数（Roster 空缺=本地调试双开 P1/P2 按 2 计）−沙盒空座（SandboxAutoPassPlayerId
        /// 即时自动 Pass 无人操作）。==1=单人局（本地/LAN 单真人+AI/试招沙盒）——浏览 UI 打开即真停
        /// 选择倒计时（冻结只影响 AI，无真人受损）。</summary>
        public int HumanActorCount
        {
            get
            {
                int count = 0;
                if (Roster != null)
                {
                    foreach (var s in Roster)
                        if (!s.IsAI) count++;
                }
                else count = 2; // 调试直开（无名册）=双端双座
                if (Sim != null && Sim.SandboxAutoPassPlayerId != null) count--;
                return count;
            }
        }

        /// <summary>单人局判据（浏览 UI 自动暂停生效口径，==需人工提交的玩家数为 1）</summary>
        public bool IsSoloHumanGame => HumanActorCount == 1;

        /// <summary>浏览 UI 冻结倒计时（N0 起兼作自动暂停通道，2026-10-13 决策六十七）：host=真暂停、
        /// client=本地倒数冻结（host 计时不随之停）。**host 权威计时仅单人局冻结**（多人局打开浏览 UI
        /// 不冻全场——倒计时照跑各端自担）；打开期间由 BattleHud.Update 每帧重估保持（跨阶段边界自洽）。
        /// LAN 单真人=host 本人（房主恒真人）→单人局真停天然落在 host 侧。</summary>
        public bool SelectTimerPaused
        {
            set
            {
                if (Flow != null) Flow.SelectTimerPaused = value && IsSoloHumanGame;
                if (Player != null) Player.ClientPhasePaused = value;
            }
        }

        // ==================== 名册查询（队伍/颜色——优先 Roster，回落 Sim） ====================

        public TeamType GetTeamOf(string playerId)
        {
            if (Roster != null)
            {
                foreach (var s in Roster)
                    if (s.PlayerId == playerId) return s.Team;
            }
            return Sim != null ? Sim.GetTeamOf(playerId) : TeamType.A;
        }

        /// <summary>房间玩家色（Roster 单源）；无 Roster 条目返回 false（调用方回落 BattlePalette 两表）</summary>
        public bool TryGetRosterColor(string playerId, out Color color)
        {
            if (Roster != null)
            {
                foreach (var s in Roster)
                {
                    if (s.PlayerId == playerId)
                    {
                        color = s.Color.ToColor();
                        return true;
                    }
                }
            }
            color = Color.white;
            return false;
        }

        // ==================== 装配 ====================

        /// <param name="seed">战斗种子（模拟核心概率事件 RNG 初始化——幸运暴击 roll；
        /// 联机 B7 两端经同一 BattleLaunchConfig 广播得到同 seed，客户端零 roll）</param>
        public static BattleSession CreateLocal(BattleMapData map, BattlePlayer player, RTSFlowController flow,
            Transform logicRoot, int seed, List<BattlePlayerSetup> roster = null)
        {
            return CreateHostCore(new LocalBattleTransport(), map, player, flow, logicRoot, seed, roster,
                isNetworked: false);
        }

        // ==================== 帧同步装配（F1，docs/active/50） ====================

        /// <summary>LAN Host 装配（Mirror 传输；Host 运行 Sim 和 AI，并负责封存输入批）。</summary>
        public static BattleSession CreateHosted(BattleMapData map, BattlePlayer player, RTSFlowController flow,
            Transform logicRoot, BattleLaunchConfig launch)
        {
            return CreateHostCore(new MirrorBattleTransport(true, launch.MatchId, launch.RemotePlayers()),
                map, player, flow, logicRoot, launch.Seed, launch.Players, isNetworked: true);
        }

        /// <summary>LAN 客户端装配：收到 BattleStart 后创建同构 Sim，表现消息只在本机回环。</summary>
        public static BattleSession CreateClientSim(BattlePlayer player, RTSFlowController flow,
            Transform logicRoot, IBattleTransport network, BattleLaunchConfig launch)
        {
            var session = new BattleSession
            {
                Transport = network,
                Player = player,
                Flow = flow,
                _logicRoot = logicRoot,
                IsNetworked = true,
                IsHostSide = false,
                Roster = launch.Players,
                LocalPlayerId = launch.LocalPlayerId,
            };

            player.Bind(network, null);
            flow.OnPhaseChanged += session.ForwardPhaseChanged;
            session._clientRouter.Register<BattleStartMessage>(BattleMessageType.BattleStart,
                msg => session.OnClientBattleStart(msg));
            session._clientRouter.Register<InputBatchMessage>(BattleMessageType.InputBatch,
                msg => flow.ReceiveInputBatch(msg));
            session._clientRouter.Register<DesyncAlertMessage>(BattleMessageType.DesyncAlert,
                msg => GICLog.Error($"[Desync] tick={msg.tick} host={msg.hostHash} client={msg.clientHash}"));
            session._clientRouter.Register<BattleOverMessage>(BattleMessageType.BattleOver, flow.ReceiveHostBattleOver);
            network.RegisterClientHandler((type, json) => session._clientRouter.Handle(type, json));
            return session;
        }

        private void OnClientBattleStart(BattleStartMessage msg)
        {
            if (Sim != null || StartupError != null || _disposed) return;
            if (msg?.map == null || msg.initialSnapshot?.units == null || msg.initialSnapshot.resources == null
                || msg.roster == null || msg.roster.Count < 2
                || !msg.roster.Any(p => p != null && !p.IsAI && p.PlayerId == LocalPlayerId))
            {
                StartupError = "房主开局数据不完整";
                return;
            }

            var sim = new BattleSimState(msg.map, msg.seed);
            sim.LogicRoot = _logicRoot;
            sim.FogMode = (BattleFogMode)msg.fogMode;
            if (!TryBuildClientSimUnits(sim, msg))
            {
                StartupError = "无法按房主开局快照重建模拟";
                return;
            }
            Roster = msg.roster;
            Sim = sim;
            Player.OnBattleStart(msg); // 先提供名册和模拟，HUD/迷雾在首次建盘时就能正确解析归属。

            // 本地 Sim 回环：BattleResolver 产出的 Segment/Snapshot/BattleOver 直接喂 View（F2）
            var loop = new ClientLoopbackTransport(Transport);
            loop.RegisterClientHandler((type, json) => _localRouter.Handle(type, json));
            _localRouter.Register<SegmentMessage>(BattleMessageType.Segment, m => Player.OnSegment(m));
            _localRouter.Register<SnapshotMessage>(BattleMessageType.Snapshot, m => Player.OnSnapshot(m));
            _localRouter.Register<BattleOverMessage>(BattleMessageType.BattleOver, m => Player.OnBattleOver(m));

            Flow.Bind(sim, loop, isHost: false, isNetworked: true);
            Flow.StartBattle();
        }

        /// <summary>客户端按 Host 同序建 Sim 单位（名册序=unitId 分配序；核心落点取快照权威位）。</summary>
        public static bool TryBuildClientSimUnits(BattleSimState sim, BattleStartMessage msg)
        {
            var unitConfig = Wargame.Instance?.Context?.Get<UnitConfig>();
            var roster = msg.roster;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pl in roster)
            {
                if (pl == null || string.IsNullOrEmpty(pl.PlayerId) || pl.MoraIncomePercent <= 0
                    || !seen.Add(pl.PlayerId)) return false;
                pl.RegisterIn(sim);
            }
            var coreData = unitConfig?.GetUnitData(UnitName.ProtocolCore);
            if (coreData == null || msg.initialSnapshot.units.Count != roster.Count
                || msg.initialSnapshot.resources.Count != roster.Count) return false;
            foreach (var pl in roster)
            {
                var coreState = msg.initialSnapshot.units.FirstOrDefault(u =>
                    u != null && u.playerId == pl.PlayerId && u.unitName == nameof(UnitName.ProtocolCore));
                var res = msg.initialSnapshot.resources.FirstOrDefault(r => r != null && r.playerId == pl.PlayerId);
                if (coreState == null || res?.handCards == null
                    || res.handCards.Exists(h => h == null || h.count <= 0)) return false;
                var core = UnitFactory.CreateUnitWithData(coreData);
                if (core == null) return false;
                core.transform.SetParent(sim.LogicRoot, false);
                if (sim.RegisterUnit(core, pl.PlayerId, pl.Team, coreState.position) != coreState.unitId)
                    return false;
                sim.RegisterHand(pl.PlayerId, new List<HandCard>());
                foreach (var h in res.handCards)
                    sim.GainCard(pl.PlayerId, h.AsCardId(), h.count, h.durability);
            }
            return true;
        }

        /// <summary>客户端回环传输：HostSend=本地 Sim 命令直喂 View；ClientSend=走网络上行。</summary>
        private sealed class ClientLoopbackTransport : IBattleTransport
        {
            private readonly IBattleTransport _network;
            private Action<BattleMessageType, string> _clientHandler;

            public ClientLoopbackTransport(IBattleTransport network) { _network = network; }

            public void HostSend(BattleMessageType type, object message)
                => _clientHandler?.Invoke(type, BattleJsonSerializer.Instance.Serialize(message));

            public void ClientSend(BattleMessageType type, object message) => _network.ClientSend(type, message);
            public void RegisterHostHandler(Action<BattleMessageType, string, string> handler) { }
            public void RegisterClientHandler(Action<BattleMessageType, string> handler) => _clientHandler = handler;
        }

        private static BattleSession CreateHostCore(IBattleTransport transport, BattleMapData map,
            BattlePlayer player, RTSFlowController flow, Transform logicRoot, int seed,
            List<BattlePlayerSetup> roster, bool isNetworked)
        {
            var sim = new BattleSimState(map, seed);
            var session = new BattleSession
            {
                Sim = sim,
                Transport = transport,
                Player = player,
                Flow = flow,
                Roster = roster,
                _logicRoot = logicRoot,
                IsNetworked = isNetworked,
                IsHostSide = true,
                LocalPlayerId = isNetworked || roster?.Any(p => p.PlayerId == PlayerID.Host) == true
                    ? PlayerID.Host
                    : roster?.FirstOrDefault(p => !p.IsAI)?.PlayerId ?? BattleDebugPlayerIds.P1,
            };
            flow.Bind(sim, transport, isHost: true, isNetworked: isNetworked,
                ownsPurchaseUnit: session.OwnsPurchaseUnit);

            // 远端上行输入保留连接身份；同进程行动直接走 Flow。
            session._hostRouter.Register<InputCommandMessage>(BattleMessageType.InputCommand,
                (msg, senderId) => flow.ReceiveInputCommand(msg, senderId));
            session._hostRouter.Register<HashReportMessage>(BattleMessageType.HashReport,
                msg => flow.ReceiveHashReport(msg));
            transport.RegisterHostHandler(session._hostRouter.Handle);

            // Client 侧路由（Host 下行：开局 / 快照 / 片命令块 / 秒拍结束段）——注册单源化（T6）
            RegisterClientRoutes(session, transport, player);

            player.Bind(transport, sim.Map);
            // （BindFlow 两态立牌布局〔散开/收拢〕已随回合机退役——即时制立牌恒散开态自由站位，
            // docs/active/48 §2.4；BattlePlayer.BindFlow 方法保留待 P4 清理，未绑定=_flow 恒 null 守卫全生效）

            // Flow 权威相位 → 门面事件（HUD 订阅单源）
            flow.OnPhaseChanged += session.ForwardPhaseChanged;
            return session;
        }

        private void ForwardPhaseChanged(BattlePhase phase, int turn) => OnPhaseChanged?.Invoke(phase, turn);

        /// <summary>客户端路由注册单源（2026-10-25 复审批 T6：CreateClient 与 CreateHostCore 两处
        /// 逐字重复收口——新增下行消息类型只改此处，勿再双写）</summary>
        private static void RegisterClientRoutes(BattleSession session, IBattleTransport transport, BattlePlayer player)
        {
            session._clientRouter.Register<BattleStartMessage>(BattleMessageType.BattleStart,
                msg => player.OnBattleStart(msg));
            session._clientRouter.Register<SnapshotMessage>(BattleMessageType.Snapshot,
                msg => player.OnSnapshot(msg));
            session._clientRouter.Register<SegmentMessage>(BattleMessageType.Segment,
                msg => player.OnSegment(msg));
            session._clientRouter.Register<BattleOverMessage>(BattleMessageType.BattleOver,
                msg => player.OnBattleOver(msg)); // S10 全灭软停：胜负广播
            transport.RegisterClientHandler((type, json) =>
            {
                // Host 已持有封存批，本机视图无需接收网络输入批回环。
                if (type != BattleMessageType.InputBatch) session._clientRouter.Handle(type, json);
            });
        }

        // ==================== 对局流程 ====================

        /// <summary>
        /// 广播开局（地形全量 + 初始快照）并进入即时制常驻选择阶段（第 1 秒）
        /// </summary>
        public void StartBattle()
        {
            if (_disposed || !IsHostSide || Flow.Phase != BattlePhase.Idle) return;
            Transport.HostSend(BattleMessageType.BattleStart, new BattleStartMessage
            {
                map = Sim.Map,
                initialSnapshot = Sim.TakeSnapshot(0),
                seed = Sim.Seed,
                roster = Roster,
                fogMode = (int)Sim.FogMode,
            });
            Flow.StartBattle();
        }

        /// <summary>
        /// 结束战斗（清空即时行动队列；状态机置 Finished）
        /// </summary>
        public void StopBattle()
        {
            if (Flow != null && Flow.Phase != BattlePhase.Finished)
                Flow.Stop();
        }

        /// <summary>关闭模拟与消息路由；退场和销毁均可安全调用。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (Flow != null)
            {
                Flow.OnPhaseChanged -= ForwardPhaseChanged;
                StopBattle();
            }
            Transport?.RegisterClientHandler(null);
            Transport?.RegisterHostHandler(null);
            (Transport as IDisposable)?.Dispose();
        }

        // ==================== 客户端入口 ====================

        public bool SubmitAction(ActionData action) => Flow != null && Flow.OnSubmitAction(action);

        /// <summary>开局装配时复制收藏许可，仅供本局房主接纳购买，不进入模拟或开局广播。</summary>
        internal void SetPurchaseUnlocks(string playerId, IEnumerable<int> ownedUnitIds)
        {
            if (ownedUnitIds != null) _purchaseUnlocks[playerId] = new HashSet<int>(ownedUnitIds);
        }

        private bool OwnsPurchaseUnit(string playerId, int unitId)
            => _purchaseUnlocks.TryGetValue(playerId, out var ownedUnits) && ownedUnits.Contains(unitId);

        // ==================== 调试单位生成 ====================

        /// <summary>
        /// 生成调试单位（复用 UnitFactory + UnitConfig 真实数据；技能由 InitSkills 按
        /// UnitConfig.skills 顺序自动创建（B4 正式技能链），DebugAttackSkill 已退役；
        /// 逻辑单位挂入隐藏 LogicRoot——仅 Host 侧持有逻辑，表现层走 UnitView）
        /// </summary>
        public string SpawnDebugUnit(UnitName unitName, string playerId, TeamType team, BattleCell cell)
        {
            // 配置统一走 DI 容器 [Bean] 缓存（ConfigManager 产出；2026-09-23 审查 Y10 收口，勿 Resources.Load 旁路）
            var config = Wargame.Instance?.Context?.Get<UnitConfig>();
            if (config == null)
            {
                GICLog.Error("[BattleSession] 容器无 UnitConfig（ConfigManager 未构建？），无法生成调试单位");
                return null;
            }

            var data = config.GetUnitData(unitName);
            if (data == null)
            {
                GICLog.Error($"[BattleSession] UnitConfig 中不存在 {unitName}");
                return null;
            }

            // Awake 在激活态完成组件收集后再挂入隐藏根
            var unit = UnitFactory.CreateUnitWithData(data);
            if (unit == null) return null;

            if (_logicRoot != null)
                unit.transform.SetParent(_logicRoot, false);

            return Sim.RegisterUnit(unit, playerId, team, cell);
        }

        /// <summary>试招沙盒单位生成（2026-10-05 时轮编辑器「开一把试招」）：同 SpawnDebugUnit 真实链，
        /// 生成后置层级覆盖星（默认 5=魔神档玩家全手操全部技能——操控分层按星级的既定语义，
        /// 数值回落公式仍读原星=数值原味；TierOverrideStars 是 Unit 运行时实例位，零资产污染随战斗回收）</summary>
        public string SpawnSandboxUnit(UnitName unitName, string playerId, TeamType team, BattleCell cell, int tierStars = 5)
        {
            var unitId = SpawnDebugUnit(unitName, playerId, team, cell);
            if (unitId == null) return null;
            var unit = Sim.GetUnit(unitId);
            if (unit != null)
                unit.TierOverrideStars = tierStars;
            return unitId;
        }

        public void RegisterDebugPlayer(string playerId)
        {
            Sim.RegisterPlayer(playerId);
        }
    }
}
