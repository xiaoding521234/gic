using GIC.Data;
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

        /// <summary>多层 Buff 逐层效果错峰间隔（秒，2026-10-01 拍板「每层的效果延后 0.15s 生效，
        /// 避免同时弹出」——寒冰之棱/歌声之环多层 tick 各层独立弹数字；第 i 层时刻=i×此值，
        /// 与箭雨段间隔同节拍语言；Host 状态恒即时结算，错峰纯表现层——客户端 Damage/Heal
        /// launchMs 到点再弹已支持，零客户端改动）</summary>
        public const float BuffLayerStaggerSeconds = 0.15f;

        /// <summary>单位受击圆柱直径（世界单位；底座圆盘可视化同源，视觉即判定——docs/18 决策二）。
        /// 容错目检后可调（基线起点 0.42）</summary>
        public const float UnitCylinderDiameter = 0.42f;

        /// <summary>per-unit 受击圆柱直径单出口（协议核心批 2026-09-29）：声明值>0 用声明值（协议核心=0.8，
        /// 大目标易命中=攻城手感），0=回落全局 0.42。四消费方同源：ProjectileResolver 接触判定（Host 权威）、
        /// EffectCompiler.WouldHitProjectile 命中预判（推荐色口径）、UnitView 底座圆盘视觉（视觉即判定）、
        /// OrbitBeamsWorld 选中弧光贴紧折算</summary>
        public static float CylinderDiameterOf(UnitState state)
            => state != null && state.cylinderDiameter > 0f ? state.cylinderDiameter : UnitCylinderDiameter;

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

        /// <summary>每回合结束发放摩拉（docs/04 §4.3：与体力统一时机；2026-09-29 拍板 5→10）</summary>
        public const int MoraGainPerTurn = 10;

        /// <summary>每回合结束发放体力（2026-09-29 拍板 5→10）</summary>
        public const int StaminaGainPerTurn = 10;

        /// <summary>配额行动消耗体力（docs/05 §5.1：移动/战技/爆发各 10；
        /// 低级单位 1~2 星自主行动豁免，延奏/契约等特殊技能 0）</summary>
        public const int StaminaCostPerAction = 10;

        // ==================== 世界/画布 sortingOrder 层级表（2026-09-28 批6 收口：8 处散布字面量单源） ====================
        // 完整层序自下而上：底座盘 -1 → 瞄准贴片 0（隐式默认，无显式设置点）→ 底座弧光 1 → 立牌 10 →
        // Buff 徽章 11 → 箭矢 12 →〔水面走 shader renderQueue 2999，另一体系（docs/14 §89⑥）〕→
        // 头顶条 Overlay 画布 38 → 伤害数字 Overlay 画布 39 → 战斗 HUD 画布 40（BattleHud.prefab 序列化值）。
        // 插新层只改这里；消费方引用常量勿再写字面量。

        /// <summary>单位底座圆盘（恒先画于一切透明件——透明盘投影感，2026-09-27 拍板）</summary>
        public const int BaseDiscSortingOrder = -1;

        /// <summary>底座环绕弧光（选中特效；瞄准贴片之上、立牌之下）</summary>
        public const int DiscOrbitSortingOrder = 1;

        /// <summary>单位立牌（sprite 与视频 quad 同序——跨单位立牌遮挡排序语义一致）</summary>
        public const int AvatarSortingOrder = 10;

        /// <summary>头顶 Buff 徽章行（立牌之上、箭矢之下）</summary>
        public const int BuffBadgeSortingOrder = 11;

        /// <summary>投射物箭矢光条</summary>
        public const int ProjectileSortingOrder = 12;

        /// <summary>头顶条 Overlay 画布（伤害数字 39 之下、HUD 40 之下——数字漂过条上方时数字在上）</summary>
        public const int OverheadBarsCanvasOrder = 38;

        /// <summary>伤害数字 Overlay 画布（HUD 40 之下——数字是战场反馈非面板，面板应盖过它）</summary>
        public const int DamageNumbersCanvasOrder = 39;

        // ==================== 演算/碰撞规则常量（2026-10-02 执行阶段复审收口批次） ====================

        /// <summary>Host 片 ack 等待超时（秒，真实时间；超时快进——客户端卡死不冻结演算；
        /// B7 LAN 前按网络余量另议）</summary>
        public const float SegmentAckTimeoutSeconds = 15f;

        /// <summary>单格体积绝对层上限（docs/05 §5.3：格内现有体积+自身体积 ≤ 3——无视阻挡配置
        /// 不可绕过的最高级；移动进入/部署落点共享原语 MovementResolver.PassesVolumeLimit 同读）</summary>
        public const int MaxTileVolume = 3;

        /// <summary>多层 Buff 第 i 层拍时刻（秒，相对段播放起点）——逐层错峰单源
        /// （第 i 层=i×BuffLayerStaggerSeconds；消费方=光环 Buff OnTurnEnd 声明 PendingAuraHit/Heal/
        /// Energy 的 HitSeconds 与 TurnResolver 碎裂回血错峰，勿再散抄公式）</summary>
        public static float LayerBeatSeconds(int layer) => layer * BuffLayerStaggerSeconds;
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
