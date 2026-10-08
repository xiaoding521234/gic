using System;
using UnityEngine;
namespace GIC.Data
{


    /// <summary>
    /// Buff 类型（协议标识；命令流/快照载体）。B2 首批=燃烧；后续按 B4 元素反应批次扩充（激化/冻结/超载…）。
    /// 2026-10-08 决策五十七「资产即身份」：本枚举降级为**族分类**（StatBuff 子类族/行为族——
    /// 协议 type 字段与客户端渲染族用）；具名 buff 身份=BuffConfig 资产（buffKey 字段）——
    /// 新增同类 buff（如第二个加攻来源）=新建 Buff_* 资产，**不再加枚举值**。
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

        [InspectorName("寒冰之棱")]
        Icicle = 6, // 凛冽轮舞批（2026-10-01 凯亚爆发=自施放 Buff，拍板「实际并不是召唤，与歌声之环类似」）：永久光环——回合末半径内敌冰伤、持有者元能超50%碎裂回血；持有者倒下仍生效（docs/units/蒙德/凯亚.md）

        [InspectorName("防御减少")]
        DefenseDown = 7, // 防御降低（StatBuff 族负值修改器=减防即增伤；具名资产 Buff_DefenseDown「冰棱减防」——凯亚2命 tick 命中施加，数值全在资产单源）
    }

    /// <summary>
    /// Buff 状态条目（快照与命令流的载体：类型 + 级别 + 剩余回合 + 来源）
    /// </summary>
    [Serializable]
    public class BuffState
    {
        public int type;

        /// <summary>具名身份键（=BuffConfig 资产名，决策五十七「资产即身份」：同资产叠层合并、
        /// 异资产同族共存——安柏加攻+班尼特加攻可同时存在）。旧回放 JSON 缺省空=客户端按 type
        /// 回落匹配（crit/arrowHeightY 同款向后兼容）</summary>
        public string buffKey;

        public int level;

        /// <summary>级别语义按类型分家：StatBuff 族=叠层数、BurnBuff 族=级别——快照原样携带</summary>
        public int remainingTurns;

        /// <summary>来源单位 id（2026-10-06 头顶 Buff 图标批：空=无来源——客户端描环按来源单位
        /// 所属玩家色解析，与快捷面板/执行预览头像描环同口径）</summary>
        public string sourceUnitId;

        /// <summary>来源技能 id（SkillName 枚举值；0=无来源技能——反应类 Buff〔燃烧/冻结〕无技能语境）。
        /// 客户端头顶 Buff 图标解析消费（2026-10-06 拍板「buff 图标用来源技能图标」：&gt;0 取技能
        /// SkillConfig.icon、0 回落元素图标）</summary>
        public int sourceSkillId;
    }
}
