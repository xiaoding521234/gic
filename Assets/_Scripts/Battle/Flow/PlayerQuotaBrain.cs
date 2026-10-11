using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// AI 玩家配额脑（B6b 起；D 批次操控分层 docs/active/32 §6 收窄+2026-09-29 术语迁移改名
    /// AIDebugBrain→PlayerQuotaBrain：原 v2 评分制的**单位级评估搬迁为伙伴自主脑（CompanionBrain）**，
    /// 本脑只管玩家 1 配额的用途——①号令（上交势力技能延奏/契约，施法者=己方眷属/伙伴/魔神均可）
    /// ②操魔神（5 星单位行动=CompanionBrain 共享评分骨架复用为候选打分器）③部署（2026-10-12
    /// AI 固定卡组批接线：手牌角色卡出战=TryBuildDeploy 部署优先；升命/购买/物品仍后续批次）
    /// ④无候选=Pass 空过放权自主军团。与真人玩家对等：配额同样可用于号令/魔神/部署
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

        // 救命分已上提共享单源（F-3，docs/active/34）：CompanionBrain.RescueScore=30——配额脑号令
        // 与伙伴脑预治疗两处同口径防漂移；本脑消费点直接引用（原 private EnsoRescueScore 退役）

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
        /// 配额评分制决策（部署/号令/操魔神/空过）。
        /// 候选枚举序（确定性）：己方存活魔神 unitId 升序 × 技能 skillIndex 升序 × 十字向枚举序 ×
        /// 号令施法者/目标 unitId 升序；严格大于替换（同分先到先得）。Pass=0 分兜底。
        /// 2026-10-12 部署分支（用户拍板「AI 玩家暂时用 安柏/凯亚/芭芭拉/体力/摩拉 固定卡组并打出手牌」）：
        /// 部署优先于号令/操魔神——先铺场后指挥（三伙伴上场后归位常规评分流）。
        /// </summary>
        private ActionData Decide(int turn)
        {
            var sim = _session.Sim;

            // ⓪ 部署：手牌角色卡+核心半径内有合法落点+摩拉够 → 出战（DeployUnitExecutor 校验链同源；
            //    同名单位在场〔含尸体〕=该卡暂不出战——规避升命语义烧配额〔docs/05 §5.4 尸体仅升命〕；
            //    三伙伴全上场后本分支自然空手，归位号令/操魔神评分流）
            var deploy = TryBuildDeploy(sim, turn);
            if (deploy != null) return deploy;

            // 决策快照（纯读，与选择阶段广播同源状态）——预判/目标评估的唯一状态源
            var snapshot = sim.TakeSnapshot(turn);
            var tracker = new CompanionBrain.CandidateTracker();

            // ① 操魔神：己方魔神（5★）单位行动——共享评分骨架（CompanionBrain.ScoreUnitCandidates，
            //    中性档案；实池判定无虚拟预留——配额行动本脑独占，伙伴决策在其后自适应）。
            //    E-1 对手建模（docs/active/33 §2）：威胁图传骨架——魔神攻击候选吃反威胁分、移动
            //    候选落点吃避险扣分（AI 玩家专属感知；真人玩家对局零影响）
            var threat = OpponentThreatModel.Build(sim, snapshot, MyTeam, turn);
            foreach (var entry in CollectArchons(sim))
                CompanionBrain.ScoreUnitCandidates(sim, snapshot, entry.Value, _playerId, MyTeam,
                    turn, tracker, null, null, threat);

            // ② 号令：势力技能（延奏/契约）候选——施法者=己方存活单位（眷属/伙伴/魔神均可，
            //    眷属技能表本无势力技能条目=天然只有伙伴/魔神当施法者）；对伙伴施法者=本回合号令
            //    （CompanionBrain 跳过其自主决策），对魔神施法者=直接操控。
            //    E-1：威胁图传号令估值（治疗救命档消费 DoomedAllies，docs/active/33 §2.3）
            foreach (var entry in CollectFactionCasters(sim))
                EvaluateEnsoSkills(sim, entry.Value, turn, tracker, threat);

            return tracker.Best ?? Pass(turn);
        }

        // ==================== 部署分支（2026-10-12 AI 固定卡组批） ====================

        /// <summary>
        /// 部署候选（确定性）：手牌角色卡按 unitName 升序 × 落点由近及远（核心半径 1 环→2 环、
        /// 行优先）→ 首个「合法落点+摩拉够+无同名存活」组合出战。
        /// 落点合法性=DeployUnitExecutor.IsDeployCellValid 单源（核心半径 2+地形+碰撞共享原语）；
        /// 摩拉=GetEffectiveDeployCost 同源；同名存活守卫=规避升命语义重复烧配额（卡不消耗，
        /// 阵亡后同名卡自然重新可出战）。
        /// </summary>
        private ActionData TryBuildDeploy(BattleSimState sim, int turn)
        {
            var hand = sim.GetHand(_playerId);
            if (hand == null || hand.Count == 0) return null;
            var config = Wargame.Instance?.Context?.Get<UnitConfig>();
            if (config == null) return null;

            // 手牌角色卡收集（升序遍历；同卡多张去重=一次只出一张；物品/货币卡不可部署）
            var candidates = new List<(int unitName, UnitConfig.UnitData data)>();
            foreach (var card in hand)
            {
                if (!card.IsUnit) continue;
                if (candidates.Exists(c => c.unitName == card.value)) continue;
                var data = config.GetUnitData((UnitName)card.value);
                if (data == null) continue;
                if (sim.GetMora(_playerId) < data.GetEffectiveDeployCost()) continue; // 摩拉不足先攒
                candidates.Add((card.value, data));
            }
            if (candidates.Count == 0) return null;
            candidates.Sort((a, b) => a.unitName.CompareTo(b.unitName));

            foreach (var candidate in candidates)
            {
                if (OwnUnitOnField(sim, (UnitName)candidate.unitName)) continue; // 同名在场（含尸体）=暂不出战
                var cell = FindDeployCell(sim, candidate.data);
                if (cell == null) continue;
                return new ActionData
                {
                    playerId = _playerId,
                    actionType = ActionType.DeployUnit,
                    deployUnitName = candidate.unitName,
                    deployCell = cell.Value,
                    turnNumber = turn,
                };
            }
            return null;
        }

        /// <summary>己方同名单位在场判定（**含尸体**——口径=DeployUnitExecutor.FindOwnUnitByName/升命
        /// 分支同构：docs/05 §5.4 尸体也仅升命不复苏。同名在场（含尸体）时再打出该卡=给尸体升命
        /// 烧配额，AI 守卫跳过；卡不消耗，若无同名单位（未部署过）则可出战）</summary>
        private bool OwnUnitOnField(BattleSimState sim, UnitName unitName)
        {
            foreach (var kv in sim.Units)
            {
                var identity = kv.Value.GetUnitComponent<UnitIdentity>();
                if (identity == null || identity.OwnerPlayerID != _playerId) continue;
                if (identity.UnitName == unitName) return true;
            }
            return false;
        }

        /// <summary>核心附近首个合法部署落点（由近及远：半径 1 环→2 环、行优先列序；确定性枚举）</summary>
        private BattleCell? FindDeployCell(BattleSimState sim, UnitConfig.UnitData deployData)
        {
            var core = sim.GetCorePosition(_playerId);
            for (int radius = 1; radius <= DeployUnitExecutor.DeployRadiusFromCore; radius++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius) continue; // 只遍历当前环
                        var cell = new BattleCell(core.x + dx, core.y + dy);
                        if (DeployUnitExecutor.IsDeployCellValid(sim, _playerId, cell, deployData))
                            return cell;
                    }
                }
            }
            return null;
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
            Unit caster, int turn, CompanionBrain.CandidateTracker tracker,
            OpponentThreatModel.ThreatMap threat)
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
                    int value = EvaluateEnsoTarget(data, caster, ally.unit, ally.unitId, threat);
                    if (value <= 0) continue;
                    tracker.Offer(value, Skill(caster, i, Direction2D.Right, turn, ally.unitId));
                }
            }
        }

        /// <summary>延奏对某目标的估值：按 OnCast 效果原子逐个粗估（与 EffectCompiler 产出同语义）。
        /// E-1：threat=威胁图（治疗救命档消费；null=无威胁上下文零加成）</summary>
        private int EvaluateEnsoTarget(SkillConfig.SkillData data, Unit caster, Unit ally,
            string allyUnitId, OpponentThreatModel.ThreatMap threat)
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
                        // E-1 救命分（docs/active/33 §2.3→F-3 上提共享单源 CompanionBrain.RescueScore）：
                        // 伤员在威胁图 DoomedAllies（敌方预测合计承伤 ≥ 当前 hp=将被集火致死）
                        // → 预治疗保命档（斩杀档对偶）
                        if (threat != null && threat.DoomedAllies.Contains(allyUnitId))
                            value += CompanionBrain.RescueScore;
                        break;
                    }

                    case SkillEffectKind.ApplyBuff:
                    {
                        // 增益：目标该具名 Buff 已满层=零价值，否则=BuffValue+行动溢价
                        // （决策五十七：buffAsset 引用优先按身份判满层；null 回落 type——与编译同口径）
                        int stackLimit = atom.paramKey2 != SkillParamKey.None ? data.GetInt(atom.paramKey2, 0) : 0;
                        var existing = ally.Buffs.Find(b => b != null && (atom.buffAsset != null
                            ? b.Config == atom.buffAsset : b.Type == atom.buffType));
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
