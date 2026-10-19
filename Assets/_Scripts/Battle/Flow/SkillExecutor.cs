using System;
using System.Collections.Generic;
using GIC.Framework;
using GIC.Data;
namespace GIC.Battle
{
    /// <summary>
    /// 技能行动执行器（B4：正式技能链——ActionData.skillIndex 索引 unit.Skills（=UnitConfig.skills
    /// 顺序创建），BaseSkill.ResolveEffects 纯结算产出效应；DebugAttackSkill 已退役）。
    /// 单位指向型技能只对目标单位生效（不适用格子判定，docs/05 §5.3）；允许鞭尸。
    /// 目标校验读片前快照（瞬发效应按片初状态结算）。
    /// 时轮（B-S1）：通过全部门槛的施放各产一条 SkillCast 命令（castsOut，段内最前发射——
    /// 客户端时轮演出起点；元能不足/不可施放=行动落空，不产施放事件）。
    /// 目标失效口径（2026-09-27 拍板 docs/18 决策十四，A 案=维持）：门槛过后目标在结算时已亡
    /// （同时制必然场景——选择时有效、执行时死亡）=行动已使用——体力元能照扣、SkillCast 照播；
    /// 与「门槛不足=落空不扣」为并行口径（不足=未获执行权；失效=已执行无效果）。
    /// </summary>
    public static class SkillExecutor
    {
        public static List<BattleEffect> Resolve(BattleSimState sim, ActionData action, BattleSnapshot sliceSnapshot,
            List<BattleCommand> castsOut = null)
        {
            var effects = new List<BattleEffect>();

            var attacker = sim.GetUnit(action.unitId);
            if (attacker == null) return effects;
            if (BattleSimState.IsDead(attacker) || !BattleSimState.CanAct(attacker)) return effects;

            var skill = GetSkill(attacker, action.skillIndex);
            if (skill == null)
            {
                GICLog.Warn($"[SkillExecutor] 单位 {action.unitId} 无技能索引 {action.skillIndex}，行动落空");
                return effects;
            }

            // 可施放检查前置（2026-09-25 三轮审查 S6 正序）：占位/不可施放技能先拦，再查消耗
            // ——原顺序会让不可施放的占位技能白扣 10 体力后才落空（UI 置灰防了常规路径，异常上交仍会撞）
            if (!skill.CanCast(attacker))
            {
                GICLog.Info($"[SkillExecutor] 单位 {action.unitId} 技能 {skill.RawData?.skillID} 不可施放，行动落空");
                return effects;
            }

            // ==================== 消耗段（统一消耗模型 C-2：全量迁移完成，双轨回落退役）====================
            // 消耗单源=SkillData.costs（docs/30）：先全查后全扣（多 cost 防「扣了体力才发现苹果不够」
            // ——任一不足=行动落空且不登记任何消耗）；**costs 空=免费技能**（无消耗语义，数据即事实）
            if (!ResourceGate.HasAll(sim, attacker, action.playerId, skill.RawData?.costs, out var missing,
                    isPlayerDirectControl: action.isPlayerDirectControl))
            {
                string missingDesc = missing.kind == CostKind.Item
                    ? missing.item.ToString()
                    : missing.kind == CostKind.AnyItem
                        ? $"任意{missing.subType}"
                        : missing.kind.ToString();
                GICLog.Info($"[SkillExecutor] 单位 {action.unitId} 技能 {skill.RawData?.skillID} 消耗不足" +
                            $"（{missingDesc}×{missing.amount}），行动落空");
                return effects;
            }
            ResourceGate.ChargeAll(sim, attacker, action.playerId, skill.RawData?.costs, effects,
                isPlayerDirectControl: action.isPlayerDirectControl);

            // 号令体力津贴（AI-1 批接线，2026-10-12 拍板口径「玩家操控伙伴势力技能＝0→−5 获得体力」）：
            // 势力技能基 0 体力（docs/07-00「号令不消耗体力」）＋玩家直接操控伙伴减免 5（负值=获得）
            // → 伙伴施法者的延奏/契约号令＝玩家净得 5 体力（StaminaEffect 正值=获得，与减免负值同构）。
            // 旗标由提交路径统一打（TurnFlowController 对 Companion 层级整体标记——真人/AI 共享同一路径
            // 自动对齐）；魔神施法者无伙伴减免（StaminaAmountOf 层级门）＝无津贴。
            // 延奏/契约资产无体力条目＝ChargeAll 恒空转，津贴为唯一体力效果（无双扣）。
            if (action.isPlayerDirectControl
                && BattleHeuristics.TierOf(attacker) == UnitTier.Companion
                && skill.RawData != null
                && (skill.RawData.skillType == SkillType.Enso || skill.RawData.skillType == SkillType.Contract))
                effects.Add(new StaminaEffect(action.playerId, 5));

            effects.AddRange(skill.ResolveEffects(sim, action, sliceSnapshot));

            // 时轮施放事件（B-S1）：施放者片初位置=发射格/朝向参考（sliceIndex/indexInSlice 由发射出口回填）；
            // 技能元素随命令下发（箭雨染色单源——丘丘人借凯亚霜袭=冰色箭雨非施法者物理灰）
            if (castsOut != null)
            {
                var casterState = SkillHitResolver.FindUnitState(sliceSnapshot, action.unitId);
                castsOut.Add(BattleCommand.SkillCast(action.unitId, 0, 0,
                    (int)skill.RawData.skillID, (int)action.direction,
                    casterState != null ? casterState.position : BattleCell.zero,
                    (int)SkillHitResolver.ResolveProjectileElement(sim, action)));
            }

            return effects;
        }

        /// <summary>skillIndex = unit.Skills 数组索引（InitSkills 按 UnitConfig.skills 顺序创建，HUD 同源映射）</summary>
        private static BaseSkill GetSkill(Unit unit, int skillIndex)
        {
            if (unit == null || skillIndex < 0 || skillIndex >= unit.Skills.Count) return null;
            return unit.Skills[skillIndex];
        }
    }
}
