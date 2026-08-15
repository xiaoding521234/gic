using UnityEngine;
using GIC.Framework;
using GIC.UI;
using GIC.Data;
using GIC.Data.Event;
using GIC.Tool;
namespace GIC.Battle
{


    /// <summary>
    /// 角色卡牌的视图策略 — 处理 Card 本身的显示逻辑
    /// </summary>
    public class UnitCardViewStrategy : CardViewStrategyBase
    {
        public override void InitCardDisplay(Card card, SaveCardData data, CardDetailView detailView)
        {
            var raw = CardConfigResolver.Instance?.UnitConfig?.GetUnitData(data.id.AsUnitName());
            if (raw != null)
            {
                card.cardBack.color = StarVisualConfig.GetStarColor(raw.starLevel);
                card.unitImage.gameObject.SetActive(true);
                card.unitImage.sprite = raw.GetCard(data.skin);
                card.obtainText.SetSingleEntry(raw.GetObtainDescriptionEntry());
            }

            card.itemImage.gameObject.SetActive(false);
            card.countImage.gameObject.SetActive(false);

            InitCommon(card, data, detailView);
        }

        public override void EnterEditMode(Card card)
        {
            if (card.saveCardData.count < 1)
                card.overlay.gameObject.SetActive(true);
        }

        public override void ApplySkin(Card card, int skinIndex) =>
            ApplySkinTo(card, skinIndex, card.unitImage);

        public override bool ShouldOverlayInEditMode(Card card) =>
            card.saveCardData.count < 1;
    }
}
