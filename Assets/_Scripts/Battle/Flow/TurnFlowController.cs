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
    /// 回合状态机（B6b 四阶段：低级单位决策→玩家选择→统一执行→回合结束，docs/04 §4.1；
    /// 阶段一/三/四已实装，低级决策与玩家选择同处 Selecting 推流——低级行动由 Host 在
    /// 快照广播时即生成（决策先于玩家选择完成、不依赖玩家本回合选择），执行阶段统一攻速排序结算）
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

        /// <summary>选择倒计时暂停门（2026-09-21：HUD 布局编辑期冻结；true=计时挂起不递减。B7 联机时编辑模式禁用另议 docs/11）</summary>
        public bool SelectTimerPaused { get; set; }

        private Coroutine _selectTimerCoroutine;

        /// <summary>选择阶段倒计时：到时=自动完成选择——先给 HUD 一次待定确认窗口（OnSelectTimerExpired，
        /// 同按钮链路），未交玩家再自动空过（Pass）进入执行阶段</summary>
        private IEnumerator SelectTimerRoutine(int turn)
        {
            while (SelectRemainingSeconds > 0f)
            {
                yield return null;
                // 阶段已推进（收齐提前开演 / 战斗停止）→ 计时作废
                if (Phase != BattlePhase.Selecting || TurnNumber != turn) yield break;
                if (SelectTimerPaused) continue; // HUD 布局编辑期冻结（不减不超时）
                SelectRemainingSeconds -= Time.deltaTime;
            }

            if (Phase != BattlePhase.Selecting || TurnNumber != turn) yield break;
            SelectRemainingSeconds = 0f;
            GICLog.Warn($"[TurnFlow] 回合 {turn} 选择阶段超时——自动完成选择，未交玩家空过");
            // 倒计时归零=自动按下「完成选择」（2026-09-26 拍板「统一复用链路」）：Pass 兜底填充前
            // 先给订阅方一次同步上交窗口——HUD 的瞄准待定金格随超时自动确认（复用按钮链路）；
            // 本地同进程同步回调保序（事件内提交可能已触发提前开演，则下方填充空转+TryBeginResolve 守卫兜底）
            OnSelectTimerExpired?.Invoke(turn);
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

        /// <summary>选择阶段倒计时归零事件（参数=回合数；触发点=超时 Pass 兜底填充**之前**——
        /// 订阅方可同步上交待定行动：HUD 瞄准待定金格=自动按下完成选择（2026-09-26 拍板
        /// 「倒计时结束时应当相当于按下了完成选择按钮，统一复用链路」）。本地同进程同步回调保序；
        /// B7 LAN 分端时 Host 超时兜底语义不变，客户端迟到上交按超时丢弃）</summary>
        public event Action<int> OnSelectTimerExpired;

        private BattleSimState _sim;
        private IBattleTransport _transport;
        private TurnResolver _resolver;

        private readonly Dictionary<string, ActionData> _pendingActions = new Dictionary<string, ActionData>();

        /// <summary>低级单位（1~2星）本回合自主行动（B6b：Host 在选择阶段头生成——
        /// docs/04 §4.1 低级单位决策先于玩家选择完成；不占玩家行动配额）</summary>
        private List<ActionData> _minorUnitActions = new List<ActionData>();

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

            // 阶段一：低级单位决策（先于玩家选择完成、只决策不结算——快照广播时即定，
            // 玩家选择期间看不见其行动内容，docs/04 §4.1）
            _minorUnitActions = LowUnitBrain.DecideAll(_sim, TurnNumber);

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

            // 完成选择定死（2026-09-26 拍板「当先点完成选择确认行动后，就应当定死了」）：每玩家
            // 每回合首份上交即定稿，重复上交一律忽略（旧「后交覆盖先交」语义废除——HUD 侧按钮
            // 置灰单提交为第一道，此处 Host 权威双保险，B7 LAN 客户端重复上交也进不来）
            if (_pendingActions.ContainsKey(action.playerId))
            {
                GICLog.Warn($"[TurnFlow] 玩家 {action.playerId} 本回合行动已定稿，忽略重复上交 {action.actionType}");
                return;
            }

            // B1 只支持 Move/Skill/Pass；B6c 加 DeployUnit（出战占玩家行动配额，docs/18 决策七）
            if (action.actionType != ActionType.Move && action.actionType != ActionType.Skill
                && action.actionType != ActionType.Pass && action.actionType != ActionType.DeployUnit)
            {
                GICLog.Warn($"[TurnFlow] 不支持行动类型 {action.actionType}，忽略");
                return;
            }

            // 空过/出战不指向行动单位（玩家级行动：超时自动 Pass / AI 无可用单位 Pass / 手牌出战同走此路），豁免单位校验
            if (action.actionType == ActionType.Pass || action.actionType == ActionType.DeployUnit)
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

            // 低级单位防线（2026-09-25 三轮审查 C1）：1~2 星单位行动由 LowUnitBrain 自主决策
            // （不占玩家配额、不走玩家上交通道）——玩家上交低级单位为行动者会与 _minorUnitActions
            // 同单位双行动（双 mover 并发推进=移动翻倍/写回互踩/双 Move 命令）。UI 侧已同步过滤
            // （BattleHud 不可选中低级单位），此处 Host 权威兜底（B7 LAN 客户端可凭空上交，双保险）
            if (!BattleHeuristics.IsMajorUnit(unit))
            {
                GICLog.Warn($"[TurnFlow] 低级单位 {action.unitId} 不接受玩家上交行动（LowUnitBrain 自主决策），忽略");
                return;
            }

            // 每玩家每回合 1 个行动：首份定稿（重复上交已在入口忽略——完成选择定死拍板）
            _pendingActions[action.playerId] = action;

            // 收齐全部玩家行动 → 进入执行阶段
            TryBeginResolve();
        }

        /// <summary>收齐全部玩家行动则进入执行阶段（提交/超时自动 Pass 共用入口）</summary>
        private void TryBeginResolve()
        {
            if (Phase != BattlePhase.Selecting) return;
            if (_pendingActions.Count < _sim.PlayerIds.Count) return;

            // 统一执行阶段：玩家行动 + 低级单位自主行动合并（全部按行动者攻速排序结算，docs/04 §4.1）
            var actions = new List<ActionData>(_pendingActions.Values);
            actions.AddRange(_minorUnitActions);
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

            // 全灭软停（2026-09-25 三轮审查 S10 轻量版；胜负判定/结算画面=B8）：恰好一队全灭→
            // 停回合循环+广播胜负（避免全灭后回合空转继续发资源、AI 继续空过）。
            // 双方同回合互灭不下发（结算语义 B8 定义）。
            // 顺序：先阶段事件（HUD 会设 Tip=执行中）再发 BattleOver（客户端覆盖 Tip=胜负），勿颠倒
            var overWinner = CheckBattleOver();
            if (overWinner.HasValue)
            {
                Phase = BattlePhase.Finished;
                SelectRemainingSeconds = -1f;
                OnPhaseChanged?.Invoke(Phase, TurnNumber);
                _transport.HostSend(BattleMessageType.BattleOver, new BattleOverMessage
                {
                    winnerTeam = (int)overWinner.Value,
                });
                yield break; // 不再进入下一回合选择阶段
            }

            AdvanceBattleClock(); // 回合结束：独立时间 +20 分钟（2026-09-14 用户拍板）
            TurnNumber++;
            BeginSelectPhase();
        }

        /// <summary>全灭检测：恰好一队存活时返回该队（=胜方）；双方仍活/双方互灭返回 null。
        /// 多人局同队玩家去重判定（2v2 一队两玩家）</summary>
        private TeamType? CheckBattleOver()
        {
            TeamType? aliveTeam = null;
            foreach (var pid in _sim.PlayerIds)
            {
                var team = _sim.GetTeamOf(pid);
                if (aliveTeam == team) continue; // 同队已查过（2v2）
                if (!_sim.HasLivingUnits(team)) continue;
                if (aliveTeam.HasValue) return null; // 两队都存活：战斗继续
                aliveTeam = team;
            }
            return aliveTeam; // 恰好一队存活=胜方；双方全灭=null
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
