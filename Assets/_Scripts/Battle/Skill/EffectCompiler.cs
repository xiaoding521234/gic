using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
using GIC.Framework;
namespace GIC.Battle
{


    /// <summary>
    /// 效果原子编译器（B-1，docs/active/29 + docs/18 决策九）：把 SkillData.effects 效果原子声明
    /// 编译成 BattleEffect——**只做声明展开，应用/合并/对账/命令发射全链零改动**（回合制片内快照
    /// 结算不需要 EGamePlay 式运行时效果实体，编译期展开即确定性等价）。
    /// 分工三真源：时轮=时间与判定规格（WHERE/WHEN）；参数表=数值（HOW MUCH）；本类消费效果原子=WHAT。
    /// 判定编译：LineProjectile→逐发发射声明（ProjectileResolver 求交）、LineBurst→逐段整线即时命中、
    /// Enso/Contract→目标校验+OnCast（单位指向不经格子判定，docs/05 §5.3）。
    /// 预判工厂 WouldHitEnemyInDirection 按判定 kind 派发——预判/结算同形由工厂强制（决策九 D5，
    /// 取代旧"每技能类自觉覆写"纪律）。
    /// </summary>
    public static class EffectCompiler
    {
        /// <summary>TriggerSkill 链最大深度（2026-09-25 三轮审查 S2 防环：延奏→变奏正常链深 1~2，
        /// 超限=循环触发配置失误，Warn 截断保 Host 不栈溢出）</summary>
        private const int MaxTriggerChainDepth = 3;

        /// <summary>链深度计数（战斗单线程；TriggerSkill 分支 ++/-- 包 try-finally，异常也归零）</summary>
        private static int _triggerDepth;

        // ==================== 技能级编译入口（ConfiguredSkill.ResolveEffects） ====================

        /// <summary>技能全编译：按技能类型分流判定轨/单位指向，产出全部 BattleEffect（不含 SkillCast/元能消耗——SkillExecutor 职责）</summary>
        public static List<BattleEffect> CompileSkill(BattleSimState sim, ActionData action,
            BattleSnapshot sliceSnapshot, SkillConfig.SkillData skillData)
        {
            var effects = new List<BattleEffect>();
            if (skillData == null || !skillData.HasEffects) return effects;

            var casterState = SkillHitResolver.FindUnitState(sliceSnapshot, action.unitId);
            if (casterState == null) return effects;

            // 单位指向型（延奏/契约）：目标校验（Host 权威）+ OnCast 效果——不经格子判定。
            // 敌我判定=TeamType 口径（2026-09-25 三轮审查 C2：延奏=同队合法目标含 2v2 队友、契约=异队）
            if (skillData.skillType == SkillType.Enso || skillData.skillType == SkillType.Contract)
            {
                var target = SkillHitResolver.FindUnitState(sliceSnapshot, action.targetUnitId);
                bool allySide = skillData.skillType == SkillType.Enso;
                var casterTeam = sim.GetTeamOf(action.playerId);
                bool valid = target != null && target.isCorpse == 0
                    && (allySide ? (TeamType)target.team == casterTeam : (TeamType)target.team != casterTeam);
                if (!valid)
                {
                    GICLog.Info($"[EffectCompiler] {action.unitId} {skillData.skillID} 目标无效（{action.targetUnitId}），行动落空");
                    return effects;
                }
                CompileOnCast(sim, action, sliceSnapshot, casterState, target, skillData, effects);
                return effects;
            }

            // 单位指向型爆发（B-3 ②，芭芭拉闪耀奇迹——时轮 aimMode=TargetUnit 声明，SkillData.IsUnitTargeted）：
            // 目标校验=我方任意单位**含尸体**（复苏语义——docs/05 §5.4「尸体可被选中为目标」+复活例外条款）；
            // 无判定轨（效果原子以 condition 声明分叉：尸体→复苏+治疗 / 活体→歌声之环），OnCast 即全部产出
            if (skillData.IsUnitTargeted())
            {
                var target = SkillHitResolver.FindUnitState(sliceSnapshot, action.targetUnitId);
                var casterTeam = sim.GetTeamOf(action.playerId);
                bool valid = target != null && (TeamType)target.team == casterTeam;
                if (!valid)
                {
                    GICLog.Info($"[EffectCompiler] {action.unitId} {skillData.skillID} 目标无效（{action.targetUnitId}），行动落空");
                    return effects;
                }
                CompileOnCast(sim, action, sliceSnapshot, casterState, target, skillData, effects);
                return effects;
            }

            // 无目标自施放爆发（时轮 aimMode=None 声明，SkillData.IsSelfCast——首个=凯亚凛冽轮舞
            // buff 型，2026-10-01 拍板「凯亚爆发实际并不是召唤，与歌声之环类似，都是buff」）：
            // 无判定轨（不扫线/无投射物——否则 Burst 无 LineBurst clip 会走「无时轮兜底」误产
            // 整线伤害），OnCast 效果直接全产出（targetFilter=Caster=自身施加 Buff）
            if (skillData.IsSelfCast())
            {
                CompileOnCast(sim, action, sliceSnapshot, casterState, null, skillData, effects);
                return effects;
            }

            // 直线/迸发型（战技/爆发）：判定轨编译（发射声明/逐段整线）+ OnCast 效果（filter=Caster 等）
            if (skillData.skillType == SkillType.Normal || skillData.skillType == SkillType.Burst)
                CompileJudgment(sim, action, sliceSnapshot, casterState, skillData, effects);

            CompileOnCast(sim, action, sliceSnapshot, casterState, null, skillData, effects);
            return effects;
        }

        // ==================== 判定轨编译（发射声明/整线段） ====================

        /// <summary>判定轨→产出声明：LineProjectile=逐发 ProjectileEffect（ProjectileResolver 求交）；
        /// LineBurst=逐段整线即时命中（快照口径）；无时轮兜底=旧合并行为（docs/18 决策八"无时轮兜底"延续）</summary>
        private static void CompileJudgment(BattleSimState sim, ActionData action, BattleSnapshot snapshot,
            UnitState casterState, SkillConfig.SkillData skillData, List<BattleEffect> effects)
        {
            int damagePercent = skillData.GetInt(SkillParamKey.Damage, 100);
            int damageCount = skillData.GetInt(SkillParamKey.DamageCount, 1);
            var delta = SkillHitResolver.DirectionToDelta(action.direction);
            var from = casterState.position;

            // 整线迸发段编译：Burst 型；或**配了 LineBurst clip 的技能**（Normal 型同消费——2026-09-25
            // 修法 A：修复前 Normal+LineBurst clip 读不到投射物 clip 走"无时轮兜底"=24 格首停投射物，
            // 与预判工厂/HUD 瞄准推荐/文档「前方 2 格」三处漂移；凯亚霜袭=首个消费方，多目标整线全中）
            var burstClips = SkillTimelineQuery.JudgmentClips(skillData.timeline, SkillJudgmentKind.LineBurst);
            if (skillData.skillType == SkillType.Burst || burstClips.Count > 0)
            {
                if (burstClips.Count > 0)
                {
                    foreach (var clip in burstClips)
                        for (int i = 0; i < damageCount; i++)
                            CompileLineBurstSegment(sim, action, snapshot, skillData, from, delta,
                                damagePercent, clip.startTime + clip.hitInterval * i, clip.maxRange, effects);
                }
                else
                {
                    CompileLineBurstSegment(sim, action, snapshot, skillData, from, delta,
                        damagePercent * damageCount, 0f, 0, effects); // 无时轮兜底：合并单段
                }
                return;
            }

            // 直线投射物：逐发发射声明（逐发独立判定/附着/反应——决策八"推翻 B4 简化①"）
            var projClips = SkillTimelineQuery.JudgmentClips(skillData.timeline, SkillJudgmentKind.LineProjectile);
            if (projClips.Count > 0)
            {
                foreach (var clip in projClips)
                    for (int i = 0; i < damageCount; i++)
                        effects.Add(new ProjectileEffect(action.unitId, action, damagePercent,
                            from, delta.x, delta.y, clip.startTime + clip.hitInterval * i,
                            clip.projectileSpeed, clip.hitDiameter, clip.maxRange));
            }
            else
            {
                effects.Add(new ProjectileEffect(action.unitId, action, damagePercent * damageCount,
                    from, delta.x, delta.y)); // 无时轮兜底：合并单发
            }
        }

        /// <summary>整线迸发单段：线上全目标各编译一次 OnHit（虚空=线终止；无截停——投放形态=整线天降）</summary>
        private static void CompileLineBurstSegment(BattleSimState sim, ActionData action, BattleSnapshot snapshot,
            SkillConfig.SkillData skillData, BattleCell from, Vector2Int delta, int attackPercent,
            float launchSeconds, int clipMaxRange, List<BattleEffect> effects)
        {
            int maxRange = clipMaxRange > 0 ? clipMaxRange : ProjectileRule.MaxRange;
            for (int step = 1; step <= maxRange; step++)
            {
                var cell = new BattleCell(from.x + delta.x * step, from.y + delta.y * step);
                if (!sim.Map.HasTile(cell.x, cell.y)) break;

                foreach (var enemy in SkillHitResolver.FindEnemiesAt(snapshot, sim.GetTeamOf(action.playerId), cell))
                    effects.AddRange(CompileOnHit(sim, action, snapshot, skillData, enemy.unitId,
                        attackPercent, 0, from, 0f, 0f, launchSeconds, launchSeconds)); // 整线迸发：段时刻即命中时刻（瞬发无飞行段）
            }
        }

        // ==================== OnCast 编译（施放时效果） ====================

        private static void CompileOnCast(BattleSimState sim, ActionData action, BattleSnapshot snapshot,
            UnitState casterState, UnitState directTarget, SkillConfig.SkillData skillData, List<BattleEffect> effects)
        {
            foreach (var atom in skillData.effects)
            {
                if (atom.trigger != SkillEffectTrigger.OnCast) continue;
                foreach (var target in ResolveCastTargets(atom, action, snapshot, sim, casterState, directTarget))
                    CompileAtom(sim, action, snapshot, skillData, atom, target.unitId, casterState,
                        0, 0, BattleCell.zero, 0f, 0f, 0f, 0f, effects, EnergyEffect.CategoryEnsoGain);
            }
        }

        /// <summary>OnCast 目标解析（群体筛选=多目标；filter 不满足返回空=跳过该原子；势力筛选=蒙德协奏规则 docs/07）</summary>
        private static IEnumerable<UnitState> ResolveCastTargets(SkillEffectConfig atom, ActionData action,
            BattleSnapshot snapshot, BattleSimState sim, UnitState casterState, UnitState directTarget)
        {
            switch (atom.targetFilter)
            {
                case SkillEffectTargetFilter.Caster:
                    yield return casterState;
                    break;
                case SkillEffectTargetFilter.AllAllies:
                    // 我方全体存活（元气迸发——群体治疗各按目标自身 maxHp 换算）；
                    // 我军=TeamType 口径（2026-09-25 三轮审查 C2：2v2 含队友单位）
                    foreach (var u in snapshot.units)
                        if ((TeamType)u.team == sim.GetTeamOf(action.playerId) && u.isCorpse == 0)
                            yield return u;
                    break;
                case SkillEffectTargetFilter.MondstadtOrSelf:
                    if (directTarget != null && (directTarget.unitId == action.unitId || BattleHeuristics.IsMondstadtUnitName(directTarget.unitName)))
                        yield return directTarget;
                    break;
                case SkillEffectTargetFilter.NotMondstadt:
                    if (directTarget != null && !BattleHeuristics.IsMondstadtUnitName(directTarget.unitName))
                        yield return directTarget;
                    break;
                default:
                    if (directTarget != null) yield return directTarget; // Target=指向目标（方向技能 OnCast Target=无目标跳过）
                    break;
            }
        }

        // ==================== OnHit 编译（命中时效果——原 Hit 内置产出的数据驱动化） ====================

        /// <summary>命中效果编译：遍历 OnHit 原子逐个展开。Damage 原子内建元素反应预览链
        /// （易伤/增伤并入乘区+反应命令+冻结 Buff——docs/06）；附着/获能等为其余独立原子。
        /// hitSeconds=命中时刻（2026-09-25 拍板「命中时才给」：获能/治疗效应携带，客户端到点应用；0=立即）</summary>
        public static List<BattleEffect> CompileOnHit(BattleSimState sim, ActionData action, BattleSnapshot sliceSnapshot,
            SkillConfig.SkillData skillData, string targetUnitId, int attackPercent, int delivery, BattleCell fromCell,
            float hitPointX, float hitPointY, float launchSeconds, float hitSeconds = 0f)
        {
            var effects = new List<BattleEffect>();
            var casterState = SkillHitResolver.FindUnitState(sliceSnapshot, action.unitId);
            if (casterState == null) return effects;

            foreach (var atom in skillData.effects)
            {
                if (atom.trigger != SkillEffectTrigger.OnHit) continue;

                // 群体语义（水之浅唱治疗）：以施法者为中心 radiusKey 格内我方存活各编译一次（含施法者）
                if (atom.targetFilter == SkillEffectTargetFilter.CasterRadiusAllies)
                {
                    int radius = atom.radiusKey != SkillParamKey.None ? skillData.GetInt(atom.radiusKey) : 0;
                    if (radius <= 0) continue;
                    foreach (var ally in sliceSnapshot.units)
                    {
                        if ((TeamType)ally.team != sim.GetTeamOf(action.playerId) || ally.isCorpse != 0) continue; // 尸体不治疗；我军=TeamType 口径（C2）
                        int dist = Math.Max(Math.Abs(ally.position.x - casterState.position.x),
                            Math.Abs(ally.position.y - casterState.position.y));
                        if (dist > radius) continue;
                        CompileAtom(sim, action, sliceSnapshot, skillData, atom, ally.unitId, casterState,
                            attackPercent, delivery, fromCell, hitPointX, hitPointY, launchSeconds, hitSeconds,
                            effects, EnergyEffect.CategorySkillHitGain);
                    }
                    continue;
                }

                // 行动者语义（战技获能 B6a，docs/18 决策七）：targetFilter=Caster → 受益者=施法者
                // （行动者）——命中敌不充能、多次命中只获一次（同片应用层按 目标+类别 去重，TurnResolver）。
                // 2026-09-25 修复：此前三战技获能原子 targetFilter 误配 Target，+10 元能发给了被命中的敌人
                if (atom.targetFilter == SkillEffectTargetFilter.Caster)
                {
                    CompileAtom(sim, action, sliceSnapshot, skillData, atom, action.unitId, casterState,
                        attackPercent, delivery, fromCell, hitPointX, hitPointY, launchSeconds, hitSeconds,
                        effects, EnergyEffect.CategorySkillHitGain);
                    continue;
                }

                CompileAtom(sim, action, sliceSnapshot, skillData, atom, targetUnitId, casterState,
                    attackPercent, delivery, fromCell, hitPointX, hitPointY, launchSeconds, hitSeconds,
                    effects, EnergyEffect.CategorySkillHitGain);
            }
            return effects;
        }

        // ==================== 单原子编译（OnCast/OnHit 共用） ====================

        private static void CompileAtom(BattleSimState sim, ActionData action, BattleSnapshot snapshot,
            SkillConfig.SkillData skillData, SkillEffectConfig atom, string targetUnitId, UnitState casterState,
            int attackPercent, int delivery, BattleCell fromCell, float hitPointX, float hitPointY,
            float launchSeconds, float hitSeconds, List<BattleEffect> effects, int energyCategory)
        {
            var attacker = sim.GetUnit(action.unitId);
            var target = sim.GetUnit(targetUnitId);
            if (attacker == null || target == null) return;
            var targetState = SkillHitResolver.FindUnitState(snapshot, targetUnitId);
            if (targetState == null) return; // 快照中不存在（瞬发读片初状态）

            // 作用条件（B-3 ② 单点收口——OnCast 指向/OnHit 命中/施法者分支全过此闸）：
            // 按目标存活态过滤（复苏/增益分支分叉声明）；None=无条件（存量原子零行为变化）
            if (!PassesCondition(atom, targetState)) return;

            switch (atom.kind)
            {
                case SkillEffectKind.Damage:
                {
                    var attackerElement = attacker.GetUnitComponent<UnitElement>()?.SelfElement ?? ElementType.Physical;
                    var element = atom.element != ElementType.Physical ? atom.element : attackerElement;

                    // 元素反应预判（融化=易伤/蒸发=增伤/冻结=控制，docs/06）——附着读片内编译视图
                    // （2026-10-01 双蒸发修复）：快照只是初值，同片前序命中的反应消耗/新附着随编译序
                    // 对后续命中可见（安柏双箭打一层水=第一发蒸发消耗、第二发见火同元素不反应）
                    var outcome = ElementReactionResolver.Preview(sim.GetCompileDye(targetUnitId, targetState), element);
                    // stats 取活态 UnitStats（当前片内无属性突变点=与片前快照恒等；未来若引入片中属性
                    // 变化——光环/移动触发效果等——须统一改读快照，防快照纪律分叉，2026-09-27 复审注记）
                    var request = new DamageRequest
                    {
                        Attacker = attacker,
                        Target = target,
                        Element = (int)element,
                        VulnerabilityBonus = outcome.VulnerabilityBonus,
                        DamageBonusDelta = outcome.DamageBonusDelta,
                    };
                    // 伤害基准分流（docs/20 §5.1）：BasedOnAttack=百分比×攻击（现行为——百分比由判定编译注入）；
                    // BasedOnMaxHealth=百分比×施法者最大生命（水之浅唱——治疗角色伤害吃生命）→ FlatDamage 加法区承载
                    var damageParam = FindParam(skillData, atom.paramKey);
                    if (damageParam != null && damageParam.baseType == SkillBaseType.BasedOnMaxHealth)
                    {
                        var casterStats = attacker.GetUnitComponent<UnitStats>();
                        request.AttackPercent = 0;
                        // float 除法至管线末点单次截断（2026-09-27 复审修复+拍板「舍弃小数」：
                        // 原 int 截断=双取整点；现中途全 float、DamagePipeline 末点 FloorToInt）
                        request.FlatDamage = casterStats != null
                            ? casterStats.GetStatStruct(StatType.HP).Max * attackPercent / 100f : 0;
                    }
                    else
                    {
                        request.AttackPercent = attackPercent;
                    }
                    var result = DamagePipeline.Calculate(request);
                    if (!result.Cancelled && result.FinalDamage > 0)
                        effects.Add(new DamageEffect(action.unitId, targetUnitId, result.FinalDamage, (int)element,
                            delivery, fromCell, hitPointX, hitPointY, outcome.ReactionType,
                            Mathf.RoundToInt(launchSeconds * 1000f))
                        { HitSeconds = hitSeconds }); // 吸血治疗继承命中时刻（WYSIWYG）

                    if (outcome.HasReaction)
                    {
                        // 反应 1:1 消耗被反应附着（docs/06 §6.3）——片内后续命中不再见旧附着；
                        // 新附着由同技能 AttachElement 原子覆盖（真实状态仍统一应用写入）
                        sim.SetCompileDye(targetUnitId, ElementType.Physical);
                        // 反应事实载体 + 冻结控制 Buff（融化伤害已并入 DamageEffect）
                        effects.Add(new ReactionEffect(action.unitId, targetUnitId, outcome.ReactionType, outcome.Level));
                        if (outcome.BuffType >= 0)
                            effects.Add(new ApplyBuffEffect(action.unitId, targetUnitId, outcome.BuffType, outcome.Level));
                    }
                    break;
                }

                case SkillEffectKind.Heal:
                {
                    // 治疗换算出口（docs/11 裸 int 坑）：编译时按 baseType 换算后传值——Fixed=直读、
                    // BasedOnMaxHealth=百分比×施法者最大生命（2026-09-30 拍板翻转，GI 语义）、
                    // BasedOnTargetMaxHealth=百分比×目标最大生命、BasedOnAttack=百分比×施法者攻击。
                    // HitSeconds=命中时刻（OnHit 治疗如水之浅唱随投射物落地弹数字；OnCast 治疗恒 0=立即）
                    int amount = ResolveHealAmount(skillData, atom.paramKey, atom.value, attacker, target);
                    if (amount > 0)
                        effects.Add(new HealEffect(action.unitId, targetUnitId, amount) { HitSeconds = hitSeconds });
                    break;
                }

                case SkillEffectKind.ApplyBuff:
                {
                    int buffValue = atom.paramKey != SkillParamKey.None ? skillData.GetInt(atom.paramKey) : atom.value;
                    int stackLimit = atom.paramKey2 != SkillParamKey.None ? skillData.GetInt(atom.paramKey2) : 0;
                    int duration = atom.paramKey3 != SkillParamKey.None ? skillData.GetInt(atom.paramKey3) : 0;
                    effects.Add(new ApplyBuffEffect(action.unitId, targetUnitId, (int)atom.buffType, 1,
                        buffValue, stackLimit, duration));
                    break;
                }

                case SkillEffectKind.AttachElement:
                {
                    var attackerElement = attacker.GetUnitComponent<UnitElement>()?.SelfElement ?? ElementType.Physical;
                    var element = atom.element != ElementType.Physical ? atom.element : attackerElement;
                    if (element != ElementType.Physical) // 物理不附着（docs/06）
                    {
                        effects.Add(new AttachElementEffect(action.unitId, targetUnitId, (int)element));
                        sim.SetCompileDye(targetUnitId, element); // 覆盖语义对片内后续命中可见（双蒸发修复）
                    }
                    break;
                }

                case SkillEffectKind.EnergyGain:
                {
                    int delta = atom.paramKey != SkillParamKey.None ? skillData.GetInt(atom.paramKey) : atom.value;
                    // HitSeconds=命中时刻（战技获能「命中时才给」——客户端元能到点跳变，2026-09-25 拍板；
                    // OnCast 协奏元能恒 0=立即）
                    effects.Add(new EnergyEffect(targetUnitId, delta, energyCategory) { HitSeconds = hitSeconds });
                    break;
                }

                case SkillEffectKind.MoraPlunder:
                {
                    // 摩拉掠夺（B-3 首个资源类原子，霜袭「每命中一个敌人掠夺其 5 摩拉」）：被掠夺方=
                    // 命中敌人的所属玩家、掠夺方=施法者玩家（璃月先例=玩家池转移）；数值引参数表
                    // （paramKey=MoraPlunder）防双源；实际量按被掠夺方池钳出（ApplyEffects）
                    int amount = atom.paramKey != SkillParamKey.None ? skillData.GetInt(atom.paramKey, 0) : atom.value;
                    if (amount > 0)
                    {
                        var targetIdentity = target.GetUnitComponent<UnitIdentity>();
                        if (targetIdentity != null)
                            effects.Add(new MoraPlunderEffect(targetIdentity.OwnerPlayerID, action.playerId, amount)
                            {
                                HitSeconds = hitSeconds,
                            });
                    }
                    break;
                }

                case SkillEffectKind.Revive:
                {
                    // 复苏（B-3 ②，芭芭拉闪耀奇迹）：paramKey=Heal 键按 baseType 换算（BasedOnMaxHealth=
                    // 施法者最大生命——2026-09-30 拍板翻转，含受疗者治疗效率单源）；
                    // condition=TargetIsCorpse 保证目标为尸体，活体目标在条件闸已跳过；
                    // 应用=清尸体态+治疗（ReviveEffect 单效应原子化），命令=Revive
                    int amount = ResolveHealAmount(skillData, atom.paramKey, atom.value, attacker, target);
                    if (amount > 0)
                        effects.Add(new ReviveEffect(action.unitId, targetUnitId, amount));
                    break;
                }

                case SkillEffectKind.TriggerSkill:
                {
                    // 技能链（块内因果序——延奏→变奏串行展开，docs/active/22 §1）：目标该型技能的
                    // 结算产出并入本行动块；变奏未实装=ConfiguredSkill 空产出/UnimplementedSkill 空产出。
                    // 防环守卫（2026-09-25 三轮审查 S2）：TriggerSkill 数据驱动可递归再入 CompileSkill——
                    // 配置失误形成 A→B→A 循环会栈溢出炸 Host，深度超限 Warn 截断（数据错误须炸得优雅）
                    if (_triggerDepth >= MaxTriggerChainDepth)
                    {
                        GICLog.Warn($"[EffectCompiler] TriggerSkill 链深度超限（{MaxTriggerChainDepth}）——疑似循环触发配置（{action.unitId} {skillData.skillID}），截断");
                        break;
                    }
                    var owner = target;
                    if (owner.Skills == null) break;
                    for (int i = 0; i < owner.Skills.Count; i++)
                    {
                        var skill = owner.Skills[i];
                        if (skill?.RawData?.skillType != atom.targetSkillType) continue;
                        var chained = new ActionData
                        {
                            playerId = action.playerId,
                            unitId = targetUnitId,
                            actionType = ActionType.Skill,
                            skillIndex = i,
                            targetUnitId = "",
                        };
                        _triggerDepth++;
                        try
                        {
                            effects.AddRange(skill.ResolveEffects(sim, chained, snapshot));
                        }
                        finally
                        {
                            _triggerDepth--; // 异常路径也归零，防静态计数器跨行动/跨对局残留
                        }
                        break; // 每单位一个该型技能（变奏）
                    }
                    break;
                }

                default:
                    // 2026-09-27 复审修复（对齐 BattleEffectCommandAudit 哲学）：未接编译的原子 kind
                    // 此前静默 no-op——新 kind 忘接编译器/配置误填=效果无声丢失，Warn 暴露
                    GICLog.Warn($"[EffectCompiler] 未接编译的效果原子 kind={atom.kind}（{skillData?.skillID} → target={targetUnitId}）——" +
                                $"该原子不产出任何效应；新 kind 请在 CompileAtom 接入，误配请修 SkillConfig");
                    break;
            }
        }

        /// <summary>效果原子作用条件（B-3 ②，docs/11「IfCorpse 条件原子」）：按目标存活态过滤——
        /// TargetIsCorpse/TargetIsAlive（复苏与增益分支同技能分叉声明）；None=无条件。</summary>
        private static bool PassesCondition(SkillEffectConfig atom, UnitState targetState)
        {
            switch (atom.condition)
            {
                case SkillEffectCondition.TargetIsCorpse: return targetState.isCorpse != 0;
                case SkillEffectCondition.TargetIsAlive: return targetState.isCorpse == 0;
                default: return true;
            }
        }

        /// <summary>治疗量换算（docs/20 §5.1 基准纪律）：Fixed=value、BasedOnMaxHealth=百分比×**施法者**
        /// 最大生命（2026-09-30 用户拍板翻转旧「被治疗者各自」口径——GI 语义：治疗量随施法者成长、
        /// 与伤害基准 BasedOnMaxHealth=施法者对称）、BasedOnAttack=百分比×施法者攻击；
        /// paramKey=None 时用 value（Fixed 语义）。
        /// 末段乘**受疗者治疗效率**（协议核心批 2026-09-29 拍板「治疗效率-50%」——目标侧结算：
        /// 全单位默认 100=零行为变化；协议核心 50=守家续航减半防不死流；UnitStats.HealEfficiency 经
        /// Buff 修饰符同生效=GetFinalStat 口径）。internal=三脑治疗估值镜像（CompanionBrain/
        /// PlayerQuotaBrain）同走此单出口，勿再手抄公式（旧镜像已收口）</summary>
        internal static int ResolveHealAmount(SkillConfig.SkillData skillData, SkillParamKey paramKey, int fallbackValue,
            Unit attacker, Unit target)
        {
            var param = FindParam(skillData, paramKey != SkillParamKey.None ? paramKey : SkillParamKey.None);
            int rawValue;
            SkillBaseType baseType;
            if (param != null)
            {
                rawValue = param.value;
                baseType = param.baseType;
            }
            else
            {
                rawValue = fallbackValue;
                baseType = SkillBaseType.Fixed;
            }
            if (paramKey == SkillParamKey.None) baseType = SkillBaseType.Fixed;

            var attackerStats = attacker.GetUnitComponent<UnitStats>();
            var targetStats = target.GetUnitComponent<UnitStats>();
            int amount;
            switch (baseType)
            {
                case SkillBaseType.BasedOnMaxHealth:
                    // float 计算后末点截断（2026-09-27 拍板「最终治疗舍弃小数点」——FloorToInt；
                    // 消除中途 int 截断的双取整点，最终值与旧 int 截断口径一致：205 血×8%=16.4→16）
                    // 基准=施法者最大生命（2026-09-30 拍板翻转：原读被治疗者各自）
                    amount = attackerStats != null ? Mathf.FloorToInt(attackerStats.GetStatStruct(StatType.HP).Max * rawValue / 100f) : 0;
                    break;
                case SkillBaseType.BasedOnTargetMaxHealth:
                    // 目标档（枚举词汇表显式前缀对——「按目标自身体型奶」类语义；现役无配置=休眠，
                    // 展示层 SkillDescriptionBuilder 已渲染「目标最大生命值」，结算侧补全对齐 docs/20 §5.1）
                    amount = targetStats != null ? Mathf.FloorToInt(targetStats.GetStatStruct(StatType.HP).Max * rawValue / 100f) : 0;
                    break;
                case SkillBaseType.BasedOnAttack:
                    amount = attackerStats != null ? Mathf.FloorToInt(attackerStats.Attack * rawValue / 100f) : 0;
                    break;
                default: // Fixed/Percent=直读（Percent 语境百分比由技能语义指定，治疗无语境默认直读）
                    amount = rawValue;
                    break;
            }
            // 治疗效率双乘区（施法者+受疗者，self 单次——单出口见 ApplyHealEfficiency）
            return ApplyHealEfficiency(attacker, target, amount);
        }

        /// <summary>治疗效率乘区**单出口**（2026-10-01 拍板「治疗效率应当对所有的回血生效，无论是
        /// 吸血还是被治疗」+追加拍板「发起治疗者也应当乘上治疗效率，例如+50%治疗效率的芭芭拉治疗
        /// 其它角色；但自己治疗自己时，治疗效率不会乘两次」）：**双侧乘区**——施法者效率×受疗者效率
        /// /100（各自独立百分比，默认 100 恒等原值）；**施法者==受疗者只乘一次**（按该单位效率——
        /// 防 self 双乘：150% 效率奶自己=×150%，非 ×225%）；负效率钳 0、整数地板=末点截断口径。
        /// **一切回血统一消费**——被治疗（ResolveHealAmount 末段）、吸血（受疗者=攻击者自身=单次）、
        /// 光环 tick 治疗（歌声之环：施法者=施加者芭芭拉）、碎裂回血（寒冰之棱）勿在各回血点手抄
        /// 乘区；治疗估值链（AI 双脑走 ResolveHealAmount）自动同源</summary>
        internal static int ApplyHealEfficiency(Unit caster, Unit target, int amount)
        {
            if (amount <= 0) return 0;
            int casterEfficiency = EfficiencyOf(caster);
            int targetEfficiency = EfficiencyOf(target);
            // 同一单位=单次（self 双乘防线）；跨单位=双侧相乘归一（150%×150%→225）
            int efficiency = caster == target
                ? casterEfficiency
                : casterEfficiency * targetEfficiency / 100;
            if (efficiency == 100) return amount;
            return amount * Mathf.Max(0, efficiency) / 100;
        }

        /// <summary>单位治疗效率（无属性/无组件回落 100=恒等）</summary>
        private static int EfficiencyOf(Unit unit)
        {
            var stats = unit?.GetUnitComponent<UnitStats>();
            return stats != null ? stats.HealEfficiency : 100;
        }

        private static SkillParam FindParam(SkillConfig.SkillData skillData, SkillParamKey key)
        {
            if (skillData.customParams == null || key == SkillParamKey.None) return null;
            foreach (var p in skillData.customParams)
                if (p.key == key) return p;
            return null;
        }

        // ==================== 预判工厂（决策九 D5：预判/结算同形由 kind 强制） ====================

        /// <summary>方向命中预判按判定 kind 派发：LineProjectile=投射物圆柱接触静态形态、
        /// LineBurst/无轨=整线或 maxRange 距离段——与各判定编译路径同口径（静态快照预判，Host 结算权威）。
        /// 敌我判定=TeamType 口径（2026-09-25 三轮审查 C2：casterTeam=施法者队伍，2v2 不再误伤判定队友）</summary>
        public static bool WouldHitEnemyInDirection(SkillTimelineAsset timeline, BattleMapData map,
            BattleSnapshot snapshot, TeamType casterTeam, BattleCell from, Direction2D direction)
        {
            var projClips = SkillTimelineQuery.JudgmentClips(timeline, SkillJudgmentKind.LineProjectile);
            if (projClips.Count > 0)
                return WouldHitProjectile(map, snapshot, casterTeam, from, direction, projClips[0]);

            var burstClips = SkillTimelineQuery.JudgmentClips(timeline, SkillJudgmentKind.LineBurst);
            int maxRange = burstClips.Count > 0 && burstClips[0].maxRange > 0
                ? burstClips[0].maxRange : ProjectileRule.MaxRange;
            return WouldHitLineSegments(map, snapshot, casterTeam, from, direction, maxRange);
        }

        /// <summary>投射物圆柱接触预判（原安柏战技覆写泛化）：距离空间版 Host maxT/VoidBoundary 同口径；
        /// 敌方恒=快照格心（移动中命中不可预知，属提示非校验）。
        /// per-enemy 受击半径（协议核心批 2026-09-29）：技能 hitDiameter 覆写优先，否则该单位自身
        /// 受击圆柱（默认 0.42/协议核心 0.8）——与 ProjectileResolver 接触判定同口径，推荐色不漂移</summary>
        private static bool WouldHitProjectile(BattleMapData map, BattleSnapshot snapshot, TeamType casterTeam,
            BattleCell from, Direction2D direction, SkillTimelineClip clip)
        {
            var delta = SkillHitResolver.DirectionToDelta(direction);
            bool skillOverride = clip != null && clip.hitDiameter > 0f; // per-skill 判定圆柱覆写（替代单位圆柱）
            int maxRange = clip != null && clip.maxRange > 0 ? clip.maxRange : ProjectileRule.MaxRange;

            float maxDist = maxRange + 0.5f;
            for (int k = 1; k <= maxRange; k++)
            {
                if (map.HasTile(from.x + delta.x * k, from.y + delta.y * k)) continue;
                maxDist = k - 0.5f; // 首个虚空格近边界=弹道截断
                break;
            }

            var dir = new Vector2(delta.x, delta.y).normalized;
            var origin = new Vector2(from.x + 0.5f, from.y + 0.5f);
            foreach (var enemy in snapshot.units)
            {
                if ((TeamType)enemy.team == casterTeam) continue; // 含尸体——尸体完全算判定；敌我=TeamType（C2）
                float radius = (skillOverride ? clip.hitDiameter : BattleMetrics.CylinderDiameterOf(enemy)) * 0.5f;
                var rel = new Vector2(enemy.position.x + 0.5f, enemy.position.y + 0.5f) - origin;
                if (rel.sqrMagnitude <= radius * radius) return true; // 发射即贴脸（同格堆叠）
                float along = Vector2.Dot(rel, dir);
                float perpSq = rel.sqrMagnitude - along * along;
                if (perpSq > radius * radius) continue; // 弹道不穿该圆柱
                float entry = along - Mathf.Sqrt(radius * radius - perpSq);
                if (entry >= 0f && entry <= maxDist) return true;
            }
            return false;
        }

        /// <summary>整线/距离段预判（原凯亚霜袭覆写泛化）：前方 maxRange 格内有敌=推荐（虚空截断）</summary>
        private static bool WouldHitLineSegments(BattleMapData map, BattleSnapshot snapshot, TeamType casterTeam,
            BattleCell from, Direction2D direction, int maxRange)
        {
            var delta = SkillHitResolver.DirectionToDelta(direction);
            for (int step = 1; step <= maxRange; step++)
            {
                var cell = new BattleCell(from.x + delta.x * step, from.y + delta.y * step);
                if (!map.HasTile(cell.x, cell.y)) break;
                if (SkillHitResolver.FindEnemiesAt(snapshot, casterTeam, cell).Count > 0) return true;
            }
            return false;
        }
    }
}
