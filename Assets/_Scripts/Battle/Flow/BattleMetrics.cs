namespace GIC.Battle
{


    /// <summary>
    /// 战斗判定常量收口（B5 连续判定体系，docs/active/22 §11）：
    /// Host 演算（ProjectileResolver）与客户端播放（BattlePlayer）共用同一速度/尺寸常量，
    /// 保证"所见即所得"——命中时刻由 Host 判定后随命令下发，客户端按同源常量播放自然对齐。
    /// 阈值类常量一律收口于此，勿散写（先例=手势层 GestureMetrics）。
    /// </summary>
    public static class BattleMetrics
    {
        /// <summary>投射物飞行速度（格/秒；1 格=1 世界单位）</summary>
        public const float ProjectileSpeed = 8f;

        /// <summary>移动每格耗时（秒）——移动插值速度 = 1/此值 格/秒</summary>
        public const float MoveStepSeconds = 0.18f;

        /// <summary>单位受击圆柱直径（世界单位；底座圆盘可视化同源，视觉即判定——docs/18 决策二）。
        /// 容错目检后可调（基线起点 0.42）</summary>
        public const float UnitCylinderDiameter = 0.42f;

        // ==================== 元能（B6a；获取端拍板 2026-09-22） ====================

        /// <summary>移动使用即获得的元能（被挡也算——移动行动已使用）</summary>
        public const int EnergyGainPerMove = 10;

        /// <summary>战技至少 1 次命中获得的元能（多次命中不叠加——同片按行动者合并去重；
        /// 爆发/延奏命中不获能）</summary>
        public const int EnergyGainPerSkillHit = 10;
    }
}
