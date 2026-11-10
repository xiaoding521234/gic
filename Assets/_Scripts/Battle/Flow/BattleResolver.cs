using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 即时制演算核心（决策九十五，docs/active/48 §2.5——原执行阶段片循环 tick 化）：
    /// 单行动/批行动/每秒整拍三类入口 → 片内快照结算（瞬发读片前快照）→ 移动同步逐步结算
    /// → 效应统一应用 → 产出段命令块 → 即时推送（ack 门控随回合机退役，命令带 startTick）。
    ///
    /// 驱动方=RTSFlowController tick 循环（20Hz accumulator 累加器）；攻速分桶/行动预告/
    /// Segment+ack 门控/片边界即时队列已退役（清单见 docs/active/48 §2.4，勿再当 bug 修）。
    /// </summary>
    public class BattleResolver
    {
        private readonly BattleSimState _sim;
        private readonly IBattleTransport _transport;
        private readonly IBattleResolverHost _flow;

        /// <summary>建筑效果系统（2026-10-26 fix-batch2 ③收口：内联建筑段迁出——效果拍效果收集/
        /// 元能引擎过阈收集与发射全走本实例；每局随 resolver 创建，无静态跨局状态）</summary>
        private readonly BuildingEffectsSystem _buildingEffects;

        public BattleResolver(BattleSimState sim, IBattleTransport transport, IBattleResolverHost flow)
        {
            _sim = sim;
            _transport = transport;
            _flow = flow;
            _buildingEffects = new BuildingEffectsSystem(sim);
        }

        // ==================== 演算入口（即时制，决策九十五 docs/active/48 §2.5） ====================
        // 回合主循环/攻速分桶/行动预告/Segment+ack 门控/片边界即时队列随回合机退役（新仓拆既定清单，
        // 勿再当 bug 修）——驱动方=RTSFlowController tick 循环；本类只提供三类演算入口：
        // ①玩家/配额脑单行动即时结算；②眷属+伙伴脑批演算（0.5s 节拍）；③每秒整拍结算（回合末族秒化）。

        /// <summary>段序计数器（每段自增——ack 键语义退役后仅作段唯一序/客户端对齐）</summary>
        private int _sliceCounter;

        /// <summary>
        /// 玩家/配额脑单行动即时结算（RTSFlowController.OnSubmitAction 校验通过后调）：
        /// DeployUnit→部署段；UseItem/EquipItem→物品段；Move/Skill/Pass→单行动片。
        /// 每段独立即时推送（即时制无攻速排序——行动上交即结算，蓝图 §2.4）。
        /// 胜负检查归调用方（RTSFlow 每段后自检）。
        /// </summary>
        public void ResolveSubmittedAction(ActionData action)
        {
            if (action == null) return;
            int slice = _sliceCounter++;
            Segment segment;
            switch (action.actionType)
            {
                case ActionType.DeployUnit:
                    segment = ResolveDeploySegment(action.turnNumber, slice, new List<ActionData> { action });
                    break;
                case ActionType.UseItem:
                case ActionType.EquipItem:
                    segment = ResolveItemActionsSegment(action.turnNumber, slice, new List<ActionData> { action });
                    break;
                case ActionType.Move:
                case ActionType.Skill:
                case ActionType.Pass:
                    var unit = _sim.GetUnit(action.unitId);
                    int speed = unit?.GetUnitComponent<UnitStats>()?.AttackSpeed ?? 0;
                    segment = ResolveSlice(action.turnNumber, slice, speed, speed, new List<ActionData> { action });
                    break;
                default:
                    GICLog.Warn($"[BattleResolver] 即时制不支持行动类型 {action.actionType}，丢弃");
                    return;
            }
            if (segment != null && segment.commands.Count > 0)
                PushSegment(segment);
        }

        /// <summary>
        /// 眷属+伙伴脑批演算（RTSFlow 每 0.5 秒 AI 节拍调）：整批同段结算（同拍=同片=同片前快照，
        /// 沿用旧同片语义）——即时制无攻速排序，行动受攻击 CD/元能/体力自然节流（蓝图 §6）。
        /// P5 已移植：三脑连续空间化（任意角瞄准 aimX/aimZ+目标点移动 targetX/targetZ 直交，
        /// A*+steering 交 RTSMovementSystem——旧「Brain 格寻路/十字方向域 P1 保持不动」注记就此销案）。
        /// </summary>
        public void ResolveAiActions(List<ActionData> actions)
        {
            if (actions == null || actions.Count == 0) return;
            var valid = new List<ActionData>();
            foreach (var action in actions)
            {
                if (action == null) continue;
                var unit = _sim.GetUnit(action.unitId);
                if (unit == null) continue;
                if (BattleSimState.IsDead(unit) || !BattleSimState.CanAct(unit)) continue; // 跨拍击杀落空
                valid.Add(action);
            }
            if (valid.Count == 0) return;
            int slice = _sliceCounter++;
            var segment = ResolveSlice(valid[0].turnNumber, slice, 0, 0, valid);
            if (segment != null && segment.commands.Count > 0)
                PushSegment(segment);
        }

        /// <summary>
        /// tick 移动收尾段：只处理到达/被挡的位置校正。普通读条与安柏移动快速采集统一在
        /// ResolveHarvestTick 按逐 tick 实际位置结算，不再于移动结束时按 PathCells 补算。
        /// 纯到达（无被挡校正）=零命令零段。
        /// </summary>
        public void ResolveMoveArrivals(List<RTSMoveState> arrivals)
        {
            if (arrivals == null || arrivals.Count == 0) return;
            var effects = new List<BattleEffect>();
            var corrects = new List<BattleCommand>(); // P4-E 被挡位置校正命令（空 path+hitX/hitY=停点 cm）
            foreach (var mover in arrivals)
            {
                if (mover.Blocked)
                {
                    // P4-E 移动实时化：被挡=停于接触前（连续 cm 位非格心）——恒发位置校正命令
                    //（客户端停活动补间+落位+撞墙弹回表现；纯到达=无命令——补间终点=目标格心天然对齐）
                    var stopCm = _sim.GetPosCm(mover.Unit);
                    var correct = BattleCommand.Move(mover.UnitId, 0, 0, new List<BattleCell>(),
                        BattleCommand.MoveBlocked, (int)mover.BlockedDirection);
                    correct.hitX = stopCm.x;
                    correct.hitY = stopCm.y;
                    corrects.Add(correct);
                }
            }
            if (corrects.Count == 0 && effects.Count == 0) return;

            int slice = _sliceCounter++;
            var segment = new Segment
            {
                turnNumber = _sim.CurrentSecond,
                sliceIndex = slice,
                sliceAttackSpeed = 0,
                turnMaxAttackSpeed = 0,
                insertedInstantAction = 0,
                startTick = _sim.CurrentTick,
            };
            var shatters = new List<(string holderId, string sourceId, BaseBuff buff)>();
            var appliedBuffs = new List<ApplyBuffEffect>();
            var newlyDead = new List<Unit>(); // EmitSliceCommands 无空守卫遍历（881 行）——被挡纯校正段=空表
            if (effects.Count > 0)
            {
                appliedBuffs = ApplyEffects(effects, shatters);
                newlyDead = _sim.ResolveDeaths(CollectDamagedTargets(effects));
            }
            foreach (var correct in corrects) // P4-E 段首直产（EmitSliceCommands 的 indexInSlice 接续既定语义）
            {
                correct.sliceIndex = slice;
                correct.indexInSlice = segment.commands.Count;
                segment.commands.Add(correct);
            }
            EmitSliceCommands($"tick{_sim.CurrentTick}移动收尾", segment, slice,
                effects, appliedBuffs, null, newlyDead, expired: null, skillCasts: null,
                shattered: shatters);
            if (segment.commands.Count > 0) PushSegment(segment);
        }

        /// <summary>逐 tick 推进普通读条与移动快速采集。普通受击不进入此中断链；主动行动由执行器显式取消。</summary>
        public void ResolveHarvestTick()
        {
            var effects = new List<BattleEffect>();
            var startedCommands = new List<BattleCommand>();
            var nodes = _sim.SortedResourceNodes();
            AppendAmberKeenSensesEffects(effects);

            foreach (var kv in _sim.SortedUnits())
            {
                string unitId = kv.Key;
                var unit = kv.Value;
                if (unit == null) continue;
                var status = unit.GetUnitComponent<UnitStatus>();
                bool incapacitated = BattleSimState.IsDead(unit)
                                     || (status != null && (status.IsHardControlled || status.IsSilenced));
                if (incapacitated)
                {
                    _sim.CancelHarvestChannel(unitId);
                    continue;
                }

                bool moving = _sim.IsUnitMoving(unitId);
                if (moving)
                {
                    _sim.CancelHarvestChannel(unitId);
                    var collect = MoveExecutor.CollectParamsOf(unit);
                    if (collect.count <= 0) continue;
                    var quickNode = FindHarvestNodeInRange(unit, nodes, collect.radiusCm);
                    if (quickNode == null
                        || _sim.QuickHarvestReadyTick(unitId, quickNode.cell) > _sim.CurrentTick) continue;
                    var identity = unit.GetUnitComponent<UnitIdentity>();
                    if (identity == null) continue;

                    int completed = 0;
                    for (int i = 0; i < collect.count; i++)
                    {
                        int remaining = _sim.TryHarvestResourceNode(quickNode.cell);
                        if (remaining < 0) break;
                        AppendHarvestEffects(effects, unitId, identity.OwnerPlayerID, quickNode.cell, remaining,
                            collect.staminaGain, collect.energyGain);
                        unit.GetUnitComponent<UnitEquipment>()?.OnCollect();
                        completed++;
                    }
                    if (completed > 0)
                        _sim.SetQuickHarvestReadyTick(unitId, quickNode.cell,
                            _sim.CurrentTick + Math.Max(0, collect.cooldownSeconds) * BattleMetrics.TicksPerSecond);
                    continue;
                }

                var node = FindHarvestNodeInRange(unit, nodes, ResourceNodeMetrics.HarvestRadiusCm);
                if (node == null)
                {
                    _sim.CancelHarvestChannel(unitId);
                    continue;
                }

                int efficiency = Math.Max(1, unit.GetUnitComponent<UnitStats>()?.HarvestEfficiency ?? 100);
                int baseTicks = node.EffectiveHarvestSeconds * BattleMetrics.TicksPerSecond;
                int durationTicks = (baseTicks * 100 + efficiency - 1) / efficiency;
                var channel = _sim.EnsureHarvestChannel(unitId, node.cell, _sim.CurrentTick,
                    durationTicks, out bool started);
                if (started)
                    startedCommands.Add(BattleCommand.HarvestChannel(BattleCommandType.HarvestChannelStart,
                        unitId, channel.nodeCell, channel.startTick, channel.finishTick));
                if (_sim.CurrentTick < channel.finishTick) continue;

                _sim.CompleteHarvestChannel(unitId);
                int remainingAfterChannel = _sim.TryHarvestResourceNode(node.cell);
                if (remainingAfterChannel < 0) continue;
                var channelIdentity = unit.GetUnitComponent<UnitIdentity>();
                if (channelIdentity == null) continue;
                AppendHarvestEffects(effects, unitId, channelIdentity.OwnerPlayerID, node.cell,
                    remainingAfterChannel, 0, 0);
                unit.GetUnitComponent<UnitEquipment>()?.OnCollect();
            }

            var controlCommands = new List<BattleCommand>();
            var cancels = _sim.ConsumePendingHarvestCancels();
            if (cancels != null)
                foreach (var cancel in cancels)
                    controlCommands.Add(BattleCommand.HarvestChannel(BattleCommandType.HarvestChannelCancel,
                        cancel.unitId, cancel.nodeCell));
            controlCommands.AddRange(startedCommands);
            if (controlCommands.Count == 0 && effects.Count == 0) return;

            int slice = _sliceCounter++;
            var segment = new Segment
            {
                turnNumber = _sim.CurrentSecond,
                sliceIndex = slice,
                sliceAttackSpeed = 0,
                turnMaxAttackSpeed = 0,
                insertedInstantAction = 0,
                startTick = _sim.CurrentTick,
            };
            foreach (var command in controlCommands)
            {
                command.sliceIndex = slice;
                command.indexInSlice = segment.commands.Count;
                segment.commands.Add(command);
            }

            var shatters = new List<(string holderId, string sourceId, BaseBuff buff)>();
            var appliedBuffs = effects.Count > 0 ? ApplyEffects(effects, shatters) : new List<ApplyBuffEffect>();
            var newlyDead = effects.Count > 0
                ? _sim.ResolveDeaths(CollectDamagedTargets(effects)) : new List<Unit>();
            EmitSliceCommands($"tick{_sim.CurrentTick}采集", segment, slice,
                effects, appliedBuffs, null, newlyDead, expired: null, skillCasts: null,
                shattered: shatters);
            if (segment.commands.Count > 0) PushSegment(segment);
        }

        private ResourceNodeState FindHarvestNodeInRange(Unit unit, List<ResourceNodeState> nodes, int radiusCm)
        {
            if (unit == null || nodes == null || radiusCm < 0) return null;
            var pos = _sim.GetPosCm(unit);
            ResourceNodeState best = null;
            long bestDistance = (long)radiusCm * radiusCm + 1;
            foreach (var node in nodes)
            {
                if (node == null || node.remainingCharges <= 0) continue;
                long dx = node.cell.CenterXCm - pos.x;
                long dz = node.cell.CenterYCm - pos.y;
                long distance = dx * dx + dz * dz;
                if (distance >= bestDistance) continue;
                best = node;
                bestDistance = distance;
            }
            return best;
        }

        private static void AppendHarvestEffects(List<BattleEffect> effects, string unitId, string playerId,
            BattleCell nodeCell, int remaining, int staminaGain, int energyGain)
        {
            effects.Add(new MoraGainEffect(playerId, ResourceNodeMetrics.MoraPerHarvest, 0));
            if (staminaGain != 0) effects.Add(new StaminaEffect(playerId, staminaGain, 0));
            if (energyGain != 0)
                effects.Add(new EnergyEffect(unitId, energyGain, EnergyEffect.CategoryCollectGain));
            effects.Add(new ResourceHarvestEffect(unitId, nodeCell,
                ResourceNodeMetrics.MoraPerHarvest, remaining, 0));
        }

        /// <summary>1B 敏锐感知：半径与持续时间均读已选命座资产；已有更长暴露时不重复刷命令。</summary>
        private void AppendAmberKeenSensesEffects(List<BattleEffect> effects)
        {
            foreach (var kv in _sim.SortedUnits())
            {
                var amber = kv.Value;
                if (amber == null || BattleSimState.IsDead(amber)
                    || !amber.HasConstellationChoice(SkillName.Amber_C1B_KeenSenses)) continue;
                var option = amber.RawData?.GetConstellationOption(1,
                    (int)SkillName.Amber_C1B_KeenSenses);
                var data = option?.skill?.data;
                if (data == null) continue;
                int radiusCm = data.GetInt(SkillParamKey.EffectRadius, 0);
                int durationSeconds = data.GetInt(SkillParamKey.VisionDuration, 0);
                if (radiusCm <= 0 || durationSeconds <= 0) continue;
                var amberIdentity = amber.GetUnitComponent<UnitIdentity>();
                if (amberIdentity == null) continue;
                var amberPos = _sim.GetPosCm(amber);
                foreach (var enemyKv in _sim.SortedUnits())
                {
                    var enemy = enemyKv.Value;
                    if (enemy == null || BattleSimState.IsDead(enemy)) continue;
                    var enemyIdentity = enemy.GetUnitComponent<UnitIdentity>();
                    if (enemyIdentity == null || enemyIdentity.Team == amberIdentity.Team) continue;
                    var enemyPos = _sim.GetPosCm(enemy);
                    long dx = enemyPos.x - amberPos.x;
                    long dz = enemyPos.y - amberPos.y;
                    if (dx * dx + dz * dz > (long)radiusCm * radiusCm) continue;

                    bool alreadyExposed = false;
                    foreach (var buff in enemy.Buffs)
                        if (buff != null && buff.Type == BuffType.VisionExposed
                            && buff.RemainingSeconds >= durationSeconds)
                        {
                            alreadyExposed = true;
                            break;
                        }
                    if (alreadyExposed) continue;
                    effects.Add(new ApplyBuffEffect(kv.Key, enemyKv.Key, (int)BuffType.VisionExposed, 1,
                        durationSeconds: durationSeconds, sourceSkillId: (int)SkillName.Amber_C1B_KeenSenses,
                        buffKey: BuffKeys.VisionExposure));
                }
            }
        }

        /// <summary>弹性分离位移下发（2026-11-08 拍板「友军碰撞有弹性、建筑硬」）：Host 权威推挤按
        /// 5Hz 批量成段——命令=Move + MovePushed 标记 + hitX/hitY 新绝对位 cm。移动中的单位跳过
        /// （Move 补间权威，避免双写）；与 ResolveMoveArrivals 的被挡校正段同构：段首直产命令 +
        /// EmitSliceCommands 领 sliceIndex（零效应载荷=纯表现段）。</summary>
        public void EmitSeparationCorrections()
        {
            var pending = _sim.PushPending;
            if (pending.Count == 0) return;
            var commands = new List<BattleCommand>();
            foreach (var kv in pending) // SortedDictionary=确定性命令序（回放段 diff）
            {
                var push = BattleCommand.Move(kv.Key, 0, 0, new List<BattleCell>(),
                    BattleCommand.MovePushed, (int)Direction2D.Right);
                push.hitX = kv.Value.x;
                push.hitY = kv.Value.y;
                commands.Add(push);
            }
            pending.Clear();
            if (commands.Count == 0) return;

            int slice = _sliceCounter++;
            var segment = new Segment
            {
                turnNumber = _sim.CurrentSecond,
                sliceIndex = slice,
                sliceAttackSpeed = 0,
                turnMaxAttackSpeed = 0,
                insertedInstantAction = 0,
                startTick = _sim.CurrentTick,
            };
            foreach (var command in commands)
            {
                command.sliceIndex = slice;
                command.indexInSlice = segment.commands.Count;
                segment.commands.Add(command);
            }
            EmitSliceCommands($"tick{_sim.CurrentTick}推挤", segment, slice,
                new List<BattleEffect>(), new List<ApplyBuffEffect>(), null, new List<Unit>(),
                expired: null, skillCasts: null, shattered: null);
            if (segment.commands.Count > 0) PushSegment(segment);
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
                startTick = _sim.CurrentTick, // 决策九十五即时制：段产生时刻（客户端绝对时刻播放）
            };

            // 片前快照（瞬发效应读取状态）+ 片内附着编译视图（2026-10-01 双蒸发修复）：
            // 同片多次命中的反应消耗/新附着随编译序对后续命中可见——快照只是附着初值
            var snapshot = _sim.TakeSnapshot(turnNumber);
            _sim.BeginCompileDyeView();

            var effects = new List<BattleEffect>();
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
                        // P6 真摇杆持续态（蓝图 §3.3「方向持续：松手停」）：aim 双零=停止信号——移除
                        // 该单位在途 mover+发位置校正命令（空 path+cm 停点，P4-E 被挡校正同协议形态、
                        // blocked=0 客户端净停不弹回）；无在途 mover=幂等零命令纯静默（重复停/摇杆回中抖动）
                        if (action.moveContinuous && action.aimX == 0 && action.aimZ == 0)
                        {
                            _sim.CancelHarvestChannel(action.unitId); // 主动停止也属于主动操作
                            int removed = _sim.ActiveMoversInternal.RemoveAll(m => m.UnitId == action.unitId);
                            if (removed > 0)
                            {
                                var stopCm = _sim.GetPosCm(unit);
                                var stop = BattleCommand.Move(action.unitId, 0, 0, new List<BattleCell>(), 0,
                                    (int)Direction2D.Right);
                                stop.hitX = stopCm.x;
                                stop.hitY = stopCm.y;
                                casts.Add(stop);
                            }
                            break;
                        }
                        // **2026-11-04 拍板「100 移速=走完一米要 1 秒」**：移动统一新轨——旧格式上交
                        //（AI 三脑与玩家 HUD 的 direction+magnitude）在 Host 侧桥为目标点
                        // cm（当前格+方向×步数→格心），与 targetX 直填同走 RTSMovementSystem
                        //（100cm/s tick 推进+圆碰撞+1 米/秒节奏）。旧格轨移动执行退役（0.18s/格
                        //≈5.5 米/秒观感被否）；MovementResolver 已随 2026-11-09 残留清理批整体删除
                        //（部署碰撞原语由 DeployUnitExecutor 自持同构实现，移动执行早已不走它）
                        if (action.targetX <= 0 && action.targetZ <= 0 && action.moveMagnitude > 0)
                        {
                            var delta = SkillHitResolver.DirectionToDelta(action.direction);
                            var fromCell = BattleCell.FromCm(_sim.GetPosCm(unit).x, _sim.GetPosCm(unit).y);
                            var targetCell = new BattleCell(
                                fromCell.x + delta.x * action.moveMagnitude,
                                fromCell.y + delta.y * action.moveMagnitude);
                            action.targetX = targetCell.CenterXCm;
                            action.targetZ = targetCell.CenterYCm;
                        }
                        if (action.targetX > 0 || action.targetZ > 0)
                        {
                            // 同单位重复移动=替换（玩家连点重定目标/AI 每 0.5 秒重决策——旧 mover 作废）
                            _sim.ActiveMoversInternal.RemoveAll(m => m.UnitId == action.unitId);
                            var cmMover = RTSMovementSystem.BuildCmMover(_sim, action, out var moveCommand);
                            if (cmMover != null)
                            {
                                _sim.CancelHarvestChannel(action.unitId);
                                _sim.ActiveMoversInternal.Add(cmMover);
                                if (moveCommand != null) casts.Add(moveCommand); // 启动 Move 命令（casts 首发位——客户端按 MoveStepSeconds=1s/格补间）
                                AddMoveCast(casts, unit, action, snapshot); // 时轮（B-S1b）：移动=特殊技能
                            }
                        }
                        break; // 零步/不可寻路=移动落空（与技能落空同语义）

                    case ActionType.Skill:
                        effects.AddRange(SkillExecutor.Resolve(_sim, action, snapshot, casts));
                        break;

                    case ActionType.Pass:
                        effects.AddRange(PassExecutor.Resolve(_sim, action));
                        break;

                    default:
                        GICLog.Warn($"[BattleResolver] B1 未实现的行动类型 {action.actionType}，跳过");
                        break;
                }
            }

            // （旧格轨移动执行链已整体退役删除——2026-11-04 拍板「1 米/秒」Move 分支全部桥入
            // RTSMovementSystem tick 新轨；MovementResolver/MoveActionState/legacyMovers 随
            // 2026-11-09 残留清理批删除。快速采集由 ResolveHarvestTick 按实际连续位置逐 tick 结算）

            // 投射物连续命中判定（B5）：移动展开后按执行阶段时间轴模拟接触（读移动者完整轨迹
            //——即时制新轨 mover/静止快照，docs/22 §11；命中点/消散点随命令毫米定点下发）
            var vanishes = new List<BattleCommand>();
            ProjectileResolver.Resolve(_sim, snapshot, effects, vanishes);
            _sim.EndCompileDyeView(); // 命中编译全部完成（此后效应应用/命令发射不再读附着）

            // 效应统一应用（伤害/治疗合并 HP 天然成立；Buff 施加记入注册表）；碎裂记录=寒冰之棱
            // 「元能>50% 立刻触发」（2026-10-01 复测拍板）——ApplyEffects 元能增益段即时产出
            var shatters = new List<(string holderId, string sourceId, BaseBuff buff)>();
            var appliedBuffs = ApplyEffects(effects, shatters);

            // 死亡判定（效应应用后统一判；同片互杀 = 同归于尽）
            var damagedUnits = CollectDamagedTargets(effects);
            var newlyDead = _sim.ResolveDeaths(damagedUnits);

            // 产出片命令块（三段唯一出口 EmitSliceCommands——命令发射序与对账见其内注释）
            EmitSliceCommands($"秒{turnNumber}片{sliceIndex}", segment, sliceIndex,
                effects, appliedBuffs, vanishes, newlyDead, expired: null, skillCasts: casts,
                shattered: shatters);

            GICLog.Info($"[BattleResolver] {segment}");
            return segment;
        }

        /// <summary>
        /// 部署段（B6c）：秒拍先于攻速分桶结算全部出战行动（每玩家每秒拍最多 1 条，配额已保证）。
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
                startTick = _sim.CurrentTick, // 决策九十五即时制：段产生时刻（客户端绝对时刻播放）
            };

            int indexInSlice = 0;
            foreach (var action in deploys)
            {
                if (DeployUnitExecutor.TryResolve(_sim, action, out var summon, out var moraChange,
                        out var itemGains))
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
                    // 命座跨层物品授予（装备批·安柏3命兔兔伯爵）：ItemGain 命令随升命命令之后
                    if (itemGains != null)
                    {
                        foreach (var gain in itemGains)
                        {
                            gain.sliceIndex = sliceIndex;
                            gain.indexInSlice = indexInSlice++;
                            segment.commands.Add(gain);
                        }
                    }
                    // 骑士团部署即召（决策八十六批D，docs/45 §1.1 拍板 #10「部署时就立刻召唤
                    // 1 个」）：周期召唤建筑落位后当场召 1 名 召唤单位（最近可部署格顺延）——不等
                    // 效果拍周期；召唤单位缺省（Amber=字段默认未配置）不召防误配
                    var summonData = Wargame.Instance?.Context?.Get<UnitConfig>()?
                        .GetUnitData((UnitName)action.deployUnitName);
                    if (summonData != null && summonData.unitType == UnitType.Building
                        && summonData.建筑效果 == BuildingEffectKind.PeriodicSummon
                        && summonData.召唤单位 != UnitName.Amber)
                    {
                        // 召唤锚=本次部署落点（TryResolve 已把新建筑注册到 action.deployCell）——
                        // 勿按名字查同名单位：多座同种建筑时 FindOwnUnitByName 恒命中先部署那座，
                        // 后部署建筑的部署即召全挤到老建筑旁（2026-10-23 报障「两座骑士团产出的
                        // 骑士都从第一座出来」根因）
                        if (DeployUnitExecutor.TrySummonAtNearestCell(_sim, action.playerId, summonData.召唤单位,
                                action.deployCell, out var instantSummon))
                        {
                            instantSummon.sliceIndex = sliceIndex;
                            instantSummon.indexInSlice = indexInSlice++;
                            segment.commands.Add(instantSummon);
                        }
                    }
                }
            }

            GICLog.Info($"[BattleResolver] 部署段 {segment}");
            return segment;
        }

        /// <summary>
        /// 物品行动段（2026-10-16 首个可打出物品批；2026-10-18 装备批并 EquipItem）：同秒拍部署段
        /// 之后的玩家级即时段——不进攻速分桶（UseItem/EquipItem 均无单位归属，同部署段口径）。
        /// 快照→逐行动校验+效果编译（UseItemExecutor/EquipItemExecutor：手牌持有/目标我方存活/
        /// 消耗 1 张）→效应统一应用→死亡判定→命令发射（Heal/StatChange(Sanity)/ItemConsume/
        /// EquipItem 命令全链复用既有协议）。凯亚变奏嵌套饮用（UseHandItem 原子）走 SkillExecutor
        /// 效应链，不经本段。
        /// </summary>
        private Segment ResolveItemActionsSegment(int turnNumber, int sliceIndex, List<ActionData> itemActions)
        {
            var segment = new Segment
            {
                turnNumber = turnNumber,
                sliceIndex = sliceIndex,
                sliceAttackSpeed = 0,
                turnMaxAttackSpeed = 0,
                insertedInstantAction = 0,
                startTick = _sim.CurrentTick, // 决策九十五即时制：段产生时刻（客户端绝对时刻播放）
            };

            var snapshot = _sim.TakeSnapshot(turnNumber);
            var effects = new List<BattleEffect>();
            foreach (var action in itemActions)
            {
                // 校验失败（无配置/无手牌/目标无效/槽满）=零效果零消耗，不影响同段其他玩家的行动
                if (action.actionType == ActionType.EquipItem)
                    EquipItemExecutor.TryResolve(_sim, action, snapshot, effects);
                else
                    UseItemExecutor.TryResolve(_sim, action, snapshot, effects);
            }

            // 效应统一应用（治疗/理智/物品消耗/装备挂槽——ItemConsumeEffect 应用=手牌扣减+Consumed 明细）+ 碎裂记录
            var shatters = new List<(string holderId, string sourceId, BaseBuff buff)>();
            var appliedBuffs = ApplyEffects(effects, shatters);
            var damagedUnits = CollectDamagedTargets(effects);
            var newlyDead = _sim.ResolveDeaths(damagedUnits);

            EmitSliceCommands($"秒{turnNumber}片{sliceIndex}", segment, sliceIndex,
                effects, appliedBuffs, vanishes: null, newlyDead, expired: null, skillCasts: null,
                shattered: shatters);

            GICLog.Info($"[BattleResolver] 物品行动段 {segment}");
            return segment;
        }

        /// <summary>
        /// 每秒整拍结算段（决策九十五「每秒整拍」拍板 4——原回合结束段秒化，docs/active/48 §2.3/§9；
        /// **2026-11-04 追拍「效果触发时间翻倍」**：拆秒拍/效果拍两档——秒拍=发放 1摩拉+1体力/Buff 计时
        /// 递减/泡湿/资源点站立采集/快照；**效果拍（每 EffectBeatTicks=2 秒）=Buff tick 效果〔OnEffectBeat
        /// 产出——歌声之环/冰棱/燃烧〕+建筑效果〔教堂/旗帜/餐馆/骑士引擎〕**，单次数值不动=触发频率
        /// 减半；战技/爆发 CD 不受影响（独立 3s/5s 拍板））：
        /// ①每玩家发放 → ②Buff 整拍效果按注册序结算（docs/22 §2——Buff 计时随 RemainingSeconds 语义改秒）
        /// → ③计时减一（上 buff 当拍结束即减）→ 到期移除 → 产出 turnEnd 段（发放恒非空，段必推）。
        /// 由 RTSFlowController 每秒整拍（tick%20==0）调用；胜负检查归调用方。
        /// </summary>
        public void ResolveSecondBeat(int second, bool effectBeat)
        {
            // 逻辑秒真源写入（即时制：BattleSecond 语义=逻辑秒——周期类建筑效果/灵魂 DeployedSecond
            // 相对计数零改动兼容：每 1 秒走 1 拍=旧「每回合走 1」等价节拍）
            _sim.BattleSecond = second;
            int sliceIndex = _sliceCounter++;
            var segment = new Segment
            {
                turnNumber = second,
                sliceIndex = sliceIndex,
                sliceAttackSpeed = 0,
                turnMaxAttackSpeed = 0,
                insertedInstantAction = 0,
                turnEnd = 1,
                startTick = _sim.CurrentTick,
            };

            // 秒拍发放（段首；indexInSlice 自此起算——EmitSliceCommands 接续 segment.commands.Count）
            int grantIndex = 0;
            foreach (var pid in _sim.PlayerIds)
            {
                // 凌晨批议题⑥天理经济作弊：摩拉发放按难度倍率（AIPlayerConfig.MoraIncomePercentOf
                // 单源——天理=AITuning.天理摩拉收入百分比、其余档恒 100 零变化；SC2 CheatMoney/petra
                // 经济倍率表先例：决策逻辑不动、收入作弊）
                int moraGain = BattleMetrics.MoraGainPerSecond * AIPlayerConfig.MoraIncomePercentOf(pid) / 100;
                _sim.ApplyMoraDelta(pid, moraGain);
                segment.commands.Add(BattleCommand.StatChange(pid, sliceIndex, grantIndex++,
                    BattleCommand.StatKindMora, moraGain));
                _sim.ApplyStaminaDelta(pid, BattleMetrics.StaminaGainPerSecond);
                segment.commands.Add(BattleCommand.StatChange(pid, sliceIndex, grantIndex++,
                    BattleCommand.StatKindStamina, BattleMetrics.StaminaGainPerSecond));
            }

            var effects = new List<BattleEffect>();

            // 按注册序结算效果拍效果，随后计时减一（到期收集；永久 Buff（RemainingSeconds<0）不计时——歌声之环类）
            // **效果拍末=同时结算（决策二十四终版「逐拍交错」）**：buff 的 OnEffectBeat 只产出声明（元素
            // 伤害走 PendingAuraHit），循环后统一按 (拍时刻, 施法者, 目标) 排序逐个过共享编译视图结算
            // ——双异元素光环同拍交错、每层附着、每拍可触发反应（对齐原神双光环同开互融）
            // **2026-11-04 追拍**：OnEffectBeat 产出只在效果拍（每 2 秒）执行——「太频繁」节奏翻倍；
            // 计时递减仍每秒（Duration 秒语义不变：冻结 2 秒仍 2 秒，只少一次无产出的空转调用）
            foreach (var buff in _sim.ActiveBuffs) // DETERMINISM-ALLOW: ActiveBuffs 是 List（注册序确定，非字典）
            {
                if (effectBeat) effects.AddRange(buff.OnEffectBeat());
                if (!buff.IsPermanent) buff.RemainingSeconds--;
            }
            if (effectBeat)
                ResolveAuraHits(effects);
            var expired = _sim.CollectExpiredBuffs();

            // 泡湿附着（决策七十六拍板②「在水面上，且不为飞行或两栖移动的单位，每个回合结束时
            // 附着 1 层水」）：地形判据=WaterTerrain 标签（水面/沼泽；**薄冰不带该标签=站薄冰不挂**），
            // 豁免=移动类型飞行/两栖，存活单位才附着（尸体不挂——复活不带附着语义接近）。
            // 覆盖语义=挂 1 层水覆盖旧附着（泡在水里火挂不住）；放 tick/到期之后=秒拍末收尾。
            // 元素联动：泡湿单位天然带水附着——凯亚冰打=冻结反应、安柏火箭=蒸发（战术意义自洽）
            foreach (var kv in _sim.SortedUnits())
            {
                var unit = kv.Value;
                if (unit == null || BattleSimState.IsDead(unit)) continue;
                var moveable = unit.GetUnitComponent<UnitMoveable>();
                var moveType = moveable != null ? moveable.NormalMoveType : ForceType.Walk;
                if (moveType == ForceType.Fly || moveType == ForceType.Amphibious) continue;
                var pos = _sim.GetPosition(unit);
                if (!_sim.Map.HasTile(pos.x, pos.y)) continue;
                if (_sim.Map.GetTile(pos.x, pos.y).HasTag(TileTag.WaterTerrain))
                    effects.Add(new AttachElementEffect(kv.Key, kv.Key, (int)ElementType.Hydro, 1));
            }

            // 地脉维度秒拍末规则（决策九十一，2026-10-25）：①灵魂衰减（每秒拍末 −⌊10%×MaxHp⌋，
            // 死亡当秒拍末豁免=完整 10 秒驻留）——SoulDecayEffect 走标准效应管道（应用层扣 HP、
            // 命令层 StatChange(StatKindHp)、HP≤0 经死亡循环「灵魂+尸体双双消散」分支）；
            // ②地脉驻留理智代价（非灵魂地脉单位 −3 理智/秒拍末——当前地脉仅灵魂=预留自动生效，
            // 胡桃/丽莎相位技能批上线即激活）。位置=泡湿之后（同为秒拍末收尾类环境规则）
            CollectLeyLineEffectBeatEffects(effects);

            var shatters = new List<(string holderId, string sourceId, BaseBuff buff)>();
            // 建筑效果拍段（决策八十六批D，docs/45 §6：泡湿之后、命令发射之前——秒拍末
            // 收尾位）：旗帜元能/教堂治疗并入 effects（命令发射+死亡收集复用既有管道）；餐馆摩拉/
            // 骑士团周期召唤直产命令（秒拍发放/部署段先例——资源/召唤不是单位效应）。
            // **2026-11-04 追拍**：建筑效果只在效果拍（每 2 秒）结算——教堂治疗/旗帜/餐馆/骑士引擎
            // 触发频率翻倍拍板；周期类字段（效果周期）按拍计数=时长自然翻倍
            var buildingExtras = new List<BattleCommand>();
            if (effectBeat)
                _buildingEffects.CollectEffectBeatEffects(_sim.BattleSecond, effects, buildingExtras);
            var appliedBuffs = ApplyEffects(effects, shatters);
            var newlyDead = _sim.ResolveDeaths(CollectDamagedTargets(effects));

            // 产出秒拍结束段命令（三段唯一出口；到期 Buff 的 RemoveBuff 发射+注册表注销在出口尾部）
            EmitSliceCommands($"秒拍{_sim.BattleSecond}结束段", segment, sliceIndex,
                effects, appliedBuffs, null, newlyDead, expired, shattered: shatters);

            // 建筑效果直产命令（餐馆摩拉/骑士团召唤）段尾发射——indexInSlice 接续既有条数
            foreach (var extra in buildingExtras)
            {
                extra.sliceIndex = sliceIndex;
                extra.indexInSlice = segment.commands.Count;
                segment.commands.Add(extra);
            }

            GICLog.Info($"[BattleResolver] {segment}");
            PushSegment(segment); // 秒拍发放恒非空段必推（决策九十五：直发无 ack）
        }

        /// <summary>
        /// 效果拍光环命中交错结算管道（决策二十四终版「同时进行」=逐拍交错）：从 effects 收集全部
        /// PendingAuraHit（buff 的 OnEffectBeat 只声明），按 (拍时刻, 施法者 unitId, 目标 unitId) 排序后
        /// 共享编译视图逐个结算——同拍并列枚举序铁律（unitId 升序）；每层命中都是一次完整元素应用
        /// （反应预判+1:1 消耗+附着，CompileElementalDamage 统一出口）——双异元素光环每拍交错、
        /// 反应在应用序列里自然发生（火环 L0 挂火→冰环 L0 融化挂冰→火环 L1 融化挂火…）。
        /// 编译产物追加进 effects（命令时刻=拍时刻显式携带，段内发射序无关紧要）；附着统一应用
        /// 落定=排序序后到者胜。
        /// </summary>
        private void ResolveAuraHits(List<BattleEffect> effects)
        {
            var hits = new List<PendingAuraHit>();
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                if (effects[i] is PendingAuraHit hit)
                {
                    hits.Add(hit);
                    effects.RemoveAt(i);
                }
            }
            if (hits.Count == 0) return;
            hits.Sort((a, b) =>
            {
                int byBeat = a.BeatSeconds.CompareTo(b.BeatSeconds);
                if (byBeat != 0) return byBeat;
                int byAttacker = string.CompareOrdinal(a.AttackerUnitId, b.AttackerUnitId);
                if (byAttacker != 0) return byAttacker;
                return string.CompareOrdinal(a.TargetUnitId, b.TargetUnitId);
            });

            _sim.BeginCompileDyeView(); // 共享视图：初值回落活态=秒拍末段开始态；交错序列全程接力
            foreach (var hit in hits)
            {
                // 元素×地形（决策七十六泛化拍板）：火元素 tick（燃烧 DoT 等）命中站在薄冰上的单位
                // →其格融化为水面——与 CompileOnHit 出口同口径（一切火元素伤害命中薄冰上单位即融）；
                // 纯附着事件（Request=null）非伤害不融；HitSeconds=拍时刻（逐拍到点融化）
                if (hit.Element == ElementType.Pyro && hit.Request != null && hit.Target != null)
                {
                    var hitPos = _sim.GetPosition(hit.Target);
                    if (_sim.Map.GetTile(hitPos.x, hitPos.y) == TileType.ThinIce)
                        effects.Add(new TerrainChangeEffect(hitPos, (int)TileType.Water)
                        {
                            HitSeconds = hit.BeatSeconds,
                        });
                }

                // 统一出口（Request=null=纯附着事件只过反应预判）；返回=来袭已被处置（反应消耗/
                // 0层/剩余层附着，决策七十四）——该层**不再附着**；无反应层才附着（覆盖，带层数）
                bool consumed = EffectCompiler.CompileElementalDamage(_sim, hit.AttackerUnitId, hit.TargetUnitId, null,
                    hit.Element, hit.Request, hit.BeatSeconds, hit.BeatSeconds, effects,
                    incomingLayers: hit.Layers);
                if (hit.Attach && !consumed)
                {
                    effects.Add(new AttachElementEffect(hit.AttackerUnitId, hit.TargetUnitId, (int)hit.Element,
                        Mathf.Max(1, hit.Layers))
                    {
                        HitSeconds = hit.BeatSeconds, // 逐拍到点挂附着图标（WYSIWYG，决策七十六复测返修）
                    });
                    _sim.SetCompileDye(hit.TargetUnitId, hit.Element, Mathf.Max(1, hit.Layers));
                }
            }
            _sim.EndCompileDyeView();
        }

        // （即时行动独立结算 ResolveInstantAction 已随片边界即时队列退役删除——决策九十五
        // docs/active/48 §2.4：即时通道购买分支=BuyCardExecutor 直通；非购买即时行动改走
        // RTSFlowController.OnSubmitAction 校验+ResolveSubmittedAction 即时结算）

        // ==================== 段命令发射（三段唯一出口，2026-09-23 审查 Y1 收口） ====================

        /// <summary>效应命中时刻→命令应用时刻注入（WYSIWYP 时间通道单源，2026-10-02 复审收口）：
        /// HitSeconds>0 才覆写 launchMs（0=立即=构造默认）；Reaction/Energy/MoraPlunder/Heal 各发射
        /// 分支统一走此口，勿散抄公式（新增带时刻效应同样走此口；internal=BuildingEffectsSystem
        /// 发射侧共用——2026-10-26 ③收口）</summary>
        internal static void ApplyHitMs(BattleCommand command, float hitSeconds)
        {
            if (hitSeconds > 0f)
                command.launchMs = Mathf.RoundToInt(hitSeconds * 1000f);
        }

        /// <summary>
        /// 段命令统一发射：片/即时段/秒拍结束段三处原为复制粘贴（曾致 turnEnd 段 Damage 漏带命中点/
        /// 反应标记的漂移）——收口后新效应→命令映射只加一处，BattleEffectCommandAudit 对账随发射统一覆盖三段。
        /// 发射序：SkillCast（时轮 B-S1）→ Damage → 消散Effect → 附着 → 反应 → 元能 → 体力（B6d）
        /// → 治疗 → 施加Buff → 死亡 → 到期移除Buff。段首已有直产命令（秒拍段发放）时 indexInSlice 接续。
        /// 与旧回合结束段序的差异：治疗从 Damage 后移至元能后（客户端 stagger 约 +0.36s，纯视觉节拍）。
        /// </summary>
        private void EmitSliceCommands(string context, Segment segment, int sliceIndex,
            List<BattleEffect> effects, List<ApplyBuffEffect> appliedBuffs,
            List<BattleCommand> vanishes, List<Unit> newlyDead,
            List<BaseBuff> expired = null, List<BattleCommand> skillCasts = null,
            List<(string holderId, string sourceId, BaseBuff buff)> shattered = null)
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

            // （旧格轨 Move 命令发射块已删除——2026-11-09 残留清理批：movers 恒空集死代码；
            //  移动命令改由 RTSMovementSystem 收尾事件驱动，BattleCommand.Move 保留供回放旧谱）

            foreach (var effect in MergeDamageEffects(effects))
            {
                // 命中点 mm 定点（P3 cm 化：HitPointX/Y 自 ProjectileResolver 起即厘米域 float——
                // ×10=毫米；旧 ×1000 是千分格系数，cm 迁移后若沿用=放大 100 倍〔P3 观察局抓出〕。
                // 客户端消费 ÷1000 换世界=cm÷1000÷10=cm/100=格——分母不变零改动）
                var dmgCmd = BattleCommand.Damage(effect.AttackerUnitId, effect.TargetUnitId, sliceIndex, indexInSlice++, effect.Amount,
                    effect.Element, effect.Delivery, effect.FromCell,
                    Mathf.RoundToInt(effect.HitPointX * 10f), Mathf.RoundToInt(effect.HitPointY * 10f), effect.ReactionType,
                    effect.LaunchMs, effect.IsCrit ? 1 : 0);
                // 箭矢视觉高度（2026-10-05 对齐动作片松弦位）：投射物路径经发射声明透传千分下发；
                // 瞬发直击（Delivery=0）无箭矢视觉不消费
                dmgCmd.arrowHeightY = Mathf.RoundToInt(effect.ProjectileHeightY * 1000f);
                // 装备格挡标记（装备批 2026-10-18·兔兔伯爵）：value=原伤害量（客户端「格挡」文案尺寸），
                // blocked=1=客户端不弹数字不掉血改弹格挡反馈（箭矢照飞——WYSIWYG 攻击发生但被抵挡）
                dmgCmd.blocked = effect.BlockedByItem != 0 ? 1 : 0;
                segment.commands.Add(dmgCmd);
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
                var attachCommand = BattleCommand.ElementAttach(effect.SourceUnitId, effect.TargetUnitId,
                    sliceIndex, indexInSlice++, effect.Element);
                // 命中时刻到点挂图标（决策七十六复测返修 WYSIWYG：箭在飞时目标不提前显示附着）
                ApplyHitMs(attachCommand, effect.HitSeconds);
                segment.commands.Add(attachCommand);
            }
            foreach (var effect in EnumerateEffects<ReactionEffect>(effects))
            {
                var reactionCommand = BattleCommand.Reaction(effect.SourceUnitId, effect.TargetUnitId,
                    sliceIndex, indexInSlice++, effect.ReactionType, effect.Level);
                // 命中时刻（决策二十五）：客户端到点清附着图标（反应 1:1 双方全消耗、等层零残留）
                ApplyHitMs(reactionCommand, effect.HitSeconds);
                segment.commands.Add(reactionCommand);
            }
            foreach (var effect in MergeEnergyEffects(effects))
            {
                var energyCommand = BattleCommand.StatChange(effect.TargetUnitId, sliceIndex, indexInSlice++,
                    BattleCommand.StatKindEnergy, effect.Delta);
                // 命中时刻（2026-09-25 拍板「命中时才给」）：战技获能命令带命中毫秒，客户端到点跳元能；
                // launchMs 字段复用为"应用时刻"（union 载荷；0=立即——移动获能/协奏/消耗/秒拍发放无命中时刻）
                ApplyHitMs(energyCommand, effect.HitSeconds);
                segment.commands.Add(energyCommand);
            }
            // 理智恢复（2026-10-01 拍板②改逐层）：每层各一枚 SanityEffect（+1×层数），命令按目标
            // **合并总值**一条——理智无弹数字，逐条首值发射=状态 N×1 vs 命令 +1 漂移（合并两清）
            var sanityTotals = new Dictionary<string, int>();
            foreach (var effect in effects)
            {
                if (!(effect is SanityEffect sanity)) continue;
                sanityTotals.TryGetValue(sanity.TargetUnitId, out var total);
                sanityTotals[sanity.TargetUnitId] = total + sanity.Delta;
            }
            foreach (var sanityPair in sanityTotals)
                segment.commands.Add(BattleCommand.StatChange(sanityPair.Key, sliceIndex, indexInSlice++,
                    BattleCommand.StatKindSanity, sanityPair.Value));
            // 地脉灵魂衰减（决策九十一）：每灵魂一条 StatChange(StatKindHp) 负值——客户端血条
            // 衰减+灵魂蓝数字（非伤害路径：无防御/暴击/反应/格挡；发射位先于死亡循环=血条先落
            // 再播消散淡出）
            foreach (var effect in EnumerateEffects<SoulDecayEffect>(effects))
                segment.commands.Add(BattleCommand.StatChange(effect.TargetUnitId, sliceIndex, indexInSlice++,
                    BattleCommand.StatKindHp, -effect.Amount));
            // 体力变化（B6d）：TargetUnitId=玩家 ID；逐条发射（2026-10-28 返修：撤同玩家去重——
            // 移动片内配额消耗与途经采集体力并存为常态，去重会吞采集增量=状态逐条应用 vs 命令
            // 只剩首条的漂移）；途经采集体力带命中时刻到点入账（与堆减件同拍）
            foreach (var effect in effects)
            {
                if (!(effect is StaminaEffect stamina)) continue;
                var staminaCommand = BattleCommand.StatChange(stamina.TargetUnitId, sliceIndex, indexInSlice++,
                    BattleCommand.StatKindStamina, stamina.Delta);
                ApplyHitMs(staminaCommand, stamina.HitSeconds);
                segment.commands.Add(staminaCommand);
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
                // 命中时刻（同元能「命中时才给」）；霜袭瞬发段恒 0=立即
                ApplyHitMs(takeCommand, effect.HitSeconds);
                ApplyHitMs(giveCommand, effect.HitSeconds);
                segment.commands.Add(takeCommand);
                segment.commands.Add(giveCommand);
            }
            // 摩拉消耗（统一消耗模型 C-1）：AppliedAmount=0 零命令（不足额防御路径）；客户端 §78 分流已备
            foreach (var effect in EnumerateEffects<MoraSpendEffect>(effects))
            {
                if (effect.AppliedAmount <= 0) continue;
                segment.commands.Add(BattleCommand.StatChange(effect.TargetUnitId, sliceIndex, indexInSlice++,
                    BattleCommand.StatKindMora, -effect.AppliedAmount));
            }
            // 摩拉获取（docs/47 资源点批，2026-10-27——MoraSpend 获取侧对偶）：采集产出（站立/途经
            // 两路径共用）→StatChange(StatKindMora)——玩家池入账唯一出口单源（HUD 资源 chips/
            // 手牌货币卡既有消费链零改动自动跟随）；途经采集带命中时刻到点入账（行走到达堆格再跳
            // chips，「表现对齐动画」返修）；秒拍末站立采集恒 0 立即（泡湿同族）
            foreach (var effect in EnumerateEffects<MoraGainEffect>(effects))
            {
                var moraGainCommand = BattleCommand.StatChange(effect.TargetUnitId, sliceIndex, indexInSlice++,
                    BattleCommand.StatKindMora, effect.Amount);
                ApplyHitMs(moraGainCommand, effect.HitSeconds);
                segment.commands.Add(moraGainCommand);
            }
            // 资源点采集事实（docs/47）：ResourceHarvest type=23——客户端 BattleBoard 余量刷新/
            // 耗尽移除+采集飘字；metadata=采集后剩余次数、value=本次获得摩拉、cell=资源点格；
            // launchMs=途经采集命中时刻（行走到达该格再减件，客户端 PlayDelayedStateApply 到点应用）
            foreach (var effect in EnumerateEffects<ResourceHarvestEffect>(effects))
            {
                var harvestCommand = BattleCommand.ResourceHarvest(effect.TargetUnitId,
                    sliceIndex, indexInSlice++, effect.NodeCell, effect.MoraGain, effect.Remaining);
                ApplyHitMs(harvestCommand, effect.HitSeconds);
                segment.commands.Add(harvestCommand);
            }
            // 物品消耗（统一消耗模型 C-1）：按 Consumed 明细逐条 ItemConsume（指定模式=单条；
            // 同类任意模式=每实际扣到的物品一条——客户端逐条扣镜像刷角标；**装备实例耐久随条透传**
            // ——决策八十一：装备条目按耐久分实例，客户端镜像按 (物品,耐久) 匹配）；明细空=零命令
            foreach (var effect in EnumerateEffects<ItemConsumeEffect>(effects))
            {
                if (effect.AppliedAmount <= 0 || effect.Consumed.Count == 0) continue;
                foreach (var spent in effect.Consumed)
                    segment.commands.Add(BattleCommand.ItemConsume(effect.TargetUnitId, sliceIndex, indexInSlice++,
                        spent.item, spent.count, spent.durability));
            }
            // 复苏（B-3 ②）：Applied=false（活体防御 no-op）零命令；单命令=解灰+复活血量（客户端 Revive 分支）
            foreach (var effect in EnumerateEffects<ReviveEffect>(effects))
            {
                if (!effect.Applied) continue;
                segment.commands.Add(BattleCommand.Revive(effect.SourceUnitId, effect.TargetUnitId,
                    sliceIndex, indexInSlice++, effect.HealAmount));
                // 灵魂归体消散（决策九十一）：随 Revive 命令补发 Death(dissolve)——客户端灵魂幽灵
                // 淡出（复苏=肉体归位，灵魂「回到」身体）；未来可升级为飞入躯体动画
                if (effect.RemovedSoulId != null)
                    segment.commands.Add(BattleCommand.Death(effect.RemovedSoulId, sliceIndex, indexInSlice++, 1));
            }
            foreach (var effect in MergeHealEffects(effects))
            {
                var healCommand = BattleCommand.Heal(effect.SourceUnitId, effect.TargetUnitId, sliceIndex, indexInSlice++, effect.Amount);
                // 吸血标记（拍板 B 2026-10-01）：metadata=HealKindLifesteal——客户端弹「吸血 +N」名前缀
                if (effect.IsLifesteal)
                    healCommand.metadata = BattleCommand.HealKindLifesteal;
                // 命中时刻（同元能「命中时才给」）：OnHit 治疗（水之浅唱）随投射物落地弹 +N；
                // 0=立即——OnCast 治疗（延奏/变奏）无飞行段
                ApplyHitMs(healCommand, effect.HitSeconds);
                segment.commands.Add(healCommand);
            }
            // 寒冰之棱碎裂（2026-10-01 复测拍板「元能>50% 立刻触发」）：碎裂治疗已随 HealEffect 在上方
            // 发射（状态由 IcicleBuff.TryShatter 内联应用——效应=命令发射载体，同溢出转移先例）；
            // 此处补发 RemoveBuff（客户端徽章即时消失），紧随治疗=观感上「碎裂回血、冰棱散去」
            if (shattered != null)
            {
                foreach (var s in shattered)
                {
                    segment.commands.Add(BattleCommand.RemoveBuff(s.sourceId, s.holderId,
                        sliceIndex, indexInSlice++, (int)s.buff.Type, s.buff.Config?.name)); // 具名 key（决策五十七）
                }
            }
            // 冻结层消耗归零（决策七十四「消耗到 0 立即解冻」）：RemoveBuff 命令（客户端徽章+霜化
            // 即时退场，随碎裂先例立即应用）；未归零=零命令（冻结徽章秒数下一拍快照自愈）
            foreach (var effect in EnumerateEffects<ConsumeFreezeEffect>(effects))
            {
                if (!effect.Depleted) continue;
                segment.commands.Add(BattleCommand.RemoveBuff(effect.SourceUnitId, effect.TargetUnitId,
                    sliceIndex, indexInSlice++, (int)BuffType.Freeze, effect.AppliedKey)); // 具名 key（决策五十七）
            }
            // 地形变化（决策七十六元素×地形）：每格一条 TerrainChange——客户端本地 map SetTile+刷新该格
            foreach (var effect in EnumerateEffects<TerrainChangeEffect>(effects))
            {
                var terrainCommand = BattleCommand.TerrainChange(effect.TargetUnitId, effect.Cell,
                    sliceIndex, indexInSlice++, effect.NewTile); // actorUnitId=触发单位（归因/调试）
                // 命中时刻到点应用（决策七十六复测返修 WYSIWYG——「箭矢还未飞到薄冰就融化」根因：
                // 命令此前无时刻、片头即刷=箭在飞冰先化；箭雨多波=每波到点融化）
                ApplyHitMs(terrainCommand, effect.HitSeconds);
                segment.commands.Add(terrainCommand);
            }
            // 装备效应（装备批 2026-10-18）：应用成功才发 EquipItem——首次装备/格挡耐久刷新/补充耐久
            // 全走同槽重发（角标更新幂等）。**决策八十一：耐久归零不再发 UnequipItem**——留槽停效
            //（客户端图标留位、角标 0 隐藏）；UnequipItem 仅死亡掠夺清槽用
            foreach (var effect in EnumerateEffects<EquipEffect>(effects))
            {
                if (!effect.Applied) continue;
                segment.commands.Add(BattleCommand.EquipItem(effect.PlayerId, effect.TargetUnitId, sliceIndex,
                    indexInSlice++, effect.Item, effect.SlotKind, effect.SlotIndex, effect.DurabilityLeft));
            }
            foreach (var applied in MergeAppliedBuffs(appliedBuffs))
            {
                var applyBuffCommand = BattleCommand.ApplyBuff(applied.SourceUnitId, applied.TargetUnitId,
                    sliceIndex, indexInSlice++, applied.BuffType, applied.Level, applied.Seconds,
                    applied.SourceSkillId, applied.BuffKey); // 具名 key（决策五十七——异资产同族各一条命令）
                // 命中时刻到点挂徽章（决策七十六复测返修 WYSIWYG：箭在飞时目标不提前结冰/挂减防；
                // 合并取首条时刻=徽章首现拍；OnCast 施放/效果拍光环 BeatSeconds 语义不变）
                ApplyHitMs(applyBuffCommand, applied.HitSeconds);
                segment.commands.Add(applyBuffCommand);
            }
            foreach (var dead in newlyDead)
            {
                bool hasId = _sim.TryGetUnitId(dead, out var deadId);
                // 灵魂死亡（决策九十一，2026-10-25）：衰减扣完/（未来）地脉内被胡桃类击杀 → 灵魂本体
                // 与所链尸体**双双消散**（拍板「生命值扣完时，灵魂和尸体都消散」——同名部署位随之
                // 释放、复苏窗口关闭）。灵魂无装备无 Buff（基础属性拍板）→ 无掠夺无 RemoveBuff；
                // 尸体侧残留 Buff 注销（消散分支同款——「死亡不清理」通则对消散单位作废）
                if (dead.IsSoul)
                {
                    var corpse = dead.LinkedCorpseUnitId != null ? _sim.GetUnit(dead.LinkedCorpseUnitId) : null;
                    if (hasId)
                        segment.commands.Add(BattleCommand.Death(deadId, sliceIndex, indexInSlice++, 1)); // 灵魂消散
                    if (corpse != null && _sim.TryGetUnitId(corpse, out var corpseId))
                    {
                        segment.commands.Add(BattleCommand.Death(corpseId, sliceIndex, indexInSlice++, 1)); // 尸体随散
                        for (int i = corpse.Buffs.Count - 1; i >= 0; i--)
                        {
                            var corpseBuff = corpse.Buffs[i];
                            if (corpseBuff == null) continue;
                            _sim.RemoveBuff(corpse, corpseBuff); // OnRemoved 回调（冻结解状态等幂等无害）+注册表注销
                        }
                        _sim.RemoveUnit(corpse);
                        GICLog.Info($"[BattleResolver] 地脉灵魂耗尽：灵魂 {deadId} 与尸体 {corpseId} 双双消散");
                    }
                    _sim.RemoveSoul(dead);
                    continue;
                }
                // 消散判定（决策八十六批B，docs/45 §4）：建筑全部+眷属角色（1~2★）死亡不留尸体
                bool dissolve = BattleHeuristics.ShouldDissolve(dead);
                if (hasId)
                    segment.commands.Add(BattleCommand.Death(deadId, sliceIndex, indexInSlice++, dissolve ? 1 : 0));
                // 装备掠夺（决策八十一「单位被击杀，其具有的装备被击杀方掠夺，放入手牌，耐久不变」）：
                // 死亡命令随发——击杀方=同段内该单位最近一次 Damage 的攻击者归属玩家（OpponentProfiler
                // 死亡归因同源口径）；掠夺条目=武器+全部配件（含耐久），入击杀方手牌后清死单位槽位
                //（词条随撤——复苏不残留幻影词条）；无击杀归属=装备随葬（无动作）
                if (hasId)
                    LootDeadEquipment(dead, deadId, segment, sliceIndex, ref indexInSlice);
                // 消散分支（建筑+眷属，拍板 #5）：装备掠夺先行（上方已跑）→ 全部 Buff 注销（注册表
                // 同步清——「死亡不清理」通则对消散单位作废：鞭尸 tick 天然停）→ 移除+销毁（格子/
                // 碰撞/同名查找/复苏目标域随之消失）。RemoveBuff 命令不发——客户端整单位走消散
                // 退场，徽章随 view 同殁（下一拍快照消失路径天然自洽）
                if (dissolve)
                {
                    for (int i = dead.Buffs.Count - 1; i >= 0; i--)
                    {
                        var buff = dead.Buffs[i];
                        if (buff == null) continue;
                        _sim.RemoveBuff(dead, buff); // OnRemoved 回调（冻结解状态等幂等无害）+注册表注销
                    }
                    _sim.RemoveUnit(dead);
                    continue;
                }
                // 倒下即失的 Buff（B-3 ②：RemoveOnHolderDeath——歌声之环「持有者倒下，歌声之环消失」）：
                // Death 命令后随发 RemoveBuff（客户端徽章即时移除），宿主注册表同步注销（倒序遍历边删）
                for (int i = dead.Buffs.Count - 1; i >= 0; i--)
                {
                    var diedBuff = dead.Buffs[i];
                    if (diedBuff == null || !diedBuff.RemoveOnHolderDeath) continue;
                    string buffSourceId = deadId;
                    if (diedBuff.source != null && _sim.TryGetUnitId(diedBuff.source, out var bsid))
                        buffSourceId = bsid;
                    _sim.RemoveBuff(dead, diedBuff);
                    if (hasId)
                        segment.commands.Add(BattleCommand.RemoveBuff(buffSourceId, deadId,
                            sliceIndex, indexInSlice++, (int)diedBuff.Type, diedBuff.Config?.name)); // 具名 key（决策五十七）
                }

                // 地脉灵魂生成（决策九十一）：走到本尾=非消散存活分支（留尸体=伙伴/魔神）→ 倒下即
                // 同位生成灵魂（「当主世界有伙伴倒下，留下尸体，其灵魂会脱离肉体，出现于地脉维度
                // 相同位置」）。基础属性+满血+治疗效率 50；Summon 命令复用（UnitState.isSoul 客户端
                // 建幽灵立牌）；死亡当秒拍末不衰减（RegisterSoul 写 DeployedSecond，完整 10 秒驻留）
                if (hasId)
                    SpawnSoul(dead, deadId, segment, sliceIndex, ref indexInSlice);
            }

            // 到期 Buff：发射 RemoveBuff 后注销注册表（仅秒拍结束段传入）
            if (expired != null)
            {
                foreach (var buff in expired)
                {
                    if (_sim.TryGetUnitId(buff.owner, out var targetId))
                    {
                        string sourceId = targetId;
                        if (buff.source != null && _sim.TryGetUnitId(buff.source, out var sid))
                            sourceId = sid;
                        segment.commands.Add(BattleCommand.RemoveBuff(sourceId, targetId, sliceIndex, indexInSlice++,
                            (int)buff.Type, buff.Config?.name)); // 具名 key（决策五十七）
                    }
                    _sim.RemoveBuff(buff.owner, buff);
                }
            }

            // 建筑元能引擎过阈召唤（2026-10-24 追记四）：直产命令——消费命令+召唤命令逐建筑发射；
            // 先于对账执行（2026-10-25 复审批修复：原排 audit 后=审计对它失明）+返回过阈事实供对账
            var buildingSummonFacts = _buildingEffects.DrainPendingSummons(segment, sliceIndex);

            // 命令产生时刻整段回填（决策九十五即时制）：客户端按绝对时刻播对齐
            for (int i = 0; i < segment.commands.Count; i++)
                segment.commands[i].tick = segment.startTick;

            BattleEffectCommandAudit.Assert(context, effects, appliedBuffs, segment.commands, buildingSummonFacts);
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

        // （行动预告 SendTurnPlan/攻速分桶 BucketByAttackSpeed 已随回合机退役删除——决策九十五
        // docs/active/48 §2.4：即时制行动上交即结算、无攻速排序；执行预览 UI 退役随 P4 批）

        // ==================== 死亡装备掠夺（决策八十一「单位被击杀，其具有的装备被击杀方掠夺，放入手牌，耐久不变」） ====================

        /// <summary>阵亡单位装备掠夺：武器+全部配件逐件入击杀方手牌（携带当前耐久——耐久不变），
        /// 随发 UnequipItem（客户端图标行清槽）+ItemGain（击杀方手牌镜像）；随后清死单位槽位
        ///（词条随撤——复苏不残留幻影词条）。击杀归属=同段内该单位最近一次 Damage 命令的攻击者
        /// 所属玩家（OpponentProfiler 死亡归因同源口径）；无归属=装备随葬（无动作）。
        /// **归零停效装备同样可被掠夺**（耐久 0 原样入牌——装备后 +0 补充/占槽安装，语义自洽）</summary>
        private void LootDeadEquipment(Unit dead, string deadId, Segment segment,
            int sliceIndex, ref int indexInSlice)
        {
            var equipment = dead.GetUnitComponent<UnitEquipment>();
            if (equipment == null || !equipment.HasAnyEquipment) return;
            string killer = KillerPlayerOf(deadId, segment);
            if (string.IsNullOrEmpty(killer))
            {
                GICLog.Info($"[掠夺] {deadId} 阵亡但无击杀归属（非伤害致死/攻击者无主人），装备随葬");
                return;
            }

            // 武器掠夺
            if (equipment.Weapon is ItemName weapon)
            {
                int weaponDurability = equipment.WeaponDurability;
                _sim.GainCard(killer, new CardId(weapon), 1, weaponDurability);
                equipment.UnequipWeapon();
                segment.commands.Add(BattleCommand.UnequipItem(deadId, sliceIndex, indexInSlice++,
                    (int)weapon, BattleCommand.EquipKindWeapon, 0));
                segment.commands.Add(BattleCommand.ItemGain(killer, sliceIndex, indexInSlice++,
                    (int)weapon, 1, weaponDurability));
                GICLog.Info($"[掠夺] {deadId} 的 {weapon}（耐久 {weaponDurability}）→ {killer} 手牌");
            }
            // 配件掠夺（按槽序；UnequipAccessory 撤词条）：耐久件=1 枚携带当前耐久（「耐久不变」）；
            // **堆叠件=按层数拆 N 枚单卡**（决策八十一追记——每枚=手牌一张、无耐久语义条目）
            var accessories = equipment.Accessories;
            for (int i = 0; i < accessories.Count; i++)
            {
                if (!(accessories[i] is ItemName accessory)) continue;
                int counter = equipment.GetAccessoryDurability(i); // 槽位计数器：耐久件=剩余耐久/堆叠件=层数
                equipment.UnequipAccessory(i);
                segment.commands.Add(BattleCommand.UnequipItem(deadId, sliceIndex, indexInSlice++,
                    (int)accessory, BattleCommand.EquipKindAccessory, i));
                if (UnitEquipment.IsStackAccessory(accessory) && counter > 0)
                {
                    _sim.GainCard(killer, new CardId(accessory), counter); // N 层=N 枚（新鲜解析——堆叠件无 Durability 参数，条目耐久恒 0）
                    segment.commands.Add(BattleCommand.ItemGain(killer, sliceIndex, indexInSlice++,
                        (int)accessory, counter, 0));
                    GICLog.Info($"[掠夺] {deadId} 的 {accessory}（槽 {i}，{counter} 层拆 {counter} 枚）→ {killer} 手牌");
                }
                else
                {
                    _sim.GainCard(killer, new CardId(accessory), 1, counter);
                    segment.commands.Add(BattleCommand.ItemGain(killer, sliceIndex, indexInSlice++,
                        (int)accessory, 1, counter));
                    GICLog.Info($"[掠夺] {deadId} 的 {accessory}（槽 {i}，耐久 {counter}）→ {killer} 手牌");
                }
            }
        }

        /// <summary>击杀方归属（同段死亡归因——扫描已下沉 KillCredit.LastDamagerBefore 单源，
        /// 2026-10-20 审查批#7：与 OpponentProfiler 核心摧毁记账共用同一实现）：段命令流内该单位
        /// 最近一次 Damage 命令的 actorUnitId → 该单位所属玩家。攻击者已被击倒仍可归属（尸体归属
        /// 玩家不变——掠夺归玩家）；无 Damage 命令/攻击者不存在/无主人=返回 null</summary>
        private string KillerPlayerOf(string deadId, Segment segment)
        {
            var damagerId = KillCredit.LastDamagerBefore(segment.commands, segment.commands.Count, deadId);
            if (damagerId == null) return null;
            var attacker = _sim.GetUnit(damagerId);
            return attacker?.GetUnitComponent<UnitIdentity>()?.OwnerPlayerID;
        }

        /// <summary>
        /// 效应统一应用（伤害/治疗/Buff 施加）；返回已施 Buff 列表（含合并后的级别与剩余秒，供命令产出）。
        /// 元能两段应用（2026-09-25 用户拍板「先扣除，再加」）：同段多来源元能效应先消耗后获取——
        /// 获取先应用会被 baseEnergy 上限钳位吞掉（30+10→40 钳 30，再 −20=10 ≠ 期望 30−20+10=20）。
        /// shatters=寒冰之棱碎裂记录（2026-10-01 复测拍板「元能&gt;50% 立刻触发」）：每笔元能增益
        /// 落地后即时判定（IcicleBuff.TryShatter 内联应用治疗+移除），事实经本列表传给命令出口
        /// 补发 RemoveBuff——由调用方创建传入（片/秒拍结束/即时段三处各自持有）。
        /// 2026-10-15 追加：元能消耗落地后同拍判定（拍板「施放冰棱若立刻过阈应当立刻破碎」——
        /// 3命满能施放残留 60 > 50% 时刚挂上的冰棱立即碎裂，勿等下一笔获能）。
        /// </summary>
        private List<ApplyBuffEffect> ApplyEffects(List<BattleEffect> effects,
            List<(string holderId, string sourceId, BaseBuff buff)> shatters)
        {
            var appliedBuffs = new List<ApplyBuffEffect>();
            List<EnergyEffect> energyCosts = null, energyGains = null;
            // 吸血治疗收集（2026-10-01 实装：DamageEffect 应用点按攻击者 LifeSteal% 结算——状态主循环内
            // 直改 ApplyHeal，效应主循环后并入 effects=命令发射载体（同溢出转移先例：主循环 foreach
            // 中 List.Add 会炸枚举器，故先收后并）；客户端随伤害 +N 绿字弹出）
            var lifestealHeals = new List<HealEffect>();
            // 寒冰之棱溢出碎裂治疗收集（2026-10-15 拍板「溢出的层数立刻破碎」）：主循环 ApplyBuff 分支
            // 收集，循环后并入 effects=命令发射载体（同吸血先例防炸枚举器）
            var overflowShatters = new List<HealEffect>();
            // 装备格挡耐久变化收集（装备批 2026-10-18·兔兔伯爵）：DamageEffect 应用点消费 TryConsumeBlock
            // 产出，循环后并入 effects=命令发射载体（同吸血先例）——EquipItem 同槽重发=角标更新
            //（**决策八十一：归零留槽停效，不再有 UnequipItem 路径**）
            var equipBlockUpdates = new List<EquipEffect>();
            // 耐久爆炸收集（兔兔伯爵增强·决策八十一「耐久每次减少，对半径1内敌人造成100%攻击力火伤」）：
            // 格挡点收集 (持有者,命中时刻,爆炸%,半径)，主循环后统一编译+递归应用（防枚举器炸，
            // 同 equipBlockUpdates 先例）；编译走 CompileElementalDamage 单出口=反应/附着/暴击全链
            var bunnyExplosionBlocks = new List<(Unit holder, string holderId, float hitSeconds, int percent, int radius)>();
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

                // 摩拉消耗（统一消耗模型 C-1，玩家级）：TrySpendMora 池写——不足理论不可达（门槛先行+
                // 每玩家每秒拍单行动），防御性钳制 Warn 记账（AppliedAmount=0 零命令，对账按 0 非漏发）
                if (effect is MoraSpendEffect moraSpend)
                {
                    int applied = _sim.GetMora(moraSpend.TargetUnitId) >= moraSpend.Amount
                        ? moraSpend.Amount : _sim.GetMora(moraSpend.TargetUnitId);
                    if (applied < moraSpend.Amount)
                        GICLog.Warn($"[BattleResolver] 摩拉消耗效应不足额（{moraSpend.TargetUnitId} 池" +
                                    $"{_sim.GetMora(moraSpend.TargetUnitId)}/{moraSpend.Amount}）——实际扣 {applied}");
                    moraSpend.AppliedAmount = applied;
                    if (applied > 0)
                        _sim.TrySpendMora(moraSpend.TargetUnitId, applied);
                    continue;
                }

                // 摩拉获取（docs/47 资源点批，2026-10-27——MoraSpend 获取侧对偶）：玩家级池写
                // （ApplyMoraDelta 含手牌货币条目同步）；TargetUnitId=玩家 ID。资源点采集（站立/
                // 途经两路径）的共用产出原子，未来资源产出族同用
                if (effect is MoraGainEffect moraGain)
                {
                    _sim.ApplyMoraDelta(moraGain.TargetUnitId, moraGain.Amount);
                    continue;
                }

                // 物品消耗（统一消耗模型 C-1，玩家级）：LoseCard 手牌条目真源扣减（减尽移除）——
                // 不足理论不可达（门槛先行+每玩家每秒拍单行动）；指定模式=单条 LoseCard；
                // 同类任意模式=按手牌列表序逐条凑量（零随机，ResourceGate 原语）。
                // **原子性**：防御路径未足额=GainCard 回滚已扣部分（获得/失去对偶）→零命令（语义=支付要么全额要么不支付）
                if (effect is ItemConsumeEffect itemConsume)
                {
                    int remaining = itemConsume.Amount;
                    itemConsume.Consumed.Clear();
                    if (itemConsume.AnyOfSubType)
                    {
                        foreach (var anyPair in ResourceGate.CollectAnyItems(
                                     _sim, itemConsume.TargetUnitId, (ItemSubType)itemConsume.SubType))
                        {
                            if (remaining <= 0) break;
                            int take = System.Math.Min(remaining, anyPair.count);
                            if (_sim.LoseCard(itemConsume.TargetUnitId, new CardId(anyPair.item), take,
                                    anyPair.durability))
                            {
                                itemConsume.Consumed.Add(((int)anyPair.item, take, anyPair.durability));
                                remaining -= take;
                            }
                        }
                    }
                    else
                    {
                        if (_sim.LoseCard(itemConsume.TargetUnitId,
                                new CardId((ItemName)itemConsume.Item), itemConsume.Amount, itemConsume.Durability))
                        {
                            itemConsume.Consumed.Add((itemConsume.Item, itemConsume.Amount, itemConsume.Durability));
                            remaining = 0; // 指定模式成功即足额——2026-10-16 复测 dump 实锤根因：此处漏减
                                           // remaining → 「remaining>0 恒真」→ 原子回滚把刚扣的还回+零命令
                                           // =「打出后数量不扣」；C-1 批既有地雷，AnyItem 分支正确、
                                           // UseItemExecutor 是指定模式首个消费者而暴露
                        }
                    }

                    if (remaining > 0)
                    {
                        // 原子性回滚：未足额=把已扣部分如数还回（GainCard=获得对偶，含条目复活；
                        // 实例耐久随明细还回——装备异耐久条目不串档）
                        foreach (var spent in itemConsume.Consumed)
                            _sim.GainCard(itemConsume.TargetUnitId, new CardId((ItemName)spent.item), spent.count,
                                spent.durability);
                        itemConsume.Consumed.Clear();
                        itemConsume.AppliedAmount = 0;
                        // 防御路径诊断（2026-10-16 报障「打出物品数量没扣减」曾在此 dump 实锤指定模式
                        // 漏减 remaining 根因已修；dump 保留——再触发即真实不足额（count 不足/形态漂移），
                        // [cardType:value:count] 逐条手牌形态可直接定位）
                        var dumpHand = new System.Text.StringBuilder();
                        var dumpList = _sim.GetHand(itemConsume.TargetUnitId);
                        if (dumpList == null) dumpHand.Append("手牌未注册");
                        else
                        {
                            foreach (var hc in dumpList)
                            {
                                if (dumpHand.Length > 0) dumpHand.Append(", ");
                                dumpHand.Append($"[{hc.cardType}:{hc.value}x{hc.count}" +
                                                (hc.durability > 0 ? $"耐{hc.durability}" : "") + "]");
                            }
                            if (dumpHand.Length == 0) dumpHand.Append("空手牌");
                        }
                        GICLog.Warn($"[BattleResolver] 物品消耗效应未足额（{itemConsume.TargetUnitId} " +
                                    $"{(itemConsume.AnyOfSubType ? ((ItemSubType)itemConsume.SubType).ToString() : ((ItemName)itemConsume.Item).ToString())}" +
                                    $"×{itemConsume.Amount}，差 {remaining}）——原子回滚零命令（手牌形态：{dumpHand}）");
                    }
                    else
                    {
                        itemConsume.AppliedAmount = itemConsume.Amount;
                    }
                    continue;
                }

                // 元素×地形（决策七十六）：地形效应**无单位载荷**——必须在单位解析守卫之前处理
                //（TargetUnitId=null 会被下方 `target==null → continue` 静默跳过=Host 地图突变
                // 不生效、只剩客户端命令自改本地图的「半生效」态：冰面板可见、Host 移动仍判水面
                // 不可入——2026-10-15 报障「无法走到薄冰上」根因）。同体力/摩拉类玩家级效应先例位。
                if (effect is TerrainChangeEffect terrain)
                {
                    // Host 真值突变——移动 BFS/AI 预判/部署/瞄准全链经 GetTile/IsPassable 自动感知；
                    // 命令层 TerrainChange 在 EmitSliceCommands 发射（客户端本地 map 同步+刷冰面板）
                    _sim.Map.SetTile(terrain.Cell.x, terrain.Cell.y, (TileType)terrain.NewTile);
                    continue;
                }

                var target = _sim.GetUnit(effect.TargetUnitId);
                if (target == null) continue;

                if (effect is DamageEffect damage)
                {
                    // 装备配件格挡（装备批 2026-10-18·兔兔伯爵）：活体单位受到 ≥BlockThreshold 的伤害
                    // 被完全抵挡——耐久 -1（**决策八十一：归零留槽停效不销毁**——撤词条、槽位保留），
                    // Amount 保留原值（命令层「格挡」文案尺寸用）但应用侧跳过扣血与吸血；
                    // **尸体不触发**（HP 恒 0 无可保护，鞭尸不白烧耐久）。
                    // 耐久变化经 equipBlockUpdates 收集、循环后并入 effects（同吸血先收后并——主循环
                    // foreach 中 List.Add 炸枚举器）→命令层发 EquipItem 同槽重发=角标更新；
                    // **耐久爆炸（兔兔伯爵增强·决策八十一）**：ExplodePercent>0 时耐久减少触发
                    // 「对半径内敌人 %攻击力火伤」——编译点同样循环后统一处理（bunnyExplosionBlocks）
                    if (damage.Amount > 0 && damage.BlockedByItem == 0 && !BattleSimState.IsDead(target))
                    {
                        var equipment = target.GetUnitComponent<UnitEquipment>();
                        if (equipment != null && equipment.TryConsumeBlock(damage.Amount, out var block))
                        {
                            damage.BlockedByItem = block.Item;
                            string blockOwner = target.GetUnitComponent<UnitIdentity>()?.OwnerPlayerID
                                                ?? effect.TargetUnitId;
                            equipBlockUpdates.Add(new EquipEffect(blockOwner, effect.TargetUnitId, block.Item)
                            {
                                SlotKind = block.SlotKind,
                                SlotIndex = block.SlotIndex,
                                DurabilityLeft = block.DurabilityLeft,
                                Applied = true,
                            });
                            // 耐久爆炸收集（兔兔伯爵·决策八十一）：持有者/命中时刻/参数——循环后编译
                            var blockItemData = Wargame.Instance?.Context?.Get<ItemConfig>()
                                ?.GetItemData((ItemName)block.Item);
                            int explodePercent = blockItemData != null
                                ? blockItemData.GetInt(nameof(SkillParamKey.ExplodePercent), 0) : 0;
                            if (explodePercent > 0)
                            {
                                int explodeRadius = blockItemData.GetInt(nameof(SkillParamKey.ExplodeRadius), 100); // cm（P3 资产 cm 制：缺省=100=旧 1 格直译）
                                bunnyExplosionBlocks.Add((target, effect.TargetUnitId,
                                    damage.HitSeconds, explodePercent, explodeRadius));
                            }
                        }
                    }

                    // 尸体 HP 恒 0，继续扣无意义但保持链路统一（属性保留）；格挡=跳过扣血
                    if (damage.BlockedByItem == 0)
                        _sim.ApplyDamage(target, damage.Amount);

                    // 吸血（2026-10-01 实装——此前 LifeSteak 只有数据位零消费，用户问「当前吸血还没
                    // 实际生效吗」实证未生效）：攻击者存活且吸血>0 → 按实际伤害量×LifeSteak% 回血
                    // （一切伤害结算点统一结算：战技/箭矢/爆发/光环 tick/燃烧；末点 FloorToInt；
                    // **治疗效率统一乘区**〔2026-10-01 拍板「治疗效率应当对所有的回血生效，无论
                    // 吸血还是被治疗」——受疗者=攻击者自身，EffectCompiler.ApplyHealEfficiency 单出口〕；
                    // 尸体攻击者不吸；**格挡伤害不吸**——装备批：被完全抵挡的攻击无实际伤害可吸）
                    var lifestealAttacker = _sim.GetUnit(damage.AttackerUnitId);
                    if (damage.BlockedByItem == 0
                        && lifestealAttacker != null && !BattleSimState.IsDead(lifestealAttacker))
                    {
                        int lifestealPercent = lifestealAttacker.GetUnitComponent<UnitStats>()?.LifeSteal ?? 0;
                        if (lifestealPercent > 0 && damage.Amount > 0)
                        {
                            int heal = EffectCompiler.ApplyHealEfficiency(lifestealAttacker, lifestealAttacker,
                                Mathf.FloorToInt(damage.Amount * lifestealPercent / 100f));
                            if (heal > 0)
                            {
                                string attackerIdForHeal = damage.AttackerUnitId;
                                if (_sim.TryGetUnitId(lifestealAttacker, out var lsId)) attackerIdForHeal = lsId;
                                _sim.ApplyHeal(lifestealAttacker, heal);
                                // +N 应用时刻继承伤害命中时刻（2026-10-01 WYSIWYG 修复）：投射物=接触 hitT
                                // 与箭落地同拍弹；tick 节拍走 LaunchMs 兜底。同源同刻合并（三次拍板终版
                                // 「修改吸血，同源同刻合并」）：IsLifesteal=客户端「吸血 +N」名前缀标记
                                // （不再豁免合并——吸血与普通治疗同走 (来源,目标,毫秒) 键，霜袭双敌并单
                                // 「吸血 +60」；异源恒不并）
                                lifestealHeals.Add(new HealEffect(attackerIdForHeal, attackerIdForHeal, heal)
                                {
                                    IsLifesteal = true,
                                    HitSeconds = damage.HitSeconds > 0f
                                        ? damage.HitSeconds
                                        : damage.LaunchMs / 1000f,
                                });
                            }
                        }
                    }
                }
                else if (effect is HealEffect heal)
                {
                    _sim.ApplyHeal(target, heal.Amount);
                }
                else if (effect is SoulDecayEffect soulDecay)
                {
                    // 地脉灵魂衰减（决策九十一）：直扣 HP（非伤害——无防御/暴击/反应/格挡；
                    // 灵魂无装备无 Buff）。HP≤0 由死亡循环「灵魂+尸体双双消散」分支统一处理
                    _sim.ApplyDamage(target, soulDecay.Amount);
                }
                else if (effect is ReviveEffect revive)
                {
                    // 复苏（B-3 ②）：清尸体态+治疗——docs/05 §5.4「血量永远0不复苏」唯一例外通道。
                    // 编译层 condition=TargetIsCorpse 已保证目标为尸体；活体目标=防御性 no-op 零命令
                    //（对账按「应用前非尸体」豁免——ReviveEffect 仅在此处回填 Applied 标记）
                    if (BattleSimState.IsDead(target))
                    {
                        target.GetUnitComponent<UnitStatus>()?.SetStatus(StatusType.Dead, false);
                        _sim.ApplyHeal(target, revive.HealAmount);
                        revive.Applied = true;
                        // 灵魂归体消散（决策九十一）：尸体复苏→所链灵魂移除（「同一实体的两半」系统级
                        // 联动——尸体在主世界被复苏、灵魂直接归体，无跨界交互）；RemovedSoulId 回传
                        // 命令层随 Revive 命令补发 Death(dissolve)（客户端灵魂淡出）
                        revive.RemovedSoulId = _sim.RemoveLinkedSoul(effect.TargetUnitId);
                    }
                }
                else if (effect is EquipEffect equip)
                {
                    // 装备效应（装备批 2026-10-18）：执行器已过全部校验，此处挂槽+词条并回填槽位/耐久
                    //（命令层 EquipItem 载荷）；**Applied=true 的耐久更新实例**（格挡路径构造）直接跳过
                    // ——状态已由 TryConsumeBlock 内联变更。防御：槽满/缺组件=零命令（卡已消耗的极端
                    // 落空——执行器槽位校验后同段无第二装备可挤占，理论不可达）。
                    // **决策八十一**：Replenish=true=已装备同物品→补充耐久（既有槽位相加封顶、跨 0
                    // 恢复重挂词条、永不双槽）；IncomingDurability=实例耐久安装（掠夺物保留原耐久）
                    if (equip.Applied) continue;
                    var equipment = target.GetUnitComponent<UnitEquipment>();
                    var itemData = Wargame.Instance?.Context?.Get<ItemConfig>()?.GetItemData((ItemName)equip.Item);
                    if (equipment == null || itemData == null)
                    {
                        GICLog.Warn($"[BattleResolver] 装备效应落空（{effect.TargetUnitId} 缺组件或物品 {(ItemName)equip.Item} 无配置）");
                        continue;
                    }
                    int incoming = equip.IncomingDurability;
                    if (equip.Replenish)
                    {
                        bool replenished;
                        if (itemData.subType == ItemSubType.Weapon)
                        {
                            replenished = equipment.ReplenishWeapon((ItemName)equip.Item, incoming, out int weaponLeft);
                            equip.SlotKind = BattleCommand.EquipKindWeapon;
                            equip.SlotIndex = 0;
                            equip.DurabilityLeft = weaponLeft;
                        }
                        else
                        {
                            replenished = equipment.ReplenishAccessory((ItemName)equip.Item, incoming, out int slot,
                                out int accessoryLeft, out _);
                            equip.SlotKind = BattleCommand.EquipKindAccessory;
                            equip.SlotIndex = slot;
                            equip.DurabilityLeft = accessoryLeft;
                        }
                        equip.Applied = replenished;
                        continue;
                    }
                    bool equipped;
                    if (itemData.subType == ItemSubType.Weapon)
                    {
                        equipped = equipment.EquipWeapon((ItemName)equip.Item, incoming);
                        equip.SlotKind = BattleCommand.EquipKindWeapon;
                        equip.SlotIndex = 0;
                        equip.DurabilityLeft = equipment.WeaponDurability;
                    }
                    else
                    {
                        equipped = equipment.EquipAccessory((ItemName)equip.Item, out int slot, incoming);
                        equip.SlotKind = BattleCommand.EquipKindAccessory;
                        equip.SlotIndex = slot;
                        equip.DurabilityLeft = equipment.GetAccessoryDurability(slot);
                    }
                    equip.Applied = equipped;
                }
                else if (effect is EnergyEffect energy)
                {
                    if (energy.Delta < 0) (energyCosts ??= new List<EnergyEffect>()).Add(energy);
                    else (energyGains ??= new List<EnergyEffect>()).Add(energy);
                }
                else if (effect is ApplyBuffEffect applyBuff)
                {
                    // 带参数通道的 Buff（B-S1b：value/stackLimit/turns 全由技能参数单源注入；
                    // 工厂 2026-10-02 收口单一 Create：参数型类型缺 turns=Warn+null 防御，恒等于原双分支）。
                    // 决策五十七「资产即身份」：buffKey 优先（具名 buff 构造+同族异名共存）；空=族首资产回落
                    BaseBuff buff = BuffFactory.Create((BuffType)applyBuff.BuffType, applyBuff.Level,
                        _sim.GetUnit(applyBuff.SourceUnitId), applyBuff.BuffValue, applyBuff.StackLimit,
                        applyBuff.DurationSeconds, applyBuff.BuffKey);
                    if (buff == null) continue;
                    buff.SourceSkillId = applyBuff.SourceSkillId; // 来源技能随 Buff 实体进注册表（快照透传）

                    // 寒冰之棱溢出层预采集（2026-10-15 拍板「溢出的层数立刻破碎」）：满层再施放时
                    // Merge 钳上限吸收不了的召唤层不再静默消失，按碎裂口径逐枚弹治疗（30% 施加者攻
                    // ——碎裂治疗同口径）。施放前快照在场层数，吸收数=施加后−施加前
                    int icicleIncoming = 0, icicleBefore = 0;
                    if (buff is IcicleBuff)
                    {
                        icicleIncoming = buff.Level; // 工厂产出含命座召唤增补（0命 2/2命 3/3命 4）
                        var existingIcicle = target.Buffs.Find(b => b is IcicleBuff);
                        icicleBefore = existingIcicle != null ? existingIcicle.Level : 0;
                    }

                    _sim.ApplyBuff(target, buff, _sim.GetUnit(applyBuff.SourceUnitId));

                    // 回填合并后的真实状态（同身份叠层时 Level/Seconds 以注册表为准——按 buffKey 匹配
                    // 应用到的实例；key 空（防御）回落 type 匹配）
                    var state = !string.IsNullOrEmpty(applyBuff.BuffKey)
                        ? target.Buffs.Find(b => b.Config != null && b.Config.name == applyBuff.BuffKey)
                        : target.Buffs.Find(b => b.Type == (BuffType)applyBuff.BuffType);
                    applyBuff.Level = state != null ? state.Level : applyBuff.Level;
                    applyBuff.Seconds = state != null ? state.RemainingSeconds : 0;
                    appliedBuffs.Add(applyBuff);

                    // 溢出层立即碎裂：未吸收层逐枚弹治疗（LayerBeatSeconds 错峰同碎裂路径）；
                    // 状态=总值内联应用（HealEffect 仅命令载体，主循环后并入防炸枚举器——同吸血先例）；
                    // 倒下守卫=防御性（死者无行动不能施放，理论不可达——与 TryShatter 倒下同判新
                    // 口径不冲突：溢出碎裂主体=施放者本人必存活）；全新施加（在场 0 层）恒无溢出零回归
                    if (icicleIncoming > 0 && state is IcicleBuff && !BattleSimState.IsDead(target))
                    {
                        int absorbed = Mathf.Max(0, state.Level - icicleBefore);
                        int overflow = icicleIncoming - absorbed;
                        if (overflow > 0 && state.Config is IcicleBuffConfig icicleCfg)
                        {
                            var shatterSource = _sim.GetUnit(applyBuff.SourceUnitId);
                            var attackerStats = shatterSource != null
                                ? shatterSource.GetUnitComponent<UnitStats>() : null;
                            int attack = attackerStats != null ? attackerStats.Attack : 0;
                            int healPerShard = EffectCompiler.ApplyHealEfficiency(shatterSource, target,
                                Mathf.FloorToInt(attack * icicleCfg.碎裂治疗百分比 / 100f));
                            if (healPerShard > 0)
                            {
                                _sim.ApplyHeal(target, healPerShard * overflow);
                                string holderId = target.GetUnitComponent<UnitIdentity>()?.UnitID
                                    ?? applyBuff.TargetUnitId;
                                for (int layer = 0; layer < overflow; layer++)
                                    overflowShatters.Add(new HealEffect(applyBuff.SourceUnitId, holderId,
                                        healPerShard)
                                    {
                                        HitSeconds = BattleMetrics.LayerBeatSeconds(layer), // 逐层错峰单源
                                    });
                                GICLog.Info($"[IcicleBuff] {holderId} 寒冰之棱溢出碎裂（满层施放，" +
                                    $"{overflow} 枚各治疗 {healPerShard}）——合计 {healPerShard * overflow}");
                            }
                        }
                    }
                }
                else if (effect is SanityEffect sanity)
                {
                    // 理智恢复（2026-09-30 歌声之环批）：RangedInt Add 自动钳 -300~300（到上限自然停涨）
                    _sim.ApplySanity(target, sanity.Delta);
                }
                else if (effect is AttachElementEffect attach)
                {
                    // 覆盖=消耗被反应附着（docs/06）；层数经 Layers（决策七十四多层铺路——
                    // 反应消耗后的剩余层/多层附着原子落此）
                    _sim.AttachElement(target, (ElementType)attach.Element, attach.Layers);
                }
                else if (effect is ReactionEffect reaction)
                {
                    // 反应消耗的真实状态落点（决策二十五：反应 1:1 双方全消耗、等层零残留——
                    // 被反应附着清除；此前靠「附着覆盖」近似消耗，反应命中不再附着后必须显式清）
                    // 决策七十四按层消耗：多层剩余层仍附着（ConsumeDyeLayers 内置等层=清）；
                    // 冻结层路径 ConsumedLayers=0 且附着本空 → Physical 守卫 no-op
                    _sim.ConsumeDyeLayers(target, reaction.ConsumedLayers);
                }
                else if (effect is ConsumeFreezeEffect consumeFreeze)
                {
                    // 冻结层消耗（决策七十四「冻结 Buff 可作为冰元素参与反应」——每秒相当于 1 层冰）：
                    // 融化/超导按反应级别消耗冻结秒；**归零立即解冻**（拍板：_sim.RemoveBuff 注销+清
                    // Frozen 状态〔OnRemoved〕→命令层发 RemoveBuff=霜化即时退场）；未归零=零命令
                    //（冻结徽章秒数靠下一拍快照自愈，Frost 表现由 Frozen 状态驱动仍正确）
                    var freeze = _sim.GetFreezeBuff(consumeFreeze.TargetUnitId);
                    if (freeze == null)
                    {
                        consumeFreeze.AppliedSeconds = 0; // 编译后冻结已被外部移除的防御路径（零命令零动作）
                    }
                    else
                    {
                        consumeFreeze.AppliedSeconds = Mathf.Min(consumeFreeze.Seconds,
                            Mathf.Max(0, freeze.RemainingSeconds));
                        freeze.RemainingSeconds -= consumeFreeze.AppliedSeconds;
                        if (freeze.RemainingSeconds <= 0)
                        {
                            consumeFreeze.AppliedKey = freeze.Config?.name; // 具名身份（决策五十七）——RemoveBuff 命令载荷
                            _sim.RemoveBuff(target, freeze); // OnRemoved 清 Frozen 状态=归零即解冻
                            consumeFreeze.Depleted = true;
                        }
                    }
                }
            }

            // 吸血治疗效应并入（状态已在主循环内联应用——此处仅为命令发射载体，同溢出转移先例；
            // 主循环 foreach 内直接 Add 会炸枚举器，故先收后并）
            effects.AddRange(lifestealHeals);
            effects.AddRange(overflowShatters); // 寒冰之棱溢出碎裂治疗（状态已内联，同上仅命令载体）
            effects.AddRange(equipBlockUpdates); // 装备格挡耐久变化（状态已内联〔TryConsumeBlock 消耗〕，命令载体）

            // 耐久爆炸（兔兔伯爵增强·决策八十一）：收集的格挡事件统一编译——持有者半径 radius 内
            // 存活敌人逐个 CompileElementalDamage（100%参数化攻击力火伤；命中时刻=被抵挡攻击的
            // HitSeconds=WYSIWYG 同拍爆炸）；编译产物**递归 ApplyEffects 应用**（伤害/反应/吸血/
            // 嵌套格挡〔敌方兔兔伯爵连环爆炸，耐久池天然递减终止〕全链）后并入主列表
            //（命令发射+死亡收集载体）。explodedTargets 空集=零动作零命令
            if (bunnyExplosionBlocks.Count > 0)
            {
                var explosionEffects = new List<BattleEffect>();
                foreach (var explosion in bunnyExplosionBlocks)
                {
                    var holderPos = _sim.GetPosition(explosion.holder);
                    string holderOwnerId = explosion.holder.GetUnitComponent<UnitIdentity>()?.OwnerPlayerID;
                    if (holderOwnerId == null) continue; // 无归属防御（理论不可达——活体格挡者必有主人）
                    var holderTeam = _sim.GetTeamOf(holderOwnerId);
                    foreach (var kv in _sim.SortedUnits())
                    {
                        var enemy = kv.Value;
                        if (BattleSimState.IsDead(enemy)) continue;
                        string enemyOwnerId = enemy.GetUnitComponent<UnitIdentity>()?.OwnerPlayerID;
                        if (enemyOwnerId == null || _sim.GetTeamOf(enemyOwnerId) == holderTeam) continue;
                        var enemyPos = _sim.GetPosition(enemy);
                        long explosionR = explosion.radius; // 欧氏平方（P3 统一资产 cm 制：ExplodeRadius 资产迁厘米〔1 格→100〕直比——不再 ×CellSizeCm）
                        if (holderPos.SquaredEuclideanCmTo(enemyPos) > explosionR * explosionR) continue;
                        EffectCompiler.CompileElementalDamage(_sim, explosion.holderId, kv.Key, null,
                            ElementType.Pyro,
                            new DamageRequest
                            {
                                Attacker = explosion.holder,
                                Target = enemy,
                                Element = (int)ElementType.Pyro,
                                AttackPercent = explosion.percent,
                            },
                            explosion.hitSeconds, explosion.hitSeconds, explosionEffects);
                    }
                }
                if (explosionEffects.Count > 0)
                {
                    appliedBuffs.AddRange(ApplyEffects(explosionEffects, shatters));
                    effects.AddRange(explosionEffects); // 命令发射+死亡收集载体（递归内已应用状态）
                }
            }

            // 元能第二阶段：先扣除后获取（与命令发射序 MergeEnergyEffects 同语义，2026-09-25 用户拍板「先扣除，再加」
            // ——获取先到会被 baseEnergy 上限钳位吞掉：30+10 钳 30 再 −20=10 ≠ 期望 20）。
            // (目标,类别) 去重=与 MergeEnergyEffects 命令合并口径恒等（2026-09-25 修复：逐条累加致状态背离命令——
            // 两发箭矢两条 +10 战技获能曾会状态 +20/命令 +10，B6a「多次命中只获一次」由此在状态层真正成立）
            // 寒冰之棱碎裂收集（复测拍板①「元能>50% 立刻触发」）：治疗/移除由 TryShatter 内联应用；
            // **逐层各一枚 HealEffect**（拍板「碎裂回血等效果各弹一次」——第 i 层 HitSeconds=i×
            // BuffLayerStaggerSeconds 错峰，客户端 launchMs 到点各弹 +N；状态=总值已应用，效应=
            // 命令发射载体同溢出转移先例）+碎裂事实（RemoveBuff 命令）；无 Buff/未过阈值零动作；
            // 倒下过阈同碎（2026-10-15 拍板「凯亚尸体的寒冰之棱会因此碎裂」——healPerShard=0
            // 自动零治疗命令，仅 RemoveBuff=尸体停止 tick）
            void CollectIcicleShatter(Unit unit)
            {
                if (unit == null) return;
                if (!IcicleBuff.TryShatter(_sim, unit, out var healPerShard, out var source, out var buff)) return;
                string holderId = unit.GetUnitComponent<UnitIdentity>()?.UnitID ?? "";
                string sourceId = (source != null ? source.GetUnitComponent<UnitIdentity>()?.UnitID : null) ?? holderId;
                if (healPerShard > 0)
                {
                    int layers = Mathf.Max(1, buff.Level);
                    for (int layer = 0; layer < layers; layer++)
                        effects.Add(new HealEffect(sourceId, holderId, healPerShard)
                        {
                            HitSeconds = BattleMetrics.LayerBeatSeconds(layer), // 逐层错峰单源（LayerBeatSeconds）
                        });
                }
                shatters.Add((holderId, sourceId, buff));
            }

            if (energyCosts != null || energyGains != null)
            {
                var appliedEnergy = new HashSet<string>();
                if (energyCosts != null)
                    foreach (var cost in energyCosts)
                    {
                        if (!appliedEnergy.Add(cost.MergeKey())) continue;
                        _sim.ApplyEnergy(_sim.GetUnit(cost.TargetUnitId), cost.Delta);
                        // 消耗落地同拍碎裂判定（2026-10-15 拍板「施放冰棱若立刻过阈应当立刻破碎」）：
                        // 扣除后仍严格过阈（3命满能施放：100−40=60 > 50%）→ 本段刚挂上的冰棱立刻碎裂；
                        // 扣除后回落阈值下（≤90 施放→残留 ≤50）零动作——与获能路径共用 TryShatter 单源
                        CollectIcicleShatter(_sim.GetUnit(cost.TargetUnitId));
                    }
                if (energyGains != null)
                    foreach (var gain in energyGains)
                    {
                        if (!appliedEnergy.Add(gain.MergeKey())) continue;
                        // 溢出转移（B8 批，安柏1命）：状态层转移后回传事实→补发转移效应命令（独立类别，
                        // 客户端到点跳接收方元能；发射层 MergeEnergyCommands 后续统一合并）
                        _sim.ApplyEnergy(_sim.GetUnit(gain.TargetUnitId), gain.Delta,
                            out var overflowReceiver, out var overflowAmount);
                        if (overflowAmount > 0 && !string.IsNullOrEmpty(overflowReceiver))
                        {
                            effects.Add(new EnergyEffect(overflowReceiver, overflowAmount,
                                EnergyEffect.CategoryOverflowTransfer));
                            CollectIcicleShatter(_sim.GetUnit(overflowReceiver)); // 溢出接收方元能已内联写入——同拍碎裂判定
                        }
                        CollectIcicleShatter(_sim.GetUnit(gain.TargetUnitId)); // 元能>50% 立刻碎裂（复测拍板①）
                        _buildingEffects.CollectEnergySummon(_sim.GetUnit(gain.TargetUnitId), gain.HitSeconds); // 元能引擎过阈即召（追记四）
                    }
            }
            return appliedBuffs;
        }

        // ==================== 效应→命令合并策略表（2026-09-27 复审收口）====================
        // 返修史集中于此族（docs/14 §79/§80 + 审查 R1/S4 全是合并键问题）——新增效应类型/调整合并
        // 口径时按此表核填，勿各处特判漂移；表内「先扣后加」=ApplyEffects 与命令发射序同语义。
        // | 效应            | 合并/去重键                          | 语义要点                                        |
        // |-----------------|--------------------------------------|------------------------------------------------|
        // | Damage          | (攻击者, 目标, launchMs 发射时刻)    | 逐发不并（时轮逐发各跳数字）；同发同目标多来源并 |
        // | Heal            | (来源, 目标, 命中毫秒)——全治疗统一 | 键含时刻对齐 Damage；同源同刻并跳（吸血同规则，2026-10-01 三拍终版）；异源恒不并 |
        // | Revive          | 不合并（一一对应）                    | Applied=false（活体）零命令；单命令=解灰+血量   |
        // | Energy          | (目标, 类别)——MergeKey() 单源；       | 同类别去重=「多次命中只获一次」；跨类别各发；     |
        // |                 | BuffTickGain 例外=键含层时刻          | 逐层 tick 各跳各弹（拍板④）；同刻同目标仍并     |
        // | Sanity          | 逐层各 +1 效应；命令按目标合并总值   | 环 tick 逐层（拍板②）；RangedInt 钳 -300~300 |
        // | Stamina         | (目标=玩家 ID) 防御性去重             | 配额行动理论唯一；重复条目丢弃                  |
        // | MoraPlunder     | (被掠方, 掠夺方) 双命令               | AppliedGain=0 零命令；按应用后实际量+命中时刻发 |
        // | ApplyBuff       | (目标, BuffType)                     | 级别取大、秒取后施合并态；命令数以合并态为准  |
        // | Attach/Reaction | 不合并（一一对应）                    | 覆盖语义后到者胜属预期                          |
        // 注：永久 Buff（RemainingSeconds<0，歌声之环）不走到期收集——倒下即失走死亡循环 RemoveBuff
        // =================================================================================

        /// <summary>同片同 (目标,类型) 的多次施加合并为一条命令（秒数以最终合并态为准）</summary>
        private static List<ApplyBuffEffect> MergeAppliedBuffs(List<ApplyBuffEffect> applied)
        {
            var merged = new Dictionary<string, ApplyBuffEffect>();
            var result = new List<ApplyBuffEffect>();
            foreach (var buff in applied)
            {
                // 合并键=目标+具名身份（决策五十七：异资产同族各成一条命令；key 空回落 type=旧路径）
                string key = $"{buff.TargetUnitId}:{buff.BuffKey ?? buff.BuffType.ToString()}";
                if (!merged.ContainsKey(key))
                {
                    merged[key] = buff;
                    result.Add(buff);
                }
                else
                {
                    merged[key].Level = Mathf.Max(merged[key].Level, buff.Level);
                    merged[key].Seconds = buff.Seconds; // 后施合并态覆盖
                }
            }
            return result;
        }

        /// <summary>地脉灵魂生成（决策九十一，2026-10-25）：死亡分支尾部调用（留尸体单位=伙伴/魔神）——
        /// UnitFactory 按同 UnitData 建**基础属性**灵魂（拍板「只继承基础属性，无装备、buff 等」：
        /// 工厂 Init=配置基值+0命被动，满血；无命座等级〔0命无徽章〕、无装备、无登场 Buff）；
        /// 治疗效率覆写 50（拍板「灵魂的治疗效率-50%」——受疗者效率乘区单出口对一切治疗自动减半；
        /// 协议核心 50 同款语义）；RegisterSoul=地脉注册表+尸体链接；Summon 命令复用（UnitState.isSoul
        /// 客户端建幽灵立牌）。同 UnitId 自增计数器=回放确定性。
        /// **位置独立（2026-10-25 拍板追记：灵魂与尸体是独立单位）**：生成=死亡拍一次性取尸体同格，
        /// 此后无任何同步链路——尸体被移动/牵引不带动灵魂；复苏归体也不读灵魂位置（尸体在哪复活在哪）
        /// </summary>
        private void SpawnSoul(Unit dead, string deadId, Segment segment, int sliceIndex, ref int indexInSlice)
        {
            var identity = dead.GetUnitComponent<UnitIdentity>();
            if (dead.RawData == null || identity == null) return;
            var soul = UnitFactory.CreateUnitWithData(dead.RawData);
            if (soul == null) return;
            var stats = soul.GetUnitComponent<UnitStats>();
            if (stats != null)
                stats.SetStatStruct(StatType.HealEfficiency,
                    new RangedInt(0, 300, BattleMetrics.SoulHealEfficiency)); // 满血由工厂 Init 保证（value=max）
            string soulId = _sim.RegisterSoul(soul, identity.OwnerPlayerID, identity.Team,
                _sim.GetPosition(dead), deadId);
            segment.commands.Add(BattleCommand.Summon(identity.OwnerPlayerID, sliceIndex, indexInSlice++,
                _sim.BuildUnitState(soulId, soul)));
            GICLog.Info($"[BattleResolver] 地脉灵魂生成：{dead.RawData.unitName}（{deadId}）→ 灵魂 {soulId}");
        }

        /// <summary>地脉维度秒拍规则（决策九十一；决策九十五拍板 4「每秒整拍」秒化）：①灵魂衰减——
        /// 每秒整拍 −⌊10%×MaxHp⌋（SoulDecayPercentPerSecond 单源，末点截断）=复苏窗直译 10 秒；
        /// 生成当秒拍豁免（DeployedSecond 判据〔BattleSecond 语义=逻辑秒〕，骑士团「部署当拍跳过」同构）；
        /// ②地脉驻留代价——非灵魂地脉单位 −3 理智/秒拍（LeyLineSanityCostPerSecond；当前地脉仅灵魂=
        /// 预留钩子自动生效，相位技能批上线即激活）。霜月等未来维度的驻留规则另立收集器勿并入本方法</summary>
        private void CollectLeyLineEffectBeatEffects(List<BattleEffect> effects)
        {
            foreach (var kv in _sim.CrossDimensionUnits)
            {
                var unit = kv.Value;
                if (unit == null || unit.Dimension != BattleDimension.LeyLine) continue;
                if (unit.IsSoul)
                {
                    if (_sim.BattleSecond <= unit.DeployedSecond) continue; // 生成当秒拍末不衰减
                    var stats = unit.GetUnitComponent<UnitStats>();
                    if (stats == null) continue;
                    var hp = stats.GetStatStruct(StatType.HP);
                    if (hp.Max <= 0) continue;
                    int decay = hp.Max * BattleMetrics.SoulDecayPercentPerSecond / 100;
                    if (decay > 0)
                        effects.Add(new SoulDecayEffect(kv.Key, decay));
                }
                else if (!BattleSimState.IsDead(unit))
                {
                    effects.Add(new SanityEffect(kv.Key, -BattleMetrics.LeyLineSanityCostPerSecond));
                }
            }
        }

        private List<Unit> CollectDamagedTargets(List<BattleEffect> effects)
        {
            var targets = new List<Unit>();
            var seen = new HashSet<string>();
            foreach (var effect in effects)
            {
                // SoulDecayEffect 一并入列（决策九十一）：灵魂衰减致 HP≤0 → 死亡循环「灵魂+尸体
                // 双双消散」分支（将来地脉内伤害致死灵魂同链）
                if ((effect is DamageEffect || effect is SoulDecayEffect)
                    && effect.TargetUnitId != null && seen.Add(effect.TargetUnitId))
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
                    // 暴击合并口径（2026-10-02 幸运暴击批）：任一段暴击即标暴——Amount 已含暴击
                    // 份额，标大更近真（非首条独裁；命中点/反应标记仍取首条不变）
                    existing.IsCrit |= damage.IsCrit;
                    // 装备格挡标记（装备批）：任一段被格挡即标——同键=同发同量伤害，段间格挡结局
                    // 恒一致（同 launch 同目标同量 → 同阈值比较），混合态实际不可达
                    if (damage.BlockedByItem != 0) existing.BlockedByItem = damage.BlockedByItem;
                }
                else
                {
                    // 命中点与反应标记取首条（同合并键合并时=最早一次接触的位置与反应）
                    var copy = new DamageEffect(damage.AttackerUnitId, damage.TargetUnitId, damage.Amount,
                        damage.Element, damage.Delivery, damage.FromCell, damage.HitPointX, damage.HitPointY,
                        damage.ReactionType, damage.LaunchMs)
                    { HitSeconds = damage.HitSeconds, IsCrit = damage.IsCrit, ProjectileHeightY = damage.ProjectileHeightY }; // 吸血治疗继承用（WYSIWYG）+暴击事实+箭高透传（2026-10-05 报障「调整箭矢高度没变化」根因：副本重建漏抄=箭高恒丢回落默认）
                    copy.BlockedByItem = damage.BlockedByItem; // 格挡标记透传（装备批）
                    merged[key] = copy;
                    result.Add(copy);
                }
            }
            return result;
        }

        /// <summary>
        /// 同片同 (来源,目标,命中时刻) 的多次治疗合并为一条命令（docs/22 §7.4：同片多伤害/治疗数值合并；
        /// 片内/即时段治疗命令发射——此前仅回合结束段发射，片内治疗对客户端不可见致双端血量背离）。
        /// 合并键含命中毫秒（2026-09-25 三轮审查 S4，对齐 MergeDamageEffects 键含 LaunchMs）：
        /// 同施法者同片同目标 OnCast(0=立即)+OnHit(延迟) 双治疗不再并成一条——并条时刻取首条会让
        /// 投射物落地治疗在片头瞬跳。
        /// **吸血同规则（2026-10-01 三次拍板终版「修改吸血，同源同刻合并」——推翻同日拍板 B 的
        /// 豁免）**：IsLifesteal 不再豁免——吸血与普通治疗同走 (来源,目标,毫秒) 键：同源同刻
        /// 并一数字（霜袭双敌=单「吸血 +60」，合并副本保留 IsLifesteal=前缀标记）；异源恒不并
        /// （两个芭芭拉的环各弹各的）。
        /// </summary>
        private static List<HealEffect> MergeHealEffects(List<BattleEffect> effects)
        {
            var merged = new Dictionary<string, HealEffect>();
            var result = new List<HealEffect>();
            foreach (var effect in effects)
            {
                if (!(effect is HealEffect heal)) continue;
                string key = $"{heal.SourceUnitId}->{heal.TargetUnitId}:{Mathf.RoundToInt(heal.HitSeconds * 1000f)}";
                if (merged.TryGetValue(key, out var existing))
                {
                    existing.Amount += heal.Amount;
                }
                else
                {
                    // 命中时刻随合并副本保留（同合并键内取首条=最早一次命中的时刻）；
                    // IsLifesteal 同保留——合并后的吸血仍带「吸血 +N」前缀
                    var copy = new HealEffect(heal.SourceUnitId, heal.TargetUnitId, heal.Amount)
                    {
                        HitSeconds = heal.HitSeconds,
                        IsLifesteal = heal.IsLifesteal,
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
                if (!seen.Add(energy.MergeKey())) continue;
                if (energy.Delta < 0) costs.Add(energy);
                else gains.Add(energy);
            }
            costs.AddRange(gains);
            return costs;
        }

        // ==================== 推送（即时制：ack 门控随回合机退役，docs/active/48 §2.4） ====================

        /// <summary>段直发（决策九十五）：录制+档案采集+HostSend 即时下发——无 ack 等待
        /// （SegmentAck 通道/15s 超时快进随回合机退役；命令流带 startTick 客户端按绝对时刻播，
        /// 客户端播放跟不上由渲染插值自然吸收，不再反压 Host——「Host 永不跑在客户端前面」
        /// 旧铁律随片级 ack 门控一并退役）</summary>
        private void PushSegment(Segment segment)
        {
            // 回放录制（2026-10-20 Replay harness 批）：命令流本体在推送前定稿——断言面单点
            BattleRecorder.CaptureSegment(segment);
            // 对手档案采集（AI-2 批，docs/active/42 §4）：段命令流观察——Damage→元素习惯/最爱
            // 目标/交战距离，Death(建筑)→核心摧毁事件账本（公开信息=全体客户端广播态，红线①合规）
            OpponentProfiler.ObserveCommands(_sim, segment.commands, segment.turnNumber);
            _transport.HostSend(BattleMessageType.Segment, new SegmentMessage { segment = segment });
        }
    }
}
