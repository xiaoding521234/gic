using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 元素反应预判结果（附着实参由调用方从片内编译视图读出——快照初值+消耗/覆盖随编译序推进；
    /// 纯数据，不改状态）
    /// </summary>
    public class ReactionOutcome
    {
        public bool HasReaction;

        /// <summary>反应类型（BattleCommand.ReactionKindMelt / ReactionKindFreeze / ReactionKindVaporize /
        /// ReactionKindSuperconduct；0=无）——ReactionEffect/Reaction 命令/Damage 命令反应标记的载体
        /// （2026-09-22 接线：反应发生对客户端可见）</summary>
        public int ReactionType;

        /// <summary>反应提供的易伤增量（并入本次伤害易伤乘区，docs/18 决策五：同乘区加法并入）——
        /// 已含级别乘数（融化=50%×级别）</summary>
        public float VulnerabilityBonus;

        /// <summary>反应提供的增伤增量（并入本次伤害增伤乘区——蒸发=增伤 50%×级别，与融化的易伤
        /// 分区，docs/06 §反应表；同乘区加法并入；已含级别乘数）</summary>
        public float DamageBonusDelta;

        /// <summary>反应施加的 Buff 族类型（-1=无；冻结=水+冰→Freeze、超导=冰+雷→DefenseDown）</summary>
        public int BuffType = -1;

        /// <summary>反应 Buff 的具名身份键（决策七十四超导批：超导减防走具名资产——null=按族首
        /// 资产回落〔冻结既有路径〕，非空=CompileElementalDamage 直接携带进 ApplyBuffEffect）</summary>
        public string BuffKey;

        /// <summary>反应级别（决策七十四多层公式：=min(双方层数)——2层火+2层冰=2级融化；
        /// 安柏双箭=两发各 1 层=两发各 1 级，docs/06 §6.3「多层消耗时强化反应级别」）</summary>
        public int Level = 1;

        /// <summary>从**附着侧**消耗的层数（=Level；附着侧剩余层仍附着——docs/06 §6.3「多余的元素
        /// 仍然附着」双向适用；等层时剩余 0=零残留，决策二十五）</summary>
        public int DyedConsumed;

        /// <summary>从**冻结 Buff** 消耗的回合数（=Level；仅冻结层路径〔PreviewFreezeLayer〕——
        /// 每个回合相当于 1 层冰，决策七十四「冻结 Buff 作为冰元素参与反应」）</summary>
        public int FreezeConsumed;
    }

    /// <summary>
    /// 元素反应判定器（B4 融化+冻结两例，2026-09-22 补蒸发三例，docs/18 决策三；2026-10-15 决策七十四
    /// 超导+多层级别公式+冻结层三扩）。
    /// 纯判定（读目标附着 + 来袭元素 → 产出易伤/增伤增量/控制效果），状态修改全部走 BattleEffect：
    /// - 融化（火+冰 / 冰+火）：易伤 +50%×级别 并入本次伤害；消耗被反应附着
    /// - 蒸发（火+水 / 水+火）：增伤 +50%×级别 并入本次伤害；消耗被反应附着
    /// - 冻结（水+冰 / 冰+水）：冰冻 2 回合（ApplyBuffEffect 产出）；消耗被反应附着
    /// - 超导（冰+雷 / 雷+冰）：施加具名减防 Buff「Buff_Superconduct 超导减防」×级别层（决策七十四）；
    ///   消耗被反应附着（减防数值=资产单源，docs/06 §反应表「防御减少 5×级别」）
    /// - 其它组合：无反应（同元素=刷新附着，由 AttachElementEffect 覆盖）
    /// 级别公式（决策七十四，docs/06 §6.3 全量落地）：Level=min(附着层数, 来袭层数)——1:1 消耗
    /// （DyedConsumed=Level），多余层由调用方附着（等层零残留=不附着，决策二十五维持）。
    /// **冻结层入口（PreviewFreezeLayer，决策七十四）**：附着为空时冻结 Buff 作为第二冰源参与
    /// 融化（火）/超导（雷）——每回合=1 层、消耗 FreezeConsumed=Level 层；**水+冻结不反应**
    /// （拍板「唯独冰水不反应，正常附着」——水照常附着，后续冰来袭经附着路径触发冻结刷新）。
    /// 命中后的新附着统一由 AttachElementEffect 承载（覆盖=消耗被反应附着）。
    /// </summary>
    public static class ElementReactionResolver
    {
        /// <summary>融化易伤加成（×级别，docs/06 §反应表）——2026-10-20 审查批#5 资产化：
        /// 真源=ElementFactionConfig.meltVulnerabilityBonus（调反应平衡改资产零代码）；
        /// 配置缺载回落代码基线 0.5（headless/异常时序兜底）</summary>
        public static float MeltVulnerabilityBonus
            => ElementFactionConfig.Instance != null ? ElementFactionConfig.Instance.meltVulnerabilityBonus : 0.5f;

        /// <summary>蒸发增伤加成（×级别）——同上，真源=ElementFactionConfig.vaporizeDamageBonus</summary>
        public static float VaporizeDamageBonus
            => ElementFactionConfig.Instance != null ? ElementFactionConfig.Instance.vaporizeDamageBonus : 0.5f;

        /// <summary>超导减防具名 Buff 资产名（buffKey=资产名，决策五十七「资产即身份」——
        /// 与冰棱减防（Buff_DefenseDown）异资产同族共存可叠加；常量登记于 GIC.Data.BuffKeys 单源）</summary>
        public const string SuperconductBuffKey = BuffKeys.Superconduct;

        /// <summary>附着侧反应预判（attachment 路径——compile 调用方从片内编译视图读实参）。
        /// 返回 outcome（HasReaction=false=无反应，无副作用）</summary>
        public static ReactionOutcome Preview(ElementType dyed, int dyedLayers, ElementType incoming, int incomingLayers)
        {
            var outcome = new ReactionOutcome();

            // 物理来袭不参与反应、不改附着（docs/06 §6.2 默认附着=元素伤害）
            if (incoming == ElementType.Physical)
                return outcome;

            if (dyed == ElementType.Physical)
                return outcome; // 无附着：附着侧无反应（冻结层入口走 PreviewFreezeLayer）

            // 级别公式（决策七十四）：min(双方层数)；1:1 消耗（DyedConsumed=级别）
            int level = Mathf.Max(1, Mathf.Min(Mathf.Max(1, dyedLayers), Mathf.Max(1, incomingLayers)));
            outcome.Level = level;
            outcome.DyedConsumed = level;

            // 融化：火 + 冰（docs/06）
            if (IsPair(dyed, incoming, ElementType.Pyro, ElementType.Cryo))
            {
                outcome.HasReaction = true;
                outcome.ReactionType = BattleCommand.ReactionKindMelt;
                outcome.VulnerabilityBonus = MeltVulnerabilityBonus * level;
                return outcome;
            }

            // 蒸发：火 + 水（docs/06）——增伤并入增伤乘区（与融化的易伤分属两区，伤害公式独立）
            if (IsPair(dyed, incoming, ElementType.Pyro, ElementType.Hydro))
            {
                outcome.HasReaction = true;
                outcome.ReactionType = BattleCommand.ReactionKindVaporize;
                outcome.DamageBonusDelta = VaporizeDamageBonus * level;
                return outcome;
            }

            // 冻结：水 + 冰（docs/06）——冰冻 2 × 级别 回合，重复取更长（FreezeBuff.Merge）
            if (IsPair(dyed, incoming, ElementType.Hydro, ElementType.Cryo))
            {
                outcome.HasReaction = true;
                outcome.ReactionType = BattleCommand.ReactionKindFreeze;
                outcome.BuffType = (int)BuffType.Freeze;
                return outcome;
            }

            // 超导：冰 + 雷（docs/06 §反应表，决策七十四实装）——施加具名减防 Buff ×级别层
            //（资产单源：每层减防 5/叠层上限 20/永久——Buff_Superconduct）
            if (IsPair(dyed, incoming, ElementType.Cryo, ElementType.Electro))
            {
                outcome.HasReaction = true;
                outcome.ReactionType = BattleCommand.ReactionKindSuperconduct;
                outcome.BuffType = (int)BuffType.DefenseDown;
                outcome.BuffKey = SuperconductBuffKey;
                return outcome;
            }

            outcome.DyedConsumed = 0; // 无反应不消耗
            return outcome;
        }

        /// <summary>冻结层反应预判（决策七十四「冻结 Buff 可作为冰元素参与反应」）：附着为空时
        /// 由调用方进入本口——冻结 Buff=第二冰源，每个回合相当于 1 层。
        /// 火来袭=融化（易伤 50%×级别）/ 雷来袭=超导（减防 Buff ×级别层）；**水来袭不反应**
        /// （拍板「唯独冰水不反应，正常附着」——出于平衡：水照常附着、可再与后续冰触发冻结刷新）；
        /// 冰来袭=同元素不反应；其余元素无冰反应对（休眠等未实装批次）。FreezeConsumed=级别回合。</summary>
        public static ReactionOutcome PreviewFreezeLayer(ElementType incoming, int incomingLayers, int freezeTurns)
        {
            var outcome = new ReactionOutcome();
            if (incoming != ElementType.Pyro && incoming != ElementType.Electro)
                return outcome; // 水（平衡拍板）/冰（同元素）/其余：不与冻结层反应

            int level = Mathf.Max(1, Mathf.Min(Mathf.Max(1, incomingLayers), Mathf.Max(1, freezeTurns)));
            outcome.Level = level;
            outcome.FreezeConsumed = level;

            if (incoming == ElementType.Pyro)
            {
                outcome.HasReaction = true;
                outcome.ReactionType = BattleCommand.ReactionKindMelt;
                outcome.VulnerabilityBonus = MeltVulnerabilityBonus * level;
            }
            else // Electro
            {
                outcome.HasReaction = true;
                outcome.ReactionType = BattleCommand.ReactionKindSuperconduct;
                outcome.BuffType = (int)BuffType.DefenseDown;
                outcome.BuffKey = SuperconductBuffKey;
            }
            return outcome;
        }

        private static bool IsPair(ElementType dyed, ElementType incoming, ElementType a, ElementType b)
        {
            return (dyed == a && incoming == b) || (dyed == b && incoming == a);
        }
    }
}
