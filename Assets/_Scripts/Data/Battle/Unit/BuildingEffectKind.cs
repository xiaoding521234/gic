using UnityEngine;
namespace GIC.Data
{


    /// <summary>
    /// 建筑效果类型（2026-10-21 决策八十六，docs/45 §6）：
    /// 建筑无技能、无命座，效果=UnitData 建筑段字段直白承载（枚举+参数），效果拍（每 2 秒）由
    /// BuildingEffectsSystem 统一结算（多座同种建筑独立结算）。未来建筑种类增长再评估原子化。
    /// </summary>
    public enum BuildingEffectKind
    {
        [InspectorName("无效果")]
        None = 0,

        [InspectorName("元能旗帜（每 2 秒为半径内我方非建筑单位+元能）")]
        EnergyAura = 1,

        [InspectorName("摩拉收入（每 2 秒己方玩家+摩拉）")]
        MoraIncome = 2,

        [InspectorName("周期召唤（部署即召1个+每过周期秒再召）")]
        PeriodicSummon = 3,

        [InspectorName("治疗伤员（每 2 秒治疗生命值低于阈值的我方存活单位）")]
        HealWounded = 4,

        [InspectorName("元能引擎召唤（初始元能+每 2 秒自充+过阈即召消耗）")]
        EnergySummon = 5,
    }
}
