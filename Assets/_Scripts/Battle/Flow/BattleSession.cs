using System;
using System.Collections.Generic;
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
    /// - LAN 模式（B7，2026-10-11 联机批）：Host 端 CreateHosted（Sim/Flow/AI 脑）+ MirrorBattleTransport
    ///   逐远端下发+本机直投；客户端 CreateClient（仅 BattlePlayer 表现层，Sim/Flow=null），
    ///   相位/倒计时由消息流派生（PhaseSource 门面）。
    /// 铁律：战斗逻辑不走 EventBusHub，一切消息经 IBattleTransport 专用通道。
    /// Roster（开局名册）双端同构：房间选的队伍/颜色/出生点进局内的单源（颜色解析/队伍判定消费）。
    /// </summary>
    public class BattleSession
    {
        public BattleSimState Sim { get; private set; }
        public IBattleTransport Transport { get; private set; }
        public BattlePlayer Player { get; private set; }
        public TurnFlowController Flow { get; private set; }

        /// <summary>开局名册（房间 PlayerInfo 全量投影；null=调试直开）——队伍/颜色/出生点局内单源</summary>
        public List<BattlePlayerSetup> Roster { get; private set; }

        /// <summary>LAN 对局（host 或 client 端）——本地直开为 false</summary>
        public bool IsNetworked { get; private set; }

        /// <summary>Host 侧（本地模式/LAN host=true；LAN client=false——装配分支与相位源切换判据）</summary>
        public bool IsHostSide { get; private set; }

        private readonly BattleMessageRouter _hostRouter = new BattleMessageRouter();
        private readonly BattleMessageRouter _clientRouter = new BattleMessageRouter();
        private Transform _logicRoot;

        // ==================== 相位/计时门面（HUD 唯一订阅口，B7 LAN） ====================
        // host/本地=TurnFlowController 权威；LAN client=BattlePlayer 消息派生。HUD 不再直接触 Flow。

        /// <summary>阶段变化（Selecting/Resolving/Finished；参数=阶段、回合数）</summary>
        public event Action<BattlePhase, int> OnPhaseChanged;

        /// <summary>选择倒计时归零（参数=回合数；客户端=本地倒数归零自动完成选择，host 仍权威兜底）</summary>
        public event Action<int> OnSelectTimerExpired;

        public BattlePhase Phase => Flow != null ? Flow.Phase
            : (Player != null ? Player.ClientPhase : BattlePhase.Idle);

        public float SelectRemainingSeconds => Flow != null ? Flow.SelectRemainingSeconds
            : (Player != null ? Player.ClientSelectRemainingSeconds : -1f);

        /// <summary>战斗时钟（分钟）：host=Flow 权威值；client=按快照回合数推导（06:00 起每回合+20）</summary>
        public int BattleTimeMinutes => Flow != null ? Flow.BattleTimeMinutes
            : TurnFlowController.BattleClockStartMinutes
              + (TurnFlowController.BattleClockMinutesPerTurn * Mathf.Max(0, (Player?.LatestSnapshot?.turnNumber ?? 1) - 1));

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
        public static BattleSession CreateLocal(BattleMapData map, BattlePlayer player, TurnFlowController flow,
            Transform logicRoot, int seed, List<BattlePlayerSetup> roster = null)
        {
            return CreateHostCore(new LocalBattleTransport(), map, player, flow, logicRoot, seed, roster,
                isNetworked: false);
        }

        /// <summary>LAN host 端装配（B7）：Sim/Flow 全量 + Mirror 传输（逐远端下发+本机直投）+
        /// 期望 ack 方数注入（host 本机 1 + 远端连接数）</summary>
        public static BattleSession CreateHosted(BattleMapData map, BattlePlayer player, TurnFlowController flow,
            Transform logicRoot, int seed, List<BattlePlayerSetup> roster)
        {
            var session = CreateHostCore(new MirrorBattleTransport(isHost: true), map, player, flow, logicRoot,
                seed, roster, isNetworked: true);
            session.Flow.ExpectedAckerCount = () => 1 + MirrorBattleTransport.RemoteClientCount();
            return session;
        }

        /// <summary>LAN 客户端装配（B7）：仅表现层（BattlePlayer）+ 客户端路由——Sim/Flow=null，
        /// 一切经 BattleStartMessage/Snapshot/Segment 消息驱动；相位/倒计时由消息派生</summary>
        public static BattleSession CreateClient(BattlePlayer player, List<BattlePlayerSetup> roster)
        {
            var transport = new MirrorBattleTransport(isHost: false);

            var session = new BattleSession
            {
                Transport = transport,
                Player = player,
                Roster = roster,
                IsNetworked = true,
                IsHostSide = false,
            };

            // Client 侧路由（Host 下行）——与 host 本机视图同构
            session._clientRouter.Register<BattleStartMessage>(BattleMessageType.BattleStart,
                msg => player.OnBattleStart(msg));
            session._clientRouter.Register<SnapshotMessage>(BattleMessageType.Snapshot,
                msg => player.OnSnapshot(msg));
            session._clientRouter.Register<SegmentMessage>(BattleMessageType.Segment,
                msg => player.OnSegment(msg));
            session._clientRouter.Register<TurnEndMessage>(BattleMessageType.TurnEnd,
                msg => player.OnTurnEnd(msg));
            session._clientRouter.Register<BattleOverMessage>(BattleMessageType.BattleOver,
                msg => player.OnBattleOver(msg)); // S10 全灭软停：胜负广播
            session._clientRouter.Register<TurnPlanMessage>(BattleMessageType.TurnPlan,
                msg => player.OnTurnPlan(msg)); // 行动预告（执行预览）
            transport.RegisterClientHandler((type, json) => session._clientRouter.Handle(type, json));

            player.Bind(transport, null); // map 经 BattleStartMessage 到达（客户端无 Sim）

            // 客户端相位派生 → 门面事件（HUD 订阅单源）
            player.ClientPhaseChanged += (phase, turn) => session.OnPhaseChanged?.Invoke(phase, turn);
            player.ClientSelectTimerExpired += turn => session.OnSelectTimerExpired?.Invoke(turn);
            return session;
        }

        private static BattleSession CreateHostCore(IBattleTransport transport, BattleMapData map,
            BattlePlayer player, TurnFlowController flow, Transform logicRoot, int seed,
            List<BattlePlayerSetup> roster, bool isNetworked)
        {
            var sim = new BattleSimState(map, seed);
            flow.Bind(sim, transport);

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
            };

            // Host 侧路由（客户端上行：行动上交 / ack / 即时行动）——SegmentAck 单列（携带发送方计数）
            session._hostRouter.Register<SubmitActionRequest>(BattleMessageType.SubmitAction,
                req => flow.OnSubmitAction(req.action));
            session._hostRouter.Register<SubmitInstantAction>(BattleMessageType.SubmitInstantAction,
                req => session.HandleInstantSubmit(req.action));
            transport.RegisterHostHandler((type, json, sender) =>
            {
                if (type == BattleMessageType.SegmentAck)
                {
                    var ack = BattleJsonSerializer.Instance.Deserialize<SegmentAck>(json);
                    flow.OnSegmentAck(ack.turnNumber, ack.sliceIndex, sender);
                }
                else
                {
                    session._hostRouter.Handle(type, json);
                }
            });

            // Client 侧路由（Host 下行：开局 / 快照 / 片命令块 / 回合结束）
            session._clientRouter.Register<BattleStartMessage>(BattleMessageType.BattleStart,
                msg => player.OnBattleStart(msg));
            session._clientRouter.Register<SnapshotMessage>(BattleMessageType.Snapshot,
                msg => player.OnSnapshot(msg));
            session._clientRouter.Register<SegmentMessage>(BattleMessageType.Segment,
                msg => player.OnSegment(msg));
            session._clientRouter.Register<TurnEndMessage>(BattleMessageType.TurnEnd,
                msg => player.OnTurnEnd(msg));
            session._clientRouter.Register<BattleOverMessage>(BattleMessageType.BattleOver,
                msg => player.OnBattleOver(msg)); // S10 全灭软停：胜负广播（订阅方=HUD 胜负提示）
            session._clientRouter.Register<TurnPlanMessage>(BattleMessageType.TurnPlan,
                msg => player.OnTurnPlan(msg)); // 行动预告（2026-09-29 执行预览：分桶后、首片推送前）
            transport.RegisterClientHandler((type, json) => session._clientRouter.Handle(type, json));

            player.Bind(transport, sim.Map);
            player.BindFlow(flow); // 两态模型：立牌布局随回合阶段切换（散开/收拢，docs/22 §11）

            // Flow 权威相位 → 门面事件（HUD 订阅单源）
            flow.OnPhaseChanged += (phase, turn) => session.OnPhaseChanged?.Invoke(phase, turn);
            flow.OnSelectTimerExpired += turn => session.OnSelectTimerExpired?.Invoke(turn);
            return session;
        }

        // ==================== 对局流程 ====================

        /// <summary>
        /// 广播开局（地形全量 + 初始快照）并进入第 1 回合选择阶段
        /// </summary>
        public void StartBattle()
        {
            Transport.HostSend(BattleMessageType.BattleStart, new BattleStartMessage
            {
                map = Sim.Map,
                initialSnapshot = Sim.TakeSnapshot(0),
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

        // ==================== 客户端入口 ====================

        public void SubmitAction(ActionData action)
        {
            action.turnNumber = Flow != null ? Flow.TurnNumber : (Player?.LatestSnapshot?.turnNumber ?? 1);
            Transport.ClientSend(BattleMessageType.SubmitAction, new SubmitActionRequest { action = action });
        }

        public void SubmitInstantAction(ActionData action)
        {
            action.turnNumber = Flow != null ? Flow.TurnNumber : (Player?.LatestSnapshot?.turnNumber ?? 1);
            Transport.ClientSend(BattleMessageType.SubmitInstantAction, new SubmitInstantAction { action = action });
        }

        private void HandleInstantSubmit(ActionData action)
        {
            if (action == null) return;
            if (Flow.Phase != BattlePhase.Resolving && Flow.Phase != BattlePhase.Selecting)
            {
                GICLog.Warn("[BattleSession] 非进行中状态，即时行动丢弃");
                return;
            }

            // 购买即时分支（决策八十六批E，docs/active/45 §7）：玩家级行动（无行动单位）——
            // **即时不占配额**（校验/结算/命令广播全在执行器内完成，不写 _pendingActions——
            // 购买后玩家仍可照常上交本回合行动）；失败零命令（客户端预检+灰显为主 UX）
            if (action.actionType == ActionType.BuyCard)
            {
                BuyCardExecutor.TryResolve(Sim, Transport, action);
                return;
            }

            var unit = Sim.GetUnit(action.unitId);
            if (unit == null || BattleSimState.IsDead(unit) || !BattleSimState.CanAct(unit))
            {
                GICLog.Warn($"[BattleSession] 即时行动单位 {action?.unitId} 不可用，丢弃");
                return;
            }

            // 归属校验（2026-09-25 三轮审查 S3：上交通道有、此处漏——B7 LAN 下客户端可上交
            // 他人单位的即时行动=作弊口）+ 眷属防线（操控分层 docs/32：眷属=启发式脑 AI 域
            // 不走即时通道；伙伴/魔神的技能链即时行动放行——旧 IsMajorUnit 口径=非眷属即过，恒等）
            var identity = unit.GetUnitComponent<UnitIdentity>();
            if (identity == null || identity.OwnerPlayerID != action.playerId)
            {
                GICLog.Warn($"[BattleSession] 即时行动单位 {action.unitId} 不属于 {action?.playerId}，丢弃");
                return;
            }
            if (BattleHeuristics.IsFamiliar(unit))
            {
                GICLog.Warn($"[BattleSession] 即时行动单位 {action.unitId} 为眷属（FamiliarBrain 自主决策），丢弃");
                return;
            }

            // 建筑防线（协议核心批 2026-09-29）：建筑（含 5★ 协议核心）不参与任何行动，即时通道同拒
            if (BattleHeuristics.IsBuilding(unit))
            {
                GICLog.Warn($"[BattleSession] 即时行动单位 {action.unitId} 为建筑（含协议核心），丢弃");
                return;
            }

            if (action.actionType != ActionType.Skill && action.actionType != ActionType.Move && action.actionType != ActionType.Pass)
            {
                GICLog.Warn($"[BattleSession] 即时行动类型 {action.actionType} B1 不支持，丢弃");
                return;
            }

            // 入队等片边界 poll（软窗语义：演算照走，片边界插入结算）
            Sim.EnqueueInstantAction(action);
        }

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
