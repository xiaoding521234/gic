using UnityEngine;

namespace GIC.Data
{
    /// <summary>
    /// AI 调参资产（2026-10-20 审查批#4 资产化——buff 常量漏检教训的双胞胎收口）：
    /// 三脑（伙伴脑/配额脑/战略层）与对手档案的全部平衡评分常量从代码 const 迁入本资产，
    /// **调 AI 平衡=改资产 Inspector 零代码零编译**（规范真源=docs/20 §5.1「平衡数值=资产」）。
    /// 各脑代码内的 public/private const 已转为只读属性别名（访问语法不变），数值真源全部在此。
    /// 乘法层维持既有形态：人格乘子=PersonaPack（逐人格资产）、难度门=AIPlayerConfig——本资产
    /// 装**基线**，人格/难度在其上乘/门控。确定性：读资产零随机，同资产同战局。
    /// 缺资产兜底：Resources 缺载时运行时自建默认实例（代码初始值）——Host 演算永不因配置缺失
    /// 不可用（与视觉类配置 warn-null 模式不同，本件是演算关键依赖）。
    /// </summary>
    [CreateAssetMenu(fileName = "AITuning", menuName = "Game/AITuning AI调参")]
    public class AITuningConfig : ScriptableObject
    {
        // ==================== 伙伴脑·攻击与获能（CompanionBrain） ====================

        [Header("伙伴脑·攻击与获能")]
        [Tooltip("斩杀加分：预估伤害≥目标当前生命时（保守估值未计反应乘区）")]
        public int 击杀斩杀加成分 = 80;
        [Tooltip("战技命中获能 +10 的评分（向爆发攒能的行动价值）")]
        public int 战技命中获能分 = 12;
        [Tooltip("治疗候选门槛：有效治疗≥治疗量一半才值得占行动")]
        public int 治疗门槛百分比 = 50;

        [Header("伙伴脑·移动评分")]
        [Tooltip("移动兜底分（低于一切攻击/有效治疗候选）")]
        public int 移动兜底分 = 10;
        [Tooltip("移动每逼近 1 格的加分")]
        public int 移动每格逼近分 = 2;
        [Tooltip("近敌接敌加分上限（离最近敌越近越优先动该单位）")]
        public int 接敌加分上限 = 8;
        [Tooltip("走位进射击线加分（移动落点即有可开火攻击线）")]
        public int 射线对齐分 = 8;

        [Header("伙伴脑·爆发估值")]
        [Tooltip("单位指向型爆发：复苏候选评分（价值对齐斩杀档之上——有尸体=复苏即最优）")]
        public int 复苏候选分 = 100;
        [Tooltip("单位指向型爆发：增益候选评分（歌声之环档——低于斩杀、高于常规攻击均值）")]
        public int 单位指向增益分 = 45;
        [Tooltip("无目标自施放爆发：自身增益候选评分（寒冰之棱档——攒满即放勿囤积）")]
        public int 自施放增益分 = 45;
        [Tooltip("自施放爆发语境评分：光环半径内每个存活敌的加分（贴敌群施放优先）")]
        public int 自施放每敌分 = 15;
        [Tooltip("J-1 冻结协同分：反应预览命中冻结的 flat 加分（低于斩杀、高于集火每档）")]
        public int 冻结控制分 = 25;
        [Tooltip("随军追逐兜底分（压常规单体技能先归队、低于群奶/救命奶与就位托底）")]
        public int 追赶托底分 = 60;

        [Header("伙伴脑·威胁响应（E-1/E-2）")]
        [Tooltip("反威胁分上限：攻击候选是敌方威胁源时的加分上限（低于斩杀）")]
        public int 反威胁分上限 = 25;
        [Tooltip("落点受威胁扣分：候选落点在敌方预测火线格内=下回合白挨打（扣后钳 1 保底 Offer）")]
        public int 火线格惩罚 = 12;
        [Tooltip("集火每档加分：行为档案「集火权重」1 档的分值（只影响同分域内目标选择）")]
        public int 集火每名次分 = 3;

        [Header("伙伴脑·支援与先锋（F/G/H 批）")]
        [Tooltip("支援自保危险半径：落点距最近敌小于此值=站敌人刀口（自保负分生效阈值）")]
        public int 支援危险半径 = 2;
        [Tooltip("光环敌覆盖每敌加分（乘档案「光环贴敌权重」与形态因子）")]
        public int 光环贴敌每敌分 = 6;
        [Tooltip("支援形态切换血线门：hp% 低于此值=保险形态（配合近敌判定 W1 返修）")]
        public int 保险血线百分比 = 60;
        [Tooltip("保险形态近敌半径：最近敌≤此值=有近敌（保险候选），否则=安全区随军")]
        public int 保险近敌半径 = 5;
        [Tooltip("支援形态预警门：威胁图预期承伤≥hp×此百分比=提前一回合后撤")]
        public int 保险预警承伤百分比 = 50;
        [Tooltip("保险形态自保倍率：自保负分×此值=后撤位自然胜出")]
        public int 保险自保倍率 = 3;
        [Tooltip("光环前瞻基准距离：距最近敌此距离处=0 分起点（验证局调参 8→14 覆盖对角开局）")]
        public int 光环前瞻基准距离 = 14;
        [Tooltip("光环前瞻每步分：每近敌 1 格的加分（对冲火线扣分的趋敌梯度）")]
        public int 光环前瞻每步分 = 3;
        [Tooltip("光环前瞻上限（enemiesInAura>0 时不再叠加前瞻防双计）")]
        public int 光环前瞻上限 = 12;
        [Tooltip("支援站位火线扣分减半系数（支援型接受中等风险换贴敌挂水；配额脑原路径全额不动）")]
        public float 支援火线扣分系数 = 0.5f;
        [Tooltip("保险形态后撤梯度：远离最近敌每格加分（给她方向性撤到阵内/射程外）")]
        public int 保险后撤每格分 = 2;
        [Tooltip("保险形态后撤梯度上限")]
        public int 保险后撤分上限 = 12;
        [Tooltip("先锋交战判定距离：锚单位距最近敌≤此值=交战态（对齐近战交换距离）")]
        public int 先锋交战距离 = 2;
        [Tooltip("交战就位优先 Offer 托底分（恒压常规技能、低于斩杀/救命/复苏档）")]
        public int 就位接管托底分 = 95;
        [Tooltip("伴随语境光环敌分倍率：视野敌群覆盖主导选格（非伴随调用恒 ×1）")]
        public int 伴随光环每敌倍率 = 2;
        [Tooltip("支援自保基准扣分（乘档案「自保权重」——量级压过移动兜底+走位线分）")]
        public int 自保基准扣分 = 15;
        [Tooltip("支援奶程内站位基础分（奶程内站位优先于平打）")]
        public int 奶程内站位分 = 20;
        [Tooltip("支援奶程外每格衰减（约 3 格外归 0 的连续梯度非硬门槛）")]
        public int 奶程外每格衰减 = 6;
        [Tooltip("救命分：治疗覆盖 DoomedAllies（将被集火致死）单位时的加分——斩杀档的对偶")]
        public int 救命分 = 30;

        // ==================== 配额脑（PlayerQuotaBrain，AI 玩家配额行动评分） ====================

        [Header("配额脑·号令与升命")]
        [Tooltip("延奏增益行动分（号令延奏的基础估值档）")]
        public int 延奏增益行动分 = 15;
        [Tooltip("延奏变奏链分（TriggerSkill 链价值）")]
        public int 延奏变奏链分 = 12;
        [Tooltip("延奏传输分（非蒙德 +20 元能双倍档的增益部分）")]
        public int 延奏传输分 = 4;
        [Tooltip("延奏满层续航分（死水修复：目标该 buff 已满层且快到期时的续 buff 价值——低于推进/进攻候选，2026-10-20 决策八十四）")]
        public int 延奏满层续航分 = 10;
        [Tooltip("延奏续航临近到期回合（剩余回合≤此值才算「快到期」值得续航；更长=时长充足零分放权，2026-10-20 决策八十四）")]
        public int 延奏续航临近到期回合 = 2;
        [Tooltip("升命投资分（高于延奏增益、低于斩杀/救命——投资档定位，AI-1 批 25→50）")]
        public int 升命投资分 = 50;

        // ==================== 战略层（AIStrategist 五态机+经济红线） ====================

        [Header("战略层·态势机（滞回阈值——战力比=机动兵力出战费加总比）")]
        [Tooltip("压制进入阈值（战力比≥此值转入压制）")]
        public float 压制进入战力比 = 1.3f;
        [Tooltip("压制保持阈值（滞回防抖档）")]
        public float 压制保持战力比 = 1.15f;
        [Tooltip("守势进入阈值（战力比≤此值转入守势）")]
        public float 守势进入战力比 = 0.7f;
        [Tooltip("守势保持阈值（滞回防抖档）")]
        public float 守势保持战力比 = 0.85f;
        [Tooltip("态势最小驻留回合（仅发育出口受驻留约束）")]
        public int 态势最小驻留回合 = 3;
        [Tooltip("发育期回合数（≤此回合恒发育态）")]
        public int 发育期回合数 = 3;
        [Tooltip("终局判据：敌核心血量比低于此值")]
        public float 终局核心血量比 = 0.15f;
        [Tooltip("回防激进度乘数（守势+核心受创/血线低=伙伴脑斩杀加成×此值）")]
        public float 回防激进度乘数 = 0.7f;

        [Header("战略层·经济红线（乘入态势乘子）")]
        [Tooltip("摩拉红线：低于此值升命分×0.5")]
        public int 摩拉红线 = 50;
        [Tooltip("体力红线：低于此值军备分×0.5")]
        public int 体力红线 = 15;

        // ==================== 对手档案（OpponentProfiler 遗忘策略） ====================

        [Header("对手档案·遗忘策略")]
        [Tooltip("习惯计数每回合衰减系数（半衰期≈7 回合，与样本环「近 8 回合加倍」同节奏）")]
        public float 习惯衰减每回合 = 0.9f;
        [Tooltip("习惯计数剪枝阈值（低于即移除，防长期对局长尾膨胀）")]
        public float 习惯剪枝阈值 = 0.05f;

        // ==================== AI 强化批（2026-10-25 凌晨批：无敌推进/锚滞回/开局原型/难度档） ====================

        [Header("AI 强化批·推进与锚定")]
        [Tooltip("无敌环境推进分（2026-10-25 凌晨批议题①，docs/11 决策八十四挂账）：感知无敌（无可感知非建筑敌）"
                 + "且移动锚为敌方建筑（核心）时的移动加分——低于一切战斗候选（攻击/治疗）、高于裸移动兜底与延奏续航档，"
                 + "治「号令挤掉 J-4 推核心」（决策八十四根因④：配额脑延奏续航 10 恒赢裸推进 10）")]
        public int 无敌环境推进分 = 8;
        [Tooltip("锚点滞回门槛（格）（凌晨批议题③，docs/11 hysteresis 挂账）：E-3 移动锚延续/眷属核心锚定的换锚距离门槛"
                 + "——新锚须比现锚严格近此格数才接管（TPS CurrentHidepoint 惯性复核+ItB 反重复负权的门槛化表达）；"
                 + "现锚每回合廉价复核（存活+可感知）通过即延续")]
        public int 锚点滞回门槛 = 2;

        [Header("AI 强化批·开局与难度")]
        [Tooltip("开局策略窗口回合（凌晨批议题④）：≤此回合部署桶序走 per-局开局原型（seed 派生确定性抽选），"
                 + "窗口后回归态势桶序（议题⑦）；ZK 开局策略原型加权抽选先例")]
        public int 开局策略窗口回合 = 4;
        [Tooltip("劲敌次优分差比（凌晨批议题⑥，0~1）：劲敌档次优注入——评分榜首与次名分差占比小于此值时取次名"
                 + "（GameAIPro ch04 hustling 三招之一「次优选择仅在分差<20% 内启用」；确定性零 roll）")]
        public float 劲敌次优分差比 = 0.2f;
        [Tooltip("天理摩拉收入百分比（凌晨批议题⑥，100=无作弊）：天理档=经济作弊型（SC2 CheatMoney/petra 经济倍率表先例）"
                 + "——回合末摩拉发放+建筑摩拉收入两消费点乘此倍率（仅对天理 AI 生效；其余档恒 100）")]
        public int 天理摩拉收入百分比 = 130;

        // ==================== 资源点采集档（docs/47 拍板⑦，2026-10-27） ====================

        [Header("AI 强化批·资源点采集")]
        [Tooltip("采集意图基准分（docs/47 拍板⑦「有特别采集能力或有采集tag的角色，相比于攻击敌人，更主动采集资源点」）："
                 + "采集型角色（IsHarvester——安柏）朝最近可见资源点移动的候选基准分——设计值稳压攻击档典型分"
                 + "（战技命中估值+斩杀加成族）；**不乘人格移动优先权重**（拍板硬语义>人格个性化，人格不得把采集"
                 + "积极性压没）；保命档（Rescue/复苏）恒在采集之上（档位分层不互斥）")]
        public int 采集意图基准分 = 200;
        [Tooltip("采集每格衰减分（docs/47）：采集候选分=基准分−距离×本值——距离越远采集优先度越低，"
                 + "衰减至攻击典型分之下自然回归战斗行为（「够得着才优先采集」的软边界；观察局标定入口）")]
        public int 采集每格衰减分 = 12;

        // ==================== AI 施放节流（决策九十五 P5-D——tick 时代无 CD 技能刷屏防线） ====================

        [Header("AI 强化批·tick 施放节流")]
        [Tooltip("AI 同单位同技能最短重施间隔 tick（20 tick=1 秒，默认 40=2 秒对齐效果拍）。tick 制无 CD 技能"
                 + "（延奏/变奏/治疗族）在 0.5s 决策节拍下会每拍施放：自我延奏刷分+治疗刷屏把伤害完全抵消="
                 + "无人死亡的死水僵局（P5-D 双 AI 观察局实锤）。三脑候选按「单位×技能槽」最近实际施放 tick"
                 + "判节流，间隔内不再产候选；攻击技能不受影响（战技 CD 60/爆发 CD 100 tick 恒大于本值）。"
                 + "0=关闭节流")]
        public int AI重施最短间隔tick = 40;

        // ==================== 访问单例 ====================

        private static AITuningConfig _instance;
        private static bool _loadAttempted;

        /// <summary>访问单例：ConfigManager 注入优先（Initialize），否则 Resources 兜底；
        /// 仍缺=运行时自建默认实例（代码初始值）——演算关键依赖永不因配置缺失不可用</summary>
        public static AITuningConfig Instance
        {
            get
            {
                if (_instance == null && !_loadAttempted)
                {
                    _loadAttempted = true;
                    _instance = Resources.Load<AITuningConfig>("Configs/AI/AITuning");
                    if (_instance == null)
                    {
                        _instance = CreateInstance<AITuningConfig>();
                        Debug.LogWarning("[AITuningConfig] 资产缺失（Resources/Configs/AI/AITuning）——以代码默认值运行");
                    }
                }
                return _instance;
            }
        }

        /// <summary>由 ConfigManager 调用注入（有资产时优先资产）</summary>
        public static void Initialize(AITuningConfig config) => _instance = config;
    }
}
