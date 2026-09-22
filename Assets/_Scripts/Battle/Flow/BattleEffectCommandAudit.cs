using System.Collections.Generic;
using GIC.Data;
using GIC.Framework;
namespace GIC.Battle
{


    /// <summary>
    /// 效应→命令对账机制（2026-09-22，docs/11 协议与命令流登记项）：Host 产出段命令后校验
    /// "每种已结算效应必有对应命令"——漏发命令=客户端不可见、双端靠下回合快照自愈背离
    /// （首个产物=片内 Heal 漏发，2026-09-19 已单独修，本类为系统性安全网）。
    /// B6 扩效应种类（治疗技能/StatChange/Summon/手牌）前，新 BattleEffect 子类必须在此登记映射，
    /// 未登记类型默认报警——这正是机制目的（新效应漏接命令即被发现）。
    /// 只报警不阻断（对账失败不炸战斗流程；B6 后按需升级为断言）。
    /// </summary>
    public static class BattleEffectCommandAudit
    {
        /// <summary>
        /// 对账：effects 中每种效应在 commands 中有对应命令。调用时机=段命令全部产出后、推送前。
        /// </summary>
        /// <param name="context">对账上下文（片/即时段/回合结束段，用于报警定位）</param>
        /// <param name="effects">本段全部已结算效应（ProjectileEffect 待判定中间态豁免——
        /// 命中后已被替换为 Hit 全套产物，未命中由 ProjectileResolver 产消散 Effect 命令）</param>
        /// <param name="appliedBuffs">应用成功的施加 Buff 效应（BuffFactory 构建失败被跳过的不算漏发；
        /// 命令产出以本列表为准，与 TurnResolver 产出循环同源）</param>
        /// <param name="commands">本段产出的全部命令</param>
        public static void Assert(string context, List<BattleEffect> effects, List<ApplyBuffEffect> appliedBuffs,
            List<BattleCommand> commands)
        {
            // 命令键索引（一次构建；合并语义的效应按合并键查存在性）
            var damageKeys = new HashSet<string>();
            var healKeys = new HashSet<string>();
            var applyBuffKeys = new HashSet<string>();
            var attachKeys = new HashSet<string>();
            var reactionKeys = new HashSet<string>();
            var energyKeys = new HashSet<string>();
            foreach (var command in commands)
            {
                switch (command.type)
                {
                    case BattleCommandType.Damage:
                        damageKeys.Add($"{command.actorUnitId}->{command.targetUnitId}");
                        break;
                    case BattleCommandType.Heal:
                        healKeys.Add($"{command.actorUnitId}->{command.targetUnitId}");
                        break;
                    case BattleCommandType.ApplyBuff:
                        applyBuffKeys.Add($"{command.targetUnitId}:{command.buffType}");
                        break;
                    case BattleCommandType.ElementAttach:
                        attachKeys.Add(command.targetUnitId);
                        break;
                    case BattleCommandType.Reaction:
                        reactionKeys.Add(command.targetUnitId);
                        break;
                    case BattleCommandType.StatChange:
                        if (command.metadata == BattleCommand.StatKindEnergy)
                            energyKeys.Add(command.targetUnitId);
                        break;
                }
            }

            foreach (var effect in effects)
            {
                // 待判定中间态：不进对账（命中产物已在 effects 内、消散命令由 ProjectileResolver 负责）
                if (effect is ProjectileEffect) continue;

                if (effect is DamageEffect damage)
                {
                    if (!damageKeys.Contains($"{damage.AttackerUnitId}->{damage.TargetUnitId}"))
                        Report(context, effect, BattleCommandType.Damage);
                }
                else if (effect is HealEffect heal)
                {
                    if (!healKeys.Contains($"{heal.SourceUnitId}->{heal.TargetUnitId}"))
                        Report(context, effect, BattleCommandType.Heal);
                }
                else if (effect is AttachElementEffect attach)
                {
                    if (!attachKeys.Contains(attach.TargetUnitId))
                        Report(context, effect, BattleCommandType.ElementAttach);
                }
                else if (effect is ReactionEffect reaction)
                {
                    if (!reactionKeys.Contains(reaction.TargetUnitId))
                        Report(context, effect, BattleCommandType.Reaction);
                }
                else if (effect is EnergyEffect energy)
                {
                    // B6a 元能：效应→StatChange 命令（注意合并语义=同片同目标多条只发一条，此处查存在性）
                    if (!energyKeys.Contains(energy.TargetUnitId))
                        Report(context, effect, BattleCommandType.StatChange);
                }
                else if (effect is ApplyBuffEffect)
                {
                    // 以应用成功的集合为准（Build 失败被 ApplyEffects 跳过的非漏发）
                }
                else
                {
                    // 未知效应类型：新 BattleEffect 子类未登记对账映射——B6 扩效应时的主要报警来源
                    GICLog.Warn($"[EffectAudit] {context}：未登记对账映射的效应类型 {effect.GetType().Name}，" +
                                $"target={effect.TargetUnitId}——若该效应应产出命令，请在 BattleEffectCommandAudit 登记映射");
                }
            }

            if (appliedBuffs != null)
            {
                foreach (var applied in appliedBuffs)
                {
                    if (!applyBuffKeys.Contains($"{applied.TargetUnitId}:{applied.BuffType}"))
                        Report(context, applied, BattleCommandType.ApplyBuff);
                }
            }
        }

        private static void Report(string context, BattleEffect effect, BattleCommandType expectedCommand)
        {
            GICLog.Warn($"[EffectAudit] {context}：效应 {effect.GetType().Name}（target={effect.TargetUnitId}）" +
                        $"漏发 {expectedCommand} 命令——客户端将看不到该结算，靠下回合快照自愈（双端背离源）");
        }
    }
}
