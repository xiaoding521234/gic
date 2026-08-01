using UnityEngine;
using GIC.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
namespace GIC.Tool
{


    public static class StringExtensions
    {
        /// <summary>
        /// 将 PascalCase 转换为 snake_case
        /// </summary>
        public static string ToSnakeCase(this string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            
            var result = new System.Text.StringBuilder();
            
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                
                if (char.IsUpper(c))
                {
                    if (i > 0)
                        result.Append('_');
                    result.Append(char.ToLower(c));
                }
                else
                {
                    result.Append(c);
                }
            }
            
            return result.ToString();
        }
    }
    public static class ColorExtension
    {
        public static Color FromHex(this string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color color);
            return color;
        }
    }
}


