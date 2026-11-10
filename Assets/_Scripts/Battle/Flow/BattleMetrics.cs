using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 战斗判定常量收口（B5 连续判定体系，docs/22 §11）：
    /// Host 演算（ProjectileResolver）与客户端播放（BattlePlayer）共用同一速度/尺寸常量，
    /// 保证"所见即所得"——命中时刻由 Host 判定后随命令下发，客户端按同源常量播放自然对齐。
    /// 阈值类常量一律收口于此，勿散写（先例=手势层 GestureMetrics）。
    /// </summary>
    public static class BattleMetrics
    {
        /// <summary>投射物飞行速度（厘米/秒——决策九十五 P3，蓝图 §10：8 格/s=800cm/s 直译；
        /// 1 格=100cm=1 米=1 世界单位；时轮 clip.projectileSpeed=0 回落此默认；全整数模拟纪律）</summary>
        public const int ProjectileSpeedCmPerSecond = 800;

        /// <summary>移动每格耗时（秒）——**2026-11-04 拍板「100 移速=走完一米要 1 秒」**：客户端旧播放器
        /// 移动补间/途经采集节拍消费（0.18→1.0 对齐新轨 1 格/秒——旧 0.18s/格≈5.5 米/秒观感被否）；
        /// 即时制新轨移动=RTSMovementSystem 厘米速度累积器（100cm/s），不读本值——本值只保证客户端
        /// 补间时长≈逻辑移动时长（格数×1 秒）视觉一致</summary>
        public const float MoveStepSeconds = 1.0f;

        /// <summary>箭矢视觉飞行高度（格=世界单位，格面起算；默认 0.45）——纯视觉参数（判定圆柱与世界
        /// 高度无关）；per-skill 覆写=时轮 clip.projectileHeight（对齐动作片弓的松弦位置，2026-10-05）</summary>
        public const float ArrowFlightHeight = 0.45f;

        /// <summary>多层 Buff 逐层效果错峰间隔（秒，2026-10-01 拍板「每层的效果延后 0.15s 生效，
        /// 避免同时弹出」——寒冰之棱/歌声之环多层 tick 各层独立弹数字；第 i 层时刻=i×此值，
        /// 与箭雨段间隔同节拍语言；Host 状态恒即时结算，错峰纯表现层——客户端 Damage/Heal
        /// launchMs 到点再弹已支持，零客户端改动）</summary>
        public const float BuffLayerStaggerSeconds = 0.15f;

        /// <summary>单位受击/碰撞圆柱直径（世界单位=格；底座圆盘可视化同源，视觉即判定——docs/18 决策二）。
        /// **决策九十五（蓝图 §3.2）**：碰撞圆=受击圆柱统一 0.5 格（「体积=1/4 格」拍板释义——
        /// 1 格 2×2 容 4 个单位；per-unit 覆写保留、协议核心 0.8 不变；旧基线 0.42 已换代，
        /// 底座圆盘视觉随常量自动放大）</summary>
        public const float UnitCylinderDiameter = 0.5f;

        /// <summary>眷属（1~2★）碰撞/受击圆柱直径（2026-11-09 用户拍板「体积也从 50 改为 35」）：
        /// 与立牌缩到 70% 的视觉体量配套——眷属要「一眼看出比伙伴低廉」，挤在一起也用更小的圆；
        /// 未显式配 受击圆柱直径 的眷属统一取本值（显式值仍优先，协议核心 0.8 不受影响）。
        /// 消费方=碰撞圆半径（移动/弹性分离）、投射物与线段命中判定、部署重叠校验、AI 驻位接触钳、
        /// 底座圆盘与选中弧光视觉——全部经 CylinderDiameterOf 三重载，改这一处即全链生效</summary>
        public const float FamiliarCylinderDiameter = 0.35f;

        /// <summary>眷属立牌整体缩放（2026-11-09 用户拍板「眷属单位要缩小为 70%」）：
        /// 与伙伴/魔神拉开体量差（配合头顶条整体 0.7 缩放 + 无刻度，见 BattleOverheadBars.眷属条整体缩放）。
        /// 只乘在立牌显示高（UnitData.额外缩放）之上，不影响判定圆柱（圆柱走 FamiliarCylinderDiameter）</summary>
        public const float FamiliarVisualScale = 0.7f;

        /// <summary>per-unit 受击圆柱直径单出口（协议核心批 2026-09-29）：声明值>0 用声明值（协议核心=0.8，
        /// 大目标易命中=攻城手感），0=回落全局 0.5（眷属回落 0.35）。四消费方同源：ProjectileResolver 接触判定
        /// （Host 权威）、EffectCompiler.WouldHitProjectile 命中预判（推荐色口径）、UnitView 底座圆盘视觉
        /// （视觉即判定）、OrbitBeamsWorld 选中弧光贴紧折算</summary>
        public static float CylinderDiameterOf(UnitState state)
            => state != null && state.cylinderDiameter > 0f ? state.cylinderDiameter : UnitCylinderDiameter;

        /// <summary>per-unit 受击圆柱直径——活体 Unit 直读重载（P3：取代 RTSMovementSystem 旧
        /// UnitState 哑实例传参〔P2 断裂面〕；UnitState 版消费方照旧）。未显式配置时按层级回落：
        /// 眷属 1~2★=FamiliarCylinderDiameter、其余=UnitCylinderDiameter（2026-11-09 眷属批）</summary>
        public static float CylinderDiameterOf(Unit unit)
        {
            if (unit == null || unit.RawData == null) return UnitCylinderDiameter;
            if (unit.RawData.受击圆柱直径 > 0f) return unit.RawData.受击圆柱直径;
            return IsFamiliarLowCost(unit.RawData.unitType, unit.RawData.starLevel)
                ? FamiliarCylinderDiameter : UnitCylinderDiameter;
        }

        /// <summary>per-unit 受击圆柱直径——UnitData 直读重载（P3 部署自由落点：部署单位尚未生成，
        /// 从配置直读；三重载同口径=声明值优先、眷属回落 0.35、其余回落全局 0.5）</summary>
        public static float CylinderDiameterOf(GIC.Data.UnitConfig.UnitData data)
        {
            if (data == null) return UnitCylinderDiameter;
            if (data.受击圆柱直径 > 0f) return data.受击圆柱直径;
            return IsFamiliarLowCost(data.unitType, data.starLevel)
                ? FamiliarCylinderDiameter : UnitCylinderDiameter;
        }

        /// <summary>「廉价小兵」口径（2026-11-09 眷属批）：1~2★ 且**非建筑**——建筑不参与减小
        /// （西风旗帜/猎鹿人餐馆等低星建筑保持 0.5 圆柱与全尺寸立牌）</summary>
        private static bool IsFamiliarLowCost(GIC.Data.UnitType unitType, int starLevel)
            => unitType != GIC.Data.UnitType.Building
               && GIC.Data.UnitTierHelper.FromStars(starLevel) == GIC.Data.UnitTier.Familiar;

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

        // （MoraGainPerTurn/StaminaGainPerTurn 每回合发放常量已删除——2026-11-09 残留清理批：
        //  零消费死常量，生产单源=MoraGainPerSecond/StaminaGainPerSecond，见秒拍 tick 机节）

        /// <summary>配额行动消耗体力（docs/05 §5.1：移动/战技/爆发各 10；
        /// 低级单位 1~2 星自主行动豁免，延奏/契约等特殊技能 0）</summary>
        public const int StaminaCostPerAction = 10;

        // ==================== 地脉维度与灵魂（决策九十一，2026-10-25） ====================

        /// <summary>灵魂每秒拍衰减百分比（占最大生命——10%×10 秒整好归零=「完整 10 秒复苏窗」；
        /// 末点截断：⌊10%×MaxHp⌋）</summary>
        public const int SoulDecayPercentPerSecond = 10;

        /// <summary>灵魂治疗效率（拍板「灵魂的治疗效率-50%」=绝对 50——协议核心 50 同款语义：
        /// 受疗者效率乘区单出口（EffectCompiler.ApplyHealEfficiency）对一切治疗自动减半；
        /// 角色/命座自有效率不继承（灵魂只继承基础属性））</summary>
        public const int SoulHealEfficiency = 50;

        /// <summary>地脉驻留理智代价（拍板「在地脉维度的单位（不包括灵魂），每回合结束减少3点理智」
        /// ——即时制直译=每秒拍 −3；地脉专属维度规则；当前地脉仅灵魂=预留自动生效，
        /// 胡桃/丽莎相位技能批上线即激活；霜月等未来维度的驻留规则另立常量勿复用本值）</summary>
        public const int LeyLineSanityCostPerSecond = 3;

        // ==================== 世界/画布 sortingOrder 层级表（2026-09-28 批6 收口：8 处散布字面量单源） ====================
        // 完整层序自下而上：水面/薄冰 -3（2026-10-17 迷雾批自 -2 下移）→ 迷雾面 -2（迷雾批新增）→ 底座盘 -1 →
        // 瞄准贴片 0（隐式默认，无显式设置点）→ 底座弧光 1 → 立牌 10 → Buff 徽章 11 → 箭矢 12 →
        // 〔水面走 shader renderQueue 2999，另一体系（docs/14 §89⑥）〕→ 头顶条 Overlay 画布 38 →
        // 伤害数字 Overlay 画布 39 → 战斗 HUD 画布 40（BattleHud.prefab 序列化值）。
        // 插新层只改这里；消费方引用常量勿再写字面量。

        /// <summary>焊接水面/薄冰面板（透明最低层——迷雾批 2026-10-17 自 -2 下移一格：雾面（-2）须恒罩
        /// 水面/冰面（未探索水格被雾压暗），雾面又在底座盘（-1，仅出现在已探索格）之下；盘与雾面
        /// 同格互斥（单位所在格恒已探索）不同格屏幕不叠=层序安全）</summary>
        public const int WaterSurfaceSortingOrder = -3;

        /// <summary>摩拉堆表现件（docs/47 追拍 2026-10-27「摩拉堆直接用摩拉素材」：官方 mora.png 立式
        /// 小片堆=透明件须显式排序——与水面/薄冰同层 -3：合法同序形态=同格互斥〔'$' 布局字符只落
        /// 草地格、水面格无堆〕+被雾面（-2）恒罩=未探索格自动雾暗〔透明序通则：雾面后画恒盖〕）</summary>
        public const int MoraPileSortingOrder = -3;

        /// <summary>岸线过渡带（2026-11-10 画面增强批 A 组：草地↔水面/薄冰的湿润边+泡沫线）——
        /// 带体落在**陆地格顶面**（高于水面 0.36）但层位与水面同 -3：合法同序形态（水陆互斥不叠）、
        /// 且恒在雾面（-2）之下=未探索格被雾压暗（透明序通则：雾面后画恒盖）</summary>
        public const int ShoreTransitionSortingOrder = -3;

        /// <summary>格线层（2026-11-10 画面增强批 A 组：非操作期极弱、瞄准/拖动/悬停提亮）——
        /// 贴地细线同层 -3；刻意低于底座盘（-1）与瞄准贴片（0）：格线是地形辅助件，不得压过任何
        /// 交互件，也不参与「谁盖谁」的竞争</summary>
        public const int GridLineSortingOrder = -3;

        /// <summary>地面贴花（碎石/泥土；cutout 写深度=排序由 Z-buffer 承担，层位仅作查层定位）</summary>
        public const int GroundDecalSortingOrder = -3;

        /// <summary>对战迷雾面（2026-10-17 迷雾批：半透明暗雾盖未探索格——水面 -3 之上、底座盘 -1 之下）</summary>
        public const int FogOverlaySortingOrder = -2;

        /// <summary>单位底座圆盘（恒先画于一切透明件——透明盘投影感，2026-09-27 拍板）</summary>
        public const int BaseDiscSortingOrder = -1;

        /// <summary>底座环绕弧光（选中特效；瞄准贴片之上、立牌之下）</summary>
        public const int DiscOrbitSortingOrder = 1;

        /// <summary>瞄准层：方向箭头带/瞄准格贴片（2026-10-15 四轮拍板显式化——旧默认 0）</summary>
        public const int AimOverlaySortingOrder = 0;

        /// <summary>瞄准层叠加件：方向箭头头（2026-10-15 四轮拍板「渲染顺序统一解」：同序透明件按
        /// 包围盒中心距相机远近排序——贴地长带与叠加件同序时**方向依赖必翻车**〔下方向带中心比
        /// 第一格的头更近相机=头被带盖住，0.001 微抬敌不过整段长带的中心偏移〕；**贴地叠加件一律
        /// 显式分层排序号**，头恒在带之上〔槽位 2=弧光 1 与立牌 10 之间全层互异，零距离排序依赖〕）</summary>
        public const int AimOverlayTopSortingOrder = 2;

        /// <summary>瞄准范围提示整块（2026-10-15 拍板「额外加上范围提示……完整的一块」：自施放光环
        /// 瞄准时 (2R+1)² 格单 quad）。**层值=-1 与底座盘同序**：块面=贴片平面（高 0.03）恒高于盘面
        /// （0.02），同序按相机距离排序时块恒先画盘后画（俯角恒为正、相机永不越地平线=顺序稳定，
        /// 与 §154④ 的「不同中心同序」方向依赖翻车不同——此处同中心+Y 差恒定）；块又恒在待定金格
        /// 贴片（0）之下=中心格待定金恒可见</summary>
        public const int AimRadiusBlockSortingOrder = -1;

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

        // P6 残留审计 D2（docs/active/49）：SegmentAckTimeoutSeconds 随 ack 门控退役删除。

        // ==================== 段播放 pacing 契约（2026-10-20 审查批#10 收口） ====================
        // Host 只随 Segment 下发攻速差值等演算事实，下列节拍换算全在客户端 BattlePlayer——
        // 常量收口此处=两端契约单源（原为客户端内联字面量，改战斗节奏时 Host 侧无从感知）

        /// <summary>攻速片片头延迟换算除子：延迟=(全场最高攻速−本片攻速)÷本值 秒
        /// （攻速每差 10 点慢 1 秒先手窗，docs/04 §4.1）</summary>
        public const float AttackSpeedSliceLeadInDivisor = 10f;

        /// <summary>部署段/即时行动块的固定短节拍（秒，不按攻速排程）</summary>
        public const float DeploySegmentBeatSeconds = 0.15f;

        /// <summary>段内命令 stagger 间隔（秒）——同段多命令逐条错峰弹出的节拍
        /// （2026-10-23 拍板 0.12→0.05：回合末级联在 B1 出槽后仍嫌弹字拖沓，再压错峰密度）</summary>
        public const float CommandStaggerSeconds = 0.05f;

        /// <summary>秒拍结束段固定节拍（秒，不按攻速排程）</summary>
        public const float SecondBeatDelaySeconds = 0.25f;

        // （MaxTileVolume 单格体积上限已删除——2026-11-09 残留清理批：随 MovementResolver 旧轨
        //  一并退役（docs/active/49 B1/B2），密度由碰撞圆自然限制）

        // ==================== 连续坐标与碰撞圆（决策九十五 P2，蓝图 §3） ====================
        // 1 格=1 米=100 厘米=1 世界单位（CellSizeCm 常量真源=GIC.Data.CellMetrics，BattleCell 同址）；
        // 单位连续位置=厘米定点整数（确定性纪律）；碰撞圆=受击圆柱统一 0.5 格（CylinderDiameterOf 同源）。

        /// <summary>默认移速基线（厘米/秒）——2026-11-07 拍板「默认 50」（0.5 格/s；原 P3 全员 100 基线
        /// 作废）。与 UnitConfig.GetEffectiveMoveSpeed 的资产回落值必须同值，单源语义见那里。</summary>
        public const int DefaultMoveSpeedCm = 50;

        /// <summary>多层 Buff 第 i 层拍时刻（秒，相对段播放起点）——逐层错峰单源
        /// （第 i 层=i×BuffLayerStaggerSeconds；消费方=光环 Buff OnEffectBeat 声明 PendingAuraHit/Heal/
        /// Energy 的 HitSeconds 与 BattleResolver 碎裂回血错峰，勿再散抄公式）</summary>
        public static float LayerBeatSeconds(int layer) => layer * BuffLayerStaggerSeconds;

        // ==================== 即时制 tick 机（决策九十五，docs/active/48 §2） ====================
        // 20Hz 固定步长；accumulator 累加器模式（Gaffer《Fix Your Timestep!》业界标准）+每帧步数 clamp
        // （spiral of death 防护——模拟跟不上时整体慢放不雪崩）；1 秒=20 tick=「每秒整拍」结算节拍。

        /// <summary>逻辑帧率（tick/秒）——1 tick=1/20 秒（RTS 业界区间 20~30Hz，蓝图 §2.1）</summary>
        public const int TicksPerSecond = 20;

        /// <summary>单 tick 时长（秒）=1/TicksPerSecond</summary>
        public const float TickSeconds = 1f / TicksPerSecond;

        /// <summary>单帧最大 tick 步数（spiral of death 防护——帧率暴跌时逻辑整体慢放，不欠债雪崩）</summary>
        public const int MaxTicksPerFrame = 20;

        /// <summary>累加器单帧时间钳制（秒——Gaffer 标准护栏，防切后台巨帧一次吞掉模拟）</summary>
        public const float MaxFrameDeltaSeconds = 0.25f;

        /// <summary>AI 决策节拍（tick 数）=每 0.5 秒三脑同频一次（蓝图 §6）</summary>
        public const int AiBeatTicks = 10;

        /// <summary>每秒整拍发放摩拉（决策九十五拍板 4「每秒整拍」——回合制 +10/回合 缩 1/10 节奏）</summary>
        public const int MoraGainPerSecond = 1;

        /// <summary>每秒整拍发放体力（同上）</summary>
        public const int StaminaGainPerSecond = 1;

        /// <summary>战斗时钟步进（分钟/秒——昼夜每秒 +1 分钟，回合数显示退役，蓝图 §2.3）</summary>
        public const int BattleClockMinutesPerSecond = 1;

        /// <summary>desync hash 落盘节拍（秒——确定性护栏四件套之一：state hash 落 BattleRecorder 行，
        /// 后置 LAN 联机的漂移定位手段，王者荣耀同款）</summary>
        public const int DesyncHashEverySeconds = 1;

        // ==================== 帧同步输入管线（F1，docs/active/50 §4.3） ====================

        /// <summary>网络权威窗口（tick）——客户端提交后 targetTick=本地 tick+本值；**只决定权威确认
        /// 何时到，不决定手感**（感知延迟由 F3 本地预测归零，MOBA 红线 ≤1 帧）。LAN 基线 2 tick=100ms。</summary>
        public const int InputDelayTicks = 2;

        /// <summary>迟到输入最大容忍（tick）——超过则丢弃（Host 重定基后仍过期）。</summary>
        public const int MaxInputLatenessTicks = 10;

        /// <summary>客户端 hash 上报节拍（tick）——每 20 tick=1 秒，与 Host 比对。</summary>
        public const int HashReportIntervalTicks = 1 * TicksPerSecond;

        /// <summary>效果拍节拍（tick 数——2026-11-04 拍板「效果触发时间翻倍，现在太频繁」：Buff tick
        /// 效果〔歌声之环/冰棱/燃烧等 OnEffectBeat 产出〕+建筑效果〔教堂治疗/旗帜元能/餐馆摩拉/骑士团
        /// 引擎〕自每秒整拍改**每 2 秒一拍**——单次数值不动（DPS/产出随之减半，属拍板预期，嫌伤害低
        /// 再单独调数值）；**战技/爆发 CD 不受影响**（3s/5s 拍板保持）；Buff 持续计时仍按秒（Duration
        /// 语义不变）；资源发放/泡湿/站立采集保持每秒整拍（经济与判定窗口不随本拍板变动）；
        /// P6 调参旋钮=本常量）</summary>
        public const int EffectBeatTicks = 2 * TicksPerSecond;

        // ==================== 攻击冷却与释放速度（决策九十五 P3，蓝图 §4.3——战技=MOBA 普攻地位） ====================

        /// <summary>战技攻击 CD（秒——蓝图拍板：战技+AI 普攻条目〔Normal 型〕同享 3 秒 CD；
        /// Normal 无 CD 会被 AI 每 0.5 秒连放，CD=即时制攻击节流主闸；per-skill 覆写后续按需）</summary>
        public const int CasterCooldownSeconds = 3;

        /// <summary>爆发 CD（秒——5 秒+元能门槛双 gate；costs 条目〔安柏 30/凯亚 40/芭芭拉 100〕不变）</summary>
        public const int BurstCooldownSeconds = 5;

        /// <summary>战技 CD（tick）=CasterCooldownSeconds×TicksPerSecond</summary>
        public const int CasterCooldownTicks = CasterCooldownSeconds * TicksPerSecond;

        /// <summary>爆发 CD（tick）=BurstCooldownSeconds×TicksPerSecond</summary>
        public const int BurstCooldownTicks = BurstCooldownSeconds * TicksPerSecond;

        /// <summary>释放速度千分基线（蓝图 §4.3「释放速度=1+攻速/100」：攻速全员 0〔资产迁移 40→0〕
        /// →恒 1；时轮 clip 发射/命中时刻与动作片播放速率同乘该倍率——攻速词条接线后自动生效；
        /// 千分定点协议=per-mille〔1.0=1000〕，整数确定性纪律）</summary>
        public const int ReleaseSpeedPerMilleBase = 1000;

        /// <summary>释放速度（千分定点：1+攻速/100，攻速≤−100 钳 0.5〔500〕防负速率反转）——
        /// 单源：Host 编译（时轮时刻÷率）与客户端动作片播放（速率×率）共用</summary>
        public static int ReleaseSpeedPerMille(int attackSpeed)
        {
            int perMille = ReleaseSpeedPerMilleBase + attackSpeed * 10;
            return perMille >= 500 ? perMille : 500;
        }
    }

    /// <summary>
    /// 投射物规则常量（docs/05 §5.3 统一拍板 + docs/18 决策二"投放形态由技能数据驱动"；
    /// 原寄居 AmberDoubleShotSkill.cs，B-1 技能类退役迁入常量收口处。
    /// **决策九十五 P3**：MaxRange 单位=厘米（旧 24 格→2400=蓝图 §10「默认射程回落 24 格→2400」
    /// 直译；时轮 clip.maxRange 同批资产迁移为 cm 值））
    /// </summary>
    public static class ProjectileRule
    {
        /// <summary>直线型弹射物飞行上限（厘米=2400；时轮 clip.maxRange=0 回落此默认）</summary>
        public const int MaxRange = 2400;

        /// <summary>投放形态：直线飞行投射物（客户端播箭矢）</summary>
        public const int LineDelivery = 1;
    }
}
