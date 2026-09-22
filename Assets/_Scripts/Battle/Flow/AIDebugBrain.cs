using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// AI 玩家脑（B6b 重构）：操控己方**高级单位**（3~5 星，docs/04 §4.1）——低级单位已改由
    /// LowUnitBrain 自主决策（不再混管）。AI 决策由 Host 生成（docs/18 决策一）——直接读
    /// BattleSimState，不走传输通道；选择阶段开始后延迟提交（给玩家看清回合切换）。
    /// v1 启发式（拍板）：战技可命中→战技 → 元能满→爆发 → 否则朝最近敌移动 → 无可动→Pass。
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
        /// v1 启发式（拍板优先级）：遍历己方存活高级单位（unitId 升序稳定）——
        /// ① 战技方向可命中 → 该单位战技朝敌；② 元能够放爆发 → 该单位爆发朝最近敌方向；
        /// 都不满足 → 第一个存活单位朝最近敌移动（切比雪夫全距离，战棋行动）；
        /// 无可用单位 → Pass（超时通道同款，不指向单位）
        /// </summary>
        private ActionData Decide(int turn)
        {
            var sim = _session.Sim;
            var majors = new List<(string unitId, Unit unit)>();
            foreach (var kv in sim.Units)
            {
                if (!BattleHeuristics.IsMajorUnit(kv.Value)) continue;
                if (BattleSimState.IsDead(kv.Value) || !BattleSimState.CanAct(kv.Value)) continue;
                var id = kv.Value.GetUnitComponent<UnitIdentity>();
                if (id == null || id.OwnerPlayerID != _playerId) continue;
                majors.Add((kv.Key, kv.Value));
            }
            // unitId 升序（枚举序铁律，决策确定性）
            majors.Sort((a, b) => string.CompareOrdinal(a.unitId, b.unitId));

            if (majors.Count == 0)
                return Pass(turn);

            foreach (var (unitId, unit) in majors)
            {
                // ① 战技可命中（直线方向有敌格）
                int skillIndex = BattleHeuristics.FindSkillIndex(unit, SkillType.Normal);
                if (skillIndex >= 0)
                {
                    var direction = BattleHeuristics.FindLineSkillDirection(sim, unit, skillIndex);
                    if (direction != 0)
                        return Skill(unit, skillIndex, direction, turn);
                }

                // ② 元能够放爆发（门槛=技能 EnergyCost，B6a；朝最近敌所在方向放）
                int burstIndex = BattleHeuristics.FindSkillIndex(unit, SkillType.Burst);
                if (burstIndex >= 0)
                {
                    int cost = BattleSimState.GetEnergyCost(unit.RawData.skills[burstIndex]);
                    if (BattleSimState.HasEnoughEnergy(unit, cost))
                    {
                        var enemy = BattleHeuristics.FindNearestEnemy(sim, unit);
                        if (enemy != null)
                        {
                            var selfPos = sim.GetPosition(unit);
                            var enemyPos = sim.GetPosition(enemy);
                            return Skill(unit, burstIndex,
                                BattleHeuristics.DeltaToDirection(enemyPos.x - selfPos.x, enemyPos.y - selfPos.y),
                                turn);
                        }
                    }
                }
            }

            // ③ 第一个存活单位朝最近敌移动（切比雪夫全距离）
            var first = majors[0];
            var enemyFirst = BattleHeuristics.FindNearestEnemy(sim, first.unit);
            if (enemyFirst == null)
                return Pass(turn);
            var selfPosition = sim.GetPosition(first.unit);
            var enemyPosition = sim.GetPosition(enemyFirst);
            int dx = enemyPosition.x - selfPosition.x;
            int dy = enemyPosition.y - selfPosition.y;
            if (dx == 0 && dy == 0)
                return Pass(turn); // 与敌人同格（叠加）：无移动意义，空过
            return new ActionData
            {
                playerId = _playerId,
                unitId = first.unitId,
                actionType = ActionType.Move,
                direction = BattleHeuristics.DeltaToDirection(dx, dy),
                moveMagnitude = Math.Max(Math.Abs(dx), Math.Abs(dy)),
                turnNumber = turn,
            };
        }

        private ActionData Skill(Unit unit, int skillIndex, Direction2D direction, int turn)
        {
            return new ActionData
            {
                playerId = _playerId,
                unitId = unit.GetUnitComponent<UnitIdentity>()?.UnitID,
                actionType = ActionType.Skill,
                skillIndex = skillIndex,
                direction = direction,
                turnNumber = turn,
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
