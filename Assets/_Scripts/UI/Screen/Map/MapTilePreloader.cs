// MapTilePreloader.cs - 大地图瓦片预载（大厅待机时把初始视野瓦片载入缓存）
using System.Collections.Generic;
using GIC.Data;
using GIC.Framework;
using UnityEngine;

namespace GIC.UI
{
    /// <summary>
    /// 大地图瓦片预载器 — 在大厅（MapScreen 未激活）时按「当前区域初始视野」预载瓦片。
    ///
    /// 为什么需要：Additive 场景预加载（PreloadScene）不激活场景，MapTileLayer.Update 不会执行，
    /// 瓦片请求在场景激活后才发出 → 打开地图先见预览图后补瓦片（先糊后清）。
    /// 预载走 AssetCache.Preload（Persistent，不消费引用计数）：场景激活后 MapTileLayer.LoadAsync
    /// 直接缓存命中 → onComplete 同步回调 → 首帧 Update 内瓦片已落位 → 打开即清晰。
    ///
    /// 预载范围：初始区域视野中心，半高取 预载视野半高（覆盖入场动画的最大远景尺寸，略放余量），
    /// 外扩 预载边距+1 格。Persistent 常驻显存约 30-42 片 DXT1 ≈ 65-88MB；区域切换时旧集自动卸载（恒定）。
    /// 调用点：MainHallScreen 启动与位置切换（UpdateBackground）——传送后初始区域随之变化。
    /// </summary>
    public static class MapTilePreloader
    {
        /// <summary>预载视野半高（世界单位）。须 ≥ MapCameraController 最大尺寸（场景覆盖 15），
        /// 入场动画从最大远景落下，此值保证动画首帧瓦片已在</summary>
        private const float 预载视野半高 = 17f;

        /// <summary>预载外扩瓦片数（比 MapTileLayer 预载边距 多 1 格余量）</summary>
        private const float 预载外扩格数 = 2f;

        private static MapConfig _cfg;
        private static readonly List<string> _lastPreloaded = new();

        /// <summary>预载当前区域初始视野瓦片（Persistent）。依赖由调用方注入，MapConfig 走 Resources 自取。
        /// 区域变更时先卸载上一轮预载集（有活跃引用的瓦片等消费者 Release 归零后才真卸），防止跨区域累积常驻</summary>
        public static void PreloadInitialTiles(PositionManager positionManager, AssetCache assetCache)
        {
            if (positionManager == null || assetCache == null) return;
            if (_cfg == null) _cfg = Resources.Load<MapConfig>("Configs/MapConfig");
            if (_cfg == null || !_cfg.IsTiled) return;

            var region = positionManager.GetCurrentRegion();
            var data = _cfg.GetRegion(region);
            if (data == null)
            {
                GICLog.Warn($"[MapTilePreloader] 区域 {region} 无 RegionData，跳过瓦片预载");
                return;
            }

            float aspect = (float)Screen.width / Screen.height;
            float halfW = 预载视野半高 * aspect;
            var c = data.viewCenterWorld;

            MapTileLayer.CalcTileRange(_cfg, c.x - halfW, c.y - 预载视野半高, c.x + halfW, c.y + 预载视野半高,
                预载外扩格数, out int x0, out int y0, out int x1, out int y1);

            // 本轮预载集（先记旧集，重进大厅/同区域重复调用时与旧集一致则零卸载）
            var newSet = new List<string>();
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    string address = $"{_cfg.TileAddressPrefix}{x}_{y}";
                    assetCache.Preload<Sprite>(address);
                    newSet.Add(address);
                }
            }

            // 卸载旧集中不属于本轮的（区域已切换）。先 Preload 新集再卸旧：
            // 两区域相邻时重叠瓦片仍被本轮持有（IsPersistent=true），UnloadPreload 不会真卸
            if (_lastPreloaded.Count > 0)
            {
                var newHashSet = new HashSet<string>(newSet);
                int unloaded = 0;
                foreach (var address in _lastPreloaded)
                {
                    if (!newHashSet.Contains(address))
                    {
                        assetCache.UnloadPreload(address);
                        unloaded++;
                    }
                }
                if (unloaded > 0)
                    GICLog.Info($"[MapTilePreloader] 卸载上一区域旧瓦片 {unloaded} 片");
            }

            _lastPreloaded.Clear();
            _lastPreloaded.AddRange(newSet);
            GICLog.Info($"[MapTilePreloader] 区域 {region} 初始视野预载瓦片 {newSet.Count} 片（{x0}-{x1} 列 × {y0}-{y1} 行）");
        }
    }
}
