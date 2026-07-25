using UnityEngine;

/// <summary>
/// 七元素代表颜色，供 UI 系统统一调用。
/// 颜色参考原神七元素风格。
/// </summary>
public static class ElementColor
{
    public static readonly Color Physical   = new Color(0.729f, 0.729f, 0.729f); // 灰白
    public static readonly Color Pyro       = new Color(0.937f, 0.325f, 0.314f); // 火红
    public static readonly Color Hydro      = new Color(0.314f, 0.635f, 0.937f); // 水蓝
    public static readonly Color Anemo      = new Color(0.314f, 0.784f, 0.690f); // 风青
    public static readonly Color Electro    = new Color(0.655f, 0.310f, 0.839f); // 雷紫
    public static readonly Color Cryo       = new Color(0.557f, 0.812f, 0.902f); // 冰浅蓝
    public static readonly Color Dendro     = new Color(0.408f, 0.698f, 0.149f); // 草绿
    public static readonly Color Geo        = new Color(0.906f, 0.725f, 0.298f); // 岩金
    public static readonly Color Light      = new Color(0.976f, 0.925f, 0.612f); // 光淡金

    /// <summary>
    /// 根据元素类型获取代表色
    /// </summary>
    public static Color GetColor(ElementType elementType)
    {
        return elementType switch
        {
            ElementType.Physical => Physical,
            ElementType.Pyro     => Pyro,
            ElementType.Hydro    => Hydro,
            ElementType.Anemo    => Anemo,
            ElementType.Electro  => Electro,
            ElementType.Cryo     => Cryo,
            ElementType.Dendro   => Dendro,
            ElementType.Geo      => Geo,
            ElementType.Light    => Light,
            _                    => Physical,
        };
    }
}
