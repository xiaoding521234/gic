using System;
namespace GIC.Data
{


    /// <summary>
    /// 战斗迷雾模式（2026-10-17 迷雾批，docs/active/44）：对局级表现层配置，随开局链
    /// （房间名册 → StartBattleEvent/BattleLaunchConfig → BattleScreen）透传，LAN 各端同模式。
    /// 迷雾=纯表现层系统（客户端本地计算本方探索集），Host 模拟零感知。
    /// </summary>
    [Serializable]
    public enum BattleFogMode
    {
        /// <summary>无迷雾（沙盒/调试直开/纯 AI 观察局）</summary>
        None = 0,

        /// <summary>对战迷雾：未探索格盖半透明暗雾——地形完整可见（RA2 式，docs/03 §3.6），
        /// 敌方单位在未探索格隐藏；房间对战默认档（用户拍板「开房间对战时，不用记忆地图」）</summary>
        Skirmish = 1,

        /// <summary>任务迷雾：未探索格罩不透明混沌盖——地形类型零泄露（虚空格同罩），
        /// 探索感档；任务系统（后续批）开局用，当前经编辑器测试局入口验证</summary>
        Mission = 2,
    }
}
