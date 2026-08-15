using UnityEngine;
using GIC.Framework;
using GIC.UI;
using GIC.Data;
using GIC.Data.Event;
using GIC.Tool;
namespace GIC.Battle
{


    /// <summary>
    /// 物品卡牌的视图策略 — 处理 Card 本身的显示逻辑
    /// </summary>
    public class ItemCardViewStrategy : CardViewStrategyBase
    {
        public override void InitCardDisplay(Card card, SaveCardData data, CardDetailView detailView)
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

            InitCommon(card, data, detailView);
        }

        public override void EnterEditMode(Card card)
        {
            var raw = CardConfigResolver.Instance?.ItemConfig?.GetItemData(card.saveCardData.id.AsItemName());
            if (raw != null && raw.maxPrepareCount == 0)
                card.overlay.gameObject.SetActive(true);
        }

        public override void ApplySkin(Card card, int skinIndex) =>
            ApplySkinTo(card, skinIndex, card.itemImage);

        public override bool ShouldOverlayInEditMode(Card card)
        {
            var raw = CardConfigResolver.Instance?.ItemConfig?.GetItemData(card.saveCardData.id.AsItemName());
            return raw != null && raw.maxPrepareCount == 0;
        }
    }
}
