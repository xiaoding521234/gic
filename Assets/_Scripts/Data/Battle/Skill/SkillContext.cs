using UnityEngine;
using GIC.Battle;
using GIC.Framework;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
namespace GIC.Data
{


    /// <summary>
    /// 技能上下文
    /// </summary>
    public class SkillContext
    {
        public Unit TargetUnit;
        public Vector2Int? TargetPosition;
        public Direction2D Direction;
        public Tile TargetTile;

        /// <summary>技能元素（解析自技能配置/施展者元素；产物效应携带，B4）</summary>
        public ElementType Element;
    }
}
