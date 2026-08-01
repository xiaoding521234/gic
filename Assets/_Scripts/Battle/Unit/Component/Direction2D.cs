using UnityEngine;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
namespace GIC.Battle
{


    /// <summary>
    /// 二维方向枚举
    /// </summary>
    public enum Direction2D
    {
        [InspectorName("右")] Right = 1,
        [InspectorName("左")] Left = 2,
        [InspectorName("上")] Up = 3,
        [InspectorName("下")] Down = 4,
        [InspectorName("右上")] UpRight = 5,
        [InspectorName("左上")] UpLeft = 6,
        [InspectorName("右下")] DownRight = 7,
        [InspectorName("左下")] DownLeft = 8
    }

}

