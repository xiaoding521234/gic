using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using GIC.Data;
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

        [Header("水面")]
        [Tooltip("每格水面细分格数：顶点波浪按细分采样插值（4=顶点间距 0.25 格，波浪平滑；1=每格仅 4 顶点，波面呈格状折痕）")]
        [InspectorName("水面细分")]
        [SerializeField] private int _waterSurfaceSubdiv = 4;

        public BattleMapData Map { get; private set; }

        /// <summary>地面格顶面高度（板面点击拾取的视差修正初判平面高度，docs/14 §86）</summary>
        public float TileTopHeight => _tileTopHeight;

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
                    // 朝向默认统一（2026-09-21 用户拍板：随机 90° 旋转观感乱，全盘同向）
                }
            }

            BuildWaterSurface(map);
        }

        /// <summary>
        /// 构建整片焊接水面（所有水/沼格合并为单 Mesh，相邻格共享角点焊接，按细分格加密顶点）。
        /// 每格自带半透明顶面的旧方案在格界必有缝：相邻面片 Alpha 双重混合 + 贴图上下缘不无缝
        /// （docs/14 §89）；合并后流动图案按世界坐标连续，格界零缝。顶点细分=波浪位移采样平滑
        /// （每格 4 顶点时波面呈格状折痕）；水格方块墙已退役（波谷下探时墙顶穿出水面=动态缝）。
        /// 水面材质由水地块 prefab 材质槽 1 携带（其顶面 submesh 已退役为空，槽位保留专作水面材质载体）。
        /// </summary>
        private void BuildWaterSurface(BattleMapData map)
        {
            var cells = new List<BattleCell>();
            for (int y = 0; y < map.height; y++)
            {
                for (int x = 0; x < map.width; x++)
                {
                    var tileType = map.GetTile(x, y);
                    if (tileType == TileType.Water || tileType == TileType.Swamp)
                        cells.Add(new BattleCell(x, y));
                }
            }
            if (cells.Count == 0 || _waterTilePrefab == null) return;

            int subdiv = Mathf.Max(1, _waterSurfaceSubdiv);
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            var cornerIndex = new Dictionary<long, int>();

            foreach (var cell in cells)
            {
                int baseSubX = cell.x * subdiv;
                int baseSubY = cell.y * subdiv;
                for (int j = 0; j < subdiv; j++)
                {
                    for (int i = 0; i < subdiv; i++)
                    {
                        int a = GetWaterCorner(cornerIndex, verts, uvs, map, baseSubX + i, baseSubY + j, subdiv);
                        int b = GetWaterCorner(cornerIndex, verts, uvs, map, baseSubX + i + 1, baseSubY + j, subdiv);
                        int c = GetWaterCorner(cornerIndex, verts, uvs, map, baseSubX + i + 1, baseSubY + j + 1, subdiv);
                        int d = GetWaterCorner(cornerIndex, verts, uvs, map, baseSubX + i, baseSubY + j + 1, subdiv);
                        // 绕向与旧格顶面一致（法线朝上）
                        tris.AddRange(new[] { d, c, b, d, b, a });
                    }
                }
            }

            var surfaceGo = new GameObject("WaterSurface");
            surfaceGo.transform.SetParent(_tilesRoot, false);
            var surfaceMesh = new Mesh { name = "BattleWaterSurface" };
            surfaceMesh.SetVertices(verts);
            surfaceMesh.SetUVs(0, uvs);
            surfaceMesh.SetTriangles(tris, 0);
            surfaceMesh.RecalculateNormals();
            surfaceMesh.RecalculateBounds();
            var meshFilter = surfaceGo.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = surfaceMesh;
            var meshRenderer = surfaceGo.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = GetWaterSurfaceMaterial();
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }

        /// <summary>
        /// 水面细分角点（焊接：同角点复用同顶点，波浪位移天然连续）。
        /// subX/subY=细分网格坐标（格坐标×细分）；UV=角点世界坐标（贴图 Repeat 每格重复一次，
        /// 与旧每格 0..1 同密度，图案跨格连续）
        /// </summary>
        private int GetWaterCorner(Dictionary<long, int> cornerIndex, List<Vector3> verts, List<Vector2> uvs,
            BattleMapData map, int subX, int subY, int subdiv)
        {
            long key = ((long)subX << 32) | (uint)subY;
            if (cornerIndex.TryGetValue(key, out int index)) return index;
            var pos = new Vector3((float)subX / subdiv - map.width / 2f, _waterSurfaceHeight, (float)subY / subdiv - map.height / 2f);
            cornerIndex[key] = verts.Count;
            verts.Add(pos);
            uvs.Add(new Vector2(pos.x, pos.z));
            return verts.Count - 1;
        }

        /// <summary>水面材质：水地块 prefab 材质槽 1（BattleWaterFlow）</summary>
        private Material GetWaterSurfaceMaterial()
        {
            if (_waterTilePrefab == null) return null;
            var waterRenderer = _waterTilePrefab.GetComponent<MeshRenderer>();
            if (waterRenderer == null) return null;
            var materials = waterRenderer.sharedMaterials;
            return materials.Length > 1 ? materials[1] : null;
        }

        private float? _waterWaveMaxHeight;

        /// <summary>水面波浪顶点最大抬升（BattleWaterFlow 波场 sin+0.5·sin 峰值 1.5 × 材质 _WaveAmplitude）。
        /// 改 shader 波场公式时峰值系数须同步；材质无该属性时按现配 0.02 兜底 0.03</summary>
        public float GetWaterWaveMaxHeight()
        {
            if (!_waterWaveMaxHeight.HasValue)
            {
                var mat = GetWaterSurfaceMaterial();
                _waterWaveMaxHeight = mat != null && mat.HasProperty("_WaveAmplitude")
                    ? 1.5f * mat.GetFloat("_WaveAmplitude")
                    : 0.03f;
            }
            return _waterWaveMaxHeight.Value;
        }

        /// <summary>表面贴片高度（瞄准高亮/选中标记等贴在表面上的小面片单出口）：
        /// 视觉表面 + 抬升量（z-fighting 静态余量，调用方定）。水/沼格视觉表面已含波峰带
        /// （波浪水面高至 表面+波峰带，贴片抬升不够会被波峰盖过「贴片到水面下」，2026-09-26 报障）</summary>
        public float GetDecalHeight(BattleCell cell, float lift)
        {
            return GetVisualSurfaceHeight(cell) + lift;
        }

        /// <summary>视觉表面高度（世界层视觉件站位单出口）：水/沼格=名义表面+波峰带+贴面余量——
        /// 单位立牌/底座圆盘/投射物路径/贴片等贴水视觉件抬过波峰带，防波浪穿模（2026-09-26 拍板
        /// 「底座圆盘也改」）；拾取（GetSurfaceHeight）与 Host 判定仍用名义表面，波峰带纯视觉</summary>
        public float GetVisualSurfaceHeight(BattleCell cell)
        {
            float wave = 0f;
            if (Map != null && Map.HasTile(cell.x, cell.y))
            {
                var tile = Map.GetTile(cell.x, cell.y);
                if (tile == TileType.Water || tile == TileType.Swamp)
                    wave = GetWaterWaveMaxHeight() + 0.01f; // 贴面余量：极端双峰同相波峰不贴片
            }
            return GetSurfaceHeight(cell) + wave;
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
        /// 格中心的世界坐标（地块底面基准 Y=0；视觉表面高度——水格含波峰带，§89 第五轮）
        /// </summary>
        public Vector3 CellToWorld(BattleCell cell)
        {
            float x = cell.x + 0.5f - Map.width / 2f;
            float z = cell.y + 0.5f - Map.height / 2f;
            float y = GetVisualSurfaceHeight(cell);
            return new Vector3(x, y, z);
        }

        /// <summary>
        /// 连续格心坐标 → 世界坐标（CellToWorld 的连续版；B5 连续判定——投射物命中点/消散点由 Host
        /// 千分定点下发，格 c 的心=c+0.5，与 CellToWorld 同系数）。视觉表面高度取归属格（floor），
        /// 水格含波峰带（§89 第五轮）
        /// </summary>
        public Vector3 ContinuousCellToWorld(float gx, float gy)
        {
            float x = gx - Map.width / 2f;
            float z = gy - Map.height / 2f;
            float y = GetVisualSurfaceHeight(new BattleCell(Mathf.FloorToInt(gx), Mathf.FloorToInt(gy)));
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
        /// 世界坐标反算格坐标（CellToWorld 的逆运算；HUD 点击拾取用，B6）
        /// </summary>
        public BattleCell WorldToCell(Vector3 worldPos)
        {
            float halfW = Map != null ? Map.width / 2f : 0f;
            float halfH = Map != null ? Map.height / 2f : 0f;
            int x = Mathf.FloorToInt(worldPos.x + halfW);
            int y = Mathf.FloorToInt(worldPos.z + halfH);
            return new BattleCell(x, y);
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
