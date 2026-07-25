using UnityEngine;

/// <summary>
/// 角色卡牌的视图策略 — 处理 Card 本身的显示逻辑
/// </summary>
public class UnitCardViewStrategy : ICardViewStrategy
{
    public void InitCardDisplay(Card card, SaveCardData data, CardDetailView detailView)
    {
        card.cardType = CardType.Unit;

        var raw = CardConfigResolver.UnitConfig?.GetUnitData(data.id.AsUnitName());
        if (raw != null)
        {
            card.cardBack.color = StarColor.GetStarColor(raw.starLevel);
            card.unitImage.gameObject.SetActive(true);
            card.unitImage.sprite = raw.GetCard(data.skin);
            card.obtainText.SetSingleEntry(raw.GetObtainDescriptionEntry());
        }

        card.itemImage.gameObject.SetActive(false);
        card.countImage.gameObject.SetActive(false);

        card.cardDetailView = detailView;
        card.saveCardData   = data;
        card.countText.text = data.count.ToString();
    }

    public void EnterEditMode(Card card)
    {
        if (card.saveCardData.count < 1)
            card.overlay.gameObject.SetActive(true);
    }

    public int GetTotalSkins(Card card) =>
        card.saveCardData.Config?.GetTotalSkins() ?? 0;

    public void ApplySkin(Card card, int skinIndex)
    {
        card.saveCardData.skin = skinIndex;
        card.unitImage.sprite  = card.saveCardData.Config?.GetSprite(skinIndex);
    }

    public bool ShouldOverlayInEditMode(Card card) =>
        card.saveCardData.count < 1;
}
