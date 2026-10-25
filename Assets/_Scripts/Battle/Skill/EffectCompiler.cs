using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
using GIC.Framework;
namespace GIC.Battle
{


    /// <summary>
    /// 效果原子编译器（B-1，docs/29 + docs/18 决策九）：把 SkillData.effects 效果原子声明
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
                bool valid = target != null && target.isCorpse == 0 && target.isSoul == 0 // 灵魂在地脉维度不可及（决策九十一——Host 权威防线，客户端候选域双保险）
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
                bool valid = target != null && target.isSoul == 0 // 灵魂在地脉维度不可及（决策九十一）——复苏/增益直指域均拒灵魂
                    && (TeamType)target.team == casterTeam;
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

        // ==================== 物品效果编译（2026-10-16 首个可打出物品批） ====================

        /// <summary>物品使用效果编译单出口（UseItemExecutor 手动打出 / UseHandItem 原子嵌套饮用共用）：
        /// 目标=饮用者经 ResolveCastTargets 解析（Target/Caster 同指饮用者；AllAllies=将来群体物品
        /// 自然生效）；物品原子约定 trigger=OnCast、数值单源=paramKey 引用 customParams（键名=枚举名
        /// 同构——描述占位符/参数表显示/效果值三方单源，2026-10-16 描述对齐返修；paramKey=None 时
        /// value 直读 Fixed 兜底；UseHandItem 原子 Warn 跳过防嵌套饮用递归）；施法者=饮用者（Heal
        /// 基准换算/治疗效率乘区按饮用者口径）。</summary>
        internal static void CompileItemUse(BattleSimState sim, string playerId, Unit drinker,
            BattleSnapshot snapshot, ItemConfig.ItemData itemData, List<BattleEffect> effects)
        {
            if (itemData == null || !itemData.HasEffects) return;
            if (drinker == null || !sim.TryGetUnitId(drinker, out var drinkerId)) return;
            var drinkerState = SkillHitResolver.FindUnitState(snapshot, drinkerId);
            if (drinkerState == null) return;
            // 合成使用行动（施法者=饮用者；playerId=物品归属玩家——UseHandItem 嵌套链随施放行动者）
            var useAction = new ActionData { playerId = playerId, unitId = drinkerId };
            foreach (var atom in itemData.effects)
            {
                if (atom == null || atom.trigger != SkillEffectTrigger.OnCast) continue;
                if (atom.kind == SkillEffectKind.UseHandItem)
                {
                    GICLog.Warn($"[EffectCompiler] 物品 {itemData.itemID} 效果配了 UseHandItem——" +
                                $"嵌套饮用无语义（防递归），跳过该原子");
                    continue;
                }
                // 数值单源预解析（2026-10-16 描述对齐返修）：paramKey≠None → 从 customParams 查值
                // 重写拷贝原子（paramKey→None+value→解析值——资产原子勿运行时改写故拷贝，CompileAtom
                // 通用 case 零改动；paramKey=None 原样返回=value 直读 Fixed 兜底）
                var compiled = ResolveItemAtom(itemData, atom);
                if (compiled == null) continue;
                foreach (var target in ResolveCastTargets(compiled, useAction, snapshot, sim, drinkerState, drinkerState, null))
                    CompileAtom(sim, useAction, snapshot, null, compiled, target.unitId, drinkerState,
                        0, 0, BattleCell.zero, 0f, 0f, 0f, 0f, effects, EnergyEffect.CategoryEnsoGain);
            }
        }

        /// <summary>物品原子数值预解析（CompileItemUse 用）：paramKey≠None 时从 customParams 按
        /// paramKey.ToString() 查值（键名=枚举名同构——描述占位符/参数表显示/效果值三方单源，技能
        /// 参数表同构模式）并重写**拷贝**原子（资产原子勿运行时改写；拷贝后 paramKey→None、value→
        /// 解析值）；**SanityCost 键=理智消耗绝对量（正数存储、SanityChange 原子 delta 取负——
        /// SkillCostEntry 消耗语义先例）**；customParams 无对应键=Warn 返回 null（配置错误暴露非静默）。</summary>
        private static SkillEffectConfig ResolveItemAtom(ItemConfig.ItemData itemData, SkillEffectConfig atom)
        {
            if (atom.paramKey == SkillParamKey.None) return atom;
            string keyName = atom.paramKey.ToString();
            int value = 0;
            bool found = false;
            if (itemData.customParams != null)
            {
                foreach (var p in itemData.customParams)
                {
                    if (p != null && p.key == keyName) { value = p.value; found = true; break; }
                }
            }
            if (!found)
            {
                GICLog.Warn($"[EffectCompiler] 物品 {itemData.itemID} 原子 paramKey={keyName} 在 customParams" +
                            $" 无对应条目，跳过该原子（物品数值单源=customParams，请补参数）");
                return null;
            }
            return new SkillEffectConfig
            {
                trigger = atom.trigger,
                kind = atom.kind,
                targetFilter = atom.targetFilter,
                condition = atom.condition,
                paramKey = SkillParamKey.None,
                paramKey2 = SkillParamKey.None,
                paramKey3 = SkillParamKey.None,
                radiusKey = SkillParamKey.None,
                value = atom.kind == SkillEffectKind.SanityChange && atom.paramKey == SkillParamKey.SanityCost
                    ? -value : value, // 理智消耗键=绝对量正数存储、原子 delta 取负（消耗语义约定）
                element = atom.element,
                elementLayers = atom.elementLayers,
                buffType = atom.buffType,
                buffAsset = atom.buffAsset,
                targetSkillType = atom.targetSkillType,
                statType = atom.statType,
                minConstellation = atom.minConstellation,
                requiredTag = atom.requiredTag,
                subType = atom.subType,
            };
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
                    CompileBurstFogBreak(sim, action, skillData, from, delta,
                        burstClips[0].startTime, burstClips[0].maxRange, effects); // 2命破雾（一次/施放——段循环外）
                }
                else
                {
                    CompileLineBurstSegment(sim, action, snapshot, skillData, from, delta,
                        damagePercent * damageCount, 0f, 0, effects); // 无时轮兜底：合并单段
                    CompileBurstFogBreak(sim, action, skillData, from, delta, 0f, 0, effects);
                }
                return;
            }

            // 直线投射物：逐发发射声明（逐发独立判定/附着/反应——决策八"推翻 B4 简化①"）；
            // projectileHeight=箭矢视觉高度（2026-10-05 对齐动画松弦位，纯视觉随命令透传）
            var projClips = SkillTimelineQuery.JudgmentClips(skillData.timeline, SkillJudgmentKind.LineProjectile);
            if (projClips.Count > 0)
            {
                foreach (var clip in projClips)
                    for (int i = 0; i < damageCount; i++)
                        effects.Add(new ProjectileEffect(action.unitId, action, damagePercent,
                            from, delta.x, delta.y, clip.startTime + clip.hitInterval * i,
                            clip.projectileSpeed, clip.hitDiameter, clip.maxRange, clip.projectileHeight));
            }
            else
            {
                effects.Add(new ProjectileEffect(action.unitId, action, damagePercent * damageCount,
                    from, delta.x, delta.y)); // 无时轮兜底：合并单发
            }
        }

        /// <summary>安柏 2命「爆发可以破除迷雾，每破除 C2FogEnergyThreshold 格迷雾获得 C2FogEnergyGain
        /// 元能」实装（docs/active/44 §9-②，2026-10-17 迷雾批追拍六；**每施放一次**——挂在段循环外）：
        /// Burst 整线迸发把线域格并入施法者队伍的 Host 探索账本（AI 雾限同享）+按**新探索格数**产元能
        /// 效应（HitSeconds=首段命中时刻——客户端到点跳变，WYSIWYG）。数据驱动门：施法者 Talent 命座
        /// 技能配了 C2Fog 两参数且命座≥2 才生效（安柏专属参数族，未配单位零消费）；FogMode=None=无迷雾
        /// 可破=零元能（沙盒/观察局零回归）。客户端表现层揭雾=BattleFog.RevealCells（PlayArrowRainCoroutine
        /// 同口径线域+同首段时刻）——表现层与 Host 各算各的账本，两端同口径恒一致</summary>
        private static void CompileBurstFogBreak(BattleSimState sim, ActionData action,
            SkillConfig.SkillData skillData, BattleCell from, Vector2Int delta,
            float firstHitSeconds, int clipMaxRange, List<BattleEffect> effects)
        {
            if (skillData.skillType != SkillType.Burst) return; // 玩家版口径「爆发」限定（霜袭等 Normal 线技不触发）
            if (sim.FogMode == BattleFogMode.None) return;      // 无迷雾=无破雾无元能
            var caster = sim.GetUnit(action.unitId);
            if (caster == null || caster.ConstellationLevel < 2) return; // 2命起
            var talent = FindTalentData(caster);
            int threshold = talent != null ? talent.GetInt(SkillParamKey.C2FogEnergyThreshold, 0) : 0;
            int gain = talent != null ? talent.GetInt(SkillParamKey.C2FogEnergyGain, 0) : 0;
            if (threshold <= 0 || gain <= 0) return; // 未配参数=无此被动（数据驱动门）

            int maxRange = clipMaxRange > 0 ? clipMaxRange : ProjectileRule.MaxRange;
            var cells = new List<BattleCell>(); // 线域格集（CompileLineBurstSegment 同口径）
            for (int step = 1; step <= maxRange; step++)
            {
                var cell = new BattleCell(from.x + delta.x * step, from.y + delta.y * step);
                if (!sim.Map.HasTile(cell.x, cell.y)) break;
                cells.Add(cell);
            }
            int newCells = sim.RevealFogCells(sim.GetTeamOf(action.playerId), cells);
            if (newCells > 0)
                effects.Add(new EnergyEffect(action.unitId, gain * (newCells / threshold),
                    EnergyEffect.CategoryFogBreakGain) { HitSeconds = firstHitSeconds });
        }

        /// <summary>施法者 Talent 命座技能数据（ConstellationApplier 同口径遍历；null=无 Talent 技能）</summary>
        private static SkillConfig.SkillData FindTalentData(Unit caster)
        {
            foreach (var skill in caster.Skills)
            {
                var d = skill?.RawData;
                if (d != null && d.skillType == SkillType.Talent) return d;
            }
            return null;
        }

        /// <summary>整线迸发单段：线上全目标各编译一次 OnHit（虚空=线终止；无截停——投放形态=整线天降）。
        /// **元素×地形交互（决策七十六）**：线上每格按技能元素突变——冰=水面格→薄冰（霜袭冻结，
        /// 含无单位格）、火=薄冰格→水面（箭雨天降语义=线域直接融、无需单位——与直线箭矢「命中单位才融」
        /// 的区分正落在形状分支上）；段内同格天然去重（每格至多产一条）</summary>
        private static void CompileLineBurstSegment(BattleSimState sim, ActionData action, BattleSnapshot snapshot,
            SkillConfig.SkillData skillData, BattleCell from, Vector2Int delta, int attackPercent,
            float launchSeconds, int clipMaxRange, List<BattleEffect> effects)
        {
            int maxRange = clipMaxRange > 0 ? clipMaxRange : ProjectileRule.MaxRange;
            var skillElement = SkillHitResolver.SkillElementOf(sim.GetUnit(action.unitId), skillData);
            for (int step = 1; step <= maxRange; step++)
            {
                var cell = new BattleCell(from.x + delta.x * step, from.y + delta.y * step);
                if (!sim.Map.HasTile(cell.x, cell.y)) break;

                // 元素×地形（线格突变——冰冻/火融；物理及其它元素不触发）；HitSeconds=波次段时刻
                // （箭雨多段融冰按波到点融化——WYSIWYG）
                if (skillElement == ElementType.Cryo)
                {
                    var tile = sim.Map.GetTile(cell.x, cell.y);
                    if ((tile == TileType.Water || tile == TileType.Swamp))
                        effects.Add(new TerrainChangeEffect(cell, (int)TileType.ThinIce) { HitSeconds = launchSeconds });
                }
                else if (skillElement == ElementType.Pyro)
                {
                    if (sim.Map.GetTile(cell.x, cell.y) == TileType.ThinIce)
                        effects.Add(new TerrainChangeEffect(cell, (int)TileType.Water) { HitSeconds = launchSeconds });
                }

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
                foreach (var target in ResolveCastTargets(atom, action, snapshot, sim, casterState, directTarget, skillData))
                    CompileAtom(sim, action, snapshot, skillData, atom, target.unitId, casterState,
                        0, 0, BattleCell.zero, 0f, 0f, 0f, 0f, effects, EnergyEffect.CategoryEnsoGain);
            }
        }

        /// <summary>OnCast 目标解析（群体筛选=多目标；filter 不满足返回空=跳过该原子；势力筛选=蒙德协奏规则 docs/07）。
        /// skillData=null（物品路径）时 CasterRadiusEnemies 半径不可解析=空产出（物品暂无此档）</summary>
        private static IEnumerable<UnitState> ResolveCastTargets(SkillEffectConfig atom, ActionData action,
            BattleSnapshot snapshot, BattleSimState sim, UnitState casterState, UnitState directTarget,
            SkillConfig.SkillData skillData)
        {
            switch (atom.targetFilter)
            {
                case SkillEffectTargetFilter.Caster:
                    yield return casterState;
                    break;
                case SkillEffectTargetFilter.CasterRadiusEnemies:
                {
                    // 变奏「全面侦查」（2026-10-17 追拍七）：以施法者（被协奏的安柏）为中心 radiusKey 格内
                    // 敌方存活各编译一次（切比雪夫；尸体不施加；半径=参数表 radiusKey 单源——EffectRadius）
                    int radius = skillData != null && atom.radiusKey != SkillParamKey.None
                        ? skillData.GetInt(atom.radiusKey) : 0;
                    if (radius <= 0) break;
                    var casterTeam = sim.GetTeamOf(action.playerId);
                    foreach (var u in snapshot.MainWorldUnits()) // 主世界域单源（②④迁移：灵魂维度隔离判据收口）
                        if ((TeamType)u.team != casterTeam && u.isCorpse == 0
                            && Math.Max(Math.Abs(u.position.x - casterState.position.x),
                                        Math.Abs(u.position.y - casterState.position.y)) <= radius)
                            yield return u;
                    break;
                }
                case SkillEffectTargetFilter.AllAllies:
                    // 我方全体存活（元气迸发——群体治疗各按目标自身 maxHp 换算）；
                    // 我军=TeamType 口径（2026-09-25 三轮审查 C2：2v2 含队友单位）；
                    // 灵魂排除（决策九十一：地脉维度与主世界互不可及）——已随 MainWorldUnits 收口（②④）
                    foreach (var u in snapshot.MainWorldUnits())
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

            // 元素×地形（决策七十六泛化拍板：「一切火元素伤害命中薄冰上的单位→其格融化为水面」——
            // 投射物直线箭矢命中/箭雨命中单位/将来火技能全经此口；tick 类火伤经 ResolveAuraHits 同判定。
            // 整线迸发（箭雨）已在线格层融过——敌人恰在薄冰上时此处重复产效应，SetTile/命令幂等无害。
            // HitSeconds=命中时刻——箭到才融〔WYSIWYG〕）
            if (SkillHitResolver.SkillElementOf(sim.GetUnit(action.unitId), skillData) == ElementType.Pyro)
            {
                var targetState = SkillHitResolver.FindUnitState(sliceSnapshot, targetUnitId);
                if (targetState != null
                    && sim.Map.GetTile(targetState.position.x, targetState.position.y) == TileType.ThinIce)
                    effects.Add(new TerrainChangeEffect(targetState.position, (int)TileType.Water)
                    {
                        HitSeconds = hitSeconds,
                    });
            }

            // 反应命中登记（决策二十五：反应双方 1:1 全消耗、等层零残留——**反应命中的来袭元素
            // 不附着**；本技能 AttachElement 原子对已反应目标跳过，无反应命中才附着〔覆盖〕）
            var reactedHits = new HashSet<string>();

            foreach (var atom in skillData.effects)
            {
                if (atom.trigger != SkillEffectTrigger.OnHit) continue;

                // 群体语义（水之浅唱治疗）：以施法者为中心 radiusKey 格内我方存活各编译一次（含施法者）
                if (atom.targetFilter == SkillEffectTargetFilter.CasterRadiusAllies)
                {
                    int radius = atom.radiusKey != SkillParamKey.None ? skillData.GetInt(atom.radiusKey) : 0;
                    if (radius <= 0) continue;
                    foreach (var ally in sliceSnapshot.MainWorldUnits()) // 主世界域单源（②④迁移）
                    {
                        if ((TeamType)ally.team != sim.GetTeamOf(action.playerId) || ally.isCorpse != 0) continue; // 尸体不治疗；灵魂排除已随 MainWorldUnits 收口（决策九十一）；我军=TeamType 口径（C2）
                        int dist = Math.Max(Math.Abs(ally.position.x - casterState.position.x),
                            Math.Abs(ally.position.y - casterState.position.y));
                        if (dist > radius) continue;
                        if (CompileAtom(sim, action, sliceSnapshot, skillData, atom, ally.unitId, casterState,
                                attackPercent, delivery, fromCell, hitPointX, hitPointY, launchSeconds, hitSeconds,
                                effects, EnergyEffect.CategorySkillHitGain))
                            reactedHits.Add(ally.unitId);
                    }
                    continue;
                }

                // 行动者语义（战技获能 B6a，docs/18 决策七）：targetFilter=Caster → 受益者=施法者
                // （行动者）——命中敌不充能、多次命中只获一次（同片应用层按 目标+类别 去重，TurnResolver）。
                // 2026-09-25 修复：此前三战技获能原子 targetFilter 误配 Target，+10 元能发给了被命中的敌人
                if (atom.targetFilter == SkillEffectTargetFilter.Caster)
                {
                    // 反应命中的来袭元素不附着（决策二十五）——Caster 指向的附着原子同样跳过
                    if (atom.kind == SkillEffectKind.AttachElement && reactedHits.Contains(action.unitId)) continue;
                    if (CompileAtom(sim, action, sliceSnapshot, skillData, atom, action.unitId, casterState,
                            attackPercent, delivery, fromCell, hitPointX, hitPointY, launchSeconds, hitSeconds,
                            effects, EnergyEffect.CategorySkillHitGain))
                        reactedHits.Add(action.unitId);
                    continue;
                }

                // 反应命中的来袭元素不附着（决策二十五）——命中目标的附着原子跳过
                if (atom.kind == SkillEffectKind.AttachElement && reactedHits.Contains(targetUnitId)) continue;
                if (CompileAtom(sim, action, sliceSnapshot, skillData, atom, targetUnitId, casterState,
                        attackPercent, delivery, fromCell, hitPointX, hitPointY, launchSeconds, hitSeconds,
                        effects, EnergyEffect.CategorySkillHitGain))
                    reactedHits.Add(targetUnitId);
            }
            return effects;
        }

        // ==================== 元素伤害统一出口（2026-10-01 拍板「与正常伤害一致，全部统一」=docs/18 决策二十三） ====================

        /// <summary>元素伤害统一编译出口：反应预判（附着读片内编译视图，融化=易伤/蒸发=增伤/冻结=控制/
        /// 超导=减防 Buff，docs/06）→ DamagePipeline 全乘区（防御/易伤/增伤；Request=null 纯附着事件跳过伤害）
        /// → 反应事实+冻结 Buff+1:1 消耗（视图即时推进）。**返回=来袭元素已被本出口处置完毕**——调用方
        /// 据此跳过自带附着：①发生反应（双方 1:1 全消耗、等层零残留，决策二十五）②0 层（纯伤害不反应
        /// 不附着——燃烧 DoT 终点语义，决策七十四修订决策二十三）③剩余层已在本出口内附着（docs/06 §6.3
        /// 「多余的元素仍然附着」）。无反应命中才由调用方附着〔覆盖〕。正常命中（CompileAtom Damage 原子）、
        /// 光环 tick（歌声之环/寒冰之棱）、反应 DoT（燃烧）、纯附着（环附着我方）共用。
        /// **冻结层兜底（决策七十四「冻结 Buff 可作为冰元素参与反应」）**：附着为空且目标冻结时，
        /// 冻结 Buff=第二冰源参与融化（火）/超导（雷）——每回合=1 层、消耗=min(层数, 冻结回合)回合、
        /// 归零立即解冻；水不与冻结层反应（平衡拍板「唯独冰水不反应，正常附着」）。
        /// 级别公式= min(双方层数)（docs/06 §6.3 多层消耗强化反应级别——2层火+2层冰=2级融化）</summary>
        public static bool CompileElementalDamage(BattleSimState sim, string attackerId, string targetUnitId,
            UnitState targetState, ElementType element, DamageRequest request,
            float launchSeconds, float hitSeconds, List<BattleEffect> effects,
            int delivery = 0, BattleCell fromCell = default, float hitPointX = 0f, float hitPointY = 0f,
            int incomingLayers = 1)
        {
            // 0 层=纯伤害不反应不附着（决策七十四修订决策二十三：燃烧 DoT 回归 0 层终点语义——
            // 仍视为火伤吃防御/易伤/增伤/暴击与「造成火伤」类触发器，但不参与反应预判、不附着）
            bool zeroLayer = incomingLayers <= 0;

            // 附着读片内编译视图（2026-10-01 双蒸发修复）：快照只是初值，同段前序命中的反应消耗/
            // 新附着随编译序对后续命中可见（安柏双箭打一层水=第一发蒸发消耗、第二发见空平直）
            var dyed = sim.GetCompileDye(targetUnitId, targetState);
            var dyedLayers = sim.GetCompileDyeLayers(targetUnitId, targetState);
            ReactionOutcome outcome = zeroLayer
                ? new ReactionOutcome()
                : ElementReactionResolver.Preview(dyed, dyedLayers, element, incomingLayers);

            // 冻结层兜底（决策七十四）：附着为空（含被前序命中反应消耗后）且目标处于冻结时，
            // 冻结 Buff 作为第二冰源参与反应——同片多命中逐发消耗（编译视图推进，双蒸发同机制）
            bool viaFreeze = false;
            if (!zeroLayer && !outcome.HasReaction && dyed == ElementType.Physical)
            {
                int freezeTurns = sim.GetCompileFreezeTurns(targetUnitId);
                if (freezeTurns > 0)
                {
                    var freezeOutcome = ElementReactionResolver.PreviewFreezeLayer(element, incomingLayers, freezeTurns);
                    if (freezeOutcome.HasReaction)
                    {
                        outcome = freezeOutcome;
                        viaFreeze = true;
                    }
                }
            }

            if (request != null)
            {
                // 暴击判定（2026-10-02 幸运暴击批，docs/18 决策二十七）：幸运=暴击率（20 点=20%、
                // 负值恒不暴、默认 0=无暴击）——roll 走 sim 种子 RNG 单出口（BattleSimState.RollChance，
                // 回放/联机确定性）；理智=暴击效果（默认 50=×1.5）经 CritBonus 注入独立乘区。
                // 本方法=全伤害唯一编译出口（正常命中/光环 tick/DoT 全经此），判定收拢于此=所有伤害
                // 都可暴击（docs/05 §5.6）；0 层伤害（燃烧）同经此=照常可暴
                var attackerStats = request.Attacker?.GetUnitComponent<UnitStats>();
                bool isCrit = attackerStats != null && sim.RollChance(attackerStats.Luck);
                if (isCrit) request.CritBonus = attackerStats.Sanity / 100f;

                request.VulnerabilityBonus = outcome.VulnerabilityBonus;
                request.DamageBonusDelta = outcome.DamageBonusDelta;
                var result = DamagePipeline.Calculate(request);
                if (!result.Cancelled && result.FinalDamage > 0)
                    effects.Add(new DamageEffect(attackerId, targetUnitId, result.FinalDamage, (int)element,
                        delivery, fromCell, hitPointX, hitPointY, outcome.ReactionType,
                        Mathf.RoundToInt(launchSeconds * 1000f))
                    { HitSeconds = hitSeconds, IsCrit = isCrit }); // 吸血治疗继承命中时刻（WYSIWYG）+暴击事实传命令层
            }
            if (outcome.HasReaction)
            {
                if (viaFreeze)
                {
                    // 冻结层消耗（决策七十四）：消耗=级别回合；归零立即解冻（应用层 ConsumeFreezeEffect）
                    effects.Add(new ConsumeFreezeEffect(attackerId, targetUnitId, outcome.FreezeConsumed));
                    sim.ConsumeCompileFreeze(targetUnitId, outcome.FreezeConsumed); // 后续命中见已扣减的冻结回合
                }
                else
                {
                    // 反应 1:1 消耗被反应附着（docs/06 §6.3）——段内后续命中/tick 不再见旧附着；
                    // 多层剩余层仍附着（「多余的元素仍然附着」）；等层零残留=Physical（决策二十五）
                    int dyedLeft = dyedLayers - outcome.DyedConsumed;
                    if (dyedLeft > 0) sim.SetCompileDye(targetUnitId, dyed, dyedLeft);
                    else sim.SetCompileDye(targetUnitId, ElementType.Physical);
                }
                // 反应事实载体（两路统一产出——冻结层路径附着本就为空，客户端清附着=无害 no-op；
                // 融化伤害已并入 DamageEffect；ConsumedLayers=应用层按层减附着，viaFreeze=0 无消耗）
                effects.Add(new ReactionEffect(attackerId, targetUnitId, outcome.ReactionType, outcome.Level)
                {
                    HitSeconds = hitSeconds, // 客户端到点清附着图标（反应消耗可见）
                    ConsumedLayers = viaFreeze ? 0 : outcome.DyedConsumed,
                });
                if (outcome.BuffType >= 0)
                {
                    // 反应类 Buff 具名身份（决策五十七+七十六）：超导减防走具名资产（outcome.BuffKey=
                    // Buff_Superconduct，与冰棱减防异资产同族共存）；冻结维持族首资产回落
                    // HitSeconds=命中时刻——冻结/超导徽章随箭到才挂（WYSIWYG，决策七十六复测返修）
                    var reactCfg = BuffConfig.OfType((BuffType)outcome.BuffType);
                    effects.Add(new ApplyBuffEffect(attackerId, targetUnitId, outcome.BuffType, outcome.Level,
                        buffKey: outcome.BuffKey ?? reactCfg?.name)
                    {
                        HitSeconds = hitSeconds,
                    });
                }
                // 多余的来袭层仍附着（docs/06 §6.3 双向：等层=不附着〔决策二十五〕；2层火 vs 1层冰=
                // 1级融化+剩1层火附着；2层火 vs 2回合冻结=2级融化清零全耗）
                int incomingLeft = incomingLayers - outcome.Level;
                if (incomingLeft > 0)
                {
                    effects.Add(new AttachElementEffect(attackerId, targetUnitId, (int)element, incomingLeft)
                    {
                        HitSeconds = hitSeconds, // 箭到才附着（WYSIWYG，决策七十六复测返修）
                    });
                    sim.SetCompileDye(targetUnitId, element, incomingLeft);
                }
            }
            return zeroLayer || outcome.HasReaction; // 0层/反应=来袭已被处置，调用方跳过自带附着
        }

        // ==================== 单原子编译（OnCast/OnHit 共用） ====================

        /// <summary>单原子编译（返回=该命中是否触发元素反应——CompileOnHit 据此跳过 AttachElement
        /// 原子：反应双方 1:1 全消耗、等层零残留，docs/06 §6.3/docs/18 决策二十五）</summary>
        private static bool CompileAtom(BattleSimState sim, ActionData action, BattleSnapshot snapshot,
            SkillConfig.SkillData skillData, SkillEffectConfig atom, string targetUnitId, UnitState casterState,
            int attackPercent, int delivery, BattleCell fromCell, float hitPointX, float hitPointY,
            float launchSeconds, float hitSeconds, List<BattleEffect> effects, int energyCategory)
        {
            var attacker = sim.GetUnit(action.unitId);
            var target = sim.GetUnit(targetUnitId);
            if (attacker == null || target == null) return false;
            var targetState = SkillHitResolver.FindUnitState(snapshot, targetUnitId);
            if (targetState == null) return false; // 快照中不存在（瞬发读片初状态）

            // 作用条件（B-3 ② 单点收口——OnCast 指向/OnHit 命中/施法者分支全过此闸）：
            // 按目标存活态过滤（复苏/增益分支分叉声明）/ tag 条件（TargetHasTag/TargetNotHasTag——
            // 目标 UnitTag 载荷过滤，首个消费方=蒲公英酒「酒鬼角色不减少理智」）；
            // None=无条件（存量原子零行为变化）
            if (!PassesCondition(atom, targetState, target)) return false;

            switch (atom.kind)
            {
                case SkillEffectKind.Damage:
                {
                    var attackerElement = attacker.GetUnitComponent<UnitElement>()?.SelfElement ?? ElementType.Physical;
                    var element = atom.element != ElementType.Physical ? atom.element : attackerElement;

                    // stats 取活态 UnitStats（当前片内无属性突变点=与片前快照恒等；未来若引入片中属性
                    // 变化——光环/移动触发效果等——须统一改读快照，防快照纪律分叉，2026-09-27 复审注记）
                    var request = new DamageRequest
                    {
                        Attacker = attacker,
                        Target = target,
                        Element = (int)element,
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
                    // 统一元素伤害出口（2026-10-01 拍板「全部统一」=docs/18 决策二十三）：正常命中/光环
                    // tick/反应 DoT 共用反应预判+全乘区链；返回=来袭已被处置（反应消耗/0层/剩余层附着）
                    // ——CompileOnHit 据此跳过本技能 AttachElement 原子（无反应才附着）
                    return CompileElementalDamage(sim, action.unitId, targetUnitId, targetState, element, request,
                        launchSeconds, hitSeconds, effects, delivery, fromCell, hitPointX, hitPointY,
                        atom.elementLayers); // 层数（决策七十四铺路：0=纯伤害/1=默认/N=多层强化级别）
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
                    // SourceSkillId=本技能（2026-10-06 头顶 Buff 图标批：延奏 AttackUp/凯亚加速/凛冽轮舞
                    // 寒冰之棱/闪耀奇迹歌声之环全经此原子——客户端按来源技能图标渲染）；
                    // TriggerSkill 链（延奏→变奏）按实际施加技能的 skillData 编译=来源随链归属正确。
                    // 决策五十七「资产即身份」：buffAsset 引用优先（具名 buff=资产名作 key，同资产叠层/
                    // 异资产同族共存）；null 回落 buffType（存量配置兼容——工厂按族找首资产）
                    // HitSeconds=命中时刻（OnCast 施放=0 立即；OnHit 命中类=箭到才挂徽章〔WYSIWYG，
                    // 决策七十六复测返修〕）
                    var resolvedBuffType = atom.buffAsset != null ? atom.buffAsset.buffType : atom.buffType;
                    string buffKey = atom.buffAsset != null ? atom.buffAsset.name : null;
                    int sourceSkillId = skillData != null ? (int)skillData.skillID : 0; // 物品原子（skillData=null）来源=0 无技能
                    effects.Add(new ApplyBuffEffect(action.unitId, targetUnitId, (int)resolvedBuffType, 1,
                        buffValue, stackLimit, duration, sourceSkillId, buffKey)
                    {
                        HitSeconds = hitSeconds,
                    });
                    break;
                }

                case SkillEffectKind.AttachElement:
                {
                    var attackerElement = attacker.GetUnitComponent<UnitElement>()?.SelfElement ?? ElementType.Physical;
                    var element = atom.element != ElementType.Physical ? atom.element : attackerElement;
                    if (element != ElementType.Physical) // 物理不附着（docs/06）
                    {
                        int layers = Mathf.Max(1, atom.elementLayers); // 附着原子层=0 视为 1（0层语义属 Damage 原子）
                        effects.Add(new AttachElementEffect(action.unitId, targetUnitId, (int)element, layers)
                        {
                            HitSeconds = hitSeconds, // 箭到才挂附着图标（WYSIWYG，决策七十六复测返修）
                        });
                        sim.SetCompileDye(targetUnitId, element, layers); // 覆盖语义对片内后续命中可见（双蒸发修复）
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
                    // 技能链（块内因果序——延奏→变奏串行展开，docs/22 §1）：目标该型技能的
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

                case SkillEffectKind.SanityChange:
                {
                    // 理智原子（2026-10-16 首个可打出物品批）：paramKey=None 时 value 直读（负=减少）；
                    // 产出 SanityEffect 单效应（应用层 ApplySanity 钳 -300~300 + StatChange(StatKindSanity)
                    // 命令链=歌声之环批既有链零新协议）；物品/技能/Buff 通用
                    int delta = atom.paramKey != SkillParamKey.None && skillData != null
                        ? skillData.GetInt(atom.paramKey) : atom.value;
                    effects.Add(new SanityEffect(targetUnitId, delta) { HitSeconds = hitSeconds });
                    break;
                }

                case SkillEffectKind.UseHandItem:
                {
                    // 使用手牌物品（2026-10-16 首个可打出物品批）：施放者玩家手牌持有 subType 子类型物品
                    // 则消耗 1 个并结算其效果（目标=本原子目标=饮用者）；无持有=空产出不阻断其他原子
                    //（「如果持有」条件语义——首个消费方=凯亚变奏冷血之剑「持有饮品会饮用1瓶」）。
                    // 消耗走 AnyItem 模式 ItemConsumeEffect（手牌列表序确定性扣减+客户端逐条镜像，
                    // 与 ResourceGate.Charge 同源效应）；物品效果编译=CompileItemUse 单出口（防递归：
                    // 物品效果原子不接 UseHandItem——嵌套饮用无语义，Warn 跳过）
                    var entries = ResourceGate.CollectAnyItems(sim, action.playerId, atom.subType);
                    if (entries.Count == 0) break; // 未持有=空产出（条件原子语义，非阻断）
                    var itemName = entries[0].item; // 手牌列表序首个（确定性，零随机）
                    effects.Add(new ItemConsumeEffect(action.playerId, atom.subType, 1));
                    var itemConfig = Wargame.Instance?.Context?.Get<ItemConfig>();
                    var itemData = itemConfig?.GetItemData(itemName);
                    if (itemData != null && itemData.HasEffects)
                        CompileItemUse(sim, action.playerId, target, snapshot, itemData, effects);
                    break;
                }

                default:
                    // 2026-09-27 复审修复（对齐 BattleEffectCommandAudit 哲学）：未接编译的原子 kind
                    // 此前静默 no-op——新 kind 忘接编译器/配置误填=效果无声丢失，Warn 暴露
                    GICLog.Warn($"[EffectCompiler] 未接编译的效果原子 kind={atom.kind}（{skillData?.skillID} → target={targetUnitId}）——" +
                                $"该原子不产出任何效应；新 kind 请在 CompileAtom 接入，误配请修 SkillConfig");
                    break;
            }
            return false;
        }

        /// <summary>效果原子作用条件（B-3 ②，docs/11「IfCorpse 条件原子」）：按目标存活态过滤——
        /// TargetIsCorpse/TargetIsAlive（复苏与增益分支同技能分叉声明）；tag 条件（2026-10-16 首个可
        /// 打出物品批）TargetHasTag/TargetNotHasTag=目标 UnitTag 载荷过滤（UnitTag 的首个效果条件
        /// 消费——此前 tags 运行时唯一逻辑消费=Core 核心判定）；None=无条件。</summary>
        private static bool PassesCondition(SkillEffectConfig atom, UnitState targetState, Unit target)
        {
            switch (atom.condition)
            {
                case SkillEffectCondition.TargetIsCorpse: return targetState.isCorpse != 0;
                case SkillEffectCondition.TargetIsAlive: return targetState.isCorpse == 0;
                case SkillEffectCondition.TargetHasTag: return HasTag(target, atom.requiredTag);
                case SkillEffectCondition.TargetNotHasTag: return !HasTag(target, atom.requiredTag);
                default: return true;
            }
        }

        /// <summary>目标单位 tag 判定（tag 条件原子用）：requiredTag=None=恒过（载荷缺配置防御）；
        /// 无 RawData/无 tags=不含（协议核心等代码生成单位天然不过 HasTag 档）</summary>
        private static bool HasTag(Unit unit, UnitTag tag)
        {
            if (tag == UnitTag.None) return true;
            var tags = unit?.RawData?.tags;
            if (tags == null) return false;
            return System.Array.IndexOf(tags, tag) >= 0;
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
            if (skillData == null || skillData.customParams == null || key == SkillParamKey.None) return null;
            foreach (var p in skillData.customParams)
                if (p.key == key) return p;
            return null;
        }

        // ==================== 预判工厂（决策九 D5：预判/结算同形由 kind 强制） ====================

        /// <summary>直线技能方向射程单源（格）：LineProjectile clip 优先（maxRange 0=回落
        /// ProjectileRule.MaxRange），否则 LineBurst clip，再回落默认——与 <see cref="WouldHitEnemyInDirection"/>
        /// 两分支的射程解析同式。消费方=HUD 直线瞄准臂长/箭头长度（2026-10-15 拍板「瞄准显示的距离
        /// 应当和射出飞行物距离一致」：安柏双箭/箭雨 24、芭芭拉水球 5、凯亚霜袭 2、丘丘人挥棒 1）</summary>
        public static int DirectionRangeOf(SkillTimelineAsset timeline)
        {
            var projClips = SkillTimelineQuery.JudgmentClips(timeline, SkillJudgmentKind.LineProjectile);
            if (projClips.Count > 0)
                return projClips[0].maxRange > 0 ? projClips[0].maxRange : ProjectileRule.MaxRange;
            var burstClips = SkillTimelineQuery.JudgmentClips(timeline, SkillJudgmentKind.LineBurst);
            if (burstClips.Count > 0 && burstClips[0].maxRange > 0)
                return burstClips[0].maxRange;
            return ProjectileRule.MaxRange;
        }

        /// <summary>方向命中预判按判定 kind 派发：LineProjectile=投射物圆柱接触静态形态、
        /// LineBurst/无轨=整线或 maxRange 距离段——与各判定编译路径同口径（静态快照预判，Host 结算权威）。
        /// 敌我判定=TeamType 口径（2026-09-25 三轮审查 C2：casterTeam=施法者队伍，2v2 不再误伤判定队友）</summary>
        public static bool WouldHitEnemyInDirection(SkillTimelineAsset timeline, BattleMapData map,
            BattleSnapshot snapshot, TeamType casterTeam, BattleCell from, Direction2D direction)
        {
            var projClips = SkillTimelineQuery.JudgmentClips(timeline, SkillJudgmentKind.LineProjectile);
            if (projClips.Count > 0)
                return WouldHitProjectile(map, snapshot, casterTeam, from, direction, projClips[0]);

            return WouldHitLineSegments(map, snapshot, casterTeam, from, direction, DirectionRangeOf(timeline));
        }

        /// <summary>方向预计命中单位枚举（2026-10-15 瞄准预览批）：与 <see cref="WouldHitEnemyInDirection"/>
        /// 同形派发——LineProjectile=弹道穿过的全部敌方受击圆柱（含尸体，Host 同语义）；LineBurst=射程内
        /// 逐格全部敌方。into 先清后填（调用方复用防逐帧分配）；静态快照预判，Host 结算权威</summary>
        public static void PredictDirectionHits(SkillTimelineAsset timeline, BattleMapData map,
            BattleSnapshot snapshot, TeamType casterTeam, BattleCell from, Direction2D direction,
            List<UnitState> into)
        {
            into.Clear();
            if (timeline == null) return;
            var projClips = SkillTimelineQuery.JudgmentClips(timeline, SkillJudgmentKind.LineProjectile);
            if (projClips.Count > 0)
            {
                PredictProjectileHits(map, snapshot, casterTeam, from, direction, projClips[0], into);
                return;
            }
            PredictLineSegmentHits(map, snapshot, casterTeam, from, direction, DirectionRangeOf(timeline), into);
        }

        /// <summary>投射物圆柱接触预判的命中单位枚举版（弹道+maxDist 与 WouldHitProjectile 同式）</summary>
        private static void PredictProjectileHits(BattleMapData map, BattleSnapshot snapshot, TeamType casterTeam,
            BattleCell from, Direction2D direction, SkillTimelineClip clip, List<UnitState> into)
        {
            var delta = SkillHitResolver.DirectionToDelta(direction);
            bool skillOverride = clip != null && clip.hitDiameter > 0f;
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
            foreach (var enemy in snapshot.MainWorldUnits()) // 主世界域单源（②④迁移：灵魂维度隔离判据收口）
            {
                if ((TeamType)enemy.team == casterTeam) continue; // 含尸体——尸体完全算判定；敌我=TeamType（C2）
                float radius = (skillOverride ? clip.hitDiameter : BattleMetrics.CylinderDiameterOf(enemy)) * 0.5f;
                var rel = new Vector2(enemy.position.x + 0.5f, enemy.position.y + 0.5f) - origin;
                if (rel.sqrMagnitude <= radius * radius) { into.Add(enemy); continue; } // 发射即贴脸（同格堆叠）
                float along = Vector2.Dot(rel, dir);
                float perpSq = rel.sqrMagnitude - along * along;
                if (perpSq > radius * radius) continue; // 弹道不穿该圆柱
                float entry = along - Mathf.Sqrt(radius * radius - perpSq);
                if (entry >= 0f && entry <= maxDist) into.Add(enemy);
            }
        }

        /// <summary>整线/距离段预判的命中单位枚举版（射程内逐格与 WouldHitLineSegments 同式）</summary>
        private static void PredictLineSegmentHits(BattleMapData map, BattleSnapshot snapshot, TeamType casterTeam,
            BattleCell from, Direction2D direction, int maxRange, List<UnitState> into)
        {
            var delta = SkillHitResolver.DirectionToDelta(direction);
            for (int step = 1; step <= maxRange; step++)
            {
                var cell = new BattleCell(from.x + delta.x * step, from.y + delta.y * step);
                if (!map.HasTile(cell.x, cell.y)) break;
                into.AddRange(SkillHitResolver.FindEnemiesAt(snapshot, casterTeam, cell));
            }
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
            foreach (var enemy in snapshot.MainWorldUnits()) // 主世界域单源（②④迁移：灵魂维度隔离判据收口）
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
