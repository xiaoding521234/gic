using UnityEngine;
using GIC.Framework;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Data
{


    /// <summary>
    /// 元素 & 势力视觉配置（ScriptableObject）
    /// 图标 Sprite（Stroke/Deep）、元素颜色等；后续更多元素/势力类配置也收纳于此。
    /// 通过 ConfigManager 加载；静态入口 Instance/GetElementColor 供非注入上下文调用（StarVisualConfig 同款模式）。
    /// </summary>
    [CreateAssetMenu(fileName = "ElementFactionConfig", menuName = "Game/ElementFactionConfig")]
    public class ElementFactionConfig : ScriptableObject
    {
        [Header("元素图标 - Stroke 风格（卡片等场景）")]
        public Sprite pyroStroke;
        public Sprite hydroStroke;
        public Sprite anemoStroke;
        public Sprite electroStroke;
        public Sprite cryoStroke;
        public Sprite dendroStroke;
        public Sprite geoStroke;
        public Sprite physicalStroke;
        public Sprite lightStroke;

        [Header("元素图标 - Deep 风格（祈愿等场景）")]
        public Sprite pyroDeep;
        public Sprite hydroDeep;
        public Sprite anemoDeep;
        public Sprite electroDeep;
        public Sprite cryoDeep;
        public Sprite dendroDeep;
        public Sprite geoDeep;
        public Sprite physicalDeep;
        public Sprite lightDeep;

        [Header("元素颜色（原 ElementColor.cs 配置化，2026-09-10 迁入）")]
        public Color pyroColor     = new Color(0.937f, 0.325f, 0.314f); // 火红
        public Color hydroColor    = new Color(0.314f, 0.635f, 0.937f); // 水蓝
        public Color anemoColor    = new Color(0.314f, 0.784f, 0.690f); // 风青
        public Color electroColor  = new Color(0.655f, 0.310f, 0.839f); // 雷紫
        public Color cryoColor     = new Color(0.557f, 0.812f, 0.902f); // 冰浅蓝
        public Color dendroColor   = new Color(0.408f, 0.698f, 0.149f); // 草绿
        public Color geoColor      = new Color(0.906f, 0.725f, 0.298f); // 岩金
        public Color physicalColor = new Color(0.729f, 0.729f, 0.729f); // 灰白
        public Color lightColor    = new Color(0.976f, 0.925f, 0.612f); // 光淡金

        [Header("势力图标")]
        public Sprite celestia;
        public Sprite nodkrai;
        public Sprite mondstadt;
        public Sprite liyue;
        public Sprite inazuma;
        public Sprite sumeru;
        public Sprite fontaine;
        public Sprite natlan;
        public Sprite snezhnaya;
        public Sprite khaenriah;
        public Sprite special;

        // ── 单例（由 ConfigManager.Init [PostConstruct] 注入；未注入时按同路径 Resources 兜底，防异常时序/编辑器工具 NRE）──

        private static ElementFactionConfig _instance;
        private static bool _instanceLoadAttempted;

        public static ElementFactionConfig Instance
        {
            get
            {
                if (_instance == null && !_instanceLoadAttempted)
                {
                    _instanceLoadAttempted = true;
                    _instance = Resources.Load<ElementFactionConfig>("Configs/ElementFactionConfig");
                    if (_instance == null)
                        Debug.LogWarning("[ElementFactionConfig] 未注入且 Resources 无此资产，元素视觉配置不可用");
                }
                return _instance;
            }
        }

        /// <summary>由 ConfigManager.Start() 调用注入</summary>
        public static void Initialize(ElementFactionConfig config) => _instance = config;

        // ── 查询方法 ──

        public Sprite GetElementIconStroke(ElementType type)
        {
            return type switch
            {
                ElementType.Pyro     => pyroStroke,
                ElementType.Hydro    => hydroStroke,
                ElementType.Anemo    => anemoStroke,
                ElementType.Electro  => electroStroke,
                ElementType.Cryo     => cryoStroke,
                ElementType.Dendro   => dendroStroke,
                ElementType.Geo      => geoStroke,
                ElementType.Physical => physicalStroke,
                ElementType.Light    => lightStroke,
                _                    => physicalStroke,
            };
        }

        public Sprite GetElementIconDeep(ElementType type)
        {
            return type switch
            {
                ElementType.Pyro     => pyroDeep,
                ElementType.Hydro    => hydroDeep,
                ElementType.Anemo    => anemoDeep,
                ElementType.Electro  => electroDeep,
                ElementType.Cryo     => cryoDeep,
                ElementType.Dendro   => dendroDeep,
                ElementType.Geo      => geoDeep,
                ElementType.Physical => physicalDeep,
                ElementType.Light    => lightDeep,
                _                    => physicalDeep,
            };
        }

        public Color GetElementColor(ElementType type)
        {
            return type switch
            {
                ElementType.Pyro     => pyroColor,
                ElementType.Hydro    => hydroColor,
                ElementType.Anemo    => anemoColor,
                ElementType.Electro  => electroColor,
                ElementType.Cryo     => cryoColor,
                ElementType.Dendro   => dendroColor,
                ElementType.Geo      => geoColor,
                ElementType.Physical => physicalColor,
                ElementType.Light    => lightColor,
                _                    => physicalColor,
            };
        }

        public Sprite GetFactionIcon(FactionType faction)
        {
            return faction switch
            {
                FactionType.Celestia   => celestia,
                FactionType.Nodkrai    => nodkrai,
                FactionType.Mondstadt  => mondstadt,
                FactionType.Liyue      => liyue,
                FactionType.Inazuma    => inazuma,
                FactionType.Sumeru     => sumeru,
                FactionType.Fontaine   => fontaine,
                FactionType.Natlan     => natlan,
                FactionType.Snezhnaya  => snezhnaya,
                FactionType.Khaenriah  => khaenriah,
                FactionType.Special    => special,
                _                      => special,
            };
        }
    }


}
