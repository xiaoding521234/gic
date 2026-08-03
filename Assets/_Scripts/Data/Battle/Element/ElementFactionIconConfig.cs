using UnityEngine;
using GIC.Framework;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Data
{


    /// <summary>
    /// 元素 & 势力图标配置（ScriptableObject）
    /// 拖入对应 Sprite 后，通过 ConfigManager 加载并通过静态入口访问。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/ElementFactionIconConfig")]
    public class ElementFactionIconConfig : ScriptableObject
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

        // ---- 查询方法 ----

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

