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

        // ==================== 体力/摩拉局内经济（B6d；数值定义 docs/04 §4.3 / docs/05 §5.1） ====================

        /// <summary>开局摩拉（=玩家摩拉物品牌的初始持有数，docs/03 §3.2）</summary>
        public const int InitialMora = 200;

        /// <summary>开局体力（=玩家体力物品牌「原粹树脂」的初始持有数）</summary>
        public const int InitialStamina = 60;

        /// <summary>每回合结束发放摩拉（docs/04 §4.3：与体力统一时机）</summary>
        public const int MoraGainPerTurn = 5;

        /// <summary>每回合结束发放体力</summary>
        public const int StaminaGainPerTurn = 5;

        /// <summary>配额行动消耗体力（docs/05 §5.1：移动/战技/爆发各 10；
        /// 低级单位 1~2 星自主行动豁免，延奏/契约等特殊技能 0）</summary>
        public const int StaminaCostPerAction = 10;
    }

    /// <summary>
    /// 投射物规则常量（docs/05 §5.3 统一拍板 + docs/18 决策二"投放形态由技能数据驱动"；
    /// 原寄居 AmberDoubleShotSkill.cs，B-1 技能类退役迁入常量收口处）
    /// </summary>
    public static class ProjectileRule
    {
        /// <summary>直线型弹射物飞行上限（统一 24 格）</summary>
        public const int MaxRange = 24;

        /// <summary>投放形态：直线飞行投射物（客户端播箭矢）</summary>
        public const int LineDelivery = 1;
    }
}
