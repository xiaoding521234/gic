// MapTileLayer.cs - 大地图瓦片层（视野动态加载/释放）
using System.Collections.Generic;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
using UnityEngine.Serialization;

namespace GIC.UI
{
    /// <summary>
    /// 大地图瓦片层 — 源图按 tilePixelSize 网格切片（含 tileOverlapPx 重叠边防双线性接缝），
    /// 每帧按相机可视矩形计算所需瓦片集：缺失 → AssetCache.LoadAsync（离视野中心近者优先入队），
    /// 超出滞回区 → Release。瓦片像素矩形由 MapConfig 标定参数换算成世界坐标，
    /// 与 MapScreen.ApplyMapCalibration 同一坐标系（图片左上角 = MapOrigin，图片 Y 向下）。
    /// 渲染层级：MapPlane(z=0) < 瓦片(z=瓦片前移) < 锚点(z≈-0.7)，全 sortingOrder=0 靠深度排序。
    /// </summary>
    public class MapTileLayer : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private MapConfig mapConfig;
        [SerializeField] private Camera mapCamera;

        [Header("显示")]
        [Tooltip("瓦片相对 MapPlane(z=0) 的 Z 偏移（负值靠相机侧，须小于锚点悬浮偏移 -0.7 的绝对值）")]
        [InspectorName("瓦片前移")]
        [SerializeField] private float tileZOffset = -0.02f;

        [Tooltip("可视范围外扩瓦片数：提前加载，平移时不易露底")]
        [SerializeField, Range(0, 3)] private int preloadMargin = 1;

        [Tooltip("释放滞回瓦片数：超出 可视+预载 此距离才释放，避免边缘反复加载/卸载")]
        [SerializeField, Range(0, 3)] private int releaseHysteresis = 1;

        [Autowired] private AssetCache _assetCache;

        // 懒注入：场景物体激活时序不保证容器已就绪（参照 CharacterPanelController.AssetCacheRef 模式）
        private AssetCache 缓存
        {
            get
            {
                if (_assetCache == null)
                    Wargame.Instance?.Context?.Inject(this);
                return _assetCache;
            }
        }

        private readonly Dictionary<Vector2Int, SpriteRenderer> _live = new();   // 已渲染
        private readonly HashSet<Vector2Int> _loading = new();                    // 加载中未回调
        private readonly HashSet<Vector2Int> _释放中 = new();                     // 加载未完成即被释放
        private readonly HashSet<Vector2Int> _wanted = new();                     // 本帧需要集
        private readonly List<Vector2Int> _removalTmp = new();
        private readonly List<(Vector2Int id, float dist)> _missingTmp = new();

        private void Update()
        {
            if (mapConfig == null || mapCamera == null || !mapConfig.IsTiled) return;
            if (缓存 == null) return; // 容器未就绪（Boot 流程未跑完）时静默等下一帧
            UpdateVisibleSet();
        }

        private void OnDestroy()
        {
            // 场景卸载兜底：归还所有瓦片引用（应用退出时容器可能已销毁，跳过）
            if (Wargame.Instance == null || _assetCache == null) return;
            foreach (var renderer in _live.Values)
                if (renderer != null) Destroy(renderer.gameObject);
            foreach (var id in _live.Keys) _assetCache.Release(Address(id));
            foreach (var id in _loading) _assetCache.Release(Address(id));
            _live.Clear();
            _loading.Clear();
            _释放中.Clear();
        }

        private string Address(Vector2Int id) => $"{mapConfig.TileAddressPrefix}{id.x}_{id.y}";

        // ==================== 几何换算（静态，预载器与运行层共用） ====================

        /// <summary>
        /// 世界矩形（含 margin 格外扩）→ 瓦片索引范围（图片左上原点、Y 向下，与 MapConfig.WorldToPixel 同向）。
        /// 范围钳制在网格内。
        /// </summary>
        public static void CalcTileRange(MapConfig cfg, float minX, float minY, float maxX, float maxY,
            float marginTiles, out int x0, out int y0, out int x1, out int y1)
        {
            float u = cfg.WorldUnitsPerPixel;
            float tileWorld = cfg.TilePixelSize * u;
            int TileX(float wx) => Mathf.FloorToInt((wx - cfg.MapOrigin.x) / u / cfg.TilePixelSize);
            int TileY(float wy) => Mathf.FloorToInt((cfg.MapOrigin.y - wy) / u / cfg.TilePixelSize);
            x0 = Mathf.Clamp(TileX(minX - tileWorld * marginTiles), 0, cfg.TileColumns - 1);
            x1 = Mathf.Clamp(TileX(maxX + tileWorld * marginTiles), 0, cfg.TileColumns - 1);
            y0 = Mathf.Clamp(TileY(maxY + tileWorld * marginTiles), 0, cfg.TileRows - 1); // 世界 Y 大 = 图片 y 小
            y1 = Mathf.Clamp(TileY(minY - tileWorld * marginTiles), 0, cfg.TileRows - 1);
        }

        /// <summary>瓦片中心世界坐标（按网格名义区域，忽略重叠边）</summary>
        public static Vector2 TileCenterWorld(MapConfig cfg, Vector2Int id)
        {
            float u = cfg.WorldUnitsPerPixel;
            return new Vector2(
                cfg.MapOrigin.x + (id.x + 0.5f) * cfg.TilePixelSize * u,
                cfg.MapOrigin.y - (id.y + 0.5f) * cfg.TilePixelSize * u);
        }

        /// <summary>按相机可视矩形维护瓦片集：加载需要的、释放滞回区外的</summary>
        private void UpdateVisibleSet()
        {
            var cfg = mapConfig;

            // 相机可视世界矩形
            float halfH = mapCamera.orthographicSize;
            float halfW = halfH * mapCamera.aspect;
            var cp = mapCamera.transform.position;
            float viewMinX = cp.x - halfW, viewMaxX = cp.x + halfW;
            float viewMinY = cp.y - halfH, viewMaxY = cp.y + halfH;

            // 需要集 = 可视区外扩 预载边距 格
            _wanted.Clear();
            CalcTileRange(cfg, viewMinX, viewMinY, viewMaxX, viewMaxY, preloadMargin, out int lx0, out int ly0, out int lx1, out int ly1);
            for (int y = ly0; y <= ly1; y++)
                for (int x = lx0; x <= lx1; x++)
                    _wanted.Add(new Vector2Int(x, y));

            // 保活区 = 可视区外扩 预载边距+释放滞回 格；区外的释放
            int keep = preloadMargin + releaseHysteresis;
            float tileWorld = cfg.TilePixelSize * cfg.WorldUnitsPerPixel;
            float keepMinX = viewMinX - tileWorld * keep, keepMaxX = viewMaxX + tileWorld * keep;
            float keepMinY = viewMinY - tileWorld * keep, keepMaxY = viewMaxY + tileWorld * keep;

            _removalTmp.Clear();
            foreach (var kv in _live)
            {
                if (!InKeepArea(kv.Key, keepMinX, keepMaxX, keepMinY, keepMaxY, cfg))
                    _removalTmp.Add(kv.Key);
            }
            foreach (var id in _removalTmp)
            {
                if (_live.TryGetValue(id, out var renderer) && renderer != null)
                    Destroy(renderer.gameObject);
                _live.Remove(id);
                _assetCache.Release(Address(id));
            }

            _removalTmp.Clear();
            foreach (var id in _loading)
            {
                if (!InKeepArea(id, keepMinX, keepMaxX, keepMinY, keepMaxY, cfg))
                    _removalTmp.Add(id);
            }
            foreach (var id in _removalTmp)
            {
                _loading.Remove(id);
                _释放中.Add(id); // 回调到达时凭此标记跳过（引用已在下方 Release 归还）
                _assetCache.Release(Address(id));
            }

            // 缺失集：按离视野中心距离排序，中心瓦片先加载
            if (_wanted.Count == _live.Count + _loading.Count)
            {
                bool complete = true;
                foreach (var id in _wanted)
                    if (!_live.ContainsKey(id) && !_loading.Contains(id)) { complete = false; break; }
                if (complete) return;
            }

            _missingTmp.Clear();
            var center = new Vector2(cp.x, cp.y);
            foreach (var id in _wanted)
            {
                if (_live.ContainsKey(id) || _loading.Contains(id)) continue;
                var tileCenter = TileCenterWorld(cfg, id);
                _missingTmp.Add((id, (tileCenter - center).sqrMagnitude));
            }
            _missingTmp.Sort((a, b) => a.dist.CompareTo(b.dist));
            foreach (var (id, _) in _missingTmp)
            {
                _loading.Add(id);
                // High 优先级：瓦片是打开地图的主视觉，插队先于大厅背景等低优先级加载
                _assetCache.LoadAsync<Sprite>(Address(id), sprite => OnTileLoaded(id, sprite), LoadPriority.High);
            }
        }

        private bool InKeepArea(Vector2Int id, float minX, float maxX, float minY, float maxY, MapConfig cfg)
        {
            var c = TileCenterWorld(cfg, id);
            float half = cfg.TilePixelSize * cfg.WorldUnitsPerPixel * 0.5f;
            return c.x + half >= minX && c.x - half <= maxX && c.y + half >= minY && c.y - half <= maxY;
        }

        private void OnTileLoaded(Vector2Int id, Sprite sprite)
        {
            _loading.Remove(id);

            if (_释放中.Remove(id)) return; // 加载期间已被释放，引用已在释放路径归还
            if (sprite == null)
            {
                GICLog.Warn($"[MapTileLayer] 瓦片加载失败: {Address(id)}");
                return;
            }
            if (!_wanted.Contains(id))
            {
                _assetCache.Release(Address(id)); // 已滚出视野，归还本次引用
                return;
            }

            PlaceTile(id, sprite);
        }

        /// <summary>按标定参数把瓦片 sprite 摆进世界（重叠边使瓦片间有 2×重叠像素带，内容一致无接缝）</summary>
        private void PlaceTile(Vector2Int id, Sprite sprite)
        {
            GetTileWorldRect(mapConfig, id, out var center2, out var worldSize);
            var center = new Vector3(center2.x, center2.y, tileZOffset);

            if (!_live.TryGetValue(id, out var renderer) || renderer == null)
            {
                var go = new GameObject($"tile_{id.x}_{id.y}");
                go.transform.SetParent(transform, false);
                renderer = go.AddComponent<SpriteRenderer>();
                _live[id] = renderer;
            }

            var b = sprite.bounds.size;
            renderer.transform.localPosition = center;
            renderer.transform.localRotation = Quaternion.identity;
            renderer.transform.localScale = new Vector3(worldSize.x / b.x, worldSize.y / b.y, 1f);
            renderer.sprite = sprite;
        }

        /// <summary>
        /// 瓦片（含重叠边）的世界中心与尺寸（图片左上原点、Y 向下，地图边缘钳制）。
        /// 运行时铺放与编辑器高清瓦片层（MapEditorFullRes）共用此几何，勿在他处复制公式。
        /// </summary>
        public static void GetTileWorldRect(MapConfig cfg, Vector2Int id, out Vector2 center, out Vector2 size)
        {
            float u = cfg.WorldUnitsPerPixel;
            int t = cfg.TilePixelSize, o = cfg.TileOverlapPx;

            // 瓦片像素矩形（含重叠边，地图边缘钳制）
            int px0 = Mathf.Max(0, id.x * t - o);
            int py0 = Mathf.Max(0, id.y * t - o);
            int px1 = Mathf.Min(cfg.SourcePixelWidth, (id.x + 1) * t + o);
            int py1 = Mathf.Min(cfg.SourcePixelHeight, (id.y + 1) * t + o);

            size = new Vector2((px1 - px0) * u, (py1 - py0) * u);
            // 图片像素 → 世界：左上角 + (px, −py)
            center = new Vector2(
                cfg.MapOrigin.x + (px0 + px1) * 0.5f * u,
                cfg.MapOrigin.y - (py0 + py1) * 0.5f * u);
        }
    }
}
