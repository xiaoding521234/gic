using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using GIC.Data;
using GIC.Framework;
namespace GIC.Battle
{


    /// <summary>
    /// AI 战略层（AI-5 批，docs/active/42 §5/§11）：五态状态机＋指挥黑板（态势派生桶乘子）＋
    /// 三账本预算器（硬保底/红线）＋军团倾向三态＋ResourceDigest 报告链。
    /// 协作机制（§3）：**权重注入而非指令下达**——战略层只调战役层效用乘子与军团软系数，
    /// 不直接指定行动；战术不盲从战略（斩杀分恒赢=设计意图）。
    /// 生命周期：进程级 static＋新战斗自清（sim 实例号判据——2026-10-15 审查修正，OpponentProfiler 同款）。
    /// 确定性：全链纯确定性推演（快照/单位表直读，零随机）。
    /// v1 标定注记：五态阈值=§5.1 示例值（待实测标定）；态势**每回合重估**（§5 报告链的
    /// 「每 3 回合例行重估」=成本优化预留——驻留+滞回已保稳，每回合重估等价且更简）；
    /// 意图黑板 v1=态势派生桶乘子（意图清单随决策日志输出，目标定向加成随 AI-3 外交层）。
    /// </summary>
    public static class AIStrategist
    {
        /// <summary>五态（§5.1；进入判据阈值=示例值待实测标定）</summary>
        public enum AIStance
        {
            [InspectorName("发育")] Develop = 0,
            [InspectorName("相持")] Stalemate = 1,
            [InspectorName("压制")] Push = 2,
            [InspectorName("守势")] Defend = 3,
            [InspectorName("终局")] Endgame = 4,
        }

        /// <summary>军团倾向三态（§3 协作机制 2——软系数进伙伴脑：回防态激进度下调；
        /// 推进=基线零变化；集结语义随多单位协同标定=v1 与推进同档）</summary>
        public enum AILegionTendency
        {
            [InspectorName("推进")] Push = 0,
            [InspectorName("集结")] Hold = 1,
            [InspectorName("回防")] Regroup = 2,
        }

        // ==================== 标定常量（v1 示例值——全部待实测标定） ====================

        /// <summary>战力比进入压制阈值（>1.3=我方战力优势）；退回滞回 1.15（±0.15）</summary>
        private const float PushRatioEnter = 1.3f;
        private const float PushRatioStay = 1.15f;

        /// <summary>战力比进入守势阈值（&lt;0.7=劣势）；退回滞回 0.85</summary>
        private const float DefendRatioEnter = 0.7f;
        private const float DefendRatioStay = 0.85f;

        /// <summary>最小驻留回合数（防抖动）</summary>
        private const int MinStanceHoldTurns = 3;

        /// <summary>发育期回合数（开局铺场）</summary>
        private const int DevelopTurns = 3;

        /// <summary>终局判据（1v1 形态）：敌方核心 HP 占比低于此值=全资源转火</summary>
        private const float EndgameCoreHpRatio = 0.15f;

        /// <summary>回防态激进度乘子（§3「激进度下调」——军团软系数作用于斩杀加成）</summary>
        public const float RegroupAggressionMul = 0.7f;

        /// <summary>经济红线（§5.3）：摩拉&lt;50 发育桶（升命）腰斩；体力&lt;15 进攻桶（操控/魔神）腰斩</summary>
        private const int MoraRedLine = 50;
        private const int StaminaRedLine = 15;

        // ==================== 状态 ====================

        /// <summary>单 AI 玩家战略态（态势/军团/桶乘子/红线门/报告链）</summary>
        public sealed class StrategistState
        {
            public AIStance Stance = AIStance.Develop;
            public AILegionTendency Legion = AILegionTendency.Push;
            public int StanceSinceTurn = 1; // 现态势进入回合（最小驻留计数）

            /// <summary>ResourceDigest（§3 报告链——每回合更新）</summary>
            public int Mora;
            public int Stamina;
            public int ArmyValue;        // 我方存活非建筑价值（出战费加总=战力代理）
            public int EnemyArmyValue;
            public float PowerRatio;     // ArmyValue/max(1,Enemy)
            public float MyCoreHpRatio = 1f;
            public float EnemyCoreHpRatio = 1f;
            public bool CoreThreatened;  // 我核心在敌预测火线（守势判据之一）

            /// <summary>三账本（§5.3——v1 消费面：体力硬保底门+摩拉/体力红线乘子；元能账=dormant）</summary>
            public int StaminaFloor;     // 当前伙伴数×5×2（自主军团 2 回合运转量——配额体力支出硬保底）

            /// <summary>指挥黑板（§5.2 态势派生桶乘子——1=中性；意图清单随战略层日志输出）。
            /// 英文名=纯逻辑标识符规范（docs/20 §1.1——中文直名例外仅限 Inspector 序列化字段，
            /// 2026-10-15 审查拍板改英文）；语义对应 PersonaPack 四桶权重（军备/契约/升命/科技）</summary>
            public float armyMul = 1f;
            public float contractMul = 1f;
            public float upgradeMul = 1f;
            public float techMul = 1f;

            /// <summary>意图清单（态势派生——随日志输出；目标定向加成随 AI-3）</summary>
            public readonly List<string> Intents = new List<string>();
        }

        private static readonly Dictionary<string, StrategistState> _states = new();
        private static int _lastSimInstance; // 新战斗自清判据=对局实例号（见 UpdateForTurn 头部）

        /// <summary>态势只读查询（伙伴脑/调试）</summary>
        public static AIStance StanceOf(string playerId)
            => _states.TryGetValue(playerId, out var s) ? s.Stance : AIStance.Develop;

        /// <summary>军团激进度乘子查询（伙伴脑斩杀加成消费；未注册=1）</summary>
        public static float LegionAggressionOf(string playerId)
            => _states.TryGetValue(playerId, out var s) && s.Legion == AILegionTendency.Regroup
                ? RegroupAggressionMul
                : 1f;

        // ==================== 每回合更新（配额脑 Decide 头部调） ====================

        /// <summary>
        /// 战略层回合更新：报告链→态势机（滞回+驻留）→军团倾向→黑板乘子→红线。
        /// 人格基线×态势乘子=乘法合成于配额脑消费点（人格只改权重不改规则，§9.1「性格≠刻板」
        /// ——态势判据对人格不可感知，故本层不持人格引用）。
        /// 学徒档「战略层仅发育态」（§9.2 开启件）：跳过态势机与威胁图构建，恒发育乘子
        /// ＋红线/体力保底照常（三账本是硬保底语义非决策质量件，全档生效）。
        /// </summary>
        public static StrategistState UpdateForTurn(BattleSimState sim, BattleSnapshot snapshot,
            string playerId, TeamType team, int turnNumber)
        {
            // 新战斗自清：sim 实例号（2026-10-15 审查修正——每局新 BattleSimState 恒新值；旧「T1 重入＋
            // 回合回退」双判据在部署早退形态下靠回合回退兜底，但对「上局仅观测到回合 1」的秒弃调试局
            // 失效〔T1==T1 不触发〕→陈旧战略态跨局泄漏；实例号判据恒新局必变、同局恒同，两种形态全覆盖）
            if (sim.InstanceId != _lastSimInstance)
            {
                _states.Clear();
                _lastSimInstance = sim.InstanceId;
            }

            var state = GetOrCreate(playerId);

            // ── ResourceDigest（§3 报告链）──
            int myValue = 0, enemyValue = 0;
            var enemyCores = new List<Unit>();
            Unit myCore = null;
            foreach (var kv in sim.Units)
            {
                var unit = kv.Value;
                var id = unit.GetUnitComponent<UnitIdentity>();
                if (id == null || unit.RawData == null) continue;
                if (BattleSimState.IsDead(unit)) continue;
                bool mine = id.Team == team;
                if (unit.RawData.unitType == UnitType.Building)
                {
                    if (mine) myCore = unit;
                    else enemyCores.Add(unit);
                    continue; // 建筑（协议核心）不入战力值——战力=机动兵力
                }
                int value = unit.RawData.GetEffectiveDeployCost();
                if (mine) myValue += value;
                else enemyValue += value;
            }
            state.Mora = sim.GetMora(playerId);
            state.Stamina = sim.GetStamina(playerId);
            state.ArmyValue = myValue;
            state.EnemyArmyValue = enemyValue;
            // 空盘守卫：双方机动兵力均为 0（开局未部署/全灭对峙）=战力比语义未定义，取中性 1.0
            // ——否则 0/max(1,0)=0 会伪触发守势（观察局 Match B 实证 T1 伪 Defend、T2 部署后退回）
            state.PowerRatio = myValue + enemyValue > 0
                ? myValue / (float)Mathf.Max(1, enemyValue)
                : 1f;

            var myStats = myCore != null ? myCore.GetUnitComponent<UnitStats>() : null;
            state.MyCoreHpRatio = myStats != null && myStats.HP > 0
                ? myStats.HP / (float)Mathf.Max(1, myStats.GetStatStruct(StatType.HP).Max)
                : 0f;
            float worstEnemyCore = 1f;
            foreach (var core in enemyCores)
            {
                var st = core.GetUnitComponent<UnitStats>();
                if (st == null || st.HP <= 0) { worstEnemyCore = 0f; break; }
                worstEnemyCore = Mathf.Min(worstEnemyCore, st.HP / (float)Mathf.Max(1, st.GetStatStruct(StatType.HP).Max));
            }
            state.EnemyCoreHpRatio = worstEnemyCore;

            // 核心受威胁=我核心格在敌预测火线（§5.1 守势判据；威胁图 ms 级——每 AI 每回合一张）。
            // 学徒=战略层仅发育态（§9.2）：不建图（CoreThreatened 恒 false——学徒档配额脑本就
            // 无威胁图，战略侧同步零成本）；三账本红线与体力保底非态势机件、全档照常
            bool developOnly = AIPlayerConfig.StrategistDevelopOnly(AIPlayerConfig.DifficultyOf(playerId));
            state.CoreThreatened = false;
            if (!developOnly)
            {
                var myCoreCell = sim.GetCorePosition(playerId);
                var threat = OpponentThreatModel.Build(sim, snapshot, team, turnNumber);
                if (threat != null && threat.ThreatenedCells.Contains(myCoreCell))
                    state.CoreThreatened = true;
            }

            // ── 三账本（§5.3）：体力硬保底=当前伙伴数×5×2（自主军团 2 回合运转量）──
            int companionCount = 0;
            foreach (var kv in sim.Units)
            {
                var unit = kv.Value;
                var id = unit.GetUnitComponent<UnitIdentity>();
                if (id == null || id.OwnerPlayerID != playerId) continue;
                if (!BattleHeuristics.IsCompanion(unit) || BattleSimState.IsDead(unit)) continue;
                companionCount++;
            }
            state.StaminaFloor = companionCount * 5 * 2;

            // ── 终局多玩家判据数据（§5.1「核心存活的敌对玩家仅剩一队」→ 2026-10-15 审查修正：按**队伍**
            // 聚合——按玩家计数在 2v2+AI 房间击倒一名敌对玩家即全队误入终局〔敌队仍有满编玩家，军备×1.6
            // 全转火为时过早〕；1v1/2v2 恒 2 队=本分支天然不触发〔终局信号=敌方核心濒破分支〕，3+ 队形态
            // （FFA/未来多队）才触发；核心身份=Core tag（每队按「任一存活核心」计，tag 化语义自洽） ──
            int totalCoreTeams = 0, enemyCoreTeams = 0;
            {
                var seenTeams = new HashSet<TeamType>();
                foreach (var kv in sim.Units)
                {
                    var unit = kv.Value;
                    var id = unit.GetUnitComponent<UnitIdentity>();
                    if (id == null || !BattleHeuristics.IsCore(unit)) continue;
                    if (BattleSimState.IsDead(unit) || !seenTeams.Add(id.Team)) continue;
                    totalCoreTeams++;
                    if (id.Team != team) enemyCoreTeams++;
                }
            }

            // ── 五态状态机（滞回±0.15＋最小驻留 3 回合）；学徒=恒发育态（§9.2）──
            if (developOnly)
            {
                if (state.Stance != AIStance.Develop) SwitchStance(state, AIStance.Develop, turnNumber);
                state.Legion = AILegionTendency.Hold; // 发育=按兵集结
                ApplyStanceMultipliers(state);
                ApplyRedLines(state);
                return state;
            }
            UpdateStance(state, turnNumber, enemyCoreTeams, totalCoreTeams);

            // ── 军团倾向（§3 协作机制 2）──
            state.Legion = state.Stance switch
            {
                AIStance.Defend => state.CoreThreatened || state.MyCoreHpRatio < 0.5f
                    ? AILegionTendency.Regroup // 仅极端守势硬性回防
                    : AILegionTendency.Hold,
                AIStance.Push or AIStance.Endgame => AILegionTendency.Push,
                _ => AILegionTendency.Hold, // 发育/相持=按兵集结（基线推进语义由单位自主脑承担）
            };

            // ── 指挥黑板（§5.2 态势派生桶乘子）＋经济红线（§5.3）──
            ApplyStanceMultipliers(state);
            ApplyRedLines(state);

            return state;
        }

        // ==================== 内部 ====================

        private static StrategistState GetOrCreate(string playerId)
        {
            if (!_states.TryGetValue(playerId, out var s))
            {
                s = new StrategistState();
                _states[playerId] = s;
            }
            return s;
        }

        /// <summary>五态机：终局＞守势＞压制＞发育（回合≤3）＞相持；战力比迁移带滞回＋最小驻留。
        /// 终局/守势=硬判据（保命优先于防抖，不受驻留限制）；1v1 终局=敌方核心濒破，
        /// 多玩家终局=核心存活的敌对**队伍**仅剩一队（2026-10-15 审查修正：队伍聚合防 2v2 提前触发）</summary>
        private static void UpdateStance(StrategistState state, int turnNumber, int enemyCoreTeams, int totalCoreTeams)
        {
            // 硬判据 1：终局（多玩家=核心存活的敌对队伍≤1 且总队伍＞2——3+ 队形态才触发；1v1/2v2=敌方
            // 核心濒破分支）。终局不回退（败北即结算）
            bool endgame = state.EnemyCoreHpRatio < EndgameCoreHpRatio
                || (totalCoreTeams > 2 && enemyCoreTeams <= 1);
            if (endgame && state.Stance != AIStance.Endgame)
            {
                SwitchStance(state, AIStance.Endgame, turnNumber);
                return;
            }
            if (state.Stance == AIStance.Endgame) return;

            // 硬判据 2：守势（核心入敌预测火线 或 战力比劣势）——进出双阈值滞回
            bool wantDefend = state.CoreThreatened || state.PowerRatio < DefendRatioEnter;
            bool stayDefend = state.CoreThreatened || state.PowerRatio < DefendRatioStay;
            if (state.Stance == AIStance.Defend ? stayDefend : wantDefend)
            {
                if (state.Stance != AIStance.Defend) SwitchStance(state, AIStance.Defend, turnNumber);
                return;
            }
            if (state.Stance == AIStance.Defend) SwitchStance(state, AIStance.Stalemate, turnNumber); // 滞回通过=离开守势

            // 战力比滞回：压制进出双阈值
            if (state.Stance == AIStance.Push ? state.PowerRatio > PushRatioStay : state.PowerRatio > PushRatioEnter)
            {
                if (state.Stance != AIStance.Push) SwitchStance(state, AIStance.Push, turnNumber);
                return;
            }
            if (state.Stance == AIStance.Push) SwitchStance(state, AIStance.Stalemate, turnNumber);

            // 发育期（开局铺场）——在压制/守势之后（硬判据优先）
            if (turnNumber <= DevelopTurns)
            {
                if (state.Stance != AIStance.Develop) SwitchStance(state, AIStance.Develop, turnNumber);
                return;
            }
            if (state.Stance == AIStance.Develop && turnNumber - state.StanceSinceTurn >= MinStanceHoldTurns)
                SwitchStance(state, AIStance.Stalemate, turnNumber); // 驻留期满→相持兜底
            // 其余=维持现态（相持）
        }

        private static void SwitchStance(StrategistState state, AIStance next, int turnNumber)
        {
            if (state.Stance == next) return;
            GICLog.Info($"[战略层] 态势切换 {state.Stance} → {next}（T{turnNumber}，战力比 {state.PowerRatio:F2}）");
            state.Stance = next;
            state.StanceSinceTurn = turnNumber;
        }

        /// <summary>态势派生桶乘子（§5.2 指挥黑板——权重注入而非指令下达）＋意图清单</summary>
        private static void ApplyStanceMultipliers(StrategistState state)
        {
            state.Intents.Clear();
            switch (state.Stance)
            {
                case AIStance.Develop:
                    state.armyMul = 0.8f; state.contractMul = 0.9f; state.upgradeMul = 1.2f; state.techMul = 1.0f;
                    state.Intents.Add("攒资源×1.2"); state.Intents.Add("保核心×1.1");
                    break;
                case AIStance.Stalemate:
                    state.armyMul = 1.0f; state.contractMul = 1.0f; state.upgradeMul = 1.1f; state.techMul = 1.0f;
                    state.Intents.Add("升命投资×1.1"); state.Intents.Add("保核心×1.1");
                    break;
                case AIStance.Push:
                    state.armyMul = 1.3f; state.contractMul = 0.8f; state.upgradeMul = 0.7f; state.techMul = 0.8f;
                    state.Intents.Add("集火推进×1.2"); state.Intents.Add("清除入侵者×1.2");
                    break;
                case AIStance.Defend:
                    state.armyMul = 0.7f; state.contractMul = 1.2f; state.upgradeMul = 1.0f; state.techMul = 0.9f;
                    state.Intents.Add("保核心×1.4"); state.Intents.Add("救援伤员×1.2");
                    break;
                case AIStance.Endgame:
                    state.armyMul = 1.6f; state.contractMul = 0.4f; state.upgradeMul = 0.5f; state.techMul = 0.3f;
                    state.Intents.Add("全员转火×1.5");
                    break;
            }
        }

        /// <summary>经济红线（§5.3 三条）：摩拉&lt;50 升命腰斩（保部署）；体力&lt;15 进攻腰斩（防打空）</summary>
        private static void ApplyRedLines(StrategistState state)
        {
            if (state.Mora < MoraRedLine) state.upgradeMul *= 0.5f;
            if (state.Stamina < StaminaRedLine) state.armyMul *= 0.5f;
        }
    }
}
