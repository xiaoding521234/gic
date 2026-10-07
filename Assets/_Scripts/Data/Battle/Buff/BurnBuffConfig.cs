using UnityEngine;
namespace GIC.Data
{


    /// <summary>燃烧配置（反应类 DoT——无技能语境，本资产即真源；同名文件=脚本资产关联硬规则）</summary>
    [CreateAssetMenu(fileName = "Buff_Burn", menuName = "Game/BuffConfig 燃烧")]
    public class BurnBuffConfig : BuffConfig
    {
        [Header("每回合火伤（平直值——走 DamagePipeline 吃防御/易伤，决策二十三）")]
        public int 每回合伤害 = 10;

        [Header("持续回合 × 级别（级别=反应层数；草延长机制同构）")]
        public int 每级持续回合 = 3;
    }
}
