using UnityEngine;
using UnityEngine.Localization;
using GIC.Tool;
namespace GIC.Data
{


    /// <summary>
    /// 技能参数键枚举 — 替代原来的裸字符串 key
    /// </summary>
    public enum SkillParamKey
    {
        [InspectorName("无")]
        None = 0,

        // 伤害类
        [InspectorName("伤害")]
        Damage = 1,

        [InspectorName("单次伤害")]
        SingleDamage = 2,

        // 治疗
        [InspectorName("治疗")]
        Heal = 3,

        // 数量/次数
        [InspectorName("次数")]
        Count = 4,

        [InspectorName("采集次数")]
        CollectCount = 16,

        [InspectorName("伤害次数")]
        DamageCount = 17,

        // 距离/范围
        [InspectorName("移动距离")]
        MoveDistance = 5,

        [InspectorName("生效半径")]
        EffectRadius = 6,

        [InspectorName("施法半径")]
        CastRadius = 7,

        // 属性提升
        [InspectorName("攻击提升")]
        ATKBonus = 8,

        // 消耗
        [InspectorName("元能消耗")]
        EnergyCost = 9,

        [InspectorName("摩拉消耗")]
        MoraCost = 10,

        [InspectorName("体力消耗")]
        StaminaCost = 11,

        // 持续
        [InspectorName("持续回合")]
        Duration = 12,

        // 命座
        [InspectorName("1命移速提升")]
        C1MoveSpeed = 13,

        [InspectorName("1命视野提升")]
        C1Vision = 14,

        [InspectorName("2命移速提升")]
        C2MoveSpeed = 15,

        [InspectorName("2命迷雾元能阈值")]
        C2FogEnergyThreshold = 18,

        [InspectorName("2命迷雾元能获取")]
        C2FogEnergyGain = 19,

        [InspectorName("视野暴露持续回合")]
        VisionDuration = 20,

        [InspectorName("召唤数量")]
        ShardCount = 21,

        [InspectorName("飞行距离")]
        ProjectileDistance = 22,

        [InspectorName("治疗半径")]
        HealRadius = 23,

        // 凯亚命座
        [InspectorName("1命治疗效率")]
        C1HealEfficiency = 24,

        [InspectorName("1命吸血")]
        C1LifeSteal = 25,

        [InspectorName("2命叠层上限")]
        C2StackLimit = 26,

        [InspectorName("2命防御减少")]
        C2DefenseReduce = 27,

        [InspectorName("3命叠层上限")]
        C3StackLimit = 28,

        [InspectorName("3命生效半径")]
        C3Radius = 29,

        // 凯亚延奏
        [InspectorName("移速提升")]
        MoveSpeedBonus = 30,

        // 凯亚战技
        [InspectorName("伤害距离")]
        DamageDistance = 31,

        // 芭芭拉命座（2026-10-15 决策七十二调换+二/三次返修：C1Radius=1命生效半径〔原 C2Radius 改名——
        // 键名随生效层，枚举值 33 不变零资产迁移〕；C1EnergyGain=32 退役占位——显示名回归历史语义
        // 「1命元能增加」（2026-10-15 审查拍板「应当对应的是1命元能增加」：调换批误改「2命获取元能」，
        // 与现役 C2EnergyGain=55「2命元能增加」在参数下拉并存=误选陷阱；枚举值/键保留=ATKBonus 先例；
        // 2命元能改用丽莎 C2EnergyGain=55 复用——三次返修拍板「枚举只和显示名对应，两个名称刚好一样」））
        [InspectorName("1命元能增加")]
        C1EnergyGain = 32,

        [InspectorName("1命生效半径")]
        C1Radius = 33,

        // 安柏采集
        [InspectorName("采集元能获取")]
        CollectEnergyGain = 34,

        [InspectorName("采集体力获取")]
        CollectStaminaGain = 35,

        // 安柏命座
        [InspectorName("2命元能上限增加")]
        C2EnergyLimit = 36,

        // 通用
        [InspectorName("叠加上限")]
        StackLimit = 37,

        // 行秋
        [InspectorName("雨帘剑获取")]
        RainSwordGain = 38,

        [InspectorName("每柄摩拉")]
        MoraPerSword = 39,

        [InspectorName("移速减少")]
        MoveSpeedReduction = 40,

        [InspectorName("契约补偿元能")]
        ContractEnergyCompensation = 41,

        [InspectorName("拒签惩罚元能")]
        ContractRefusalPenalty = 42,

        [InspectorName("掠夺折率")]
        PlunderRate = 43,

        [InspectorName("解除移动次数")]
        ContractBreakMoveCount = 44,

        [InspectorName("1命雨帘剑上限")]
        C1RainSwordLimit = 45,

        [InspectorName("1命协同元能")]
        C1SynergyEnergyGain = 46,

        [InspectorName("2命击倒获取剑数")]
        C2KillSwordGain = 47,

        [InspectorName("2命溢出攻击半径")]
        C2OverflowRadius = 48,

        [InspectorName("3命协同韧性")]
        C3SynergyTenacityGain = 49,

        // 通用
        [InspectorName("元能增加")]
        EnergyGain = 50,

        // 丽莎
        [InspectorName("惩罚伤害")]
        PenaltyDamage = 51,

        [InspectorName("1命飞针刺索敌半径")]
        C1NeedleRadius = 52,

        [InspectorName("1命飞针刺伤害")]
        C1NeedleDamage = 53,

        [InspectorName("1命飞针刺次数")]
        C1NeedleCount = 54,

        [InspectorName("2命元能增加")]
        C2EnergyGain = 55, // 丽莎+芭芭拉共用（2026-10-15 决策七十二三次返修：芭芭拉2命元能复用同键——键只对应显示名，两角色语义同名）

        [InspectorName("2命闪电触发上限")]
        C2TriggerLimit = 56,

        [InspectorName("3命增伤提升")]
        C3DamageBonus = 57,

        // 琴
        [InspectorName("近战距离")]
        CloseRange = 58,

        [InspectorName("击退格数")]
        Knockback = 59,

        [InspectorName("命中距离上限")]
        MaxRange = 60,

        [InspectorName("风场格数")]
        WindFieldRange = 61,

        [InspectorName("冲刺距离")]
        DashRange = 62,

        [InspectorName("移速提升持续")]
        MoveSpeedDuration = 63,

        [InspectorName("移速伤害")]
        MoveSpeedDamage = 64,

        [InspectorName("瞬移范围")]
        TeleportRange = 65,

        [InspectorName("全队护盾")]
        C0Shield = 66,

        [InspectorName("1命命中距离提升")]
        C1RangeBonus = 67,

        [InspectorName("2命风场持续增加")]
        C2DurationBonus = 68,

        [InspectorName("2命牵引额外伤害")]
        C2ThornDamage = 69,

        // 诺艾尔
        [InspectorName("蓄力回合")]
        ChargeTurns = 70,

        [InspectorName("护盾")]
        Shield = 71,

        [InspectorName("1命护盾转元能比例")]
        C1ShieldToEnergyRate = 72,

        [InspectorName("2命晕眩持续")]
        C2StunDuration = 73,

        [InspectorName("2命护盾")]
        C2Shield = 74,

        // 凯亚战技（文档有、早期落地遗漏）
        [InspectorName("掠夺摩拉")]
        MoraPlunder = 75,

        // 凯亚命座（2026-10-08 寒冰之棱重构：上限常驻 4，命座改召唤数量增补）
        [InspectorName("2命召唤数量")]
        C2ShardBonus = 76,

        [InspectorName("3命召唤数量")]
        C3ShardBonus = 77,

        // 凯亚命座（2026-10-15 决策七十二「3命额外加元能上限+60」；半径通道复用 C3Radius=29）
        [InspectorName("3命元能上限增加")]
        C3EnergyLimit = 78,

        // 物品参数（2026-10-16 首个可打出物品批·描述对齐返修：物品 customParams 字符串键=枚举名同构
        // ——描述占位符/参数表显示/效果值三方单源；消耗类键=绝对量正数存储、消费侧取负——
        // SkillCostEntry 消耗语义先例；首个消费方=蒲公英酒）
        [InspectorName("理智消耗")]
        SanityCost = 79,

        [InspectorName("治疗量")]
        HealAmount = 80,

        // 装备参数（2026-10-18 装备批·兔兔伯爵）：物品 customParams 字符串键=枚举名同构（食物先例）
        // ——描述占位符/参数表显示/格挡与耐久真源三方单源（Host UnitEquipment 读 customParams）
        [InspectorName("伤害抵挡阈值")]
        BlockThreshold = 81,

        [InspectorName("耐久")]
        Durability = 82,

        // 装备耐久爆炸参数（2026-10-18 决策八十一·兔兔伯爵增强「耐久每次减少，对半径1内敌人
        // 造成100%攻击力火伤」）：ExplodePercent=耐久减少触发的爆炸伤害（%攻击力，0=不触发）；
        // ExplodeRadius=爆炸半径（格，切比雪夫）——TurnResolver 格挡点编译消费
        [InspectorName("耐久爆炸伤害")]
        ExplodePercent = 83,

        [InspectorName("耐久爆炸半径")]
        ExplodeRadius = 84,

        // 装备槽内堆叠上限（2026-10-18 决策八十一追记·勾玉回归堆叠方案：「堆叠上限为3（指的是
        // 装备槽，而不是手牌堆叠上限），描述加上每次装备仅装备1个」）：配此参数（>0）=堆叠配件
        // ——同槽可叠 N 枚、每张物品牌装备 +1 层、每层全效（与耐久配件〔Durability 参数〕互斥；
        // 未来魔法箭矢类同语义）。连携勾玉=首个堆叠配件（槽内层数=连携储存数）
        [InspectorName("装备堆叠上限")]
        EquipStackLimit = 85,

        // 命中伤害范围真圆（决策九十五 P3，蓝图 §4.2「范围=真圆」）：投射物命中点/线爆命中域
        // 半径（厘米；1 格=100——箭矢/箭雨/水球拍板 50=0.5 格）；**0=单体**（仅接触命中者，
        // 取代旧「同格成员聚合」方形域）。数值单源=customParams 本键，时轮发射声明携带
        // （ProjectileEffect.AoeRadiusCm），ProjectileResolver 消费
        [InspectorName("命中半径")]
        AoERadius = 86,
    }

    /// <summary>
    /// SkillParamKey 扩展方法
    /// </summary>
    public static class SkillParamKeyExtensions
    {
        /// <summary>
        /// 获取参数键在本地化表中的 Entry Key
        /// </summary>
        private static string GetEntryKey(this SkillParamKey key)
        {
            return key.ToString();
        }

        /// <summary>
        /// 创建用于本地化系统的 LocalizedString 对象
        /// </summary>
        private static LocalizedString GetLocalizedString(this SkillParamKey key)
        {
            return new LocalizedString(TableName.SkillParamName.ToString(), key.GetEntryKey());
        }

        /// <summary>
        /// 创建 Entry 对象（用于 TextCombiner）
        /// </summary>
        public static TextEntry GetEntry(this SkillParamKey key, string leadingSeparator = "")
        {
            LocalizedString localizedString = key.GetLocalizedString();
            return new TextEntry(localizedString, leadingSeparator);
        }
    }

}



