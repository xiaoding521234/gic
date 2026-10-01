using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 寒冰之棱（凛冽轮舞批，2026-10-01 凯亚爆发实装——拍板「凯亚爆发实际并不是召唤，
    /// 与芭芭拉的歌声之环类似，都是buff」：自施放永久光环，docs/units/蒙德/凯亚.md「寒冰之棱」节）：
    /// 持有者=凯亚自身（OnCast[ApplyBuff Caster] 经 ShardCount 参数通道注入初始 2 层），
    /// 永久不计时（RemainingTurns=-1），**持有者倒下仍生效**（RemoveOnHolderDeath=false——
    /// docs/05 §5.4 被动失效通则的技能级例外；碎裂判定要求存活，倒下期间只 tick 不碎裂）。
    /// 碎裂（2026-10-01 复测拍板「元能&gt;50% 时立刻触发，而非回合结束才判定」）：**每笔元能增益
    /// 落地后即时判定**（TurnResolver 元能增益段调 TryShatter——含溢出转移接收方），持有者存活且
    /// 元能严格大于 50% 上限 → 每层治疗 30% 施加者攻击力并移除（碎裂后当回合末自然不再 tick）。
    /// tick：每回合结束对持有者切比雪夫半径内敌人造成 20% 施加者攻击力冰伤（含尸体——鞭尸同
    /// Burn/歌声之环先例；**走 DamagePipeline 吃目标防御/易伤乘区**〔2026-10-01 现场取证返修：
    /// 平直值口径致 C2 减防对 tick 无效报障〕、不经反应预览=不触发反应/不附着）。
    /// 命座参数（施加者 Talent 命座技能参数，SourceConstellation 单出口）：叠层上限 2
    /// （2命 +C2StackLimit、3命 +C3StackLimit）；半径 1（3命 +C3Radius）；
    /// 2命起 tick 命中敌人额外施加防御减少 Buff（-C2DefenseReduce，10 层 12 回合叠时长）。
    /// 数值基准=施加者（自施放=凯亚自身；无施加者回落持有者——BurnBuff 归属同款兜底）。
    /// </summary>
    public class IcicleBuff : BaseBuff
    {
        /// <summary>每回合对半径内敌人的伤害（% 施加者攻击力；docs/units/蒙德/凯亚.md——平值 20% 非按层）</summary>
        public const int DamagePercentPerTurn = 20;

        /// <summary>碎裂时每层治疗量（% 施加者攻击力——「每枚对持有者治疗30%攻击力」）</summary>
        public const int HealPercentPerShard = 30;

        /// <summary>碎裂元能阈值（% 持有者元能上限——「当持有者元能超过50%，寒冰之棱碎裂」；超过=严格大于）</summary>
        public const int ShatterEnergyThresholdPercent = 50;

        /// <summary>作用半径（切比雪夫，格；3命起 +C3Radius）</summary>
        public const int Radius = 1;

        /// <summary>基础叠层上限（2命起 +C2StackLimit、3命起 +C3StackLimit）</summary>
        public const int BaseStackLimit = 2;

        /// <summary>2命减防 Buff 叠层上限（2026-10-01 复测拍板「可以叠加10层」）</summary>
        public const int DefDownStackLimit = 10;

        /// <summary>2命减防 Buff 持续回合（2026-10-01 复测拍板「持续12回合，持续时间随层数叠加」——
        /// StatBuff 族时长累加原生语义，与攻击提升/移速提升同构）</summary>
        public const int DefDownDurationTurns = 12;

        public override BuffType Type => BuffType.Icicle;

        public override bool RemoveOnHolderDeath => false; // 「即便持有者倒下，寒冰之棱仍然生效」——技能级例外

        public IcicleBuff(int initialStacks = 1)
        {
            Level = Mathf.Max(1, initialStacks); // 层数（初始=ShardCount 2；上限随施加者命座 2/3/4）
            RemainingTurns = -1;                  // 永久（IsPermanent 标记——不计时，碎裂才消失）
        }

        /// <summary>同类重复施加：叠层（钳施加者命座上限）+刷新施加者（数值基准随之更新）</summary>
        public override void Merge(BaseBuff newer)
        {
            if (newer is IcicleBuff)
                source = newer.source;
            Level = Mathf.Min(Level + Mathf.Max(1, newer.Level), StackLimit());
        }

        public override List<BattleEffect> OnTurnEnd()
        {
            var effects = new List<BattleEffect>();
            if (owner == null || Sim == null) return effects;

            var holderIdentity = owner.GetUnitComponent<UnitIdentity>();
            if (holderIdentity == null) return effects;
            var holderPos = Sim.GetPosition(owner);
            var holderTeam = holderIdentity.Team;
            var ownerId = holderIdentity.UnitID;

            // 数值基准=施加者（自施放=凯亚自身；无施加者回落持有者——BurnBuff 伤害归属同款兜底）
            var attacker = source != null ? source : owner;
            string attackerId = attacker.GetUnitComponent<UnitIdentity>()?.UnitID ?? ownerId;

            // tick：**逐层各弹一次**（2026-10-01 拍板「2层相当于有两个此buff，应当各弹一次」——每层=
            // 独立 buff 实例；第 i 层时刻=i×BuffLayerStaggerSeconds 错峰避免同拍弹出，Host 状态恒即时
            // 结算、错峰纯表现层）。每层对半径内敌方 20% 施加者攻冰伤（含尸体——鞭尸同 Burn/歌声之环
            // 先例）；2命起每层命中额外施加防御减少（逐层各一次）。
            // 碎裂不在此判定（复测拍板①「元能>50% 立刻触发」——移除后本回合末自然无 tick）
            var (cLevel, talent) = SourceConstellation();
            int radius = Radius + (cLevel >= 3 ? (talent != null ? talent.GetInt(SkillParamKey.C3Radius, 1) : 1) : 0);
            int defReduce = cLevel >= 2 ? (talent != null ? talent.GetInt(SkillParamKey.C2DefenseReduce, 5) : 5) : 0;
            int layers = Mathf.Max(1, Level);

            foreach (var kv in Sim.Units)
            {
                var unit = kv.Value;
                var identity = unit.GetUnitComponent<UnitIdentity>();
                if (identity == null || identity.Team == holderTeam) continue; // 只作用敌方
                var pos = Sim.GetPosition(unit);
                if (Math.Max(Math.Abs(pos.x - holderPos.x), Math.Abs(pos.y - holderPos.y)) > radius)
                    continue; // 出半径

                // 每层伤害=20% 施加者攻——**走 DamagePipeline**（2026-10-01 现场取证返修：用户报
                // 「3命凯亚冰棱伤害恒 8 减防看似未生效」——取证实证 DefenseDown 已挂（敌防 0→-20）
                // 但 tick 平直值不吃防御/易伤故恒 8；改走管线吃目标防御/易伤乘区=C2 减防对 tick 生效。
                // 不经 EffectCompiler 反应预览=维持不触发反应/不附着现状；凯亚 1命吸血数据位已挂但
                // 全工程零消费，走管线无吸血副作用）
                var result = DamagePipeline.Calculate(new DamageRequest
                {
                    Attacker = attacker,
                    Target = unit,
                    Element = (int)ElementType.Cryo,
                    AttackPercent = DamagePercentPerTurn,
                });
                int damagePerLayer = result.FinalDamage;

                for (int layer = 0; layer < layers; layer++)
                {
                    if (damagePerLayer > 0)
                        effects.Add(new DamageEffect(attackerId, kv.Key, damagePerLayer, (int)ElementType.Cryo,
                            launchMs: Mathf.RoundToInt(layer * BattleMetrics.BuffLayerStaggerSeconds * 1000f)));
                    if (defReduce > 0)
                        effects.Add(new ApplyBuffEffect(attackerId, kv.Key, (int)BuffType.DefenseDown, 1,
                            defReduce, DefDownStackLimit, DefDownDurationTurns)); // 2命：防御减少——10 层 12 回合叠时长（逐层各施加）
                }
            }
            return effects;
        }

        /// <summary>当前叠层上限（0命=2；2命 +C2StackLimit、3命再 +C3StackLimit——按施加者命座与命座技能参数）</summary>
        private int StackLimit()
        {
            var (cLevel, talent) = SourceConstellation();
            return BaseStackLimit
                   + (cLevel >= 2 ? (talent != null ? talent.GetInt(SkillParamKey.C2StackLimit, 1) : 1) : 0)
                   + (cLevel >= 3 ? (talent != null ? talent.GetInt(SkillParamKey.C3StackLimit, 1) : 1) : 0);
        }

        /// <summary>已达叠层上限（AI 脑自身增益候选排除——重施加无增益不占行动）</summary>
        public override bool IsAtStackCap() => Level >= StackLimit();

        // ==================== 碎裂（2026-10-01 复测拍板：元能>50% 立刻触发，非回合末延迟） ====================

        /// <summary>碎裂判定+状态应用：每笔元能增益落地后由 TurnResolver 元能增益段调用（含溢出转移
        /// 接收方——其元能在 ApplyEnergy 内联写入）。持有者存活且元能**严格大于** 50% 上限 →
        /// **逐层**治疗 30% 施加者攻击力（2026-10-01 拍板「包括碎裂回血等效果各弹一次」——每层
        /// 一枚 HealEffect 由调用方按 BuffLayerStaggerSeconds 错峰追加，状态=总值内联应用 ApplyHeal）
        /// +移除 Buff（注册表注销）；out healPerShard=单层治疗量、sourceUnit/buff 供命令发射
        /// （同溢出转移先例——效应=命令发射载体，状态已应用不二次结算）。倒下不碎裂（复活后
        /// 下一笔元能获取自然触发）；无 Buff/未过阈值返回 false 零动作。</summary>
        public static bool TryShatter(BattleSimState sim, Unit holder, out int healPerShard, out Unit sourceUnit, out IcicleBuff buff)
        {
            healPerShard = 0;
            sourceUnit = null;
            buff = null;
            if (sim == null || holder == null) return false;
            buff = holder.Buffs.Find(b => b != null && b.Type == BuffType.Icicle) as IcicleBuff;
            if (buff == null) return false;
            if (BattleSimState.IsDead(holder)) return false; // 倒下不碎裂（只 tick；复活后下一笔元能获取触发）

            var stats = holder.GetUnitComponent<UnitStats>();
            if (stats == null) return false;
            var energy = stats.GetStatStruct(StatType.Energy);
            if (energy.Max <= 0 || energy.Value <= energy.Max * ShatterEnergyThresholdPercent / 100f)
                return false; // 未过阈值（「超过50%」=严格大于）

            sourceUnit = buff.source != null ? buff.source : holder;
            var attackerStats = sourceUnit.GetUnitComponent<UnitStats>();
            int attack = attackerStats?.Attack ?? 0;
            int layers = Mathf.Max(1, buff.Level);
            // 治疗效率双乘区（2026-10-01 拍板「发起治疗者也应当乘治疗效率；自己治疗自己不乘两次」
            // ——施法者=施加者、受疗者=持有者；自施放同单位=单次〔1命凯亚 ×150% 一次，非 225%〕，
            // ApplyHealEfficiency 单出口）
            healPerShard = EffectCompiler.ApplyHealEfficiency(sourceUnit, holder,
                Mathf.FloorToInt(attack * HealPercentPerShard / 100f));

            sim.ApplyHeal(holder, healPerShard * layers); // 状态=总值即时（Host 不做层间延迟——错峰纯表现层）
            sim.RemoveBuff(holder, buff);
            GICLog.Info($"[IcicleBuff] {holder.GetUnitComponent<UnitIdentity>()?.UnitID} 寒冰之棱碎裂" +
                        $"（元能 {energy.Value}/{energy.Max} > {ShatterEnergyThresholdPercent}%，{layers} 枚" +
                        $"各治疗 {healPerShard}）——合计 {healPerShard * layers}");
            return true;
        }
    }
}
