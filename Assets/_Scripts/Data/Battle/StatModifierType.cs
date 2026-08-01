using GIC.Framework;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Data
{

    /// <summary>
    /// 属性修改器类型
    /// </summary>
    public enum StatModifierType
    {
        BaseFlat,       // 基础值固定加成
        BasePercent,    // 基础值百分比加成
        MinFlat,        // 下限固定加成
        MinPercent,     // 下限百分比加成
        MaxFlat,        // 上限固定加成
        MaxPercent,     // 上限百分比加成
        ValueFlat,      // 当前值固定加成
        ValuePercent    // 当前值百分比加成
    }
}

