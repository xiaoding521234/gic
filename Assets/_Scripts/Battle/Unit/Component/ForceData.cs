using UnityEngine;

/// <summary>
/// 力的数据结构（方向、瞬移、跳跃等）
/// </summary>
[System.Serializable]
public class ForceData
{
    public Direction2D Direction;
    public int Magnitude;
    public ForceType Type;
    public Unit Source;

    public Vector2Int? TargetPosition;
    public Vector2Int? JumpOffset;

    public ForceData() { }

    public ForceData(Direction2D direction, int magnitude, ForceType type, Unit source)
    {
        Direction = direction;
        Magnitude = magnitude;
        Type = type;
        Source = source;
    }

    public ForceData(Vector2Int targetPosition, Unit source)
    {
        TargetPosition = targetPosition;
        Type = ForceType.Teleport;
        Source = source;
        Direction = Direction2D.Right;
    }

    public ForceData(Vector2Int jumpOffset, ForceType forceType, Unit source)
    {
        JumpOffset = jumpOffset;
        Type = forceType;
        Source = source;
        Direction = Direction2D.Right;
    }
}
