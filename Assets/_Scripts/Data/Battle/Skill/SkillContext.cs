using UnityEngine;

/// <summary>
/// 技能上下文
/// </summary>
public class SkillContext
{
    public Unit TargetUnit;
    public Vector2Int? TargetPosition;
    public Direction2D Direction;
    public Tile TargetTile;
}