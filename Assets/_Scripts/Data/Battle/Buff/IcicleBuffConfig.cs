using UnityEngine;
namespace GIC.Data
{


    /// <summary>寒冰之棱配置（凯亚凛冽轮舞光环——永久、倒下仍生效；命座成长仍走施加者 Talent 技能参数）</summary>
    [CreateAssetMenu(fileName = "Buff_Icicle", menuName = "Game/BuffConfig 寒冰之棱")]
    public class IcicleBuffConfig : BuffConfig
    {
        [Header("每回合对半径内敌人的伤害（% 施加者攻击力/层——平值非按层）")]
        public int 每回合伤害百分比 = 20;

        [Header("碎裂时每层治疗（% 施加者攻击力）")]
        public int 碎裂治疗百分比 = 30;

        [Header("碎裂元能阈值（% 持有者元能上限——严格大于才触发）")]
        public int 碎裂元能阈值百分比 = 50;

        [Header("作用半径（切比雪夫，格；3命起 +C3Radius 命座技能参数）")]
        public int 作用半径 = 1;

        [Header("基础叠层上限（2命起 +C2StackLimit、3命再 +C3StackLimit）")]
        public int 基础叠层上限 = 2;

        [Header("2命 tick 命中附带减防 Buff 叠层上限")]
        public int 减防叠层上限 = 10;

        [Header("2命 tick 命中附带减防 Buff 持续回合（-1=永久，2026-10-07 拍板）")]
        public int 减防持续回合 = -1;

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
    }
}
