using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
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
    /// 五阶段完整版 AI 决策→玩家选择→玩家执行→AI 执行→回合结束 B6 落地，docs/active/22 §5）
    /// </summary>
    public class TurnFlowController : MonoBehaviour
    {
        public BattlePhase Phase { get; private set; } = BattlePhase.Idle;
        public int TurnNumber { get; private set; } = 0;

        // ==================== 战斗独立时间系统（2026-09-14 用户拍板） ====================
        // 战斗地图不与全局游戏时间共用：独立时钟初始 6:00 整，每个回合结束 +20 分钟。
        // 时段边界沿用 TimeUtility.DayStartHour/DayEndHour（8:00-20:00 白天，与全局 TimePeriod
        // 语义一致），驱动战斗地图音乐昼夜选池等表现。

        public const int BattleClockStartMinutes = 6 * 60;
        public const int BattleClockMinutesPerTurn = 20;

        /// <summary>战斗内时间（当天分钟数，0-1439）</summary>
        public int BattleTimeMinutes { get; private set; } = BattleClockStartMinutes;

        /// <summary>战斗时段（独立时钟推导；TurnFlow 未就绪方回退全局时段）</summary>
        public TimePeriod BattleTimePeriod
        {
            get
            {
                int hour = BattleTimeMinutes / 60 % 24;
                return hour >= TimeUtility.DayStartHour && hour < TimeUtility.DayEndHour
                    ? TimePeriod.Daytime
                    : TimePeriod.Night;
            }
        }

        /// <summary>回合结束推进独立时钟（片循环演算完毕、进入下一回合选择阶段前调用）</summary>
        private void AdvanceBattleClock()
        {
            BattleTimeMinutes = (BattleTimeMinutes + BattleClockMinutesPerTurn) % (24 * 60);
        }

        // ==================== 选择时限（docs/04 §4.2，B6 落地） ====================
        // 第 1 回合 25 秒；2~6 回合 16 秒；第 7 回合起每回合 -0.5 秒，下限 8 秒（第 22 回合触底）。
        // 超时行为 docs 未明确定义，按"未交玩家自动上交 Pass（空过）"实现——待用户拍板确认。

        public const float FirstTurnSelectSeconds = 25f;
        public const float BaseSelectSeconds = 16f;
        public const float SelectShrinkPerTurn = 0.5f;
        public const float MinSelectSeconds = 8f;

        /// <summary>第 N 回合选择阶段时限（秒），公式 docs/04 §4.2</summary>
        public static float GetSelectLimitSeconds(int turn)
        {
            if (turn <= 1) return FirstTurnSelectSeconds;
            if (turn <= 6) return BaseSelectSeconds;
            return Mathf.Max(MinSelectSeconds, BaseSelectSeconds - (turn - 6) * SelectShrinkPerTurn);
        }

        /// <summary>选择阶段剩余秒数（供 HUD 轮询；-1 = 非选择阶段）</summary>
        public float SelectRemainingSeconds { get; private set; } = -1f;

        private Coroutine _selectTimerCoroutine;

        /// <summary>选择阶段倒计时：到时未交玩家自动空过（Pass）并进入执行阶段</summary>
        private IEnumerator SelectTimerRoutine(int turn)
        {
            while (SelectRemainingSeconds > 0f)
            {
                yield return null;
                // 阶段已推进（收齐提前开演 / 战斗停止）→ 计时作废
                if (Phase != BattlePhase.Selecting || TurnNumber != turn) yield break;
                SelectRemainingSeconds -= Time.deltaTime;
            }

            if (Phase != BattlePhase.Selecting || TurnNumber != turn) yield break;
            SelectRemainingSeconds = 0f;
            GICLog.Warn($"[TurnFlow] 回合 {turn} 选择阶段超时，未交玩家自动空过");
            foreach (var pid in _sim.PlayerIds)
            {
                if (_pendingActions.ContainsKey(pid)) continue;
                _pendingActions[pid] = new ActionData
                {
                    playerId = pid,
                    actionType = ActionType.Pass,
                    turnNumber = turn,
                };
            }
            TryBeginResolve();
        }

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
            BattleTimeMinutes = BattleClockStartMinutes; // 独立时钟复位（06:00 起）
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

            // 选择时限计时（收齐提前开演时由阶段推进作废；docs/04 §4.2）
            SelectRemainingSeconds = GetSelectLimitSeconds(TurnNumber);
            if (_selectTimerCoroutine != null) StopCoroutine(_selectTimerCoroutine);
            _selectTimerCoroutine = StartCoroutine(SelectTimerRoutine(TurnNumber));

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

            // B1 只支持 Move/Skill/Pass
            if (action.actionType != ActionType.Move && action.actionType != ActionType.Skill && action.actionType != ActionType.Pass)
            {
                GICLog.Warn($"[TurnFlow] B1 不支持行动类型 {action.actionType}，忽略");
                return;
            }

            // 空过不指向单位（超时自动 Pass / AI 无可用单位 Pass 同走此路），豁免单位校验
            if (action.actionType == ActionType.Pass)
            {
                _pendingActions[action.playerId] = action;
                TryBeginResolve();
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

            // 每玩家每回合 1 个行动：后交覆盖先交
            _pendingActions[action.playerId] = action;

            // 收齐全部玩家行动 → 进入执行阶段
            TryBeginResolve();
        }

        /// <summary>收齐全部玩家行动则进入执行阶段（提交/超时自动 Pass 共用入口）</summary>
        private void TryBeginResolve()
        {
            if (Phase != BattlePhase.Selecting) return;
            if (_pendingActions.Count < _sim.PlayerIds.Count) return;

            var actions = new List<ActionData>(_pendingActions.Values);
            if (_resolveCoroutine != null) StopCoroutine(_resolveCoroutine);
            _resolveCoroutine = StartCoroutine(ResolveTurnRoutine(actions));
        }

        private IEnumerator ResolveTurnRoutine(List<ActionData> actions)
        {
            Phase = BattlePhase.Resolving;
            SelectRemainingSeconds = -1f; // 计时终止（HUD 不再显示倒计时）
            OnPhaseChanged?.Invoke(Phase, TurnNumber);

            yield return _resolver.ResolveTurnCoroutine(TurnNumber, actions);

            _resolveCoroutine = null;
            AdvanceBattleClock(); // 回合结束：独立时间 +20 分钟（2026-09-14 用户拍板）
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
            if (_selectTimerCoroutine != null)
            {
                StopCoroutine(_selectTimerCoroutine);
                _selectTimerCoroutine = null;
            }
            SelectRemainingSeconds = -1f;
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
