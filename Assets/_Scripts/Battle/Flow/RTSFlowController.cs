using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
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
    /// - **每秒整拍**（20 tick）：TurnResolver.ResolveSecondBeat（回合末族秒化：发放/Buff 计时/
    ///   泡湿/灵魂衰减/资源点站立采集/建筑效果）+ 快照广播 + desync hash 落盘（护栏四）+ 战斗时钟 +1 分钟；
    /// - **行动上交即校验即结算**（配额制退役——体力=手动操作唯一节流，蓝图 §5）；
    /// - **胜负判定**：核心摧毁唯一判据不变（CheckBattleOver 队伍口径同构）——每秒拍后+每次行动结算后检查。
    ///
    /// Phase 兼容映射（P1 过渡，P4 批退役 Selecting 语义）：Running=Selecting——HUD 上交链路/
    /// 配额脑触发/门面订阅零改动；HUD 每 0.5 秒收到同值事件=幂等无害（Tip 重设同文本）。
    /// 回合数显示退役：TurnNumber 属性=逻辑秒（配额脑验证口径），战斗时钟 BattleTimeMinutes 为主显示。
    /// </summary>
    public class RTSFlowController : MonoBehaviour, ITurnResolverHost
    {
        // ==================== 对外状态（View/门面兼容层） ====================

        /// <summary>阶段（P1 兼容映射：Running=Selecting、终局=Finished；Idle 仅开局前）</summary>
        public BattlePhase Phase { get; private set; } = BattlePhase.Idle;

        /// <summary>当前 tick（即时制真源——1 秒=20 tick，BattleMetrics.TicksPerSecond）</summary>
        public int CurrentTick { get; private set; }

        /// <summary>逻辑秒（tick/20）——配额脑/回放/周期类相对计数的兼容口径（旧「回合数」秒化）</summary>
        public int TurnNumber => CurrentTick / BattleMetrics.TicksPerSecond;

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
        private TurnResolver _resolver;

        // ==================== 帧同步输入管线（F1，docs/active/50 §4） ====================

        /// <summary>Host 侧=true（唯一权威输入定序者）；客户端=false（只消费 InputBatch，不跑脑/不定序）</summary>
        public bool IsHost { get; private set; } = true;

        /// <summary>联机模式=true（输入按 targetTick 对齐）；单机=false（即时结算，零手感损失）</summary>
        public bool IsNetworked { get; private set; }

        /// <summary>待结算输入批（按 targetTick 升序）；Host 与客户端共用</summary>
        private readonly SortedDictionary<int, List<OrderedInput>> _inputByTick =
            new SortedDictionary<int, List<OrderedInput>>();

        /// <summary>玩家内输入序号（Host 定序 tie-break；客户端提交时递增）</summary>
        private readonly Dictionary<string, int> _inputSeq = new Dictionary<string, int>();

        /// <summary>Host 侧每秒 hash（按 tick 存，供客户端 HashReport 比对）</summary>
        private readonly Dictionary<int, int> _hostHashByTick = new Dictionary<int, int>();

        /// <summary>最近一次 desync tick（-1=无）</summary>
        public int LastDesyncTick { get; private set; } = -1;

        /// <summary>尚未广播的输入批 tick（Host）：入队即标脏，tick 末尾统一提前广播（设计稿 §3.3
        /// 「Host 至少提前 1 tick 广播」——不能等到应用该 tick 时才发，否则客户端注定晚到丢批）。</summary>
        private readonly HashSet<int> _dirtyBatchTicks = new HashSet<int>();

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
                _accumulator -= BattleMetrics.TickSeconds;
                TickAdvance();
                steps++;
                if (Phase != BattlePhase.Selecting) break; // 拍内终局（核心破）即停
            }
        }

        /// <summary>单 tick 推进：AI 节拍（%10）+秒化整拍（%20）</summary>
        private void TickAdvance()
        {
            CurrentTick++;
            _sim.CurrentTick = CurrentTick;

            // —— F1：先应用本 tick 输入批（Host 广播+两端同批结算），再做移动/节拍 ——
            ApplyDueInputs();

            // —— tick 移动推进（P2 连续空间，蓝图 §3.3：A* 路径+圆碰撞每 tick 步进；收尾段=采集结算）——
            var arrivals = RTSMovementSystem.AdvanceTick(_sim);
            if (arrivals != null && arrivals.Count > 0)
            {
                _resolver.ResolveMoveArrivals(arrivals);
                CheckBattleEnded();
            }

            // —— AI 三脑节拍（每 0.5 秒）——
            if (CurrentTick % BattleMetrics.AiBeatTicks == 0)
                RunAiBeat();

            // —— 每秒整拍（发放/Buff 计时/泡湿/灵魂/采集/建筑效果 + 快照 + 时钟 + hash）——
            if (CurrentTick % BattleMetrics.TicksPerSecond == 0)
                RunSecondBeat();

            // —— F1：tick 末尾把本拍新增的输入批提前广播（设计稿 §3.3「至少提前 1 tick」）——
            // 放在 AI 节拍/秒拍之后：本拍生成的 AI 行动（targetTick=下一 tick）当拍即出，
            // 客户端下一 tick 才有整整一个 tick（50ms）的接收余量。
            FlushInputBroadcasts();
        }

        // ==================== AI 节拍（每 0.5 秒，蓝图 §6：三脑同频） ====================

        private void RunAiBeat()
        {
            if (Phase != BattlePhase.Selecting) return;
            if (!IsHost) return; // F1：AI 三脑只在 Host 跑；客户端只消费 InputBatch 里的 AI 行动
            int second = TurnNumber;

            // Host 探索账本同步（决策七十九 P4 适配：旧「BeginSelectPhase 单钩」随回合机退役——
            // 决策时态同步语义秒化为每 AI 节拍揭雾；客户端表现层逐帧揭雾不受影响）
            _sim.RevealFog();

            // 眷属脑（1~2★ 启发式）+ 伙伴脑（3~4★ 评分制——感知窗口内玩家上交行动）
            var actions = new List<ActionData>();
            actions.AddRange(FamiliarBrain.DecideAll(_sim, second));
            var playerWindow = new List<ActionData>(_recentPlayerActions);
            actions.AddRange(CompanionBrain.DecideAll(_sim, second, playerWindow));
            _recentPlayerActions.Clear();
            foreach (var action in actions)
            {
                action.turnNumber = second;
                action.submitTick = CurrentTick;
            }

            // 回放录制（决策九十五：节拍批=旧「回合全量行动」的秒化等价物——回放按批直喂演算）
            BattleRecorder.CaptureActions(second, actions);
            if (IsNetworked)
            {
                // F1：AI 行动也作为输入进批（targetTick=下一 tick），客户端同批结算；
                // 不在此刻结算——否则 Host 提前一 tick，与客户端不同步
                foreach (var action in actions)
                    EnqueueInput(action, CurrentTick + 1, isAi: true, seq: NextSeq(action.playerId));
            }
            else
            {
                _resolver.ResolveAiActions(actions);
            }

            // 配额脑驱动（PlayerQuotaBrain 场景件零改动兼容：沿用 Selecting 事件触发，
            // 脑内协程验证 Phase==Selecting && TurnNumber==second 后 Decide→SubmitAction）；
            // HUD 门面同收本事件=同值幂等刷新（P4 批拆分专用事件）
            OnPhaseChanged?.Invoke(BattlePhase.Selecting, second);

            CheckBattleEnded();
        }

        // ==================== 每秒整拍（回合末族秒化，蓝图 §2.3/§9） ====================

        private void RunSecondBeat()
        {
            if (Phase != BattlePhase.Selecting) return;
            int second = TurnNumber;

            // 效果拍（2026-11-04 拍板「时间翻倍」）：Buff tick 效果+建筑效果每 2 秒一拍
            //（EffectBeatTicks=40）——秒拍族（发放/计时递减/泡湿/采集/快照/时钟）仍每秒
            bool effectBeat = CurrentTick % BattleMetrics.EffectBeatTicks == 0;
            _resolver.ResolveSecondBeat(second, effectBeat);

            CheckBattleEnded();
            if (Phase != BattlePhase.Selecting) return; // 秒拍内终局不再广播快照

            // 快照广播（对账/自愈——旧「每选择阶段头」秒化为每秒一次）
            _transport.HostSend(BattleMessageType.Snapshot, new SnapshotMessage
            {
                snapshot = _sim.TakeSnapshot(second),
                selectLimitSeconds = 0, // 即时制无选择倒计时（View 兼容字段）
            });

            // 战斗时钟：每秒 +1 分钟（昼夜跨界音乐选池链按既有 BattleTimePeriod 消费自动跟随）
            BattleTimeMinutes = (BattleTimeMinutes + BattleMetrics.BattleClockMinutesPerSecond) % (24 * 60);

            // desync hash（确定性护栏四：单机自证断言 + 后置 LAN 漂移定位，王者荣耀同款）
            // desync hash（确定性护栏四）：Host 记录按 tick 供比对；客户端上报
            int stateHash = ComputeStateHash(_sim);
            BattleRecorder.CaptureTickHash(CurrentTick, stateHash);
            if (IsNetworked)
            {
                if (IsHost)
                {
                    _hostHashByTick[CurrentTick] = stateHash;
                    if (_hostHashByTick.Count > 4 * 60 * BattleMetrics.TicksPerSecond)
                    {
                        int minTick = CurrentTick - 4 * 60 * BattleMetrics.TicksPerSecond;
                        var stale = new List<int>();
                        foreach (var kv in _hostHashByTick)
                            if (kv.Key < minTick) stale.Add(kv.Key);
                        foreach (var k in stale) _hostHashByTick.Remove(k);
                    }
                }
                else
                {
                    _transport.ClientSend(BattleMessageType.HashReport, new HashReportMessage
                    {
                        tick = CurrentTick,
                        hash = stateHash,
                        unitCount = _sim.Units.Count,
                    });
                }
            }
        }

        // ==================== 行动上交（配额制退役：即交即结算） ====================

        /// <summary>
        /// 行动上交（本地双开：双端共用一进程都走本入口；BattleSession.SubmitAction 路由至此）——
        /// 校验链沿用旧 OnSubmitAction 权威防线（灵魂/归属/建筑/眷属防线、伙伴 isPlayerDirectControl
        /// 标记），配额/回合号/重复上交三道旧门随回合机退役。
        /// </summary>
        public void OnSubmitAction(ActionData action)
        {
            if (Phase != BattlePhase.Selecting)
            {
                GICLog.Warn($"[RTSFlow] 战斗未运行/已终局，忽略 {action}");
                return;
            }
            if (action == null || !_sim.PlayerIds.Contains(action.playerId))
            {
                GICLog.Warn($"[RTSFlow] 未知玩家 {action?.playerId}，忽略");
                return;
            }

            // 玩家级行动（无行动单位）豁免单位校验：部署/物品/装备（目标校验在各自执行器内收口）
            if (action.actionType == ActionType.DeployUnit || action.actionType == ActionType.UseItem
                || action.actionType == ActionType.EquipItem)
            {
                SubmitAndResolve(action);
                return;
            }

            if (action.actionType != ActionType.Move && action.actionType != ActionType.Skill
                && action.actionType != ActionType.Pass)
            {
                GICLog.Warn($"[RTSFlow] 不支持行动类型 {action.actionType}，忽略（B1 白名单）");
                return;
            }

            var unit = _sim.GetUnit(action.unitId);
            if (unit == null)
            {
                GICLog.Warn($"[RTSFlow] 未知单位 {action.unitId}，忽略");
                return;
            }

            // 灵魂无任何行动（决策九十一）：Host 权威防线
            if (unit.IsSoul)
            {
                GICLog.Warn($"[RTSFlow] 灵魂 {action.unitId} 无任何行动（地脉驻留体），忽略上交");
                return;
            }

            var identity = unit.GetUnitComponent<UnitIdentity>();
            if (identity == null || identity.OwnerPlayerID != action.playerId)
            {
                GICLog.Warn($"[RTSFlow] 单位 {action.unitId} 不属于玩家 {action.playerId}，忽略");
                return;
            }

            // 建筑防线（协议核心批）：建筑（含协议核心）不参与任何行动
            if (unit.RawData != null && unit.RawData.unitType == UnitType.Building)
            {
                GICLog.Warn($"[RTSFlow] 建筑 {action.unitId}（含协议核心）不接受任何玩家上交行动，忽略");
                return;
            }

            // 单位级行动域校验（操控分层，docs/32 §1/§4；伙伴技能解锁 2026-10-12）：
            // 眷属=AI 域；伙伴=玩家可直接操控（标记 isPlayerDirectControl——体力减免 5+号令津贴）
            var tier = BattleHeuristics.TierOf(unit);
            if (tier == UnitTier.Familiar)
            {
                GICLog.Warn($"[RTSFlow] 眷属 {action.unitId} 不接受玩家上交行动（启发式脑自主决策），忽略");
                return;
            }
            if (tier == UnitTier.Companion)
                action.isPlayerDirectControl = true;

            SubmitAndResolve(action);
        }

        /// <summary>校验通过：打时刻戳→伙伴脑感知窗→即时演算→胜负自检</summary>
        private void SubmitAndResolve(ActionData action)
        {
            action.turnNumber = TurnNumber;
            action.submitTick = CurrentTick;
            BattleRecorder.CaptureSubmittedAction(action); // 回放 v2：玩家/配额脑上交行动录制（校验后=拒绝不上盘）
            _recentPlayerActions.Add(action); // 伙伴脑下一节拍的感知输入（窗口 0.5 秒）

            if (!IsNetworked)
            {
                // 单机：保持即时结算（零手感损失）
                _resolver.ResolveSubmittedAction(action);
                CheckBattleEnded();
                return;
            }
            if (IsHost)
            {
                // Host：入队到权威窗口，由 ApplyDueInputs 在 targetTick 结算+广播
                EnqueueInput(action, CurrentTick + BattleMetrics.InputDelayTicks,
                    isAi: false, seq: NextSeq(action.playerId));
                return;
            }
            // 客户端：只上报输入（F3 预测在 View 层）；本地不结算
            _transport.ClientSend(BattleMessageType.InputCommand, new InputCommandMessage
            {
                playerId = action.playerId,
                seq = NextSeq(action.playerId),
                submitTick = CurrentTick,
                action = action,
            });
        }

        // ==================== 胜负判定（核心摧毁唯一判据，口径同旧 CheckBattleOver） ====================

        /// <summary>终局检查（每行动结算后+每秒拍后）：sim.IsBattleDecided 截断判据 + 队伍口径广播</summary>
        private void CheckBattleEnded()
        {
            if (Phase != BattlePhase.Selecting) return; // 已终局幂等
            var overWinner = CheckBattleOver(_sim);
            if (!overWinner.HasValue) return;

            Phase = BattlePhase.Finished;
            OnPhaseChanged?.Invoke(Phase, TurnNumber);
            _transport.HostSend(BattleMessageType.BattleOver, new BattleOverMessage
            {
                winnerTeam = (int)overWinner.Value,
            });
            BattleRecorder.CaptureGameOver((int)overWinner.Value, TurnNumber); // 回放终局对照
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
            bool isHost = true, bool isNetworked = false)
        {
            _sim = sim;
            _transport = transport;
            IsHost = isHost;
            IsNetworked = isNetworked;
            _resolver = new TurnResolver(sim, transport, this);
        }

        // ==================== 帧同步输入管线（F1） ====================

        private int NextSeq(string playerId)
        {
            _inputSeq.TryGetValue(playerId, out int seq);
            _inputSeq[playerId] = seq + 1;
            return seq;
        }

        /// <summary>入队一条输入（Host 侧；targetTick 已重定基）。同 tick 内按 (playerId, seq) 定序。</summary>
        private void EnqueueInput(ActionData action, int targetTick, bool isAi, int seq)
        {
            if (action == null) return;
            if (targetTick <= CurrentTick) targetTick = CurrentTick + 1; // 过期→顺延下一 tick
            if (targetTick - CurrentTick > BattleMetrics.MaxInputLatenessTicks)
            {
                GICLog.Warn($"[RTSFlow] 输入迟到超限丢弃 player={action.playerId} target={targetTick} now={CurrentTick}");
                return;
            }
            if (!_inputByTick.TryGetValue(targetTick, out var list))
            {
                list = new List<OrderedInput>();
                _inputByTick[targetTick] = list;
            }
            list.Add(new OrderedInput
            {
                playerId = action.playerId,
                seq = seq,
                isAi = isAi ? 1 : 0,
                action = action,
            });
            if (IsHost) _dirtyBatchTicks.Add(targetTick); // 待广播（tick 末尾统一提前发出）
        }

        /// <summary>输入定序（唯一序）：playerId 序数升序 → seq 升序。Host 广播前与结算前同源消费，
        /// 客户端保序不重排（docs/active/50 §4.2 定序规则）。</summary>
        private static int CompareInputs(OrderedInput a, OrderedInput b)
        {
            int p = string.CompareOrdinal(a.playerId, b.playerId);
            return p != 0 ? p : a.seq.CompareTo(b.seq);
        }

        /// <summary>Host：把标脏的输入批提前广播（tick 末尾调用）。只发改动过的 tick，
        /// 且必须在应用前 ≥1 tick 到手——客户端据此与本端同批同序结算。</summary>
        private void FlushInputBroadcasts()
        {
            if (!IsHost || !IsNetworked || _dirtyBatchTicks.Count == 0) return;
            var ticks = new List<int>(_dirtyBatchTicks);
            ticks.Sort();
            foreach (var tick in ticks)
            {
                _dirtyBatchTicks.Remove(tick);
                if (tick <= CurrentTick) continue; // 已应用/过期，不补发
                if (!_inputByTick.TryGetValue(tick, out var list) || list.Count == 0) continue;
                // 定序在广播前完成：客户端「保序不重排」，本处序=各端结算序（唯一序）。
                list.Sort(CompareInputs);
                _transport.HostSend(BattleMessageType.InputBatch,
                    new InputBatchMessage { tick = tick, inputs = new List<OrderedInput>(list) });
            }
        }

        /// <summary>应用当前 tick 的输入批：各端按同一序结算（Host 定序在广播前完成）。</summary>
        private void ApplyDueInputs()
        {
            if (!_inputByTick.TryGetValue(CurrentTick, out var inputs) || inputs.Count == 0) return;
            _inputByTick.Remove(CurrentTick);

            // Host 是唯一定序者；客户端保留 Host 下发的批内顺序（勿重排）
            if (IsHost)
            {
                // 定序在广播前完成：FlushInputBroadcasts 发的是「应用时刻之前的同一份序」，
                // 勿在此刻再发一份（那时客户端已进入/越过本 tick，只会收到「迟到丢弃」警告）。
                // 此处再排一次=幂等（广播后可能又追加了迟到项，重定基已保证不会进来）。
                inputs.Sort(CompareInputs);
            }

            var aiBatch = new List<ActionData>();
            foreach (var input in inputs)
            {
                if (input?.action == null) continue;
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
            CheckBattleEnded();
        }

        /// <summary>Host：收到客户端输入（重定基到 targetTick 后入队）。</summary>
        public void ReceiveInputCommand(InputCommandMessage msg)
        {
            if (msg?.action == null) return;
            int target = msg.submitTick + BattleMetrics.InputDelayTicks;
            int min = CurrentTick + 1;
            if (target < min) target = min;
            // 与 Host 本地玩家同权：远端行动也进伙伴脑「感知窗口内玩家行动」（F1 单机同构——
            // 旧实现只看到 Host 本地玩家，AI 行为随「谁在操作」而变）。
            _recentPlayerActions.Add(msg.action);
            EnqueueInput(msg.action, target, isAi: false, seq: msg.seq);
        }

        /// <summary>客户端：收到 Host 定序输入批（不重排，按批内顺序结算）。</summary>
        public void ReceiveInputBatch(InputBatchMessage msg)
        {
            if (msg?.inputs == null || msg.inputs.Count == 0) return;
            if (msg.tick <= CurrentTick)
            {
                GICLog.Warn($"[RTSFlow] 输入批迟到丢弃 tick={msg.tick} now={CurrentTick}");
                return;
            }
            _inputByTick[msg.tick] = msg.inputs;
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
            _accumulator = 0f;
            BattleTimeMinutes = BattleClockStartMinutes; // 独立时钟复位（06:00 起）
            Phase = BattlePhase.Selecting;
            _transport.HostSend(BattleMessageType.Snapshot, new SnapshotMessage
            {
                snapshot = _sim.TakeSnapshot(0),
                selectLimitSeconds = 0,
            });
            OnPhaseChanged?.Invoke(Phase, 0);
        }

        /// <summary>停止状态机（战斗结束/退出场景）</summary>
        public void Stop()
        {
            Phase = BattlePhase.Finished;
            OnPhaseChanged?.Invoke(Phase, TurnNumber);
        }

        // ==================== ITurnResolverHost（ack 门控退役——no-op 兼容层） ====================

        /// <summary>ack 门控已随回合机退役（决策九十五）——保留接口空实现（回放宿主兼容）</summary>
        public void NotifySegmentPushed(int turnNumber, int sliceIndex) { }

        /// <summary>ack 门控已退役——恒 true（无等待语义）</summary>
        // P6 残留审计 D2：HasSegmentAck 随 ack 门控退役删除（ITurnResolverHost 已收窄）。

        // ==================== desync hash（确定性护栏四，蓝图 §2.2） ====================

        /// <summary>战场状态 hash（确定序遍历：单位 unitId 升序→HP/元能/位置；玩家资源按注册序）——
        /// 同 seed 同命令流必同 hash（漂移=演算层引入了非确定性）；单机=回放自证断言，
        /// 后置 LAN=双端比对定位漂移拍（王者荣耀不同步率 2%→0.03% 同款手段）。
        /// 回放器 v2 同源消费——public static 单源勿复制分叉</summary>
        public static int ComputeStateHash(BattleSimState sim)
        {
            int hash = 17;
            var unitIds = new List<string>(sim.Units.Keys);
            unitIds.Sort(StringComparer.Ordinal); // 确定性：unitId 升序（Dictionary 枚举序不稳定）
            foreach (var unitId in unitIds)
            {
                var unit = sim.Units[unitId];
                if (unit == null) continue;
                var stats = unit.GetUnitComponent<UnitStats>();
                if (stats == null) continue;
                var pos = sim.GetPosition(unit);
                hash = hash * 31 + stats.HP;
                hash = hash * 31 + stats.Energy;
                hash = hash * 31 + pos.x;
                hash = hash * 31 + pos.y;
            }
            foreach (var pid in sim.PlayerIds) // PlayerIds=注册序（List，稳定）
            {
                hash = hash * 31 + sim.GetMora(pid);
                hash = hash * 31 + sim.GetStamina(pid);
            }
            // F0-4/F1：RNG 状态纳入 hash——随机序列一旦分叉必须能定位到 tick
            hash = hash * 31 + unchecked((int)sim.RngState);
            return hash;
        }

        private void OnDestroy()
        {
            _recentPlayerActions.Clear();
        }
    }
}
