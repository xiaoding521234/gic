using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 技能命中结算入口（B4 起；B-1 起为效果原子编译派发器，docs/active/29）：
    /// 技能 effects 非空 → EffectCompiler.CompileOnHit 数据驱动产出；空 → 内置三件套
    /// （伤害+附着+战技获能，B6a 硬编码分档）以隐式默认原子等价编译（未迁移旧技能类兜底，决策九 D5）。
    /// 反应判定读片前快照的 DyedElement（快照一致性）；状态修改全部以效应形态产出。
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

        /// <summary>片前快照中该格的敌方存活+尸体单位（含尸体——尸体完全算判定，docs/05 §5.4）</summary>
        public static List<UnitState> FindEnemiesAt(BattleSnapshot snapshot, string playerId, BattleCell cell)
        {
            var result = new List<UnitState>();
            if (snapshot == null) return result;
            foreach (var state in snapshot.units)
            {
                if (state.playerId == playerId) continue;
                if (state.position.x != cell.x || state.position.y != cell.y) continue;
                result.Add(state);
            }
            return result;
        }
    }
}
