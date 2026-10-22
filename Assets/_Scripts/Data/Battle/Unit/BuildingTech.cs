using UnityEngine;
namespace GIC.Data
{


    /// <summary>
    /// 建筑科技梯度（2026-10-21 决策八十六，docs/active/45 §2）：
    /// 1~2★=低科建筑、3~4★=中科建筑、5★=高科建筑。
    /// 部署前置链：中科需自己有低科建筑在场、高科需自己有中科建筑在场（低科=自己有 Core tag 建筑在场，
    /// 恒满足仍显式校验）；**不共享队友**（2v2 各算各——判定一律按 OwnerPlayerID 过滤）。
    /// 协议核心 5★=高科：开局在场、不受部署前置约束、不参与「上一梯度在场」判定。
    /// 梯度由星级推导（零配置迁移），全项目门控逻辑一律经 <see cref="BuildingTechHelper.FromStars"/>
    /// 换算后按枚举分支，禁止在代码里散落星级区间判断（对齐 UnitTierHelper 惯例）。
    /// </summary>
    public enum BuildingTech
    {
        [InspectorName("低科建筑（1-2星）")] LowTech = 1,
        [InspectorName("中科建筑（3-4星）")] MidTech = 2,
        [InspectorName("高科建筑（5星）")] HighTech = 3,
    }

    /// <summary>
    /// 建筑梯度换算唯一真源（docs/active/45 §2）：FromStars 星级→梯度单一出口
    /// （1-2→低科、3-4→中科、5→高科；越界拍平到最近档）。
    /// </summary>
    public static class BuildingTechHelper
    {
        /// <summary>星级→建筑梯度单一换算出口（全项目门控只看此枚举，勿散写星级区间判断）</summary>
        public static BuildingTech FromStars(int starLevel)
        {
            if (starLevel <= 2) return BuildingTech.LowTech;
            if (starLevel <= 4) return BuildingTech.MidTech;
            return BuildingTech.HighTech;
        }
    }
}
