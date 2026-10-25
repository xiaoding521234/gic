namespace GIC.Battle
{
    /// <summary>
    /// 建筑元能引擎过阈召唤对账事实（2026-10-25 复审批：安全网随内容族扩展——此前排在 audit 之后
    /// 执行=审计对它结构性失明）。不参与结算与命令发射（发射在 TurnResolver.DrainPendingBuildingSummons
    /// 直产）——只作为「每条过阈事实必产消费命令（StatChange 能量 -阈值）」的对账凭据
    /// （ConsumeFreezeEffect 事实先例同族）；召唤命令可选（棋盘占满/同段死亡=合法落空）不对账。
    /// </summary>
    public class BuildingSummonFactEffect : BattleEffect
    {
        /// <summary>过阈的建筑单位 id（对账键=energyKeys 按目标查存在性）</summary>
        public string BuildingId;

        public BuildingSummonFactEffect(string buildingId)
        {
            TargetUnitId = buildingId;
            BuildingId = buildingId;
        }
    }
}
