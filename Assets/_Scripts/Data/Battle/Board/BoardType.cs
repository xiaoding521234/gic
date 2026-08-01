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
    /// 棋盘类型
    /// </summary>
    public enum BoardType
    {
        [InspectorName("主世界")]
        MainWorld = 1,
        
        [InspectorName("安魂地")]
        RestingPlace = 2,
    }

    /// <summary>
    /// BoardType 扩展方法
    /// </summary>
    public static class BoardTypeExtensions
    {
        
        /// <summary>
        /// 转换为存储字符串
        /// </summary>
        public static string ToStorageString(this BoardType boardType)
        {
            return boardType.ToString();
        }
        
        /// <summary>
        /// 从存储字符串解析
        /// </summary>
        public static BoardType FromStorageString(string str)
        {
            if (System.Enum.TryParse<BoardType>(str, out var result))
                return result;
            
            Debug.LogWarning($"无法解析棋盘类型: {str}");
            return BoardType.MainWorld;
        }

        /// <summary>
        /// 获取棋盘类型在本地化表中的 Entry Key
        /// </summary>
        private static string GetEntryKey(this BoardType boardType)
        {
            return boardType.ToString();
        }

        /// <summary>
        /// 创建用于本地化系统的 LocalizedString 对象
        /// </summary>
        private static LocalizedString GetLocalizedString(this BoardType boardType)
        {
            return new LocalizedString(TableName.UIText.ToString(), boardType.GetEntryKey());
        }

        /// <summary>
        /// 创建 Entry 对象（用于 TextCombiner）
        /// </summary>
        /// <param name="leadingSeparator">前置连接符</param>
        public static TextEntry GetEntry(this BoardType boardType, string leadingSeparator = "")
        {
            LocalizedString localizedString = boardType.GetLocalizedString();
            return new TextEntry(localizedString, leadingSeparator);
        }
    }
}



