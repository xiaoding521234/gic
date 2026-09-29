using UnityEngine;
namespace GIC.Data
{


    /// <summary>
    /// 单位操控层级（D 批次「操控分层与号令系统」，docs/active/32 §1 规则总表——2026-09-29 拍板）。
    /// 层级由既有星级推导（零配置迁移），术语=原神风格重命名：
    /// 眷属=1-2 星（原低级单位，AI 自主小兵）/ 伙伴=3-4 星（原高级单位主体，AI 自主+可被号令）/
    /// 魔神=5 星（原高级单位顶层单立，玩家全手操）。全项目门控逻辑一律经 <see cref="UnitTierHelper.FromStars"/>
    /// 换算后按枚举分支，禁止在代码里散落星级区间判断（先例：「与友方互不阻挡」单字段方案教训）。
    /// </summary>
    public enum UnitTier
    {
        [InspectorName("眷属（1-2星 AI 自主）")] Familiar = 1,
        [InspectorName("伙伴（3-4星 AI 自主+可号令）")] Companion = 2,
        [InspectorName("魔神（5星 玩家全手操）")] Archon = 3,
    }

    /// <summary>
    /// 层级换算与层级表唯一真源（docs/active/32 §2/§5.2）：
    /// - FromStars：星级→层级单一出口（1-2→眷属、3-4→伙伴、5→魔神；越界拍平到最近档）；
    /// - StaminaCostOf：体力成本层级表（眷属 0 / 伙伴 5 / 魔神 10）——体力成本由**施法者层级**决定、
    ///   非技能属性（同一技能资产被多层级单位共享：Common_Walk 全员共享、凯亚霜袭被丘丘人引用，
    ///   技能级 costs 天然无法表达 per-tier 价格）；眷属 0=门槛恒过的自然表达，豁免特判代码退役。
    /// </summary>
    public static class UnitTierHelper
    {
        /// <summary>星级→层级单一换算出口（全项目门控只看此枚举，勿散写星级区间判断）</summary>
        public static UnitTier FromStars(int starLevel)
        {
            if (starLevel <= 2) return UnitTier.Familiar;
            if (starLevel <= 4) return UnitTier.Companion;
            return UnitTier.Archon;
        }

        /// <summary>体力成本层级表（docs/active/32 §5.1，真源=此一处）：眷属 0 / 伙伴 5 / 魔神 10。
        /// 消费方=ResourceGate（Stamina 条目按施法者层级强制换算——技能资产声明值=基准/校验值，
        /// 实际扣值=此函数）+ HUD 置灰镜像（HasSkillResources 同口径）+ 编辑器校验行。</summary>
        public static int StaminaCostOf(UnitTier tier)
        {
            switch (tier)
            {
                case UnitTier.Familiar: return 0;
                case UnitTier.Companion: return 5;
                case UnitTier.Archon: return 10;
                default: return 0;
            }
        }
    }
}
