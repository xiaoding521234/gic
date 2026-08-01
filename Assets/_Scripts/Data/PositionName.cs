using UnityEngine;
using UnityEngine.Localization;
using GIC.Framework;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Data
{


    public enum PositionName
    {
        #region 挪德地区 (1000-1999)  // 挪德对应 RegionName.Nodkrai = 1
        [InspectorName("星砂滩")]
        StarsandShoal = 1001,
        
        [InspectorName("那夏镇门外")]
        NashaTownGate = 1002,

        [InspectorName("那夏镇")]
        NashaTown = 1003,
        #endregion

        #region 蒙德地区 (2000-2999)  // 蒙德对应 RegionName.Mondstadt = 2
        [InspectorName("风龙废墟")]
        StormterrorLair = 2001,

        [InspectorName("晨曦酒庄")]
        DawnWinery = 2002,

        [InspectorName("蒙德城桥头")]
        MondstadtBridge = 2003,

        [InspectorName("蒙德城广场")]
        MondstadtSquare = 2004,

        [InspectorName("风起地")]
        Windrise = 2005,

        [InspectorName("星落湖")]
        StarfellLake = 2006,

        [InspectorName("龙脊雪山脚")]
        DragonspineFoot = 2007,
        #endregion

        #region 璃月地区 (3000-3999)  // 璃月对应 RegionName.Liyue = 3
        [InspectorName("瑶光滩")]
        YaoguangShoal = 3001,

        [InspectorName("孤云阁")]
        GuyunStoneForest = 3002,

        [InspectorName("璃月港口")]
        LiyueHarbor = 3003,

        [InspectorName("璃月城内")]
        LiyueCity = 3004,

        [InspectorName("黄金屋")]
        GoldenHouse = 3005,

        [InspectorName("绝云间")]
        JueyunKarst = 3006,

        [InspectorName("望舒客栈")]
        WangshuInn = 3007,
        #endregion

        #region 稻妻地区 (4000-4999)  // 稻妻对应 RegionName.Inazuma = 4
        // 待添加
        #endregion

        #region 须弥地区 (5000-5999)  // 须弥对应 RegionName.Sumeru = 5
        // 待添加
        #endregion

        #region 枫丹地区 (6000-6999)  // 枫丹对应 RegionName.Fontaine = 6
        // 待添加
        #endregion

        #region 纳塔地区 (7000-7999)  // 纳塔对应 RegionName.Natlan = 7
        // 待添加
        #endregion

        #region 至冬地区 (8000-8999)  // 至冬对应 RegionName.Snezhnaya = 8
        // 待添加
        #endregion

        #region 坎瑞亚地区 (9000-9999)  // 坎瑞亚对应 RegionName.Khaenriah = 9
        // 待添加
        #endregion
    }

    /// <summary>
    /// PositionName 扩展方法
    /// </summary>
    public static class PositionNameExtensions
    {
        /// <summary>
        /// 获取地点在本地化表中的 Entry Key
        /// </summary>
        /// <returns>本地化表的 Key</returns>
        private static string GetEntryKey(this PositionName position)
        {
            return position.ToString();
        }

        /// <summary>
        /// 创建用于本地化系统的 LocalizedString 对象
        /// </summary>
        /// <returns>LocalizedString 对象</returns>
        private static LocalizedString GetLocalizedString(this PositionName position)
        {
            return new LocalizedString(TableName.PositionName.ToString(), position.GetEntryKey());
        }

        /// <summary>
        /// 创建 Entry 对象（用于 TextCombiner）
        /// </summary>
        /// <param name="leadingSeparator">前置连接符</param>
        /// <returns>Entry 对象</returns>
        public static TextEntry GetEntry(this PositionName position, string leadingSeparator = "")
        {
            LocalizedString localizedString = position.GetLocalizedString();
            return new TextEntry(localizedString, leadingSeparator);
        }
    }
}



