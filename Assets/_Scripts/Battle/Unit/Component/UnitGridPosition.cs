using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 单位棋盘位置组件
    /// </summary>
    public class UnitGridPosition : MonoBehaviour, IUnitComponent
    {
        private Unit _owner;

        [Header("位置信息")]
        [SerializeField] private BoardType _boardType = BoardType.MainWorld;

        /// <summary>旧格坐标缓存（**仅旧场景序列化兼容保留**——运行时真源=PositionCm，格坐标由
        /// 连续坐标派生；决策九十五 P2 双轨，勿再直接读写本字段）</summary>
        [SerializeField] private Vector2Int _gridPosition;

        /// <summary>连续位置真源（厘米，决策九十五 P2：格→连续双轨——SetPosition 旧链写格心 cm
        /// 零回归；SetPositionCm 新链=tick 移动每步连续写）</summary>
        [SerializeField] private Vector2Int _positionCm;

        // ==================== 属性 ====================
        public BoardType BoardType => _boardType;

        /// <summary>格坐标=连续坐标派生（cm/CellSizeCm）——sim.GetPosition 全链零改动兼容</summary>
        public Vector2Int Position => new Vector2Int(
            _positionCm.x / CellMetrics.CellSizeCm, _positionCm.y / CellMetrics.CellSizeCm);

        /// <summary>连续位置（厘米，真源）</summary>
        public Vector2Int PositionCm => _positionCm;

        // ==================== 初始化 ====================
        public void Init(Unit owner)
        {
            _owner = owner;
        }

        // ==================== 设置方法 ====================
        public void SetBoardType(BoardType boardType) => _boardType = boardType;

        /// <summary>格写入（旧链——双写：格缓存+连续真源格心 cm，零回归）</summary>
        public void SetPosition(Vector2Int position)
        {
            _gridPosition = position;
            _positionCm = new Vector2Int(
                position.x * CellMetrics.CellSizeCm + CellMetrics.CellHalfCm,
                position.y * CellMetrics.CellSizeCm + CellMetrics.CellHalfCm);
        }

        /// <summary>连续坐标写入（新链——tick 移动每步调用；格缓存自动派生）</summary>
        public void SetPositionCm(int xCm, int zCm)
        {
            _positionCm = new Vector2Int(xCm, zCm);
            _gridPosition = new Vector2Int(xCm / CellMetrics.CellSizeCm, zCm / CellMetrics.CellSizeCm);
        }

        public void SetPosition(BoardType boardType, Vector2Int position)
        {
            _boardType = boardType;
            SetPosition(position);
        }
        
        // ==================== 便利方法 ====================
        public bool IsInBoard(BoardType boardType) => _boardType == boardType;

        // 2026-09-23 审查 Y6：已删除 GetManhattanDistance/IsInRange/IsInSameBoard 死助手——
        // 零调用且度量与项目切比雪夫拍板冲突（docs/03 §3.3）；距离计算一律走切比雪夫（参照 BattleHeuristics）
    }
}


