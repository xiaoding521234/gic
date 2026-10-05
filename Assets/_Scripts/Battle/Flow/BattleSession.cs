using System;
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
    /// 一场对局的组合根：Host（逻辑）与 Client（表现）同进程装配（B1-B6 本地模式）。
    /// B7 LAN 时仅传输实现换 Mirror，两侧路由/消息不变（docs/active/22 §4）。
    /// 铁律：战斗逻辑不走 EventBusHub，一切消息经 IBattleTransport 专用通道。
    /// </summary>
    public class BattleSession
    {
        public BattleSimState Sim { get; private set; }
        public IBattleTransport Transport { get; private set; }
        public BattlePlayer Player { get; private set; }
        public TurnFlowController Flow { get; private set; }

        private readonly BattleMessageRouter _hostRouter = new BattleMessageRouter();
        private readonly BattleMessageRouter _clientRouter = new BattleMessageRouter();
        private Transform _logicRoot;

        // ==================== 装配 ====================

        /// <param name="seed">战斗种子（模拟核心概率事件 RNG 初始化——幸运暴击 roll；
        /// 联机 B7 两端经同一 BattleLaunchConfig 广播得到同 seed，客户端零 roll）</param>
        public static BattleSession CreateLocal(BattleMapData map, BattlePlayer player, TurnFlowController flow,
            Transform logicRoot, int seed)
        {
            var transport = new LocalBattleTransport();
            var sim = new BattleSimState(map, seed);
            flow.Bind(sim, transport);

            var session = new BattleSession
            {
                Sim = sim,
                Transport = transport,
                Player = player,
                Flow = flow,
                _logicRoot = logicRoot,
            };

            // Host 侧路由（客户端上行：行动上交 / ack / 即时行动）
            session._hostRouter.Register<SubmitActionRequest>(BattleMessageType.SubmitAction,
                req => flow.OnSubmitAction(req.action));
            session._hostRouter.Register<SegmentAck>(BattleMessageType.SegmentAck,
                ack => flow.OnSegmentAck(ack.turnNumber, ack.sliceIndex));
            session._hostRouter.Register<SubmitInstantAction>(BattleMessageType.SubmitInstantAction,
                req => session.HandleInstantSubmit(req.action));
            transport.RegisterHostHandler((type, json) => session._hostRouter.Handle(type, json));

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
            player.BindFlow(flow); // 两态模型：立牌布局随回合阶段切换（散开/收拢，docs/active/22 §11）
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
            if (Flow.Phase != BattlePhase.Finished)
                Flow.Stop();
        }

        // ==================== 客户端入口 ====================

        public void SubmitAction(ActionData action)
        {
            action.turnNumber = Flow.TurnNumber;
            Transport.ClientSend(BattleMessageType.SubmitAction, new SubmitActionRequest { action = action });
        }

        public void SubmitInstantAction(ActionData action)
        {
            action.turnNumber = Flow.TurnNumber;
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

            var unit = Sim.GetUnit(action.unitId);
            if (unit == null || BattleSimState.IsDead(unit) || !BattleSimState.CanAct(unit))
            {
                GICLog.Warn($"[BattleSession] 即时行动单位 {action?.unitId} 不可用，丢弃");
                return;
            }

            // 归属校验（2026-09-25 三轮审查 S3：上交通道有、此处漏——B7 LAN 下客户端可上交
            // 他人单位的即时行动=作弊口）+ 眷属防线（操控分层 docs/active/32：眷属=启发式脑 AI 域
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
