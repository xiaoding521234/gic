using System;
using System.Collections;
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
    /// B1 AI 决策占位：恒定攻击最近敌人（docs/22 §5；B6 换启发式）。
    /// AI 决策由 Host 生成（docs/18 决策一）——直接读 BattleSimState，不走传输通道。
    /// 选择阶段开始后延迟提交（给玩家看清回合切换），每回合一个行动。
    /// </summary>
    public class AIDebugBrain : MonoBehaviour
    {
        private BattleSession _session;
        private string _playerId;
        private int _lastSubmittedTurn = -1;

        /// <summary>AI 提交延迟（秒）——模拟"思考"，同时让回合切换肉眼可辨</summary>
        private const float SubmitDelaySeconds = 0.8f;

        public void Bind(BattleSession session, string playerId)
        {
            _session = session;
            _playerId = playerId;
            session.Flow.OnPhaseChanged += OnPhaseChanged;
        }

        private void OnDestroy()
        {
            if (_session != null && _session.Flow != null)
                _session.Flow.OnPhaseChanged -= OnPhaseChanged;
        }

        private void OnPhaseChanged(BattlePhase phase, int turn)
        {
            if (phase != BattlePhase.Selecting) return;
            if (turn == _lastSubmittedTurn) return; // 同回合防重复提交
            StartCoroutine(SubmitActionRoutine(turn));
        }

        private IEnumerator SubmitActionRoutine(int turn)
        {
            yield return new WaitForSeconds(SubmitDelaySeconds);

            // 回合已推进（战斗结束/状态机停止）则放弃
            if (_session == null || _session.Flow == null) yield break;
            if (_session.Flow.Phase != BattlePhase.Selecting || _session.Flow.TurnNumber != turn) yield break;

            var action = Decide(turn);
            _session.SubmitAction(action);
            _lastSubmittedTurn = turn;
        }

        /// <summary>
        /// 固定脚本：第一个存活单位战技攻击最近的存活敌人；无可用目标则空过
        /// </summary>
        private ActionData Decide(int turn)
        {
            var sim = _session.Sim;
            var identity = default(UnitIdentity);

            // 找己方第一个存活单位
            Unit ownUnit = null;
            foreach (var kv in sim.Units)
            {
                var unit = kv.Value;
                var id = unit.GetUnitComponent<UnitIdentity>();
                if (id == null || id.OwnerPlayerID != _playerId) continue;
                if (BattleSimState.IsDead(unit) || !BattleSimState.CanAct(unit)) continue;
                if (ownUnit == null)
                {
                    ownUnit = unit;
                    identity = id;
                }
            }

            if (ownUnit == null || identity == null)
                return Pass(turn);

            // 最近存活敌人（曼哈顿距离）
            Unit target = null;
            int bestDistance = int.MaxValue;
            foreach (var kv in sim.Units)
            {
                var unit = kv.Value;
                var id = unit.GetUnitComponent<UnitIdentity>();
                if (id == null || id.OwnerPlayerID == _playerId) continue;
                if (BattleSimState.IsDead(unit)) continue;

                var posA = sim.GetPosition(unit);
                var posB = sim.GetPosition(ownUnit);
                int distance = Math.Abs(posA.x - posB.x) + Math.Abs(posA.y - posB.y);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    target = unit;
                }
            }

            if (target == null)
                return Pass(turn);

            return new ActionData
            {
                playerId = _playerId,
                unitId = identity.UnitID,
                actionType = ActionType.Skill,
                turnNumber = turn,
                targetUnitId = target.GetUnitComponent<UnitIdentity>()?.UnitID,
                skillIndex = 0,
            };
        }

        private ActionData Pass(int turn)
        {
            return new ActionData
            {
                playerId = _playerId,
                actionType = ActionType.Pass,
                turnNumber = turn,
            };
        }
    }
}
