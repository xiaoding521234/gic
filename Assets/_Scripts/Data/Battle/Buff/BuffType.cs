using System;
using UnityEngine;
namespace GIC.Data
{


    /// <summary>
    /// Buff 类型（协议标识；命令流/快照载体）。B2 首批=燃烧；后续按 B4 元素反应批次扩充（激化/冻结/超载…）。
    /// </summary>
    public enum BuffType
    {
        None = 0,

        [InspectorName("燃烧")]
        Burn = 1,

        [InspectorName("冻结")]
        Freeze = 2,

        [InspectorName("攻击提升")]
        AttackUp = 3,

        [InspectorName("移速提升")]
        MoveSpeedUp = 4,

        [InspectorName("歌声之环")]
        SongOfLife = 5, // B-3 ②（芭芭拉闪耀奇迹）：多行为永久光环——回合末对持有者半径1内敌人水伤/我方治疗+附着；持有者倒下消失（docs/units/蒙德/芭芭拉.md）
    }

    /// <summary>
    /// Buff 状态条目（快照与命令流的载体：类型 + 级别 + 剩余回合）
    /// </summary>
    [Serializable]
    public class BuffState
    {
        public int type;
        public int level;
        public int remainingTurns;
    }
}
