using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 歌声之环（B-3 ②，芭芭拉闪耀奇迹的活体分支——docs/units/蒙德/芭芭拉.md「歌声之环」节）：
    /// 永久光环（不计时，持有者倒下消失——RemoveOnHolderDeath 覆写 true），上限 1 层。
    /// 每回合结束：对持有者切比雪夫半径 1 内敌人造成 10% 攻击力水伤（含尸体——鞭尸同 Burn 先例）；
    /// 对半径内我方**存活**单位（含持有者）治疗 10 生命值并附着水元素。
    /// 数值基准=施加者（source=施放闪耀奇迹的芭芭拉——技能数值随施法者成长；无施加者回落持有者，
    /// 与 BurnBuff 伤害归属同款兜底）；Buff tick 伤害=平直值不进 DamagePipeline（Burn 先例：tick
    /// 伤害无反应/防御乘区）。
    /// 「恢复 1 理智」暂未落地——战斗协议无理智字段（UnitState 无 sanity），理智入战斗协议后补。
    /// 命座扩展（C1 元能/C2 半径+免附着/C3 叠层上限）随 B8 命座批（参数届时经 ApplyBuff 通道注入）。
    /// </summary>
    public class SongOfLifeBuff : BaseBuff
    {
        /// <summary>每回合对半径内敌人的伤害（% 攻击力；docs/units/蒙德/芭芭拉.md）</summary>
        public const int DamagePercentPerTurn = 10;

        /// <summary>每回合对半径内我方的治疗（固定值）</summary>
        public const int HealPerTurn = 10;

        /// <summary>作用半径（切比雪夫，格）</summary>
        public const int Radius = 1;

        public override BuffType Type => BuffType.SongOfLife;

        public override bool RemoveOnHolderDeath => true; // 持有者倒下，歌声之环消失

        public SongOfLifeBuff()
        {
            Level = 1;          // 恒 1 层（上限 1；命中座 C3 再扩）
            RemainingTurns = -1; // 永久（IsPermanent 标记——不计时，倒下即失）
        }

        /// <summary>同类重复施加：上限 1 层——不叠层不续时（永久无时可续），仅刷新施加者
        /// （数值基准随之更新）。</summary>
        public override void Merge(BaseBuff newer)
        {
            if (newer is SongOfLifeBuff)
                source = newer.source;
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

            // 数值基准=施加者（施放闪耀奇迹的芭芭拉）；无施加者回落持有者（BurnBuff 伤害归属同款兜底）
            var attacker = source != null ? source : owner;
            string attackerId = attacker.GetUnitComponent<UnitIdentity>()?.UnitID ?? ownerId;
            int attack = attacker.GetUnitComponent<UnitStats>()?.Attack ?? 0;
            int damage = attack * DamagePercentPerTurn / 100; // 末点截断口径（int 除法=FloorToInt 等价）

            foreach (var kv in Sim.Units)
            {
                var unit = kv.Value;
                var identity = unit.GetUnitComponent<UnitIdentity>();
                if (identity == null) continue;
                var pos = Sim.GetPosition(unit);
                if (Math.Max(Math.Abs(pos.x - holderPos.x), Math.Abs(pos.y - holderPos.y)) > Radius)
                    continue; // 出半径

                if (identity.Team != holderTeam)
                {
                    // 敌方（含尸体——尸体保留势力归属仍算敌人，鞭尸同 Burn 先例）
                    if (damage > 0)
                        effects.Add(new DamageEffect(attackerId, kv.Key, damage, (int)ElementType.Hydro));
                }
                else
                {
                    // 我方存活（含持有者）：治疗+水附着（尸体不治疗——血量恒 0 铁律 docs/05 §5.4）
                    if (BattleSimState.IsDead(unit)) continue;
                    effects.Add(new HealEffect(attackerId, kv.Key, HealPerTurn));
                    effects.Add(new AttachElementEffect(attackerId, kv.Key, (int)ElementType.Hydro));
                }
            }
            return effects;
        }
    }
}
