using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;
using GIC.UI;
using GIC.Data;
using GIC.Data.Event;
using GIC.Tool;
namespace GIC.Battle
{


    /// <summary>
    /// 卡牌视图策略基类 — 收敛 Unit/Item 策略中完全一致的公共逻辑
    /// （Init 尾部字段赋值 / 皮肤总数 / 皮肤写入+取图）
    /// </summary>
    public abstract class CardViewStrategyBase : ICardViewStrategy
    {
        /// <summary>InitCardDisplay 的公共尾部：绑定详情视图、存档数据与数量文本</summary>
        protected static void InitCommon(Card card, SaveCardData data, CardDetailView detailView)
        {
            card.cardDetailView = detailView;
            card.saveCardData = data;
            card.countText.text = data.count.ToString();
        }

        /// <summary>皮肤写入公共部分：更新存档 skin 并把对应图赋给目标 Image</summary>
        protected static void ApplySkinTo(Card card, int skinIndex, Image targetImage)
        {
            card.saveCardData.skin = skinIndex;
            targetImage.sprite = card.saveCardData.Config?.GetSprite(skinIndex);
        }

        public int GetTotalSkins(Card card) =>
            card.saveCardData.Config?.GetTotalSkins() ?? 0;

        public abstract void InitCardDisplay(Card card, SaveCardData data, CardDetailView detailView);
        public abstract void EnterEditMode(Card card);
        public abstract void ApplySkin(Card card, int skinIndex);
        public abstract bool ShouldOverlayInEditMode(Card card);
    }
}
