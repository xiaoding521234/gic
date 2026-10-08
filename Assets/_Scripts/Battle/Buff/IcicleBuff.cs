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
    /// 持有者=凯亚自身（OnCast[ApplyBuff Caster] 经 ShardCount 参数通道注入初始 2 层，
    /// 2命/3命各+1 召唤增补——2026-10-08 拍板「寒冰之棱上限改为4（不再依靠命座），2命 3命
    /// 的效果改为增加爆发召唤数量各为1」：上限常驻资产值、命座只改每次施放的召唤量），
    /// 永久不计时（RemainingTurns=-1），**持有者倒下仍生效**（RemoveOnHolderDeath=false——
    /// docs/05 §5.4 被动失效通则的技能级例外；碎裂判定要求存活，倒下期间只 tick 不碎裂）。
    /// 数值单源=Buff_Icicle.asset（docs/active/39 Buff 配置化——tick 伤害/碎裂治疗/碎裂阈值/半径/
    /// 叠层上限全在资产，调平衡改 Inspector 零代码）；**叠层上限与作用半径均不随命座**（2026-10-08
    /// 重构：旧 C2/C3StackLimit、C3Radius 命座参数通道退役，参数通道枚举值/本地化键保留=ATKBonus 先例）；
    /// **2命起 tick 命中敌人额外施加冰棱减防**（Buff_DefenseDown 资产具名 buff——数值全在该资产单源，
    /// 永久不计时〔2026-10-07 拍板「持续时间改为无限」，同 StatBuff 永久保护〕，逐层各施加；
    /// 2026-10-08 晚返拍「减防仍然留在2命」——重构批首版曾误整体退役，当晚恢复）。
    /// 碎裂（2026-10-01 复测拍板「元能&gt;50% 时立刻触发，而非回合结束才判定」）：**每笔元能增益
    /// 落地后即时判定**（TurnResolver 元能增益段调 TryShatter——含溢出转移接收方），持有者存活且
    /// 元能严格大于〔碎裂元能阈值百分比〕上限 → 每层治疗〔碎裂治疗百分比〕施加者攻击力并移除
    /// （碎裂后当回合末自然不再 tick）。
    /// tick：每回合结束对持有者切比雪夫〔作用半径〕内敌人造成〔每回合伤害百分比〕施加者攻击力
    /// 冰伤（含尸体——鞭尸同 Burn/歌声之环先例；**走 DamagePipeline 吃目标防御/易伤乘区**〔2026-10-01
    /// 现场取证返修：平直值口径致减防乘区对 tick 无效报障〕、不经反应预览=不触发反应/不附着）。
    /// 数值基准=施加者（自施放=凯亚自身；无施加者回落持有者——BurnBuff 归属同款兜底）。
    /// </summary>
    public class IcicleBuff : BaseBuff
    {
        private IcicleBuffConfig Cfg => (IcicleBuffConfig)Config;

        public override BuffType Type => BuffType.Icicle;

        public override bool RemoveOnHolderDeath => false; // 「即便持有者倒下，寒冰之棱仍然生效」——技能级例外

        public IcicleBuff(IcicleBuffConfig config, int initialStacks = 1)
        {
            Config = config;
            Level = Mathf.Max(1, initialStacks); // 层数（初始=ShardCount+命座召唤增补；上限=资产常驻值 4）
            RemainingTurns = -1;                  // 永久（IsPermanent 标记——不计时，碎裂才消失）
        }

        /// <summary>爆发召唤数量按施加者命座增补（2026-10-08 拍板：2命/3命各+1——
        /// 工厂构造后、注册/Merge 前调用（source 已赋）；两路消费一致：
        /// 首次注册直接带增补、Merge 的 newer 也已带增补按次入账）</summary>
        public void ApplyConstellationSummonBonus()
        {
            var (cLevel, talent) = SourceConstellation();
            int bonus = (cLevel >= 2 ? (talent != null ? talent.GetInt(SkillParamKey.C2ShardBonus, 1) : 1) : 0)
                      + (cLevel >= 3 ? (talent != null ? talent.GetInt(SkillParamKey.C3ShardBonus, 1) : 1) : 0);
            if (bonus > 0) Level += bonus;
        }

        /// <summary>同类重复施加：叠层（钳资产常驻上限）+刷新施加者（数值基准随之更新）</summary>
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

            // 数值基准=施加者（自施放=凯亚自身；无施加者回落持有者）——AttackerOf/AttackerIdOf 单源
            var attacker = AttackerOf();
            string attackerId = AttackerIdOf(ownerId);

            // tick：**逐层各弹一次**（2026-10-01 拍板「2层相当于有两个此buff，应当各弹一次」——每层=
            // 独立 buff 实例；第 i 层时刻=i×BuffLayerStaggerSeconds 错峰避免同拍弹出，Host 状态恒即时
            // 结算、错峰纯表现层）。每层对半径内敌方 % 施加者攻冰伤（含尸体——鞭尸同 Burn/歌声之环
            // 先例）；2命起每层命中额外施加冰棱减防（逐层各一次——2026-10-08 晚返拍「减防仍然留在2命」）。
            // 碎裂不在此判定（复测拍板①「元能>50% 立刻触发」——移除后本回合末自然无 tick）
            var (cLevel, _) = SourceConstellation();
            int radius = Cfg.作用半径; // 常驻资产值（2026-10-08 重构：C3Radius 命座通道退役）
            bool applyDefDown = cLevel >= 2; // 2命起 tick 命中附带冰棱减防（是否施加=命座开关；数值全在 Buff_DefenseDown 资产单源）
            int layers = Mathf.Max(1, Level);

            foreach (var kv in Sim.Units)
            {
                var unit = kv.Value;
                var identity = unit.GetUnitComponent<UnitIdentity>();
                if (identity == null || identity.Team == holderTeam) continue; // 只作用敌方
                if (Sim.GetPosition(unit).ChebyshevTo(holderPos) > radius)
                    continue; // 出半径（ChebyshevTo 单源）

                // 每层伤害=% 施加者攻——**只声明命中**（PendingAuraHit，拍时刻=layer×0.15）：回合末
                // 交错管道统一结算（反应预判+消耗+每层附着=决策二十四终版；伤害走 DamagePipeline 全
                // 乘区——目标防御/易伤乘区对 tick 生效〔现场取证返修口径延续〕）。凯亚 1命吸血随一切
                // DamageEffect 统一结算（ApplyEffects）——tick 命中也吸血=预期
                for (int layer = 0; layer < layers; layer++)
                {
                    effects.Add(new PendingAuraHit(attackerId, kv.Key, attacker, unit, ElementType.Cryo,
                        new DamageRequest
                        {
                            Attacker = attacker,
                            Target = unit,
                            Element = (int)ElementType.Cryo,
                            AttackPercent = Cfg.每回合伤害百分比,
                        }, BattleMetrics.LayerBeatSeconds(layer)));
                    if (applyDefDown)
                        // 2命：冰棱减防（决策五十七具名 buff）——**零注入**：每层加成/叠层上限/持续
                        // 全=Buff_DefenseDown 资产单源；buffKey=资产名=具名身份（同族异名共存基础）
                        effects.Add(new ApplyBuffEffect(attackerId, kv.Key, (int)BuffType.DefenseDown, 1,
                            buffKey: "Buff_DefenseDown")); // 逐层各施加（2026-10-01 拍板）
                }
            }
            return effects;
        }

        /// <summary>当前叠层上限（常驻=资产基础叠层上限——2026-10-08 拍板不再依靠命座，
        /// 旧 C2/C3StackLimit 命座通道退役）</summary>
        private int StackLimit() => Cfg.基础叠层上限;

        /// <summary>已达叠层上限（AI 脑自身增益候选排除——重施加无增益不占行动）</summary>
        public override bool IsAtStackCap() => Level >= StackLimit();

        // ==================== 碎裂（2026-10-01 复测拍板：元能>50% 立刻触发，非回合末延迟） ====================

        /// <summary>碎裂判定+状态应用：每笔元能增益落地后由 TurnResolver 元能增益段调用（含溢出转移
        /// 接收方——其元能在 ApplyEnergy 内联写入）。持有者存活且元能**严格大于**〔碎裂元能阈值百分比〕
        /// 上限 → **逐层**治疗〔碎裂治疗百分比〕施加者攻击力（2026-10-01 拍板「包括碎裂回血等效果
        /// 各弹一次」——每层一枚 HealEffect 由调用方按 BuffLayerStaggerSeconds 错峰追加，状态=总值
        /// 内联应用 ApplyHeal）+移除 Buff（注册表注销）；out healPerShard=单层治疗量、sourceUnit/buff
        /// 供命令发射（同溢出转移先例——效应=命令发射载体，状态已应用不二次结算）。倒下不碎裂（复活后
        /// 下一笔元能获取自然触发）；无 Buff/未过阈值返回 false 零动作。阈值/治疗百分比读取=该 Buff
        /// 实例的 Config 资产（Buff_Icicle.asset 单源）。</summary>
        public static bool TryShatter(BattleSimState sim, Unit holder, out int healPerShard, out Unit sourceUnit, out IcicleBuff buff)
        {
            healPerShard = 0;
            sourceUnit = null;
            buff = null;
            if (sim == null || holder == null) return false;
            buff = holder.Buffs.Find(b => b != null && b.Type == BuffType.Icicle) as IcicleBuff;
            if (buff == null) return false;
            if (buff.Config is not IcicleBuffConfig cfg) return false;
            if (BattleSimState.IsDead(holder)) return false; // 倒下不碎裂（只 tick；复活后下一笔元能获取触发）

            var stats = holder.GetUnitComponent<UnitStats>();
            if (stats == null) return false;
            var energy = stats.GetStatStruct(StatType.Energy);
            if (energy.Max <= 0 || energy.Value <= energy.Max * cfg.碎裂元能阈值百分比 / 100f)
                return false; // 未过阈值（「超过50%」=严格大于）

            sourceUnit = buff.AttackerOf(); // 施加者优先、亡佚回落持有者（BaseBuff 单源）
            var attackerStats = sourceUnit.GetUnitComponent<UnitStats>();
            int attack = attackerStats?.Attack ?? 0;
            int layers = Mathf.Max(1, buff.Level);
            // 治疗效率双乘区（2026-10-01 拍板「发起治疗者也应当乘治疗效率；自己治疗自己不乘两次」
            // ——施法者=施加者、受疗者=持有者；自施放同单位=单次〔1命凯亚 ×150% 一次，非 225%〕，
            // ApplyHealEfficiency 单出口）
            healPerShard = EffectCompiler.ApplyHealEfficiency(sourceUnit, holder,
                Mathf.FloorToInt(attack * cfg.碎裂治疗百分比 / 100f));

            sim.ApplyHeal(holder, healPerShard * layers); // 状态=总值即时（Host 不做层间延迟——错峰纯表现层）
            sim.RemoveBuff(holder, buff);
            GICLog.Info($"[IcicleBuff] {holder.GetUnitComponent<UnitIdentity>()?.UnitID} 寒冰之棱碎裂" +
                        $"（元能 {energy.Value}/{energy.Max} > {cfg.碎裂元能阈值百分比}%，{layers} 枚" +
                        $"各治疗 {healPerShard}）——合计 {healPerShard * layers}");
            return true;
        }
    }
}
