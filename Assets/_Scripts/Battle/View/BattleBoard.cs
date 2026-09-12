using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
namespace GIC.Battle
{


    /// <summary>
    /// 战场棋盘：按 BattleMapData 实例化 3D 格子（有厚度地块）并做格↔世界坐标换算。
    /// 棋盘平面 = XZ（Y 向上），斜俯视正交相机呈现。
    /// </summary>
    public class BattleBoard : MonoBehaviour
    {
        [Header("格子预制体")]
        [SerializeField] private GameObject _grassTilePrefab;
        [SerializeField] private GameObject _waterTilePrefab;
        [SerializeField] private GameObject _stoneTilePrefab;

        [Header("表面高度")]
        [Tooltip("地面格顶面高度（单位站此高度）")]
        [SerializeField] private float _tileTopHeight = 0.5f;
        [Tooltip("水面格顶面高度（水面相对下沉）")]
        [SerializeField] private float _waterSurfaceHeight = 0.36f;

        public BattleMapData Map { get; private set; }

        private Transform _tilesRoot;

        /// <summary>
        /// 建盘（幂等：重复调用先清空重建）
        /// </summary>
        public void Build(BattleMapData map)
        {
            Map = map;

            if (_tilesRoot == null)
            {
                var rootGo = new GameObject("Tiles");
                rootGo.transform.SetParent(transform, false);
                _tilesRoot = rootGo.transform;
            }
            for (int i = _tilesRoot.childCount - 1; i >= 0; i--)
                Destroy(_tilesRoot.GetChild(i).gameObject);

            for (int y = 0; y < map.height; y++)
            {
                for (int x = 0; x < map.width; x++)
                {
                    var tileType = map.GetTile(x, y);
                    if (!map.HasTile(x, y)) continue; // 虚空格不放地块

                    var prefab = GetTilePrefab(tileType);
                    if (prefab == null) continue;

                    var tile = Instantiate(prefab, _tilesRoot);
                    tile.name = $"Tile_{x}_{y}";
                    tile.transform.localPosition = TileBottomPosition(x, y);
                    // 随机 90° 旋转打散贴图重复感（顶面无缝纹理，旋转不破 UV）
                    tile.transform.localRotation = Quaternion.Euler(0f, 90f * UnityEngine.Random.Range(0, 4), 0f);
                }
            }
        }

        private GameObject GetTilePrefab(TileType tileType)
        {
            switch (tileType)
            {
                case TileType.Grassland:
                case TileType.Plain:
                case TileType.Sand:
                case TileType.Snow:
                case TileType.Ice:
                    return _grassTilePrefab;
                case TileType.Water:
                case TileType.Swamp:
                    return _waterTilePrefab;
                case TileType.StonePath:
                    return _stoneTilePrefab;
                default:
                    return _grassTilePrefab;
            }
        }

        /// <summary>
        /// 格中心的世界坐标（地块底面基准 Y=0）
        /// </summary>
        public Vector3 CellToWorld(BattleCell cell)
        {
            float x = cell.x + 0.5f - Map.width / 2f;
            float z = cell.y + 0.5f - Map.height / 2f;
            float y = GetSurfaceHeight(cell);
            return new Vector3(x, y, z);
        }

        /// <summary>
        /// 该格表面高度（单位站立面）
        /// </summary>
        public float GetSurfaceHeight(BattleCell cell)
        {
            if (Map != null && Map.HasTile(cell.x, cell.y))
            {
                var tile = Map.GetTile(cell.x, cell.y);
                if (tile == TileType.Water || tile == TileType.Swamp)
                    return _waterSurfaceHeight;
            }
            return _tileTopHeight;
        }

        /// <summary>
        /// 地块预制体摆放位置（预制体轴心在底面中心）
        /// </summary>
        private Vector3 TileBottomPosition(int x, int y)
        {
            return new Vector3(x + 0.5f - Map.width / 2f, 0f, y + 0.5f - Map.height / 2f);
        }
    }
}
