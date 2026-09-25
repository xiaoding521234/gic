using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// AI 玩家脑（B6b 重构；2026-09-25 v2 强化=评分制）：操控己方**高级单位**（3~5 星，docs/04 §4.1）
    /// ——低级单位由 LowUnitBrain 自主决策。AI 决策由 Host 生成（docs/18 决策一）——直接读
    /// BattleSimState/BattleSnapshot，不走传输通道；选择阶段开始后延迟提交（给玩家看清回合切换）。
    ///
    /// v2 评分制（取代 v1 优先级链）：全候选评分取最优——
    /// ① 攻击技能（战技/爆发）：CanCast+元能+体力三门槛、十字四向逐向预判（WouldHitEnemyInDirection
    /// 与 HUD 瞄准推荐/结算形态同源），评分=逐目标有效伤害（过杀截断）+斩杀加成+战技命中获能；
    /// ② 延奏：按效果原子估值（治疗按缺口、增益按未满层、变奏链/协奏元能加成），蒙德或自身目标；
    /// ③ 移动：体力够时十字逼近最近敌（方向纪律 docs/18 决策八，勿八向）；④ 无候选=Pass。
    /// 体力感知（B6d）：体力不足的配额行动直接不生成候选（上交也会落空浪费回合，先攒体力）。
    /// 确定性：unitId 升序/skillIndex 升序/十字向枚举序遍历，严格大于替换（同分先到先得）。
    /// </summary>
    public class AIDebugBrain : MonoBehaviour
    {
        private BattleSession _session;
        private string _playerId;
        private int _lastSubmittedTurn = -1;

        /// <summary>AI 提交延迟（秒）——模拟"思考"，同时让回合切换肉眼可辨</summary>
        private const float SubmitDelaySeconds = 0.8f;

        // ==================== 评分口径（内部常量，勿散写调用处） ====================

        /// <summary>斩杀加成：预估伤害 ≥ 目标当前生命（保守估值，未计反应乘区）</summary>
        private const int KillBonusScore = 80;

        /// <summary>战技命中获能 +10 的评分（B6a：向爆发攒能的行动价值）</summary>
        private const int SkillHitEnergyScore = 12;

        /// <summary>增益 Buff 每原子的行动溢价（叠层/长效增益优于原地空过的粗估）</summary>
        private const int EnsoBuffActionScore = 15;

        /// <summary>被协者已实装变奏（TriggerSkill 链）的评分</summary>
        private const int EnsoTriggerChainScore = 12;

        /// <summary>协奏元能转移（+10/+20）的评分</summary>
        private const int EnsoEnergyTransferScore = 4;

        /// <summary>治疗候选门槛：有效治疗 ≥ 治疗量一半才值得占行动（勿为挠痒花元能）</summary>
        private const int HealWorthRatioPercent = 50;

        /// <summary>移动兜底分（低于一切攻击/有效延奏候选）</summary>
        private const int MoveBaseScore = 10;

        /// <summary>移动每逼近 1 格的加分</summary>
        private const int MoveProgressScorePerCell = 2;

        /// <summary>近敌接敌加分上限（离最近敌越近越优先动该单位）</summary>
        private const int MoveEngageBonusMax = 8;

        /// <summary>走位进射击线加分（移动落点即有可开火攻击线=下回合可输出）</summary>
        private const int MoveLineUpScore = 8;

        public void Bind(BattleSession session, string playerId)
        {
            _session = session;
            _playerId = playerId;
            session.Flow.OnPhaseChanged += OnPhaseChanged;
        }

        private void OnDestroy()
        {
            if (_session != null && _session.Flow != null)
                _session.Flow.OnPhaseChanged -= OnPhaseChanged;
        }

        private void OnPhaseChanged(BattlePhase phase, int turn)
        {
            if (phase != BattlePhase.Selecting) return;
            if (turn == _lastSubmittedTurn) return; // 同回合防重复提交
            StartCoroutine(SubmitActionRoutine(turn));
        }

        private IEnumerator SubmitActionRoutine(int turn)
        {
            yield return new WaitForSeconds(SubmitDelaySeconds);

            // 回合已推进（战斗结束/状态机停止）则放弃
            if (_session == null || _session.Flow == null) yield break;
            if (_session.Flow.Phase != BattlePhase.Selecting || _session.Flow.TurnNumber != turn) yield break;

            var action = Decide(turn);
            _session.SubmitAction(action);
            _lastSubmittedTurn = turn;
        }

        // ==================== v2 评分制决策 ====================

        /// <summary>
        /// 候选枚举序（确定性）：己方存活高级单位 unitId 升序 × 技能 skillIndex 升序 × 十字向
        /// 枚举序 × 延奏目标 unitId 升序；严格大于替换（同分先到先得）。
        /// Pass=0 分兜底：全部候选评分 &gt;0 才可能替换
        /// </summary>
        private ActionData Decide(int turn)
        {
            var sim = _session.Sim;
            // 决策快照（纯读，与选择阶段广播同源状态）——预判/目标评估的唯一状态源
            var snapshot = sim.TakeSnapshot(turn);

            var majors = CollectMajors(sim);
            if (majors.Count == 0)
                return Pass(turn);

            var tracker = new CandidateTracker();
            foreach (var entry in majors)
                EvaluateAttackSkills(sim, snapshot, entry, turn, tracker);
            foreach (var entry in majors)
                EvaluateEnsoSkills(sim, entry, turn, tracker);
            foreach (var entry in majors)
                EvaluateMove(sim, snapshot, entry, turn, tracker);

            return tracker.Best ?? Pass(turn);
        }

        private List<(string unitId, Unit unit)> CollectMajors(BattleSimState sim)
        {
            var majors = new List<(string unitId, Unit unit)>();
            foreach (var kv in sim.Units)
            {
                if (!BattleHeuristics.IsMajorUnit(kv.Value)) continue;
                if (BattleSimState.IsDead(kv.Value) || !BattleSimState.CanAct(kv.Value)) continue;
                var id = kv.Value.GetUnitComponent<UnitIdentity>();
                if (id == null || id.OwnerPlayerID != _playerId) continue;
                majors.Add((kv.Key, kv.Value));
            }
            // unitId 升序（枚举序铁律，决策确定性）
            majors.Sort((a, b) => string.CompareOrdinal(a.unitId, b.unitId));
            return majors;
        }

        /// <summary>
        /// ① 攻击技能候选（战技/爆发）：CanCast（占位技能跳过——凛冽轮舞/闪耀奇迹未实装不可施放）+
        /// 元能门槛（B6a：消耗值非上限）+ 体力门槛（B6d：不足则上交也落空，不生成候选）；
        /// 十字四向逐向预判+逐目标评分（预判与结算形态同源=WouldHitEnemyInDirection）
        /// </summary>
        private void EvaluateAttackSkills(BattleSimState sim, BattleSnapshot snapshot,
            (string unitId, Unit unit) entry, int turn, CandidateTracker tracker)
        {
            var unit = entry.unit;
            var skills = unit.Skills;
            var from = sim.GetPosition(unit);

            for (int i = 0; i < skills.Count; i++)
            {
                var skill = skills[i];
                var data = skill?.RawData;
                if (data == null) continue;
                if (data.skillType != SkillType.Normal && data.skillType != SkillType.Burst) continue;
                if (!skill.CanCast(unit)) continue;

                if (!BattleSimState.HasEnoughEnergy(unit, BattleSimState.GetEnergyCost(data))) continue;
                if (!sim.HasEnoughStamina(_playerId, BattleSimState.GetStaminaCost(data))) continue;

                int perTarget = BattleHeuristics.EstimatePerTargetDamage(unit, data);
                if (perTarget <= 0) continue;

                foreach (var direction in BattleHeuristics.CrossDirections)
                {
                    if (!skill.WouldHitEnemyInDirection(sim.Map, snapshot, _playerId, from, direction))
                        continue;
                    var targets = BattleHeuristics.PreviewLineTargets(sim, snapshot, _playerId, from, data, direction);
                    if (targets.Count == 0) continue; // 纯尸体线：不浪费行动

                    int score = 0;
                    foreach (var target in targets)
                    {
                        score += Math.Min(perTarget, target.hp); // 过杀部分不重复计分
                        if (perTarget >= target.hp) score += KillBonusScore;
                    }
                    if (data.skillType == SkillType.Normal)
                        score += SkillHitEnergyScore; // 战技命中 +10 元能（爆发/延奏命中不获能）

                    tracker.Offer(score, Skill(unit, i, direction, turn));
                }
            }
        }

        /// <summary>
        /// ② 延奏候选（Enso，单位指向）：元能门槛（延奏 0 体力=B6d 豁免）；目标=蒙德或自身
        /// （非蒙德效果原子空产出，docs/07 协奏规则）；估值=治疗缺口+增益未满层+变奏链+协奏元能
        /// </summary>
        private void EvaluateEnsoSkills(BattleSimState sim,
            (string unitId, Unit unit) entry, int turn, CandidateTracker tracker)
        {
            var unit = entry.unit;
            var skills = unit.Skills;

            for (int i = 0; i < skills.Count; i++)
            {
                var skill = skills[i];
                var data = skill?.RawData;
                if (data == null || data.skillType != SkillType.Enso) continue;
                if (!skill.CanCast(unit)) continue;
                if (!BattleSimState.HasEnoughEnergy(unit, BattleSimState.GetEnergyCost(data))) continue;

                // 候选目标：己方存活单位（含自身）unitId 升序
                var allies = new List<(string unitId, Unit unit)>();
                foreach (var kv in sim.Units)
                {
                    if (BattleSimState.IsDead(kv.Value)) continue;
                    var id = kv.Value.GetUnitComponent<UnitIdentity>();
                    if (id == null || id.OwnerPlayerID != _playerId) continue;
                    allies.Add((kv.Key, kv.Value));
                }
                allies.Sort((a, b) => string.CompareOrdinal(a.unitId, b.unitId));

                foreach (var ally in allies)
                {
                    if (!BattleHeuristics.IsMondstadtOrSelfUnit(unit, ally.unit)) continue;
                    int value = EvaluateEnsoTarget(data, unit, ally.unit);
                    if (value <= 0) continue;
                    tracker.Offer(value, Skill(unit, i, Direction2D.Right, turn, ally.unitId));
                }
            }
        }

        /// <summary>延奏对某目标的估值：按 OnCast 效果原子逐个粗估（与 EffectCompiler 产出同语义）</summary>
        private int EvaluateEnsoTarget(SkillConfig.SkillData data, Unit caster, Unit ally)
        {
            if (data.effects == null) return 0;
            var allyStats = ally.GetUnitComponent<UnitStats>();
            if (allyStats == null) return 0;
            int value = 0;

            foreach (var atom in data.effects)
            {
                if (atom.trigger != SkillEffectTrigger.OnCast) continue;

                switch (atom.kind)
                {
                    case SkillEffectKind.Heal:
                    {
                        // 治疗换算（EffectCompiler.ResolveHealAmount 同语义）：BasedOnMaxHealth=目标
                        // 各自、BasedOnAttack=施法者、Fixed=直读；有效治疗=min(治疗量, 缺口)
                        var param = FindHealParam(data, atom);
                        int raw = param?.value ?? atom.value;
                        var baseType = param?.baseType ?? SkillBaseType.Fixed;
                        int heal;
                        if (baseType == SkillBaseType.BasedOnMaxHealth)
                            heal = allyStats.GetStatStruct(StatType.HP).Max * raw / 100;
                        else if (baseType == SkillBaseType.BasedOnAttack)
                            heal = caster.GetUnitComponent<UnitStats>()?.Attack * raw / 100 ?? 0;
                        else
                            heal = raw;
                        int missing = Math.Max(0, allyStats.GetStatStruct(StatType.HP).Max - allyStats.HP);
                        int effective = Math.Min(heal, missing);
                        if (effective * 100 < heal * HealWorthRatioPercent) break; // 缺口不足半量：不占行动
                        value += effective;
                        break;
                    }

                    case SkillEffectKind.ApplyBuff:
                    {
                        // 增益：目标该类 Buff 已满层=零价值，否则=BuffValue+行动溢价
                        int stackLimit = atom.paramKey2 != SkillParamKey.None ? data.GetInt(atom.paramKey2, 0) : 0;
                        var existing = ally.Buffs.Find(b => b.Type == (BuffType)atom.buffType);
                        if (existing != null && stackLimit > 0 && existing.Level >= stackLimit) break;
                        int buffValue = atom.paramKey != SkillParamKey.None ? data.GetInt(atom.paramKey, 0) : atom.value;
                        value += buffValue + EnsoBuffActionScore;
                        break;
                    }

                    case SkillEffectKind.TriggerSkill:
                    {
                        // 变奏链：被协者该型技能已实装（effects 非空）才有产出（UnimplementedSkill 空产出）
                        if (HasImplementedSkillOfType(ally, atom.targetSkillType))
                            value += EnsoTriggerChainScore;
                        break;
                    }

                    case SkillEffectKind.EnergyGain:
                        // 协奏元能 +10/+20（蒙德/非蒙德分支；AI 只选蒙德或自身目标=恒 +10 档）
                        value += EnsoEnergyTransferScore;
                        break;
                }
            }
            return value;
        }

        /// <summary>治疗参数查找（EffectCompiler.ResolveHealAmount 的 paramKey→None 兜底同构）</summary>
        private static SkillParam FindHealParam(SkillConfig.SkillData data, SkillEffectConfig atom)
        {
            var key = atom.paramKey != SkillParamKey.None ? atom.paramKey : SkillParamKey.None;
            if (key == SkillParamKey.None || data.customParams == null) return null;
            foreach (var p in data.customParams)
                if (p.key == key) return p;
            return null;
        }

        /// <summary>单位是否持有指定类型的已实装技能（effects 非空；TriggerSkill 链估值用）</summary>
        private static bool HasImplementedSkillOfType(Unit unit, SkillType skillType)
        {
            var skills = unit?.Skills;
            if (skills == null) return false;
            foreach (var skill in skills)
            {
                if (skill?.RawData == null || skill.RawData.skillType != skillType) continue;
                return skill.RawData.HasEffects;
            }
            return false;
        }

        /// <summary>
        /// ③ 移动候选：体力够（B6d 配额行动）且最近敌不在同格——十字逼近（主轴先行，方向纪律
        /// docs/18 决策八）；步数=距离与移速上限取小（被挡停格前=预期，B6a 被挡也算已使用获能）。
        /// 评分=兜底分+逼近进度+近敌接敌加分（离敌越近的单位越优先动）+走位进射击线加分
        /// （落点即有可开火攻击线=下回合可输出，远程单位走向射击行列而非盲目扎堆）
        /// </summary>
        private void EvaluateMove(BattleSimState sim, BattleSnapshot snapshot,
            (string unitId, Unit unit) entry, int turn, CandidateTracker tracker)
        {
            if (!sim.HasEnoughStamina(_playerId, BattleMetrics.StaminaCostPerAction)) return;

            var enemy = BattleHeuristics.FindNearestEnemy(sim, entry.unit);
            if (enemy == null) return;

            var from = sim.GetPosition(entry.unit);
            var to = sim.GetPosition(enemy);
            int dx = to.x - from.x;
            int dy = to.y - from.y;
            if (dx == 0 && dy == 0) return; // 同格堆叠：无逼近意义

            var direction = BattleHeuristics.BestCrossApproachDirection(dx, dy);
            if (direction == 0) return;

            int distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
            int steps = Math.Min(distance, MoveExecutor.MaxMoveDistance(entry.unit));
            if (steps <= 0) return;

            // 逼近进度：沿主轴走 steps 格后的切比雪夫距离缩减量
            var step = MovementResolver.StepVector(direction);
            int projected = Math.Max(Math.Abs(dx - step.x * steps), Math.Abs(dy - step.y * steps));
            int progress = Math.Max(0, distance - projected);

            int score = MoveBaseScore
                        + progress * MoveProgressScorePerCell
                        + Math.Min(MoveEngageBonusMax, Math.Max(0, 16 - distance) / 2);

            // 走位进射击线：落点即有可开火攻击线（近似预判——被挡提前停/移动获能未计入，粗估即可）
            var projectedCell = new BattleCell(from.x + step.x * steps, from.y + step.y * steps);
            if (WouldHaveFiringLineFrom(sim, snapshot, entry.unit, projectedCell))
                score += MoveLineUpScore;

            tracker.Offer(score, new ActionData
            {
                playerId = _playerId,
                unitId = entry.unitId,
                actionType = ActionType.Move,
                direction = direction,
                moveMagnitude = steps,
                turnNumber = turn,
            });
        }

        /// <summary>落点是否有可开火攻击线（任一可施放攻击技能从该点十字向可命中存活敌）——走位估值用</summary>
        private bool WouldHaveFiringLineFrom(BattleSimState sim, BattleSnapshot snapshot, Unit unit, BattleCell cell)
        {
            var skills = unit.Skills;
            for (int i = 0; i < skills.Count; i++)
            {
                var skill = skills[i];
                var data = skill?.RawData;
                if (data == null) continue;
                if (data.skillType != SkillType.Normal && data.skillType != SkillType.Burst) continue;
                if (!skill.CanCast(unit)) continue;
                if (!BattleSimState.HasEnoughEnergy(unit, BattleSimState.GetEnergyCost(data))) continue;
                if (BattleHeuristics.EstimatePerTargetDamage(unit, data) <= 0) continue;

                foreach (var direction in BattleHeuristics.CrossDirections)
                {
                    if (!skill.WouldHitEnemyInDirection(sim.Map, snapshot, _playerId, cell, direction)) continue;
                    if (BattleHeuristics.PreviewLineTargets(sim, snapshot, _playerId, cell, data, direction).Count > 0)
                        return true;
                }
            }
            return false;
        }

        // ==================== 行动构造 ====================

        private ActionData Skill(Unit unit, int skillIndex, Direction2D direction, int turn, string targetUnitId = null)
        {
            return new ActionData
            {
                playerId = _playerId,
                unitId = unit.GetUnitComponent<UnitIdentity>()?.UnitID,
                actionType = ActionType.Skill,
                skillIndex = skillIndex,
                direction = direction,
                targetUnitId = targetUnitId,
                turnNumber = turn,
            };
        }

        private ActionData Pass(int turn)
        {
            return new ActionData
            {
                playerId = _playerId,
                actionType = ActionType.Pass,
                turnNumber = turn,
            };
        }

        /// <summary>候选追踪：严格大于替换（同分先到先得——枚举序即决策确定性）</summary>
        private sealed class CandidateTracker
        {
            public ActionData Best { get; private set; }
            private int _bestScore;

            public void Offer(int score, ActionData action)
            {
                if (action == null || score <= _bestScore) return;
                _bestScore = score;
                Best = action;
            }
        }
    }
}
