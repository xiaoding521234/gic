using UnityEngine;
namespace GIC.Data
{


    /// <summary>
    /// 地块视觉配置（2026-09-27 地形批次）：各地面类型的顶面材质变体（每格确定性随机选一防整片重复）
    /// + 草簇装饰参数（立牌式面向玩家、随机铺满草地格）。
    /// 顶面=地块 prefab 材质槽 1；变体未配置时回退 prefab 自带顶面材质，草簇未配置时不铺。
    /// </summary>
    [CreateAssetMenu(fileName = "TileVisualsConfig", menuName = "Game/TileVisualsConfig")]
    public class TileVisualsConfig : ScriptableObject
    {
        [Header("顶面材质变体（每格确定性随机选一；空=用地块 prefab 自带顶面材质）")]
        [Tooltip("草地/平原/沙地/雪地/冰面共用草地顶面（与地块 prefab 映射同口径）")]
        public Material[] 草地顶面变体;

        [Tooltip("土地顶面（蒙德土地）")]
        public Material[] 土地顶面变体;

        [Tooltip("石砖顶面（石路）")]
        public Material[] 石砖顶面变体;

        [Tooltip("圆石顶面")]
        public Material[] 圆石顶面变体;

        [Header("草簇装饰（立牌式面向玩家，随机铺满草地格；材质=GIC/Battle/GrassSway 顶点摆动）")]
        [Tooltip("草簇材质变体（各挂不同草簇贴图；空=不铺草簇）")]
        public Material[] 草簇材质变体;

        [Tooltip("每格草簇数范围（含两端，随机取整）")]
        public Vector2Int 每格草簇数 = new Vector2Int(3, 5);

        [Tooltip("草簇高度范围（世界单位=格）")]
        public Vector2 草簇高度范围 = new Vector2(0.18f, 0.30f);

        [Tooltip("草簇后倾角（与立牌同款：顶部远离相机后仰，倾角=相机俯角时面正对视线）")]
        [Range(0f, 80f)] public float 草簇后倾角 = 55f;

        [Tooltip("草簇朝向随机抖动（度，绕竖直轴；0=全部正对相机）")]
        [Range(0f, 90f)] public float 草簇朝向抖动 = 12f;

        [Tooltip("草簇在格内的边缘留白（0.1=距格边 0.1 格）")]
        [Range(0f, 0.45f)] public float 草簇格内留白 = 0.10f;

        /// <summary>
        /// 地形类型 → 顶面材质变体（null=该类型未配置变体，回退 prefab 自带材质）
        /// </summary>
        public Material[] GetTopVariants(TileType tileType)
        {
            switch (tileType)
            {
                case TileType.Grassland:
                case TileType.Plain:
                case TileType.Sand:
                case TileType.Snow:
                case TileType.Ice:
                    return 草地顶面变体;
                case TileType.Dirt:
                    return 土地顶面变体;
                case TileType.StonePath:
                    return 石砖顶面变体;
                case TileType.Cobblestone:
                    return 圆石顶面变体;
                default:
                    return null;
            }
        }
    }
}
