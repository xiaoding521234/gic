using UnityEngine;
using UnityEngine.Serialization;
namespace GIC.Data
{


    /// <summary>歌声之环配置（芭芭拉闪耀奇迹光环——永久；命座成长仍走施加者 Talent 技能参数，与本资产正交）</summary>
    [CreateAssetMenu(fileName = "Buff_SongOfLife", menuName = "Game/BuffConfig 歌声之环")]
    public class SongOfLifeBuffConfig : BuffConfig
    {
        [Header("每 2 秒对半径内敌人的伤害（% 施加者攻击力/层）")]
        [FormerlySerializedAs("每回合伤害百分比")]
        public int 每拍伤害百分比 = 10;

        [Header("每 2 秒对半径内我方的治疗（% 施加者最大生命值/层——基准=芭芭拉自身）")]
        [FormerlySerializedAs("每回合治疗百分比")]
        public int 每拍治疗百分比 = 5;

        [Header("基础元能获取（0命；2命起=C2EnergyGain 命座技能参数取代——决策七十二调换+复用丽莎同键）")]
        public int 基础元能获取 = 5;

        [Header("每 2 秒理智恢复（每层一枚，命令按目标合并总值）")]
        [FormerlySerializedAs("每回合理智恢复")]
        public int 每拍理智恢复 = 1;

        [Header("作用半径（切比雪夫，格；1命起 +C1Radius 命座技能参数——决策七十二调换+键名随生效层改）")]
        public int 作用半径 = 1;

        /// <summary>光环半径单源（2026-10-20 审查批#6）：AuraRadiusOf/AuraRadiusOfBuffType/
        /// BattleHud.SelfCastAuraRadiusOf 三消费方同源读</summary>
        public override int AuraRadius => 作用半径;

        /// <summary>1命起 +C1Radius（决策七十二调换后 1命生效）——SongOfLifeBuff.OnEffectBeat
        /// 与客户端预估共用</summary>
        public override int ConstellationAuraBonus(int constellation, SkillConfig.SkillData talent)
            => constellation >= 1 ? (talent != null ? talent.GetInt(SkillParamKey.C1Radius, 1) : 1) : 0;

        /// <summary>关联描述占位符解析（{AuraDamagePercent} 等——数值=本资产字段单源，金色高亮由构建层统一）</summary>
        public override int? ResolveRelatedPlaceholder(string key)
        {
            switch (key)
            {
                case KeyAuraDamagePercent: return 每拍伤害百分比;
                case KeyAuraHealPercent: return 每拍治疗百分比;
                case KeyAuraEnergyGain: return 基础元能获取;
                case KeyAuraSanityGain: return 每拍理智恢复;
                case KeyAuraRadius: return 作用半径;
            }
            return null;
        }

        /// <summary>占位符数值基准（与技能参数 baseType 同构——%与基底名随数值整体金色高亮，2026-10-07 返修四）</summary>
        public override SkillBaseType? RelatedPlaceholderBaseType(string key)
        {
            switch (key)
            {
                case KeyAuraDamagePercent: return SkillBaseType.BasedOnAttack;
                case KeyAuraHealPercent: return SkillBaseType.BasedOnMaxHealth;
            }
            return null;
        }
    }
}
