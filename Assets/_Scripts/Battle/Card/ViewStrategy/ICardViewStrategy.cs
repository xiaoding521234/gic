using GIC.Framework;
using GIC.UI;
using GIC.Data;
using GIC.Data.Event;
using GIC.Tool;

namespace GIC.Battle
{

    /// <summary>
    /// 卡牌视图策略接口 — 将 Card 中因 CardType 不同而产生的分支逻辑
    /// 抽到各自的策略实现中，消除 if (Unit) / if (Item) 的泛滥。
    /// </summary>
    public interface ICardViewStrategy
    {
        /// <summary>初始化卡牌本身的外观（Card.Init 中调用）</summary>
        void InitCardDisplay(Card card, SaveCardData data, CardDetailView detailView);

        /// <summary>进入编辑模式时的特殊处理</summary>
        void EnterEditMode(Card card);

        /// <summary>皮肤总数</summary>
        int GetTotalSkins(Card card);

        /// <summary>应用第 N 个皮肤</summary>
        void ApplySkin(Card card, int skinIndex);

        /// <summary>进入编辑时是否应被覆盖遮罩</summary>
        bool ShouldOverlayInEditMode(Card card);
    }

}




