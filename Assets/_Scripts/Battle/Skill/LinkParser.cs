using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
namespace GIC.Battle
{

    /// <summary>
    /// 解析 link 富文本中的 "Type:Id" 格式
    /// 例如 "Unit:Amber" → ("Unit", "Amber")
    /// 无冒号时兜底为 Concept 类型
    /// </summary>
    public static class LinkParser
    {
        public static (string type, string id) Parse(string linkId)
        {
            if (string.IsNullOrEmpty(linkId))
                return ("Concept", "");

            int idx = linkId.IndexOf(':');
            if (idx <= 0)
                return ("Concept", linkId);

            return (linkId.Substring(0, idx), linkId.Substring(idx + 1));
        }
    }

}

