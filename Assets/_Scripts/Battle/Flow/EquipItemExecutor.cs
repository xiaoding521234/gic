using System;
using System.Collections.Generic;
using GIC.Framework;
using GIC.Data;
namespace GIC.Battle
{
    /// <summary>
    /// 装备物品执行器（2026-10-18 装备批，docs/05 §5.2+docs/01「装备即消耗」——使用物品同构的
    /// 玩家级行动，占配额不耗体力）：校验链=物品配置存在且 subType 为 Weapon/Accessory → 手牌持有
    ///（Host 权威，同 UseItem/DeployUnit 先例）→ 装备目标为我方存活非建筑单位（targetUnitId，
    /// 域=饮用同款——尸体不可装备、建筑不可装备）→ 槽位有空（武器槽恒 1 空闲/配件按层级眷属1·
    /// 伙伴2·魔神3 有空槽）→ 消耗 1 张（ItemConsumeEffect）+ EquipEffect（应用层挂 UnitEquipment
    /// 槽位+词条，命令层产 EquipItem 命令客户端镜像）。**槽位校验读活单位**（snapshot 目标校验后
    /// 取 sim 单位——同段不可能有第二个装备改同一单位：装备只对己方单位、每玩家每秒拍 1 配额行动）。
    /// </summary>
    public static class EquipItemExecutor
    {
        /// <summary>校验+消耗+装备效应并入。成功=true；失败=零效果零消耗（行动已使用，占配额不退
        /// ——同 UseItem 口径：Host 校验兜底，常规路径 HUD 只给可装备入口+槽位/耐久预判域）。
        /// **决策八十一耐久统一方案**：①手牌持有校验按实例三元组 (Item, value, equipDurability)
        /// 匹配（装备条目按耐久分实例）；②已装备同物品→补充耐久路径（既有槽位相加封顶、永不双槽；
        /// 满耐久=落空——客户端拖入时已提示）；③未装备→空槽安装（实例耐久随卡——掠夺物保留原耐久）</summary>
        public static bool TryResolve(BattleSimState sim, ActionData action, BattleSnapshot sliceSnapshot,
            List<BattleEffect> effects)
        {
            var itemName = (ItemName)action.equipItemId;
            var config = Wargame.Instance?.Context?.Get<ItemConfig>();
            var data = config?.GetItemData(itemName);
            if (data == null || (data.subType != ItemSubType.Weapon && data.subType != ItemSubType.Accessory))
            {
                GICLog.Warn($"[EquipItem] {action.playerId} 上交 {itemName} 无配置或非武器/配件（不可装备），装备落空");
                return false;
            }

            // 手牌持有校验（Host 权威——实例三元组匹配：cardType==Item + value + 实例耐久；
            // 装备条目按耐久分实例〔决策八十一〕，非装备物品恒 0 不受影响）
            var hand = sim.GetHand(action.playerId);
            bool inHand = false;
            if (hand != null)
            {
                foreach (var card in hand)
                {
                    if ((CardType)card.cardType != CardType.Item) continue;
                    if (card.value == (int)itemName && card.durability == action.equipDurability && card.count >= 1)
                    {
                        inHand = true;
                        break;
                    }
                }
            }
            if (!inHand)
            {
                GICLog.Warn($"[EquipItem] {action.playerId} 手牌无 {itemName}（耐久 {action.equipDurability}）实例，装备拒绝（手牌形态：{DumpHand(sim, action.playerId)}）");
                return false;
            }

            // 装备目标校验：我方存活非建筑单位（域=饮用同款——TeamType 口径同延奏；尸体不可装备；
            // 灵魂不可装备——地脉维度不可及+无装备拍板，决策九十一）
            var targetState = SkillHitResolver.FindUnitState(sliceSnapshot, action.targetUnitId);
            var ownerTeam = sim.GetTeamOf(action.playerId);
            if (targetState == null || (TeamType)targetState.team != ownerTeam || targetState.isCorpse != 0
                || targetState.isSoul != 0)
            {
                GICLog.Warn($"[EquipItem] {action.playerId} 装备 {itemName} 目标无效（{action.targetUnitId}——须为我方存活单位），装备落空");
                return false;
            }
            var target = sim.GetUnit(action.targetUnitId);
            if (target == null)
            {
                GICLog.Warn($"[EquipItem] {action.playerId} 装备 {itemName} 目标单位缺失（{action.targetUnitId}），装备落空");
                return false;
            }
            if (target.RawData != null && target.RawData.unitType == UnitType.Building)
            {
                GICLog.Warn($"[EquipItem] {action.playerId} 装备 {itemName} 目标为建筑（{action.targetUnitId}），装备落空");
                return false;
            }

            var equipment = target.GetUnitComponent<UnitEquipment>();
            if (equipment == null)
            {
                GICLog.Warn($"[EquipItem] 目标 {action.targetUnitId} 无 UnitEquipment 组件（Unit.prefab 未挂载？），装备落空");
                return false;
            }

            // 补充耐久/加层路径（决策八十一「当已经有某装备时，再次装备，则补充耐久，永远不出现两个
            // 槽位都是某装备」）：已装备同物品→合并既有槽；满（耐久件满耐久/堆叠件满层）=落空
            //（客户端点选时已提示，Host 兜底——重复装备浪费卡无意义）；未装备→空槽安装。
            // 堆叠配件 incoming=恒 1 层（决策八十一追记「每次装备仅装备1个」——手牌实例耐久对其恒 0 无意义）
            bool isWeapon = data.subType == ItemSubType.Weapon;
            bool alreadyHeld = equipment.HasEquipped(itemName);
            int incoming = UnitEquipment.IsStackAccessory(itemName) ? 1 : action.equipDurability;
            if (alreadyHeld)
            {
                int cap = UnitEquipment.SlotCapOf(itemName);
                int current = isWeapon ? equipment.WeaponDurability : equipment.HeldAccessoryDurability(itemName);
                if (current >= cap)
                {
                    GICLog.Info($"[EquipItem] {action.playerId} 装备 {itemName} → {action.targetUnitId} 已持有且已满" +
                                $"（{current}/{cap}），装备落空");
                    return false;
                }
            }
            else
            {
                bool slotFree = isWeapon ? equipment.HasFreeWeaponSlot : equipment.HasFreeAccessorySlot;
                if (!slotFree)
                {
                    GICLog.Info($"[EquipItem] {action.playerId} 装备 {itemName} → {action.targetUnitId} 槽位已满，装备落空");
                    return false;
                }
            }

            // 消耗 1 张（装备即消耗，docs/01）+ 装备效应（应用层挂槽/补充+词条，命令层 EquipItem）
            effects.Add(new ItemConsumeEffect(action.playerId, itemName, 1) { Durability = action.equipDurability });
            effects.Add(new EquipEffect(action.playerId, action.targetUnitId, (int)itemName)
            {
                Replenish = alreadyHeld,
                IncomingDurability = incoming,
            });

            GICLog.Info($"[EquipItem] {action.playerId} {(alreadyHeld ? "补充耐久" : "装备")} {itemName}" +
                        $"（耐久 {action.equipDurability}）→ {action.targetUnitId}");
            return true;
        }

        /// <summary>手牌条目形态 dump（与 UseItemExecutor 同源诊断；装备实例耐久随条显示——决策八十一）</summary>
        private static string DumpHand(BattleSimState sim, string playerId)
        {
            var hand = sim.GetHand(playerId);
            if (hand == null) return "手牌未注册";
            var sb = new System.Text.StringBuilder();
            foreach (var card in hand)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append($"[{card.cardType}:{card.value}x{card.count}" +
                          (card.durability > 0 ? $"耐{card.durability}" : "") + "]");
            }
            return sb.Length == 0 ? "空手牌" : sb.ToString();
        }
    }
}
