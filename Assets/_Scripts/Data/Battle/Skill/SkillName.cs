using UnityEngine;
using UnityEngine.Localization;
using GIC.Framework;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Data
{


    /// <summary>
    /// 技能标识枚举
    /// </summary>
    public enum SkillName
    {
        [InspectorName("无")]
        None = 0,

        #region 通用技能
        [InspectorName("步行")]
        Common_Walk = 1,

        [InspectorName("飞行")]
        Common_Fly = 2,
        #endregion

        #region 派蒙 (1001)
        [InspectorName("时间")]
        Paimon_Skill1 = 1001001,

        [InspectorName("空间")]
        Paimon_Skill2 = 1001002,

        [InspectorName("派蒙命座")]
        Paimon_Constellation = 1001005,
        #endregion


        #region 安柏 (3001)
        [InspectorName("飞行冠军")]
        Amber_FlyingChampion = 3001001,

        [InspectorName("一箭双丘丘")]
        Amber_DoubleShot = 3001002,

        [InspectorName("箭雨")]
        Amber_ArrowRain = 3001003,

        [InspectorName("百发百中")]
        Amber_Sharpshooter = 3001004,

        [InspectorName("全面侦查")]
        Amber_Scouting = 3001005,

        [InspectorName("安柏命座")]
        Amber_Constellation = 3001006,
        #endregion

        #region 凯亚 (3002)
        [InspectorName("霜袭")]
        Kaeya_Frostgnaw = 3002001,

        [InspectorName("凛冽轮舞")]
        Kaeya_GlacialWaltz = 3002002,

        [InspectorName("隐藏的实力")]
        Kaeya_HiddenStrength = 3002003,

        [InspectorName("冷血之剑")]
        Kaeya_ColdBloodedBlade = 3002004,

        [InspectorName("凯亚命座")]
        Kaeya_Constellation = 3002005,
        #endregion

        #region 芭芭拉 (3003)
        [InspectorName("闪耀奇迹")]
        Barbara_ShiningMiracle = 3003001,

        [InspectorName("心意注入")]
        Barbara_HeartfeltDevotion = 3003002,

        [InspectorName("元气迸发")]
        Barbara_VitalityBurst = 3003003,

        [InspectorName("芭芭拉命座")]
        Barbara_Constellation = 3003005,
        #endregion

        #region 丽莎 (3004)
        [InspectorName("指尖雷暴")]
        Lisa_VioletArc = 3004001,

        [InspectorName("蔷薇的雷光")]
        Lisa_LightningRose = 3004002,

        [InspectorName("蔷薇标记")]
        Lisa_RoseMark = 3004003,

        [InspectorName("脉冲的魔女")]
        Lisa_PulsingWitch = 3004004,

        [InspectorName("丽莎命座")]
        Lisa_Constellation = 3004005,
        #endregion

        #region 迪卢克 (3006)
        [InspectorName("逆焰之刃")]
        Diluc_SearingOnslaught = 3006001,

        [InspectorName("黎明")]
        Diluc_Dawn = 3006002,

        [InspectorName("晨曦的传统")]
        Diluc_Tradition = 3006003,

        [InspectorName("罪罚裁断")]
        Diluc_Judgment = 3006004,

        [InspectorName("迪卢克命座")]
        Diluc_Constellation = 3006005,
        #endregion

        #region 温迪 (3008)
        [InspectorName("风神之诗")]
        Venti_WindOde = 3008001,

        [InspectorName("颂时风若")]
        Venti_WindsGrandOde = 3008002,

        [InspectorName("高天之歌")]
        Venti_SkywardSonnet = 3008003,

        [InspectorName("绪风之拥")]
        Venti_WindEmbrace = 3008004,

        [InspectorName("温迪命座")]
        Venti_Constellation = 3008005,
        #endregion

        #region 空/旅行者 (11002)
        [InspectorName("风涡剑")]
        Traveler_WindBlade = 11002001,

        [InspectorName("提瓦特煎蛋")]
        Traveler_FriedEgg = 11002002,

        [InspectorName("群星的涡风")]
        Traveler_StarwindVortex = 11002003,

        [InspectorName("甜甜花酿鸡")]
        Traveler_SweetMadame = 11002004,

        [InspectorName("空命座")]
        Traveler_Constellation = 11002005,
        #endregion
    }

    /// <summary>
    /// SkillName 扩展方法
    /// </summary>
    public static class SkillNameExtensions
    {

        /// <summary>
        /// 获取技能在本地化表中的 Entry Key
        /// </summary>
        private static string GetEntryKey(this SkillName skillName)
        {
            return skillName.ToString();
        }

        /// <summary>
        /// 创建用于本地化系统的 LocalizedString 对象
        /// </summary>
        private static LocalizedString GetLocalizedString(this SkillName skillName)
        {
            return new LocalizedString(TableName.SkillName.ToString(), skillName.GetEntryKey());
        }

        /// <summary>
        /// 创建 Entry 对象（用于 TextCombiner）
        /// </summary>
        public static TextEntry GetEntry(this SkillName skillName, string leadingSeparator = "")
        {
            LocalizedString localizedString = skillName.GetLocalizedString();
            return new TextEntry(localizedString, leadingSeparator);
        }
    }
}



