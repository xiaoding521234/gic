namespace GIC.Framework
{
    /// <summary>
    /// 手势识别器状态机（Apple UIGestureRecognizer / TouchScript 同构，docs/24 §4.2）：
    /// Idle → Possible →（离散：Recognized；连续：Began → Changed* → Ended）。
    /// Failed = 自判不成立**或被同 surface 其它识别器宣胜挤出**（TouchScript 原注释语义："failed by itself or by another recognized gesture"）；
    /// Cancelled = 外部打断（输入锁 / 双指起手取代单指 / 仲裁）。
    /// 终态后识别器自动复位回 Idle（Apple：事件序列结束复位）；连击等待（chain window）期间可停在 Possible。
    /// </summary>
    public enum GestureState
    {
        Idle,
        Possible,
        Began,
        Changed,
        /// <summary>离散手势完成（Apple discrete ended；TouchScript Recognized=Ended 同义分立）</summary>
        Recognized,
        /// <summary>连续手势正常收尾</summary>
        Ended,
        Cancelled,
        Failed
    }
}
