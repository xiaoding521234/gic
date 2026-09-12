using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
namespace GIC.Battle
{


    /// <summary>
    /// 回合阶段
    /// </summary>
    public enum BattlePhase
    {
        Idle,       // 未开战
        Selecting,  // 选择阶段（收齐双方行动）
        Resolving,  // 执行阶段（片循环演算中）
        Finished,   // 战斗结束
    }

    /// <summary>
    /// 回合状态机（最小版：选择阶段收齐行动 → 执行阶段片循环 → 回合结束循环；
    /// 五阶段完整版 AI 决策→玩家选择→玩家执行→AI 执行→回合结束 B6 落地，docs/22 §5）
    /// </summary>
    public class TurnFlowController : MonoBehaviour
    {
        public BattlePhase Phase { get; private set; } = BattlePhase.Idle;
        public int TurnNumber { get; private set; } = 0;

        /// <summary>阶段变化通知（调试 UI 刷新用；战斗内不走 EventBus）</summary>
        public event Action<BattlePhase, int> OnPhaseChanged;

        private BattleSimState _sim;
        private IBattleTransport _transport;
        private TurnResolver _resolver;

        private readonly Dictionary<string, ActionData> _pendingActions = new Dictionary<string, ActionData>();
        private Coroutine _resolveCoroutine;

        // ack 门控状态（Host 永不跑在客户端前面）
        private string _pendingAckKey;
        private readonly HashSet<string> _ackedKeys = new HashSet<string>();

        public void Bind(BattleSimState sim, IBattleTransport transport)
        {
            _sim = sim;
            _transport = transport;
            _resolver = new TurnResolver(sim, transport, this);
        }

        // ==================== 战斗启动 ====================

        /// <summary>
        /// 开战：进入第 1 回合选择阶段
        /// </summary>
        public void StartBattle()
        {
            TurnNumber = 1;
            BeginSelectPhase();
        }

        private void BeginSelectPhase()
        {
            Phase = BattlePhase.Selecting;
            _pendingActions.Clear();
            _ackedKeys.Clear();
            _transport.HostSend(BattleMessageType.Snapshot, new SnapshotMessage
            {
                snapshot = _sim.TakeSnapshot(TurnNumber),
            });
            OnPhaseChanged?.Invoke(Phase, TurnNumber);
        }

        // ==================== 提交（Host 侧入口） ====================

        /// <summary>
        /// 行动选择上交（本地双开：双端共用一进程，两侧都走本入口）
        /// </summary>
        public void OnSubmitAction(ActionData action)
        {
            if (Phase != BattlePhase.Selecting)
            {
                GICLog.Warn($"[TurnFlow] 非选择阶段，忽略 {action}");
                return;
            }

            if (action == null || !_sim.PlayerIds.Contains(action.playerId))
            {
                GICLog.Warn($"[TurnFlow] 未知玩家 {action?.playerId}，忽略");
                return;
            }

            var unit = _sim.GetUnit(action.unitId);
            if (unit == null)
            {
                GICLog.Warn($"[TurnFlow] 未知单位 {action.unitId}，忽略");
                return;
            }

            // 归属校验
            var identity = unit.GetUnitComponent<UnitIdentity>();
            if (identity == null || identity.OwnerPlayerID != action.playerId)
            {
                GICLog.Warn($"[TurnFlow] 单位 {action.unitId} 不属于玩家 {action.playerId}，忽略");
                return;
            }

            // B1 只支持 Move/Skill/Pass
            if (action.actionType != ActionType.Move && action.actionType != ActionType.Skill && action.actionType != ActionType.Pass)
            {
                GICLog.Warn($"[TurnFlow] B1 不支持行动类型 {action.actionType}，忽略");
                return;
            }

            // 每玩家每回合 1 个行动：后交覆盖先交
            _pendingActions[action.playerId] = action;

            // 收齐全部玩家行动 → 进入执行阶段
            if (_pendingActions.Count >= _sim.PlayerIds.Count)
            {
                var actions = new List<ActionData>(_pendingActions.Values);
                if (_resolveCoroutine != null) StopCoroutine(_resolveCoroutine);
                _resolveCoroutine = StartCoroutine(ResolveTurnRoutine(actions));
            }
        }

        private IEnumerator ResolveTurnRoutine(List<ActionData> actions)
        {
            Phase = BattlePhase.Resolving;
            OnPhaseChanged?.Invoke(Phase, TurnNumber);

            yield return _resolver.ResolveTurnCoroutine(TurnNumber, actions);

            _resolveCoroutine = null;
            TurnNumber++;
            BeginSelectPhase();
        }

        /// <summary>
        /// 停止状态机（战斗结束；中断进行中的片循环协程）
        /// </summary>
        public void Stop()
        {
            if (_resolveCoroutine != null)
            {
                StopCoroutine(_resolveCoroutine);
                _resolveCoroutine = null;
            }
            Phase = BattlePhase.Finished;
            OnPhaseChanged?.Invoke(Phase, TurnNumber);
        }

        // ==================== ack 门控 ====================

        public void NotifySegmentPushed(int turnNumber, int sliceIndex)
        {
            _pendingAckKey = AckKey(turnNumber, sliceIndex);
            _ackedKeys.Remove(_pendingAckKey);
        }

        public bool HasSegmentAck(int turnNumber, int sliceIndex)
        {
            return _ackedKeys.Contains(AckKey(turnNumber, sliceIndex));
        }

        /// <summary>
        /// 客户端片播放完成确认（SegmentAck 到达）
        /// </summary>
        public void OnSegmentAck(int turnNumber, int sliceIndex)
        {
            _ackedKeys.Add(AckKey(turnNumber, sliceIndex));
        }

        private static string AckKey(int turnNumber, int sliceIndex) => $"{turnNumber}:{sliceIndex}";

        private void OnDestroy()
        {
            _pendingActions.Clear();
            _ackedKeys.Clear();
        }
    }
}
