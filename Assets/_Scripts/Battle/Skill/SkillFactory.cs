using GIC.Data;
using GIC.Framework;
namespace GIC.Battle
{


    /// <summary>
    /// 技能工厂（2026-09-30 旧轨退役：技能类注册轨整体拆除——注册数=0 实证、四个手写技能类已随
    /// B-1 原子库迁移退役删除，现行全技能=纯配置 ConfiguredSkill，docs/18 决策九 D5 收口；
    /// 反射扫描/注册表/手写注册 API 均为死重，批8 复审挂账本批拍板拆除）。
    /// 两层分流：effects 非空 → ConfiguredSkill 数据驱动通用类（效果原子管线，加技能=配 effects 零代码）；
    /// 空 → UnimplementedSkill 占位。
    /// </summary>
    public static class SkillFactory
    {
        /// <summary>
        /// 创建技能实例。两层分流（旧三层中的「注册专属类优先」分支已随技能类退役删除）：
        /// ① effects 非空 → ConfiguredSkill 数据驱动通用类（效果原子管线）；
        /// ② effects 空 → UnimplementedSkill 占位（保 unit.Skills 与 UnitConfig.skills
        /// **索引严格对齐**——ActionData.skillIndex 双端同源映射，占位不可施放）而非 null
        /// （null 会令 InitSkills 跳过 → 后续技能索引整体前移错位，2026-09-18 B4 实证防）。
        /// </summary>
        public static BaseSkill CreateWithData(SkillConfig.SkillData data)
        {
            if (data == null) return null;

            BaseSkill skill;
            if (data.skillID != SkillName.None && data.HasEffects)
            {
                skill = new ConfiguredSkill(); // 数据驱动通用类（加技能=配 effects 零代码）
            }
            else
            {
                if (data.skillID != SkillName.None)
                    GICLog.Warn($"[SkillFactory] 未配置效果原子: {data.skillID} → 占位（不可施放）");
                skill = new UnimplementedSkill(); // None 配置条目=空槽，占位保索引对齐（不告警）
            }

            skill?.Init(data);
            return skill;
        }
    }
}
