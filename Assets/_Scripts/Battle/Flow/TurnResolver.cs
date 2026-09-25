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
            // 部署段（B6c）：出战行动先于全部攻速片结算（新单位当回合即被后续行动波及；
            // 占玩家行动配额由上交通道保证——DeployUnit 同走 SubmitAction 一玩家一行动，docs/18 决策七）
            var deploys = actions.FindAll(a => a.actionType == ActionType.DeployUnit);
            bool hasDeploySegment = deploys.Count > 0;
            if (hasDeploySegment)
            {
                actions.RemoveAll(a => a.actionType == ActionType.DeployUnit);
                yield return PushSegmentAndWaitAck(ResolveDeploySegment(turnNumber, 0, deploys));
            }

            // 玩家级 Pass（空 unitId：超时自动空过/AI 无单位 Pass）无单位归属——分桶前静默剔除，
            // 勿落进 BucketByAttackSpeed 的「行动引用了不存在的单位」Warn（2026-09-25 审查 S4）
            actions.RemoveAll(a => a.actionType == ActionType.Pass && string.IsNullOrEmpty(a.unitId));

            // 攻速分桶：攻速值完全相同才同片；片按攻速降序（先结算高攻速）
            var buckets = BucketByAttackSpeed(actions);
            int maxSpeed = 0;
            if (buckets.Count > 0)
                maxSpeed = buckets[0].speed;

            int sliceIndex = hasDeploySegment ? 1 : 0; // 部署段占 0 号（ack 键 turn:slice 唯一性）
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
            var casts = new List<BattleCommand>(); // 时轮施放事件（B-S1）

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
                        if (mover == null) break;
                        // 体力门槛（B6d）：移动=配额行动消耗 10 体力（低级单位豁免）；不足→移动落空
                        if (!StaminaGate.TryCharge(_sim, unit, action.playerId,
                            BattleMetrics.StaminaCostPerAction, "移动", effects))
                            break;
                        movers.Add(mover);
                        AddMoveCast(casts, unit, action, snapshot); // 时轮（B-S1b）：移动=特殊技能，同样产施放事件
                        break;

                    case ActionType.Skill:
                        effects.AddRange(SkillExecutor.Resolve(_sim, action, snapshot, casts));
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

            // 移动即获元能（B6a 拍板：移动使用 +10，被挡也算——移动行动已使用）
            foreach (var mover in movers)
                effects.Add(new EnergyEffect(mover.UnitId, BattleMetrics.EnergyGainPerMove, EnergyEffect.CategoryMoveGain));

            // 投射物连续命中判定（B5）：移动展开后按执行阶段时间轴模拟接触（读移动者完整路径，
            // docs/active/22 §11——命中点/消散点随命令千分定点下发）
            var vanishes = new List<BattleCommand>();
            ProjectileResolver.Resolve(_sim, snapshot, effects, movers, vanishes);

            // 效应统一应用（伤害/治疗合并 HP 天然成立；Buff 施加记入注册表）
            var appliedBuffs = ApplyEffects(effects);

            // 死亡判定（效应应用后统一判；同片互杀 = 同归于尽）
            var damagedUnits = CollectDamagedTargets(effects);
            var newlyDead = _sim.ResolveDeaths(damagedUnits);

            // 产出片命令块（三段唯一出口 EmitSliceCommands——命令发射序与对账见其内注释）
            EmitSliceCommands($"回合{turnNumber}片{sliceIndex}", segment, sliceIndex,
                effects, appliedBuffs, movers, vanishes, newlyDead, expired: null, skillCasts: casts);

            GICLog.Info($"[TurnResolver] {segment}");
            return segment;
        }

        /// <summary>
        /// 部署段（B6c）：回合开始先于攻速分桶结算全部出战行动（每玩家每回合最多 1 条，配额已保证）。
        /// 直产命令（Summon+StatChange），无效应链、无死亡判定；播放侧短节拍（deploy 标记段）。
        /// </summary>
        private Segment ResolveDeploySegment(int turnNumber, int sliceIndex, List<ActionData> deploys)
        {
            var segment = new Segment
            {
                turnNumber = turnNumber,
                sliceIndex = sliceIndex,
                sliceAttackSpeed = 0,
                turnMaxAttackSpeed = 0,
                deploy = 1,
            };

            int indexInSlice = 0;
            foreach (var action in deploys)
            {
                if (DeployUnitExecutor.TryResolve(_sim, action, out var summon, out var moraChange))
                {
                    if (summon != null)
                    {
                        summon.sliceIndex = sliceIndex;
                        summon.indexInSlice = indexInSlice++;
                        segment.commands.Add(summon);
                    }
                    if (moraChange != null)
                    {
                        moraChange.sliceIndex = sliceIndex;
                        moraChange.indexInSlice = indexInSlice++;
                        segment.commands.Add(moraChange);
                    }
                }
            }

            GICLog.Info($"[TurnResolver] 部署段 {segment}");
            return segment;
        }

        /// <summary>
        /// 回合结束段（B2 + B6d 经济闭环）：①每玩家发放 5摩拉+5体力（docs/04 §4.3——发放直产命令
        /// 不走效应链，资源变更非单位效应，同部署扣费先例；发放恒有内容，段必推送）→
        /// ②Buff 回合结束效果按注册序结算（docs/active/22 §2）→ ③计时减一（上 buff 当回合结束即减，
        /// docs/04 §4.4）→ 到期移除 → 产出 turnEnd 段。
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

            // B6d 经济闭环：回合结束发放（段首；indexInSlice 自此起算——EmitSliceCommands 接续 segment.commands.Count）
            int grantIndex = 0;
            foreach (var pid in _sim.PlayerIds)
            {
                _sim.ApplyMoraDelta(pid, BattleMetrics.MoraGainPerTurn);
                segment.commands.Add(BattleCommand.StatChange(pid, sliceIndex, grantIndex++,
                    BattleCommand.StatKindMora, BattleMetrics.MoraGainPerTurn));
                _sim.ApplyStaminaDelta(pid, BattleMetrics.StaminaGainPerTurn);
                segment.commands.Add(BattleCommand.StatChange(pid, sliceIndex, grantIndex++,
                    BattleCommand.StatKindStamina, BattleMetrics.StaminaGainPerTurn));
            }

            var effects = new List<BattleEffect>();

            // 按注册序结算回合结束效果，随后计时减一（到期收集）
            foreach (var buff in _sim.ActiveBuffs)
            {
                effects.AddRange(buff.OnTurnEnd());
                buff.RemainingTurns--;
            }
            var expired = _sim.CollectExpiredBuffs();

            var appliedBuffs = ApplyEffects(effects);
            var newlyDead = _sim.ResolveDeaths(CollectDamagedTargets(effects));

            // 产出回合结束段命令（三段唯一出口；到期 Buff 的 RemoveBuff 发射+注册表注销在出口尾部）
            EmitSliceCommands($"回合{turnNumber}结束段", segment, sliceIndex,
                effects, appliedBuffs, null, null, newlyDead, expired);

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
            var casts = new List<BattleCommand>(); // 时轮施放事件（B-S1）

            switch (action.actionType)
            {
                case ActionType.Skill:
                    effects.AddRange(SkillExecutor.Resolve(_sim, action, snapshot, casts));
                    break;
                case ActionType.Move:
                    var mover = MoveExecutor.BuildMover(_sim, action);
                    if (mover == null) break;
                    // 体力门槛（B6d）：移动=配额行动消耗 10 体力（低级单位豁免）；不足→移动落空
                    if (!StaminaGate.TryCharge(_sim, unit, action.playerId,
                        BattleMetrics.StaminaCostPerAction, "移动", effects))
                        break;
                    moverList.Add(mover);
                    MovementResolver.Resolve(_sim, moverList);

                    // 移动即获元能（B6a 拍板：移动使用 +10，被挡也算）
                    effects.Add(new EnergyEffect(mover.UnitId, BattleMetrics.EnergyGainPerMove, EnergyEffect.CategoryMoveGain));

                    AddMoveCast(casts, unit, action, snapshot); // 时轮（B-S1b）：移动=特殊技能，同样产施放事件
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

            // 产出即时段命令（三段唯一出口；单 mover 复用同一 Move 发射循环）
            EmitSliceCommands($"回合{turnNumber}即时段", segment, sliceIndex,
                effects, appliedBuffs, moverList, vanishes, newlyDead, expired: null, skillCasts: casts);

            GICLog.Info($"[TurnResolver] 即时行动 {segment}");
            return segment;
        }

        // ==================== 段命令发射（三段唯一出口，2026-09-23 审查 Y1 收口） ====================

        /// <summary>
        /// 段命令统一发射：片/即时段/回合结束段三处原为复制粘贴（曾致 turnEnd 段 Damage 漏带命中点/
        /// 反应标记的漂移）——收口后新效应→命令映射只加一处，BattleEffectCommandAudit 对账随发射统一覆盖三段。
        /// 发射序：SkillCast（时轮 B-S1）→ Move → Damage → 消散Effect → 附着 → 反应 → 元能 → 体力（B6d）
        /// → 治疗 → 施加Buff → 死亡 → 到期移除Buff。段首已有直产命令（回合结束段发放）时 indexInSlice 接续。
        /// 与旧回合结束段序的差异：治疗从 Damage 后移至元能后（客户端 stagger 约 +0.36s，纯视觉节拍）。
        /// </summary>
        private void EmitSliceCommands(string context, Segment segment, int sliceIndex,
            List<BattleEffect> effects, List<ApplyBuffEffect> appliedBuffs,
            List<MoveActionState> movers, List<BattleCommand> vanishes, List<Unit> newlyDead,
            List<BaseBuff> expired = null, List<BattleCommand> skillCasts = null)
        {
            int indexInSlice = segment.commands.Count;

            // 时轮施放事件（B-S1）：段内最前——客户端时轮演出起点（片播放起点=各 clip 的 t=0）
            if (skillCasts != null)
            {
                foreach (var cast in skillCasts)
                {
                    cast.sliceIndex = sliceIndex;
                    cast.indexInSlice = indexInSlice++;
                    segment.commands.Add(cast);
                }
            }

            // Move：被挡也发命令（全挡 path=[原格] / 部分挡 path=已走段），携带 MoveBlocked 标记+方向
            // 供客户端播"撞墙弹回"表现（2026-09-21）
            if (movers != null)
            {
                foreach (var mover in movers)
                {
                    if (mover.Path.Count > 1 || mover.Blocked)
                        segment.commands.Add(BattleCommand.Move(mover.UnitId, sliceIndex, indexInSlice++, new List<BattleCell>(mover.Path),
                            mover.Blocked ? BattleCommand.MoveBlocked : 0, mover.Blocked ? (int)mover.Direction : 0));
                }
            }

            foreach (var effect in MergeDamageEffects(effects))
            {
                segment.commands.Add(BattleCommand.Damage(effect.AttackerUnitId, effect.TargetUnitId, sliceIndex, indexInSlice++, effect.Amount,
                    effect.Element, effect.Delivery, effect.FromCell,
                    Mathf.RoundToInt(effect.HitPointX * 1000f), Mathf.RoundToInt(effect.HitPointY * 1000f), effect.ReactionType,
                    effect.LaunchMs));
            }

            if (vanishes != null)
            {
                foreach (var vanish in vanishes)
                {
                    vanish.sliceIndex = sliceIndex;
                    vanish.indexInSlice = indexInSlice++;
                    segment.commands.Add(vanish);
                }
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
            foreach (var effect in MergeEnergyEffects(effects))
            {
                var energyCommand = BattleCommand.StatChange(effect.TargetUnitId, sliceIndex, indexInSlice++,
                    BattleCommand.StatKindEnergy, effect.Delta);
                // 命中时刻（2026-09-25 拍板「命中时才给」）：战技获能命令带命中毫秒，客户端到点跳元能；
                // launchMs 字段复用为"应用时刻"（union 载荷；0=立即——移动获能/协奏/消耗/回合发放无命中时刻）
                if (effect.HitSeconds > 0f)
                    energyCommand.launchMs = Mathf.RoundToInt(effect.HitSeconds * 1000f);
                segment.commands.Add(energyCommand);
            }
            // 体力变化（B6d）：TargetUnitId=玩家 ID；同片同玩家防御性去重（配额行动唯一，
            // 理论只一条——低级单位豁免、延奏契约 0 消耗，正常流不会同玩家多条）
            var staminaSeen = new HashSet<string>();
            foreach (var effect in effects)
            {
                if (!(effect is StaminaEffect stamina)) continue;
                if (!staminaSeen.Add(stamina.TargetUnitId)) continue;
                segment.commands.Add(BattleCommand.StatChange(stamina.TargetUnitId, sliceIndex, indexInSlice++,
                    BattleCommand.StatKindStamina, stamina.Delta));
            }
            // 摩拉掠夺（B-3 首个资源类原子）：实际量双向 StatChange(Mora)——被掠夺方 −Gain / 掠夺方 +Gain
            //（AppliedGain=应用钳出的实际量，池空零动作零命令；命令按应用后实际值发，客户端增量与 Host 一致）。
            // 命中时刻（同元能「命中时才给」）：HitSeconds>0 时双命令带应用时刻；霜袭瞬发段恒 0=立即
            foreach (var effect in EnumerateEffects<MoraPlunderEffect>(effects))
            {
                if (effect.AppliedGain <= 0) continue;
                var takeCommand = BattleCommand.StatChange(effect.TargetUnitId, sliceIndex, indexInSlice++,
                    BattleCommand.StatKindMora, -effect.AppliedGain);
                var giveCommand = BattleCommand.StatChange(effect.ToPlayerId, sliceIndex, indexInSlice++,
                    BattleCommand.StatKindMora, effect.AppliedGain);
                if (effect.HitSeconds > 0f)
                {
                    int hitMs = Mathf.RoundToInt(effect.HitSeconds * 1000f);
                    takeCommand.launchMs = hitMs;
                    giveCommand.launchMs = hitMs;
                }
                segment.commands.Add(takeCommand);
                segment.commands.Add(giveCommand);
            }
            foreach (var effect in MergeHealEffects(effects))
            {
                var healCommand = BattleCommand.Heal(effect.SourceUnitId, effect.TargetUnitId, sliceIndex, indexInSlice++, effect.Amount);
                // 命中时刻（同元能「命中时才给」）：OnHit 治疗（水之浅唱）随投射物落地弹 +N；
                // 0=立即——OnCast 治疗（延奏/变奏）无飞行段
                if (effect.HitSeconds > 0f)
                    healCommand.launchMs = Mathf.RoundToInt(effect.HitSeconds * 1000f);
                segment.commands.Add(healCommand);
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

            // 到期 Buff：发射 RemoveBuff 后注销注册表（仅回合结束段传入）
            if (expired != null)
            {
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
            }

            BattleEffectCommandAudit.Assert(context, effects, appliedBuffs, segment.commands);
        }

        // ==================== 分桶与应用 ====================

        /// <summary>
        /// 移动行动的时轮施放事件（B-S1b：「移动是特殊的技能」——与技能行动同产 SkillCast；
        /// skillId=移动技能条目（skills[0]·Move 型）的枚举值，客户端表现轨同链）
        /// </summary>
        private static void AddMoveCast(List<BattleCommand> casts, Unit unit, ActionData action, BattleSnapshot snapshot)
        {
            var skills = unit?.Skills;
            if (skills == null) return;
            for (int i = 0; i < skills.Count; i++)
            {
                if (skills[i]?.RawData?.skillType != SkillType.Move) continue;
                var casterState = SkillHitResolver.FindUnitState(snapshot, action.unitId);
                casts.Add(BattleCommand.SkillCast(action.unitId, 0, 0,
                    (int)skills[i].RawData.skillID, (int)action.direction,
                    casterState != null ? casterState.position : BattleCell.zero));
                return;
            }
        }

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
        /// 效应统一应用（伤害/治疗/Buff 施加）；返回已施 Buff 列表（含合并后的级别与剩余回合，供命令产出）。
        /// 元能两段应用（2026-09-25 用户拍板「先扣除，再加」）：同段多来源元能效应先消耗后获取——
        /// 获取先应用会被 baseEnergy 上限钳位吞掉（30+10→40 钳 30，再 −20=10 ≠ 期望 30−20+10=20）。
        /// </summary>
        private List<ApplyBuffEffect> ApplyEffects(List<BattleEffect> effects)
        {
            var appliedBuffs = new List<ApplyBuffEffect>();
            List<EnergyEffect> energyCosts = null, energyGains = null;
            foreach (var effect in effects)
            {
                // 体力效应（B6d）：TargetUnitId=玩家 ID 非 Unit——先于单位解析处理（GetUnit(玩家ID)=null 会被跳过）
                if (effect is StaminaEffect stamina)
                {
                    _sim.ApplyStaminaDelta(stamina.TargetUnitId, stamina.Delta);
                    continue;
                }

                // 摩拉掠夺（B-3 首个资源类原子）：玩家池转移——实际量=min(掠夺量, 被掠夺方池)，
                // 池空抢不到（AppliedGain=0 零命令）；双向池写（含手牌货币条目同步）。同片多命中
                // （霜袭打 2 敌）按序结算=各抢各的，总量=每次命中各 5
                if (effect is MoraPlunderEffect plunder)
                {
                    int gain = Mathf.Min(plunder.Amount, _sim.GetMora(plunder.TargetUnitId));
                    plunder.AppliedGain = gain;
                    if (gain > 0)
                    {
                        _sim.ApplyMoraDelta(plunder.TargetUnitId, -gain);
                        _sim.ApplyMoraDelta(plunder.ToPlayerId, gain);
                    }
                    continue;
                }

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
                else if (effect is EnergyEffect energy)
                {
                    if (energy.Delta < 0) (energyCosts ??= new List<EnergyEffect>()).Add(energy);
                    else (energyGains ??= new List<EnergyEffect>()).Add(energy);
                }
                else if (effect is ApplyBuffEffect applyBuff)
                {
                    // 带参数通道的 Buff（B-S1b：AttackUp 等——value/stackLimit/turns 全由技能参数单源注入）
                    BaseBuff buff = applyBuff.DurationTurns > 0
                        ? BuffFactory.Create((BuffType)applyBuff.BuffType, applyBuff.Level,
                            _sim.GetUnit(applyBuff.SourceUnitId), applyBuff.BuffValue, applyBuff.StackLimit,
                            applyBuff.DurationTurns)
                        : BuffFactory.Create((BuffType)applyBuff.BuffType, applyBuff.Level,
                            _sim.GetUnit(applyBuff.SourceUnitId), applyBuff.BuffValue);
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

            // 元能第二阶段：先扣除后获取（与命令发射序 MergeEnergyEffects 同语义，2026-09-25 用户拍板「先扣除，再加」
            // ——获取先到会被 baseEnergy 上限钳位吞掉：30+10 钳 30 再 −20=10 ≠ 期望 20）。
            // (目标,类别) 去重=与 MergeEnergyEffects 命令合并口径恒等（2026-09-25 修复：逐条累加致状态背离命令——
            // 两发箭矢两条 +10 战技获能曾会状态 +20/命令 +10，B6a「多次命中只获一次」由此在状态层真正成立）
            if (energyCosts != null || energyGains != null)
            {
                var appliedEnergy = new HashSet<string>();
                if (energyCosts != null)
                    foreach (var cost in energyCosts)
                    {
                        if (!appliedEnergy.Add($"{cost.TargetUnitId}:{cost.Category}")) continue;
                        _sim.ApplyEnergy(_sim.GetUnit(cost.TargetUnitId), cost.Delta);
                    }
                if (energyGains != null)
                    foreach (var gain in energyGains)
                    {
                        if (!appliedEnergy.Add($"{gain.TargetUnitId}:{gain.Category}")) continue;
                        _sim.ApplyEnergy(_sim.GetUnit(gain.TargetUnitId), gain.Delta);
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
        /// 同片多伤害按 (攻击者,目标,发射时刻) 合并（命令粒度 = 一次原子视觉事件）。
        /// 时轮 B-S1：合并键含 LaunchMs——逐发（发射时刻不同）不并，各产独立 Damage 命令
        /// （两发箭矢=两个伤害数字分时弹出）；同发内同目标多来源仍合并。
        /// </summary>
        private static List<DamageEffect> MergeDamageEffects(List<BattleEffect> effects)
        {
            var merged = new Dictionary<string, DamageEffect>();
            var result = new List<DamageEffect>();
            foreach (var effect in effects)
            {
                if (!(effect is DamageEffect damage)) continue;
                string key = $"{damage.AttackerUnitId}->{damage.TargetUnitId}:{damage.LaunchMs}";
                if (merged.TryGetValue(key, out var existing))
                {
                    existing.Amount += damage.Amount;
                }
                else
                {
                    // 命中点与反应标记取首条（同合并键合并时=最早一次接触的位置与反应）
                    var copy = new DamageEffect(damage.AttackerUnitId, damage.TargetUnitId, damage.Amount,
                        damage.Element, damage.Delivery, damage.FromCell, damage.HitPointX, damage.HitPointY,
                        damage.ReactionType, damage.LaunchMs);
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
                    // 命中时刻随合并副本保留（首条命中时刻=最早命中，2026-09-25「命中时才给」）
                    var copy = new HealEffect(heal.SourceUnitId, heal.TargetUnitId, heal.Amount)
                    {
                        HitSeconds = heal.HitSeconds,
                    };
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

        /// <summary>同片元能效应合并（2026-09-25 审查 R1 修复）：合并键=目标+来源类别——
        /// 同类别同目标取首条（B6a 拍板"战技多命中只获一次"的去重口径），跨类别各发一条命令互不吞
        /// （协奏获能与目标同片自身移动获能、施法者消耗并存时全部下发；此前键只含目标、取首条
        /// 会吞掉消耗/协奏命令致客户端元能显示背离，下回合快照才自愈）。
        /// 发射序=先消耗后获取（与 ApplyEffects 元能两段应用同语义，2026-09-25 用户拍板「先扣除，再加」
        /// ——获取先到会被 baseEnergy 上限钳位吞掉：30+10 钳 30 再 −20=10 ≠ 期望 20）。
        /// 类别内跨行动同目标（如双延奏者同片协奏同一目标）当前角色池不可能出现，出现时再细分行动源。</summary>
        private static List<EnergyEffect> MergeEnergyEffects(List<BattleEffect> effects)
        {
            var seen = new HashSet<string>();
            var costs = new List<EnergyEffect>();
            var gains = new List<EnergyEffect>();
            foreach (var effect in effects)
            {
                if (!(effect is EnergyEffect energy)) continue;
                if (!seen.Add($"{energy.TargetUnitId}:{energy.Category}")) continue;
                if (energy.Delta < 0) costs.Add(energy);
                else gains.Add(energy);
            }
            costs.AddRange(gains);
            return costs;
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
