using UnityEngine;
namespace GIC.Data
{


    /// <summary>
    /// 资源点种类（docs/47，2026-10-27 拍板「非地形非单位不占体积」——独立数据实体，
    /// 不扩 TileType、不进体积/阻挡判定；储量/产出参数走 BattleMetrics 常量族，
    /// 种类多到需要差异化参数时再升 SO 配置）
    /// </summary>
    public enum ResourceNodeKind
    {
        [InspectorName("无")]
        None = 0,

        [InspectorName("摩拉堆")]
        MoraPile = 1, // 可采 ResourceNodeMetrics.MoraPileMaxCharges 次、每次 MoraPerHarvest 摩拉；耗尽从地图消失（SC2 矿脉同构）
    }

    /// <summary>
    /// 资源点常量族（docs/47，2026-10-27 拍板 10 次×5 摩拉）——归 Data 层（消费方含
    /// BattleMapConfig.BuildData 解析；Battle 层正向引用合法，勿倒挂 BattleMetrics 造成
    /// Data→Battle 反向依赖）。资源点种类多到需要差异化参数时再升 SO 配置。
    /// </summary>
    public static class ResourceNodeMetrics
    {
        /// <summary>摩拉堆可采次数（charges 模型——SC2 矿脉同构：逐次扣减、归零耗尽从地图消失）</summary>
        public const int MoraPileMaxCharges = 10;

        /// <summary>每次采集获得的摩拉（站立采集与安柏途经采集同产——资源点定义收益恒出；
        /// 安柏技能额外产出体力/元能走技能参数 CollectStaminaGain/CollectEnergyGain）</summary>
        public const int MoraPerHarvest = 5;

        /// <summary>普通读条采集默认时间（秒；资源点可按类型/实例覆写）</summary>
        public const int DefaultHarvestSeconds = 3;

        /// <summary>普通读条采集的默认触发半径（cm）；移动技能可用 CollectRadius 独立覆写快速采集半径</summary>
        public const int HarvestRadiusCm = 50;

        /// <summary>旧移动技能缺少 CollectCooldown 参数时的兼容回落（秒）；现役技能应在资产中显式配置</summary>
        public const int DefaultQuickHarvestCooldownSeconds = 6;
    }
}
