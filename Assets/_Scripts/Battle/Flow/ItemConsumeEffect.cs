using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{
    /// <summary>
    /// 物品消耗效应（统一消耗模型 C-1，docs/30 §2.3——技能声明 Item/AnyItem cost 用，如酒/苹果/食物）：
    /// TargetUnitId=玩家 ID。**双模式**：AnyOfSubType=false=指定物品（Item 字段有效）；
    /// AnyOfSubType=true=同类任意（SubType 字段有效，如「任意饮品」——应用时按手牌列表序
    /// 确定性逐条扣、跨条目凑足、原子性失败不扣分毫；货币卡不可被匹配，见 ResourceGate）。
    /// 应用=LoseCard（条目真源扣减、减尽移除）；命令=按 Consumed 明细逐条 ItemConsume
    /// （客户端手牌镜像即时扣减+角标刷新）；对账按明细 (玩家,物品) 查存在性。
    /// AppliedAmount 同 MoraSpend 防御性回填口径（0=不足零命令非漏发）。
    /// **装备实例耐久（决策八十一）**：Durability=指定模式的手牌条目实例匹配（装备条目按耐久分实例
    /// ——掠夺物 2 ≠ 新鲜卡 3；非装备物品恒 0）。
    /// </summary>
    public class ItemConsumeEffect : BattleEffect
    {
        public int Item;

        /// <summary>同类任意模式的子类型（AnyOfSubType=true 时有效）</summary>
        public int SubType;

        /// <summary>true=消耗任意 SubType 同类物品（Item 忽略）；false=消耗指定 Item</summary>
        public bool AnyOfSubType;

        /// <summary>指定物品模式的手牌条目实例耐久（装备条目按耐久分实例——决策八十一；非装备恒 0）</summary>
        public int Durability;

        /// <summary>申请消耗量</summary>
        public int Amount;

        /// <summary>实际扣减总量（ApplyEffects 回填；0=不足零命令）</summary>
        public int AppliedAmount;

        /// <summary>实际消耗明细（ApplyEffects 回填：(物品, 数量, 实例耐久) 逐条——命令发射/对账/
        /// 原子回滚遍历用；指定模式=单条，同类任意模式=按手牌序逐条凑量；耐久=装备条目实例（非装备恒 0）</summary>
        public readonly List<(int item, int count, int durability)> Consumed
            = new List<(int item, int count, int durability)>();

        /// <summary>指定物品模式构造（kind=Item）</summary>
        public ItemConsumeEffect(string playerId, ItemName item, int amount)
        {
            TargetUnitId = playerId;
            Item = (int)item;
            AnyOfSubType = false;
            Amount = amount;
        }

        /// <summary>同类任意模式构造（kind=AnyItem——如「任意饮品×1」）</summary>
        public ItemConsumeEffect(string playerId, ItemSubType subType, int amount)
        {
            TargetUnitId = playerId;
            SubType = (int)subType;
            AnyOfSubType = true;
            Amount = amount;
        }
    }
}
