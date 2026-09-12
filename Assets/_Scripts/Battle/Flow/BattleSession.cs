using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
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
    /// B7 LAN 时仅传输实现换 Mirror，两侧路由/消息不变（docs/22 §4）。
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

        public static BattleSession CreateLocal(BattleMapData map, BattlePlayer player, TurnFlowController flow, Transform logicRoot)
        {
            var transport = new LocalBattleTransport();
            var sim = new BattleSimState(map);
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
            transport.RegisterClientHandler((type, json) => session._clientRouter.Handle(type, json));

            player.Bind(transport, sim.Map);
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
        /// 生成调试单位（复用 UnitFactory + UnitConfig 真实数据 + DebugAttackSkill；
        /// 逻辑单位挂入隐藏 LogicRoot——仅 Host 侧持有逻辑，表现层走 UnitView）
        /// </summary>
        public string SpawnDebugUnit(UnitName unitName, string playerId, TeamType team, BattleCell cell)
        {
            var config = Resources.Load<UnitConfig>("Configs/UnitConfig");
            if (config == null)
            {
                GICLog.Error("[BattleSession] 未找到 Resources/Configs/UnitConfig");
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

            unit.AddSkill(new DebugAttackSkill());
            if (_logicRoot != null)
                unit.transform.SetParent(_logicRoot, false);

            return Sim.RegisterUnit(unit, playerId, team, cell);
        }

        public void RegisterDebugPlayer(string playerId)
        {
            Sim.RegisterPlayer(playerId);
        }
    }
}
