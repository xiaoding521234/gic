using UnityEngine;
using UnityEngine.Localization;
using GIC.Tool;
namespace GIC.Data
{


    /// <summary>
    /// 技能标签枚举（2026-10-11 决策六十四：技能盘按钮显示的功能标签——战斗 HUD 四键
    /// （战技/爆发/延奏）与被动技能盘图标显示本标签而非技能名；None=未配置，按钮回落显示技能名。
    /// 规范=UnitTag 同款：枚举+专用本地化表（TableName.SkillTag，Entry Id=枚举值对齐）+
    /// GetEntry 扩展；技能资产在 SkillData.技能标签 上配置（Inspector 单选，逐技能单源）。
    /// 移动键例外：按钮标签由单位 normalMoveType 派生（步行/飞行/两栖，与图标/名称同口径），
    /// 不读移动技能资产标签（占位单位共享 Common_Walk 但常态可能是飞行/两栖，标签须与图标一致）。
    /// </summary>
    public enum SkillTag
    {
        [InspectorName("无")]
        None = 0,

        #region 输出
        [InspectorName("伤害")]
        Damage = 1001,

        [InspectorName("控制")]
        Control = 1002,
        #endregion

        #region 增益
        [InspectorName("加攻")]
        AttackUp = 2001,

        [InspectorName("加速")]
        MoveSpeedUp = 2002,

        [InspectorName("视野")]
        Vision = 2003,

        [InspectorName("护盾")]
        Shield = 2004,

        [InspectorName("吸血")]
        Lifesteal = 2005,
        #endregion

        #region 恢复
        [InspectorName("治疗")]
        Heal = 3001,

        [InspectorName("复苏")]
        Revive = 3002,
        #endregion

        #region 移动
        [InspectorName("步行")]
        Walk = 4001,

        [InspectorName("飞行")]
        Flight = 4002,

        [InspectorName("两栖")]
        Amphibious = 4003,

        [InspectorName("位移")]
        Teleport = 4004,
        #endregion

        #region 特殊
        [InspectorName("命座")]
        Constellation = 5001,

        [InspectorName("标记")]
        Mark = 5002,

        [InspectorName("契约")]
        Contract = 5003,

        [InspectorName("资源")]
        Resource = 5004,

        [InspectorName("蓄力")]
        Charge = 5005,

        [InspectorName("采集")]
        Gathering = 5006,

        [InspectorName("掠夺")]
        Ransom = 5007,

        [InspectorName("生存")]
        Survival = 5008,

        [InspectorName("冷却")]
        Cooldown = 5009,
        #endregion
    }

    /// <summary>
    /// SkillTag 扩展方法
    /// </summary>
    public static class SkillTagExtensions
    {

        /// <summary>
        /// 获取技能标签在本地化表中的 Entry Key
        /// </summary>
        private static string GetEntryKey(this SkillTag tag)
        {
            return tag.ToString();
        }

        /// <summary>
        /// 创建用于本地化系统的 LocalizedString 对象
        /// </summary>
        private static LocalizedString GetLocalizedString(this SkillTag tag)
        {
            return new LocalizedString(TableName.SkillTag.ToString(), tag.GetEntryKey());
        }

        /// <summary>
        /// 创建 Entry 对象（用于 TextCombiner）
        /// </summary>
        /// <param name="leadingSeparator">前置连接符</param>
        public static TextEntry GetEntry(this SkillTag tag, string leadingSeparator = "")
        {
            LocalizedString localizedString = tag.GetLocalizedString();
            return new TextEntry(localizedString, leadingSeparator);
        }
    }
}
