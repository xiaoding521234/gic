using UnityEngine;
namespace GIC.Data
{


    /// <summary>寒冰之棱配置（凯亚凛冽轮舞光环——永久、倒下仍生效；**上限/半径常驻资产值**
    /// ——2026-10-08 拍板「上限改为4（不再依靠命座）」，命座只经 C2/C3ShardBonus 增补每次施放的召唤量）</summary>
    [CreateAssetMenu(fileName = "Buff_Icicle", menuName = "Game/BuffConfig 寒冰之棱")]
    public class IcicleBuffConfig : BuffConfig
    {
        [Header("每回合对半径内敌人的伤害（% 施加者攻击力/层——平值非按层）")]
        public int 每回合伤害百分比 = 20;

        [Header("碎裂时每层治疗（% 施加者攻击力）")]
        public int 碎裂治疗百分比 = 30;

        [Header("碎裂元能阈值（% 持有者元能上限——严格大于才触发）")]
        public int 碎裂元能阈值百分比 = 50;

        [Header("作用半径（切比雪夫，格——常驻，不随命座）")]
        public int 作用半径 = 1;

        [Header("叠层上限（常驻——2026-10-08 拍板不再依靠命座；旧 C2/C3StackLimit 通道退役）")]
        public int 基础叠层上限 = 4;

        /// <summary>关联描述占位符解析（{AuraDamagePercent} 等——数值=本资产字段单源，金色高亮由构建层统一）</summary>
        public override int? ResolveRelatedPlaceholder(string key)
        {
            switch (key)
            {
                case KeyAuraDamagePercent: return 每回合伤害百分比;
                case KeyShatterHealPercent: return 碎裂治疗百分比;
                case KeyShatterEnergyThreshold: return 碎裂元能阈值百分比;
                case KeyAuraRadius: return 作用半径;
                case KeyBaseStackLimit: return 基础叠层上限;
            }
            return null;
        }

        /// <summary>占位符数值基准（与技能参数 baseType 同构——%与基底名随数值整体金色高亮，2026-10-07 返修四）</summary>
        public override SkillBaseType? RelatedPlaceholderBaseType(string key)
        {
            switch (key)
            {
                case KeyAuraDamagePercent: return SkillBaseType.BasedOnAttack;
                case KeyShatterHealPercent: return SkillBaseType.BasedOnAttack;
                case KeyShatterEnergyThreshold: return SkillBaseType.Percent; // 上下文百分比（持有者元能上限）——渲染 N% 无基底名
            }
            return null;
        }
    }
}
