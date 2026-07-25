using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 单位棋盘位置组件
/// </summary>
public class UnitGridPosition : MonoBehaviour, IUnitComponent
{
    private Unit _owner;
    
    [Header("位置信息")]
    [SerializeField] private BoardType _boardType = BoardType.MainWorld;
    [SerializeField] private Vector2Int _gridPosition;
    
    // ==================== 属性 ====================
    public BoardType BoardType => _boardType;
    public Vector2Int Position => _gridPosition;
    
    // ==================== 初始化 ====================
    public void Init(Unit owner)
    {
        _owner = owner;
    }
    
    // ==================== 设置方法 ====================
    public void SetBoardType(BoardType boardType) => _boardType = boardType;
    public void SetPosition(Vector2Int position) => _gridPosition = position;
    public void SetPosition(BoardType boardType, Vector2Int position)
    {
        _boardType = boardType;
        _gridPosition = position;
    }
    
    // ==================== 便利方法 ====================
    public bool IsInBoard(BoardType boardType) => _boardType == boardType;
    
    public bool IsInSameBoard(UnitGridPosition other)
    {
        if (other == null) return false;
        return _boardType == other._boardType;
    }
    
    public int GetManhattanDistance(UnitGridPosition other)
    {
        if (!IsInSameBoard(other)) return int.MaxValue;
        return Mathf.Abs(_gridPosition.x - other._gridPosition.x) + 
               Mathf.Abs(_gridPosition.y - other._gridPosition.y);
    }
    
    public bool IsInRange(UnitGridPosition other, int radius)
    {
        return GetManhattanDistance(other) <= radius;
    }
}