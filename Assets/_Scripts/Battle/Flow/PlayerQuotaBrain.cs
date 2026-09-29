using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// AI 玩家配额脑（B6b 起；D 批次操控分层 docs/active/32 §6 收窄+2026-09-29 术语迁移改名
    /// AIDebugBrain→PlayerQuotaBrain：原 v2 评分制的**单位级评估搬迁为伙伴自主脑（CompanionBrain）**，
    /// 本脑只管玩家 1 配额的用途——①号令（上交势力技能延奏/契约，施法者=己方眷属/伙伴/魔神均可）
    /// ②操魔神（5 星单位行动=CompanionBrain 共享评分骨架复用为候选打分器）③部署/升命/购买/物品
    /// （后续批次接线）④无候选=Pass 空过放权自主军团。与真人玩家对等：配额同样可用于号令/魔神
    /// （§10 验收「AI 玩家对等」）。
    /// AI 决策由 Host 生成（docs/18 决策一）——直接读 BattleSimState/BattleSnapshot，不走传输通道；
    /// 选择阶段开始后延迟提交（给玩家看清回合切换）。
    /// 体力感知：配额行动 costs 门槛镜像（ResourceGate.HasAll 同源——体力按施法者层级换算）；
    /// 决策时不足的候选不生成（上交也会落空浪费配额，先攒体力）。
    /// 确定性：unitId 升序/skillIndex 升序/十字向枚举序遍历，严格大于替换（同分先到先得）。
    /// </summary>
    public class PlayerQuotaBrain : MonoBehaviour
    {
        private BattleSession _session;
        private string _playerId;
        private int _lastSubmittedTurn = -1;

        /// <summary>AI 玩家队伍（2026-09-25 三轮审查 C2：敌我/我军判定=TeamType 口径；操控权归属=playerId 保留）</summary>
        private TeamType MyTeam => _session != null && _session.Sim != null
            ? _session.Sim.GetTeamOf(_playerId)
            : TeamType.A;

        /// <summary>AI 提交延迟（秒）——模拟"思考"，同时让回合切换肉眼可辨</summary>
        private const float SubmitDelaySeconds = 0.8f;

        // ==================== 评分口径（号令档内部常量，勿散写调用处） ====================

        /// <summary>增益 Buff 每原子的行动溢价（叠层/长效增益优于原地空过的粗估）</summary>
        private const int EnsoBuffActionScore = 15;

        /// <summary>被协者已实装变奏（TriggerSkill 链）的评分</summary>
        private const int EnsoTriggerChainScore = 12;

        /// <summary>协奏元能转移（+10/+20）的评分</summary>
        private const int EnsoEnergyTransferScore = 4;

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

        // ==================== 配额评分制决策（号令/操魔神/空过） ====================

        /// <summary>
        /// 候选枚举序（确定性）：己方存活魔神 unitId 升序 × 技能 skillIndex 升序 × 十字向枚举序 ×
        /// 号令施法者/目标 unitId 升序；严格大于替换（同分先到先得）。Pass=0 分兜底。
        /// </summary>
        private ActionData Decide(int turn)
        {
            var sim = _session.Sim;
            // 决策快照（纯读，与选择阶段广播同源状态）——预判/目标评估的唯一状态源
            var snapshot = sim.TakeSnapshot(turn);
            var tracker = new CompanionBrain.CandidateTracker();

            // ① 操魔神：己方魔神（5★）单位行动——共享评分骨架（CompanionBrain.ScoreUnitCandidates，
            //    中性档案；实池判定无虚拟预留——配额行动本脑独占，伙伴决策在其后自适应）
            foreach (var entry in CollectArchons(sim))
                CompanionBrain.ScoreUnitCandidates(sim, snapshot, entry.Value, _playerId, MyTeam,
                    turn, tracker, null, null);

            // ② 号令：势力技能（延奏/契约）候选——施法者=己方存活单位（眷属/伙伴/魔神均可，
            //    眷属技能表本无势力技能条目=天然只有伙伴/魔神当施法者）；对伙伴施法者=本回合号令
            //    （CompanionBrain 跳过其自主决策），对魔神施法者=直接操控
            foreach (var entry in CollectFactionCasters(sim))
                EvaluateEnsoSkills(sim, entry.Value, turn, tracker);

            return tracker.Best ?? Pass(turn);
        }

        /// <summary>己方存活魔神（5★，玩家域单位行动——配额脑唯一可操单位档）。
        /// 建筑排除（协议核心批 2026-09-29）：协议核心是 5★ 勿被当魔神操——建筑不参与任何行动</summary>
        private List<KeyValuePair<string, Unit>> CollectArchons(BattleSimState sim)
        {
            var archons = new List<KeyValuePair<string, Unit>>();
            foreach (var kv in sim.Units)
            {
                if (BattleHeuristics.IsBuilding(kv.Value)) continue; // 协议核心 5★ ≠ 可操魔神
                if (!BattleHeuristics.IsArchon(kv.Value)) continue;
                if (BattleSimState.IsDead(kv.Value) || !BattleSimState.CanAct(kv.Value)) continue;
                var id = kv.Value.GetUnitComponent<UnitIdentity>();
                if (id == null || id.OwnerPlayerID != _playerId) continue;
                archons.Add(kv);
            }
            archons.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key)); // unitId 升序（枚举序铁律）
            return archons;
        }

        /// <summary>己方存活单位全集（号令施法者候选域——含眷属：其技能表无势力技能条目则天然空集）。
        /// 建筑排除（协议核心批 2026-09-29）：核心无技能条目天然空集，显式排除保单一判据口径</summary>
        private List<KeyValuePair<string, Unit>> CollectFactionCasters(BattleSimState sim)
        {
            var casters = new List<KeyValuePair<string, Unit>>();
            foreach (var kv in sim.Units)
            {
                if (BattleHeuristics.IsBuilding(kv.Value)) continue; // 建筑不参与任何行动
                if (BattleSimState.IsDead(kv.Value) || !BattleSimState.CanAct(kv.Value)) continue;
                var id = kv.Value.GetUnitComponent<UnitIdentity>();
                if (id == null || id.OwnerPlayerID != _playerId) continue;
                casters.Add(kv);
            }
            casters.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return casters;
        }

        /// <summary>
        /// 号令候选（势力技能=玩家对自主军团的指挥通道，docs/active/32 §4）：占玩家 1 配额、
        /// 势力技能自身 0 体力（docs/07-00 口径）；目标=蒙德或自身（非蒙德效果原子空产出）；
        /// 估值=治疗缺口+增益未满层+变奏链+协奏元能（OnCast 效果原子逐个粗估，与 EffectCompiler 产出同语义）
        /// </summary>
        private void EvaluateEnsoSkills(BattleSimState sim,
            Unit caster, int turn, CompanionBrain.CandidateTracker tracker)
        {
            var skills = caster.Skills;
            for (int i = 0; i < skills.Count; i++)
            {
                var skill = skills[i];
                var data = skill?.RawData;
                if (data == null || data.skillType != SkillType.Enso) continue; // 契约 AI 档随其实装批接线（docs/11）
                if (!skill.CanCast(caster)) continue;
                if (!ResourceGate.HasAll(sim, caster, _playerId, data.costs, out _)) continue; // 消耗门槛（C-2 costs 单源）

                // 候选目标：我军存活单位（含自身）unitId 升序——延奏目标域=阵营口径
                // （2026-09-25 三轮审查 C2：2v2 可协奏队友单位）；行动者归属=playerId 保留
                var allies = new List<(string unitId, Unit unit)>();
                foreach (var kv in sim.Units)
                {
                    if (BattleSimState.IsDead(kv.Value)) continue;
                    var id = kv.Value.GetUnitComponent<UnitIdentity>();
                    if (id == null || id.Team != MyTeam) continue;
                    allies.Add((kv.Key, kv.Value));
                }
                allies.Sort((a, b) => string.CompareOrdinal(a.unitId, b.unitId));

                foreach (var ally in allies)
                {
                    if (!BattleHeuristics.IsMondstadtOrSelfUnit(caster, ally.unit)) continue;
                    int value = EvaluateEnsoTarget(data, caster, ally.unit);
                    if (value <= 0) continue;
                    tracker.Offer(value, Skill(caster, i, Direction2D.Right, turn, ally.unitId));
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
                        // 治疗换算单出口（协议核心批 2026-09-29 收口：EffectCompiler.ResolveHealAmount
                        // 同源，含受疗者治疗效率——协议核心 50%=守家续航估值同步减半）；有效治疗=min(治疗量, 缺口)
                        int heal = EffectCompiler.ResolveHealAmount(data, atom.paramKey, atom.value, caster, ally);
                        int missing = Math.Max(0, allyStats.GetStatStruct(StatType.HP).Max - allyStats.HP);
                        int effective = Math.Min(heal, missing);
                        if (effective * 100 < heal * CompanionBrain.HealWorthRatioPercent) break; // 缺口不足半量：不占行动
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
    }
}
