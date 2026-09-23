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

        [Header("头顶血条（血量语义，与队伍色分离）")]
        public Color 血条我方绿 = new Color(0.31f, 0.83f, 0.42f);
        public Color 血条敌方红 = new Color(0.78f, 0.36f, 0.31f);
        public Color 血条底 = new Color(0.07f, 0.06f, 0.05f, 0.9f);

        [Header("反馈数字")]
        public Color 伤害红 = new Color(1f, 0.3f, 0.25f);
        public Color 治疗绿 = new Color(0.3f, 0.95f, 0.45f);

        [Header("投射物（B5 表现批次换正式箭矢素材前的白色光条占位）")]
        public Color 箭矢占位色 = new Color(0.98f, 0.93f, 0.80f, 1f);

        [Header("HUD 基调")]
        public Color 文字米白 = new Color(0.93f, 0.89f, 0.82f);
        public Color 暖金 = new Color(0.83f, 0.74f, 0.56f);
        public Color 高亮金 = new Color(0.83f, 0.74f, 0.56f, 0.55f);
        public Color 按钮底盘 = new Color(0.10f, 0.09f, 0.07f, 0.92f);

        [Header("瞄准高亮（青芯+内嵌黑边色块：可选且推荐=原神 Hydro 系青蓝 / 可选但不推荐=红；不可选=无提示。2026-09-23 分色拍板；09-24 视觉定稿=青蓝（白/金两试色已废）+每格向内黑边")]
        public Color 瞄准推荐色 = new Color(0.30f, 0.76f, 0.95f, 0.8f);
        public Color 瞄准不推荐色 = new Color(1f, 0.3f, 0.25f, 0.8f);

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
