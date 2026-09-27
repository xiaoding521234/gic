using System;
using UnityEngine;
namespace GIC.Data
{


    /// <summary>
    /// 消耗资源种类（统一消耗模型 C-1，docs/active/30 §2.1）
    /// </summary>
    public enum CostKind
    {
        [InspectorName("元能")] Energy = 1,
        [InspectorName("体力")] Stamina = 2,
        [InspectorName("摩拉")] Mora = 3,
        [InspectorName("物品（指定）")] Item = 4,
        [InspectorName("物品（同类任意）")] AnyItem = 5,
    }

    /// <summary>
    /// 技能消耗声明条目（统一消耗模型 C-1，docs/active/30）：技能/行动消耗=数据驱动的
    /// (资源, 数量) 列表——SkillData.costs 挂接；**加消耗=配条目零代码**。
    /// 门槛语义（docs/18 决策十五）：不足=行动落空且不扣；先全查后全扣；
    /// 低级单位豁免玩家资源消耗（体力/Mora/Item/AnyItem——元能为单位自身资源不豁免）。
    /// C-2 存量迁移完成（2026-09-28）：全技能消耗已入 costs，旧口径（EnergyCost 参数门槛/体力类型
    /// 分档/移动常量直读）已退役——**costs 空即免费技能（无消耗语义，数据即事实）**。
    /// kind=Item=消耗指定物品（item 字段有效）；kind=AnyItem=消耗任意同类物品（subType 字段有效，
    /// 如「任意饮品」——跨同类条目凑足、原子性失败、按手牌列表序确定性扣减零随机；
    /// **货币卡（摩拉/体力）不可被 AnyItem 匹配**——账户资源不经物品消耗链，配 subType=Currency 属配置错误）。
    /// </summary>
    [Serializable]
    public class SkillCostEntry
    {
        [InspectorName("资源种类")]
        public CostKind kind = CostKind.Energy;

        [InspectorName("物品（kind=Item 有效）")]
        public ItemName item;

        [InspectorName("物品子类型（kind=AnyItem 有效）")]
        public ItemSubType subType = ItemSubType.Drink;

        [InspectorName("数量")]
        public int amount = 1;
    }
}
