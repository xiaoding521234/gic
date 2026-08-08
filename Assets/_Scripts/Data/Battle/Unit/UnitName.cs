using System;
using System.Linq;
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
    /// 角色名称枚举
    /// </summary>
    public enum UnitName
    {
        #region 天空岛 (1001)
        [InspectorName("派蒙")]
        Paimon = 1001,
        #endregion

        #region 挪德卡莱 (2001-2999)
        [InspectorName("兹白")]
        Zibai = 2001,

        [InspectorName("莉奈娅")]
        Linnea = 2002,

        [InspectorName("叶洛亚")]
        Illuga = 2003,
        [InspectorName("哥伦比娅")]
        Columbina = 2004,
        #endregion

        #region 蒙德 (3001-3999)
        [InspectorName("安柏")]
        Amber = 3001,

        [InspectorName("凯亚")]
        Kaeya = 3002,

        [InspectorName("芭芭拉")]
        Barbara = 3003,

        [InspectorName("丽莎")]
        Lisa = 3004,

        [InspectorName("迪奥娜")]
        Diona = 3005,

        [InspectorName("迪卢克")]
        Diluc = 3006,

        [InspectorName("法尔伽")]
        Varka = 3007,

        [InspectorName("温迪")]
        Venti = 3008,

        [InspectorName("巴巴托斯")]
        Barbatos = 3009,

        [InspectorName("班尼特")]
        Bennett = 3010,

        [InspectorName("琴")]
        Jean = 3011,
        #endregion

        #region 璃月 (4001-4999)
        [InspectorName("行秋")]
        Xingqiu = 4001,

        [InspectorName("北斗")]
        Beidou = 4002,

        [InspectorName("香菱")]
        Xiangling = 4003,

        [InspectorName("凝光")]
        Ningguang = 4004,

        [InspectorName("胡桃")]
        Hutao = 4005,

        [InspectorName("甘雨")]
        Ganyu = 4006,

        [InspectorName("钟离")]
        Zhongli = 4007,
        #endregion

        #region 稻妻 (5001-5999)
        [InspectorName("梦见月瑞希")]
        Mizuki = 5001,

        [InspectorName("五郎")]
        Gorou = 5002,

        [InspectorName("绮良良")]
        Kirara = 5003,

        [InspectorName("影")]
        Ei = 5004,
        #endregion

        #region 须弥 (6001-6999)
        [InspectorName("艾尔海森")]
        Alhaitham = 6001,

        [InspectorName("柯莱")]
        Collei = 6002,

        [InspectorName("妮露")]
        Nilou = 6003,

        [InspectorName("纳西妲")]
        Nahida = 6004,
        #endregion

        #region 枫丹 (7001-7999)
        [InspectorName("希格雯")]
        Sigewinne = 7001,

        [InspectorName("莱欧斯利")]
        Wriothesley = 7002,

        [InspectorName("千织")]
        Chiori = 7003,

        [InspectorName("芙宁娜")]
        Furina = 7004,
        #endregion

        #region 纳塔 (8001-8999)
        [InspectorName("玛薇卡")]
        Mavuika = 8001,
        #endregion

        #region 至冬 (9001-9999)
        // 至冬角色待添加
        #endregion

        #region 坎瑞亚 (10001-10999)
        [InspectorName("火斧丘丘暴徒")]
        PyroAxeHilichurlBrute = 10001,

        [InspectorName("丘丘人")]
        Hilichurl = 10002,
        #endregion

        #region 特殊 (11001-11999)
        [InspectorName("协议核心")]
        ProtocolCore = 11001,
        [InspectorName("空")]
        Traveler = 11002,
        #endregion
    }

    /// <summary>
    /// UnitName 扩展方法
    /// </summary>
    public static class UnitNameExtensions
    {

        /// <summary>
        /// 获取角色在本地化表中的 Entry Key
        /// </summary>
        private static string GetEntryKey(this UnitName unitName)
        {
            return unitName.ToString();
        }

        /// <summary>
        /// 创建用于本地化系统的 LocalizedString 对象
        /// </summary>
        private static LocalizedString GetLocalizedString(this UnitName unitName)
        {
            return new LocalizedString(TableName.UnitName.ToString(), unitName.GetEntryKey());
        }

        /// <summary>
        /// 创建 Entry 对象（用于 TextCombiner）
        /// </summary>
        /// <param name="leadingSeparator">前置连接符</param>
        public static TextEntry GetEntry(this UnitName unitName, string leadingSeparator = "")
        {
            LocalizedString localizedString = unitName.GetLocalizedString();
            return new TextEntry(localizedString, leadingSeparator);
        }

        /// <summary>
        /// 从存储字符串解析
        /// </summary>
        public static UnitName FromStorageString(string str)
        {
            if (Enum.TryParse<UnitName>(str, out var result))
                return result;

            Debug.LogWarning($"无法解析角色名称: {str}");
            return UnitName.Traveler;
        }
    }
}



