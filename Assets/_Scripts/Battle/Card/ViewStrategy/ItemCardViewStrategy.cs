using UnityEngine;
using GIC.Framework;
using GIC.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Tool;
namespace GIC.Battle
{


    /// <summary>
    /// 物品卡牌的视图策略 — 处理 Card 本身的显示逻辑
    /// </summary>
    public class ItemCardViewStrategy : ICardViewStrategy
    {
        public void InitCardDisplay(Card card, SaveCardData data, CardDetailView detailView)
        {
            var cfg = data.Config;
            if (cfg != null)
            {
                card.cardBack.color = cfg.GetStarColor();
                card.itemImage.gameObject.SetActive(true);
                card.itemImage.sprite = cfg.GetSprite(data.skin);
                card.countImage.gameObject.SetActive(true);
            }

            card.unitImage.gameObject.SetActive(false);

            card.cardDetailView = detailView;
            card.saveCardData   = data;
            card.countText.text = data.count.ToString();
        }

        public void EnterEditMode(Card card)
        {
            var raw = CardConfigResolver.ItemConfig?.GetItemData(card.saveCardData.id.AsItemName());
            if (raw != null && raw.maxPrepareCount == 0)
                card.overlay.gameObject.SetActive(true);
        }

        public int GetTotalSkins(Card card) =>
            card.saveCardData.Config?.GetTotalSkins() ?? 0;

        public void ApplySkin(Card card, int skinIndex)
        {
            card.saveCardData.skin = skinIndex;
            card.itemImage.sprite  = card.saveCardData.Config?.GetSprite(skinIndex);
        }

        public bool ShouldOverlayInEditMode(Card card)
        {
            var raw = CardConfigResolver.ItemConfig?.GetItemData(card.saveCardData.id.AsItemName());
            return raw != null && raw.maxPrepareCount == 0;
        }
    }

}


