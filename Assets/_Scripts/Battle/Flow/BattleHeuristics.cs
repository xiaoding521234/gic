using System;
using System.Collections.Generic;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// AI 决策共享工具（B6b；2026-09-25 v2 强化；D 批次术语迁移）：配额脑（PlayerQuotaBrain）与
    /// 眷属脑（FamiliarBrain）、伙伴脑（CompanionBrain）共用的敌人查找/方向/技能查询/命中预判
    /// 启发式原语。全部纯读 BattleSimState/BattleSnapshot，不改状态（docs/18 决策一）。
    /// 方向纪律（docs/18 决策八）：移动与直线技能瞄准全员=「十字方向其一」——AI 上交方向
    /// 一律十字四向，勿走八向（八向上交技能侧被 SkillHitResolver.DirectionToDelta 归一主轴，
    /// 斜向敌人必空放=白耗元能，docs/11 方向纪律①④ 已收口）。
    /// </summary>
    public static class BattleHeuristics
    {
        /// <summary>十字四向（移动/直线技能瞄准方向域；枚举值序遍历=决策确定性）</summary>
        public static readonly Direction2D[] CrossDirections =
        {
            Direction2D.Right,
            Direction2D.Left,
            Direction2D.Up,
            Direction2D.Down,
        };

        // ==================== 单位分拣 ====================

        /// <summary>单位层级（D 批次操控分层，docs/active/32 §2）：星级→UnitTier 单一换算出口，
        /// 全部层级门控/豁免/分拣逻辑只看此枚举——禁止散落星级区间判断</summary>
        public static UnitTier TierOf(Unit unit)
        {
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

        // ==================== 敌人查找 ====================

        /// <summary>
        /// 距离某单位最近的存活敌方单位（切比雪夫距离——docs/03 §3.3 八方向等价度量；
        /// 等距平局取 unitId 升序=枚举序铁律）
        /// </summary>
        public static Unit FindNearestEnemy(BattleSimState sim, Unit self)
        {
            var enemies = FindEnemiesByDistance(sim, self);
            return enemies.Count > 0 ? enemies[0] : null;
        }

        /// <summary>全部存活敌方单位按切比雪夫距离升序（等距平局 unitId 升序=枚举序铁律）——
        /// 低级单位换目标巡逻用（2026-09-26 拍板「当自己的任何攻击都无法打到时，换目标巡逻」：
        /// 逐敌试逼近步，贴身打不了/不可达的自动换下一个）；纯读不改状态</summary>
        public static List<Unit> FindEnemiesByDistance(BattleSimState sim, Unit self)
        {
            var selfId = self.GetUnitComponent<UnitIdentity>();
            var result = new List<Unit>();
            if (selfId == null) return result;
            var selfPos = sim.GetPosition(self);

            foreach (var kv in sim.Units)
            {
                var unit = kv.Value;
                var id = unit.GetUnitComponent<UnitIdentity>();
                if (id == null || id.Team == selfId.Team) continue; // 敌我=TeamType 口径（2026-09-25 三轮审查 C2）
                if (BattleSimState.IsDead(unit)) continue;
                result.Add(unit);
            }
            var distOf = new Dictionary<Unit, int>();
            foreach (var unit in result)
                distOf[unit] = sim.GetPosition(unit).ChebyshevTo(selfPos); // 切比雪夫单源（BattleCell.ChebyshevTo）；顺删零消费的 posOf（2026-10-02 复审）
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

        // ==================== 方向与技能 ====================

        /// <summary>
        /// 十字逼近方向（朝目标的主轴先行：|dx|≥|dy| 走横轴、否则纵轴——切比雪夫域内只有走
        /// 大差轴才缩短距离；等距取横轴=确定性约定）。dx=dy=0 返回 0（同格堆叠无逼近意义）
        /// </summary>
        public static Direction2D BestCrossApproachDirection(int dx, int dy)
        {
            if (dx == 0 && dy == 0) return 0;
            if (Math.Abs(dx) >= Math.Abs(dy))
                return dx > 0 ? Direction2D.Right : Direction2D.Left;
            return dy > 0 ? Direction2D.Up : Direction2D.Down;
        }

        /// <summary>朝目标格的最短路首步（十字 BFS 绕行，2026-09-26 报障「低级单位不会绕路，一直卡在
        /// 湖旁边」+同日二轮报障「走了两步后就再也不走了」）：替代直行方向分量——直行被湖/虚空/单位
        /// 挡时按 BFS 拐弯绕行；小兵每回合重算走一步=逐回合沿最短路逼近。
        /// **目标集=敌格十字邻格中「可通行且无阻挡单位占据」的格**——敌格本身剔除（走进必被敌挡弹回）
        /// +被占/不可行邻格剔除（二轮报障根因：goal 豁免让小兵走进被占格/水格，结算弹回、被挡也算
        /// 已使用→每回合原地弹回看起来再也不走）。通行=地形按移动者常态类型 IsPassable；阻挡=存活+
        /// 尸体单位格（移动者开「与友方互不阻挡」时友方格放行——**注意：这是 MovementResolver.CanEnter
        /// 的保守近似而非同口径**（2026-09-28 批5 复审勘正）：CanEnter 实为「占据者.BlockAllies 且双方
        /// 都未开 flag」才挡、另有体积绝对层；此处只查移动者侧 flag 且一律视为阻挡——偏差全为保守向
        /// （多绕路不会被结算弹回），现役低级单位全开 flag 时与 CanEnter 等价；首个不开 flag 的低级
        /// 单位落地时须同步双侧条件）。
        /// 返回 0=已贴身/同格/无路（缺席）。方向域=十字四向，方向纪律不变；枚举序展开=决策确定性。</summary>
        public static Direction2D FindApproachFirstStep(BattleSimState sim, Unit self, BattleCell target,
            List<BattleCell> reservedCells = null)
        {
            if (!BuildApproachField(sim, self, target, out var occupied, out var goals, out var from, out var forceType,
                reservedCells))
                return 0;
            var map = sim.Map;
            if (goals[from.x, from.y]) return 0; // 已贴身（站合法邻格）：无逼近意义

            // BFS（十字域）：队列携带首步方向；到任一目标格即回传
            var visited = new bool[map.width, map.height];
            var queue = new Queue<(int x, int y, Direction2D first)>();
            visited[from.x, from.y] = true;
            foreach (var dir in CrossDirections)
            {
                var delta = SkillHitResolver.DirectionToDelta(dir);
                int nx = from.x + delta.x, ny = from.y + delta.y;
                if (!map.HasTile(nx, ny) || visited[nx, ny]) continue;
                if (occupied[nx, ny]) continue;
                if (!map.IsPassable(nx, ny, forceType)) continue;
                visited[nx, ny] = true;
                if (goals[nx, ny]) return dir; // 一步贴身：直接到位
                queue.Enqueue((nx, ny, dir));
            }
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var dir in CrossDirections)
                {
                    var delta = SkillHitResolver.DirectionToDelta(dir);
                    int nx = cur.x + delta.x, ny = cur.y + delta.y;
                    if (!map.HasTile(nx, ny) || visited[nx, ny]) continue;
                    if (occupied[nx, ny]) continue;
                    if (!map.IsPassable(nx, ny, forceType)) continue;
                    visited[nx, ny] = true;
                    if (goals[nx, ny]) return cur.first; // 到达目标集：回传首步方向
                    queue.Enqueue((nx, ny, cur.first));
                }
            }
            return 0; // 无路（真不可达）：缺席
        }

        /// <summary>逼近场构建（FindApproachFirstStep/FindApproachStraightSteps 共用口径，防双份漂移
        /// ——2026-10-03 抽提）：同格判定+单位占据格（含尸体；互不阻挡开启时友方格放行=CanEnter 的
        /// 保守近似，细节与同步条件见 FindApproachFirstStep 注释）+目标集（敌格十字邻格中「可通行且
        /// 无阻挡占据」的格）。false=同格/地图无效（调用方一律按"无逼近"处理）。
        /// I-D 移动意图占位（docs/active/37 §5，2026-10-04）：reservedCells=意图板已声明的落点
        ///（前位伙伴/玩家魔神的决策落点）——后位 BFS 视作占位避让（集团推进不互撞）；null=零行为
        /// 变化（眷属/配额路径不传）。</summary>
        private static bool BuildApproachField(BattleSimState sim, Unit self, BattleCell target,
            out bool[,] occupied, out bool[,] goals, out BattleCell from, out ForceType forceType,
            List<BattleCell> reservedCells = null)
        {
            occupied = null; goals = null;
            from = new BattleCell(0, 0);
            forceType = ForceType.Walk;
            var map = sim.Map;
            if (map == null || map.width <= 0 || map.height <= 0) return false;
            from = sim.GetPosition(self);
            if (from.x == target.x && from.y == target.y) return false; // 同格：无逼近意义

            var moveable = self.GetUnitComponent<UnitMoveable>();
            forceType = moveable != null ? moveable.NormalMoveType : ForceType.Walk;
            bool passAllies = moveable != null && moveable.与友方互不阻挡;
            var selfIdentity = self.GetUnitComponent<UnitIdentity>();

            // 单位占据格（含尸体——尸体保留碰撞）：自身除外；互不阻挡开启时友方格放行
            occupied = new bool[map.width, map.height];
            foreach (var kv in sim.Units)
            {
                var pos = sim.GetPosition(kv.Value);
                if (pos.x == from.x && pos.y == from.y) continue;
                bool isAlly = false;
                if (passAllies && selfIdentity != null)
                {
                    var id = kv.Value.GetUnitComponent<UnitIdentity>();
                    isAlly = id != null && id.Team == selfIdentity.Team;
                }
                if (!isAlly && pos.x >= 0 && pos.x < map.width && pos.y >= 0 && pos.y < map.height)
                    occupied[pos.x, pos.y] = true;
            }

            // 目标集：敌格十字邻格中「可通行 + 无阻挡单位占据」的格（敌在水里时取岸格；
            // 被占/不可行邻格一律剔除——否则小兵走进去每回合被弹回=二轮报障根因）
            goals = new bool[map.width, map.height];
            foreach (var dir in CrossDirections)
            {
                var delta = SkillHitResolver.DirectionToDelta(dir);
                int gx = target.x + delta.x, gy = target.y + delta.y;
                if (!map.HasTile(gx, gy)) continue;
                if (!map.IsPassable(gx, gy, forceType)) continue;
                if (occupied[gx, gy]) continue;
                goals[gx, gy] = true;
            }
            // I-D 移动意图占位：前位已声明落点并入 occupied（越界格跳过；声明口径=乐观落点，
            // 结算偏差保守向——多绕路不会死锁，真实结算后下回合重算吸收）
            if (reservedCells != null)
            {
                foreach (var rc in reservedCells)
                {
                    if (rc.x >= 0 && rc.x < map.width && rc.y >= 0 && rc.y < map.height)
                        occupied[rc.x, rc.y] = true;
                }
            }
            return true;
        }

        /// <summary>沿 BFS 最短路的「直线前缀」步数（2026-10-03 报障「丘丘人来回左右移动持续多回合」
        /// 返修原语——眷属 v6 多格移动专用）：从单位位到目标攻击位集的最短路上，沿首步方向**不拐弯**
        /// 能连续走几格——只踏「到攻击位距离严格递减」的最短路格，**踏上攻击位（dist=0）即停不越过**。
        /// 与「首步方向×N 直线飞」（旧 v6 写法=振荡根源：L 形路径越拐点偏航+越过攻击位不停，下回合
        /// BFS 指回程=左右乒乓，打不进射程就永不收敛）的区别：每回合距离单调递减，数学上不可能振荡。
        /// 返回 0=已贴身/同格/无路（与 FindApproachFirstStep 同判）；direction out=首步方向。
        /// 口径=BuildApproachField 共用（保守近似同 FindApproachFirstStep 注释）</summary>
        public static int FindApproachStraightSteps(BattleSimState sim, Unit self, BattleCell target,
            int maxSteps, out Direction2D direction, List<BattleCell> reservedCells = null)
        {
            direction = 0;
            if (maxSteps <= 0) return 0;
            if (!BuildApproachField(sim, self, target, out var occupied, out var goals, out var from, out var forceType,
                reservedCells))
                return 0;
            var map = sim.Map;
            if (goals[from.x, from.y]) return 0; // 已贴身（站合法邻格）：无逼近意义

            // 多源 BFS（自攻击位集反向扩散）：每格到最近攻击位的步数 dist
            var dist = new int[map.width, map.height];
            var queue = new Queue<(int x, int y)>();
            for (int x = 0; x < map.width; x++)
                for (int y = 0; y < map.height; y++)
                {
                    if (goals[x, y]) { dist[x, y] = 0; queue.Enqueue((x, y)); }
                    else dist[x, y] = -1;
                }
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var dir in CrossDirections)
                {
                    var delta = SkillHitResolver.DirectionToDelta(dir);
                    int nx = cur.x + delta.x, ny = cur.y + delta.y;
                    if (!map.HasTile(nx, ny) || dist[nx, ny] >= 0) continue;
                    if (occupied[nx, ny] || !map.IsPassable(nx, ny, forceType)) continue;
                    dist[nx, ny] = dist[cur.x, cur.y] + 1;
                    queue.Enqueue((nx, ny));
                }
            }
            if (dist[from.x, from.y] < 0) return 0; // 无路（真不可达）

            // 首方向=十字序首个「距离严格缩短」的邻格（与 FindApproachFirstStep 同为确定性最短路首步）
            var firstX = 0; var firstY = 0;
            foreach (var dir in CrossDirections)
            {
                var delta = SkillHitResolver.DirectionToDelta(dir);
                int nx = from.x + delta.x, ny = from.y + delta.y;
                if (!map.HasTile(nx, ny)) continue;
                if (occupied[nx, ny] || !map.IsPassable(nx, ny, forceType)) continue;
                if (dist[nx, ny] == dist[from.x, from.y] - 1)
                {
                    direction = dir; firstX = delta.x; firstY = delta.y;
                    break;
                }
            }
            if (direction == 0) return 0;

            // 直线前缀逐格推进：距离不严格递减（拐弯/绕行起点）即停；被地形/单位挡即停；
            // 踏上攻击位（dist=0）即停——到位不越点
            int steps = 0;
            int cx = from.x, cy = from.y;
            while (steps < maxSteps)
            {
                int nx = cx + firstX, ny = cy + firstY;
                if (!map.HasTile(nx, ny)) break;
                if (occupied[nx, ny] || !map.IsPassable(nx, ny, forceType)) break;
                if (dist[nx, ny] != dist[cx, cy] - 1) break; // 偏离最短路：停在拐点，下回合重算换向
                steps++;
                cx = nx; cy = ny;
                if (dist[cx, cy] == 0) break; // 踏上攻击位：本回合到此为止
            }
            return steps;
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
        /// 技能可开火的首个十字方向（低级单位简化预判用；AI 玩家脑评分制逐向自评勿走此口）。
        /// 预判=技能实例 WouldHitEnemyInDirection（与 HUD 瞄准推荐/结算形态同源，docs/18 决策九 D5
        /// 工厂强制同形）+ 存活目标校验（纯尸体线=浪费行动不选）。返回 0=无可命中方向
        /// </summary>
        public static Direction2D FindAttackDirection(BattleSimState sim, BattleSnapshot snapshot,
            Unit self, BaseSkill skill)
        {
            var identity = self.GetUnitComponent<UnitIdentity>();
            if (identity == null || skill == null) return 0;
            var from = sim.GetPosition(self);
            foreach (var direction in CrossDirections)
            {
                if (!skill.WouldHitEnemyInDirection(sim.Map, snapshot, identity.Team, from, direction))
                    continue;
                if (PreviewLineTargets(sim, snapshot, identity.Team, from, skill.RawData, direction).Count == 0)
                    continue;
                return direction;
            }
            return 0;
        }

        /// <summary>
        /// 该方向上技能将实际命中的存活敌方（评分/预判共用的目标清单）——镜像 EffectCompiler
        /// 判定两分支同口径：LineProjectile=投射物首停格（含尸体截停=Host 同语义）该格存活敌全中；
        /// LineBurst/无时轮=射程（clip.maxRange，缺省 24）内整线存活敌全中。纯尸体线返回空表
        /// （伤害打尸体=浪费，评分口径按存活目标计）
        /// </summary>
        public static List<UnitState> PreviewLineTargets(BattleSimState sim, BattleSnapshot snapshot,
            TeamType casterTeam, BattleCell from, SkillConfig.SkillData skillData, Direction2D direction)
        {
            var result = new List<UnitState>();
            var delta = SkillHitResolver.DirectionToDelta(direction);

            // 投射物形态：首个敌方占格（含尸体）截停，命中格 AoE 同格存活敌全中
            var projClips = SkillTimelineQuery.JudgmentClips(skillData?.timeline, SkillJudgmentKind.LineProjectile);
            if (projClips.Count > 0)
            {
                int maxRange = projClips[0].maxRange > 0 ? projClips[0].maxRange : ProjectileRule.MaxRange;
                for (int step = 1; step <= maxRange; step++)
                {
                    var cell = new BattleCell(from.x + delta.x * step, from.y + delta.y * step);
                    if (!sim.Map.HasTile(cell.x, cell.y)) break; // 虚空截断
                    var enemies = SkillHitResolver.FindEnemiesAt(snapshot, casterTeam, cell);
                    if (enemies.Count == 0) continue;
                    foreach (var enemy in enemies)
                        if (enemy.isCorpse == 0) result.Add(enemy);
                    break; // 首停
                }
                return result;
            }

            // 整线/距离段形态：射程内存活敌全中（虚空截断、无截停）
            var burstClips = SkillTimelineQuery.JudgmentClips(skillData?.timeline, SkillJudgmentKind.LineBurst);
            int range = burstClips.Count > 0 && burstClips[0].maxRange > 0
                ? burstClips[0].maxRange : ProjectileRule.MaxRange;
            for (int step = 1; step <= range; step++)
            {
                var cell = new BattleCell(from.x + delta.x * step, from.y + delta.y * step);
                if (!sim.Map.HasTile(cell.x, cell.y)) break;
                foreach (var enemy in SkillHitResolver.FindEnemiesAt(snapshot, casterTeam, cell))
                    if (enemy.isCorpse == 0) result.Add(enemy);
            }
            return result;
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

        /// <summary>
        /// 单位方向攻击射程（CR-Move 驻位基准，2026-10-02 拍板「像皇室战争那样」）：全部可施放
        /// 方向攻击技能（Normal/Burst、非单位指向/自施放、伤害>0、消耗门槛过）能命中的最远格数。
        /// 射程语义与 PreviewLineTargets 两分支**完全同口径**（投射物=clip.maxRange 缺省 24 截停；
        /// 整线=clip.maxRange 缺省 24——安柏箭矢/箭雨未配 maxRange=全图 24 狙击射程；凯亚霜袭=2；
        /// 芭芭拉水球=5）。无可施放方向攻击技能=0（驻位距离回落 0=贴身逼近）。
        /// 纯读不 roll 不查命中线——有射程≠有线（无线时走对齐/兜底链，勿在此混入线判定）
        /// </summary>
        public static int AttackRangeOf(BattleSimState sim, Unit unit, string playerId)
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

                int range;
                var projClips = SkillTimelineQuery.JudgmentClips(data.timeline, SkillJudgmentKind.LineProjectile);
                if (projClips.Count > 0)
                    range = projClips[0].maxRange > 0 ? projClips[0].maxRange : ProjectileRule.MaxRange;
                else
                {
                    var burstClips = SkillTimelineQuery.JudgmentClips(data.timeline, SkillJudgmentKind.LineBurst);
                    range = burstClips.Count > 0 && burstClips[0].maxRange > 0
                        ? burstClips[0].maxRange : ProjectileRule.MaxRange;
                }
                if (range > best) best = range;
            }
            return best;
        }

        /// <summary>技能参数表查参数（EffectCompiler.FindParam 同构私有出口）</summary>
        private static SkillParam FindParam(SkillConfig.SkillData skillData, SkillParamKey key)
        {
            if (skillData?.customParams == null || key == SkillParamKey.None) return null;
            foreach (var p in skillData.customParams)
                if (p.key == key) return p;
            return null;
        }

        /// <summary>单位支援半径（F-1 支援锚驻位分流，docs/active/34 §5.1）：技能集内全部
        /// Heal 原子（OnCast/OnHit）的 radiusKey 参数最大值（GetInt 缺省 1——与
        /// EnumerateSkillHeals 的半径提取同口径）；无治疗原子=0（非支援型天然不消费）。
        /// 伤员锚驻位距离=此值（贴身奶），敌锚仍=AttackRangeOf</summary>
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
                    int radius = atom.radiusKey != SkillParamKey.None ? data.GetInt(atom.radiusKey, 1) : 1;
                    if (radius > best) best = radius;
                }
            }
            return best;
        }

        /// <summary>沿 BFS 最短路的「首段直线前缀」（G-5 修 1，docs/active/35 §4——伙伴锚循环
        /// 移动语义直线前缀化，眷属 v6.1/FindApproachStraightSteps 同族防歪）：BFS 首步方向上
        /// 逐格推进，「到目标 BFS 距离严格递减」判据截断=路径开头的连续直线段（钳 maxSteps 与
        /// 该向地形可行程）。治「BFS 首步×N 直线飞越路径拐点」（环湖绕行首步=Left 被直线化
        /// 执行成纯西行滑边——2026-10-03 探针实证，docs/14 §114 同族）。不可达/无直线段=
        ///（direction=0, steps=0）
        /// 【2026-10-04 退役】：切比判据在绕行段恒短前缀=「凯亚开局只走 1 格」根因（§115 坑⑧），
        /// 最后一处生产调用点（伙伴锚循环）已换 FindApproachStraightSteps（BFS dist 场递减判据）。
        /// **勿新增消费方**——绕行/集团拥挤场景一律用 FindApproachStraightSteps。</summary>
        public static void ApproachStraightPrefix(BattleSimState sim, Unit unit, BattleCell to,
            int maxSteps, out Direction2D direction, out int steps)
        {
            direction = FindApproachFirstStep(sim, unit, to);
            steps = 0;
            if (direction == 0 || maxSteps <= 0) { direction = 0; return; }

            var delta = SkillHitResolver.DirectionToDelta(direction);
            var from = sim.GetPosition(unit);
            var forceType = unit.GetUnitComponent<UnitMoveable>()?.NormalMoveType ?? ForceType.Walk;
            int prevDist = Math.Max(Math.Abs(to.x - from.x), Math.Abs(to.y - from.y));
            int run = 0;
            for (int s = 1; s <= maxSteps; s++)
            {
                var cell = new BattleCell(from.x + delta.x * s, from.y + delta.y * s);
                if (!sim.Map.HasTile(cell.x, cell.y)) break;
                if (!sim.Map.IsPassable(cell.x, cell.y, forceType)) break;
                int nextDist = Math.Max(Math.Abs(to.x - cell.x), Math.Abs(to.y - cell.y));
                if (nextDist >= prevDist) break; // 距离不减=越过路径拐点（直线前缀终点）
                prevDist = nextDist;
                run++;
            }
            steps = run;
            if (run <= 0) direction = 0;
        }

        /// <summary>单位光环半径（G-1 光环位置价值，docs/active/35：持有「半径型 tick 光环」Buff
        /// 的有效作用半径——光环挂水/挂冰引擎的贴敌驱动力来源）。现役白名单=SongOfLife/Icicle
        ///（**新光环类落地时在 is-pattern 补一行**——BaseBuff 无通用 Radius 基座，白名单是显式
        /// 扩展点非硬编码）；无光环=0（天然不消费）。**静态基础值口径**：Radius 是 Buff 静态成员、
        /// 命座加成（如歌声之环 2命 +C2Radius=2）在 Buff 类内部结算——本原语取基础值=保守感知
        ///（命座单位感知半径略小于实际，贴敌判定不越界）。注意「发放」语义（2026-10-03 用户勘正）：
        /// 闪耀奇迹=发放非转移——施加只给目标挂新实例，施法者持有不消失，光环在谁身上谁带贴敌驱动力</summary>
        public static int AuraRadiusOf(Unit unit)
        {
            if (unit?.Buffs == null) return 0;
            foreach (var buff in unit.Buffs)
            {
                if (buff is SongOfLifeBuff) return SongOfLifeBuff.Radius;
                if (buff is IcicleBuff) return IcicleBuff.Radius;
            }
            return 0;
        }
    }
}
