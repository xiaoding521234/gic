using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 歌声之环（B-3 ② + B8 命座批，芭芭拉闪耀奇迹——docs/units/蒙德/芭芭拉.md「歌声之环」节）：
    /// 永久光环（不计时，持有者倒下消失——RemoveOnHolderDeath 覆写 true），叠层上限随施加者命座成长。
    /// 每回合结束：①为**持有者**增加元能（**0命=+5**〔2026-09-30 用户拍板「0命就可以每回合加5元能」〕，
    /// 1命起=C1EnergyGain 参数=+10——参数载体=施加者命座技能）；②对持有者切比雪夫半径内敌人造成
    /// 10% 施加者攻击力水伤（含尸体——鞭尸同 Burn 先例；**走 DamagePipeline 吃目标防御/易伤乘区**
    /// 〔2026-10-01 拍板「这些伤害都应该统一」——与寒冰之棱 tick 同口径；不经反应预览=不触发反应〕）；③对半径内
    /// 我方**存活**单位（含持有者）治疗**施加者 5% 最大生命值**、恢复 1 理智并附着水元素（**2命起不再附着**；
    /// 2026-09-30 拍板：平值 10 无法成长改比例；基准=施加者芭芭拉自身最大生命〔非各自〕，末点截断后×层数）。
    /// 半径 1（**2命起 +C2Radius=2**）；叠层上限 1（**3命起 +C3StackLimit=2**——Level=层数；
    /// **多层=逐层各弹一次**（2026-10-01 拍板④「2层相当于有两个此buff，应当各弹一次」——伤害/治疗/
    /// 元能逐层独立弹数字/跳条、第 i 层时刻=i×BuffLayerStaggerSeconds 错峰避免同拍弹出；理智/附着
    /// =总额单发——无弹数字且 Sanity 命令按目标去重会吞逐层尾条）），重复施加叠层钳上限。
    /// 命座参数读取=施加者（source=施放闪耀奇迹的芭芭拉）的 Talent 命座技能参数（命座也是技能——
    /// 参数载体型被动，docs/09「0命=固有被动、1-3命=增强」）；施加者亡佚/无命座技能回落基础值。
    /// 「恢复 1 理智」已落地（2026-09-30 用户拍板「必须要的」——SanityEffect 结算+StatChange(Sanity)
    /// 命令+UnitState.sanity 快照；玩法消费方随未来理智机制批）。
    /// </summary>
    public class SongOfLifeBuff : BaseBuff
    {
        /// <summary>每回合对半径内敌人的伤害（% 施加者攻击力；docs/units/蒙德/芭芭拉.md）</summary>
        public const int DamagePercentPerTurn = 10;

        /// <summary>每回合对半径内我方的治疗（% 施加者最大生命值；2026-09-30 拍板「平值 10 无法成长」改比例——基准=芭芭拉自身，非各自）</summary>
        public const int HealPercentPerTurn = 5;

        /// <summary>0命每回合为持有者增加的元能（2026-09-30 拍板；1命起=C1EnergyGain 参数〔+10〕取代）</summary>
        public const int EnergyGainPerTurnBase = 5;

        /// <summary>每回合为半径内我方恢复的理智（2026-09-30 用户拍板「恢复1理智是必须要的」正式落地；
        /// 玩法消费方随未来理智机制批，本批打通结算/命令/快照链）</summary>
        public const int SanityGainPerTurn = 1;

        /// <summary>作用半径（切比雪夫，格；2命起 +C2Radius）</summary>
        public const int Radius = 1;

        public override BuffType Type => BuffType.SongOfLife;

        public override bool RemoveOnHolderDeath => true; // 持有者倒下，歌声之环消失

        public SongOfLifeBuff()
        {
            Level = 1;          // 层数（上限随施加者命座：0命1层/3命2层）
            RemainingTurns = -1; // 永久（IsPermanent 标记——不计时，倒下即失）
        }

        /// <summary>同类重复施加：叠层（钳当前施加者命座上限）+刷新施加者（数值基准随之更新）。</summary>
        public override void Merge(BaseBuff newer)
        {
            if (newer is SongOfLifeBuff)
                source = newer.source;
            Level = Mathf.Min(Level + 1, StackLimit());
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

            // 命座参数（施加者视角——数值随施法者命座成长；source 亡佚回落持有者=基础值）
            var (cLevel, talent) = SourceConstellation();
            int stacks = Mathf.Max(1, Level);
            int energyGainPerLayer = (cLevel >= 1
                ? (talent != null ? talent.GetInt(SkillParamKey.C1EnergyGain, EnergyGainPerTurnBase * 2) : EnergyGainPerTurnBase * 2)
                : EnergyGainPerTurnBase);
            int radius = Radius + (cLevel >= 2
                ? (talent != null ? talent.GetInt(SkillParamKey.C2Radius, 1) : 1)
                : 0);
            bool attachAllies = cLevel < 2; // 2命起：不再为我方角色附着元素

            // 数值基准=施加者（施放闪耀奇迹的芭芭拉）；无施加者回落持有者（BurnBuff 伤害归属同款兜底）
            var attacker = source != null ? source : owner;
            string attackerId = attacker.GetUnitComponent<UnitIdentity>()?.UnitID ?? ownerId;
            var attackerStats = attacker.GetUnitComponent<UnitStats>();
            // 逐层口径（2026-10-01 拍板④「2层相当于有两个此buff，应当各弹一次」）：伤害/治疗/元能/理智
            // 逐层各弹一次（第 i 层时刻=i×BuffLayerStaggerSeconds 错峰；Host 状态恒即时结算）——元能=
            // 每层一枚 EnergyEffect（BuffTickGain 合并键含层时刻=逐层条目不互吞）；理智=每层一枚 +1
            //（同日拍板②；命令层按目标合并总值防漂移）；附着=每目标一枚（覆盖幂等；敌方附着为
            // 统一拍板新增——tick 参与反应链，docs/18 决策二十三）
            // 治疗基准=施加者（芭芭拉）最大生命（与伤害同基准单位；float 末点 FloorToInt 同 ResolveHealAmount 口径）
            int healPerLayer = (attackerStats != null ? Mathf.FloorToInt(attackerStats.GetStatStruct(StatType.HP).Max * HealPercentPerTurn / 100f) : 0);

            foreach (var kv in Sim.Units)
            {
                var unit = kv.Value;
                var identity = unit.GetUnitComponent<UnitIdentity>();
                if (identity == null) continue;
                var pos = Sim.GetPosition(unit);
                if (Math.Max(Math.Abs(pos.x - holderPos.x), Math.Abs(pos.y - holderPos.y)) > radius)
                    continue; // 出半径

                if (identity.Team != holderTeam)
                {
                    // 敌方（含尸体——尸体保留势力归属仍算敌人，鞭尸同 Burn 先例）：逐层各弹一次。
                    // 伤害=10% 施加者攻——**只声明命中**（PendingAuraHit，拍时刻=layer×0.15）：
                    // TurnResolver 回合末交错管道按拍排序过共享编译视图统一结算（反应预判+消耗+
                    // 每层附着=决策二十四终版「同时进行」——双异元素光环同拍交错互融）
                    for (int layer = 0; layer < stacks; layer++)
                    {
                        float beat = layer * BattleMetrics.BuffLayerStaggerSeconds;
                        effects.Add(new PendingAuraHit(attackerId, kv.Key, attacker, unit, ElementType.Hydro,
                            new DamageRequest
                            {
                                Attacker = attacker,
                                Target = unit,
                                Element = (int)ElementType.Hydro,
                                AttackPercent = DamagePercentPerTurn,
                            }, beat));
                    }
                }
                else
                {
                    // 我方存活（含持有者）：治疗/元能/理智逐层各弹一次+附着逐层声明
                    // （尸体不治疗——血量恒 0 铁律 docs/05 §5.4；理智恢复与治疗同拍结算）
                    if (BattleSimState.IsDead(unit)) continue;
                    // 治疗效率双乘区（2026-10-01 拍板「发起治疗者也应当乘治疗效率；自己治疗自己
                    // 不乘两次」——施法者=施加者芭芭拉〔若 +50% 效率则全环治疗 ×150%〕、受疗者=
                    // 各我方单位各自过效率〔凯亚 1命受疗 150%、协议核心 50% 同乘区〕；施奶自己=
                    // 同单位单次；ApplyHealEfficiency 单出口；基准 healPerLayer=施加者 5% MaxHp 不变）
                    int healForTarget = EffectCompiler.ApplyHealEfficiency(attacker, unit, healPerLayer);
                    if (healForTarget > 0)
                        for (int layer = 0; layer < stacks; layer++)
                            effects.Add(new HealEffect(attackerId, kv.Key, healForTarget)
                            {
                                HitSeconds = layer * BattleMetrics.BuffLayerStaggerSeconds,
                            });
                    if (energyGainPerLayer > 0)
                        for (int layer = 0; layer < stacks; layer++)
                            effects.Add(new EnergyEffect(kv.Key, energyGainPerLayer, EnergyEffect.CategoryBuffTickGain)
                            {
                                HitSeconds = layer * BattleMetrics.BuffLayerStaggerSeconds,
                            });
                    // 理智恢复逐层（2026-10-01 拍板②「+1 应当也是每层的效果」）：每层各一枚 +1
                    // ——命令层按目标合并总值（TurnResolver 发射，防状态/命令漂移）
                    for (int layer = 0; layer < stacks; layer++)
                        effects.Add(new SanityEffect(kv.Key, SanityGainPerTurn));
                    // 附着我方（0~1命）：逐层纯附着声明（Request=null）——同进交错管道，敌方异元素
                    // 光环 tick 同拍可见可反应（挂水的我方被敌方冰棱 tick 冻结等）
                    if (attachAllies)
                        for (int layer = 0; layer < stacks; layer++)
                            effects.Add(new PendingAuraHit(attackerId, kv.Key, attacker, unit, ElementType.Hydro,
                                null, layer * BattleMetrics.BuffLayerStaggerSeconds));
                }
            }
            return effects;
        }

        /// <summary>当前叠层上限（0命=1；3命起=1+C3StackLimit〔=2〕——按施加者命座与命座技能参数）</summary>
        private int StackLimit()
        {
            var (cLevel, talent) = SourceConstellation();
            return 1 + (cLevel >= 3 ? (talent != null ? talent.GetInt(SkillParamKey.C3StackLimit, 1) : 1) : 0);
        }
    }
}
