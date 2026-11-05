namespace GIC.Battle
{


    /// <summary>
    /// TurnResolver 对宿主的依赖面（2026-10-20 Replay/回归 harness 批：headless 回放解耦用）。
    /// 生产宿主=RTSFlowController；回放宿主=ReplayRunner。
    /// P6 残留审计 D2（docs/active/49）：片 ack 门控随回合机退役，HasSegmentAck 已删除
    /// （命令流带 startTick，客户端按绝对时刻播，不再需要 Host 等待客户端回执）。
    /// </summary>
    public interface ITurnResolverHost
    {
        /// <summary>片已推送（即时制保留钩子：录制/诊断用）</summary>
        void NotifySegmentPushed(int turnNumber, int sliceIndex);
    }
}
