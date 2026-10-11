// BattleDeckBuilder.cs - 玩家当前卡组 → 手牌获得清单单源（2026-10-11 联机批抽取）
// 消费方两处共享同源：BattleScreen（host 侧本机/AI 玩家手牌构建）+ CoopScreen（LAN 客户端卡组上报）。
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
using GIC.Framework;
namespace GIC.Battle
{


    public static class BattleDeckBuilder
    {
        /// <summary>
        /// 玩家存档当前卡组 → 初始手牌获得清单（2026-09-25 拍板：初始卡组按顺序经 GainCard 获得）：
        /// 每卡携带获得数量（角色=1、物品=备战数 min(存档持有, maxPrepareCount 备战上限)——
        /// 如背包 100 体力牌、备战上限 60 → 获得 60）；货币物品牌（摩拉/体力）编入=开局增量
        /// （2026-09-29 拍板 A——编入量并入开局资源池，统一赠送 200/60 照送）；空卡组回退丘丘人×2。
        /// 走 CardManager 卡组视图=与收藏卡组界面同源同排序（角色前物品后、SortOrder、星级）。
        /// </summary>
        public static List<HandCard> BuildCurrentDeck(SaveManager saveManager, CardManager cardManager)
        {
            var result = new List<HandCard>();
            var save = saveManager?.CurrentSave;
            if (save != null)
            {
                int currentDeck = save.progress.currentDeck;
                var decks = cardManager?.decks;
                if (decks != null && currentDeck >= 0 && currentDeck < decks.Length)
                {
                    foreach (var card in decks[currentDeck].Cards)
                    {
                        int count = 1;
                        if (card.id.cardType == CardType.Item)
                        {
                            var itemData = CardConfigResolver.Instance?.ItemConfig?.GetItemData(card.id.AsItemName());
                            count = Mathf.Min(
                                saveManager?.CurrentSave?.GetItemCount(card.id.AsItemName()) ?? 0,
                                itemData?.maxPrepareCount ?? 0);
                        }
                        result.Add(new HandCard(card.id, count));
                    }
                }
            }
            if (result.Count == 0)
            {
                result.Add(new HandCard(new CardId(UnitName.Hilichurl), 1));
                result.Add(new HandCard(new CardId(UnitName.Hilichurl), 1));
            }
            return result;
        }
    }
}
