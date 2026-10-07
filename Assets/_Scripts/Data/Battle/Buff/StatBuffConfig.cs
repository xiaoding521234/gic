using UnityEngine;
namespace GIC.Data
{


    /// <summary>
    /// 属性增益 Buff 配置（加攻/加速/减防——StatBuff 族，2026-10-07 决策五十六返修三拍板
    /// 「buff 独立出去了，技能描述的配置里就不应该有 buff 的配置」：延奏技能资产不再携带
    /// buff 数值，每层加成/叠层上限/持续回合单源归本资产；ApplyBuff 注入通道非零仍优先覆写
    /// ——冰棱 2命 tick 的 C2DefenseReduce 命座参数流与决策五十四「技能实参＞模板」语义保留）。
    /// 同名文件硬规则（docs/14 §131①）——ScriptableObject 子类必须一文件一类。
    /// </summary>
    [CreateAssetMenu(fileName = "BuffConfig", menuName = "Game/BuffConfig 属性增益（加攻/加速/减防）")]
    public class StatBuffConfig : BuffConfig
    {
        [Header("每层加成（延奏类 buff 数值单源；ApplyBuff 注入通道非零时被覆写〔冰棱2命命座参数流〕）")]
        public int 每层加成 = 10;

        [Header("叠层上限")]
        public int 叠层上限 = 5;

        [Header("持续回合（-1=永久——关联描述 {DurationTurns} 渲染本地化「永久」）")]
        public int 持续回合 = -1;

        /// <summary>关联描述占位符（{BonusPerStack}/{StackLimit}/{DurationTurns}——延奏技能资产已剥离
        /// buff 参数，描述数值全部读本资产）</summary>
        public override int? ResolveRelatedPlaceholder(string key)
        {
            switch (key)
            {
                case KeyStatBonus: return 每层加成;
                case KeyStatStackLimit: return 叠层上限;
                case KeyStatDurationTurns: return 持续回合;
            }
            return null;
        }
    }
}
