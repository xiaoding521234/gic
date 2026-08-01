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
    /// 物品ID枚举
    /// </summary>
    public enum ItemName
    {
        [InspectorName("无")]
        None = 0,

        #region 货币

        [InspectorName("摩拉")]
        Mora = 1001,

        [InspectorName("纠缠之缘")]
        IntertwinedFate = 1002,

        [InspectorName("体力")]
        Stamina = 1003,

        [InspectorName("原石")]
        Primogem = 1005,

        #endregion

        #region 武器

        [InspectorName("铁剑")]
        IronSword = 2001,
        [InspectorName("铁盾")]
        IronShield = 2002,
        [InspectorName("皮甲")]
        LeatherArmor = 2003,

        #endregion

        #region 配件

        [InspectorName("勾玉")]
        Magatama = 3001,

        #endregion

        #region 食物

        [InspectorName("苹果")]
        Apple = 4001,
        [InspectorName("生肉")]
        RawMeat = 4002,
        [InspectorName("蛋")]
        Egg = 4003,
        [InspectorName("小麦")]
        Wheat = 4004,
        [InspectorName("萝卜")]
        Radish = 4005,

        #endregion

        #region 饮品

        [InspectorName("蒲公英酒")]
        DandelionWine = 5001,
        [InspectorName("迪奥娜特调")]
        DionaSpecial = 5002,

        #endregion

        #region 材料

        [InspectorName("火晶体")]
        FireCrystal = 6001,
        [InspectorName("水晶体")]
        WaterCrystal = 6002,
        [InspectorName("雷晶体")]
        ThunderCrystal = 6003,
        [InspectorName("风晶体")]
        WindCrystal = 6004,
        [InspectorName("冰晶体")]
        IceCrystal = 6005,
        [InspectorName("岩晶体")]
        RockCrystal = 6006,
        [InspectorName("草晶体")]
        GrassCrystal = 6007,

        #endregion

        #region 任务物品

        [InspectorName("神秘钥匙")]
        MysteryKey = 7001,
        [InspectorName("古老卷轴")]
        AncientScroll = 7002,

        #endregion
    }



    /// <summary>
    /// ItemName 扩展方法
    /// </summary>
    public static class ItemNameExtensions
    {
        /// <summary>
        /// 获取物品在本地化表中的 Entry Key
        /// </summary>
        private static string GetEntryKey(this ItemName itemName)
        {
            return itemName.ToString();
        }

        /// <summary>
        /// 创建用于本地化系统的 LocalizedString 对象
        /// </summary>
        private static LocalizedString GetLocalizedString(this ItemName itemName)
        {
            return new LocalizedString(TableName.ItemName.ToString(), itemName.GetEntryKey());
        }

        /// <summary>
        /// 创建 Entry 对象（用于 TextCombiner）
        /// </summary>
        /// <param name="leadingSeparator">前置连接符</param>
        public static TextEntry GetEntry(this ItemName itemName, string leadingSeparator = "")
        {
            LocalizedString localizedString = itemName.GetLocalizedString();
            return new TextEntry(localizedString, leadingSeparator);
        }

    }


}



