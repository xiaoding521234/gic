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
                MissingImageGuard.Assign(card.unitImage, raw.GetCard(data.skin)); // 立绘缺失兜底（docs：调用点显式）
                card.obtainText.SetSingleEntry(raw.GetObtainDescriptionEntry());
            }

            card.itemImage.gameObject.SetActive(false);
            card.countImage.gameObject.SetActive(false);

            // 初始态：未拥有卡显示半透明遮罩（不含获取文本，仍可点击查看详情）
            SetOverlayState(card, data.count < 1, false);

            InitCommon(card, data, detailView);
        }

        public override void EnterEditMode(Card card)
        {
            if (card.saveCardData.count < 1)
                SetOverlayState(card, true, true); // 编辑模式：遮罩+获取方式文本（现状语义）
        }

        public override void ExitEditDeck(Card card) =>
            SetOverlayState(card, card.saveCardData.count < 1, false); // 回初始态：未拥有仅遮罩

        public override void ApplySkin(Card card, int skinIndex) =>
            ApplySkinTo(card, skinIndex, card.unitImage);

        public override bool ShouldOverlayInEditMode(Card card) =>
            card.saveCardData.count < 1;
    }
}
