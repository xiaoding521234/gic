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
    /// AI 玩家配额脑（B6b 起；D 批次操控分层 docs/32 §6 收窄+2026-09-29 术语迁移改名
    /// AIDebugBrain→PlayerQuotaBrain：原 v2 评分制的**单位级评估搬迁为伙伴自主脑（CompanionBrain）**，
    /// 本脑只管玩家 1 配额的用途——①号令（上交势力技能延奏/契约，施法者=己方眷属/伙伴/魔神均可）
    /// ②操魔神（5 星单位行动=CompanionBrain 共享评分骨架复用为候选打分器）③部署（2026-10-12
    /// AI 固定卡组批接线：手牌角色卡出战=TryBuildDeploy 部署优先）④**直接操控伙伴（AI-1 批，
    /// docs/active/42 §11：CompanionBrain 共享骨架对己方 3-4★ 伙伴产候选——严格对齐真人，
    /// 门槛镜像 isPlayerDirectControl=true 减免 5）**⑤**升命候选（AI-1 批：同名存活+命座未满+
    /// 摩拉够→DeployUnit 同名路径升命，评分入池竞争）**⑥无候选=Pass 空过放权自主军团。
    /// 与真人玩家对等：配额同样可用于号令/魔神/部署/直接操控伙伴。
    /// AI 决策由 Host 生成（docs/18 决策一）——直接读 BattleSimState/BattleSnapshot，不走传输通道；
    /// 每决策节拍延迟提交（原「给玩家看清回合切换」语义已随回合机退役）。
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
        private int _lastSubmittedSecond = -1;
        /// <summary>上次尝试购买的逻辑秒（P6 残留审计 C6，docs/active/49）：恢复「每回合（=1 秒）
        /// 至多一次购买」的 1:1 语义——决策节拍本身是 0.5 秒。</summary>
        private int _lastPurchaseSecond = -1;

        /// <summary>AI 玩家队伍（2026-09-25 三轮审查 C2：敌我/我军判定=TeamType 口径；操控权归属=playerId 保留）</summary>
        private TeamType MyTeam => _session != null && _session.Sim != null
            ? _session.Sim.GetTeamOf(_playerId)
            : TeamType.A;

        /// <summary>AI 提交延迟（秒）——P5 三脑同频（蓝图 §6「决策频率=每 0.5 秒」）：延迟=节拍长；
        /// 旧 0.8s 回合制「让回合切换肉眼可辨」语义随回合机退役（即时制无回合切换可辨）</summary>
        private const float SubmitDelaySeconds = 0.4f; // 略短于 0.5s 节拍：保证串行守卫在下一拍前释放（P6 残留审计 A5）

        // ==================== 评分口径（#4 审查批资产化：数值真源=Resources/Configs/AI/AITuning.asset——
        // 调 AI 平衡=改资产 Inspector 零代码；下列属性=代码侧访问别名，勿散写调用处） ====================

        /// <summary>增益 Buff 每原子的行动溢价（叠层/长效增益优于原地空过的粗估）</summary>
        private static int EnsoBuffActionScore => AITuningConfig.Instance.延奏增益行动分;

        /// <summary>被协者已实装变奏（TriggerSkill 链）的评分</summary>
        private static int EnsoTriggerChainScore => AITuningConfig.Instance.延奏变奏链分;

        /// <summary>协奏元能转移（+10/+20）的评分——蒙德/自身目标=+10 档基分；非蒙德目标 +20 档=双倍
        /// （AI-1 批传输档：非蒙德目标元能翻倍但不触发变奏——docs/07-势力机制/蒙德.md）</summary>
        private static int EnsoEnergyTransferScore => AITuningConfig.Instance.延奏传输分;

        /// <summary>升命候选评分（AI-1 批，docs/active/42 §11；2026-10-12 观察局二轮标定）：
        /// 永驻命座被动的投资档——**高于延奏号令增益档（~30-45）与威胁边际档（0-25）**、低于
        /// 斩杀边际（50~80+）与救命治疗（RescueScore 30+有效治疗≈50-60）；首版 25 在边际修正后
        /// 仍被延奏常态压制（观察局 T4~T12 实锤升命 30 回合仅中 4），50=「该救命/该斩杀的回合让位、
        /// 平时投资优先」的拍板位——实际数值随三账本批（AI-5）按摩拉水位重标定</summary>
        private static int UpgradeConstellationScore => AITuningConfig.Instance.升命投资分;

        // 救命分已上提共享单源（F-3，docs/archive/34）：CompanionBrain.RescueScore=30——配额脑号令
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

        /// <summary>配额脑思考协程句柄（P5 串行守卫——0.5s 节拍×0.5s 思考延迟下防协程重叠双交；
        /// 串行=每拍至多一交，上一拍未完成则本拍跳过）</summary>
        private Coroutine _submitRoutine;

        private void OnPhaseChanged(BattlePhase phase, int turn)
        {
            if (phase != BattlePhase.Selecting) return;
            if (_submitRoutine != null) return; // 上一拍仍在思考：本拍跳过（P5 串行守卫）
            _submitRoutine = StartCoroutine(SubmitActionRoutine(turn));
        }

        private IEnumerator SubmitActionRoutine(int startedTurn)
        {
            yield return new WaitForSeconds(SubmitDelaySeconds);
            _submitRoutine = null;

            // 战斗结束/状态机停止则放弃。P5 同频改造：CurrentSecond=逻辑秒（每秒翻号），旧
            // 「Phase!=Selecting || CurrentSecond!=turn」双校验在 0.8s 延迟跨秒界时会 20% 丢拍——
            // 改 Phase 单校验+当前秒取值（思考延迟可能跨秒界，决策用当下拍）
            if (_session == null || _session.Flow == null) yield break;
            if (_session.Flow.Phase != BattlePhase.Selecting) yield break;
            int now = _session.Flow.CurrentSecond;
            if (now == _lastSubmittedSecond) yield break; // 同拍防重复提交（幂等）

            var action = Decide(now);
            // P5-D：空 unitId 的 Pass（配额=空过放权）不上交——tick 制无配额语义，上交只产
            // RTSFlow「未知单位」警告刷屏（部署/物品/装备等玩家级行动本就无 unitId，照常上交）
            if (action != null
                && !(action.actionType == ActionType.Pass && string.IsNullOrEmpty(action.unitId)))
                _session.SubmitAction(action);
            _lastSubmittedSecond = now;
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

            // 决策快照（纯读，与选择阶段广播同源状态）——预判/目标评估的唯一状态源；
            // 战略层与评分流共用（战略层逐拍更新=Decide 头部，docs/active/42 §5 报告链〔原「每回合」措辞〕：
            // 部署早退拍也更新——新战斗自清依赖 T1 重入观测，跳过部署拍会让跨局陈旧态漏清
            // 〔观察局 Match A 实证 T1-3 无 [战略层] 行=UpdateForSecond 未跑〕；T1-3 态势可见性同得）
            var snapshot = sim.TakeSnapshot(turn);

            // 战略层逐拍更新（AI-5 批，docs/active/42 §5）：报告链→态势机（滞回+驻留）→军团倾向→
            // 黑板乘子→红线；配额脑消费=乘子合成（人格基线×态势乘子，乘法于消费点）+体力保底门
            var strat = AIStrategist.UpdateForSecond(sim, snapshot, _playerId, MyTeam, turn);
            GICLog.Info($"[战略层] {_playerId} T{turn} 态势={strat.Stance} 军团={strat.Legion}" +
                $" 比值={strat.PowerRatio:F2} 乘子[军{strat.armyMul:F2}/契{strat.contractMul:F2}/升{strat.upgradeMul:F2}]" +
                $" 意图=[{string.Join("，", strat.Intents)}]");

            // ⓪-1 购买（2026-10-23 强化批拍板「让 AI 也会购买部署建筑」）：BuyCard 即时通道
            //    **不占配额**（每秒至多一次；即时同步结算——LocalBattleTransport.ClientSend 直投，
            //    下位部署分支当拍即可消费新购卡）；旗帜走购买补货、角色补货档待琴实装自然激活
            TryPurchase(sim, turn);

            // ⓪ 部署：手牌角色卡+核心半径内有合法落点+摩拉够 → 出战（DeployUnitExecutor 校验链同源；
            //    同名单位在场〔含尸体〕=该卡暂不出战——规避升命语义烧配额〔docs/05 §5.4 尸体仅升命〕；
            //    三伙伴全上场后本分支自然空手，归位号令/操魔神评分流）
            var deploy = TryBuildDeploy(sim, turn);
            if (deploy != null) return deploy;

            var tracker = new CompanionBrain.CandidateTracker();

            // ① 操魔神：己方魔神（5★）单位行动——共享评分骨架（CompanionBrain.ScoreUnitCandidates，
            //    中性档案；实池判定无虚拟预留——配额行动本脑独占，伙伴决策在其后自适应）。
            //    E-1 对手建模（docs/archive/33 §2）：威胁图传骨架——魔神攻击候选吃反威胁分、移动
            //    候选落点吃避险扣分（AI 玩家专属感知；真人玩家对局零影响）。
            //    AI-4：per-unit tracker＋人格军备桶乘子；学徒=对手预测器关闭件（威胁图不建）。
            //    AI-5：人格×态势军备乘子合成+体力保底门（§5.3——保自主军团 2 秒拍运转量）
            var difficulty = AIPlayerConfig.DifficultyOf(_playerId);
            var threat = AIPlayerConfig.QuotaThreatEnabled(difficulty)
                ? OpponentThreatModel.BuildCached(sim, snapshot, MyTeam, turn) // #2 审查批：秒级缓存
                : null;
            float armMul = 军备桶 * strat.armyMul;
            foreach (var entry in CollectArchons(sim))
            {
                var unitTracker = new CompanionBrain.CandidateTracker();
                CompanionBrain.ScoreUnitCandidates(sim, snapshot, entry.Value, _playerId, MyTeam,
                    turn, unitTracker, null, null, threat, null, true, true,
                    AIStrategist.LegionAggressionOf(_playerId));
                if (unitTracker.Best == null) continue;
                int cost = UnitTierHelper.StaminaCostOf(BattleHeuristics.TierOf(entry.Value));
                if (cost > 0 && strat.Stamina - cost < strat.StaminaFloor) continue; // 体力保底门（§5.3）
                tracker.Offer(Mathf.RoundToInt(unitTracker.BestScore * armMul), unitTracker.Best);
            }

            // ①b 直接操控伙伴（AI-1 批，2026-10-12 拍板「AI 玩家操控严格对齐真人玩家」）：
            //    己方 3-4★ 伙伴候选与操魔神同池竞争——门槛镜像 isPlayerDirectControl=true（体力减免 5
            //    与真人同口径）；档案=单位自身行为档案（评分保真：伙伴擅长维度权重照常生效）。
            //    **机会成本口径（2026-10-12 观察局诊断后落地，docs/active/42 §7「伙伴自主本来就会
            //    做的事操控分低」）**：被操控伙伴不操控也会自主行动——操控的边际价值＝威胁感知候选分
            //    −自主期望分（基线 pass：同骨架 threat=null=伙伴脑现行感知口径）。原始分直接入池会以
            //    35~284 的伤害分常态饿死升命（25）与号令（~30-45）——观察局 T4~T12 实锤（升命 30 回合
            //    仅赢 4 回合）。边际≤0=放权更好不入池；被操控伙伴跳过自主决策（commanded 集）。
            //    基线成本口径=isPlayerDirectControl=false（自主付费 5；体力枯竭时自主无线、操控有
            //    线→操控解锁行动=真实边际，docs/05 §5.1「体力吃紧时直接操控本身是账本收益」）
            foreach (var entry in CollectCompanions(sim))
            {
                var unit = entry.Value;
                var profile = unit.RawData?.行为档案;
                float legion = AIStrategist.LegionAggressionOf(_playerId); // AI-5：军团软系数（回防=斩杀下调）
                // 基线 pass：自主近似（无威胁图、成本不减免；同带军团系数=边际尺度一致）
                var baselineTracker = new CompanionBrain.CandidateTracker();
                CompanionBrain.ScoreUnitCandidates(sim, snapshot, unit, _playerId, MyTeam,
                    turn, baselineTracker, null, profile, null, null, false, true, legion);
                int baseline = baselineTracker.BestScore;
                // 候选 pass：威胁感知（与操魔神档同感知）
                var unitTracker = new CompanionBrain.CandidateTracker();
                CompanionBrain.ScoreUnitCandidates(sim, snapshot, unit, _playerId, MyTeam,
                    turn, unitTracker, null, profile, threat, null, true, true, legion);
                if (unitTracker.Best == null) continue;
                // AI-4：人格激进度乘子（狂战士更爱操控攻击/守城者更放权）；AI-5：态势军备乘子
                int marginal = Mathf.RoundToInt((unitTracker.BestScore - baseline) * 激进度乘子 * strat.armyMul);
                if (marginal <= 0) continue; // 威胁感知不优于自主=放权更好
                tracker.Offer(marginal, unitTracker.Best);
            }

            // ② 号令：势力技能（延奏/契约）候选——施法者=己方存活单位（眷属/伙伴/魔神均可，
            //    眷属技能表本无势力技能条目=天然只有伙伴/魔神当施法者）；对伙伴施法者=本拍号令
            //    （CompanionBrain 跳过其自主决策），对魔神施法者=直接操控。
            //    E-1：威胁图传号令估值（治疗救命档消费 DoomedAllies，docs/archive/33 §2.3）
            foreach (var entry in CollectFactionCasters(sim))
                EvaluateEnsoSkills(sim, entry.Value, turn, tracker, threat, 契约桶 * strat.contractMul);

            // ③ 升命候选（AI-1 批）：手牌角色卡同名存活在场+命座未满+摩拉够 → DeployUnit 同名路径
            //    升命（执行器既有链复用；deployCell 升命分支不消费）。评分入池与号令/操控竞争
            EvaluateUpgrades(sim, turn, tracker, 升命桶 * strat.upgradeMul);

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
            // 劲敌次优注入（凌晨批议题⑥）：榜首与次名分差占比 < AITuning.劲敌次优分差比 → 取次名
            //（GameAIPro ch04 hustling「次优选择仅在分差<20% 内启用」——确定性零 roll；分差大仍取
            // 最优=只在接近时犯错的拟人形态；与学徒轮转法互斥〔难度档不同〕）
            else if (AIPlayerConfig.SecondBestInjectionEnabled(difficulty) && tracker.All.Count > 1)
            {
                var ranked = tracker.All.OrderByDescending(c => c.score).ToList();
                int top = ranked[0].score, second = ranked[1].score;
                int ratioPercent = Mathf.RoundToInt(AITuningConfig.Instance.劲敌次优分差比 * 100);
                if (second * 100 >= top * (100 - ratioPercent))
                {
                    chosen = ranked[1].action;
                    chosenScore = second;
                    GICLog.Info($"[配额脑] {_playerId} T{turn} 劲敌次优注入：榜 {ranked.Count} 候选" +
                        $"（最优 {top}→选中 {second}，分差比 {ratioPercent}% 内）");
                }
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

        // ==================== 部署分支（2026-10-12 AI 固定卡组批；凌晨批议题④⑦桶序升级） ====================

        /// <summary>部署桶（议题④⑦桶序化的分拣单元——桶内容与校验链不动、只动序）</summary>
        private enum DeployBucket
        {
            /// <summary>1★ 建筑首铺（旗帜：便宜+每秒滚雪球经济先行；同桶 unitName 升序）</summary>
            Flag = 0,
            /// <summary>角色（战力主力：unitName 升序 × 同名在场〔含尸体〕跳过——规避升命语义烧配额）</summary>
            Characters = 1,
            /// <summary>其余建筑（2★+ 与 1★ 复制：首铺优先〔同名存活升序〕→态势分档→星级→unitName）</summary>
            OtherBuildings = 2,
        }

        /// <summary>per-局开局部署原型（凌晨批议题④，ZK 开局策略原型加权抽选先例）：三桶序变体——
        /// 0=标准铺（旗→角→建=决策八十八观察线）1=经济先行（旗→建→角=餐馆提前滚雪球）2=速攻
        ///（角→旗→建=先铺战力抢节奏）。抽选=FNV-1a(seed‖playerId) mod 3——**不走 RollChance**
        ///（战斗 RNG 流为暴击专用〔EffectCompiler 唯一消费方〕，决策层消费会破「同 seed+同命令序列
        /// =同战局」回放不变式；哈希抽选同 seed 同战局+回放零漂移目标不变）；开局窗口内生效
        ///（AITuning.开局策略窗口秒数），窗口后回归态势桶序（议题⑦）</summary>
        private static int OpeningDeployVariant(int seed, string playerId)
        {
            unchecked
            {
                uint hash = 2166136261;
                for (int i = 0; i < 4; i++)
                {
                    hash = (hash ^ (byte)(seed & 0xFF)) * 16777619;
                    seed >>= 8;
                }
                if (!string.IsNullOrEmpty(playerId))
                {
                    foreach (var c in playerId)
                    {
                        hash = (hash ^ (byte)(c & 0xFF)) * 16777619;
                        hash = (hash ^ (byte)((c >> 8) & 0xFF)) * 16777619;
                    }
                }
                // murmur3 fmix32 终结器（avalanche）：裸 FNV-1a 短输入（12 字节）mod 3 有轻偏
                //（1200 样本实测 348/397/455 → 加终结器 405/395/400）——三变体抽选均匀性校正
                hash ^= hash >> 16;
                hash *= 2246822519;
                hash ^= hash >> 13;
                hash *= 3266489917;
                hash ^= hash >> 16;
                return (int)(hash % 3);
            }
        }

        /// <summary>
        /// 部署候选（确定性，2026-10-23 强化批扩建筑档；凌晨批议题④⑦桶序化）：
        /// 三桶分拣（旗帜首铺/角色/其余建筑）+ **桶序动态决定**——
        /// ①开局窗口内（≤AITuning.开局策略窗口秒数）=per-局开局原型（seed 派生抽选，议题④）；
        /// ②窗口后=态势桶序（议题⑦，petra 优先级预算队列映射）：发育=经济建筑优先（旗→建→角，
        /// 餐馆提前滚雪球）、守势=防御建筑优先（旗→建〔防御置顶〕→角）、压制/终局/相持=战力
        /// 输出优先（旗→角→建=现状序）；桶③内序含态势分档（经济/防御/军团置顶）。
        /// 落点合法性=DeployUnitExecutor.IsDeployCellValid 单源（**自己存活建筑锚并集**〔2026-10-23
        /// 拍板：核心锚建筑 3/角色 2、其它建筑锚各减 1〕+地形+碰撞共享原语）；
        /// 摩拉=GetEffectiveDeployCost 同源（不足的候选入桶前已滤——先攒；红线语义维持）。
        /// 每秒拍部署配额一张，逐桶找首个可出战候选。
        /// </summary>
        private ActionData TryBuildDeploy(BattleSimState sim, int turn)
        {
            var hand = sim.GetHand(_playerId);
            if (hand == null || hand.Count == 0) return null;
            var config = Wargame.Instance?.Context?.Get<UnitConfig>();
            if (config == null) return null;

            // 手牌单位卡收集（同卡多张去重=一次只出一张；物品/货币卡不可部署）
            var unitCards = new List<(int unitName, UnitConfig.UnitData data)>();
            foreach (var card in hand)
            {
                if (!card.IsUnit) continue;
                if (unitCards.Exists(c => c.unitName == card.value)) continue;
                var data = config.GetUnitData((UnitName)card.value);
                if (data == null) continue;
                unitCards.Add((card.value, data));
            }

            // 三桶分拣（桶内排序见各桶注释）
            var firstLowStarBuildings = new List<(int unitName, UnitConfig.UnitData data)>(); // ① 1★ 建筑首铺
            var characters = new List<(int unitName, UnitConfig.UnitData data)>();           // ② 角色
            var otherBuildings = new List<(int unitName, bool sameNameAlive, UnitConfig.UnitData data)>(); // ③ 其余建筑
            foreach (var card in unitCards)
            {
                if (sim.GetMora(_playerId) < card.data.GetEffectiveDeployCost()) continue; // 摩拉不足先攒
                if (card.data.unitType != UnitType.Building) { characters.Add(card); continue; }
                // 建筑消散不留尸体=同名在场即存活（FindOwnUnitByName 含尸体口径对建筑等效存活）
                bool sameNameAlive = DeployUnitExecutor.FindOwnUnitByName(sim, _playerId, (UnitName)card.unitName) != null;
                if (card.data.starLevel <= 1 && !sameNameAlive)
                    firstLowStarBuildings.Add(card);
                else
                    otherBuildings.Add((card.unitName, sameNameAlive, card.data));
            }
            firstLowStarBuildings.Sort((a, b) => a.unitName.CompareTo(b.unitName)); // unitName 升序（枚举序铁律）
            characters.Sort((a, b) => a.unitName.CompareTo(b.unitName));

            // —— 桶序决定（议题④开局原型 × 议题⑦态势预算）——
            var stance = AIStrategist.StanceOf(_playerId);
            DeployBucket[] order;
            bool openingWindow = turn <= AITuningConfig.Instance.开局策略窗口秒数;
            if (openingWindow)
            {
                int variant = OpeningDeployVariant(sim.Seed, _playerId);
                if (turn == 1)
                    GICLog.Info($"[配额脑] {_playerId} T{turn} 开局部署原型={variant}" +
                        (variant == 0 ? "（标准铺：旗→角→建）" : variant == 1 ? "（经济先行：旗→建→角）" : "（速攻：角→旗→建）"));
                order = variant switch
                {
                    1 => new[] { DeployBucket.Flag, DeployBucket.OtherBuildings, DeployBucket.Characters },
                    2 => new[] { DeployBucket.Characters, DeployBucket.Flag, DeployBucket.OtherBuildings },
                    _ => new[] { DeployBucket.Flag, DeployBucket.Characters, DeployBucket.OtherBuildings },
                };
            }
            else
            {
                // 窗口后：态势桶序（议题⑦——桶内容与校验链不动、只动序；摩拉红线维持）
                order = stance switch
                {
                    AIStrategist.AIStance.Develop => new[] { DeployBucket.Flag, DeployBucket.OtherBuildings, DeployBucket.Characters },
                    AIStrategist.AIStance.Defend => new[] { DeployBucket.Flag, DeployBucket.OtherBuildings, DeployBucket.Characters },
                    _ => new[] { DeployBucket.Flag, DeployBucket.Characters, DeployBucket.OtherBuildings },
                };
            }

            // 桶③内序（议题⑦态势分档）：首铺优先 → 态势置顶档（发育=经济/守势=防御/压制=军团）
            // → 星级 → unitName（分档只是同档内前置，星级链校验仍在 Host 兜底——跳档候选自然落空）
            int StanceTier(BuildingEffectKind kind)
                => stance switch
                {
                    AIStrategist.AIStance.Develop =>
                        kind == BuildingEffectKind.MoraIncome || kind == BuildingEffectKind.EnergyAura ? 0 : 1,
                    AIStrategist.AIStance.Defend =>
                        kind == BuildingEffectKind.HealWounded ? 0 : 1,
                    AIStrategist.AIStance.Push or AIStrategist.AIStance.Endgame =>
                        kind == BuildingEffectKind.EnergySummon || kind == BuildingEffectKind.PeriodicSummon ? 0 : 1,
                    _ => 0, // 相持=无分档（维持星级序）
                };
            otherBuildings.Sort((a, b) =>
            {
                if (a.sameNameAlive != b.sameNameAlive) return a.sameNameAlive.CompareTo(b.sameNameAlive);
                int ta = StanceTier(a.data.建筑效果), tb = StanceTier(b.data.建筑效果);
                if (ta != tb) return ta.CompareTo(tb);
                if (a.data.starLevel != b.data.starLevel) return a.data.starLevel.CompareTo(b.data.starLevel);
                return a.unitName.CompareTo(b.unitName);
            });

            // 逐桶找首个可出战候选（桶序=order；每桶内枚举序铁律）
            foreach (var bucket in order)
            {
                switch (bucket)
                {
                    case DeployBucket.Flag:
                        foreach (var candidate in firstLowStarBuildings)
                        {
                            var action = TryDeployBuildingCard(sim, turn, candidate.unitName, candidate.data);
                            if (action != null) return action;
                        }
                        break;
                    case DeployBucket.Characters:
                        // 同名在场〔含尸体〕=暂不出战，卡不消耗
                        foreach (var candidate in characters)
                        {
                            if (OwnUnitOnField(sim, (UnitName)candidate.unitName)) continue;
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
                        break;
                    case DeployBucket.OtherBuildings:
                        foreach (var candidate in otherBuildings)
                        {
                            var action = TryDeployBuildingCard(sim, turn, candidate.unitName, candidate.data);
                            if (action != null) return action;
                        }
                        break;
                }
            }
            return null;
        }

        /// <summary>建筑卡出战尝试（星级链+每种上限+合法落点三重校验——与 Host 执行器同源；
        /// 任一不满足=本候选跳过，摩拉已在分桶时过滤）</summary>
        private ActionData TryDeployBuildingCard(BattleSimState sim, int turn, int unitNameValue,
            UnitConfig.UnitData data)
        {
            if (!DeployUnitExecutor.PassesBuildingStarGate(sim, _playerId, data)) return null; // 星级链
            if (!DeployUnitExecutor.PassesBuildingLimit(sim, _playerId, data)) return null;     // 每种上限 3
            var cell = FindDeployCell(sim, data);
            if (cell == null) return null;
            return new ActionData
            {
                playerId = _playerId,
                actionType = ActionType.DeployUnit,
                deployUnitName = unitNameValue,
                deployCell = cell.Value,
                turnNumber = turn,
            };
        }

        /// <summary>己方同名单位在场判定（**含尸体**——单源=DeployUnitExecutor.FindOwnUnitByName，
        /// docs/05 §5.4 尸体也仅升命不复苏：同名在场（含尸体）时再打出该卡=给尸体升命烧配额，
        /// 部署分支守卫跳过〔升命候选另走 EvaluateUpgrades 存活口径〕；卡不消耗）</summary>
        private bool OwnUnitOnField(BattleSimState sim, UnitName unitName)
            => DeployUnitExecutor.FindOwnUnitByName(sim, _playerId, unitName) != null;

        // ==================== 购买分支（2026-10-23 强化批立项；凌晨批议题②购物清单化） ====================

        /// <summary>
        /// AI 购买（凌晨批议题②）：游戏规则购买**无每回合次数限制**（2026-10-24 用户勘正）——
        /// 旧版「买入即 return」自限一购已拆除，改**购物清单循环**（BuyCard 即时通道不占配额，
        /// LocalBattleTransport 同步结算——手牌/摩拉即时更新）：
        /// ①清单=旗帜补货（协议核心出售；手牌无旗帜卡——卡不消耗=终身部署材料，整局至多触发一次）
        /// +角色补货（己方存活建筑可购买列表×手牌无同名卡×仅角色×配置存在×**技能已实装**
        /// 〔任一技能 HasEffects——观察局实锤琴技能全占位，买半成品=浪费；琴技能实装批自然激活〕）；
        /// ②预算=当前摩拉 − 摩拉红线（AITuning.摩拉红线——与升命红线同源的保底语义，购买不掏空
        /// 部署/升命资金池）；每项另须预留部署价（买来当拍即可部署）；
        /// ③价值序=旗帜（部署锚+经济使能件）→角色 unitName 升序（购买价值无客观序，枚举序铁律）；
        /// ④循环购入直至清单耗尽/预算不足（手牌守卫在清单构建期一次性评估+同名单去重=无重复购）。
        /// 购物上限 8 件/秒=防御性护栏（正常对局清单 1~3 件远达不到）。
        /// 解锁门槛由 Host BuyCardExecutor 权威校验兜底（AI 未解锁角色=购买落空零损失）；
        /// 失败静默——下一拍再试。商店建筑枚举序=unitId 升序（确定性枚举序铁律）。
        /// </summary>
        private void TryPurchase(BattleSimState sim, int turn)
        {
            // P6 残留审计 C6（docs/active/49）：决策节拍=每 0.5 秒，旧「每回合至多一次」注释对应的
            // 实际门控不存在——补逻辑秒门控，恢复「每回合（=1 秒）至多一次」的 1:1 语义。
            if (turn == _lastPurchaseSecond) return;
            _lastPurchaseSecond = turn;

            var hand = sim.GetHand(_playerId);
            if (hand == null) return;
            var unitConfig = Wargame.Instance?.Context?.Get<UnitConfig>();
            if (unitConfig == null) return;
            var itemConfig = Wargame.Instance?.Context?.Get<ItemConfig>();
            int moraRedLine = AITuningConfig.Instance.摩拉红线;

            // —— 购物清单构建（tier 0=旗帜/1=角色；档内 unitName 升序）——
            var list = new List<(int tier, int unitName, int cost, int deployReserve)>();

            // ① 旗帜补货（协议核心出售）：手牌无旗帜卡 → 买一张（卡不消耗=终身部署材料）
            bool handHasFlag = false;
            foreach (var card in hand)
            {
                if (card.IsUnit && card.value == (int)UnitName.FavoniusBanner) { handHasFlag = true; break; }
            }
            if (!handHasFlag)
            {
                var flagData = unitConfig.GetUnitData(UnitName.FavoniusBanner);
                if (flagData != null)
                {
                    var flagCard = new CardId(UnitName.FavoniusBanner);
                    list.Add((0, (int)UnitName.FavoniusBanner,
                        flagCard.PurchaseCostOf(unitConfig, itemConfig), flagData.GetEffectiveDeployCost()));
                }
            }

            // ② 角色补货：自己存活建筑的可购买列表 × 手牌无同名卡 × 仅角色卡 × 配置存在
            //    × 技能已实装 → 入清单（unitId 升序枚举+多建筑同售去重）
            var shopBuildings = new List<KeyValuePair<string, Unit>>();
            foreach (var kv in sim.SortedUnits())
            {
                var unit = kv.Value;
                if (unit == null || BattleSimState.IsDead(unit)) continue;
                var identity = unit.GetUnitComponent<UnitIdentity>();
                if (identity == null || identity.OwnerPlayerID != _playerId) continue;
                var data = unit.RawData;
                if (data == null || data.unitType != UnitType.Building) continue;
                if (data.可购买卡片 == null || data.可购买卡片.Count == 0) continue;
                shopBuildings.Add(kv);
            }
            shopBuildings.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key)); // unitId 升序（枚举序铁律）
            var seenCharacters = new HashSet<int>();
            foreach (var shop in shopBuildings)
            {
                foreach (var purchasable in shop.Value.RawData.可购买卡片)
                {
                    if (purchasable.cardType != CardType.Unit) continue; // 物品卡不买（无使用档=死牌）
                    if (!seenCharacters.Add(purchasable.value)) continue; // 多建筑同售去重（清单内同名一张）
                    bool inHand = false;
                    foreach (var card in hand)
                    {
                        if (card.IsUnit && card.value == purchasable.value) { inHand = true; break; }
                    }
                    if (inHand) continue; // 卡不消耗——手牌已有同名卡再买=死牌
                    var data = unitConfig.GetUnitData(purchasable.AsUnitName());
                    if (data == null) continue; // 缺配置（琴未入 UnitConfig）=免费死牌不买
                    if (data.unitType == UnitType.Building) continue; // 建筑卡不买（卡组自备）
                    // 技能已实装门槛（2026-10-23 观察局实锤：琴在 UnitConfig 但技能全占位
                    // 〔SkillFactory 未配置效果原子×5〕——AI 买半成品角色=纯浪费；技能实装批自然进购买域）
                    bool anySkillImplemented = false;
                    if (data.skills != null)
                    {
                        foreach (var skill in data.skills)
                        {
                            if (skill == null || skill.data == null) continue;
                            if (skill.data.HasEffects) { anySkillImplemented = true; break; }
                        }
                    }
                    if (!anySkillImplemented) continue;
                    list.Add((1, purchasable.value,
                        purchasable.PurchaseCostOf(unitConfig, itemConfig), data.GetEffectiveDeployCost()));
                }
            }
            list.Sort((a, b) => a.tier != b.tier ? a.tier.CompareTo(b.tier)
                : a.unitName.CompareTo(b.unitName)); // 价值序：旗帜→角色 unitName 升序（枚举序铁律）

            // —— 循环购入（预算=摩拉 − 红线；每项预留部署价；预算不足跳下一项——更便宜的可能买得起）——
            int purchased = 0;
            foreach (var item in list)
            {
                if (purchased >= 8) break; // 防御性护栏（正常对局清单 1~3 件远达不到）
                int mora = sim.GetMora(_playerId);
                if (mora - item.cost - item.deployReserve < moraRedLine) continue;
                _session.SubmitInstantAction(new ActionData
                {
                    playerId = _playerId,
                    actionType = ActionType.BuyCard,
                    buyCardType = (int)CardType.Unit,
                    buyCardValue = item.unitName,
                    turnNumber = turn,
                });
                purchased++;
                GICLog.Info($"[配额脑] {_playerId} T{turn} 购买 {(UnitName)item.unitName}" +
                    $"（{item.cost} 摩拉，预留部署 {item.deployReserve}，红线 {moraRedLine}，本轮已购 {purchased}）");
            }
        }

        // ==================== 升命候选（AI-1 批，2026-10-12 docs/active/42 §11） ====================

        /// <summary>
        /// 升命候选（确定性）：手牌 3★+ 角色卡 unitName 升序（同卡多张去重）→ 同名**存活**单位在场
        /// 且 ConstellationLevel&lt;MaxConstellation 且摩拉够 → DeployUnit 同名路径升命候选
        /// （评分=UpgradeConstellationScore 入池与号令/操控竞争，非部署式无条件优先）。
        /// 校验口径全镜像 DeployUnitExecutor.TryResolve 升命分支（starLevel≥3/满命/GetEffectiveDeployCost）；
        /// 尸体不升（复苏前置投资=后续批次；现行「给尸体升命」判定为烧配额维持跳过）；
        /// deployCell 升命分支不消费（执行器升命先于落点校验），填核心位保语义。
        /// upgradeMul（AI-4/5 批）：人格升命桶×态势升命乘子（调用点合成——body 只乘一次防双乘；
        /// 摩拉红线腰斩在战略层 ApplyRedLines 已乘入 strat.upgradeMul）
        /// </summary>
        private void EvaluateUpgrades(BattleSimState sim, int turn, CompanionBrain.CandidateTracker tracker,
            float upgradeMul = 1f)
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
                if (data.unitType == UnitType.Building) continue; // 建筑无命座（决策八十六⑦「升命分支对建筑不适用」
                // ——执行器同口径已分流铺场链）。2026-10-24 观察局 T24 实证 3★ 建筑混入候选靠平票侥幸未中：
                // 选中后会以核心格为落点走部署链烧摩拉——升命候选域排除建筑
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
                int constellationChoice = 0;
                int optionScore = 0;
                if (candidate.data.HasBranchConstellation)
                {
                    int nextLevel = existing.ConstellationLevel + 1;
                    var tiers = candidate.data.constellationTiers;
                    if (nextLevel > tiers.Count) continue;
                    var options = tiers[nextLevel - 1].options;
                    UnitConfig.ConstellationOption selected = null;
                    if (options != null)
                        foreach (var option in options)
                        {
                            if (option?.skill?.data == null) continue;
                            if (selected != null && (option.aiScore < selected.aiScore
                                || (option.aiScore == selected.aiScore
                                    && option.skill.data.skillID >= selected.skill.data.skillID))) continue;
                            selected = option;
                        }
                    if (selected == null) continue;
                    constellationChoice = (int)selected.skill.data.skillID;
                    optionScore = selected.aiScore;
                }
                // AI-4：人格升命桶乘子（经济家/守城者更爱投资）；AI-5：态势升命乘子（upgradeMul=两乘已合成）
                int upgradeScore = Mathf.RoundToInt(UpgradeConstellationScore * upgradeMul) + optionScore;
                GICLog.Info($"[配额脑] {_playerId} T{turn} 升命候选 {(UnitName)candidate.unitName}" +
                    $" C{existing.ConstellationLevel}→C{existing.ConstellationLevel + 1}" +
                    $" 分={upgradeScore}（当前最优 {tracker.BestScore}）");
                tracker.Offer(upgradeScore, new ActionData
                {
                    playerId = _playerId,
                    actionType = ActionType.DeployUnit,
                    deployUnitName = candidate.unitName,
                    constellationChoice = constellationChoice,
                    deployCell = sim.GetCorePosition(_playerId),
                    turnNumber = turn,
                });
            }
        }

        /// <summary>首个合法部署落点（2026-10-23 锚点拍板后多锚环扫）：锚序=CollectDeployAnchors
        /// 单源（核心优先+unitId 升序——安全区先铺的确定性枚举），每锚由近及远环序（1 环→N 环、
        /// 行优先列序）；锚半径=DeployRadiusOf 同源（P3 cm 制→÷CellSizeCm 回格环扫——建筑 3/2 格、
        /// 角色 2/1 格不变）</summary>
        private BattleCell? FindDeployCell(BattleSimState sim, UnitConfig.UnitData deployData)
        {
            bool deployIsBuilding = deployData != null && deployData.unitType == UnitType.Building;
            foreach (var anchor in DeployUnitExecutor.CollectDeployAnchors(sim, _playerId))
            {
                int radius = DeployUnitExecutor.DeployRadiusOf(anchor.isCore, deployIsBuilding)
                    / CellMetrics.CellSizeCm; // P3 cm→格桥（格环扫）
                for (int ring = 1; ring <= radius; ring++)
                {
                    for (int dy = -ring; dy <= ring; dy++)
                    {
                        for (int dx = -ring; dx <= ring; dx++)
                        {
                            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring) continue; // 只遍历当前环
                            var cell = new BattleCell(anchor.cell.x + dx, anchor.cell.y + dy);
                            if (DeployUnitExecutor.IsDeployCellValid(sim, _playerId, cell, deployData))
                                return cell;
                        }
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
            foreach (var kv in sim.SortedUnits())
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
            foreach (var kv in sim.SortedUnits())
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
            foreach (var kv in sim.SortedUnits())
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
        /// 号令候选（势力技能=玩家对自主军团的指挥通道，docs/32 §4）：占玩家 1 配额、
        /// 势力技能自身 0 体力（docs/07-00 口径）；目标域=我军存活单位（含自身）unitId 升序——
        /// 延奏目标域=阵营口径（2026-09-25 三轮审查 C2：2v2 可协奏队友单位）；行动者归属=playerId 保留；
        /// 估值=治疗缺口+增益未满层+变奏链+协奏元能（OnCast 效果原子逐个粗估，与 EffectCompiler 产出同语义）。
        /// AI-1 批传输档：非蒙德目标不再滤除（docs/07-势力机制/蒙德.md——+20 元能不触发变奏，
        /// 延奏自身 buff/治疗对任何目标照常生效；当前固定卡组全蒙德=传输档待混搭卡组激活）
        /// ensoMul（AI-4/5 批）：人格契约桶×态势契约乘子（调用点合成——body 只乘一次防双乘）
        /// </summary>
        private void EvaluateEnsoSkills(BattleSimState sim,
            Unit caster, int turn, CompanionBrain.CandidateTracker tracker,
            OpponentThreatModel.ThreatMap threat, float ensoMul = 1f)
        {
            var skills = caster.Skills;
            for (int i = 0; i < skills.Count; i++)
            {
                var skill = skills[i];
                var data = skill?.RawData;
                if (data == null || data.skillType != SkillType.Enso) continue; // 契约 AI 档随其实装批接线（docs/11）
                if (!skill.CanCast(caster)) continue;
                if (!ResourceGate.HasAll(sim, caster, _playerId, data.costs, out _)) continue; // 消耗门槛（C-2 costs 单源）
                // AI 施放节流（P5-D）：延奏无 CD——0.5s 节拍下自我延奏每拍刷分（分恒 38 压一切候选）；
                // 按 AITuning「AI 重施最短间隔 tick」节流后回到战术节拍
                if (BattleHeuristics.AiCastThrottled(sim, caster.GetUnitComponent<UnitIdentity>()?.UnitID, i)) continue;

                // 候选目标：我军存活**角色**（含自身）unitId 升序——延奏目标域=阵营口径
                // （2026-09-25 三轮审查 C2：2v2 可协奏队友单位）；行动者归属=playerId 保留。
                // 建筑排除（AI-4 观察局实锤返修）：docs/07 蒙德「我方**角色**」——协议核心是建筑
                // 非角色，buff/变奏/元能对核心零收益（治疗核心归伙伴治疗技能域），混入候选
                // 只会污染评分榜（交涉家契约桶 ×1.6 放大后显形——学徒失误注入选中过给核心的延奏）
                var allies = new List<(string unitId, Unit unit)>();
                foreach (var kv in sim.SortedUnits())
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
                    // AI-4：人格契约桶乘子（交涉家/指挥官更爱号令）；AI-5：态势契约乘子（ensoMul=两乘已合成）
                    tracker.Offer(Mathf.RoundToInt(value * ensoMul), Skill(caster, i, Direction2D.Right, turn, ally.unitId));
                }
            }
        }

        /// <summary>延奏对某目标的估值：按 OnCast 效果原子逐个粗估（与 EffectCompiler 产出同语义）。
        /// mondOrSelf=蒙德角色或施法者自身（AI-1 批传输档参数）：true→变奏链+元能 +10 档；
        /// false→无变奏、元能 +20 档双倍评分（延奏自身 buff/治疗对任何目标照常生效）。
        /// E-1：threat=威胁图（治疗救命档消费；null=无威胁上下文零加成）
        /// 死水修复（2026-10-20 决策八十四，用户拍板精确语义）：目标该 buff 已满层时降权——
        /// 限时且快到期（剩余≤延奏续航临近到期秒数，断了要重新叠层）=续航分；时长充足或永久=零新收益，
        /// 且变奏链/元能同步跳过（纯续航不构成行动理由——否则满层延奏恒压放权=自我延奏死水循环，
        /// 双 AI 观察局 T11 起实锤）；治疗原子照常（缺口判定自带过滤）。
        /// 未持有/未满层=全额（叠层+叠时高收益，拍板保留）。
        /// 注：旧满层判定读技能参数 paramKey2——决策五十六 buff 参数迁资产后技能侧恒空、
        /// 判定失效=死水分恒 32 的根因；现改 BaseBuff.IsAtStackCap（资产单源）。</summary>
        private int EvaluateEnsoTarget(SkillConfig.SkillData data, Unit caster, Unit ally,
            string allyUnitId, OpponentThreatModel.ThreatMap threat, bool mondOrSelf)
        {
            if (data.effects == null) return 0;
            var allyStats = ally.GetUnitComponent<UnitStats>();
            if (allyStats == null) return 0;
            int value = 0;
            bool buffCappedNoGain = false; // 核心 buff 已满层且无新叠层收益（续航档门）

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
                        // E-1 救命分（docs/archive/33 §2.3→F-3 上提共享单源 CompanionBrain.RescueScore）：
                        // 伤员在威胁图 DoomedAllies（敌方预测合计承伤 ≥ 当前 hp=将被集火致死）
                        // → 预治疗保命档（斩杀档对偶）
                        if (threat != null && threat.DoomedAllies.Contains(allyUnitId))
                            value += CompanionBrain.RescueScore;
                        break;
                    }

                    case SkillEffectKind.ApplyBuff:
                    {
                        // 增益：满层判定走 BaseBuff.IsAtStackCap（BuffConfig 资产单源——决策五十六
                        // buff 参数迁资产后技能参数侧恒空，旧 paramKey2 判定失效=死水根因）
                        var existing = ally.Buffs.Find(b => b != null && (atom.buffAsset != null
                            ? b.Config == atom.buffAsset : b.Type == atom.buffType));
                        if (existing != null && existing.IsAtStackCap())
                        {
                            // 满层：限时且快到期=续航分（避免断档重新叠层）；时长充足/永久=零
                            buffCappedNoGain = true;
                            if (!existing.IsPermanent
                                && existing.RemainingSeconds <= AITuningConfig.Instance.延奏续航临近到期秒数)
                                value += AITuningConfig.Instance.延奏满层续航分;
                            break;
                        }
                        // 未持有/未满层：全额（叠层+叠时高收益——用户拍板保留）
                        int buffValue = atom.paramKey != SkillParamKey.None ? data.GetInt(atom.paramKey, 0) : atom.value;
                        value += buffValue + EnsoBuffActionScore;
                        break;
                    }

                    case SkillEffectKind.TriggerSkill:
                    {
                        if (buffCappedNoGain) break; // 纯续航延奏不计变奏链（死水修复）
                        // 变奏链：被协者该型技能已实装（effects 非空）才有产出（UnimplementedSkill 空产出）
                        // ——仅蒙德角色/施法者自身触发变奏（非蒙德=纯传输档无变奏链，AI-1 批）
                        if (mondOrSelf && HasImplementedSkillOfType(ally, atom.targetSkillType))
                            value += EnsoTriggerChainScore;
                        break;
                    }

                    case SkillEffectKind.EnergyGain:
                        if (buffCappedNoGain) break; // 纯续航延奏不计元能转移（死水修复）
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
