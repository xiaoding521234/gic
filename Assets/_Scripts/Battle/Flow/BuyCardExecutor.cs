using System.Collections.Generic;
using GIC.Data;
using GIC.Framework;
namespace GIC.Battle
{


    /// <summary>
    /// 购买执行器（决策八十六批E，docs/active/45 §7）：选择阶段即时行动通道
    /// （SubmitInstantAction→BattleSession.HandleInstantSubmit 的 BuyCard 分支）——
    /// **即时执行、不占配额、不进执行段**（校验/结算/命令广播即完，不写 _pendingActions——
    /// 购买后玩家仍可照常上交本回合行动）。
    /// 校验链（Host 权威，客户端预检+灰显为主 UX）：①该玩家存在存活建筑其可购买列表含此卡
    /// （来源建筑不指定=等价校验，防凭空购买）；②（角色卡）玩家已解锁（存档 ownedUnits——
    /// 与祈愿同源，抽到/初始解锁才可买）；③摩拉够（购买价单源=CardIdPurchaseExtensions.PurchaseCostOf：
    /// 单位卡=部署价一半、物品卡=星级摩拉价值表）。
    /// 结算=GainCard 入手牌+扣摩拉+直产迷你段（CardGain 单位卡 / ItemGain 物品卡 + StatChange(Mora)；
    /// sliceIndex=-1+insertedInstantAction=1 出带标记——LAN 客户端不派生执行相位）即时广播
    /// +BattleRecorder 录段（回放按行动序重演，生成的购买段进入命令流 diff 面）。
    /// 失败=零命令零状态变更。回放口径：SaveManager 缺失（编辑器域最小容器）=解锁判定恒过
    /// （录制时已验证，重演不因环境缺档漂移）。
    /// </summary>
    public static class BuyCardExecutor
    {
        /// <summary>购买结算（Host 权威入口）。成功=true（已 GainCard+扣摩拉+广播+录制）</summary>
        public static bool TryResolve(BattleSimState sim, IBattleTransport transport, ActionData action)
        {
            if (action == null || sim == null) return false;
            var cardId = new CardId((CardType)action.buyCardType, action.buyCardValue);

            // ①来源建筑：该玩家存活建筑的可购买列表含此卡
            if (!HasPurchaseSource(sim, action.playerId, cardId))
            {
                GICLog.Info($"[BuyCard] {action.playerId} 购买 {cardId} 落空（无存活建筑出售此卡）");
                return false;
            }

            // ②角色卡解锁（拍板 #11「购买角色卡需玩家已解锁该角色」；物品卡/建筑卡无门槛）
            if (cardId.cardType == CardType.Unit && !IsUnitUnlocked(cardId))
            {
                GICLog.Info($"[BuyCard] {action.playerId} 购买 {cardId} 落空（角色未解锁）");
                return false;
            }

            // ③价格与摩拉（购买价单源）
            var unitConfig = Wargame.Instance?.Context?.Get<UnitConfig>();
            var itemConfig = Wargame.Instance?.Context?.Get<ItemConfig>();
            int cost = cardId.PurchaseCostOf(unitConfig, itemConfig);
            if (cost > 0 && !sim.TrySpendMora(action.playerId, cost))
            {
                GICLog.Info($"[BuyCard] {action.playerId} 购买 {cardId} 落空（摩拉不足 {sim.GetMora(action.playerId)}/{cost}）");
                return false;
            }

            // 结算：入手牌（count 合并语义同 GainCard）
            sim.GainCard(action.playerId, cardId, 1);

            // 迷你段广播（sliceIndex=-1 出带；insertedInstantAction=1=客户端不派生相位）
            var segment = new Segment
            {
                turnNumber = action.turnNumber,
                sliceIndex = -1,
                sliceAttackSpeed = 0,
                turnMaxAttackSpeed = 0,
                insertedInstantAction = 1,
            };
            BattleCommand gainCommand = cardId.cardType == CardType.Unit
                ? BattleCommand.CardGain(action.playerId, -1, 0, cardId.value, 1)
                : BattleCommand.ItemGain(action.playerId, -1, 0, cardId.value, 1);
            segment.commands.Add(gainCommand);
            if (cost != 0)
                segment.commands.Add(BattleCommand.StatChange(action.playerId, -1, 1,
                    BattleCommand.StatKindMora, -cost));
            transport?.HostSend(BattleMessageType.Segment, new SegmentMessage { segment = segment });
            BattleRecorder.CaptureSegment(segment); // 回放断言面（生成的购买段进命令流 diff）
            BattleRecorder.CaptureInstantAction(action.turnNumber, action); // 行动行（CaptureActions 时前置合并）
            GICLog.Info($"[BuyCard] {action.playerId} 购买 {cardId}（摩拉 {cost}）");
            return true;
        }

        /// <summary>来源校验（拍板 #5）：该玩家任一存活建筑的可购买卡片列表含此卡——
        /// 显式列表数据驱动（docs/active/45 §1.2），不指定具体建筑=等价校验防凭空购买</summary>
        private static bool HasPurchaseSource(BattleSimState sim, string playerId, CardId cardId)
        {
            foreach (var kv in sim.Units)
            {
                var unit = kv.Value;
                if (unit == null || BattleSimState.IsDead(unit)) continue;
                var identity = unit.GetUnitComponent<UnitIdentity>();
                if (identity == null || identity.OwnerPlayerID != playerId) continue;
                var data = unit.RawData;
                if (data == null || data.unitType != UnitType.Building) continue;
                if (data.可购买卡片 == null) continue;
                foreach (var purchasable in data.可购买卡片)
                    if (purchasable == cardId) return true;
            }
            return false;
        }

        /// <summary>角色解锁判定（与祈愿同源=存档卡牌收藏 ownedUnits；建筑卡恒解锁）。
        /// 存档系统缺失（编辑器域回放/最小容器）=恒过（录制时已验证，重演不漂移）</summary>
        private static bool IsUnitUnlocked(CardId cardId)
        {
            var saveManager = Wargame.Instance?.Context?.Get<SaveManager>();
            var ownedUnits = saveManager?.CurrentSave?.progress?.ownedUnits;
            if (ownedUnits == null) return true; // 回放/编辑器域口径
            foreach (var card in ownedUnits)
            {
                if (card == null || card.id.cardType != CardType.Unit) continue;
                if (card.id.value == cardId.value) return true;
            }
            return false;
        }
    }
}
