/// <summary>
/// 可使用物品接口 — 实现此接口的物品在详情面板中显示"使用"按钮
/// </summary>
public interface IUsable
{
    /// <summary>是否可以使用</summary>
    bool CanUse { get; }

    /// <summary>使用按钮文本</summary>
    TextEntry GetUseButtonText();

    /// <summary>
    /// 尝试使用物品
    /// </summary>
    /// <param name="data">存档数据</param>
    /// <param name="resultMessage">使用结果消息（用于弹窗提示）</param>
    /// <returns>true=使用成功, false=使用失败</returns>
    bool TryUse(SaveCardData data, out string resultMessage);
}
