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
    /// 回合状态机（D 批次操控分层五阶段，docs/active/32 §3 / docs/04 §4.1）：
    /// 眷属决策（1-2★ 启发式脑，选择阶段头 Host 内部生成）→ 玩家选择（所有玩家同时提交 1 配额：
    /// 号令/部署/升命/购买/物品/魔神操作/空过）→ **伙伴决策（3-4★ 评分制脑——收齐全部玩家选择后、
    /// 开演前的机器瞬时阶段：感知己方已提交选择、跳过已被号令单位，无选择时限无 UI 态）** →
    /// 统一执行（眷属+伙伴+玩家配额行动按攻速排序）→ 回合结束。
    /// 「覆盖」机制不采用（玩家先选、伙伴后决策=天然无替换，伙伴决策额外获得己方玩家意图输入）；
    /// 零新协议（伙伴决策=Host 内部阶段，与眷属决策同性质；本地双开同进程语义一致，LAN 无泄露路径）。
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
        // 超时行为已拍板（2026-09-26，docs/18 决策六）：先触发 OnSelectTimerExpired（HUD 无条件复用
        // 完成选择按钮链路=待定金格自动确认），未交玩家再自动上交 Pass（空过）。

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

        /// <summary>眷属单位（1~2星）本回合自主行动（B6b 起：Host 在选择阶段头生成——
        /// docs/04 §4.1 眷属决策先于玩家选择完成；不占玩家行动配额、不感知玩家本回合选择；
        /// D-6 术语迁移改名 _minorUnitActions→_familiarUnitActions）</summary>
        private List<ActionData> _familiarUnitActions = new List<ActionData>();

        private Coroutine _resolveCoroutine;

        // ack 门控状态（Host 永不跑在客户端前面）
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

            // 阶段一：眷属决策（1-2★ 启发式脑——先于玩家选择完成、只决策不结算，快照广播时即定，
            // 玩家选择期间看不见其行动内容，docs/04 §4.1；D-6 已随术语迁移改名 FamiliarBrain）
            _familiarUnitActions = FamiliarBrain.DecideAll(_sim, TurnNumber);

            _transport.HostSend(BattleMessageType.Snapshot, new SnapshotMessage
            {
                snapshot = _sim.TakeSnapshot(TurnNumber),
            });

            // 选择时限计时（收齐提前开演时由阶段推进作废；docs/04 §4.2）
            SelectRemainingSeconds = GetSelectLimitSeconds(TurnNumber);
            if (_selectTimerCoroutine != null) StopCoroutine(_selectTimerCoroutine);
            _selectTimerCoroutine = StartCoroutine(SelectTimerRoutine(TurnNumber));

            // 试招沙盒空座即时 Pass（2026-10-05 时轮编辑器「开一把试招」）：木桩方无真人无配额脑——
            // 选择阶段一开即交 Pass（不等超时兜底），试招方节奏不被每回合 8~25s 白等拖累
            if (_sim.SandboxAutoPassPlayerId != null && !_pendingActions.ContainsKey(_sim.SandboxAutoPassPlayerId))
            {
                _pendingActions[_sim.SandboxAutoPassPlayerId] = new ActionData
                {
                    playerId = _sim.SandboxAutoPassPlayerId,
                    actionType = ActionType.Pass,
                    turnNumber = TurnNumber,
                };
                TryBeginResolve(); // 试招方已交时收齐即开演；未交=直通等其提交
            }

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

            // 回合号校验（2026-09-27 复审修复，LAN 纵深二道防线）：本地模式 BattleSession.SubmitAction
            // 提交时即写当前回合恒等价、零行为变化；B7 分端后拦截错轮/迟到重放上交（阶段门之外）
            if (action.turnNumber != TurnNumber)
            {
                GICLog.Warn($"[TurnFlow] 上交回合号 {action.turnNumber} ≠ 当前 {TurnNumber}，忽略（错轮/迟到上交）");
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

            // 建筑防线（协议核心批 2026-09-29 拍板）：建筑（含协议核心）不参与任何行动——
            // 无技能无移动无配额，玩家上交一律拒绝（B7 LAN 防作弊同构；5★ 核心勿落进魔神档）
            if (unit.RawData != null && unit.RawData.unitType == UnitType.Building)
            {
                GICLog.Warn($"[TurnFlow] 建筑 {action.unitId}（含协议核心）不接受任何玩家上交行动，忽略");
                return;
            }

            // 单位级行动域校验（D 批次操控分层，docs/active/32 §1/§4）：眷属=AI 域（启发式脑自主，
            // 不占配额不走玩家通道）；伙伴=AI 域——玩家仅可**号令**（势力技能 Enso/Contract，占 1 配额），
            // 移动/战技/爆发上交会与伙伴自主决策同单位双行动（双 mover 并发推进=移动翻倍/写回互踩），
            // 拒绝；魔神=玩家全手操（现行行为）。UI 侧技能盘层级门控（D-5）为第一道，此处 Host 权威
            // 兜底（B7 LAN 客户端可凭空上交，双保险）
            var tier = BattleHeuristics.TierOf(unit);
            if (tier == UnitTier.Familiar)
            {
                GICLog.Warn($"[TurnFlow] 眷属 {action.unitId} 不接受玩家上交行动（启发式脑自主决策），忽略");
                return;
            }
            if (tier == UnitTier.Companion && !IsFactionSkillAction(unit, action))
            {
                GICLog.Warn($"[TurnFlow] 伙伴 {action.unitId} 仅接受势力技能号令（评分制脑自主决策），忽略 {action.actionType}");
                return;
            }

            // 每玩家每回合 1 个行动：首份定稿（重复上交已在入口忽略——完成选择定死拍板）
            _pendingActions[action.playerId] = action;

            // 收齐全部玩家行动 → 进入执行阶段
            TryBeginResolve();
        }

        /// <summary>收齐全部玩家行动 → 伙伴决策阶段 → 执行阶段（提交/超时自动 Pass 共用入口）</summary>
        private void TryBeginResolve()
        {
            if (Phase != BattlePhase.Selecting) return;
            if (_pendingActions.Count < _sim.PlayerIds.Count) return;

            // 阶段三：伙伴决策（3-4★ 评分制脑，docs/active/32 §3——收齐全部玩家选择后、开演前的
            // 机器瞬时阶段）：感知输入=各玩家已提交选择（号令跳过+体力预留）；己方眷属意图 v1 不可见
            // （信息口径统一：执行前任何意图只在自己决策链路内可见），故感知源只传玩家行动不含眷属行动
            var playerActions = new List<ActionData>(_pendingActions.Values);
            var companionActions = CompanionBrain.DecideAll(_sim, TurnNumber, playerActions);

            // 统一执行阶段：玩家配额行动 + 眷属自主 + 伙伴自主合并（全部按行动者攻速排序结算）
            var actions = new List<ActionData>(playerActions);
            actions.AddRange(_familiarUnitActions);
            actions.AddRange(companionActions);

            if (_resolveCoroutine != null) StopCoroutine(_resolveCoroutine);
            _resolveCoroutine = StartCoroutine(ResolveTurnRoutine(actions));
        }

        /// <summary>行动是否为势力技能（号令通道，docs/active/32 §4）：Skill 类型且技能槽为
        /// Enso/Contract——眷属技能表本无势力技能条目=天然无眷属号令，无需额外防线</summary>
        private static bool IsFactionSkillAction(Unit unit, ActionData action)
        {
            if (action.actionType != ActionType.Skill) return false;
            var skill = action.skillIndex >= 0 && action.skillIndex < unit.Skills.Count
                ? unit.Skills[action.skillIndex]
                : null;
            var skillType = skill?.RawData?.skillType ?? SkillType.Talent;
            return skillType == SkillType.Enso || skillType == SkillType.Contract;
        }

        private IEnumerator ResolveTurnRoutine(List<ActionData> actions)
        {
            Phase = BattlePhase.Resolving;
            SelectRemainingSeconds = -1f; // 计时终止（HUD 不再显示倒计时）
            OnPhaseChanged?.Invoke(Phase, TurnNumber);

            yield return _resolver.ResolveTurnCoroutine(TurnNumber, actions);

            _resolveCoroutine = null;

            // 胜负判定（协议核心批 2026-09-29 拍板：核心摧毁=唯一败北判据，全灭软停退役；结算画面=B8）：
            // TurnResolver 片末检查已在核心死亡片中止演算（剩余片不结算），此处统一广播胜负。
            // 双方核心同回合互灭不下发（平局语义 B8 定义）。
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

        /// <summary>胜负判定（协议核心批 2026-09-29 拍板，docs/02 §2）：核心摧毁=唯一败北判据——
        /// 队伍败北=该队全部玩家的核心被摧毁（多人预留：核心每玩家一枚非每队一枚）；
        /// 全灭不再结束对局（核心在=手牌可再出战，MOBA 基地语义翻盘）。
        /// 恰好一队核心存活=胜方；双方核心同回合互灭返回 null（平局语义 B8 结算批定义）。
        /// 防御：全对局无已登记核心（装配异常）回落旧 S10 全灭口径</summary>
        private TeamType? CheckBattleOver()
        {
            if (!_sim.HasAnyRegisteredCore())
            {
                // 旧 S10 全灭软停（防御回落——正常对局装配必含双方核心，勿再当主判据维护）
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

            TeamType? coreAliveTeam = null;
            foreach (var pid in _sim.PlayerIds)
            {
                var team = _sim.GetTeamOf(pid);
                if (coreAliveTeam == team) continue; // 同队已查过（多人口径：任一玩家核心存活=队未败）
                if (!_sim.TeamHasLivingCore(team)) continue;
                if (coreAliveTeam.HasValue) return null; // 两队核心都存活：战斗继续
                coreAliveTeam = team;
            }
            return coreAliveTeam; // 恰好一队核心存活=胜方；双方核心同回合互毁=null
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
            // 同键复推防御：先清旧 ack（正常流片号单调递增不撞键；超时快进后亦不重推同片）——
            // 2026-09-27 复审清理：残字段 _pendingAckKey 无外部读者，就地局部化
            _ackedKeys.Remove(AckKey(turnNumber, sliceIndex));
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
