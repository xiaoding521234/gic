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
        [SerializeField] private GameObject _dirtTilePrefab;
        [SerializeField] private GameObject _cobbleTilePrefab;

        [Header("地块视觉")]
        [Tooltip("地块视觉配置（顶面变体材质/草簇装饰参数；null=全部用地块 prefab 自带材质、无草簇）")]
        [SerializeField] private TileVisualsConfig _tileVisuals;

        [Header("表面高度")]
        [Tooltip("地面格顶面高度（单位站此高度）")]
        [SerializeField] private float _tileTopHeight = 0.5f;
        [Tooltip("水面格顶面高度（水面相对下沉）")]
        [SerializeField] private float _waterSurfaceHeight = 0.36f;

        [Header("水面")]
        [Tooltip("每格水面细分格数：顶点波浪按细分采样插值（4=顶点间距 0.25 格，波浪平滑；1=每格仅 4 顶点，波面呈格状折痕）")]
        [InspectorName("水面细分")]
        [SerializeField] private int _waterSurfaceSubdiv = 4;

        [Header("薄冰（决策七十六：冰元素冻结水面——半透明冰面板叠水面，水下流动仍隐约可见）")]
        [Tooltip("薄冰主色（运行时克隆水面材质改 tint——波场公式同源=冰面随下方波浪起伏恒贴水面）")]
        [SerializeField] private Color 薄冰色 = new Color(0.78f, 0.90f, 0.96f, 0.80f);
        [Tooltip("薄冰深色（shader _DeepColor——冰下水的暗部）")]
        [SerializeField] private Color 薄冰深色 = new Color(0.30f, 0.44f, 0.55f, 1f);
        [Tooltip("薄冰流动速度（低于水面=冰下缓流感；0=静止）")]
        [SerializeField] private float 薄冰流动速度 = 0.012f;
        [Tooltip("薄冰波峰高光（低=哑光冰面）")]
        [SerializeField, Range(0f, 1f)] private float 薄冰高光 = 0.10f;
        [Tooltip("薄冰面板相对水面的高度余量（同波场同相位=恒定间隙不穿插）")]
        [SerializeField] private float 薄冰抬升 = 0.006f;

        public BattleMapData Map { get; private set; }

        /// <summary>地面格顶面高度（板面点击拾取的视差修正初判平面高度，docs/14 §86）</summary>
        public float TileTopHeight => _tileTopHeight;

        private Transform _tilesRoot;

        /// <summary>本盘运行时生成的 mesh（水面焊接面等；Build 重建/OnDestroy 显式销毁——
        /// Destroy 物体不销 mesh，§63① mesh 条款，2026-09-28 批6）</summary>
        private readonly List<Mesh> _runtimeMeshes = new List<Mesh>();

        /// <summary>薄冰面板运行时件（mesh/材质实例/物体——元素×地形变化重建、OnDestroy 释放，
        /// 决策七十六）</summary>
        private GameObject _thinIceSurface;
        private Mesh _thinIceMesh;
        private Material _thinIceMaterial;

        private void OnDestroy()
        {
            foreach (var mesh in _runtimeMeshes)
                if (mesh != null) Destroy(mesh);
            _runtimeMeshes.Clear();
            if (_thinIceMaterial != null) Destroy(_thinIceMaterial);
        }

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
            // 旧盘运行时 mesh 显式销毁（Destroy 物体不销 mesh——§63① mesh 条款，2026-09-28 批6）
            foreach (var mesh in _runtimeMeshes)
                if (mesh != null) Destroy(mesh);
            _runtimeMeshes.Clear();
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
                    ApplyTopVariant(tile, tileType, x, y);
                    // 朝向默认统一（2026-09-21 用户拍板：随机 90° 旋转观感乱，全盘同向）
                }
            }

            BuildWaterSurface(map);
            RebuildThinIceSurface(); // 幂等（初始地图无薄冰=空面板；防御性支持已含薄冰的重建）
        }

        /// <summary>地形变化应用（决策七十六元素×地形，TerrainChange 命令客户端入口）：
        /// 本地 map 真值写入（移动瞄准/箭矢落地判地形等消费方即时正确）+薄冰面板全量重建
        /// （变化低频、面板格数少=全量重建最简且天然幂等；水面焊接面不动——薄冰格**保留**下方
        /// 流动水面=拍板「半透明冰面板叠水面」）</summary>
        public void ApplyTerrainChange(BattleCell cell, TileType newTile)
        {
            if (Map == null) return;
            Map.SetTile(cell.x, cell.y, newTile);
            RebuildThinIceSurface();
        }

        /// <summary>
        /// 薄冰面板重建（全部 ThinIce 格合并为单 Mesh——与水面焊接面同构：同波场同相位顶点起伏
        /// =冰面恒贴下方波浪，格界零缝）。材质=运行时克隆水面材质改 tint/流速/高光（Inspector 四参
        /// 可调）；无薄冰格=面板移除。
        /// </summary>
        private void RebuildThinIceSurface()
        {
            if (_thinIceSurface != null)
            {
                Destroy(_thinIceSurface);
                _thinIceSurface = null;
            }
            if (_thinIceMesh != null)
            {
                _runtimeMeshes.Remove(_thinIceMesh);
                Destroy(_thinIceMesh);
                _thinIceMesh = null;
            }
            if (Map == null) return;

            var cells = new List<BattleCell>();
            for (int y = 0; y < Map.height; y++)
            for (int x = 0; x < Map.width; x++)
                if (Map.GetTile(x, y) == TileType.ThinIce)
                    cells.Add(new BattleCell(x, y));
            if (cells.Count == 0) return;

            var waterMat = GetWaterSurfaceMaterial();
            if (waterMat == null)
            {
                GIC.Framework.GICLog.Warn("[BattleBoard] 薄冰面板缺少水面材质源（水地块 prefab 材质槽 1）——本次跳过");
                return;
            }
            if (_thinIceMaterial == null)
            {
                _thinIceMaterial = new Material(waterMat); // 运行时实例（OnDestroy 释放；波场参数与水面同源）
                if (_thinIceMaterial.HasProperty("_BaseColor")) _thinIceMaterial.SetColor("_BaseColor", 薄冰色);
                if (_thinIceMaterial.HasProperty("_DeepColor")) _thinIceMaterial.SetColor("_DeepColor", 薄冰深色);
                if (_thinIceMaterial.HasProperty("_FlowSpeed")) _thinIceMaterial.SetFloat("_FlowSpeed", 薄冰流动速度);
                if (_thinIceMaterial.HasProperty("_Highlight")) _thinIceMaterial.SetFloat("_Highlight", 薄冰高光);
            }
            else
            {
                // 实例已建（Inspector 参数热改后重建面板沿用新值——一次性 tint 需重写）
                if (_thinIceMaterial.HasProperty("_BaseColor")) _thinIceMaterial.SetColor("_BaseColor", 薄冰色);
                if (_thinIceMaterial.HasProperty("_DeepColor")) _thinIceMaterial.SetColor("_DeepColor", 薄冰深色);
                if (_thinIceMaterial.HasProperty("_FlowSpeed")) _thinIceMaterial.SetFloat("_FlowSpeed", 薄冰流动速度);
                if (_thinIceMaterial.HasProperty("_Highlight")) _thinIceMaterial.SetFloat("_Highlight", 薄冰高光);
            }

            _thinIceMesh = BuildSurfaceMesh(cells, "BattleThinIceSurface", _waterSurfaceHeight + 薄冰抬升);
            _runtimeMeshes.Add(_thinIceMesh);
            _thinIceSurface = new GameObject("ThinIceSurface");
            _thinIceSurface.transform.SetParent(_tilesRoot, false);
            var meshFilter = _thinIceSurface.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = _thinIceMesh;
            var meshRenderer = _thinIceSurface.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = _thinIceMaterial;
            // 同水面（-2）：薄冰面板恒为最低透明层（其下水面同序-2=同序按距离，冰面恒在水面上方
            // 由高度差+半透明混合保证；瞄准贴片 0/立牌 10/箭矢 12 全在其上）
            meshRenderer.sortingOrder = -2;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }

        /// <summary>焊接面 mesh 构建（水面/薄冰面板共用——相邻格共享角点焊接、细分加密波浪平滑；
        /// height=面片基准高度〔水面=_waterSurfaceHeight、薄冰=+抬升——波场位移由 shader 同公式
        /// 施加，与基准高度无关〕）</summary>
        private Mesh BuildSurfaceMesh(List<BattleCell> cells, string meshName, float height)
        {
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
                        int a = GetWaterCorner(cornerIndex, verts, uvs, Map, baseSubX + i, baseSubY + j, subdiv, height);
                        int b = GetWaterCorner(cornerIndex, verts, uvs, Map, baseSubX + i + 1, baseSubY + j, subdiv, height);
                        int c = GetWaterCorner(cornerIndex, verts, uvs, Map, baseSubX + i + 1, baseSubY + j + 1, subdiv, height);
                        int d = GetWaterCorner(cornerIndex, verts, uvs, Map, baseSubX + i, baseSubY + j + 1, subdiv, height);
                        // 绕向与旧格顶面一致（法线朝上）
                        tris.AddRange(new[] { d, c, b, d, b, a });
                    }
                }
            }

            var mesh = new Mesh { name = meshName };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
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

            var surfaceMesh = BuildSurfaceMesh(cells, "BattleWaterSurface", _waterSurfaceHeight);
            _runtimeMeshes.Add(surfaceMesh); // 运行时 mesh 登记（Build 重建/OnDestroy 显式销毁，§63①）

            var surfaceGo = new GameObject("WaterSurface");
            surfaceGo.transform.SetParent(_tilesRoot, false);
            var meshFilter = surfaceGo.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = surfaceMesh;
            var meshRenderer = surfaceGo.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = GetWaterSurfaceMaterial();
            // 透明排序实证（2026-10-06 像素回读定裁）：sortingOrder 支配 renderQueue——水面若留
            // order 0 会整片画在底座圆盘（-1）之上=「圆盘沉到水面下」报障根因（旧认知「盘 queue3000
            // > 水 2999 故盘在水面之上」被推翻）。-2=水面恒为最低透明层（画于盘 -1 之下、瞄准贴片 0/
            // 立牌 10/箭矢 12 等一切之上），显式实现 2999 队列的本意。勿"修"回 0
            meshRenderer.sortingOrder = -2;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }

        /// <summary>
        /// 水面细分角点（焊接：同角点复用同顶点，波浪位移天然连续）。
        /// subX/subY=细分网格坐标（格坐标×细分）；UV=角点世界坐标（贴图 Repeat 每格重复一次，
        /// 与旧每格 0..1 同密度，图案跨格连续）；height=面片基准高度（水面/薄冰抬升共用）
        /// </summary>
        private int GetWaterCorner(Dictionary<long, int> cornerIndex, List<Vector3> verts, List<Vector2> uvs,
            BattleMapData map, int subX, int subY, int subdiv, float height)
        {
            long key = ((long)subX << 32) | (uint)subY;
            if (cornerIndex.TryGetValue(key, out int index)) return index;
            var pos = new Vector3((float)subX / subdiv - map.width / 2f, height, (float)subY / subdiv - map.height / 2f);
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
        /// 「底座圆盘也改」）；薄冰格同口径（冰面同波场起伏=冰上件须同过波峰带，决策七十六）；
        /// 拾取（GetSurfaceHeight）与 Host 判定仍用名义表面，波峰带纯视觉</summary>
        public float GetVisualSurfaceHeight(BattleCell cell)
        {
            float wave = 0f;
            if (Map != null && Map.HasTile(cell.x, cell.y))
            {
                var tile = Map.GetTile(cell.x, cell.y);
                if (tile == TileType.Water || tile == TileType.Swamp || tile == TileType.ThinIce)
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
                case TileType.ThinIce: // 薄冰=水面体+顶面冰面板（决策七十六；初始地图不使用，防御性支持重建）
                    return _waterTilePrefab;
                case TileType.StonePath:
                    return _stoneTilePrefab;
                case TileType.Dirt:
                    return _dirtTilePrefab;
                case TileType.Cobblestone:
                    return _cobbleTilePrefab;
                default:
                    return _grassTilePrefab;
            }
        }

        /// <summary>
        /// 顶面材质变体（2026-09-27 地形批次）：同型地块每格确定性随机选一变体，防整片重复。
        /// 确定性哈希（非 UnityEngine.Random）=重建稳定，编辑器建盘断言与运行时同结果。
        /// </summary>
        private void ApplyTopVariant(GameObject tile, TileType tileType, int x, int y)
        {
            var variants = _tileVisuals != null ? _tileVisuals.GetTopVariants(tileType) : null;
            if (variants == null || variants.Length == 0) return;
            var renderer = tile.GetComponent<MeshRenderer>();
            if (renderer == null) return;
            var mats = renderer.sharedMaterials;
            if (mats.Length < 2) return; // 顶面=材质槽 1
            mats[1] = variants[CellVariantIndex(x, y, variants.Length)];
            renderer.sharedMaterials = mats;
        }

        /// <summary>格坐标确定性哈希 → [0, count) 变体序号（internal：房间地图预览复用同哈希——
        /// 预览逐格变体与战场建盘完全一致，所见即所得；确定性=重建稳定）</summary>
        internal static int CellVariantIndex(int x, int y, int count)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ 0x9E3779B9u;
                return (int)(h % (uint)count);
            }
        }

        /// <summary>
        /// 构建草簇装饰层（2026-09-27 地形批次：立牌式草簇随机铺满草地格）。
        /// BattlePlayer 建盘后传战场相机（编辑器侧建盘断言无相机时传 null，朝向兜底正北）。
        /// </summary>
        public void BuildGrassDecor(Camera cam)
        {
            BattleGrassDecor.Build(_tilesRoot, Map, this, _tileVisuals, cam);
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
