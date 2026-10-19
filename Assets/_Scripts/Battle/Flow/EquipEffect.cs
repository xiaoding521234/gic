using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{
    /// <summary>
    /// 装备效应（装备批 2026-10-18）：EquipItem 行动的结算载体——应用层挂 UnitEquipment（槽位+
    /// 词条），命令层产 EquipItem 命令（客户端血条右侧图标行）。**耐久消耗刷新复用本类**：格挡发生时
    /// ApplyEffects 直接构造已回填（Applied=true）的实例并入 effects（同吸血先收后并），命令层发
    /// EquipItem 同槽重发=角标更新（**决策八十一：耐久归零不再发 UnequipItem——留槽停效，图标留位
    /// 角标隐藏**）
    /// </summary>
    public class EquipEffect : BattleEffect
    {
        /// <summary>装备玩家（ItemConsumeEffect 同口径——玩家级归属）</summary>
        public string PlayerId;

        /// <summary>物品（ItemName 枚举值）</summary>
        public int Item;

        /// <summary>槽位种类（BattleCommand.EquipKindWeapon/EquipKindAccessory——应用层回填）</summary>
        public int SlotKind;

        /// <summary>槽位序号（武器恒 0；配件 0..2——应用层回填）</summary>
        public int SlotIndex;

        /// <summary>装备后剩余耐久（应用层回填；0=无耐久概念/归零停效态）</summary>
        public int DurabilityLeft;

        /// <summary>应用成功（槽位校验通过并已挂载；false=槽满/缺组件防御路径，零命令）</summary>
        public bool Applied;

        /// <summary>补充耐久模式（决策八十一「当已经有某装备时，再次装备，则补充耐久」）：已装备
        /// 同物品→ReplenishAccessory/ReplenishWeapon 既有槽位相加封顶（永不双槽）；false=新槽挂载</summary>
        public bool Replenish;

        /// <summary>本次装备/补充的实例耐久（手牌装备条目携带——掠夺物保留原耐久、新鲜卡=配置满值；
        /// -1=配置解析〔防御默认〕）</summary>
        public int IncomingDurability = -1;

        public EquipEffect(string playerId, string targetUnitId, int item)
        {
            PlayerId = playerId;
            TargetUnitId = targetUnitId;
            Item = item;
        }
    }
}
