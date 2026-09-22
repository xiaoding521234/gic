using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{


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

        // 2026-09-23 审查 Y6：已删除 GetManhattanDistance/IsAdjacent/IsInSameBoard 死助手——
        // 零调用且度量与项目切比雪夫拍板冲突（docs/03 §3.3）；距离计算一律走切比雪夫（参照 BattleHeuristics）
    }
}


