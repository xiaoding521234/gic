using System.Collections.Generic;
using UnityEngine;

namespace GIC.Data
{
    /// <summary>
    /// 战斗视觉统一配色（2026-09-18 统一化批次建立，收口前配色字面量散落 BattleHud/BattlePlayer/UnitView 三文件且同语义不同值）。
    /// 队伍色（底座/队列框/accent）与血条色分列——同单位「阵营红底盘 + 血红条」是两套语义非漂移；
    /// 改战斗观感基调一律改本资产，勿在代码里写新字面量（docs/14 §63）。
    /// 通过 ConfigManager 加载；静态入口 Instance 供非注入上下文调用（ElementFactionConfig 同款模式）。
    /// </summary>
    [CreateAssetMenu(fileName = "BattlePalette", menuName = "Game/BattlePalette")]
    public class BattlePalette : ScriptableObject
    {
        [Header("队伍主色（立牌底座 / HUD 队列框 / 信息块 accent 同源）")]
        public Color 我方主色 = new Color(0.25f, 0.55f, 1f);
        public Color 敌方主色 = new Color(1f, 0.35f, 0.3f);

        [Header("玩家配色（2026-09-29 执行预览拍板：头像描环按「归属玩家」染色——本端视角我=蓝、我队"
                + "队友=绿、敌队玩家按序=红/紫…；数组式可扩 3v3。只新预览消费，队伍色口径（立牌底座等）不动）")]
        public List<Color> 我方玩家色 = new List<Color>
        {
            new Color(0.25f, 0.55f, 1f),   // 我自己
            new Color(0.35f, 0.78f, 0.42f), // 我队队友（2v2）
        };
        public List<Color> 敌方玩家色 = new List<Color>
        {
            new Color(1f, 0.35f, 0.3f),    // 敌队第 1 玩家
            new Color(0.72f, 0.48f, 0.95f), // 敌队第 2 玩家（2v2）
        };

        [Header("头顶血条（血量语义，与队伍色分离）")]
        public Color 血条我方绿 = new Color(0.31f, 0.83f, 0.42f);
        public Color 血条敌方红 = new Color(0.78f, 0.36f, 0.31f);
        public Color 血条底 = new Color(0.07f, 0.06f, 0.05f, 0.9f);

        [Header("反馈数字")]
        public Color 伤害红 = new Color(1f, 0.3f, 0.25f);
        [Tooltip("治疗数字色（2026-10-01 网检对齐原神：治疗数字=黄绿 #BCFF37，非纯绿）")]
        public Color 治疗绿 = new Color(0.737f, 1f, 0.216f);
        [Tooltip("反应伤害数字色——蒸发（2026-10-01 二次拍板「原神里反应都有自己的颜色」：每反应独立色勿共用——基准=官方反应图标双色调实测〔biligame 官方 wiki，webrefs/damage-number-colors〕：蒸发图标=火橙#CA6841+水青#51CAF1；双色调直混落紫灰带且与雷淡紫撞色，故按母元素冷暖拆分=亮品红/蒸汽粉 #FF66D9）")]
        public Color 反应蒸发色 = new Color(1f, 0.40f, 0.851f);
        [Tooltip("反应伤害数字色——融化（图标=火橙#CB6A43+冰青#448E99 双色调；与蒸发拆冷暖=珊瑚红/融灼暖红 #FF6A5A——火融冰意象，与火数字橙 #FF9B00 可区分）")]
        public Color 反应融化色 = new Color(1f, 0.416f, 0.353f);
        [Tooltip("反应伤害数字色——冻结（冰晶蓝 #5A8CFF——冻结图标=冰蓝#4BABC4+深蓝#2957AD，取比水数字更深的蓝以区分；冻结数字带名+带反应色=2026-10-01 三次拍板「反应名都应该加上」）")]
        public Color 反应冻结色 = new Color(0.353f, 0.549f, 1f);
        [Tooltip("原神式伤害数字（BattleDamageNumbers）的黑描边色")]
        public Color 伤害数字描边色 = new Color(0.04f, 0.03f, 0.03f, 0.85f);
        [Tooltip("头顶条元能填充色（BattleOverheadBars 元能条，2026-09-24；09-25 目检拍板改白色）")]
        public Color 元能条色 = new Color(0.95f, 0.95f, 0.95f);

        [Header("伤害数字元素色（2026-10-01 网检对齐原神：数字=亮彩霓虹风、显著亮于元素主题色——图标用 ElementFactionConfig、箭矢用下方「箭矢元素色」段〔2026-10-04 起分家〕勿混——白=物理/橙=火/青=水/冰青=冰/淡紫=雷/薄荷=风/淡金=岩/黄绿=草；基准=社区原神复刻 Baity mod 实测色系）")]
        [Tooltip("物理伤害数字=纯白 #FFFFFF（原神物理数字白）")]
        public Color 伤害数字物理色 = new Color(1f, 1f, 1f);
        [Tooltip("火伤害数字=橙 #FF9B00（原神火数字是橙而非红——元素主题火红勿用于数字）")]
        public Color 伤害数字火色 = new Color(1f, 0.608f, 0f);
        [Tooltip("水伤害数字=亮青蓝 #33CCFF")]
        public Color 伤害数字水色 = new Color(0.2f, 0.8f, 1f);
        [Tooltip("冰伤害数字=冰青白 #99FFFF")]
        public Color 伤害数字冰色 = new Color(0.6f, 1f, 1f);
        [Tooltip("雷伤害数字=淡紫粉 #E19BFF")]
        public Color 伤害数字雷色 = new Color(0.882f, 0.608f, 1f);
        [Tooltip("风伤害数字=薄荷青 #66FFCC")]
        public Color 伤害数字风色 = new Color(0.4f, 1f, 0.8f);
        [Tooltip("岩伤害数字=淡金 #FFCC66")]
        public Color 伤害数字岩色 = new Color(1f, 0.8f, 0.4f);
        [Tooltip("草伤害数字=黄绿 #BAFF37")]
        public Color 伤害数字草色 = new Color(0.729f, 1f, 0.216f);
        [Tooltip("光伤害数字（GIC 自定——原神无光元素，取淡暖金白）")]
        public Color 伤害数字光色 = new Color(1f, 0.95f, 0.72f);

        [Header("箭矢元素色（2026-10-04 拍板「箭矢的颜色应当为红色」——箭矢表现色与元素主题色分家：")]
        [Header("主题火红 #EF5350 染白箭观感偏粉不被读作红（主题色≠表现色二次实证，色板结构同「伤害数字元素色」先例）；")]
        [Header("消费=BattlePlayer.ResolveArrowTint 单源（命中箭/消散箭/箭雨落箭三路）；未列元素回落主题色")]
        [Tooltip("物理箭矢=主题灰白同值")]
        public Color 箭矢物理色 = new Color(0.729f, 0.729f, 0.729f);
        [Tooltip("火箭矢=饱和正红 #F23829（2026-10-04 拍板：安柏箭矢应为红色）")]
        public Color 箭矢火色 = new Color(0.949f, 0.22f, 0.16f);
        [Tooltip("水箭矢=主题亮青蓝同值")]
        public Color 箭矢水色 = new Color(0.314f, 0.635f, 0.937f);
        [Tooltip("冰箭矢=主题冰青同值")]
        public Color 箭矢冰色 = new Color(0.557f, 0.812f, 0.902f);
        [Tooltip("雷箭矢=主题淡紫同值")]
        public Color 箭矢雷色 = new Color(0.655f, 0.31f, 0.839f);
        [Tooltip("风箭矢=主题薄荷同值")]
        public Color 箭矢风色 = new Color(0.314f, 0.784f, 0.69f);
        [Tooltip("岩箭矢=主题淡金同值")]
        public Color 箭矢岩色 = new Color(0.906f, 0.725f, 0.298f);
        [Tooltip("草箭矢=主题黄绿同值")]
        public Color 箭矢草色 = new Color(0.408f, 0.698f, 0.149f);
        [Tooltip("光箭矢=主题淡暖金白同值")]
        public Color 箭矢光色 = new Color(0.976f, 0.925f, 0.612f);

        [Header("投射物（箭矢素材缺失时的白色光条占位兜底——正式箭矢已落地 2026-09-28，运行时元素色染色不走此色）")]
        public Color 箭矢占位色 = new Color(0.98f, 0.93f, 0.80f, 1f);

        [Header("HUD 基调")]
        public Color 文字米白 = new Color(0.93f, 0.89f, 0.82f);
        public Color 暖金 = new Color(0.83f, 0.74f, 0.56f);
        public Color 高亮金 = new Color(0.83f, 0.74f, 0.56f, 0.55f);
        public Color 按钮底盘 = new Color(0.10f, 0.09f, 0.07f, 0.92f);

        [Header("瞄准高亮（青芯+内嵌黑边色块：可选且推荐=原神 Hydro 系青蓝 / 可选但不推荐=红；不可选=无提示。2026-09-23 分色拍板；09-24 视觉定稿=青蓝（白/金两试色已废）+每格向内黑边")]
        public Color 瞄准推荐色 = new Color(0.30f, 0.76f, 0.95f, 0.8f);
        public Color 瞄准不推荐色 = new Color(1f, 0.3f, 0.25f, 0.8f);
        [Tooltip("瞄准待定金格（2026-09-26 拍板：点可选格不立即提交——变金待定、完成选择按钮确认；原神风格金色）")]
        public Color 瞄准已选色 = new Color(0.96f, 0.79f, 0.27f, 0.85f);

        [Header("立牌状态")]
        public Color 冻结冰色 = new Color(0.62f, 0.83f, 0.96f);
        public Color 立牌尸体灰 = new Color(0.45f, 0.45f, 0.45f, 0.9f);
        public Color 受击闪红 = new Color(1f, 0.35f, 0.3f);
        public Color 名字尸体灰 = new Color(0.5f, 0.5f, 0.5f, 0.85f);

        private static BattlePalette _instance;
        private static bool _instanceLoadAttempted;

        public static BattlePalette Instance
        {
            get
            {
                if (_instance == null && !_instanceLoadAttempted)
                {
                    _instanceLoadAttempted = true;
                    _instance = Resources.Load<BattlePalette>("Configs/BattlePalette");
                    if (_instance == null)
                        Debug.LogWarning("[BattlePalette] 未注入且 Resources 无此资产，战斗配色不可用");
                }
                return _instance;
            }
        }

        /// <summary>由 ConfigManager.Start() 调用注入</summary>
        public static void Initialize(BattlePalette config) => _instance = config;
    }
}
