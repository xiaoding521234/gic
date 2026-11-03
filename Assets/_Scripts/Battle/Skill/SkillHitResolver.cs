using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 技能命中结算入口（B4 起；B-1 起为效果原子编译派发器，docs/29）：
    /// 技能 effects 非空 → EffectCompiler.CompileOnHit 数据驱动产出；空 → 内置三件套
    /// （伤害+附着+战技获能，B6a 硬编码分档）以隐式默认原子等价编译（未迁移旧技能类兜底，决策九 D5）。
    /// 反应判定读片内附着编译视图（快照初值+反应消耗/新附着随编译序推进——2026-10-01 双蒸发修复）；
    /// 状态修改全部以效应形态产出。
    /// </summary>
    public static class SkillHitResolver
    {
        /// <summary>
        /// 产出命中目标的全套效应（伤害含反应易伤并入；命中后附着来袭元素）
        /// </summary>
        /// <param name="attackPercent">攻击百分比（时轮逐发=每发各自的值；无时轮兜底=已合并值）</param>
        /// <param name="delivery">投放形态（0=直击 / 1=直线投射物）</param>
        /// <param name="fromCell">投射物发射格</param>
        /// <param name="hitPointX">命中点连续格心坐标（投射物有效；Host 接触判定得出）</param>
        /// <param name="hitPointY">命中点连续格心坐标 Y</param>
        /// <param name="launchSeconds">发射时刻（秒；时轮 B-S1——Damage 命令带 launchMs 供客户端节拍）</param>
        /// <param name="hitSeconds">命中时刻（秒，相对片播放起点，ProjectileResolver 接触判定得出；0=立即。
        /// 战技获能/命中治疗到点应用——客户端元能/治疗数字随命中时刻跳变，非施放即跳（2026-09-25 拍板「命中时才给」）</param>
        public static List<BattleEffect> Hit(BattleSimState sim, ActionData action, BattleSnapshot sliceSnapshot,
            string targetUnitId, int attackPercent, int delivery, BattleCell fromCell,
            float hitPointX = 0f, float hitPointY = 0f, float launchSeconds = 0f, float hitSeconds = 0f)
        {
            var attacker = sim.GetUnit(action.unitId);
            if (attacker == null) return new List<BattleEffect>();

            var skillData = GetActionSkillData(attacker, action);

            // 数据驱动管线：技能 effects 非空 → 效果原子编译（docs/18 决策九）
            if (skillData != null && skillData.HasEffects)
            {
                return EffectCompiler.CompileOnHit(sim, action, sliceSnapshot, skillData, targetUnitId,
                    attackPercent, delivery, fromCell, hitPointX, hitPointY, launchSeconds, hitSeconds);
            }

            // 旧技能类兜底（effects 空）：内置三件套以隐式默认原子等价编译——
            // 伤害（施法者元素）+附着+战技获能（B6a 硬编码分档），零代码重复
            var implicitEffects = new List<SkillEffectConfig>
            {
                new SkillEffectConfig { trigger = SkillEffectTrigger.OnHit, kind = SkillEffectKind.Damage },
                new SkillEffectConfig { trigger = SkillEffectTrigger.OnHit, kind = SkillEffectKind.AttachElement },
            };
            if (IsNormalSkillOfAction(attacker, action))
                implicitEffects.Add(new SkillEffectConfig
                {
                    trigger = SkillEffectTrigger.OnHit,
                    kind = SkillEffectKind.EnergyGain,
                    // 受益者=施法者/行动者（B6a：命中敌不充能——targetFilter 误配 Target 会把 +10 发给被命中的敌人，2026-09-25 实证）
                    targetFilter = SkillEffectTargetFilter.Caster,
                    value = BattleMetrics.EnergyGainPerSkillHit,
                });
            var fallbackData = new SkillConfig.SkillData { effects = implicitEffects };
            return EffectCompiler.CompileOnHit(sim, action, sliceSnapshot, fallbackData, targetUnitId,
                attackPercent, delivery, fromCell, hitPointX, hitPointY, launchSeconds, hitSeconds);
        }

        /// <summary>投射物元素（箭矢元素色单源，2026-09-28 箭矢正式素材批）：技能 OnHit 首个 Damage
        /// 原子的元素（Physical=施法者自身元素，与 CompileAtom 伤害同口径）；无 Damage 原子/无 effects
        /// =施法者元素。ProjectileResolver 消散命令回填 reactionKind 用——命中箭取 Damage.metadata、
        /// 消散箭取此处，两路恒同元素（历史例证：丘丘人曾借凯亚霜袭=冰箭而非施法者物理灰——2026-10-03
        /// 丘丘人已换专属挥棒，原子优先口径仍在）</summary>
        public static ElementType ResolveProjectileElement(BattleSimState sim, ActionData action)
        {
            var attacker = sim.GetUnit(action.unitId);
            if (attacker == null) return ElementType.Physical;
            var skillData = GetActionSkillData(attacker, action);
            return SkillElementOf(attacker, skillData);
        }

        /// <summary>技能元素（E-3 抽提单源：ResolveProjectileElement 与估值侧共用）：OnHit 首个
        /// Damage 原子的元素优先（Physical=施法者自身元素回落，与 CompileAtom 伤害同口径）；无
        /// Damage 原子/无 effects/施法者缺组件=施法者元素。E-3 反应预期估值消费（CompanionBrain
        /// 反应分预判）——加新消费方一律经此方法勿手抄遍历</summary>
        public static ElementType SkillElementOf(Unit attacker, SkillConfig.SkillData data)
        {
            var self = attacker?.GetUnitComponent<UnitElement>()?.SelfElement ?? ElementType.Physical;
            if (data?.effects == null) return self;
            foreach (var atom in data.effects)
            {
                if (atom.trigger == SkillEffectTrigger.OnHit && atom.kind == SkillEffectKind.Damage)
                    return atom.element != ElementType.Physical ? atom.element : self;
            }
            return self;
        }

        /// <summary>行动选中技能的配置数据（attacker.Skills[skillIndex].RawData；越界/空返回 null）</summary>
        private static SkillConfig.SkillData GetActionSkillData(Unit attacker, ActionData action)
        {
            if (action.skillIndex < 0 || action.skillIndex >= attacker.Skills.Count) return null;
            return attacker.Skills[action.skillIndex]?.RawData;
        }

        /// <summary>行动选中的技能是否战技（SkillType.Normal）——元能获取/未来战技类规则分档依据</summary>
        private static bool IsNormalSkillOfAction(Unit attacker, ActionData action)
        {
            if (action.skillIndex < 0 || action.skillIndex >= attacker.Skills.Count) return false;
            return attacker.Skills[action.skillIndex]?.RawData?.skillType == SkillType.Normal;
        }

        /// <summary>片前快照中找单位状态（瞬发结算唯一状态源）</summary>
        public static UnitState FindUnitState(BattleSnapshot snapshot, string unitId)
        {
            if (snapshot == null || unitId == null) return null;
            foreach (var state in snapshot.units)
                if (state.unitId == unitId) return state;
            return null;
        }

        /// <summary>十字方向 → 格增量（斜向输入归一到主轴；投射物=十字方向其一）</summary>
        public static Vector2Int DirectionToDelta(Direction2D direction)
        {
            switch (direction)
            {
                case Direction2D.Right:
                case Direction2D.UpRight:
                case Direction2D.DownRight:
                    return new Vector2Int(1, 0);
                case Direction2D.Left:
                case Direction2D.UpLeft:
                case Direction2D.DownLeft:
                    return new Vector2Int(-1, 0);
                case Direction2D.Up:
                    return new Vector2Int(0, 1);
                case Direction2D.Down:
                    return new Vector2Int(0, -1);
                default:
                    return new Vector2Int(1, 0);
            }
        }

        // ==================== 自由角瞄准向量（决策九十五 P3，蓝图 §4.1） ====================

        /// <summary>行动瞄准向量单源（千分定点=轴向单位向量×1000；如 (1000,0)=东、(707,707)=东北 45°）
        /// ——**P3 起 EffectCompiler/ProjectileResolver 全部方向消费走此口**。
        /// 推导序（前者优先）：①action.aimX/aimZ 显式声明（P4 HUD 任意角直填）→②direction 十字桥
        /// （AI 三脑〔P5 移植前〕与旧 HUD 链上交的 Direction2D 归一主轴——十字=向量子集，判定数学
        /// 天然兼容零回归；**与 P3 前消费序完全一致**=direction 为现状唯一真源，坐标格型优先序留 P4
        /// HUD 改版拍板）→③targetCell（施法者→目标格心）→④targetUnitId（施法者→目标位置）→⑤东兜底
        /// （DirectionToDelta default 同向）。</summary>
        public static Vector2Int AimVectorOf(BattleSimState sim, ActionData action, BattleSnapshot snapshot)
        {
            if (action.aimX != 0 || action.aimZ != 0)
                return NormalizePerMille(action.aimX, action.aimZ);

            var delta = DirectionToDelta(action.direction);
            if (delta.x != 0 || delta.y != 0)
                return new Vector2Int(delta.x * 1000, delta.y * 1000);

            var caster = snapshot != null ? FindUnitState(snapshot, action.unitId) : null;
            if (caster != null
                && (action.targetCell.x != 0 || action.targetCell.y != 0
                    || caster.posX != action.targetCell.CenterXCm || caster.posZ != action.targetCell.CenterYCm))
            {
                int dx = action.targetCell.CenterXCm - caster.posX;
                int dz = action.targetCell.CenterYCm - caster.posZ;
                if (dx != 0 || dz != 0) return NormalizePerMille(dx, dz);
            }

            var target = snapshot != null && !string.IsNullOrEmpty(action.targetUnitId)
                ? FindUnitState(snapshot, action.targetUnitId) : null;
            if (caster != null && target != null)
            {
                int dx = target.posX - caster.posX;
                int dz = target.posZ - caster.posZ;
                if (dx != 0 || dz != 0) return NormalizePerMille(dx, dz);
            }

            return new Vector2Int(1000, 0);
        }

        /// <summary>整数千分归一（全整数确定性纪律——牛顿迭代平方根同 RTSMovementSystem.SqrtI 款；
        /// 零向量=东兜底）</summary>
        public static Vector2Int NormalizePerMille(int x, int z)
        {
            if (x == 0 && z == 0) return new Vector2Int(1000, 0);
            int len = SqrtI((long)x * x + (long)z * z);
            if (len <= 0) return new Vector2Int(1000, 0);
            int nx = (int)((long)x * 1000 / len);
            int nz = (int)((long)z * 1000 / len);
            if (nx == 0 && nz == 0) return new Vector2Int(1000, 0);
            return new Vector2Int(nx, nz);
        }

        /// <summary>整数平方根（牛顿迭代全整数——初值浮点仅初始化，迭代收敛后与平台无关）</summary>
        private static int SqrtI(long value)
        {
            if (value <= 0) return 0;
            int x = (int)Mathf.Sqrt(value);
            while ((long)(x + 1) * (x + 1) <= value) x++;
            while ((long)x * x > value) x--;
            return x;
        }

        /// <summary>片前快照中该格的敌方存活+尸体单位（含尸体——尸体完全算判定，docs/05 §5.4）。
        /// 敌我判定=TeamType 口径（2026-09-25 三轮审查 C2 收口：casterTeam 为施法者**队伍**，
        /// 2v2 下不再把队友当敌人）</summary>
        public static List<UnitState> FindEnemiesAt(BattleSnapshot snapshot, TeamType casterTeam, BattleCell cell)
        {
            var result = new List<UnitState>();
            if (snapshot == null) return result;
            foreach (var state in snapshot.MainWorldUnits()) // 主世界域单源（②④迁移：灵魂维度隔离判据收口）
            {
                if ((TeamType)state.team == casterTeam) continue;
                if (state.position.x != cell.x || state.position.y != cell.y) continue;
                result.Add(state);
            }
            return result;
        }
    }
}
