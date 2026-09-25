using System;
using System.Collections.Generic;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// AI 决策共享工具（B6b；2026-09-25 v2 强化）：AI 玩家脑（AIDebugBrain）与低级单位脑
    /// （LowUnitBrain）共用的敌人查找/方向/技能查询/命中预判启发式原语。
    /// 全部纯读 BattleSimState/BattleSnapshot，不改状态（docs/18 决策一）。
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

        /// <summary>是否低级单位（1~2 星，自主行动如 MOBA 小兵，docs/04 §4.1；3~5 星=高级单位）</summary>
        public static bool IsMinorUnit(Unit unit)
        {
            return unit.RawData != null && unit.RawData.starLevel <= 2;
        }

        /// <summary>是否高级单位（3~5 星，由所属玩家操控）</summary>
        public static bool IsMajorUnit(Unit unit)
        {
            return unit.RawData != null && unit.RawData.starLevel >= 3;
        }

        // ==================== 敌人查找 ====================

        /// <summary>
        /// 距离某单位最近的存活敌方单位（切比雪夫距离——docs/03 §3.3 八方向等价度量；
        /// 等距平局取 unitId 升序=枚举序铁律）
        /// </summary>
        public static Unit FindNearestEnemy(BattleSimState sim, Unit self)
        {
            var selfId = self.GetUnitComponent<UnitIdentity>();
            if (selfId == null) return null;
            var selfPos = sim.GetPosition(self);

            Unit best = null;
            int bestDistance = int.MaxValue;
            string bestUnitId = null;
            foreach (var kv in sim.Units)
            {
                var unit = kv.Value;
                var id = unit.GetUnitComponent<UnitIdentity>();
                if (id == null || id.Team == selfId.Team) continue; // 敌我=TeamType 口径（2026-09-25 三轮审查 C2：2v2 不把队友当敌人）
                if (BattleSimState.IsDead(unit)) continue;

                var pos = sim.GetPosition(unit);
                int distance = Math.Max(Math.Abs(pos.x - selfPos.x), Math.Abs(pos.y - selfPos.y));
                var unitId = kv.Key;
                // 等距平局：unitId 升序（枚举序铁律，确定性）
                if (distance < bestDistance || (distance == bestDistance && string.CompareOrdinal(unitId, bestUnitId) < 0))
                {
                    best = unit;
                    bestDistance = distance;
                    bestUnitId = unitId;
                }
            }
            return best;
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
        /// 其余=施法者攻击。未计防御/反应乘区=启发式估值，斩杀判定按保守口径）
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
            return baseValue * percent * count / 100;
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
    }
}
