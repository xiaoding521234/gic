using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
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
        private readonly ITurnResolverHost _flow;

        /// <summary>骑士团总部元能引擎过阈待召队列（2026-10-24 决策八十六追记四）：(建筑 unitId, 过阈
        /// 获能命中时刻)——ApplyEffects 加能落地同拍入队（状态层已扣消耗额），EmitSliceCommands 尾
        /// DrainPendingBuildingSummons 统一发射消费命令+召唤命令（RegisterUnit 只在发射拍执行，
        /// 防应用循环内枚举突变——CollectBuildingEffects「先快照再处理」同族纪律）</summary>
        private readonly List<(string buildingId, float hitSeconds)> _pendingBuildingSummons
            = new List<(string buildingId, float hitSeconds)>();

        /// <summary>ack 等待超时（秒，真实时间；超时快进）——阈值真源=BattleMetrics.SegmentAckTimeoutSeconds
        /// （2026-10-02 复审收口：协议常量入 Metrics，与其它战斗阈值同址）</summary>
        private const float AckTimeoutSeconds = BattleMetrics.SegmentAckTimeoutSeconds;

        public TurnResolver(BattleSimState sim, IBattleTransport transport, ITurnResolverHost flow)
        {
            _sim = sim;
            _transport = transport;
            _flow = flow;
        }

        // ==================== 回合主循环 ====================

        public IEnumerator ResolveTurnCoroutine(int turnNumber, List<ActionData> actions)
        {
            // 回合计数真源写入（决策八十六批D，docs/45 §6——周期类建筑效果消费；
            // 回合开头写一次，段内 RegisterUnit 的单位 DeployedTurn 同回合自洽）
            _sim.CurrentTurn = turnNumber;
            // 部署段（B6c）：出战行动先于全部攻速片结算（新单位当回合即被后续行动波及；
            // 占玩家行动配额由上交通道保证——DeployUnit 同走 SubmitAction 一玩家一行动，docs/18 决策七）
            var deploys = actions.FindAll(a => a.actionType == ActionType.DeployUnit);
            bool hasDeploySegment = deploys.Count > 0;
            if (hasDeploySegment)
            {
                actions.RemoveAll(a => a.actionType == ActionType.DeployUnit);
                yield return PushSegmentAndWaitAck(ResolveDeploySegment(turnNumber, 0, deploys));
                if (_sim.IsBattleDecided()) yield break; // 核心片末检查（协议核心批）：战斗已分胜负口径（2026-10-24 勘正，docs/14 §171）
            }

            // 物品行动段（2026-10-16 首个可打出物品批；2026-10-18 装备批并 EquipItem）：玩家级行动
            //（unitId 留空）不进攻速分桶——先行摘出独立段结算（部署段之后：刚出战的单位当回合可被
            // 喂酒/装备；首个消费方=蒲公英酒，装备批加兔兔伯爵/武器）
            var itemActions = actions.FindAll(a => a.actionType == ActionType.UseItem
                                                   || a.actionType == ActionType.EquipItem);
            bool hasUseItemSegment = itemActions.Count > 0;
            if (hasUseItemSegment)
            {
                actions.RemoveAll(a => a.actionType == ActionType.UseItem || a.actionType == ActionType.EquipItem);
                int useItemSlice = hasDeploySegment ? 1 : 0; // 紧随部署段占号
                yield return PushSegmentAndWaitAck(ResolveItemActionsSegment(turnNumber, useItemSlice, itemActions));
                if (_sim.IsBattleDecided()) yield break; // 防御性同口径（物品效果理论上可含伤害；战斗已分胜负口径 docs/14 §171）
            }

            // 玩家级 Pass（空 unitId：超时自动空过/AI 无单位 Pass）无单位归属——分桶前静默剔除，
            // 勿落进 BucketByAttackSpeed 的「行动引用了不存在的单位」Warn（2026-09-25 审查 S4）
            actions.RemoveAll(a => a.actionType == ActionType.Pass && string.IsNullOrEmpty(a.unitId));

            // 攻速分桶：攻速值完全相同才同片；片按攻速降序（先结算高攻速）
            var buckets = BucketByAttackSpeed(actions);
            int maxSpeed = 0;
            if (buckets.Count > 0)
                maxSpeed = buckets[0].speed;

            // 行动预告（2026-09-29 执行预览拍板）：分桶后、首片推送前下发——与后续攻速片一一对应；
            // 部署段（已抽出）/玩家级 Pass（已剔除）天然不入表；单位级 Pass=无行动不入（预览只收
            // 真行动）；attackSpeed 取分桶同源 UnitStats 活值。客户端据此建多行执行预览（攻速 10 点
            // 分区间成行，HUD 侧归组——Host 仍按精确攻速分片结算，两套分组互不干扰）
            SendTurnPlan(turnNumber, actions);

            int sliceIndex = (hasDeploySegment ? 1 : 0) + (hasUseItemSegment ? 1 : 0); // 部署/使用物品段占前置号（ack 键 turn:slice 唯一性）
            foreach (var bucket in buckets)
            {
                var segment = ResolveSlice(turnNumber, sliceIndex, bucket.speed, maxSpeed, bucket.actions);
                yield return PushSegmentAndWaitAck(segment);
                sliceIndex++;
                // 片末核心检查（协议核心批 2026-09-29 拍板「片末检查立即停」；2026-10-24 勘正为
                // 战斗已分胜负口径 IsBattleDecided）：恰好一队核心存活→剩余片不再结算与推送
                // （MOBA 观感：终局核心倒下即收尾）；**单玩家核心倒下≠终局**（2v2 队友核心仍在=
                // 战斗继续、剩余片照常结算——旧 AnyCoreDestroyed 单玩家口径会把此后每回合截断在
                // 首个攻速片=速度 30/10 档/即时队列/回合末段全停摆，docs/14 §171）；
                // 胜负广播由 TurnFlowController 既有 CheckBattleOver 位置接住（口径同构；
                // 顺序铁律不变：先阶段事件再 BattleOver）
                if (_sim.IsBattleDecided()) yield break;
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
                    if (_sim.IsBattleDecided()) yield break; // 即时段同口径（战斗已分胜负口径 docs/14 §171）
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

            // 片前快照（瞬发效应读取状态）+ 片内附着编译视图（2026-10-01 双蒸发修复）：
            // 同片多次命中的反应消耗/新附着随编译序对后续命中可见——快照只是附着初值
            var snapshot = _sim.TakeSnapshot(turnNumber);
            _sim.BeginCompileDyeView();

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
                        // 移动消耗（统一消耗模型 C-2）：移动技能 costs 单源（MoveExecutor.GetMoveCosts——
                        // 无 Move 条目单位走常量兜底）；低级单位豁免（ResourceGate 口径）；不足→移动落空
                        var moveCosts = MoveExecutor.GetMoveCosts(unit);
                        if (!ResourceGate.HasAll(_sim, unit, action.playerId, moveCosts, out _,
                                isPlayerDirectControl: action.isPlayerDirectControl))
                        {
                            GICLog.Info($"[TurnResolver] 单位 {action.unitId} 移动消耗不足，移动落空");
                            break;
                        }
                        ResourceGate.ChargeAll(_sim, unit, action.playerId, moveCosts, effects,
                            isPlayerDirectControl: action.isPlayerDirectControl);
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
            // docs/22 §11——命中点/消散点随命令千分定点下发）
            var vanishes = new List<BattleCommand>();
            ProjectileResolver.Resolve(_sim, snapshot, effects, movers, vanishes);
            _sim.EndCompileDyeView(); // 命中编译全部完成（此后效应应用/命令发射不再读附着）

            // 效应统一应用（伤害/治疗合并 HP 天然成立；Buff 施加记入注册表）；碎裂记录=寒冰之棱
            // 「元能>50% 立刻触发」（2026-10-01 复测拍板）——ApplyEffects 元能增益段即时产出
            var shatters = new List<(string holderId, string sourceId, BaseBuff buff)>();
            var appliedBuffs = ApplyEffects(effects, shatters);

            // 死亡判定（效应应用后统一判；同片互杀 = 同归于尽）
            var damagedUnits = CollectDamagedTargets(effects);
            var newlyDead = _sim.ResolveDeaths(damagedUnits);

            // 产出片命令块（三段唯一出口 EmitSliceCommands——命令发射序与对账见其内注释）
            EmitSliceCommands($"回合{turnNumber}片{sliceIndex}", segment, sliceIndex,
                effects, appliedBuffs, movers, vanishes, newlyDead, expired: null, skillCasts: casts,
                shattered: shatters);

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
                    // 回合末周期；召唤单位缺省（Amber=字段默认未配置）不召防误配
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

            GICLog.Info($"[TurnResolver] 部署段 {segment}");
            return segment;
        }

        /// <summary>
        /// 物品行动段（2026-10-16 首个可打出物品批；2026-10-18 装备批并 EquipItem）：回合前部署段
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

            EmitSliceCommands($"回合{turnNumber}片{sliceIndex}", segment, sliceIndex,
                effects, appliedBuffs, movers: null, vanishes: null, newlyDead, expired: null, skillCasts: null,
                shattered: shatters);

            GICLog.Info($"[TurnResolver] 物品行动段 {segment}");
            return segment;
        }

        /// <summary>
        /// 回合结束段（B2 + B6d 经济闭环）：①每玩家发放 5摩拉+5体力（docs/04 §4.3——发放直产命令
        /// 不走效应链，资源变更非单位效应，同部署扣费先例；发放恒有内容，段必推送）→
        /// ②Buff 回合结束效果按注册序结算（docs/22 §2）→ ③计时减一（上 buff 当回合结束即减，
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

            // 按注册序结算回合结束效果，随后计时减一（到期收集；永久 Buff（RemainingTurns<0）不计时——歌声之环类）
            // **回合末=同时结算（决策二十四终版「逐拍交错」）**：buff 的 OnTurnEnd 只产出声明（元素
            // 伤害走 PendingAuraHit），循环后统一按 (拍时刻, 施法者, 目标) 排序逐个过共享编译视图结算
            // ——双异元素光环同拍交错、每层附着、每拍可触发反应（对齐原神双光环同开互融）
            foreach (var buff in _sim.ActiveBuffs)
            {
                effects.AddRange(buff.OnTurnEnd());
                if (!buff.IsPermanent) buff.RemainingTurns--;
            }
            ResolveAuraHits(effects);
            var expired = _sim.CollectExpiredBuffs();

            // 泡湿附着（决策七十六拍板②「在水面上，且不为飞行或两栖移动的单位，每个回合结束时
            // 附着 1 层水」）：地形判据=WaterTerrain 标签（水面/沼泽；**薄冰不带该标签=站薄冰不挂**），
            // 豁免=移动类型飞行/两栖，存活单位才附着（尸体不挂——复活不带附着语义接近）。
            // 覆盖语义=挂 1 层水覆盖旧附着（泡在水里火挂不住）；放 tick/到期之后=回合末收尾。
            // 元素联动：泡湿单位天然带水附着——凯亚冰打=冻结反应、安柏火箭=蒸发（战术意义自洽）
            foreach (var kv in _sim.Units)
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

            var shatters = new List<(string holderId, string sourceId, BaseBuff buff)>();
            // 建筑回合末效果段（决策八十六批D，docs/45 §6：泡湿之后、命令发射之前——回合末
            // 收尾位）：旗帜元能/教堂治疗并入 effects（命令发射+死亡收集复用既有管道）；餐馆摩拉/
            // 骑士团周期召唤直产命令（回合发放/部署段先例——资源/召唤不是单位效应）
            var buildingExtras = new List<BattleCommand>();
            CollectBuildingEffects(turnNumber, effects, buildingExtras);
            var appliedBuffs = ApplyEffects(effects, shatters);
            var newlyDead = _sim.ResolveDeaths(CollectDamagedTargets(effects));

            // 产出回合结束段命令（三段唯一出口；到期 Buff 的 RemoveBuff 发射+注册表注销在出口尾部）
            EmitSliceCommands($"回合{turnNumber}结束段", segment, sliceIndex,
                effects, appliedBuffs, null, null, newlyDead, expired, shattered: shatters);

            // 建筑效果直产命令（餐馆摩拉/骑士团召唤）段尾发射——indexInSlice 接续既有条数
            foreach (var extra in buildingExtras)
            {
                extra.sliceIndex = sliceIndex;
                extra.indexInSlice = segment.commands.Count;
                segment.commands.Add(extra);
            }

            GICLog.Info($"[TurnResolver] {segment}");
            return segment;
        }

        /// <summary>建筑回合末效果收集（决策八十六批D，docs/45 §1.1/§6）：只结算存活建筑
        /// （消散即移除天然；多座同种独立结算——两座餐馆=+10）。旗帜=半径内我方非建筑单位（含倒下
        /// 角色，拍板 #8）各+元能（EnergyEffect 建筑类别，合并键含来源建筑——两座旗帜对同一目标各
        /// +1 不互吞）；餐馆=己方玩家+摩拉（StatChange 直产）；骑士团=以部署回合为 0、每过 效果周期
        /// 回合再召 1 名（最近可部署格顺延；部署当回合末 diff=0 跳过——部署段已即召）；教堂=我方存活
        /// HP&lt;效果阈值% 单位（含建筑，拍板 #9）治疗 效果数值%×本建筑攻击（末点截断通则）。
        /// **先快照再处理**：周期召唤会 RegisterUnit 突变 _units，快照后枚举安全</summary>
        private void CollectBuildingEffects(int turnNumber, List<BattleEffect> effects, List<BattleCommand> extraCommands)
        {
            var buildings = new List<(string id, Unit unit, UnitConfig.UnitData data, UnitIdentity identity)>();
            foreach (var kv in _sim.Units)
            {
                var unit = kv.Value;
                if (unit == null || BattleSimState.IsDead(unit)) continue;
                var data = unit.RawData;
                if (data == null || data.unitType != UnitType.Building || data.建筑效果 == BuildingEffectKind.None) continue;
                var identity = unit.GetUnitComponent<UnitIdentity>();
                if (identity == null) continue;
                buildings.Add((kv.Key, unit, data, identity));
            }

            foreach (var building in buildings)
            {
                var data = building.data;
                switch (data.建筑效果)
                {
                    case BuildingEffectKind.EnergyAura:
                    {
                        if (data.效果半径 <= 0 || data.效果数值 <= 0) break;
                        var center = _sim.GetPosition(building.unit);
                        foreach (var tkv in _sim.Units)
                        {
                            var target = tkv.Value;
                            if (target == null) continue;
                            var targetIdentity = target.GetUnitComponent<UnitIdentity>();
                            if (targetIdentity == null || targetIdentity.Team != building.identity.Team) continue; // 我方（Team 域，歌声之环同口径）
                            var targetData = target.RawData;
                            if (targetData != null && targetData.unitType == UnitType.Building) continue; // 非建筑（拍板 #8）
                            // 倒下角色照常（拍板 #8 含倒下；眷属/建筑无尸体=自然缺席）
                            if (_sim.GetPosition(target).ChebyshevTo(center) > data.效果半径) continue;
                            effects.Add(new EnergyEffect(tkv.Key, data.效果数值, EnergyEffect.CategoryBuildingGain)
                            {
                                SourceUnitId = building.id,
                            });
                        }
                        break;
                    }
                    case BuildingEffectKind.MoraIncome:
                    {
                        if (data.效果数值 <= 0) break;
                        _sim.ApplyMoraDelta(building.identity.OwnerPlayerID, data.效果数值);
                        extraCommands.Add(BattleCommand.StatChange(building.identity.OwnerPlayerID, 0, 0,
                            BattleCommand.StatKindMora, data.效果数值));
                        break;
                    }
                    case BuildingEffectKind.PeriodicSummon:
                    {
                        if (data.效果周期 <= 0) break;
                        int diff = turnNumber - building.unit.DeployedTurn;
                        if (diff <= 0 || diff % data.效果周期 != 0) break;
                        if (DeployUnitExecutor.TrySummonAtNearestCell(_sim, building.identity.OwnerPlayerID,
                                data.召唤单位, _sim.GetPosition(building.unit), out var summonCommand))
                            extraCommands.Add(summonCommand);
                        break;
                    }
                    case BuildingEffectKind.EnergySummon:
                    {
                        // 元能引擎自充（2026-10-24 追记四，骑士团总部）：回合末自获 效果数值 元能——
                        // EnergyEffect 自身为目标（建筑类别获能，合并键含来源=多座总部各自各跳）；
                        // 过阈即召由 ApplyEffects 落地拍统一判定（外部充能〔歌声之环 tick〕同拍同权）
                        if (data.效果数值 <= 0) break;
                        effects.Add(new EnergyEffect(building.id, data.效果数值, EnergyEffect.CategoryBuildingGain)
                        {
                            SourceUnitId = building.id,
                        });
                        break;
                    }
                    case BuildingEffectKind.HealWounded:
                    {
                        if (data.效果数值 <= 0) break;
                        int healAmount = data.效果数值 * data.GetEffectiveAttack() / 100; // 末点截断（整数除法）
                        if (healAmount <= 0) break;
                        foreach (var tkv in _sim.Units)
                        {
                            var target = tkv.Value;
                            if (target == null || BattleSimState.IsDead(target)) continue; // 存活（拍板 #9）
                            var targetIdentity = target.GetUnitComponent<UnitIdentity>();
                            if (targetIdentity == null || targetIdentity.Team != building.identity.Team) continue;
                            var stats = target.GetUnitComponent<UnitStats>();
                            if (stats == null) continue;
                            var hp = stats.GetStatStruct(StatType.HP);
                            if (hp.Max <= 0 || hp.Value * 100 >= data.效果阈值 * hp.Max) continue; // 低于阈值才奶
                            effects.Add(new HealEffect(building.id, tkv.Key, healAmount));
                        }
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 回合末光环命中交错结算管道（决策二十四终版「同时进行」=逐拍交错）：从 effects 收集全部
        /// PendingAuraHit（buff 的 OnTurnEnd 只声明），按 (拍时刻, 施法者 unitId, 目标 unitId) 排序后
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

            _sim.BeginCompileDyeView(); // 共享视图：初值回落活态=回合末开始态；交错序列全程接力
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
            _sim.BeginCompileDyeView(); // 片内附着编译视图（同片多命中消耗序贯可见，双蒸发修复）
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
                    // 移动消耗（统一消耗模型 C-2）：移动技能 costs 单源（MoveExecutor.GetMoveCosts——
                    // 无 Move 条目单位走常量兜底）；低级单位豁免（ResourceGate 口径）；不足→移动落空
                    var moveCosts = MoveExecutor.GetMoveCosts(unit);
                    if (!ResourceGate.HasAll(_sim, unit, action.playerId, moveCosts, out _,
                            isPlayerDirectControl: action.isPlayerDirectControl))
                    {
                        GICLog.Info($"[TurnResolver] 单位 {action.unitId} 移动消耗不足，移动落空");
                        break;
                    }
                    ResourceGate.ChargeAll(_sim, unit, action.playerId, moveCosts, effects,
                        isPlayerDirectControl: action.isPlayerDirectControl);
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
            _sim.EndCompileDyeView(); // 命中编译全部完成（此后效应应用/命令发射不再读附着）

            var shatters = new List<(string holderId, string sourceId, BaseBuff buff)>();
            var appliedBuffs = ApplyEffects(effects, shatters);
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
                effects, appliedBuffs, moverList, vanishes, newlyDead, expired: null, skillCasts: casts,
                shattered: shatters);

            GICLog.Info($"[TurnResolver] 即时行动 {segment}");
            return segment;
        }

        // ==================== 段命令发射（三段唯一出口，2026-09-23 审查 Y1 收口） ====================

        /// <summary>效应命中时刻→命令应用时刻注入（WYSIWYP 时间通道单源，2026-10-02 复审收口）：
        /// HitSeconds>0 才覆写 launchMs（0=立即=构造默认）；Reaction/Energy/MoraPlunder/Heal 各发射
        /// 分支统一走此口，勿散抄公式（新增带时刻效应同样走此口）</summary>
        private static void ApplyHitMs(BattleCommand command, float hitSeconds)
        {
            if (hitSeconds > 0f)
                command.launchMs = Mathf.RoundToInt(hitSeconds * 1000f);
        }

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

            // Move：被挡也发命令（全挡 path=[原格] / 部分挡 path=已走段），携带 MoveBlocked 标记+方向
            // 供客户端播"撞墙弹回"表现（2026-09-21）；direction 恒传真实移动方向（2026-09-29 拍板③：
            // 立牌朝向随移动方向镜像——此前成功移动传 0，客户端把朝向重置回右，移动转向失效根因）
            if (movers != null)
            {
                foreach (var mover in movers)
                {
                    if (mover.Path.Count > 1 || mover.Blocked)
                        segment.commands.Add(BattleCommand.Move(mover.UnitId, sliceIndex, indexInSlice++, new List<BattleCell>(mover.Path),
                            mover.Blocked ? BattleCommand.MoveBlocked : 0, (int)mover.Direction));
                }
            }

            foreach (var effect in MergeDamageEffects(effects))
            {
                var dmgCmd = BattleCommand.Damage(effect.AttackerUnitId, effect.TargetUnitId, sliceIndex, indexInSlice++, effect.Amount,
                    effect.Element, effect.Delivery, effect.FromCell,
                    Mathf.RoundToInt(effect.HitPointX * 1000f), Mathf.RoundToInt(effect.HitPointY * 1000f), effect.ReactionType,
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
                // launchMs 字段复用为"应用时刻"（union 载荷；0=立即——移动获能/协奏/消耗/回合发放无命中时刻）
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
            // 即时退场，随碎裂先例立即应用）；未归零=零命令（冻结徽章回合数下回合快照自愈）
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
                    sliceIndex, indexInSlice++, applied.BuffType, applied.Level, applied.Turns,
                    applied.SourceSkillId, applied.BuffKey); // 具名 key（决策五十七——异资产同族各一条命令）
                // 命中时刻到点挂徽章（决策七十六复测返修 WYSIWYG：箭在飞时目标不提前结冰/挂减防；
                // 合并取首条时刻=徽章首现拍；OnCast 施放/回合末光环 BeatSeconds 语义不变）
                ApplyHitMs(applyBuffCommand, applied.HitSeconds);
                segment.commands.Add(applyBuffCommand);
            }
            foreach (var dead in newlyDead)
            {
                bool hasId = _sim.TryGetUnitId(dead, out var deadId);
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
                // 退场，徽章随 view 同殁（下回合快照消失路径天然自洽）
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
                        segment.commands.Add(BattleCommand.RemoveBuff(sourceId, targetId, sliceIndex, indexInSlice++,
                            (int)buff.Type, buff.Config?.name)); // 具名 key（决策五十七）
                    }
                    _sim.RemoveBuff(buff.owner, buff);
                }
            }

            BattleEffectCommandAudit.Assert(context, effects, appliedBuffs, segment.commands);

            // 建筑元能引擎过阈召唤（2026-10-24 追记四）：直产命令（audit 后=MoraIncome/PeriodicSummon
            // 直产同族口径）——消费命令+召唤命令逐建筑发射
            DrainPendingBuildingSummons(segment, sliceIndex);
        }

        /// <summary>建筑元能引擎过阈召唤发射（决策八十六追记四）：ApplyEffects 落地拍收集的过阈事实——
        /// 逐建筑发射 消费命令（-效果阈值；带过阈获能同拍时刻，客户端能量条先涨后扣）+ 召唤命令
        ///（TrySummonAtNearestCell 锚=建筑自身格，最近可部署格顺延）；同段死亡的建筑=召唤取消；
        /// 落点无合法格（棋盘占满）=召唤落空、能量已耗（边缘自担，与「立刻」语义一致）。
        /// RegisterUnit 只在此发射拍执行——应用循环内不突变 _units（枚举安全，
        /// CollectBuildingEffects「先快照再处理」同族纪律）</summary>
        private void DrainPendingBuildingSummons(Segment segment, int sliceIndex)
        {
            if (_pendingBuildingSummons.Count == 0) return;
            foreach (var (buildingId, hitSeconds) in _pendingBuildingSummons)
            {
                var building = _sim.GetUnit(buildingId);
                if (building == null || BattleSimState.IsDead(building)) continue; // 同段死亡=取消
                var data = building.RawData;
                if (data == null || data.召唤单位 == UnitName.Amber) continue; // 召唤缺省防误配
                var identity = building.GetUnitComponent<UnitIdentity>();
                if (identity == null) continue;
                var consume = BattleCommand.StatChange(buildingId, sliceIndex, segment.commands.Count,
                    BattleCommand.StatKindEnergy, -data.效果阈值);
                ApplyHitMs(consume, hitSeconds);
                segment.commands.Add(consume);
                if (DeployUnitExecutor.TrySummonAtNearestCell(_sim, identity.OwnerPlayerID, data.召唤单位,
                        _sim.GetPosition(building), out var summonCommand))
                {
                    summonCommand.sliceIndex = sliceIndex;
                    summonCommand.indexInSlice = segment.commands.Count;
                    segment.commands.Add(summonCommand);
                }
            }
            _pendingBuildingSummons.Clear();
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
        /// 行动预告下发（2026-09-29 执行预览）：全部真行动扁平化广播（玩家上交+低级单位+AI 脑同源；
        /// 部署/玩家级 Pass 不入——前者已抽出、后者已剔除；单位级 Pass 预览无行动不入）。
        /// 攻速取 UnitStats 活值=分桶同源；未知名单位跳过（与分桶 Warn 同口径）
        /// </summary>
        private void SendTurnPlan(int turnNumber, List<ActionData> actions)
        {
            var msg = new TurnPlanMessage { turnNumber = turnNumber };
            foreach (var action in actions)
            {
                if (action.actionType == ActionType.Pass) continue; // 空过=无行动不入预告
                var unit = _sim.GetUnit(action.unitId);
                if (unit == null) continue;
                msg.entries.Add(new TurnPlanEntry
                {
                    unitId = action.unitId,
                    playerId = action.playerId,
                    actionType = (int)action.actionType,
                    skillIndex = action.skillIndex,
                    attackSpeed = unit.GetUnitComponent<UnitStats>()?.AttackSpeed ?? 0,
                });
            }
            _transport.HostSend(BattleMessageType.TurnPlan, msg);
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
        /// 效应统一应用（伤害/治疗/Buff 施加）；返回已施 Buff 列表（含合并后的级别与剩余回合，供命令产出）。
        /// 元能两段应用（2026-09-25 用户拍板「先扣除，再加」）：同段多来源元能效应先消耗后获取——
        /// 获取先应用会被 baseEnergy 上限钳位吞掉（30+10→40 钳 30，再 −20=10 ≠ 期望 30−20+10=20）。
        /// shatters=寒冰之棱碎裂记录（2026-10-01 复测拍板「元能&gt;50% 立刻触发」）：每笔元能增益
        /// 落地后即时判定（IcicleBuff.TryShatter 内联应用治疗+移除），事实经本列表传给命令出口
        /// 补发 RemoveBuff——由调用方创建传入（片/回合结束/即时段三处各自持有）。
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
                // 每玩家每回合单行动），防御性钳制 Warn 记账（AppliedAmount=0 零命令，对账按 0 非漏发）
                if (effect is MoraSpendEffect moraSpend)
                {
                    int applied = _sim.GetMora(moraSpend.TargetUnitId) >= moraSpend.Amount
                        ? moraSpend.Amount : _sim.GetMora(moraSpend.TargetUnitId);
                    if (applied < moraSpend.Amount)
                        GICLog.Warn($"[TurnResolver] 摩拉消耗效应不足额（{moraSpend.TargetUnitId} 池" +
                                    $"{_sim.GetMora(moraSpend.TargetUnitId)}/{moraSpend.Amount}）——实际扣 {applied}");
                    moraSpend.AppliedAmount = applied;
                    if (applied > 0)
                        _sim.TrySpendMora(moraSpend.TargetUnitId, applied);
                    continue;
                }

                // 物品消耗（统一消耗模型 C-1，玩家级）：LoseCard 手牌条目真源扣减（减尽移除）——
                // 不足理论不可达（门槛先行+每玩家每回合单行动）；指定模式=单条 LoseCard；
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
                        GICLog.Warn($"[TurnResolver] 物品消耗效应未足额（{itemConsume.TargetUnitId} " +
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
                                int explodeRadius = blockItemData.GetInt(nameof(SkillParamKey.ExplodeRadius), 1);
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
                        GICLog.Warn($"[TurnResolver] 装备效应落空（{effect.TargetUnitId} 缺组件或物品 {(ItemName)equip.Item} 无配置）");
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
                        applyBuff.DurationTurns, applyBuff.BuffKey);
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

                    // 回填合并后的真实状态（同身份叠层时 Level/Turns 以注册表为准——按 buffKey 匹配
                    // 应用到的实例；key 空（防御）回落 type 匹配）
                    var state = !string.IsNullOrEmpty(applyBuff.BuffKey)
                        ? target.Buffs.Find(b => b.Config != null && b.Config.name == applyBuff.BuffKey)
                        : target.Buffs.Find(b => b.Type == (BuffType)applyBuff.BuffType);
                    applyBuff.Level = state != null ? state.Level : applyBuff.Level;
                    applyBuff.Turns = state != null ? state.RemainingTurns : 0;
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
                    // 冻结层消耗（决策七十四「冻结 Buff 可作为冰元素参与反应」——每个回合相当于 1 层冰）：
                    // 融化/超导按反应级别消耗冻结回合；**归零立即解冻**（拍板：_sim.RemoveBuff 注销+清
                    // Frozen 状态〔OnRemoved〕→命令层发 RemoveBuff=霜化即时退场）；未归零=零命令
                    //（冻结徽章回合数靠下回合快照自愈，Frost 表现由 Frozen 状态驱动仍正确）
                    var freeze = _sim.GetFreezeBuff(consumeFreeze.TargetUnitId);
                    if (freeze == null)
                    {
                        consumeFreeze.AppliedTurns = 0; // 编译后冻结已被外部移除的防御路径（零命令零动作）
                    }
                    else
                    {
                        consumeFreeze.AppliedTurns = Mathf.Min(consumeFreeze.Turns,
                            Mathf.Max(0, freeze.RemainingTurns));
                        freeze.RemainingTurns -= consumeFreeze.AppliedTurns;
                        if (freeze.RemainingTurns <= 0)
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
                    foreach (var kv in _sim.Units)
                    {
                        var enemy = kv.Value;
                        if (BattleSimState.IsDead(enemy)) continue;
                        string enemyOwnerId = enemy.GetUnitComponent<UnitIdentity>()?.OwnerPlayerID;
                        if (enemyOwnerId == null || _sim.GetTeamOf(enemyOwnerId) == holderTeam) continue;
                        var enemyPos = _sim.GetPosition(enemy);
                        if (holderPos.ChebyshevTo(enemyPos) > explosion.radius) continue;
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

            // 骑士团总部元能引擎过阈收集（2026-10-24 决策八十六追记四）：加能落地同拍判定——过阈即耗
            //（状态层立刻扣 效果阈值，不留「过阈未耗」窗口）+入队待召；消费命令与召唤命令由
            // EmitSliceCommands 尾统一发射（命令序=全部获能命令之后：客户端能量条先涨后扣再出骑士，
            // 因果读得懂）。外部充能（歌声之环 tick/自充）与未来中途给能同拍触发——建筑效果不必
            // 等回合末（用户拍板「注意建筑的效果不一定是在回合结束才生效了」）
            void CollectBuildingEnergySummon(Unit unit, float hitSeconds)
            {
                if (unit == null) return;
                var data = unit.RawData;
                if (data == null || data.unitType != UnitType.Building
                    || data.建筑效果 != BuildingEffectKind.EnergySummon) return;
                if (data.效果阈值 <= 0 || data.召唤单位 == UnitName.Amber) return; // 召唤缺省防误配（部署即召同款守卫）
                var stats = unit.GetUnitComponent<UnitStats>();
                if (stats == null) return;
                var energy = stats.GetStatStruct(StatType.Energy);
                if (energy.Value < data.效果阈值) return;
                energy.Add(-data.效果阈值); // 过阈即耗（触发线=消耗额单字段）
                stats.SetStatStruct(StatType.Energy, energy);
                string buildingId = unit.GetUnitComponent<UnitIdentity>()?.UnitID;
                if (!string.IsNullOrEmpty(buildingId))
                    _pendingBuildingSummons.Add((buildingId, hitSeconds));
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
                        CollectBuildingEnergySummon(_sim.GetUnit(gain.TargetUnitId), gain.HitSeconds); // 元能引擎过阈即召（追记四）
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
        // | ApplyBuff       | (目标, BuffType)                     | 级别取大、回合取后施合并态；命令数以合并态为准  |
        // | Attach/Reaction | 不合并（一一对应）                    | 覆盖语义后到者胜属预期                          |
        // 注：永久 Buff（RemainingTurns<0，歌声之环）不走到期收集——倒下即失走死亡循环 RemoveBuff
        // =================================================================================

        /// <summary>同片同 (目标,类型) 的多次施加合并为一条命令（回合数以最终合并态为准）</summary>
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

        // ==================== 推送与 ack ====================

        private IEnumerator PushSegmentAndWaitAck(Segment segment)
        {
            _flow.NotifySegmentPushed(segment.turnNumber, segment.sliceIndex);
            // 回放录制（2026-10-20 Replay harness 批）：命令流本体在推送前定稿——断言面单点
            BattleRecorder.CaptureSegment(segment);
            // 对手档案采集（AI-2 批，docs/active/42 §4）：段命令流观察——Damage→元素习惯/最爱
            // 目标/交战距离，Death(建筑)→核心摧毁事件账本（公开信息=全体客户端广播态，红线①合规）
            OpponentProfiler.ObserveCommands(_sim, segment.commands, segment.turnNumber);
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
