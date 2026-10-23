using UnityEngine;
namespace GIC.Data
{


    /// <summary>
    /// 建筑回合末效果类型（2026-10-21 决策八十六，docs/45 §6）：
    /// 建筑无技能、无命座，效果=UnitData 建筑段字段直白承载（枚举+参数），回合末由 TurnResolver
    /// 建筑效果段统一结算（多座同种建筑独立结算）。未来建筑种类增长再评估原子化。
    /// </summary>
    public enum BuildingEffectKind
    {
        [InspectorName("无效果")]
        None = 0,

        [InspectorName("元能旗帜（回合末半径内我方非建筑单位+元能）")]
        EnergyAura = 1,

        [InspectorName("摩拉收入（回合末己方玩家+摩拉）")]
        MoraIncome = 2,

        [InspectorName("周期召唤（部署即召1个+每过周期回合再召）")]
        PeriodicSummon = 3,

        [InspectorName("治疗伤员（回合末治疗生命值低于阈值的我方存活单位）")]
        HealWounded = 4,
    }
}
