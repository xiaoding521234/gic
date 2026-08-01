using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Framework
{

    /// <summary>
    /// 音乐类型枚举，数字越大优先级越高
    /// </summary>
    public enum MusicType
    {
        /// <summary>
        /// 休闲音乐（优先级最低）
        /// </summary>
        Relaxed = 0,
        
        /// <summary>
        /// 战斗音乐（中优先级，可顶掉休闲音乐）
        /// </summary>
        Battle = 1,
        
        /// <summary>
        /// 专属压迫感音乐（高优先级，可顶掉休闲和战斗音乐）
        /// 如：巴巴托斯、摩拉克斯等神明专属BGM
        /// </summary>
        Exclusive = 2
    }
}

