using System;
using System.Collections.Generic;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// AI 决策共享工具（B6b；2026-09-25 v2 强化；D 批次术语迁移）：配额脑（PlayerQuotaBrain）与
    /// 眷属脑（FamiliarBrain）、伙伴脑（CompanionBrain）共用的敌人查找/距离/技能查询/命中预判
    /// 启发式原语。全部纯读 BattleSimState/BattleSnapshot，不改状态（docs/18 决策一）。
    /// **P5 连续空间移植（决策九十五，蓝图 §6）**：格固有原语全退役——十字 BFS（FindApproachFirstStep/
    /// FindApproachStraightSteps）、距离场（BuildApproachField）、主轴逼近（BestCrossApproachDirection）、
    /// 格步进线预判（PreviewLineTargets）、轴对齐/换线族（消费方 CompanionBrain 内随批退役）→
    /// **tile A*（RTSPathfinder）+目标点驻位（StandoffTargetCm）+向量瞄准预判（PredictAimTargets，线段×圆）**；
    /// 距离度量=欧氏厘米（蓝图 §3.1 切比雪夫→欧氏全链迁移）；AI 移动=「选目标点」（targetX/targetZ 直交，
    /// 执行层 RTSMovementSystem A*+steering）；瞄准=自由角千分向量（aimX/aimZ——旧「十字方向其一」
    /// 方向纪律随八方向限制退役，docs/18 决策八历史注记）。
    /// </summary>
    public static class BattleHeuristics
    {
        /// <summary>十字四向（P5 注记：AI 移动/瞄准方向域已退役——本表仅剩 DeployUnitExecutor
        /// 召唤落格 BFS 的展开序消费〔执行层非脑层〕；枚举值序遍历=决策确定性语义保持）</summary>
        public static readonly Direction2D[] CrossDirections =
        {
            Direction2D.Right,
            Direction2D.Left,
            Direction2D.Up,
            Direction2D.Down,
        };

        // ==================== 单位分拣 ====================

        /// <summary>单位层级（D 批次操控分层，docs/32 §2）：星级→UnitTier 单一换算出口，
        /// 全部层级门控/豁免/分拣逻辑只看此枚举——禁止散落星级区间判断。
        /// TierOverrideStars 覆盖（-1=无）：试招沙盒把编辑单位置 5=魔神档玩家全手操（2026-10-05）</summary>
        public static UnitTier TierOf(Unit unit)
        {
            if (unit == null) return UnitTier.Familiar;
            if (unit.TierOverrideStars >= 0) return UnitTierHelper.FromStars(unit.TierOverrideStars);
            return unit.RawData != null
                ? UnitTierHelper.FromStars(unit.RawData.starLevel)
                : UnitTier.Familiar; // 无配置数据按最低档处理（不参与任何玩家域/消耗扣减）
        }

        /// <summary>是否眷属（1~2 星：AI 自主小兵，启发式脑决策、玩家资源全豁免）</summary>
        public static bool IsFamiliar(Unit unit) => TierOf(unit) == UnitTier.Familiar;

        /// <summary>是否伙伴（3~4 星：AI 自主+可被号令——评分制脑决策，玩家资源消耗按伙伴档）</summary>
        public static bool IsCompanion(Unit unit) => TierOf(unit) == UnitTier.Companion;

        /// <summary>是否魔神（5 星：玩家全手操，无 AI 兜底——不操=站桩）</summary>
        public static bool IsArchon(Unit unit) => TierOf(unit) == UnitTier.Archon;

        /// <summary>是否建筑（协议核心批 2026-09-29 拍板「建筑不参与任何行动」——单一判据 unitType，
        /// 与星级/层级无关：协议核心是 5★ 勿落进魔神操档，未来低星建筑也勿落眷属/伙伴档）。
        /// 三脑分拣、玩家上交校验、即时通道、技能盘全按此排除</summary>
        public static bool IsBuilding(Unit unit)
            => unit != null && unit.RawData != null && unit.RawData.unitType == UnitType.Building;

        /// <summary>是否核心（2026-10-13 拍板「核心身份 tag 化」：所有持 核心 标签的单位=胜负判据
        /// 单位——**同一玩家全部核心单位被击倒才判战败**；眷属/伙伴攻城目标、AI 守护与终局判据、
        /// 核心摧毁记账全按此识别——未来世界树等新核心单位=UnitConfig 配 [核心] tag 零代码加入；
        /// 与「建筑不参与行动」的 IsBuilding 判据正交（核心必是建筑、建筑未必是核心）</summary>
        public static bool IsCore(Unit unit)
            => unit?.RawData?.tags != null
               && System.Array.IndexOf(unit.RawData.tags, GIC.Data.UnitTag.Core) >= 0;

        /// <summary>是否消散单位（决策八十六批B，docs/45 §4 拍板 #5「建筑被摧毁与眷属死亡
        /// 不留尸体」）：建筑全部（含协议核心——核心摧毁由胜负判据接管）+ 眷属层级角色（1~2★：
        /// 丘丘人/西风骑士）。死亡后走移除链（Buff 注销+Units 移除+GO 销毁）而非转尸体——
        /// 「死亡不清理」通则对消散单位作废；伙伴/魔神尸体维持。单一判据源，勿散写</summary>
        public static bool ShouldDissolve(Unit unit)
            => IsBuilding(unit)
               || (unit != null && unit.RawData != null
                   && unit.RawData.unitType == UnitType.Character && IsFamiliar(unit));

        /// <summary>是否采集型单位（docs/47 拍板⑦，2026-10-27「有特别采集能力或有采集tag的角色，
        /// 相比于攻击敌人，更主动采集资源点」——双判据任一即采集型）：①UnitConfig.tags 含
        /// Gathering（采集 tag——安柏等身份标识）；②任一移动技能 CollectCount&gt;0（安柏飞行冠军
        /// 「沿途采集」——特别采集能力）。数据驱动：将来自创采集单位配 tag 或给移动技能配参数
        /// 即自动进入采集档，零代码。消费=CompanionBrain 采集档（眷属脑同构判据预留——
        /// 现役眷属零采集型=零候选零行为变化）</summary>
        public static bool IsHarvester(Unit unit)
        {
            if (unit?.RawData?.tags != null
                && System.Array.IndexOf(unit.RawData.tags, GIC.Data.UnitTag.Gathering) >= 0)
                return true;
            return MoveExecutor.CollectParamsOf(unit).count > 0;
        }

        // ==================== 敌人查找 ====================

        /// <summary>决策单位对敌单位的战术感知（2026-10-23 实战报障「敌方凯亚无视距离更近的
        /// 我方凯亚、直奔协议核心」修复）：=队伍迷雾可见（IsUnitVisibleTo 单出口——探索账本/核心
        /// 透视/视野暴露）**或**目标处于自身攻击视野（GetEffectiveAttackVision，决策三十一既定
        /// 「追击感知半径」）切比雪夫半径内。Why：迷雾批把 AI 感知唯一入口收在探索账本（探索视野
        /// 仅 1~2 格），低于决策三十一眷属 v7「攻击视野内才追」与伙伴 J-4「自身攻击视野内无普通敌
        /// 才推核心」的既定语义——雾局下近处敌（2~5 格、所在格未探索）对 AI 完全不存在=J-4 每回合
        /// 判「无视野敌」直奔核心、v7 追击域被账本架空（2026-10-23 观察局实锤：d=3 敌不在锚集）。
        /// 雾只压**战略情报**（远端敌情——威胁图/AIStrategist/OpponentProfiler 仍走 IsUnitVisibleTo
        /// 团队账本口径，勿混用本口），不夺单位自己的武器视距。敌我/存活过滤由调用方负责（本判定
        /// 只管「看得见」）；本口为 Host 决策侧专用，结算域禁入</summary>
        public static bool PerceivesEnemy(BattleSimState sim, Unit self, Unit enemy)
        {
            if (sim == null || self == null || enemy == null) return false;
            var identity = self.GetUnitComponent<UnitIdentity>();
            if (identity == null) return false;
            return sim.IsUnitVisibleTo(identity.Team, enemy) || IsEnemyInTacticalSight(sim, self, enemy);
        }

        /// <summary>目标处于自身攻击视野（P5：欧氏 cm ≤ GetEffectiveAttackVision——资产值已迁厘米，
        /// Unspecified 回落 500=5 格；与眷属 v7 追击域/J-4/H-3 视野敌判同口径）内——
        /// 「武器够得着的敌情自己看得见」</summary>
        public static bool IsEnemyInTacticalSight(BattleSimState sim, Unit self, Unit enemy)
        {
            return DistanceCm(sim, enemy, self) <= TacticalSightOf(self);
        }

        /// <summary>战术视距单源（2026-10-25 复审批 T5 下沉：攻击视野回落此前在眷属脑/伙伴脑多处
        /// 手抄——「Unspecified 回落」语义一处维护；与 IsEnemyInTacticalSight/J-4/H-3 同口径。
        /// P5 视野 cm 化：**值域=厘米**（回落 500=5 格、安柏 800=8 格）</summary>
        public static int TacticalSightOf(Unit self)
            => self != null && self.RawData != null ? self.RawData.GetEffectiveAttackVision() : 500;

        /// <summary>可见敌集单源（2026-10-25 复审批 T5 下沉：CompanionBrain 敌集枚举三连抄收口）——
        /// 敌我（TeamType）/存活/PerceivesEnemy（雾账本+战术视距）过滤后按 (id, 单位) 返回；
        /// 距离/光环/视野内筛选由调用方按需附加（NearestEnemyDistance/EnemiesInAuraAt/
        /// CollectVisionEnemies 三消费方同口径，勿再手抄循环）</summary>
        public static List<(string id, Unit enemy)> CollectVisibleEnemies(BattleSimState sim, TeamType myTeam, Unit self)
        {
            var result = new List<(string, Unit)>();
            if (sim == null) return result;
            foreach (var kv in sim.SortedUnits())
            {
                var enemy = kv.Value;
                if (enemy == null) continue;
                var identity = enemy.GetUnitComponent<UnitIdentity>();
                if (identity == null || identity.Team == myTeam) continue;
                if (BattleSimState.IsDead(enemy)) continue;
                if (!PerceivesEnemy(sim, self, enemy)) continue;
                result.Add((kv.Key, enemy));
            }
            return result;
        }

        /// <summary>全部存活敌方单位按欧氏 cm 距离升序（等距平局 unitId 升序=枚举序铁律；P5 度量迁移：
        /// 切比雪夫格→欧氏厘米，蓝图 §3.1）——
        /// 低级单位换目标巡逻用（2026-09-26 拍板「当自己的任何攻击都无法打到时，换目标巡逻」：
        /// 逐敌试逼近步，贴身打不了/不可达的自动换下一个）；纯读不改状态。
        /// **AI 迷雾收口（2026-10-17 追拍三「AI 也受雾限」）**：雾内敌方不入列（敌方核心恒可见=
        /// 攻城锚零回归）——眷属目标序/伙伴敌锚/FindAttackAim 三消费方一处收口；结算域勿走此口。
        /// **战术视距（2026-10-23 报障修复）**：判定走 PerceivesEnemy（雾账本可见或自身攻击视野内）
        /// ——探索账本未覆盖的近处敌（≤自身攻击视野）照常入列，v7/J-4 语义在雾局恢复成立</summary>
        public static List<Unit> FindEnemiesByDistance(BattleSimState sim, Unit self)
        {
            var selfId = self.GetUnitComponent<UnitIdentity>();
            var result = new List<Unit>();
            if (selfId == null) return result;

            foreach (var kv in sim.SortedUnits())
            {
                var unit = kv.Value;
                var id = unit.GetUnitComponent<UnitIdentity>();
                if (id == null || id.Team == selfId.Team) continue; // 敌我=TeamType 口径（2026-09-25 三轮审查 C2）
                if (BattleSimState.IsDead(unit)) continue;
                if (!PerceivesEnemy(sim, self, unit)) continue; // AI 雾限（追拍三）+战术视距（2026-10-23）：账本外近敌（≤自身攻击视野）照常入列
                result.Add(unit);
            }
            var distOf = new Dictionary<Unit, int>();
            foreach (var unit in result)
                distOf[unit] = DistanceCm(sim, unit, self); // 欧氏 cm 单源（P5 迁移）；顺删零消费的 posOf（2026-10-02 复审）与零消费的 FindNearestEnemy（P5 退役）
            result.Sort((a, b) =>
            {
                int da = distOf[a], db = distOf[b];
                if (da != db) return da.CompareTo(db);
                sim.TryGetUnitId(a, out var ia);
                sim.TryGetUnitId(b, out var ib);
                return string.CompareOrdinal(ia, ib); // 等距平局：unitId 升序（枚举序铁律，确定性）
            });
            return result;
        }

        // ==================== 连续空间原语与技能查询（P5：方向与技能段头——方向域已退役） ====================
        // ==================== 连续空间原语（决策九十五 P5，蓝图 §6：tile A* + 向量瞄准 + 驻位点） ====================

        /// <summary>两单位欧氏距离（厘米，整数截断——牛顿迭代平方根，零浮点纪律与
        /// RTSMovementSystem.SqrtI 同式；评分域换算格=÷CellMetrics.CellSizeCm——AITuning
        /// 「每格 X 分」评分系数保持格语义不漂移）。a/b 缺失=int.MaxValue（判缺席域勿 NaN）</summary>
        public static int DistanceCm(BattleSimState sim, Unit a, Unit b)
        {
            if (sim == null || a == null || b == null) return int.MaxValue;
            var pa = sim.GetPosCm(a);
            var pb = sim.GetPosCm(b);
            return DistCm(pa.x, pa.y, pb.x, pb.y);
        }

        /// <summary>两点欧氏距离（厘米）——点位域消费方（支援站位评分/采集距离/驻位点）单源</summary>
        public static int DistCm(int axCm, int azCm, int bxCm, int bzCm)
        {
            long dx = bxCm - axCm;
            long dz = bzCm - azCm;
            return SqrtI(dx * dx + dz * dz);
        }

        /// <summary>整数平方根（牛顿迭代收敛后与平台无关——sim 决策域零浮点纪律）</summary>
        private static int SqrtI(long value)
        {
            if (value <= 0) return 0;
            int x = (int)Math.Sqrt(value); // 初值仅初始化（浮点不进结果）
            while ((long)(x + 1) * (x + 1) <= value) x++;
            while ((long)x * x > value) x--;
            return x;
        }

        /// <summary>单位碰撞圆半径（cm）——受击圆柱直径同源（BattleMetrics.CylinderDiameterOf 格值×100，
        /// per-unit 覆写〔协议核心 0.8〕+全局 0.5；RTSMovementSystem.RadiusCmOf 同式——驻位点接触钳消费）</summary>
        public static int UnitRadiusCm(Unit unit)
            => (int)Math.Round(BattleMetrics.CylinderDiameterOf(unit) * CellMetrics.CellSizeCm) / 2;

        /// <summary>单位攻击射程（厘米，P5 取代格桥 AttackRangeOf）：全部可施放方向攻击技能
        /// （Normal/Burst、非单位指向/自施放、伤害>0、消耗门槛过）能命中的最远 cm——
        /// DirectionRangeCmOf 直读（不经 RangeCellsOf 格桥整除，150cm 类值不再抹尾）。
        /// 无可施放方向攻击技能=0（驻位距离回落 0=贴身逼近）。纯读不查命中线——有射程≠有线
        ///（无线时攻击档自然不产候选，勿在此混入线判定）</summary>
        public static int AttackRangeCmOf(BattleSimState sim, Unit unit, string playerId)
        {
            int best = 0;
            var skills = unit?.Skills;
            if (skills == null) return 0;
            for (int i = 0; i < skills.Count; i++)
            {
                var skill = skills[i];
                var data = skill?.RawData;
                if (data == null) continue;
                if (data.skillType != SkillType.Normal && data.skillType != SkillType.Burst) continue;
                if (data.IsUnitTargeted() || data.IsSelfCast()) continue;
                if (!skill.CanCast(unit)) continue;
                if (!ResourceGate.HasAll(sim, unit, playerId, data.costs, out _)) continue;
                if (EstimatePerTargetDamage(unit, data) <= 0) continue;
                int range = EffectCompiler.DirectionRangeCmOf(data.timeline);
                if (range > best) best = range;
            }
            return best;
        }

        /// <summary>敌单位快照连续位（cm；posX/posZ 双零=未填态回落格心换算——快照文档口径）</summary>
        private static (int x, int z) EnemyPosCm(UnitState enemy)
        {
            if (enemy.posX != 0 || enemy.posZ != 0) return (enemy.posX, enemy.posZ);
            return ((int)((enemy.position.x + 0.5f) * CellMetrics.CellSizeCm),
                (int)((enemy.position.y + 0.5f) * CellMetrics.CellSizeCm));
        }

        /// <summary>技能对某瞄准向量将实际命中的存活敌（AI 评分/预判目标清单——P5 取代格步进
        /// PreviewLineTargets，蓝图 §4.1 线段×圆）：镜像 Host 结算形态——LineProjectile=射线×受击圆
        /// **cm 精确版**（敌圆心=快照 posX/posZ 而非格心——P4-C HUD 预判族是格域近似版，两族配对勿混），
        /// **首接触单位截停（含尸体——Host 同语义：纯尸体线=空表浪费行动不选）**，命中=接触点
        /// AoE 半径内（AoERadius 资产 cm；0=单体回落受击圆半径 25cm——P4-C 口径）存活+雾限敌；
        /// LineBurst=整线线段×受击圆（EffectCompiler.SegmentTouchesUnitCircle——Host 单源）。
        /// 虚空截断=沿向半格采样首 void 格近缘。存活+雾限过滤=AI 雾限口径（追拍三：雾限决策不限结算；
        /// **结算域 EffectCompiler 勿走此口**）</summary>
        public static List<UnitState> PredictAimTargets(BattleSimState sim, BattleSnapshot snapshot,
            TeamType casterTeam, Unit caster, SkillConfig.SkillData data, int aimX, int aimZ)
        {
            var result = new List<UnitState>();
            if (sim == null || snapshot == null || caster == null || data == null) return result;
            var v = SkillHitResolver.NormalizePerMille(aimX, aimZ);
            if (v.x == 0 && v.y == 0) v = new UnityEngine.Vector2Int(1000, 0); // 零向量兜底=东（与 AimVectorOf 同向）
            var from = sim.GetPosCm(caster);
            int dirX = v.x, dirZ = v.y; // 千分单位向量（分量绝对值 ≤1000）

            // 虚空截断（沿向半格=50cm 采样，与 EffectCompiler.PredictProjectileHits 同步长）
            // 判定轨单次查询（2026-11-06 后期卡顿修复）：原走 EffectCompiler.DirectionRangeCmOf——
            // 该函数内部各查一次 LineProjectile/LineBurst（每次 new List+排序），而本方法在 AI 决策里
            // 对「每个候选敌 × 每单位 × 每技能」调用一次=百单位级分配风暴（实测单拍 0.7s）；
            // 此处一次查询同时得 maxRange 与投影轨，取值优先级与 DirectionRangeCmOf 完全一致
            var projClips = SkillTimelineQuery.JudgmentClips(data.timeline, SkillJudgmentKind.LineProjectile);
            int maxRange;
            if (projClips.Count > 0 && projClips[0].maxRange > 0) maxRange = projClips[0].maxRange;
            else
            {
                var burstRangeClips = SkillTimelineQuery.JudgmentClips(data.timeline, SkillJudgmentKind.LineBurst);
                maxRange = burstRangeClips.Count > 0 && burstRangeClips[0].maxRange > 0
                    ? burstRangeClips[0].maxRange
                    : ProjectileRule.MaxRange;
            }
            int voidCut = -1;
            for (int t = 50; t <= maxRange; t += 50)
            {
                int px = from.x + dirX * t / 1000;
                int pz = from.y + dirZ * t / 1000;
                var cell = BattleCell.FromCm(px, pz);
                if (sim.Map == null || !sim.Map.HasTile(cell.x, cell.y)) { voidCut = t - 25; break; }
            }

            if (projClips.Count > 0)
            {
                // LineProjectile：首接触（含尸体）截停——逐敌求入射距离取最小者
                bool skillOverride = projClips[0].hitDiameter > 0f;
                int maxDist = voidCut >= 0 ? voidCut : maxRange + 50;
                int bestEntry = int.MaxValue;
                UnitState firstContact = null;
                foreach (var enemy in snapshot.MainWorldUnits())
                {
                    if ((TeamType)enemy.team == casterTeam) continue; // 含尸体（截停语义）；敌我=TeamType（C2）
                    int radiusCm = (int)Math.Round((skillOverride ? projClips[0].hitDiameter
                        : BattleMetrics.CylinderDiameterOf(enemy)) * CellMetrics.CellSizeCm) / 2;
                    var ep = EnemyPosCm(enemy);
                    long relX = ep.x - from.x, relZ = ep.z - from.y;
                    long relSq = relX * relX + relZ * relZ;
                    long rSq = (long)radiusCm * radiusCm;
                    int entry;
                    if (relSq <= rSq) entry = 0; // 发射即贴脸（同点堆叠）
                    else
                    {
                        long along = (relX * dirX + relZ * dirZ) / 1000; // cm（千分向量点积÷1000）
                        long perpSq = relSq - along * along;
                        if (perpSq > rSq) continue; // 射线不穿该圆
                        entry = (int)(along - SqrtI(rSq - perpSq));
                    }
                    if (entry >= 0 && entry <= maxDist && entry < bestEntry)
                    { bestEntry = entry; firstContact = enemy; }
                }
                if (firstContact == null) return result;
                // 命中集=接触点 AoE 半径内存活+雾限敌
                int aoeCm = data.GetInt(SkillParamKey.AoERadius, 0);
                if (aoeCm <= 0) aoeCm = 25;
                int hx = from.x + dirX * bestEntry / 1000;
                int hz = from.y + dirZ * bestEntry / 1000;
                foreach (var enemy in snapshot.MainWorldUnits())
                {
                    if ((TeamType)enemy.team == casterTeam) continue;
                    if (enemy.isCorpse != 0 || !sim.IsEnemyStateVisibleTo(casterTeam, enemy)) continue; // AI 雾限（追拍三）
                    var ep = EnemyPosCm(enemy);
                    long ddx = ep.x - hx, ddz = ep.z - hz;
                    if (ddx * ddx + ddz * ddz <= (long)aoeCm * aoeCm) result.Add(enemy);
                }
                return result;
            }

            // LineBurst：整线线段×受击圆——SegmentTouchesUnitCircle（Host CompileLineBurstSegment 单源）
            int maxDistBurst = voidCut >= 0 ? voidCut : maxRange;
            int endX = from.x + dirX * maxDistBurst / 1000;
            int endZ = from.y + dirZ * maxDistBurst / 1000;
            foreach (var enemy in snapshot.MainWorldUnits())
            {
                if ((TeamType)enemy.team == casterTeam) continue;
                if (enemy.isCorpse != 0 || !sim.IsEnemyStateVisibleTo(casterTeam, enemy)) continue;
                if (EffectCompiler.SegmentTouchesUnitCircle(from.x, from.y, endX, endZ, enemy)) result.Add(enemy);
            }
            return result;
        }

        /// <summary>首个可命中存活敌的瞄准向量（P5 取代十字向 FindAttackDirection——低级单位/共享
        /// 骨架攻击档的任意角瞄准口）：可见敌距离序逐敌试瞄（aim=自身→敌向量千分），
        /// PredictAimTargets 非空即命中——首个可命中敌即返回（旧「首个可命中十字向」语义的连续版；
        /// 确定性=FindEnemiesByDistance 距离+unitId 序，零 roll）。全部敌不可命中=false（aimX/aimZ=0）
        /// ——纯尸体线/全被虚空截断/全超射程均落空。targetsInto=命中清单（评分消费，调用方复用）</summary>
        public static bool FindAttackAim(BattleSimState sim, BattleSnapshot snapshot, Unit self,
            BaseSkill skill, out int aimX, out int aimZ, List<UnitState> targetsInto, List<Unit> enemiesIn = null)
        {
            aimX = 0; aimZ = 0;
            if (targetsInto != null) targetsInto.Clear();
            var identity = self.GetUnitComponent<UnitIdentity>();
            var data = skill?.RawData;
            if (identity == null || data == null) return false;
            var from = sim.GetPosCm(self);
            // 射程剪枝（2026-11-06 后期卡顿修复）：本技能最大命中距离（cm，单源）——圆心距已超出
            // 「射程 + 目标受击半径」的敌，首接触判定恒不成立（命中点必在圆心内侧 radius 且沿向
            // ≤ maxRange+50），继续跑 PredictAimTargets 纯浪费：后期百单位 × 双技能 × 可见敌数 =
            // 单拍 0.7s 的主项。判据保守留 50cm 裕量，行为与命中几何同口径
            int reachCm = EffectCompiler.DirectionRangeCmOf(data.timeline) + 50;
            // 可见敌集可由调用方传入复用（2026-11-06 后期卡顿修复）：眷属脑单次决策内爆发/战技/移动
            // 三处需要同一份「距离序可见敌」，旧实现各算一遍（每次新建 SortedUnits+全量距离+排序）
            foreach (var enemy in enemiesIn ?? FindEnemiesByDistance(sim, self))
            {
                var enemyPos = sim.GetPosCm(enemy);
                int dx = enemyPos.x - from.x;
                int dz = enemyPos.y - from.y;
                int dist = SqrtI((long)dx * dx + (long)dz * dz);
                if (dist > reachCm + UnitRadiusCm(enemy) + 50) continue; // 超射程：恒不可命中（见上）
                int vx, vz;
                if (dist <= 0) { vx = 1000; vz = 0; } // 同点堆叠：东兜底（与 AimVectorOf 同向）
                else { vx = dx * 1000 / dist; vz = dz * 1000 / dist; }
                var targets = PredictAimTargets(sim, snapshot, identity.Team, self, data, vx, vz);
                if (targets.Count == 0) continue;
                aimX = vx; aimZ = vz;
                if (targetsInto != null) { targetsInto.Clear(); targetsInto.AddRange(targets); }
                return true;
            }
            return false;
        }

        /// <summary>锚单位驻位目标点（cm，P5 移动档核心——「进攻击范围即停」CR-Move 的连续版）：
        /// 沿 anchor→self 方向、距 anchor 中心 standoffCm 处的点；standoff&lt;=0=anchor 本位（贴身接触，
        /// 执行层碰撞圆停于接触前）。**接触钳**：standoff 小于双方碰撞圆半径和+10cm 时钳到接触外——
        /// 驻位点不得站进碰撞域（目标格不可走时执行侧 RTSPathfinder 钳最近可走格，brain 不再预判格可走性）</summary>
        public static Vector2IntCm StandoffTargetCm(BattleSimState sim, Unit self, Unit anchor, int standoffCm)
        {
            var anchorPos = sim.GetPosCm(anchor);
            if (standoffCm <= 0) return new Vector2IntCm(anchorPos.x, anchorPos.y);
            var selfPos = sim.GetPosCm(self);
            int dx = selfPos.x - anchorPos.x;
            int dz = selfPos.y - anchorPos.y;
            int dist = SqrtI((long)dx * dx + (long)dz * dz);
            if (dist <= 0) return new Vector2IntCm(anchorPos.x, anchorPos.y); // 同点堆叠：锚本位
            int standoff = Math.Max(standoffCm, UnitRadiusCm(self) + UnitRadiusCm(anchor) + 10);
            return new Vector2IntCm(anchorPos.x + dx * standoff / dist,
                anchorPos.y + dz * standoff / dist);
        }

        /// <summary>驻位目标点（点位版——锚位非在场单位实体时消费：先锋外推追逐位/资源堆格心等）；
        /// 接触钳按通用受击圆 25cm+自身半径计（锚半径未知时保守），语义同 <see cref="StandoffTargetCm"/></summary>
        public static Vector2IntCm StandoffPointCm(BattleSimState sim, Unit self, int anchorXCm, int anchorZCm, int standoffCm)
        {
            if (standoffCm <= 0) return new Vector2IntCm(anchorXCm, anchorZCm);
            var selfPos = sim.GetPosCm(self);
            int dx = selfPos.x - anchorXCm;
            int dz = selfPos.y - anchorZCm;
            int dist = SqrtI((long)dx * dx + (long)dz * dz);
            if (dist <= 0) return new Vector2IntCm(anchorXCm, anchorZCm);
            int standoff = Math.Max(standoffCm, UnitRadiusCm(self) + 25 + 10);
            return new Vector2IntCm(anchorXCm + dx * standoff / dist, anchorZCm + dz * standoff / dist);
        }

        /// <summary>路径可达性预判（v8 可达性语义的连续版）：RTSPathfinder 从自身到目标点寻路——
        /// 不可达（被地形/尸体围死）=false，防「追一个永远打不到的目标傻站不攻城」。占用避让
        /// （旧 I-D 移动意图占位）随 BFS 退役——执行层 A* 尸体/地形硬挡+友军可穿，真阻挡由
        /// 碰撞收尾（被挡停+下拍重决策）自然吸收</summary>
        public static bool PathReachable(BattleSimState sim, Unit self, int toXCm, int toZCm)
        {
            if (sim == null || self == null) return false;
            var pos = sim.GetPosCm(self);
            if (pos.x == toXCm && pos.y == toZCm) return true; // 已到=可达
            var moveable = self.GetUnitComponent<UnitMoveable>();
            var forceType = moveable != null ? moveable.NormalMoveType : ForceType.Walk;
            var path = RTSPathfinder.FindPath(sim, pos.x, pos.y, toXCm, toZCm, forceType);
            // FindPath 不可达/钳回起点=「原地单点」——真路径=多于一点或末点异于起点
            return path != null && (path.Count > 1 || path[0].x != pos.x || path[0].z != pos.y);
        }


        /// <summary>单位技能实例列表中首个指定类型技能的索引（-1=无）——遍历 unit.Skills
        /// 实例（与 SkillExecutor.GetSkill 同源索引；RawData.skills 空槽不进实例表，勿混用）</summary>
        public static int FindSkillIndex(Unit unit, SkillType skillType)
        {
            var skills = unit?.Skills;
            if (skills == null) return -1;
            for (int i = 0; i < skills.Count; i++)
            {
                if (skills[i]?.RawData?.skillType == skillType) return i;
            }
            return -1;
        }

        /// <summary>
        /// 技能对单个目标的预估伤害（AI 评分用；口径=Damage×DamageCount 按基准换算——
        /// BasedOnMaxHealth=施法者最大生命（芭芭拉水之浅唱类，与 EffectCompiler.Damage 同语义）、
        /// 其余=施法者攻击 ×暴击期望乘区 1+(幸运/100)×(理智/100)（2026-10-02 幸运暴击批接入
        /// AI 估值，docs/18 决策二十七——确定性期望值非 roll、模拟核心随机纪律不破；全员幸运 0
        /// 时恒等零行为变化；斩杀判定随之由保守口径转为期望口径=幸运高的单位更倾向预判暴击斩杀）。
        /// 未计防御/反应乘区=启发式估值
        /// </summary>
        public static int EstimatePerTargetDamage(Unit caster, SkillConfig.SkillData skillData)
        {
            if (skillData == null) return 0;
            int percent = skillData.GetInt(SkillParamKey.Damage, 0);
            if (percent <= 0) return 0;
            int count = Math.Max(1, skillData.GetInt(SkillParamKey.DamageCount, 1));
            var stats = caster?.GetUnitComponent<UnitStats>();
            if (stats == null) return 0;

            int baseValue = stats.Attack;
            if (FindParam(skillData, SkillParamKey.Damage)?.baseType == SkillBaseType.BasedOnMaxHealth)
                baseValue = stats.GetStatStruct(StatType.HP).Max;
            int raw = baseValue * percent * count / 100;
            // 暴击期望（整数万分比）：1 + (幸运/100)×(理智/100)——负幸运不进此路（roll 侧 ≤0 恒否）
            int critExpectPercent = 10000 + Math.Max(0, stats.Luck) * stats.Sanity;
            return raw * critExpectPercent / 10000;
        }

        /// <summary>
        /// 延奏目标是否合法有产出（蒙德协奏规则 docs/07：目标=蒙德角色或施法者自身——
        /// 非蒙德且非自身时效果原子全部空产出，勿选）。与 EffectCompiler.ResolveCastTargets 同语义
        /// </summary>
        public static bool IsMondstadtOrSelfUnit(Unit self, Unit ally)
        {
            if (self == null || ally == null) return false;
            if (ally == self) return true;
            var id = ally.GetUnitComponent<UnitIdentity>();
            return IsMondstadtUnitName(id != null ? id.UnitName.ToString() : "");
        }

        /// <summary>单位名是否蒙德角色（2026-09-25 三轮审查 S8 单出口：蒙德判定全项目唯一实现——
        /// 数据源=UnitConfig.factions（与 RawData 同源）；消费方=AI 估值+EffectCompiler 协奏筛选）</summary>
        public static bool IsMondstadtUnitName(string unitName)
        {
            var config = GIC.Framework.Wargame.Instance?.Context?.Get<UnitConfig>();
            if (config == null || !Enum.TryParse<UnitName>(unitName, out var name)) return false;
            if (!config.TryGetUnitData(name, out var data) || data.factions == null) return false;
            foreach (var faction in data.factions)
                if (faction == FactionType.Mondstadt) return true;
            return false;
        }

        /// <summary>技能参数表查参数（EffectCompiler.FindParam 同构私有出口）</summary>
        private static SkillParam FindParam(SkillConfig.SkillData skillData, SkillParamKey key)
        {
            if (skillData?.customParams == null || key == SkillParamKey.None) return null;
            foreach (var p in skillData.customParams)
                if (p.key == key) return p;
            return null;
        }

        /// <summary>单位支援半径（F-1 支援锚驻位分流，docs/archive/34 §5.1）——**厘米**（P3 资产迁移后
        /// 治疗半径参数=cm 本口直读；P5 拆「格消费方」错配：此前消费方按切比雪夫格比较 100cm 值=
        /// 恒在奶程内的站桩错配随 P5-C 欧氏化修复）：技能集内全部 Heal 原子（OnCast/OnHit）的
        /// radiusKey 参数最大值（GetInt 缺省 100=1 格——与 EnumerateSkillHeals 半径提取同口径）；
        /// 无治疗原子=0（非支援型天然不消费）。伤员锚驻位距离=此值（贴身奶），敌锚仍=AttackRangeCmOf</summary>
        public static int SupportRadiusOf(Unit unit)
        {
            int best = 0;
            var skills = unit?.Skills;
            if (skills == null) return 0;
            for (int i = 0; i < skills.Count; i++)
            {
                var data = skills[i]?.RawData;
                if (data?.effects == null) continue;
                foreach (var atom in data.effects)
                {
                    if (atom == null || atom.kind != SkillEffectKind.Heal) continue;
                    if (atom.trigger != SkillEffectTrigger.OnCast && atom.trigger != SkillEffectTrigger.OnHit) continue;
                    int radius = atom.radiusKey != SkillParamKey.None
                        ? data.GetInt(atom.radiusKey, CellMetrics.CellSizeCm) : CellMetrics.CellSizeCm;
                    if (radius > best) best = radius;
                }
            }
            return best;
        }

        /// <summary>单位光环半径（G-1 光环位置价值，docs/archive/35：持有「半径型 tick 光环」Buff
        /// 的有效作用半径——光环挂水/挂冰引擎的贴敌驱动力来源）——**厘米**（P3 资产迁移后
        /// BuffConfig.AuraRadius=cm〔冰棱/歌声之环=100〕，P5 起消费方欧氏 cm 比较）。
        /// 读取=BuffConfig.AuraRadius 虚属性
        /// 单源（2026-10-20 审查批#6：原 is-pattern 白名单退役——新光环族=BuffConfig 子类覆写，
        /// 本口/AuraRadiusOfBuffType/客户端预估三处全通）；无光环=0（天然不消费）。
        /// **静态基础值口径**：命座加成在 Buff 类内部结算——本原语取基础值=保守感知（命座单位感知
        /// 半径略小于实际，贴敌判定不越界）。注意「发放」语义（2026-10-03 用户勘正）：
        /// 闪耀奇迹=发放非转移——施加只给目标挂新实例，施法者持有不消失，光环在谁身上谁带贴敌驱动力</summary>
        public static int AuraRadiusOf(Unit unit)
        {
            if (unit?.Buffs == null) return 0;
            foreach (var buff in unit.Buffs)
                if (buff?.Config != null && buff.Config.AuraRadius > 0)
                    return buff.Config.AuraRadius;
            return 0;
        }

        /// <summary>J-2 光环 Buff 半径按 BuffType 直查（自施放爆发**施放前**预判用——Buff 尚未
        /// 上身，AuraRadiusOf 查不到。读取=BuffConfig.AuraRadius 虚属性单源（2026-10-20 审查批#6：
        /// switch 白名单退役）——与运行时结算同源</summary>
        public static int AuraRadiusOfBuffType(BuffType type)
            => BuffConfig.OfType(type)?.AuraRadius ?? 0;

        // ==================== AI 施放节流（决策九十五 P5-D——tick 时代无 CD 技能刷屏防线） ====================

        /// <summary>最近实际施放 tick 记账（key=unitId|skillIndex）。**写=SkillExecutor 全 gate
        /// 通过后**（RecordSkillCast）——决策侧只读不写：预测器（OpponentThreatModel.DecideOne 直跑）
        /// 与观察局旁听同权威决策幂等（一步收敛不动点契约保持）。静态生命周期=进程——
        /// sim 实例号自清（2026-10-15 四定案②：新静态 AI 注册表一律 InstanceId）</summary>
        private static readonly Dictionary<string, int> _lastAiCastTick = new Dictionary<string, int>();
        private static int _lastThrottleSim = -1;

        /// <summary>AI 候选施放节流查询（纯读）：距该单位该技能最近一次实际施放不足
        /// AITuning「AI 重施最短间隔 tick」=节流中（不产候选）。三脑候选域统一消费——tick 制无 CD
        /// 技能（延奏/变奏/治疗族）在 0.5s 决策节拍下会每拍施放：自我延奏刷分+治疗刷屏把伤害完全
        /// 抵消=无人死亡的死水僵局（P5-D 双 AI 观察局实锤：U177 承伤 160/30s 但被奶回、全场零死亡）。
        /// 攻击技能不受影响（战技 CD 60/爆发 CD 100 tick 恒大于缺省 40）；0=关闭节流</summary>
        public static bool AiCastThrottled(BattleSimState sim, string unitId, int skillIndex)
        {
            if (sim == null || string.IsNullOrEmpty(unitId)) return false;
            int interval = AITuningConfig.Instance.AI重施最短间隔tick;
            if (interval <= 0) return false;
            if (sim.InstanceId != _lastThrottleSim)
            {
                _lastAiCastTick.Clear();
                _lastThrottleSim = sim.InstanceId;
            }
            return _lastAiCastTick.TryGetValue(unitId + "|" + skillIndex, out int last)
                && sim.CurrentTick - last < interval;
        }

        /// <summary>记录一次实际施放（SkillExecutor 全 gate 通过后调——真人施放同记账：
        /// 语义=「该单位该技能最近一次实际出招」，真人占用同样构成 AI 侧节流窗）</summary>
        public static void RecordSkillCast(BattleSimState sim, string unitId, int skillIndex)
        {
            if (sim == null || string.IsNullOrEmpty(unitId)) return;
            if (sim.InstanceId != _lastThrottleSim)
            {
                _lastAiCastTick.Clear();
                _lastThrottleSim = sim.InstanceId;
            }
            _lastAiCastTick[unitId + "|" + skillIndex] = sim.CurrentTick;
        }
    }
}
