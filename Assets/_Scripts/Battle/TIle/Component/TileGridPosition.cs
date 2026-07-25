using UnityEngine;

/// <summary>
/// 地砖棋盘位置组件
/// </summary>
public class TileGridPosition : MonoBehaviour, ITileComponent
{
    private Tile _owner;
    
    [Header("位置信息")]
    [SerializeField] private BoardType _boardType = BoardType.MainWorld;
    [SerializeField] private Vector2Int _gridPosition;
    
    // ==================== 属性 ====================
    public BoardType BoardType => _boardType;
    public Vector2Int Position => _gridPosition;
    
    // ==================== 初始化 ====================
    public void Initialize(Tile owner)
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
    
    public bool IsInSameBoard(TileGridPosition other)
    {
        if (other == null) return false;
        return _boardType == other._boardType;
    }
    
    public int GetManhattanDistance(TileGridPosition other)
    {
        if (!IsInSameBoard(other)) return int.MaxValue;
        return Mathf.Abs(_gridPosition.x - other._gridPosition.x) + 
               Mathf.Abs(_gridPosition.y - other._gridPosition.y);
    }
    
    public bool IsAdjacent(TileGridPosition other)
    {
        return GetManhattanDistance(other) == 1;
    }
}