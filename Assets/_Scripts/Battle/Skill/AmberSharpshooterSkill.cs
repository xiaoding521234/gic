using System.Collections.Generic;
using GIC.Data;
using GIC.Framework;
namespace GIC.Battle
{


    /// <summary>
    /// 安柏·百发百中（延奏，docs/units/蒙德/安柏.md #4 + docs/07-势力机制/蒙德.md）：
    /// 消耗施法者 20 元能（SkillExecutor 通用门槛），选择全图任意一个**我方**角色协奏——
    /// 被协者攻击提升 +10（ATKBonus），至多叠 5 层（StackLimit），持续 12 回合（Duration），
    /// 持续时间随层数叠加（AttackUpBuff.Merge=叠层+时长累加）。
    ///
    /// 协奏元能归属（docs/07 蒙德）：目标=蒙德角色或施法者自身 → +10 元能并触发其变奏（Henka 技能，
    /// 框架位——当前变奏技能未实现则空产出）；目标=非蒙德角色 → +20 元能（不触发变奏）。
    /// 单位指向型技能（docs/18 决策二）：全图我方、无距离限制、不经格子判定。
    /// 时轮 B-S1b（2026-09-23 实装；HUD 单位指向瞄准同步改我方）。
    /// </summary>
    [SkillAttribute(SkillName.Amber_Sharpshooter)]
    public class AmberSharpshooterSkill : BaseSkill
    {
        public override bool CanCast(Unit caster) => true;

        public override List<BattleEffect> ResolveEffects(BattleSimState sim, ActionData action, BattleSnapshot sliceSnapshot)
        {
            var effects = new List<BattleEffect>();

            // 目标校验：片前快照中存在、我方（同 playerId）、存活（协奏尸体无意义）
            var target = SkillHitResolver.FindUnitState(sliceSnapshot, action.targetUnitId);
            if (target == null || target.playerId != action.playerId || target.isCorpse != 0)
            {
                GICLog.Info($"[Amber_Sharpshooter] 协奏目标无效（{action.targetUnitId}），行动落空");
                return effects;
            }

            // 延奏自身效果：被协者攻击提升（数值单源=技能参数，经 ApplyBuffEffect 参数通道入工厂）
            int atkBonus = GetParamValue(SkillParamKey.ATKBonus, 10);
            int stackLimit = GetParamValue(SkillParamKey.StackLimit, 5);
            int duration = GetParamValue(SkillParamKey.Duration, 12);
            effects.Add(new ApplyBuffEffect(action.unitId, target.unitId, (int)BuffType.AttackUp, 1,
                atkBonus, stackLimit, duration));

            // 协奏元能归属：蒙德角色或施法者自身 +10 并触发变奏；非蒙德 +20（docs/07 蒙德）
            bool isMondstadtOrSelf = target.unitId == action.unitId || IsMondstadtUnit(sim, target.unitName);
            effects.Add(new EnergyEffect(target.unitId, isMondstadtOrSelf ? 10 : 20, EnergyEffect.CategoryEnsoGain));

            // 变奏触发框架位：目标=蒙德/自身 → 调用其 Henka 技能结算（变奏未实现则空产出，B8 前按需实装）
            if (isMondstadtOrSelf)
                effects.AddRange(TriggerHenka(sim, action, sliceSnapshot, target.unitId));

            return effects;
        }

        /// <summary>目标单位是否蒙德角色（UnitConfig.factions 单源；快照不携带势力）</summary>
        private static bool IsMondstadtUnit(BattleSimState sim, string unitName)
        {
            var config = Wargame.Instance?.Context?.Get<UnitConfig>();
            if (config == null || !System.Enum.TryParse<UnitName>(unitName, out var name)) return false;
            if (!config.TryGetUnitData(name, out var data) || data.factions == null) return false;
            foreach (var faction in data.factions)
                if (faction == FactionType.Mondstadt) return true;
            return false;
        }

        /// <summary>
        /// 变奏触发：目标单位 Henka 技能的结算产出并入本行动块（块内因果序——延奏→变奏串行展开，
        /// docs/active/22 §1）。变奏的行动上下文 unitId=变奏持有者自身。
        /// </summary>
        private static List<BattleEffect> TriggerHenka(BattleSimState sim, ActionData action,
            BattleSnapshot sliceSnapshot, string henkaOwnerId)
        {
            var effects = new List<BattleEffect>();
            var owner = sim.GetUnit(henkaOwnerId);
            if (owner == null || owner.Skills == null) return effects;

            for (int i = 0; i < owner.Skills.Count; i++)
            {
                var skill = owner.Skills[i];
                if (skill?.RawData?.skillType != SkillType.Henka) continue;

                var henkaAction = new ActionData
                {
                    playerId = action.playerId,
                    unitId = henkaOwnerId,
                    actionType = ActionType.Skill,
                    skillIndex = i,
                    targetUnitId = "",
                };
                effects.AddRange(skill.ResolveEffects(sim, henkaAction, sliceSnapshot));
                break; // 每单位一个变奏
            }
            return effects;
        }
    }
}
