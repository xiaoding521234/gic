using System.Collections.Generic;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 属性增益 Buff 基类（2026-09-25 三轮审查 S7 提取：AttackUp/MoveSpeedUp 此前 100% 同构复制——
    /// 加第三个属性 Buff 勿再复制一份，子类=3 行声明注入 StatType+BuffType 即成）。
    /// 生效载体=UnitStats 修改器（BaseFlat 区，与面板属性同乘区基底）；叠层（上限内）+时长累加，
    /// 层数变化时 Remove+Add 重挂修改器。数值单源=技能参数经 ApplyBuffEffect 参数通道
    /// （BuffValue/StackLimit/DurationTurns）由工厂注入，勿在子类硬编码。
    /// 注意：BaseBuff.Level 此处语义=当前**层数**（非 BurnBuff 的级别）——同名二字，快照消费时勿混。
    /// </summary>
    public abstract class StatBuff : BaseBuff
    {
        private readonly StatType _statType;
        private StatModifier _modifier;

        /// <summary>每层加成值（技能参数注入）</summary>
        public int BonusPerStack { get; private set; }

        /// <summary>叠层上限（技能参数注入；0=无上限约定不使用）</summary>
        public int StackLimit { get; private set; }

        protected StatBuff(StatType statType, int level, int bonusPerStack, int stackLimit, int turns)
        {
            _statType = statType;
            Level = level;
            BonusPerStack = bonusPerStack;
            StackLimit = stackLimit;
            RemainingTurns = turns;
        }

        public override List<BattleEffect> OnTurnEnd() => new List<BattleEffect>(); // 纯增益：无回合结束效果，仅回合制计时

        /// <summary>同类重复施加：叠层（上限内）+ 时长累加；层数变化同步重挂修改器</summary>
        public override void Merge(BaseBuff newer)
        {
            if (newer is StatBuff incoming)
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

        /// <summary>层数→修改器同步（Remove+Add 重挂）</summary>
        private void RefreshModifier()
        {
            DetachModifier();
            var stats = owner?.GetUnitComponent<UnitStats>();
            if (stats == null) return;
            _modifier = new StatModifier(_statType, BonusPerStack * Level, StatModifierType.BaseFlat);
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
