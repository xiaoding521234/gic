/// <summary>
/// 卡牌详情面板接口 — 按 CardType 拆分的详情子面板实现此接口
/// </summary>
public interface ICardDetailPanel
{
    /// <summary>初始化面板内容（由 CardDetailView.Init 调用）</summary>
    void Init(Card card);

    /// <summary>显示/隐藏面板</summary>
    void SetActive(bool active);

    /// <summary>设置使用按钮（仅物品面板有效）</summary>
    void SetUsable(IUsable usable, SaveCardData data);

    /// <summary>重建布局</summary>
    void RebuildLayout();
}
