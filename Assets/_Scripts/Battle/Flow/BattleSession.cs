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
        public RTSFlowController Flow { get; private set; }

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

        // （LAN host CreateHosted / LAN client CreateClient 装配已随决策九十五拍板 2 退役删除——
        // 即时制 v1 单机；实时 Mirror 同步=tick 命令流后置批次，docs/active/48 §13。
        // MirrorBattleTransport/Mirror 联机域文件保留不删，装配入口拦截=BattleScreen；
        // 房间流程 toast 化拦截与完整清理随 P4/P6 批）

        private static BattleSession CreateHostCore(IBattleTransport transport, BattleMapData map,
            BattlePlayer player, RTSFlowController flow, Transform logicRoot, int seed,
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

            // Host 侧路由（客户端上行：行动上交/即时行动）——SegmentAck 特判随 ack 门控退役删除
            session._hostRouter.Register<SubmitActionRequest>(BattleMessageType.SubmitAction,
                req => flow.OnSubmitAction(req.action));
            session._hostRouter.Register<SubmitInstantAction>(BattleMessageType.SubmitInstantAction,
                req => session.HandleInstantSubmit(req.action));
            transport.RegisterHostHandler((type, json, sender) => session._hostRouter.Handle(type, json));

            // Client 侧路由（Host 下行：开局 / 快照 / 片命令块 / 回合结束）——注册单源化（T6）
            RegisterClientRoutes(session, transport, player);

            player.Bind(transport, sim.Map);
            // （BindFlow 两态立牌布局〔散开/收拢〕已随回合机退役——即时制立牌恒散开态自由站位，
            // docs/active/48 §2.4；BattlePlayer.BindFlow 方法保留待 P4 清理，未绑定=_flow 恒 null 守卫全生效）

            // Flow 权威相位 → 门面事件（HUD 订阅单源）
            flow.OnPhaseChanged += (phase, turn) => session.OnPhaseChanged?.Invoke(phase, turn);
            flow.OnSelectTimerExpired += turn => session.OnSelectTimerExpired?.Invoke(turn);
            return session;
        }

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
            session._clientRouter.Register<TurnEndMessage>(BattleMessageType.TurnEnd,
                msg => player.OnTurnEnd(msg));
            session._clientRouter.Register<BattleOverMessage>(BattleMessageType.BattleOver,
                msg => player.OnBattleOver(msg)); // S10 全灭软停：胜负广播
            session._clientRouter.Register<TurnPlanMessage>(BattleMessageType.TurnPlan,
                msg => player.OnTurnPlan(msg)); // 行动预告（执行预览）
            transport.RegisterClientHandler((type, json) => session._clientRouter.Handle(type, json));
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
            action.submitTick = Flow != null ? Flow.CurrentTick : 0; // 决策九十五：上交时刻 tick
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
            if (Flow == null || Flow.Phase != BattlePhase.Selecting)
            {
                GICLog.Warn("[BattleSession] 战斗未运行/已终局，即时行动丢弃");
                return;
            }

            // 购买即时分支（决策八十六批E，docs/45 §7）：玩家级行动——即时即结算
            // （校验/结算/命令广播全在执行器内完成；失败零命令，客户端预检+灰显为主 UX）
            if (action.actionType == ActionType.BuyCard)
            {
                BuyCardExecutor.TryResolve(Sim, Transport, action);
                return;
            }

            // 非购买即时行动（旧片边界队列语义随回合机退役，决策九十五）：
            // 直接走 RTSFlow 权威校验+即时结算——归属/眷属/建筑/灵魂防线在 OnSubmitAction 内收口
            Flow.OnSubmitAction(action);
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
