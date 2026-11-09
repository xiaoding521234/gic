namespace GIC.Battle
{
    /// <summary>
    /// 战斗相位（P6 残留审计从 TurnFlowController.cs 拆出，docs/active/49 D1）。
    /// **即时制语义（决策九十五）**：RTSFlowController 把 Running 兼容映射为 Selecting 并常驻，
    /// 且每 0.5 秒（AI 节拍）重复广播 OnPhaseChanged(Selecting, second)——消费方必须做幂等守卫，
    /// 勿把「同一相位重复事件」当阶段切换（HUD 已按此修复，docs/active/49 A1）。
    /// </summary>
    public enum BattlePhase
    {
        Idle,       // 未开战
        Selecting,  // 选择阶段（即时制=Running 兼容映射，战斗全程常驻）
        // Resolving 已删除（2026-11-09 残留清理批：执行相位随回合机退役，即时制不进入；编号不复用）
        Finished,   // 战斗结束
    }
}