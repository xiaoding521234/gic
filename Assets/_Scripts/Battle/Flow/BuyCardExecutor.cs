using System;
using GIC.Data;
using GIC.Framework;
namespace GIC.Battle
{


    /// <summary>
    /// 购买接纳与结算（docs/45 §7）：房主接纳时查收藏，结算只读共享配置和模拟状态。
    /// 已接纳的输入可在两端或回放中执行，不依赖执行机器的存档。
    /// </summary>
    internal static class BuyCardExecutor
    {
        public static bool CanAccept(BattleSimState sim, ActionData action, Func<string, int, bool> ownsUnit)
        {
            var cardId = new CardId((CardType)action.buyCardType, action.buyCardValue);
            if (!TryQuote(sim, action.playerId, cardId, out int cost, out var unitConfig)) return false;
            if (cardId.RequiresPurchaseUnlock(unitConfig) && ownsUnit?.Invoke(action.playerId, cardId.value) != true)
            {
                GICLog.Info($"[BuyCard] {action.playerId} 购买 {cardId} 落空（角色未解锁）");
                return false;
            }
            return sim.GetMora(action.playerId) >= cost;
        }

        /// <summary>在执行拍重新核对来源和余额；失败不扣费、不发命令。</summary>
        public static bool TryResolve(BattleSimState sim, ActionData action, out Segment segment)
        {
            segment = null;
            var cardId = new CardId((CardType)action.buyCardType, action.buyCardValue);
            if (!TryQuote(sim, action.playerId, cardId, out int cost, out _))
                return false;
            if (!sim.TrySpendMora(action.playerId, cost))
            {
                GICLog.Info($"[BuyCard] {action.playerId} 购买 {cardId} 落空（摩拉不足 {sim.GetMora(action.playerId)}/{cost}）");
                return false;
            }

            sim.GainCard(action.playerId, cardId, 1);
            // 保持单机 v2 购买段的编码和命令顺序。
            segment = new Segment
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
            GICLog.Info($"[BuyCard] {action.playerId} 购买 {cardId}（摩拉 {cost}）");
            return true;
        }

        private static bool TryQuote(BattleSimState sim, string playerId, CardId cardId,
            out int cost, out UnitConfig unitConfig)
        {
            var context = Wargame.Instance?.Context;
            unitConfig = context?.Get<UnitConfig>();
            var itemConfig = cardId.cardType == CardType.Item ? context?.Get<ItemConfig>() : null;
            if (!cardId.TryGetPurchaseCost(unitConfig, itemConfig, out cost)) return false;
            if (HasPurchaseSource(sim, playerId, cardId)) return true;
            GICLog.Info($"[BuyCard] {playerId} 购买 {cardId} 落空（无存活建筑出售此卡）");
            return false;
        }

        /// <summary>来源校验（拍板 #5）：该玩家任一存活建筑的可购买卡片列表含此卡——
        /// 显式列表数据驱动（docs/45 §1.2），不指定具体建筑=等价校验防凭空购买</summary>
        private static bool HasPurchaseSource(BattleSimState sim, string playerId, CardId cardId)
        {
            foreach (var kv in sim.SortedUnits())
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
    }
}
