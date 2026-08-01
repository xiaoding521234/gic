using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
using GIC.Tool;
namespace GIC.UI
{

    /// <summary>
    /// 卡牌详情面板接口 — 按 CardType 拆分的详情子面板实现此接口
    /// </summary>
    public interface ICardDetailPanel
    {
        /// <summary>初始化面板内容（由 CardDetailView.Init 调用）</summary>
        void Init(Card card);

        /// <summary>不依赖 Card 组件的初始化（用于关联面板等无 Card 实例的场景）</summary>
        /// <param name="isReadOnly">只读模式：隐藏使用按钮等可交互元素</param>
        void Init(SaveCardData data, bool isReadOnly = false);

        /// <summary>显示/隐藏面板</summary>
        void SetActive(bool active);

        /// <summary>设置使用按钮（仅物品面板有效，只读模式下自动隐藏）</summary>
        void SetUsable(IUsable usable, SaveCardData data);

        /// <summary>重建布局</summary>
        void RebuildLayout();
    }

}




