namespace GIC.Battle
{


    /// <summary>
    /// TurnResolver 对宿主的依赖面（2026-10-20 Replay/回归 harness 批：headless 回放解耦用）。
    /// 生产宿主=TurnFlowController（场景件：NotifySegmentPushed 驱动 ack 门控、HasSegmentAck 查
    /// 客户端回执）；回放宿主=ReplayRunner 内恒真 ack（不进 Play、无客户端、同步泵协程——
    /// TurnResolver 的 ack 等待循环零等待即出）。
    /// </summary>
    public interface ITurnResolverHost
    {
        /// <summary>片已推送（TurnFlowController：ack 键登记/超时追踪）</summary>
        void NotifySegmentPushed(int turnNumber, int sliceIndex);

        /// <summary>片是否已被全体客户端 ack（回放宿主恒 true=零等待）</summary>
        bool HasSegmentAck(int turnNumber, int sliceIndex);
    }
}
