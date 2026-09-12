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
    /// 执行阶段片循环（docs/22 §2 分步流式演算）：
    /// 攻速分桶 → 片内快照结算（瞬发读片前快照）→ 移动同步逐步结算 → 效应统一应用
    /// → 产出片命令块 → 推送并等待 ack（超时快进）→ 片边界 poll 即时行动队列。
    ///
    /// Host 永不跑在客户端前面（片级 ack 门控）。
    /// </summary>
    public class TurnResolver
    {
        private readonly BattleSimState _sim;
        private readonly IBattleTransport _transport;
        private readonly TurnFlowController _flow;

        /// <summary>ack 等待超时（秒，真实时间；超时快进）</summary>
        private const float AckTimeoutSeconds = 15f;

        public TurnResolver(BattleSimState sim, IBattleTransport transport, TurnFlowController flow)
        {
            _sim = sim;
            _transport = transport;
            _flow = flow;
        }

        // ==================== 回合主循环 ====================

        public IEnumerator ResolveTurnCoroutine(int turnNumber, List<ActionData> actions)
        {
            // 攻速分桶：攻速值完全相同才同片；片按攻速降序（先结算高攻速）
            var buckets = BucketByAttackSpeed(actions);
            int maxSpeed = 0;
            if (buckets.Count > 0)
                maxSpeed = buckets[0].speed;

            int sliceIndex = 0;
            foreach (var bucket in buckets)
            {
                var segment = ResolveSlice(turnNumber, sliceIndex, bucket.speed, maxSpeed, bucket.actions);
                yield return PushSegmentAndWaitAck(segment);
                sliceIndex++;
            }

            // 片边界 poll 即时行动队列（连携/契约类；B1 调试验证协议预留）
            while (true)
            {
                var instant = _sim.DequeueInstantAction();
                if (instant == null) break;

                var segment = ResolveInstantAction(turnNumber, sliceIndex, maxSpeed, instant);
                if (segment != null)
                {
                    yield return PushSegmentAndWaitAck(segment);
                    sliceIndex++;
                }
            }

            // 回合结束效果按注册序（B1 无注册）→ WaitConditions 扫描（B1 无）
            _transport.HostSend(BattleMessageType.TurnEnd, new TurnEndMessage { turnNumber = turnNumber });
        }

        // ==================== 片结算 ====================

        /// <summary>
        /// 片内结算：瞬发效应读片前快照独立结算，移动同片动态逐步展开，效应统一应用
        /// </summary>
        private Segment ResolveSlice(int turnNumber, int sliceIndex, int sliceSpeed, int maxSpeed, List<ActionData> actions)
        {
            var segment = new Segment
            {
                turnNumber = turnNumber,
                sliceIndex = sliceIndex,
                sliceAttackSpeed = sliceSpeed,
                turnMaxAttackSpeed = maxSpeed,
                insertedInstantAction = 0,
            };

            // 片前快照（瞬发效应读取状态）
            var snapshot = _sim.TakeSnapshot(turnNumber);

            var effects = new List<BattleEffect>();
            var movers = new List<MoveActionState>();
            var deadTargets = new List<Unit>();

            // 枚举序：unitId 升序（命令排列与写-写冲突合并依据；不影响结算结果）
            actions.Sort((a, b) => string.CompareOrdinal(a.unitId, b.unitId));
            foreach (var action in actions)
            {
                var unit = _sim.GetUnit(action.unitId);
                if (unit == null) continue;

                // 跨片击杀落空：片开始执行前被击杀 → 行动落空（同片互杀不受此影响）
                if (BattleSimState.IsDead(unit)) continue;
                if (!BattleSimState.CanAct(unit)) continue;

                switch (action.actionType)
                {
                    case ActionType.Move:
                        var mover = MoveExecutor.BuildMover(_sim, action);
                        if (mover != null) movers.Add(mover);
                        break;

                    case ActionType.Skill:
                        effects.AddRange(SkillExecutor.Resolve(_sim, action, snapshot));
                        break;

                    case ActionType.Pass:
                        effects.AddRange(PassExecutor.Resolve(_sim, action));
                        break;

                    default:
                        GICLog.Warn($"[TurnResolver] B1 未实现的行动类型 {action.actionType}，跳过");
                        break;
                }
            }

            // 移动同步逐步结算（动态展开；与快照结算并存）
            MovementResolver.Resolve(_sim, movers);

            // 效应统一应用（伤害/治疗合并 HP 天然成立）
            ApplyEffects(effects);

            // 死亡判定（效应应用后统一判；同片互杀 = 同归于尽）
            var damagedUnits = CollectDamagedTargets(effects);
            var newlyDead = _sim.ResolveDeaths(damagedUnits);
            deadTargets.AddRange(newlyDead);

            // 产出片命令块（枚举序）
            int indexInSlice = 0;
            foreach (var mover in movers)
            {
                if (mover.Path.Count > 1)
                    segment.commands.Add(BattleCommand.Move(mover.UnitId, sliceIndex, indexInSlice++, new List<BattleCell>(mover.Path)));
            }
            foreach (var effect in MergeDamageEffects(effects))
            {
                segment.commands.Add(BattleCommand.Damage(effect.AttackerUnitId, effect.TargetUnitId, sliceIndex, indexInSlice++, effect.Amount, effect.Element));
            }
            foreach (var dead in newlyDead)
            {
                if (_sim.TryGetUnitId(dead, out var deadId))
                    segment.commands.Add(BattleCommand.Death(deadId, sliceIndex, indexInSlice++));
            }

            GICLog.Info($"[TurnResolver] {segment}");
            return segment;
        }

        /// <summary>
        /// 即时行动独立结算（片边界插入；产出 insertedInstantAction 标记的独立块）
        /// </summary>
        private Segment ResolveInstantAction(int turnNumber, int sliceIndex, int maxSpeed, ActionData action)
        {
            var unit = _sim.GetUnit(action.unitId);
            if (unit == null || BattleSimState.IsDead(unit) || !BattleSimState.CanAct(unit))
            {
                GICLog.Warn($"[TurnResolver] 即时行动 {action.unitId} 不可执行，丢弃");
                return null;
            }

            var snapshot = _sim.TakeSnapshot(turnNumber);
            var effects = new List<BattleEffect>();
            MoveActionState mover = null;

            switch (action.actionType)
            {
                case ActionType.Skill:
                    effects.AddRange(SkillExecutor.Resolve(_sim, action, snapshot));
                    break;
                case ActionType.Move:
                    mover = MoveExecutor.BuildMover(_sim, action);
                    if (mover != null)
                    {
                        var movers = new List<MoveActionState> { mover };
                        MovementResolver.Resolve(_sim, movers);
                    }
                    break;
                case ActionType.Pass:
                    break;
                default:
                    GICLog.Warn($"[TurnResolver] 即时行动类型 {action.actionType} B1 不支持");
                    return null;
            }

            ApplyEffects(effects);
            var newlyDead = _sim.ResolveDeaths(CollectDamagedTargets(effects));

            var segment = new Segment
            {
                turnNumber = turnNumber,
                sliceIndex = sliceIndex,
                sliceAttackSpeed = unit.GetUnitComponent<UnitStats>()?.AttackSpeed ?? 0,
                turnMaxAttackSpeed = maxSpeed,
                insertedInstantAction = 1,
            };

            int indexInSlice = 0;
            if (mover != null && mover.Path.Count > 1)
                segment.commands.Add(BattleCommand.Move(mover.UnitId, sliceIndex, indexInSlice++, new List<BattleCell>(mover.Path)));
            foreach (var effect in MergeDamageEffects(effects))
                segment.commands.Add(BattleCommand.Damage(effect.AttackerUnitId, effect.TargetUnitId, sliceIndex, indexInSlice++, effect.Amount, effect.Element));
            foreach (var dead in newlyDead)
            {
                if (_sim.TryGetUnitId(dead, out var deadId))
                    segment.commands.Add(BattleCommand.Death(deadId, sliceIndex, indexInSlice++));
            }

            GICLog.Info($"[TurnResolver] 即时行动 {segment}");
            return segment;
        }

        // ==================== 分桶与应用 ====================

        /// <summary>
        /// 按行动单位攻速分桶（攻速完全相同才同片），桶按攻速降序
        /// </summary>
        private List<(int speed, List<ActionData> actions)> BucketByAttackSpeed(List<ActionData> actions)
        {
            var dict = new SortedDictionary<int, List<ActionData>>();
            foreach (var action in actions)
            {
                var unit = _sim.GetUnit(action.unitId);
                if (unit == null)
                {
                    GICLog.Warn($"[TurnResolver] 行动引用了不存在的单位 {action.unitId}，丢弃");
                    continue;
                }
                var stats = unit.GetUnitComponent<UnitStats>();
                int speed = stats?.AttackSpeed ?? 0;
                if (!dict.TryGetValue(speed, out var list))
                {
                    list = new List<ActionData>();
                    dict[speed] = list;
                }
                list.Add(action);
            }

            var buckets = new List<(int, List<ActionData>)>();
            // SortedDictionary 升序 → 倒序遍历得降序
            foreach (var speed in dict.Keys)
                buckets.Insert(0, (speed, dict[speed]));
            return buckets;
        }

        private void ApplyEffects(List<BattleEffect> effects)
        {
            foreach (var effect in effects)
            {
                var target = _sim.GetUnit(effect.TargetUnitId);
                if (target == null) continue;

                if (effect is DamageEffect damage)
                {
                    // 尸体 HP 恒 0，继续扣无意义但保持链路统一（属性保留）
                    _sim.ApplyDamage(target, damage.Amount);
                }
                else if (effect is HealEffect heal)
                {
                    _sim.ApplyHeal(target, heal.Amount);
                }
            }
        }

        private List<Unit> CollectDamagedTargets(List<BattleEffect> effects)
        {
            var targets = new List<Unit>();
            var seen = new HashSet<string>();
            foreach (var effect in effects)
            {
                if (effect is DamageEffect && effect.TargetUnitId != null && seen.Add(effect.TargetUnitId))
                {
                    var unit = _sim.GetUnit(effect.TargetUnitId);
                    if (unit != null)
                        targets.Add(unit);
                }
            }
            return targets;
        }

        /// <summary>
        /// 同片多伤害按 (攻击者,目标) 合并（命令粒度 = 一次原子视觉事件）
        /// </summary>
        private static List<DamageEffect> MergeDamageEffects(List<BattleEffect> effects)
        {
            var merged = new Dictionary<string, DamageEffect>();
            var result = new List<DamageEffect>();
            foreach (var effect in effects)
            {
                if (!(effect is DamageEffect damage)) continue;
                string key = $"{damage.AttackerUnitId}->{damage.TargetUnitId}";
                if (merged.TryGetValue(key, out var existing))
                {
                    existing.Amount += damage.Amount;
                }
                else
                {
                    var copy = new DamageEffect(damage.AttackerUnitId, damage.TargetUnitId, damage.Amount, damage.Element);
                    merged[key] = copy;
                    result.Add(copy);
                }
            }
            return result;
        }

        // ==================== 推送与 ack ====================

        private IEnumerator PushSegmentAndWaitAck(Segment segment)
        {
            _flow.NotifySegmentPushed(segment.turnNumber, segment.sliceIndex);
            _transport.HostSend(BattleMessageType.Segment, new SegmentMessage { segment = segment });

            // 等全体客户端 ack 或超时快进（本地单客户端）
            float elapsed = 0f;
            while (!_flow.HasSegmentAck(segment.turnNumber, segment.sliceIndex) && elapsed < AckTimeoutSeconds)
            {
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }
        }
    }
}
