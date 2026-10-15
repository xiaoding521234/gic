using System;
using System.Collections.Generic;
using System.Linq;
using GIC.Data;
using GIC.Framework;
namespace GIC.Battle
{


    /// <summary>
    /// 对手档案＋事件账本（AI-2 批，docs/active/42 §4/§11）：每敌方玩家一份滚动行为画像
    /// ＋永久事件标记——**AI-3 外交层（态度账/契约 P签/背叛判定）的地基**；本批=采集侧落地，
    /// 消费侧随 AI-3 激活（现行唯一行为改动=威胁图伙伴脑接线，见 CompanionBrain.DecideAll）。
    /// 信息边界红线（§1①）：只读公共信息——①各玩家已提交配额行动（已交选择=非隐私，
    /// I-B 拍板「阶段三本就感知」同口径，DecideAll 传入）；②命令流（Segment 全量=对全体客户端
    /// 广播的公开态）。不读敌方未交选择/手牌/内部决策中间产物。
    /// 确定性：全链纯确定性采集（提交与命令流均按结算序），零随机。
    /// 生命周期：进程级 static＋新战斗自清（sim 实例号判据——2026-10-15 审查修正；StartBattle
    /// 恒新 BattleSimState，无需 BattleSession 接线）。
    /// </summary>
    public static class OpponentProfiler
    {
        /// <summary>习惯计数每回合衰减系数（§4 遗忘策略「指数滑动平均」族——半衰期≈7 回合，
        /// 与样本环「近 8 回合加倍」同节奏：允许对手变招但需持续证据）</summary>
        private const float HabitDecayPerTurn = 0.9f;

        /// <summary>习惯计数剪枝阈值（低于即移除，防长期对局长尾膨胀）</summary>
        private const float HabitPruneThreshold = 0.05f;

        // ==================== 画像 ====================

        /// <summary>
        /// 单个敌方玩家档案（§4 字段全量；contractHabit=预留字段——契约系统未实装无生产者，
        /// AI-3 激活时接线）。
        /// FavoriteTarget 口径注记：全局键控（受击 unitId→计数）而非按观察者分键——1v1 精确；
        /// 2v2 混入队友受击（消费方可按 victim 队属过滤），多观察者细分随 AI-3 态度账按需升级。
        /// </summary>
        public sealed class OpponentProfile
        {
            /// <summary>攻击性占比（0~1）＝配额行动中攻击技能（Normal/Burst）提交占比——
            /// 滚动样本环（玩家域口径：眷属/伙伴自主攻击不计入=反映玩家本人的进攻意志）</summary>
            public readonly SampleRing Assertiveness = new();

            /// <summary>交战距离偏好（格，切比雪夫）＝攻击时刻发射格↔命中点距离滚动样本</summary>
            public readonly SampleRing EngageDistance = new();

            /// <summary>元素习惯（Damage 命令 metadata=元素 → 衰减计数；「他们用什么元素打人」）</summary>
            public readonly Dictionary<int, float> ElementHabit = new();

            /// <summary>最爱目标（受击 unitId → 衰减计数；「他们最爱打谁」）</summary>
            public readonly Dictionary<string, float> FavoriteTarget = new();

            /// <summary>契约习惯（预留——AI-3 激活时接线生产者）：平均档位/拒签/毁约/加利发起次数</summary>
            public readonly SampleRing AvgContractTier = new();
            public float ContractRefuseCount;
            public float ContractBreakCount;
            public float ContractSweetenerCount;

            /// <summary>事件账本·永久标记（§4「重大事件永久标记不衰减」——记仇）：该玩家摧毁过核心的队伍集</summary>
            public readonly HashSet<TeamType> DestroyedCoresOf = new();

            internal void DecayHabits()
            {
                DecayDictionary(ElementHabit);
                DecayDictionary(FavoriteTarget);
                ContractRefuseCount *= HabitDecayPerTurn;
                ContractBreakCount *= HabitDecayPerTurn;
                ContractSweetenerCount *= HabitDecayPerTurn;
            }

            /// <summary>衰减＋剪枝（2026-10-12 观察局实锤返修：Dictionary 枚举中**值写入**同样 bump
            /// 版本号炸枚举器——与决策五十七 CollectAnyItems「物化列表」同族陷阱；键表物化后写值）</summary>
            private static void DecayDictionary<TKey>(Dictionary<TKey, float> map)
            {
                if (map.Count == 0) return;
                var keys = new List<TKey>(map.Keys);
                var prune = new List<TKey>();
                foreach (var key in keys)
                {
                    map[key] = map[key] * HabitDecayPerTurn;
                    if (map[key] < HabitPruneThreshold) prune.Add(key);
                }
                foreach (var key in prune) map.Remove(key);
            }
        }

        /// <summary>
        /// 数值画像滚动样本环（§4 遗忘策略：指数滑动平均——**近 8 回合样本权重×2**、更早样本权重×1，
        /// 容量 16 回合窗口）。查询=按当前回合加权平均（允许对手变招但需持续证据）。
        /// </summary>
        public sealed class SampleRing
        {
            private readonly List<(int turn, float value)> _samples = new();

            public void AddSample(int turn, float value)
            {
                _samples.Add((turn, value));
                if (_samples.Count > 16) _samples.RemoveAt(0); // 环形窗口（超容量丢最旧）
            }

            /// <summary>近 8 回合加权的滚动平均值（无样本=0）</summary>
            public float WeightedValue(int currentTurn)
            {
                if (_samples.Count == 0) return 0f;
                float sum = 0f, weightSum = 0f;
                foreach (var s in _samples)
                {
                    float w = currentTurn - s.turn < 8 ? 2f : 1f;
                    sum += s.value * w;
                    weightSum += w;
                }
                return weightSum > 0f ? sum / weightSum : 0f;
            }

            /// <summary>最新样本（无=0；快变信号用）</summary>
            public float Latest => _samples.Count > 0 ? _samples[^1].value : 0f;

            public int Count => _samples.Count;
        }

        // ==================== 状态（进程级＋新战斗自清） ====================

        private static readonly Dictionary<string, OpponentProfile> _profiles = new();
        private static int _lastObservedTurn = -1; // 回合推进衰减游标（同回合多次观察只衰减一次）
        private static int _lastSimInstance;       // 新战斗自清判据=对局实例号（2026-10-15 审查修正）

        /// <summary>全量档案只读视图（harness/调试 dump 用）</summary>
        public static IReadOnlyDictionary<string, OpponentProfile> AllProfiles => _profiles;

        /// <summary>玩家档案（get-or-create；观察点与消费点共用）</summary>
        public static OpponentProfile ProfileOf(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return null;
            if (!_profiles.TryGetValue(playerId, out var profile))
            {
                profile = new OpponentProfile();
                _profiles[playerId] = profile;
            }
            return profile;
        }

        /// <summary>事件账本查询（§6.1 态度账「记仇」消费）：该玩家是否摧毁过指定队伍的核心</summary>
        public static bool HasDestroyedCoreOf(string playerId, TeamType victimTeam)
            => _profiles.TryGetValue(playerId, out var p) && p.DestroyedCoresOf.Contains(victimTeam);

        // ==================== 观察点 1：配额行动（CompanionBrain.DecideAll 调） ====================

        /// <summary>
        /// 各玩家本回合已提交配额行动分类 → 攻击性占比样本。玩家域口径：Skill(Normal/Burst)=攻击
        /// （战技/爆发=战斗行动）；Henka/Enso/Contract/Move/DeployUnit/Pass=非攻击（部署/走位/
        /// 号令/空过=经营与支援）。
        /// </summary>
        public static void ObserveTurnActions(BattleSimState sim, List<ActionData> playerActions, int turnNumber)
        {
            if (playerActions == null || playerActions.Count == 0) return;
            // 采集侧防御（2026-10-12 观察局实锤返修）：本观察点在 DecideAll 主链上——任何异常都会
            // 死锁回合流（TryBeginResolve 无兜底）——档案为辅助数据，绝不具备杀死对局的能力
            try
            {
                EnsureFresh(sim, turnNumber);

                var attackCount = new Dictionary<string, int>();
                var totalCount = new Dictionary<string, int>();
                foreach (var pa in playerActions)
                {
                    if (pa == null || string.IsNullOrEmpty(pa.playerId)) continue;
                    totalCount[pa.playerId] = (totalCount.TryGetValue(pa.playerId, out var t) ? t : 0) + 1;
                    if (pa.actionType != ActionType.Skill) continue;
                    var actor = sim != null ? sim.GetUnit(pa.unitId) : null;
                    var skill = actor != null && pa.skillIndex >= 0 && pa.skillIndex < actor.Skills.Count
                        ? actor.Skills[pa.skillIndex]
                        : null;
                    var skillType = skill?.RawData?.skillType ?? SkillType.Talent;
                    if (skillType == SkillType.Normal || skillType == SkillType.Burst)
                        attackCount[pa.playerId] = (attackCount.TryGetValue(pa.playerId, out var a) ? a : 0) + 1;
                }
                foreach (var kv in totalCount)
                {
                    attackCount.TryGetValue(kv.Key, out var attacks);
                    ProfileOf(kv.Key).Assertiveness.AddSample(turnNumber, kv.Value > 0 ? (float)attacks / kv.Value : 0f);
                }
            }
            catch (Exception ex)
            {
                GICLog.Warn($"[对手档案] 配额行动观察异常 T{turnNumber}（跳过本回合，不阻塞主流程）: {ex.Message}");
            }
        }

        // ==================== 观察点 2：命令流（TurnResolver.PushSegmentAndWaitAck 调） ====================

        /// <summary>
        /// 段命令流观察（Host 侧、Segment 完整构建后、广播前）：
        /// Damage→元素习惯/最爱目标/交战距离；Death(建筑)→事件账本核心摧毁标记（永久）。
        /// 死亡归因=同段内该受击者最近一次 Damage 的攻击者（ResolveDeaths 与伤害同段结算）。
        /// </summary>
        public static void ObserveCommands(BattleSimState sim, List<BattleCommand> commands, int turnNumber)
        {
            if (sim == null || commands == null || commands.Count == 0) return;
            // 采集侧防御（同 ObserveTurnActions）：段广播主链上的观察点——异常绝不阻塞推段
            try
            {
                EnsureFresh(sim, turnNumber);

                var lastDamageAttackerByVictim = new Dictionary<string, string>();
                foreach (var cmd in commands)
                {
                    switch (cmd.type)
                    {
                        case BattleCommandType.Damage:
                        {
                            var attackerOwner = OwnerOf(sim, cmd.actorUnitId);
                            if (attackerOwner == null) break; // 无主伤害（环境类）不计画像
                            lastDamageAttackerByVictim[cmd.targetUnitId] = cmd.actorUnitId;
                            var profile = ProfileOf(attackerOwner);
                            profile.ElementHabit[cmd.metadata] =
                                (profile.ElementHabit.TryGetValue(cmd.metadata, out var e) ? e : 0f) + 1f;
                            profile.FavoriteTarget[cmd.targetUnitId] =
                                (profile.FavoriteTarget.TryGetValue(cmd.targetUnitId, out var f) ? f : 0f) + 1f;
                            // 交战距离=发射格↔命中点切比雪夫（命中点千分格→格）
                            float hitX = cmd.hitX / 1000f, hitY = cmd.hitY / 1000f;
                            float dist = Math.Max(Math.Abs(cmd.cell.x - hitX), Math.Abs(cmd.cell.y - hitY));
                            profile.EngageDistance.AddSample(turnNumber, dist);
                            break;
                        }
                        case BattleCommandType.Death:
                        {
                            // 事件账本：核心摧毁=永久标记不衰减（§4「记仇是可依赖的长期行为」；
                            // 归因=同段最近攻击者——核心单位（Core tag，2026-10-13 tag 化：
                            // 协议核心/未来世界树同记账）死亡即记账）
                            var victim = sim.GetUnit(cmd.targetUnitId);
                            if (victim == null || !BattleHeuristics.IsCore(victim)) break;
                            if (!lastDamageAttackerByVictim.TryGetValue(cmd.targetUnitId, out var killerId)) break;
                            var killerOwner = OwnerOf(sim, killerId);
                            var victimIdentity = victim.GetUnitComponent<UnitIdentity>();
                            if (killerOwner == null || victimIdentity == null
                                || killerOwner == victimIdentity.OwnerPlayerID) break;
                            if (ProfileOf(killerOwner).DestroyedCoresOf.Add(victimIdentity.Team))
                                GICLog.Info($"[对手档案] {killerOwner} 摧毁 {victimIdentity.Team} 方协议核心——事件账本永久标记（记仇）");
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                GICLog.Warn($"[对手档案] 命令流观察异常 T{turnNumber}（跳过本段，不阻塞推段）: {ex.Message}");
            }
        }

        // ==================== 内部 ====================

        private static string OwnerOf(BattleSimState sim, string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return null;
            var unit = sim.GetUnit(unitId);
            return unit?.GetUnitComponent<UnitIdentity>()?.OwnerPlayerID;
        }

        /// <summary>新战斗自清＋回合推进衰减（双观察点共用；同回合多次观察只衰减一次）。
        /// 自清判据=sim 实例号（2026-10-15 审查修正——每局新 BattleSimState 恒新值；旧「T1 重入」
        /// 判据对「上局仅观测到回合 1」的秒弃调试局失效〔T1==T1 不触发〕→陈旧画像跨局泄漏；
        /// sim 为 null 的防御观察点回落旧回合判据）</summary>
        private static void EnsureFresh(BattleSimState sim, int turnNumber)
        {
            if (sim != null && sim.InstanceId != _lastSimInstance)
            {
                _profiles.Clear();
                _lastSimInstance = sim.InstanceId;
                _lastObservedTurn = turnNumber;
                return;
            }
            if (sim == null && turnNumber <= 1 && _lastObservedTurn != 1)
            {
                _profiles.Clear();
                _lastObservedTurn = 1;
                return;
            }
            if (turnNumber > _lastObservedTurn)
            {
                foreach (var profile in _profiles.Values)
                    profile.DecayHabits();
                _lastObservedTurn = turnNumber;
            }
        }
    }
}
