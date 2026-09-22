using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 执行阶段片循环（docs/active/22 §2 分步流式演算）：
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

            // 回合结束效果按注册序（B2 = Buff 回合制计时 + DoT；产出独立 turnEnd 段）
            var turnEndSegment = ResolveTurnEnd(turnNumber, sliceIndex, maxSpeed);
            if (turnEndSegment != null)
            {
                yield return PushSegmentAndWaitAck(turnEndSegment);
                sliceIndex++;
            }

            // WaitConditions 扫描（B1 无）
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

            // 投射物连续命中判定（B5）：移动展开后按执行阶段时间轴模拟接触（读移动者完整路径，
            // docs/active/22 §11——命中点/消散点随命令千分定点下发）
            var vanishes = new List<BattleCommand>();
            ProjectileResolver.Resolve(_sim, snapshot, effects, movers, vanishes);

            // 效应统一应用（伤害/治疗合并 HP 天然成立；Buff 施加记入注册表）
            var appliedBuffs = ApplyEffects(effects);

            // 死亡判定（效应应用后统一判；同片互杀 = 同归于尽）
            var damagedUnits = CollectDamagedTargets(effects);
            var newlyDead = _sim.ResolveDeaths(damagedUnits);
            deadTargets.AddRange(newlyDead);

            // 产出片命令块（枚举序）；被挡也发命令（全挡 path=[原格] / 部分挡 path=已走段），
            // 携带 MoveBlocked 标记+方向供客户端播"撞墙弹回"表现
            int indexInSlice = 0;
            foreach (var mover in movers)
            {
                if (mover.Path.Count > 1 || mover.Blocked)
                    segment.commands.Add(BattleCommand.Move(mover.UnitId, sliceIndex, indexInSlice++, new List<BattleCell>(mover.Path),
                        mover.Blocked ? BattleCommand.MoveBlocked : 0, mover.Blocked ? (int)mover.Direction : 0));
            }
            foreach (var effect in MergeDamageEffects(effects))
            {
                segment.commands.Add(BattleCommand.Damage(effect.AttackerUnitId, effect.TargetUnitId, sliceIndex, indexInSlice++, effect.Amount, effect.Element, effect.Delivery, effect.FromCell,
                    Mathf.RoundToInt(effect.HitPointX * 1000f), Mathf.RoundToInt(effect.HitPointY * 1000f), effect.ReactionType));
            }
            foreach (var vanish in vanishes)
            {
                vanish.sliceIndex = sliceIndex;
                vanish.indexInSlice = indexInSlice++;
                segment.commands.Add(vanish);
            }
            foreach (var effect in EnumerateEffects<AttachElementEffect>(effects))
            {
                segment.commands.Add(BattleCommand.ElementAttach(effect.SourceUnitId, effect.TargetUnitId,
                    sliceIndex, indexInSlice++, effect.Element));
            }
            foreach (var effect in EnumerateEffects<ReactionEffect>(effects))
            {
                segment.commands.Add(BattleCommand.Reaction(effect.SourceUnitId, effect.TargetUnitId,
                    sliceIndex, indexInSlice++, effect.ReactionType, effect.Level));
            }
            foreach (var effect in MergeHealEffects(effects))
            {
                segment.commands.Add(BattleCommand.Heal(effect.SourceUnitId, effect.TargetUnitId, sliceIndex, indexInSlice++, effect.Amount));
            }
            foreach (var applied in MergeAppliedBuffs(appliedBuffs))
            {
                segment.commands.Add(BattleCommand.ApplyBuff(applied.SourceUnitId, applied.TargetUnitId,
                    sliceIndex, indexInSlice++, applied.BuffType, applied.Level, applied.Turns));
            }
            foreach (var dead in newlyDead)
            {
                if (_sim.TryGetUnitId(dead, out var deadId))
                    segment.commands.Add(BattleCommand.Death(deadId, sliceIndex, indexInSlice++));
            }

            BattleEffectCommandAudit.Assert($"回合{turnNumber}片{sliceIndex}", effects, appliedBuffs, segment.commands);

            GICLog.Info($"[TurnResolver] {segment}");
            return segment;
        }

        /// <summary>
        /// 回合结束段（B2）：Buff 回合结束效果按注册序结算（docs/active/22 §2）→
        /// 计时减一（上 buff 当回合结束即减，docs/04 §4.4）→ 到期移除 → 产出 turnEnd 段。
        /// 无 Buff 无效果时返回 null（不推送空段）。
        /// </summary>
        private Segment ResolveTurnEnd(int turnNumber, int sliceIndex, int maxSpeed)
        {
            var segment = new Segment
            {
                turnNumber = turnNumber,
                sliceIndex = sliceIndex,
                sliceAttackSpeed = maxSpeed,
                turnMaxAttackSpeed = maxSpeed,
                insertedInstantAction = 0,
                turnEnd = 1,
            };

            var effects = new List<BattleEffect>();

            // 按注册序结算回合结束效果，随后计时减一（到期收集）
            foreach (var buff in _sim.ActiveBuffs)
            {
                effects.AddRange(buff.OnTurnEnd());
                buff.RemainingTurns--;
            }
            var expired = _sim.CollectExpiredBuffs();

            if (effects.Count == 0 && expired.Count == 0)
                return null; // 本回合无任何回合结束内容：不推送空段

            var appliedBuffs = ApplyEffects(effects);
            var newlyDead = _sim.ResolveDeaths(CollectDamagedTargets(effects));

            int indexInSlice = 0;
            foreach (var effect in MergeDamageEffects(effects))
            {
                segment.commands.Add(BattleCommand.Damage(effect.AttackerUnitId, effect.TargetUnitId, sliceIndex, indexInSlice++, effect.Amount, effect.Element, effect.Delivery, effect.FromCell));
            }
            foreach (var effect in effects)
            {
                if (effect is HealEffect heal)
                    segment.commands.Add(BattleCommand.Heal(heal.SourceUnitId, heal.TargetUnitId, sliceIndex, indexInSlice++, heal.Amount));
            }
            // 回合结束段同构产出（2026-09-22 接线；当前 Buff.OnTurnEnd 只产伤害，防御性补齐——
            // 未来"回合结束获得 Buff/附着"类效应不漏发命令，对账机制统一覆盖三段）
            foreach (var effect in EnumerateEffects<AttachElementEffect>(effects))
            {
                segment.commands.Add(BattleCommand.ElementAttach(effect.SourceUnitId, effect.TargetUnitId,
                    sliceIndex, indexInSlice++, effect.Element));
            }
            foreach (var effect in EnumerateEffects<ReactionEffect>(effects))
            {
                segment.commands.Add(BattleCommand.Reaction(effect.SourceUnitId, effect.TargetUnitId,
                    sliceIndex, indexInSlice++, effect.ReactionType, effect.Level));
            }
            foreach (var applied in MergeAppliedBuffs(appliedBuffs))
            {
                segment.commands.Add(BattleCommand.ApplyBuff(applied.SourceUnitId, applied.TargetUnitId,
                    sliceIndex, indexInSlice++, applied.BuffType, applied.Level, applied.Turns));
            }
            foreach (var dead in newlyDead)
            {
                if (_sim.TryGetUnitId(dead, out var deadId))
                    segment.commands.Add(BattleCommand.Death(deadId, sliceIndex, indexInSlice++));
            }
            foreach (var buff in expired)
            {
                if (_sim.TryGetUnitId(buff.owner, out var targetId))
                {
                    string sourceId = targetId;
                    if (buff.source != null && _sim.TryGetUnitId(buff.source, out var sid))
                        sourceId = sid;
                    segment.commands.Add(BattleCommand.RemoveBuff(sourceId, targetId, sliceIndex, indexInSlice++, (int)buff.Type));
                }
                _sim.RemoveBuff(buff.owner, buff);
            }

            BattleEffectCommandAudit.Assert($"回合{turnNumber}结束段", effects, appliedBuffs, segment.commands);

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
            var moverList = new List<MoveActionState>();

            switch (action.actionType)
            {
                case ActionType.Skill:
                    effects.AddRange(SkillExecutor.Resolve(_sim, action, snapshot));
                    break;
                case ActionType.Move:
                    var mover = MoveExecutor.BuildMover(_sim, action);
                    if (mover != null)
                    {
                        moverList.Add(mover);
                        MovementResolver.Resolve(_sim, moverList);
                    }
                    break;
                case ActionType.Pass:
                    break;
                default:
                    GICLog.Warn($"[TurnResolver] 即时行动类型 {action.actionType} B1 不支持");
                    return null;
            }

            // 投射物连续命中判定（即时行动段无并发移动者，连续判定退化为静止接触）
            var vanishes = new List<BattleCommand>();
            ProjectileResolver.Resolve(_sim, snapshot, effects, moverList, vanishes);

            var appliedBuffs = ApplyEffects(effects);
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
            if (moverList.Count > 0 && (moverList[0].Path.Count > 1 || moverList[0].Blocked))
                segment.commands.Add(BattleCommand.Move(moverList[0].UnitId, sliceIndex, indexInSlice++, new List<BattleCell>(moverList[0].Path),
                    moverList[0].Blocked ? BattleCommand.MoveBlocked : 0, moverList[0].Blocked ? (int)moverList[0].Direction : 0));
            foreach (var effect in MergeDamageEffects(effects))
                segment.commands.Add(BattleCommand.Damage(effect.AttackerUnitId, effect.TargetUnitId, sliceIndex, indexInSlice++, effect.Amount, effect.Element, effect.Delivery, effect.FromCell,
                    Mathf.RoundToInt(effect.HitPointX * 1000f), Mathf.RoundToInt(effect.HitPointY * 1000f), effect.ReactionType));
            foreach (var vanish in vanishes)
            {
                vanish.sliceIndex = sliceIndex;
                vanish.indexInSlice = indexInSlice++;
                segment.commands.Add(vanish);
            }
            foreach (var effect in EnumerateEffects<AttachElementEffect>(effects))
                segment.commands.Add(BattleCommand.ElementAttach(effect.SourceUnitId, effect.TargetUnitId,
                    sliceIndex, indexInSlice++, effect.Element));
            foreach (var effect in EnumerateEffects<ReactionEffect>(effects))
                segment.commands.Add(BattleCommand.Reaction(effect.SourceUnitId, effect.TargetUnitId,
                    sliceIndex, indexInSlice++, effect.ReactionType, effect.Level));
            foreach (var effect in MergeHealEffects(effects))
                segment.commands.Add(BattleCommand.Heal(effect.SourceUnitId, effect.TargetUnitId, sliceIndex, indexInSlice++, effect.Amount));
            foreach (var applied in MergeAppliedBuffs(appliedBuffs))
                segment.commands.Add(BattleCommand.ApplyBuff(applied.SourceUnitId, applied.TargetUnitId,
                    sliceIndex, indexInSlice++, applied.BuffType, applied.Level, applied.Turns));
            foreach (var dead in newlyDead)
            {
                if (_sim.TryGetUnitId(dead, out var deadId))
                    segment.commands.Add(BattleCommand.Death(deadId, sliceIndex, indexInSlice++));
            }

            BattleEffectCommandAudit.Assert($"回合{turnNumber}即时段", effects, appliedBuffs, segment.commands);

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

        /// <summary>
        /// 效应统一应用（伤害/治疗/Buff 施加）；返回已施 Buff 列表（含合并后的级别与剩余回合，供命令产出）
        /// </summary>
        private List<ApplyBuffEffect> ApplyEffects(List<BattleEffect> effects)
        {
            var appliedBuffs = new List<ApplyBuffEffect>();
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
                else if (effect is ApplyBuffEffect applyBuff)
                {
                    var buff = BuffFactory.Create((BuffType)applyBuff.BuffType, applyBuff.Level, _sim.GetUnit(applyBuff.SourceUnitId));
                    if (buff == null) continue;
                    _sim.ApplyBuff(target, buff, _sim.GetUnit(applyBuff.SourceUnitId));

                    // 回填合并后的真实状态（同类叠加时 Level/Turns 以注册表为准）
                    var state = target.Buffs.Find(b => b.Type == (BuffType)applyBuff.BuffType);
                    applyBuff.Level = state != null ? state.Level : applyBuff.Level;
                    applyBuff.Turns = state != null ? state.RemainingTurns : 0;
                    appliedBuffs.Add(applyBuff);
                }
                else if (effect is AttachElementEffect attach)
                {
                    _sim.AttachElement(target, (ElementType)attach.Element); // 覆盖=消耗被反应附着（docs/06）
                }
            }
            return appliedBuffs;
        }

        /// <summary>同片同 (目标,类型) 的多次施加合并为一条命令（回合数以最终合并态为准）</summary>
        private static List<ApplyBuffEffect> MergeAppliedBuffs(List<ApplyBuffEffect> applied)
        {
            var merged = new Dictionary<string, ApplyBuffEffect>();
            var result = new List<ApplyBuffEffect>();
            foreach (var buff in applied)
            {
                string key = $"{buff.TargetUnitId}:{buff.BuffType}";
                if (!merged.ContainsKey(key))
                {
                    merged[key] = buff;
                    result.Add(buff);
                }
                else
                {
                    merged[key].Level = Mathf.Max(merged[key].Level, buff.Level);
                    merged[key].Turns = buff.Turns; // 后施合并态覆盖
                }
            }
            return result;
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
                    // 命中点与反应标记取首条（同片同 (攻击者,目标) 合并时=最早一次接触的位置与反应）
                    var copy = new DamageEffect(damage.AttackerUnitId, damage.TargetUnitId, damage.Amount,
                        damage.Element, damage.Delivery, damage.FromCell, damage.HitPointX, damage.HitPointY,
                        damage.ReactionType);
                    merged[key] = copy;
                    result.Add(copy);
                }
            }
            return result;
        }

        /// <summary>
        /// 同片同 (来源,目标) 的多次治疗合并为一条命令（docs/active/22 §7.4：同片多伤害/治疗数值合并；
        /// 片内/即时段治疗命令发射——此前仅回合结束段发射，片内治疗对客户端不可见致双端血量背离）
        /// </summary>
        private static List<HealEffect> MergeHealEffects(List<BattleEffect> effects)
        {
            var merged = new Dictionary<string, HealEffect>();
            var result = new List<HealEffect>();
            foreach (var effect in effects)
            {
                if (!(effect is HealEffect heal)) continue;
                string key = $"{heal.SourceUnitId}->{heal.TargetUnitId}";
                if (merged.TryGetValue(key, out var existing))
                {
                    existing.Amount += heal.Amount;
                }
                else
                {
                    var copy = new HealEffect(heal.SourceUnitId, heal.TargetUnitId, heal.Amount);
                    merged[key] = copy;
                    result.Add(copy);
                }
            }
            return result;
        }

        /// <summary>按类型枚举效应（附着/反应一一对应产出命令，覆盖语义后到者胜——与合并策略不同属预期）</summary>
        private static IEnumerable<T> EnumerateEffects<T>(List<BattleEffect> effects) where T : BattleEffect
        {
            foreach (var effect in effects)
                if (effect is T typed)
                    yield return typed;
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
