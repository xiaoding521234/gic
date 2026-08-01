using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
namespace GIC.Battle
{

    /// <summary>
    /// 移动修改器
    /// </summary>
    public class MoveableModifier
    {
        public MoveableModifierType Type;
        public bool BoolValue;
        public ForceType ForceTypeValue;

        public MoveableModifier(MoveableModifierType type, bool value)
        {
            Type = type;
            BoolValue = value;
        }

        public MoveableModifier(ForceType value)
        {
            Type = MoveableModifierType.NormalMoveType;
            ForceTypeValue = value;
        }
    }

}

