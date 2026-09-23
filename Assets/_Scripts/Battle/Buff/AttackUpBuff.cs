using System.Collections.Generic;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 攻击提升（延奏 Buff，docs/07-势力机制/蒙德.md「延奏自身效果」）：
    /// 被协奏角色攻击力提升 BonusPerStack × Level（层数），持续 Duration 回合；
    /// 同类重复施加=叠层（至多 StackLimit 层）+ 时长累加（持续时间随层数叠加，docs/units/蒙德/安柏.md #4）。
    /// 时轮 B-S1b（2026-09-23）：数值单源=技能参数（ATKBonus/StackLimit/Duration）经
    /// ApplyBuffEffect.BuffValue 通道传入工厂，勿在此硬编码默认值以外另设来源。
    /// 生效载体=UnitStats 修改器（BaseFlat 区，与面板攻击同乘区基底）；层数变化时重挂修改器。
    /// </summary>
    public class AttackUpBuff : BaseBuff
    {
        /// <summary>每层攻击提升值（技能参数 ATKBonus，工厂注入）</summary>
        public int BonusPerStack = 10;

        /// <summary>叠层上限（技能参数 StackLimit，工厂注入；0=无上限约定不使用）</summary>
        public int StackLimit = 5;

        private StatModifier _modifier;

        public override BuffType Type => BuffType.AttackUp;

        public AttackUpBuff(int level, int bonusPerStack, int stackLimit, int turns)
        {
            Level = level;
            BonusPerStack = bonusPerStack;
            StackLimit = stackLimit;
            RemainingTurns = turns;
        }

        public override List<BattleEffect> OnTurnEnd() => new List<BattleEffect>(); // 纯增益：无回合结束效果，仅回合制计时

        /// <summary>同类重复施加：叠层（上限内）+ 时长累加；层数变化同步重挂修改器</summary>
        public override void Merge(BaseBuff newer)
        {
            if (newer is AttackUpBuff incoming)
            {
                BonusPerStack = incoming.BonusPerStack; // 以最新施加的技能参数为准（同技能参数恒定）
                StackLimit = incoming.StackLimit;
                Level = StackLimit > 0 ? System.Math.Min(Level + incoming.Level, StackLimit) : Level + incoming.Level;
            }
            RemainingTurns += newer.RemainingTurns;
            RefreshModifier();
        }

        public override void OnApplied()
        {
            RefreshModifier();
        }

        public override void OnRemoved()
        {
            DetachModifier();
        }

        /// <summary>层数→修改器同步（Remove+Add 重挂；BaseFlat 区与面板攻击同基底）</summary>
        private void RefreshModifier()
        {
            DetachModifier();
            var stats = owner?.GetUnitComponent<UnitStats>();
            if (stats == null) return;
            _modifier = new StatModifier(StatType.Attack, BonusPerStack * Level, StatModifierType.BaseFlat);
            stats.AddModifier(_modifier);
        }

        private void DetachModifier()
        {
            if (_modifier == null) return;
            owner?.GetUnitComponent<UnitStats>()?.RemoveModifier(_modifier);
            _modifier = null;
        }
    }
}
