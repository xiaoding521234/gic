using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
    /// AI 固定卡组批接线：手牌角色卡出战=TryBuildDeploy 部署优先）④**直接操控伙伴（AI-1 批，
    /// docs/active/42 §11：CompanionBrain 共享骨架对己方 3-4★ 伙伴产候选——严格对齐真人，
    /// 门槛镜像 isPlayerDirectControl=true 减免 5）**⑤**升命候选（AI-1 批：同名存活+命座未满+
    /// 摩拉够→DeployUnit 同名路径升命，评分入池竞争）**⑥无候选=Pass 空过放权自主军团。
    /// 与真人玩家对等：配额同样可用于号令/魔神/部署/直接操控伙伴。
    /// AI 决策由 Host 生成（docs/18 决策一）——直接读 BattleSimState/BattleSnapshot，不走传输通道；
    /// 选择阶段开始后延迟提交（给玩家看清回合切换）。
    /// AI-4 批（docs/active/42 §9）：人格原型乘子（军备桶=操魔神/激进度=操控边际/契约桶=号令/
    /// 升命桶=投资——PersonaPack 资产零代码换人格）＋难度档（学徒=失误注入轮转法+对手预测器关闭件）。
    /// 体力感知：配额行动 costs 门槛镜像（ResourceGate.HasAll 同源——体力按施法者层级换算，
    /// 直接操控伙伴传 isPlayerDirectControl=true 与真人同口径）；
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

        /// <summary>协奏元能转移（+10/+20）的评分——蒙德/自身目标=+10 档基分；非蒙德目标 +20 档=双倍
        /// （AI-1 批传输档：非蒙德目标元能翻倍但不触发变奏——docs/07-势力机制/蒙德.md）</summary>
        private const int EnsoEnergyTransferScore = 4;

        /// <summary>升命候选评分（AI-1 批，docs/active/42 §11；2026-10-12 观察局二轮标定）：
        /// 永驻命座被动的投资档——**高于延奏号令增益档（~30-45）与威胁边际档（0-25）**、低于
        /// 斩杀边际（50~80+）与救命治疗（RescueScore 30+有效治疗≈50-60）；首版 25 在边际修正后
        /// 仍被延奏常态压制（观察局 T4~T12 实锤升命 30 回合仅中 4），50=「该救命/该斩杀的回合让位、
        /// 平时投资优先」的拍板位——实际数值随三账本批（AI-5）按摩拉水位重标定</summary>
        private const int UpgradeConstellationScore = 50;

        // 救命分已上提共享单源（F-3，docs/active/34）：CompanionBrain.RescueScore=30——配额脑号令
        // 与伙伴脑预治疗两处同口径防漂移；本脑消费点直接引用（原 private EnsoRescueScore 退役）

        // ==================== AI 玩家配置（AI-4 批，docs/active/42 §9——Inspector/harness 可调） ====================

        /// <summary>难度档（Bind 时登记 AIPlayerConfig；对局中改档走 AIPlayerConfig.SetDifficulty
        /// 即时生效——§9.2 决策质量阶梯，学徒=失误注入+关闭预测器/反应预期/意图板）</summary>
        [Header("AI 玩家配置（AI-4）")]
        public AIDifficulty 难度 = AIDifficulty.Grandmaster;

        /// <summary>人格原型包（§9.1 八原型资产 Resources/Configs/AI/Persona_*；null=回落默认
        /// Persona_经济家，资产缺失=中性 1.0——房间 AI 人格选择器=后续 UI 批）</summary>
        public PersonaPack 人格原型;

        private PersonaPack _persona;

        /// <summary>人格解析（懒加载一次；缺资产=中性）</summary>
        private PersonaPack Persona
        {
            get
            {
                if (_persona == null)
                    _persona = 人格原型 != null ? 人格原型 : Resources.Load<PersonaPack>("Configs/AI/Persona_经济家");
                return _persona;
            }
        }

        private float 军备桶 => Persona != null ? Persona.军备桶权重 : 1f;
        private float 契约桶 => Persona != null ? Persona.契约桶权重 : 1f;
        private float 升命桶 => Persona != null ? Persona.升命桶权重 : 1f;
        private float 激进度乘子 => Persona != null ? Persona.激进度 : 1f;

        public void Bind(BattleSession session, string playerId)
        {
            _session = session;
            _playerId = playerId;
            session.Flow.OnPhaseChanged += OnPhaseChanged;
            AIPlayerConfig.SetDifficulty(playerId, 难度); // AI-4：难度登记（伙伴脑按归属查门）
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
        /// 配额评分制决策（部署/直接操控伙伴/号令/操魔神/升命/空过）。
        /// 候选枚举序（确定性）：己方存活单位 unitId 升序 × 技能 skillIndex 升序 × 十字向枚举序 ×
        /// 号令施法者/目标 unitId 升序 × 升命卡 unitName 升序；严格大于替换（同分先到先得）。
        /// Pass=0 分兜底。
        /// 2026-10-12 部署分支（用户拍板「AI 玩家暂时用 安柏/凯亚/芭芭拉/体力/摩拉 固定卡组并打出手牌」）：
        /// 部署优先于号令/操魔神——先铺场后指挥（三伙伴上场后本分支自然空手，归位常规评分流）。
        /// AI-1 批（docs/active/42 §11）：直接操控伙伴（对齐真人减免口径）+升命+延奏传输档入池。
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
            //    候选落点吃避险扣分（AI 玩家专属感知；真人玩家对局零影响）。
            //    AI-4：per-unit tracker＋人格军备桶乘子；学徒=对手预测器关闭件（威胁图不建）
            var difficulty = AIPlayerConfig.DifficultyOf(_playerId);
            var threat = AIPlayerConfig.QuotaThreatEnabled(difficulty)
                ? OpponentThreatModel.Build(sim, snapshot, MyTeam, turn)
                : null;
            foreach (var entry in CollectArchons(sim))
            {
                var unitTracker = new CompanionBrain.CandidateTracker();
                CompanionBrain.ScoreUnitCandidates(sim, snapshot, entry.Value, _playerId, MyTeam,
                    turn, unitTracker, null, null, threat, null, true);
                if (unitTracker.Best == null) continue;
                tracker.Offer(Mathf.RoundToInt(unitTracker.BestScore * 军备桶), unitTracker.Best);
            }

            // ①b 直接操控伙伴（AI-1 批，2026-10-12 拍板「AI 玩家操控严格对齐真人玩家」）：
            //    己方 3-4★ 伙伴候选与操魔神同池竞争——门槛镜像 isPlayerDirectControl=true（体力减免 5
            //    与真人同口径）；档案=单位自身行为档案（评分保真：伙伴擅长维度权重照常生效）。
            //    **机会成本口径（2026-10-12 观察局诊断后落地，docs/active/42 §7「伙伴自主本来就会
            //    做的事操控分低」）**：被操控伙伴不操控也会自主行动——操控的边际价值＝威胁感知候选分
            //    −自主期望分（基线 pass：同骨架 threat=null=伙伴脑现行感知口径）。原始分直接入池会以
            //    35~284 的伤害分常态饿死升命（25）与号令（~30-45）——观察局 T4~T12 实锤（升命 30 回合
            //    仅赢 4 回合）。边际≤0=放权更好不入池；被操控伙伴跳过自主决策（commanded 集）。
            //    基线成本口径=isPlayerDirectControl=false（自主付费 5；体力枯竭回合自主无线、操控有
            //    线→操控解锁行动=真实边际，docs/05 §5.1「体力吃紧时直接操控本身是账本收益」）
            foreach (var entry in CollectCompanions(sim))
            {
                var unit = entry.Value;
                var profile = unit.RawData?.行为档案;
                // 基线 pass：自主近似（无威胁图、成本不减免）
                var baselineTracker = new CompanionBrain.CandidateTracker();
                CompanionBrain.ScoreUnitCandidates(sim, snapshot, unit, _playerId, MyTeam,
                    turn, baselineTracker, null, profile, null, null, false);
                int baseline = baselineTracker.BestScore;
                // 候选 pass：威胁感知（与操魔神档同感知）
                var unitTracker = new CompanionBrain.CandidateTracker();
                CompanionBrain.ScoreUnitCandidates(sim, snapshot, unit, _playerId, MyTeam,
                    turn, unitTracker, null, profile, threat, null, true);
                if (unitTracker.Best == null) continue;
                // AI-4：人格激进度乘子（狂战士更爱操控攻击/守城者更放权）
                int marginal = Mathf.RoundToInt((unitTracker.BestScore - baseline) * 激进度乘子);
                if (marginal <= 0) continue; // 威胁感知不优于自主=放权更好
                tracker.Offer(marginal, unitTracker.Best);
            }

            // ② 号令：势力技能（延奏/契约）候选——施法者=己方存活单位（眷属/伙伴/魔神均可，
            //    眷属技能表本无势力技能条目=天然只有伙伴/魔神当施法者）；对伙伴施法者=本回合号令
            //    （CompanionBrain 跳过其自主决策），对魔神施法者=直接操控。
            //    E-1：威胁图传号令估值（治疗救命档消费 DoomedAllies，docs/active/33 §2.3）
            foreach (var entry in CollectFactionCasters(sim))
                EvaluateEnsoSkills(sim, entry.Value, turn, tracker, threat);

            // ③ 升命候选（AI-1 批）：手牌角色卡同名存活在场+命座未满+摩拉够 → DeployUnit 同名路径
            //    升命（执行器既有链复用；deployCell 升命分支不消费）。评分入池与号令/操控竞争
            EvaluateUpgrades(sim, turn, tracker);

            // 学徒失误注入轮转法（§9.2——确定性次优而非随机 roll：评分榜取第 (turn mod N)+1 名，
            // 有「人味错误」但同种子可复现，零 roll 铁律不破）
            ActionData chosen = tracker.Best;
            int chosenScore = tracker.BestScore;
            if (AIPlayerConfig.MistakeInjectionEnabled(difficulty) && tracker.All.Count > 0)
            {
                // OrderByDescending 稳定排序=平局保持枚举序（枚举序铁律）；榜取第 (turn mod N)+1 名 ∈ [1..N]
                var ranked = tracker.All.OrderByDescending(c => c.score).ToList();
                int rank = (turn % ranked.Count) + 1;
                chosen = ranked[rank - 1].action;
                chosenScore = ranked[rank - 1].score;
                GICLog.Info($"[配额脑] {_playerId} T{turn} 学徒失误注入：榜 {ranked.Count} 候选取第 {rank} 名" +
                    $"（最优 {tracker.BestScore}→选中 {chosenScore}）");
            }

            // 决策日志（docs/active/42 §10 可调试性：落选可审计——升命/号令常年不入选时
            // 从「配额=分」行归因分数构成，勿盲调常量）
            if (chosen != null)
            {
                GICLog.Info($"[配额脑] {_playerId} T{turn} 配额={chosen.actionType}" +
                    (chosen.actionType == ActionType.Skill
                        ? $" 技能{chosen.skillIndex} 单位={chosen.unitId} 目标={chosen.targetUnitId}"
                        : chosen.actionType == ActionType.DeployUnit
                            ? $" 出战/升命={(UnitName)chosen.deployUnitName}@{chosen.deployCell}"
                            : "") +
                    $" 分={chosenScore}");
            }
            else
            {
                GICLog.Info($"[配额脑] {_playerId} T{turn} 配额=空过放权");
            }

            return chosen ?? Pass(turn);
        }

        // ==================== 部署分支（2026-10-12 AI 固定卡组批） ====================

        /// <summary>
        /// 部署候选（确定性）：手牌角色卡按 unitName 升序 × 落点由近及远（核心半径 1 环→2 环、
        /// 行优先）→ 首个「合法落点+摩拉够+无同名在场（含尸体）」组合出战（AI-1 批起升命另走评分池）。
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

        /// <summary>己方同名单位在场判定（**含尸体**——单源=DeployUnitExecutor.FindOwnUnitByName，
        /// docs/05 §5.4 尸体也仅升命不复苏：同名在场（含尸体）时再打出该卡=给尸体升命烧配额，
        /// 部署分支守卫跳过〔升命候选另走 EvaluateUpgrades 存活口径〕；卡不消耗）</summary>
        private bool OwnUnitOnField(BattleSimState sim, UnitName unitName)
            => DeployUnitExecutor.FindOwnUnitByName(sim, _playerId, unitName) != null;

        // ==================== 升命候选（AI-1 批，2026-10-12 docs/active/42 §11） ====================

        /// <summary>
        /// 升命候选（确定性）：手牌 3★+ 角色卡 unitName 升序（同卡多张去重）→ 同名**存活**单位在场
        /// 且 ConstellationLevel&lt;MaxConstellation 且摩拉够 → DeployUnit 同名路径升命候选
        /// （评分=UpgradeConstellationScore 入池与号令/操控竞争，非部署式无条件优先）。
        /// 校验口径全镜像 DeployUnitExecutor.TryResolve 升命分支（starLevel≥3/满命/GetEffectiveDeployCost）；
        /// 尸体不升（复苏前置投资=后续批次；现行「给尸体升命」判定为烧配额维持跳过）；
        /// deployCell 升命分支不消费（执行器升命先于落点校验），填核心位保语义。
        /// </summary>
        private void EvaluateUpgrades(BattleSimState sim, int turn, CompanionBrain.CandidateTracker tracker)
        {
            var hand = sim.GetHand(_playerId);
            if (hand == null || hand.Count == 0) return;
            var config = Wargame.Instance?.Context?.Get<UnitConfig>();
            if (config == null) return;

            var candidates = new List<(int unitName, UnitConfig.UnitData data)>();
            foreach (var card in hand)
            {
                if (!card.IsUnit) continue;
                if (candidates.Exists(c => c.unitName == card.value)) continue; // 同卡去重=一次只评一级
                var data = config.GetUnitData((UnitName)card.value);
                if (data == null || data.starLevel < 3) continue; // 升命语义=3★+（执行器同口径）
                candidates.Add((card.value, data));
            }
            if (candidates.Count == 0) return;
            candidates.Sort((a, b) => a.unitName.CompareTo(b.unitName)); // unitName 升序（枚举序铁律）

            foreach (var candidate in candidates)
            {
                var existing = DeployUnitExecutor.FindOwnUnitByName(sim, _playerId, (UnitName)candidate.unitName);
                if (existing == null) continue; // 未出战：归 TryBuildDeploy 部署分支（⓪ 优先），此处不辖
                if (BattleSimState.IsDead(existing)) continue; // 尸体升命=烧配额（复苏前置投资后置批次）
                if (existing.ConstellationLevel >= ConstellationApplier.MaxConstellation) continue; // 已满命
                if (sim.GetMora(_playerId) < candidate.data.GetEffectiveDeployCost()) continue; // 摩拉不足先攒
                // AI-4：人格升命桶乘子（经济家/守城者更爱投资）
                int upgradeScore = Mathf.RoundToInt(UpgradeConstellationScore * 升命桶);
                GICLog.Info($"[配额脑] {_playerId} T{turn} 升命候选 {(UnitName)candidate.unitName}" +
                    $" C{existing.ConstellationLevel}→C{existing.ConstellationLevel + 1}" +
                    $" 分={upgradeScore}（当前最优 {tracker.BestScore}）");
                tracker.Offer(upgradeScore, new ActionData
                {
                    playerId = _playerId,
                    actionType = ActionType.DeployUnit,
                    deployUnitName = candidate.unitName,
                    deployCell = sim.GetCorePosition(_playerId),
                    turnNumber = turn,
                });
            }
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

        /// <summary>己方存活魔神（5★，配额脑可操档之一）。建筑排除（协议核心批 2026-09-29）：
        /// 协议核心是 5★ 勿被当魔神操——建筑不参与任何行动</summary>
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

        /// <summary>己方存活伙伴（3-4★，AI-1 批直接操控候选域——玩家配额可直接操控，与真人对等）。
        /// 建筑排除同 CollectArchons；被操控伙伴由 CompanionBrain commanded 集（玩家已提交单位）跳过
        /// 自主决策，无双行动</summary>
        private List<KeyValuePair<string, Unit>> CollectCompanions(BattleSimState sim)
        {
            var companions = new List<KeyValuePair<string, Unit>>();
            foreach (var kv in sim.Units)
            {
                if (BattleHeuristics.IsBuilding(kv.Value)) continue; // 建筑不参与任何行动
                if (!BattleHeuristics.IsCompanion(kv.Value)) continue;
                if (BattleSimState.IsDead(kv.Value) || !BattleSimState.CanAct(kv.Value)) continue;
                var id = kv.Value.GetUnitComponent<UnitIdentity>();
                if (id == null || id.OwnerPlayerID != _playerId) continue;
                companions.Add(kv);
            }
            companions.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key)); // unitId 升序（枚举序铁律）
            return companions;
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
        /// 势力技能自身 0 体力（docs/07-00 口径）；目标域=我军存活单位（含自身）unitId 升序——
        /// 延奏目标域=阵营口径（2026-09-25 三轮审查 C2：2v2 可协奏队友单位）；行动者归属=playerId 保留；
        /// 估值=治疗缺口+增益未满层+变奏链+协奏元能（OnCast 效果原子逐个粗估，与 EffectCompiler 产出同语义）。
        /// AI-1 批传输档：非蒙德目标不再滤除（docs/07-势力机制/蒙德.md——+20 元能不触发变奏，
        /// 延奏自身 buff/治疗对任何目标照常生效；当前固定卡组全蒙德=传输档待混搭卡组激活）
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

                // 候选目标：我军存活**角色**（含自身）unitId 升序——延奏目标域=阵营口径
                // （2026-09-25 三轮审查 C2：2v2 可协奏队友单位）；行动者归属=playerId 保留。
                // 建筑排除（AI-4 观察局实锤返修）：docs/07 蒙德「我方**角色**」——协议核心是建筑
                // 非角色，buff/变奏/元能对核心零收益（治疗核心归伙伴治疗技能域），混入候选
                // 只会污染评分榜（交涉家契约桶 ×1.6 放大后显形——学徒失误注入选中过给核心的延奏）
                var allies = new List<(string unitId, Unit unit)>();
                foreach (var kv in sim.Units)
                {
                    if (BattleSimState.IsDead(kv.Value)) continue;
                    if (BattleHeuristics.IsBuilding(kv.Value)) continue;
                    var id = kv.Value.GetUnitComponent<UnitIdentity>();
                    if (id == null || id.Team != MyTeam) continue;
                    allies.Add((kv.Key, kv.Value));
                }
                allies.Sort((a, b) => string.CompareOrdinal(a.unitId, b.unitId));

                foreach (var ally in allies)
                {
                    bool mondOrSelf = BattleHeuristics.IsMondstadtOrSelfUnit(caster, ally.unit);
                    int value = EvaluateEnsoTarget(data, caster, ally.unit, ally.unitId, threat, mondOrSelf);
                    if (value <= 0) continue;
                    // AI-4：人格契约桶乘子（交涉家/指挥官更爱号令）
                    tracker.Offer(Mathf.RoundToInt(value * 契约桶), Skill(caster, i, Direction2D.Right, turn, ally.unitId));
                }
            }
        }

        /// <summary>延奏对某目标的估值：按 OnCast 效果原子逐个粗估（与 EffectCompiler 产出同语义）。
        /// mondOrSelf=蒙德角色或施法者自身（AI-1 批传输档参数）：true→变奏链+元能 +10 档；
        /// false→无变奏、元能 +20 档双倍评分（延奏自身 buff/治疗对任何目标照常生效）。
        /// E-1：threat=威胁图（治疗救命档消费；null=无威胁上下文零加成）</summary>
        private int EvaluateEnsoTarget(SkillConfig.SkillData data, Unit caster, Unit ally,
            string allyUnitId, OpponentThreatModel.ThreatMap threat, bool mondOrSelf)
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
                        // ——仅蒙德角色/施法者自身触发变奏（非蒙德=纯传输档无变奏链，AI-1 批）
                        if (mondOrSelf && HasImplementedSkillOfType(ally, atom.targetSkillType))
                            value += EnsoTriggerChainScore;
                        break;
                    }

                    case SkillEffectKind.EnergyGain:
                        // 协奏元能：蒙德/自身 +10 档=基分；非蒙德 +20 档=双倍（docs/07-势力机制/蒙德.md；
                        // 延奏技能资产 EnergyGain 原子按目标档产出，估值同构镜像）
                        value += mondOrSelf ? EnsoEnergyTransferScore : EnsoEnergyTransferScore * 2;
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
