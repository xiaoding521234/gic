using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Profiling;
using GIC.Framework;
using GIC.Data;
using GIC.Tool;
namespace GIC.Battle
{


    /// <summary>
    /// 即时制 tick 状态机（决策九十五，docs/active/48 §2——取代 TurnFlowController 五阶段回合机）：
    /// - **20Hz 固定步长 accumulator 累加器**（Gaffer《Fix Your Timestep!》业界标准）+每帧步数 clamp
    ///   （spiral of death 防护）+双缓冲插值位（UnitView 按段命令插值，护栏三）——确定性护栏四件套；
    /// - **每 0.5 秒 AI 节拍**（AiBeatTicks=10 tick）：眷属脑+伙伴脑静态批演算；配额脑（场景件
    ///   PlayerQuotaBrain）沿用 OnPhaseChanged(Selecting, second) 事件驱动零改动兼容；
    /// - **每秒整拍**（20 tick）：BattleResolver.ResolveSecondBeat（回合末族秒化：发放/Buff 计时/
    ///   泡湿/灵魂衰减/资源点站立采集/建筑效果）+ 快照广播 + desync hash 落盘（护栏四）+ 战斗时钟 +1 分钟；
    /// - **单机即时结算，联机按 Host 封存的逐拍输入批结算**；
    /// - **胜负判定**：核心摧毁唯一判据不变（CheckBattleOver 队伍口径同构）——每秒拍后+每次行动结算后检查。
    ///
    /// Phase 兼容映射（P1 过渡，P4 批退役 Selecting 语义）：Running=Selecting——HUD 上交链路/
    /// 配额脑触发/门面订阅零改动；HUD 每 0.5 秒收到同值事件=幂等无害（Tip 重设同文本）。
    /// 回合数显示退役：CurrentSecond 属性=逻辑秒（配额脑验证口径），战斗时钟 BattleTimeMinutes 为主显示。
    /// </summary>
    public class RTSFlowController : MonoBehaviour, IBattleResolverHost
    {
        // ==================== 对外状态（View/门面兼容层） ====================

        /// <summary>阶段（P1 兼容映射：Running=Selecting、终局=Finished；Idle 仅开局前）</summary>
        public BattlePhase Phase { get; private set; } = BattlePhase.Idle;

        /// <summary>当前 tick（即时制真源——1 秒=20 tick，BattleMetrics.TicksPerSecond）</summary>
        public int CurrentTick { get; private set; }

        /// <summary>逻辑秒（tick/20）——配额脑/回放/周期类相对计数的兼容口径（旧「回合数」秒化）</summary>
        public int CurrentSecond => CurrentTick / BattleMetrics.TicksPerSecond;

        // ==================== 战斗独立时间系统（秒化：每秒 +1 分钟，回合数显示退役） ====================

        public const int BattleClockStartMinutes = 6 * 60;

        /// <summary>战斗内时间（当天分钟数，0-1439）</summary>
        public int BattleTimeMinutes { get; private set; } = BattleClockStartMinutes;

        /// <summary>战斗时段（独立时钟推导；未开战回落全局时段）</summary>
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

        /// <summary>选择倒计时（View 兼容属性——即时制无选择阶段，恒 -1=非倒计时态）</summary>
        public float SelectRemainingSeconds => -1f;

        /// <summary>倒计时暂停门（View 兼容空实现——即时制无倒计时；N0 浏览暂停改由 tick 暂停位承载）</summary>
        public bool SelectTimerPaused { get; set; }

        /// <summary>阶段变化通知（P1：AI 节拍每 0.5 秒发 Selecting 驱动配额脑+HUD 幂等刷新；
        /// 终局发 Finished）</summary>
        public event Action<BattlePhase, int> OnPhaseChanged;

        private BattleSimState _sim;
        private IBattleTransport _transport;
        private BattleResolver _resolver;
        private Func<string, int, bool> _ownsPurchaseUnit;
        private IReadOnlyDictionary<int, List<ActionData>> _replayDecisions;
        private Action<int, int> _replayHash;
        private bool IsReplay => _replayDecisions != null;
        private static readonly ProfilerMarker TickMarker = new ProfilerMarker("GIC.Battle.Tick");

        // ==================== 帧同步输入管线（F1，docs/active/50 §4） ====================

        /// <summary>Host 侧=true（唯一权威输入定序者）；客户端=false（只消费 InputBatch，不跑脑/不定序）</summary>
        public bool IsHost { get; private set; } = true;

        /// <summary>联机模式=true（输入按 targetTick 对齐）；单机=false（即时结算，零手感损失）</summary>
        public bool IsNetworked { get; private set; }

        private BattleInputTimeline _inputTimeline;

        /// <summary>玩家内输入序号（Host 定序 tie-break；客户端提交时递增）</summary>
        private readonly Dictionary<string, int> _inputSeq = new Dictionary<string, int>();

        /// <summary>Host 侧每秒 hash（按 tick 存，供客户端 HashReport 比对）</summary>
        private readonly Dictionary<int, int> _hostHashByTick = new Dictionary<int, int>();

        /// <summary>最近一次 desync tick（-1=无）</summary>
        public int LastDesyncTick { get; private set; } = -1;

        private HashReportMessage _pendingHashReport;
        private BattleOverMessage _localBattleOver;
        private BattleOverMessage _pendingHostBattleOver;

        /// <summary>最近 AI 节拍窗内的玩家上交行动（伙伴脑感知输入——旧「感知己方玩家已提交选择」
        /// 秒化：每 0.5 秒窗口收集，节拍后清空）</summary>
        private readonly List<ActionData> _recentPlayerActions = new List<ActionData>();

        // ==================== 累加器（确定性护栏一/二，Gaffer 标准模式） ====================

        private float _accumulator;

        private void Update()
        {
            if (Phase != BattlePhase.Selecting) return; // 未开战/已终局不推进
            if (SelectTimerPaused) return; // N0 单人局浏览暂停（决策六十七）：暂停位不推 tick（HUD BrowseUiOpen 每帧重估写入、
                                           // BattleSession 门控 value && IsSoloHumanGame——多人局不冻全场；P6 批修复：P1 重写时
                                           // 本消费点断链，旧回合机 SelectTimerPaused 语义曾由倒计时链承载）
            float frameDelta = Time.deltaTime; // DETERMINISM-ALLOW: tick 驱动层 accumulator（不入模拟结算）
            if (frameDelta > BattleMetrics.MaxFrameDeltaSeconds)
                frameDelta = BattleMetrics.MaxFrameDeltaSeconds; // 巨帧钳制（切后台回归）
            _accumulator += frameDelta;

            // 每帧步数 clamp：模拟跟不上时整体慢放（spiral of death 防护——欠债不滚雪球）
            int steps = 0;
            while (_accumulator >= BattleMetrics.TickSeconds && steps < BattleMetrics.MaxTicksPerFrame)
            {
                if (!TickAdvance())
                {
                    // 等权威批时保留有限的追帧余量，不丢 tick，也不积攒无上限的时间债。
                    _accumulator = Mathf.Min(_accumulator,
                        BattleMetrics.MaxTicksPerFrame * BattleMetrics.TickSeconds);
                    break;
                }
                _accumulator -= BattleMetrics.TickSeconds;
                steps++;
                if (Phase != BattlePhase.Selecting) break; // 拍内终局（核心破）即停
            }
        }

        /// <summary>联机必须消费下一拍的权威输入批，空批表示该拍无输入。</summary>
        private bool TickAdvance()
        {
            if (Phase != BattlePhase.Selecting || SelectTimerPaused) return false;
            InputBatchMessage batch = null;
            if (IsNetworked && !_inputTimeline.TryTakeNext(out batch)) return false;
            using var tickSample = TickMarker.Auto();

            if (_pendingHashReport != null)
            {
                // 收到下一拍才确认 Host 已完成上一拍，不提前上报上一拍 hash。
                _transport.ClientSend(BattleMessageType.HashReport, _pendingHashReport);
                _pendingHashReport = null;
            }

            CurrentTick = batch != null ? batch.tick : CurrentTick + 1;
            _sim.CurrentTick = CurrentTick;
            AdvanceSimulationTick(batch);
            TryApplyHostBattleOver();
            if (IsHost && !IsReplay) BattleRecorder.CaptureCompletedTick(CurrentTick);
            return true;
        }

        private void AdvanceSimulationTick(InputBatchMessage batch)
        {
            if (_localBattleOver != null) return; // 本地已判出胜负，只继续消费权威拍，等待 Host 终局。
            if (batch != null)
            {
                if (IsHost && !IsReplay) BattleRecorder.CaptureInputBatch(batch);
                if (ApplyInputBatch(batch.inputs)) return;
            }

            // —— tick 移动推进（P2 连续空间，蓝图 §3.3：A* 路径+圆碰撞每 tick 步进；收尾段=采集结算）——
            var arrivals = RTSMovementSystem.AdvanceTick(_sim);
            if (arrivals != null && arrivals.Count > 0)
            {
                _resolver.ResolveMoveArrivals(arrivals);
                if (CheckBattleEnded()) return;
            }

            // 两端都推进分离；各自的模拟为本地表现生成位置校正。
            _resolver.ResolveSeparationTick();

            // —— 普通读条与移动快速采集（主动技能在输入窗取消，硬控/离范围在此逐 tick 复核）——
            _resolver.ResolveHarvestTick();
            if (CheckBattleEnded()) return;

            bool aiBeat = CurrentTick % BattleMetrics.AiBeatTicks == 0;
            if (IsHost && aiBeat) _sim.RevealFog();
            if (IsReplay)
            {
                if (_replayDecisions.TryGetValue(CurrentTick, out var recordedAi))
                {
                    _resolver.ResolveAiActions(recordedAi);
                    if (CheckBattleEnded()) return;
                }
            }
            else
            {
                if (aiBeat) RunAiBeat();
                RunFamiliarBrainWave();
            }

            // —— 每秒整拍（发放/Buff 计时/泡湿/灵魂/采集/建筑效果 + 快照 + 时钟 + hash）——
            if (CurrentTick % BattleMetrics.TicksPerSecond == 0)
                RunSecondBeat();

            SealAndBroadcastNextTick();
        }

        // ==================== AI 节拍（每 0.5 秒，蓝图 §6：三脑同频） ====================

        private void RunAiBeat()
        {
            if (Phase != BattlePhase.Selecting) return;
            if (!IsHost) return; // F1：AI 三脑只在 Host 跑；客户端只消费 InputBatch 里的 AI 行动
            int second = CurrentSecond;

            // 眷属脑改按 tick 相位摊平（2026-11-06 后期卡顿修复）：见 RunFamiliarBrainSlice——
            // 百余眷属同拍全量决策=单拍 0.7~0.2 秒卡顿，现每 tick 只决策 1/AiBeatTicks 的候选

            // 伙伴脑（3~4★ 评分制——感知窗口内玩家上交行动）
            var actions = new List<ActionData>();
            var playerWindow = new List<ActionData>(_recentPlayerActions);
            actions.AddRange(CompanionBrain.DecideAll(_sim, second, playerWindow));
            _recentPlayerActions.Clear();
            foreach (var action in actions)
            {
                action.turnNumber = second;
                action.submitTick = CurrentTick;
            }

            if (IsNetworked)
            {
                // F1：AI 行动也作为输入进批（targetTick=下一 tick），客户端同批结算；
                // 不在此刻结算——否则 Host 提前一 tick，与客户端不同步
                foreach (var action in actions)
                    EnqueueInput(action, CurrentTick + 1, isAi: true, seq: NextSeq(action.playerId));
            }
            else
            {
                BattleRecorder.CaptureActions(second, actions);
                _resolver.ResolveAiActions(actions);
            }

            // 配额脑驱动（PlayerQuotaBrain 场景件零改动兼容：沿用 Selecting 事件触发，
            // 脑内协程验证 Phase==Selecting && CurrentSecond==second 后 Decide→SubmitAction）；
            // HUD 门面同收本事件=同值幂等刷新（P4 批拆分专用事件）
            OnPhaseChanged?.Invoke(BattlePhase.Selecting, second);

            CheckBattleEnded();
        }

        /// <summary>眷属决策分摊片数（节拍后 N 个 tick 摊完一轮；4=单拍峰值降为 1/4）</summary>
        private const int FamiliarDecisionSlices = 4;

        /// <summary>
        /// 眷属脑摊平波（2026-11-06 后期卡顿修复）：节拍 tick 起的 FamiliarDecisionSlices 个 tick 内，
        /// 每个 tick 只决策「候选序号 % 片数 == 本 tick 偏移」的眷属——每单位仍每 0.5 秒决策一次，
        /// 只是把同拍峰值切成 4 小片。上交/录制/广播链与节拍批完全同构。
        /// </summary>
        private void RunFamiliarBrainWave()
        {
            if (Phase != BattlePhase.Selecting) return;
            if (!IsHost) return; // 与节拍批同口径：AI 只在 Host 决策
            int beat = BattleMetrics.AiBeatTicks;
            if (beat <= 1) return;
            // 错开节拍 tick 本身（2026-11-06）：伙伴脑/揭雾/配额脑都在节拍 tick 上跑（实测 45~65ms），
            // 眷属切片若从节拍 tick 起会与之叠加成单帧峰值——故从节拍后第 1 个 tick 起算
            int slot = CurrentTick % beat - 1;
            if (slot < 0 || slot >= FamiliarDecisionSlices) return; // 只在节拍后窗口内分摊
            int second = CurrentSecond;
            int offset = slot; // 切片相位 0..FamiliarDecisionSlices-1

            var actions = FamiliarBrain.DecideAll(_sim, second, offset, FamiliarDecisionSlices);
            if (actions.Count == 0) return;

            foreach (var action in actions)
            {
                action.turnNumber = second;
                action.submitTick = CurrentTick;
            }
            if (IsNetworked)
            {
                foreach (var action in actions)
                    EnqueueInput(action, CurrentTick + 1, isAi: true, seq: NextSeq(action.playerId));
            }
            else
            {
                BattleRecorder.CaptureActions(second, actions);
                _resolver.ResolveAiActions(actions);
            }
            CheckBattleEnded();
        }

        // ==================== 每秒整拍（回合末族秒化，蓝图 §2.3/§9） ====================

        private void RunSecondBeat()
        {
            if (Phase != BattlePhase.Selecting) return;
            int second = CurrentSecond;

            // 效果拍（2026-11-04 拍板「时间翻倍」）：Buff tick 效果+建筑效果每 2 秒一拍
            //（EffectBeatTicks=40）——秒拍族（发放/计时递减/泡湿/采集/快照/时钟）仍每秒
            bool effectBeat = CurrentTick % BattleMetrics.EffectBeatTicks == 0;
            _resolver.ResolveSecondBeat(second, effectBeat);

            if (CheckBattleEnded()) return; // 秒拍内终局不再广播快照

            // 快照广播（对账/自愈——旧「每选择阶段头」秒化为每秒一次）
            _transport.HostSend(BattleMessageType.Snapshot, new SnapshotMessage
            {
                snapshot = _sim.TakeSnapshot(second),
                selectLimitSeconds = 0, // 即时制无选择倒计时（View 兼容字段）
            });

            // 战斗时钟：每秒 +1 分钟（昼夜跨界音乐选池链按既有 BattleTimePeriod 消费自动跟随）
            BattleTimeMinutes = (BattleTimeMinutes + BattleMetrics.BattleClockMinutesPerSecond) % (24 * 60);

            // Host 记账，客户端上报；回放在同一拍读取 hash。
            int stateHash = ComputeStateHash(_sim);
            if (IsReplay)
            {
                _replayHash?.Invoke(CurrentTick, stateHash);
                return;
            }
            if (IsHost) BattleRecorder.CaptureTickHash(CurrentTick, stateHash);
            if (IsNetworked)
            {
                if (IsHost)
                {
                    _hostHashByTick[CurrentTick] = stateHash;
                    const int historyTicks = 4 * 60 * BattleMetrics.TicksPerSecond;
                    if (CurrentTick > historyTicks) _hostHashByTick.Remove(CurrentTick - historyTicks);
                }
                else
                {
                    _pendingHashReport = new HashReportMessage
                    {
                        tick = CurrentTick,
                        hash = stateHash,
                        unitCount = _sim.Units.Count,
                    };
                }
            }
        }

        // ==================== 行动上交（配额制退役：即交即结算） ====================

        /// <summary>单机同步结算，房主入队，客户端只发请求。客户端返回 true 不代表房主已接纳。</summary>
        public bool OnSubmitAction(ActionData action)
        {
            if (!ValidateSubmittedAction(action)) return false;
            action.turnNumber = CurrentSecond;
            action.submitTick = CurrentTick;

            if (!IsHost)
            {
                _transport.ClientSend(BattleMessageType.InputCommand, new InputCommandMessage
                {
                    playerId = action.playerId,
                    seq = NextSeq(action.playerId),
                    submitTick = CurrentTick,
                    action = action,
                });
                return true;
            }

            if (IsNetworked)
                return AdmitPlayerInput(action, (long)CurrentTick + BattleMetrics.InputDelayTicks,
                    NextSeq(action.playerId));

            if (!CanAdmitPurchase(action)) return false;
            BattleRecorder.CaptureSubmittedAction(action); // 单机是拍末提交窗，不是联机的拍首输入。
            RememberPlayerAction(action);
            _resolver.ResolveSubmittedAction(action);
            CheckBattleEnded();
            return true;
        }

        private bool ValidateSubmittedAction(ActionData action)
        {
            if (Phase != BattlePhase.Selecting)
            {
                GICLog.Warn($"[RTSFlow] 战斗未运行/已终局，忽略 {action}");
                return false;
            }
            if (action == null || !_sim.PlayerIds.Contains(action.playerId))
            {
                GICLog.Warn($"[RTSFlow] 未知玩家 {action?.playerId}，忽略");
                return false;
            }

            // 玩家级行动无行动单位；其执行器根据结算时的状态判定是否成功。
            if (action.actionType == ActionType.DeployUnit || action.actionType == ActionType.UseItem
                || action.actionType == ActionType.EquipItem || action.actionType == ActionType.BuyCard)
                return true;

            if (action.actionType != ActionType.Move && action.actionType != ActionType.Skill
                && action.actionType != ActionType.Pass)
            {
                GICLog.Warn($"[RTSFlow] 不支持行动类型 {action.actionType}，忽略（B1 白名单）");
                return false;
            }

            var unit = _sim.GetUnit(action.unitId);
            if (unit == null)
            {
                GICLog.Warn($"[RTSFlow] 未知单位 {action.unitId}，忽略");
                return false;
            }

            // 灵魂无任何行动（决策九十一）：Host 权威防线
            if (unit.IsSoul)
            {
                GICLog.Warn($"[RTSFlow] 灵魂 {action.unitId} 无任何行动（地脉驻留体），忽略上交");
                return false;
            }

            var identity = unit.GetUnitComponent<UnitIdentity>();
            if (identity == null || identity.OwnerPlayerID != action.playerId)
            {
                GICLog.Warn($"[RTSFlow] 单位 {action.unitId} 不属于玩家 {action.playerId}，忽略");
                return false;
            }

            // 建筑防线（协议核心批）：建筑（含协议核心）不参与任何行动
            if (unit.RawData != null && unit.RawData.unitType == UnitType.Building)
            {
                GICLog.Warn($"[RTSFlow] 建筑 {action.unitId}（含协议核心）不接受任何玩家上交行动，忽略");
                return false;
            }

            // 单位级行动域校验（操控分层，docs/32 §1/§4；伙伴技能解锁 2026-10-12）：
            // 眷属=AI 域；伙伴=玩家可直接操控（标记 isPlayerDirectControl——体力减免 5+号令津贴）
            var tier = BattleHeuristics.TierOf(unit);
            if (tier == UnitTier.Familiar)
            {
                GICLog.Warn($"[RTSFlow] 眷属 {action.unitId} 不接受玩家上交行动（启发式脑自主决策），忽略");
                return false;
            }
            action.isPlayerDirectControl = tier == UnitTier.Companion;
            return true;
        }

        private bool CanAdmitPurchase(ActionData action)
            => action.actionType != ActionType.BuyCard || BuyCardExecutor.CanAccept(_sim, action, _ownsPurchaseUnit);

        private void RememberPlayerAction(ActionData action)
        {
            if (action.actionType != ActionType.BuyCard)
                _recentPlayerActions.Add(action); // 伙伴脑下一节拍的感知窗口。
        }

        private bool AdmitPlayerInput(ActionData action, long targetTick, int seq)
        {
            if (!CanAdmitPurchase(action) || !EnqueueInput(action, targetTick, isAi: false, seq: seq)) return false;
            RememberPlayerAction(action);
            return true;
        }

        // ==================== 胜负判定（核心摧毁唯一判据，口径同旧 CheckBattleOver） ====================

        /// <summary>模拟达到终局即停止结算；联机客户端只以 Host 消息结束对局。</summary>
        private bool CheckBattleEnded()
        {
            if (Phase != BattlePhase.Selecting || _localBattleOver != null) return true;
            var overWinner = CheckBattleOver(_sim);
            if (!overWinner.HasValue) return false;

            var result = new BattleOverMessage
            {
                winnerTeam = (int)overWinner.Value,
                finalTick = CurrentTick,
            };
            if (IsNetworked && !IsHost)
                _localBattleOver = result;
            else
                FinishBattle(result);
            return true;
        }

        private void FinishBattle(BattleOverMessage result)
        {
            Stop();
            _transport.HostSend(BattleMessageType.BattleOver, result);
            if (IsHost && !IsReplay) BattleRecorder.CaptureGameOver(result.winnerTeam, result.finalTick);
        }

        /// <summary>胜负判定（协议核心批拍板，docs/02 §2——与旧 TurnFlowController 逐条同构；回放器
        /// v2 同源消费，public static 单源勿复制分叉）：核心摧毁=唯一败北判据（队败=该队全部玩家核心
        /// 被摧毁）；恰好一队核心存活=胜方；双方核心同拍互灭=null（平局语义 B8 定义保留）。
        /// 无已登记核心（装配异常）回落旧全灭口径</summary>
        public static TeamType? CheckBattleOver(BattleSimState sim)
        {
            if (!sim.HasAnyRegisteredCore())
            {
                TeamType? aliveTeam = null;
                foreach (var pid in sim.PlayerIds)
                {
                    var team = sim.GetTeamOf(pid);
                    if (aliveTeam == team) continue;
                    if (!sim.HasLivingUnits(team)) continue;
                    if (aliveTeam.HasValue) return null;
                    aliveTeam = team;
                }
                return aliveTeam;
            }

            TeamType? coreAliveTeam = null;
            foreach (var pid in sim.PlayerIds)
            {
                var team = sim.GetTeamOf(pid);
                if (coreAliveTeam == team) continue;
                if (!sim.TeamHasLivingCore(team)) continue;
                if (coreAliveTeam.HasValue) return null;
                coreAliveTeam = team;
            }
            return coreAliveTeam;
        }

        // ==================== 战斗启停 ====================

        public void Bind(BattleSimState sim, IBattleTransport transport,
            bool isHost = true, bool isNetworked = false, Func<string, int, bool> ownsPurchaseUnit = null)
        {
            _sim = sim;
            _transport = transport;
            IsHost = isHost;
            IsNetworked = isNetworked;
            _ownsPurchaseUnit = ownsPurchaseUnit;
            _resolver = new BattleResolver(sim, transport, this) { RecordSegments = isHost };
            _replayDecisions = null;
            _replayHash = null;
        }

        /// <summary>回放使用正式逐拍推进，但仅执行已录制的决策，不重新运行大脑或向网络发包。</summary>
        public void BindReplay(BattleSimState sim, IBattleTransport transport, bool isNetworked,
            IReadOnlyDictionary<int, List<ActionData>> decisions, Action<int, int> onHash)
        {
            Bind(sim, transport, isNetworked: isNetworked);
            _replayDecisions = decisions ?? throw new ArgumentNullException(nameof(decisions));
            _replayHash = onHash;
            _resolver.RecordSegments = false;
        }

        public bool StepReplay(InputBatchMessage batch)
        {
            if (!IsReplay || Phase != BattlePhase.Selecting || IsNetworked != (batch != null)) return false;
            if (batch != null && (batch.tick != CurrentTick + 1 || !_inputTimeline.TryReceive(batch))) return false;
            return TickAdvance();
        }

        /// <summary>单机录制的上交已通过接纳，按录制时机执行（含第 0 拍）。</summary>
        public bool ApplyReplaySubmit(ActionData action)
        {
            if (!IsReplay || IsNetworked || Phase != BattlePhase.Selecting || action?.submitTick != CurrentTick)
                return false;
            _resolver.ResolveSubmittedAction(action);
            CheckBattleEnded();
            return true;
        }

        // ==================== 帧同步输入管线（F1） ====================

        private int NextSeq(string playerId)
        {
            _inputSeq.TryGetValue(playerId, out int seq);
            _inputSeq[playerId] = seq + 1;
            return seq;
        }

        /// <summary>向 Host 尚未封存的输入批追加行动。</summary>
        private bool EnqueueInput(ActionData action, long targetTick, bool isAi, int seq)
        {
            if (action == null) return false;
            var input = new OrderedInput
            {
                playerId = action.playerId,
                seq = seq,
                isAi = isAi ? 1 : 0,
                action = action,
            };
            if (_inputTimeline.TrySchedule(input, targetTick, BattleMetrics.MaxInputLeadTicks)) return true;
            GICLog.Warn($"[RTSFlow] 输入超出未来窗口，忽略 player={action.playerId} target={targetTick} now={CurrentTick}");
            return false;
        }

        /// <summary>Host 在开局及每拍末尾封存下一拍；只定序一次，只发送一次。</summary>
        private void SealAndBroadcastNextTick()
        {
            if (!IsHost || !IsNetworked || IsReplay || Phase != BattlePhase.Selecting) return;
            _transport.HostSend(BattleMessageType.InputBatch, _inputTimeline.SealNext());
        }

        /// <summary>Host 和客户端按封存顺序应用输入；结算归属使用本拍的逻辑秒。</summary>
        private bool ApplyInputBatch(List<OrderedInput> inputs)
        {
            if (inputs.Count == 0) return false;

            var aiBatch = new List<ActionData>();
            foreach (var input in inputs)
            {
                if (input?.action == null) continue;
                input.action.turnNumber = CurrentSecond;
                if (input.isAi == 1)
                {
                    aiBatch.Add(input.action);
                    continue;
                }
                if (aiBatch.Count > 0)
                {
                    _resolver.ResolveAiActions(aiBatch);
                    aiBatch.Clear();
                }
                _resolver.ResolveSubmittedAction(input.action);
            }
            if (aiBatch.Count > 0) _resolver.ResolveAiActions(aiBatch);
            return CheckBattleEnded();
        }

        /// <summary>Host：发送者取自传输层，校验后才允许输入进入权威批。</summary>
        public bool ReceiveInputCommand(InputCommandMessage msg, string senderId)
        {
            if (!IsHost || !IsNetworked || msg?.action == null || string.IsNullOrEmpty(senderId)) return false;
            if (msg.playerId != senderId || msg.action.playerId != senderId)
            {
                GICLog.Warn($"[RTSFlow] 输入玩家与发送连接不一致，忽略 sender={senderId}");
                return false;
            }
            return ValidateSubmittedAction(msg.action) && AdmitPlayerInput(msg.action,
                (long)msg.submitTick + BattleMetrics.InputDelayTicks, msg.seq);
        }

        /// <summary>客户端：缓存 Host 的输入批；缺少下一拍时不推进。</summary>
        public void ReceiveInputBatch(InputBatchMessage msg)
        {
            if (IsHost || !IsNetworked || Phase != BattlePhase.Selecting || msg?.inputs == null) return;
            if (!_inputTimeline.TryReceive(msg))
                GICLog.Warn($"[RTSFlow] 忽略已消费或重复的输入批 tick={msg.tick} now={CurrentTick}");
        }

        /// <summary>等待终局拍消费完毕，再发布 Host 的终局结果。</summary>
        public void ReceiveHostBattleOver(BattleOverMessage msg)
        {
            if (IsHost || !IsNetworked || Phase != BattlePhase.Selecting || msg == null
                || _pendingHostBattleOver != null) return;
            _pendingHostBattleOver = msg;
            TryApplyHostBattleOver();
        }

        private void TryApplyHostBattleOver()
        {
            if (Phase != BattlePhase.Selecting || _pendingHostBattleOver == null
                || CurrentTick < _pendingHostBattleOver.finalTick) return;
            var msg = _pendingHostBattleOver;
            _pendingHostBattleOver = null;

            if (_localBattleOver == null || _localBattleOver.finalTick != msg.finalTick
                || _localBattleOver.winnerTeam != msg.winnerTeam)
                GICLog.Warn($"[RTSFlow] 本地终局与 Host 不一致，按 Host 结果收尾 tick={msg.finalTick}");
            FinishBattle(msg);
        }

        /// <summary>Host：比对客户端 hash 上报（F1 只告警+落日志，不中断）。</summary>
        public void ReceiveHashReport(HashReportMessage msg)
        {
            if (msg == null) return;
            if (!_hostHashByTick.TryGetValue(msg.tick, out int hostHash))
            {
                GICLog.Warn($"[RTSFlow] HashReport tick={msg.tick} 无本地记录（已过期）");
                return;
            }
            if (hostHash == msg.hash) return;
            LastDesyncTick = msg.tick;
            GICLog.Error($"[Desync] tick={msg.tick} host={hostHash} client={msg.hash} units={msg.unitCount}");
            if (IsNetworked)
                _transport.HostSend(BattleMessageType.DesyncAlert, new DesyncAlertMessage
                {
                    tick = msg.tick,
                    hostHash = hostHash,
                    clientHash = msg.hash,
                });
        }

        /// <summary>开战：进入 tick 循环（Phase=Selecting=Running 兼容映射；初始快照广播）</summary>
        public void StartBattle()
        {
            CurrentTick = 0;
            _sim.CurrentTick = 0;
            _accumulator = 0f;
            _inputTimeline = IsNetworked ? new BattleInputTimeline() : null;
            _inputSeq.Clear();
            _recentPlayerActions.Clear();
            _hostHashByTick.Clear();
            _pendingHashReport = null;
            _localBattleOver = null;
            _pendingHostBattleOver = null;
            LastDesyncTick = -1;
            BattleTimeMinutes = BattleClockStartMinutes; // 独立时钟复位（06:00 起）
            Phase = BattlePhase.Selecting;
            _transport.HostSend(BattleMessageType.Snapshot, new SnapshotMessage
            {
                snapshot = _sim.TakeSnapshot(0),
                selectLimitSeconds = 0,
            });
            SealAndBroadcastNextTick();
            OnPhaseChanged?.Invoke(Phase, 0);
        }

        /// <summary>停止状态机（战斗结束/退出场景）</summary>
        public void Stop()
        {
            Phase = BattlePhase.Finished;
            OnPhaseChanged?.Invoke(Phase, CurrentSecond);
        }

        // ==================== IBattleResolverHost（ack 门控退役——no-op 兼容层） ====================

        /// <summary>ack 门控已随回合机退役（决策九十五）——保留接口空实现（回放宿主兼容）</summary>
        public void NotifySegmentPushed(int turnNumber, int sliceIndex) { }

        /// <summary>ack 门控已退役——恒 true（无等待语义）</summary>
        // P6 残留审计 D2：HasSegmentAck 随 ack 门控退役删除（IBattleResolverHost 已收窄）。

        // ==================== desync hash（确定性护栏四，蓝图 §2.2） ====================

        /// <summary>战场状态 hash（确定序遍历：单位 unitId 升序→HP/元能/位置；玩家资源按注册序）——
        /// 同 seed 同命令流必同 hash（漂移=演算层引入了非确定性）；单机=回放自证断言，
        /// 后置 LAN=双端比对定位漂移拍（王者荣耀不同步率 2%→0.03% 同款手段）。
        /// 回放器 v2 同源消费——public static 单源勿复制分叉</summary>
        public static int ComputeStateHash(BattleSimState sim)
        {
            int hash = 17;
            // 主世界 + 地脉维度（灵魂独立注册表——按 unitId 合并排序=确定序）
            var unitIds = new List<string>(sim.Units.Keys);
            foreach (var kv in sim.CrossDimensionUnits) unitIds.Add(kv.Key);
            unitIds.Sort(StringComparer.Ordinal);
            foreach (var unitId in unitIds)
            {
                var unit = sim.GetUnit(unitId); // 双注册表透明解析
                if (unit == null) continue;
                var stats = unit.GetUnitComponent<UnitStats>();
                if (stats == null) continue;
                var posCm = sim.GetPosCm(unit);

                hash = hash * 31 + stats.HP;
                hash = hash * 31 + stats.Energy;
                hash = hash * 31 + stats.Sanity;
                hash = hash * 31 + posCm.x;
                hash = hash * 31 + posCm.y;
                hash = hash * 31 + (int)unit.Dimension;
                hash = hash * 31 + unit.DeployedSecond;
                hash = hash * 31 + unit.LastPeriodicSummonSecond;
                hash = hash * 31 + unit.ConstellationLevel;
                foreach (int choice in unit.ConstellationChoices) hash = hash * 31 + choice;

                // Buff：身份（族×资产名）+ 层数 + 剩余秒——先排序再折叠，列表序差异不致漂
                var buffKeys = new List<string>();
                if (unit.Buffs != null)
                {
                    foreach (var buff in unit.Buffs)
                    {
                        if (buff == null) continue;
                        buffKeys.Add($"{(int)buff.Type}|{(buff.Config != null ? buff.Config.name : string.Empty)}" +
                                     $"|{buff.Level}|{buff.RemainingSeconds}");
                    }
                }
                buffKeys.Sort(StringComparer.Ordinal);
                foreach (var key in buffKeys)
                    foreach (char c in key) hash = hash * 31 + c;

                // 冷却（战技/爆发）
                hash = hash * 31 + sim.SkillCdRemainTicks(unitId, SkillType.Normal);
                hash = hash * 31 + sim.SkillCdRemainTicks(unitId, SkillType.Burst);

                // 装备（武器+耐久；配件按槽序+各件耐久）
                var equipment = unit.GetUnitComponent<UnitEquipment>();
                if (equipment != null)
                {
                    hash = hash * 31 + (equipment.Weapon.HasValue ? (int)equipment.Weapon.Value : 0);
                    hash = hash * 31 + equipment.WeaponDurability;
                    hash = hash * 31 + equipment.AccessorySlotCount;
                    var accessories = equipment.Accessories;
                    if (accessories != null)
                    {
                        for (int i = 0; i < accessories.Count; i++)
                        {
                            var acc = accessories[i];
                            hash = hash * 31 + (acc.HasValue ? (int)acc.Value : 0);
                            hash = hash * 31 + (acc.HasValue ? equipment.HeldAccessoryDurability(acc.Value) : 0);
                        }
                    }
                }
            }

            foreach (var pid in sim.PlayerIds) // PlayerIds=注册序（List，稳定）
            {
                hash = hash * 31 + sim.GetMora(pid);
                hash = hash * 31 + sim.GetStamina(pid);
                var hand = sim.GetHand(pid);
                if (hand != null)
                {
                    foreach (var card in hand)
                    {
                        hash = hash * 31 + card.cardType;
                        hash = hash * 31 + card.value;
                        hash = hash * 31 + card.count;
                        hash = hash * 31 + card.durability;
                    }
                }
            }

            // 资源点余量（耗尽即消失——余量错=采集经济已分叉）
            foreach (var node in sim.SortedResourceNodes())
            {
                hash = hash * 31 + node.kind;
                hash = hash * 31 + node.cell.x;
                hash = hash * 31 + node.cell.y;
                hash = hash * 31 + node.remainingCharges;
                hash = hash * 31 + node.harvestSeconds;
            }
            foreach (var channel in sim.SortedHarvestChannels())
            {
                foreach (char c in channel.unitId) hash = hash * 31 + c;
                hash = hash * 31 + channel.nodeCell.x;
                hash = hash * 31 + channel.nodeCell.y;
                hash = hash * 31 + channel.startTick;
                hash = hash * 31 + channel.finishTick;
            }
            foreach (var discovery in sim.SortedResourceDiscoveryTicks())
            {
                foreach (char c in discovery.unitId) hash = hash * 31 + c;
                hash = hash * 31 + discovery.nextTick;
            }
            foreach (var cooldown in sim.SortedQuickHarvestCooldowns())
            {
                foreach (char c in cooldown.unitId) hash = hash * 31 + c;
                hash = hash * 31 + cooldown.nodeCell.x;
                hash = hash * 31 + cooldown.nodeCell.y;
                hash = hash * 31 + cooldown.readyTick;
            }

            // F0-4/F1：RNG 状态纳入 hash——随机序列一旦分叉必须能定位到 tick
            hash = hash * 31 + unchecked((int)sim.RngState);
            return hash;
        }

        private void OnDestroy()
        {
            _recentPlayerActions.Clear();
        }

        /// <summary>只管理输入定序与拍号，不推进模拟，也不读写传输层。</summary>
        private sealed class BattleInputTimeline
        {
            private readonly Dictionary<int, List<OrderedInput>> _scheduled = new Dictionary<int, List<OrderedInput>>();
            private readonly Dictionary<int, InputBatchMessage> _confirmed = new Dictionary<int, InputBatchMessage>();
            private int _nextTick = 1;
            private int _lastSealedTick;

            public bool TrySchedule(OrderedInput input, long preferredTick, int maxLeadTicks)
            {
                long target = Math.Max(preferredTick, (long)_lastSealedTick + 1);
                if (target > int.MaxValue || target - (_nextTick - 1L) > maxLeadTicks) return false;

                int tick = (int)target;
                if (!_scheduled.TryGetValue(tick, out var inputs))
                    _scheduled.Add(tick, inputs = new List<OrderedInput>());
                inputs.Add(input);
                return true;
            }

            public InputBatchMessage SealNext()
            {
                int tick = ++_lastSealedTick;
                var batch = new InputBatchMessage { tick = tick };
                if (_scheduled.TryGetValue(tick, out var inputs))
                {
                    _scheduled.Remove(tick);
                    // 真人与 AI 的序号可能相等；稳定排序保留 Host 入队顺序。
                    batch.inputs = inputs.Count > 1
                        ? inputs.OrderBy(input => input.playerId, StringComparer.Ordinal).ThenBy(input => input.seq).ToList()
                        : inputs;
                }
                _confirmed.Add(tick, batch);
                return batch;
            }

            public bool TryReceive(InputBatchMessage batch)
            {
                if (batch.tick < _nextTick || _confirmed.ContainsKey(batch.tick)) return false;
                _confirmed.Add(batch.tick, batch);
                return true;
            }

            public bool TryTakeNext(out InputBatchMessage batch)
            {
                if (!_confirmed.TryGetValue(_nextTick, out batch)) return false;
                _confirmed.Remove(_nextTick++);
                return true;
            }
        }
    }
}
