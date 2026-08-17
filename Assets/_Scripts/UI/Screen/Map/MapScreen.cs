// MapScreen.cs - 大地图弹层 Screen（3D 地图 + 相机交互版）
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Tool;
namespace GIC.UI
{


    public class MapScreen : ScreenBase
    {
        [Header("固定UI")]
        [SerializeField] private Button closeButton;
        [Tooltip("区域按钮 → RegionName 映射。新增区域：数组加一条 + 场景拖入按钮即可，无需改代码")]
        [SerializeField] private List<RegionButtonEntry> regionButtons = new();
        [SerializeField] private TextCombiner titleText;

        [Serializable]
        public class RegionButtonEntry
        {
            public RegionName region;
            public Button button;
        }

        [Header("3D 地图结构引用（场景中静态）")]
        [SerializeField] private MapCameraController 地图相机;      // MapCamera 上的相机控制器
        [SerializeField] private SpriteRenderer 地图贴图;           // MapPlane（垂直画布 XY 平面上的大地图）
        [SerializeField] private Transform 锚点容器;                // MapWorld/Anchors，动态锚点挂载点

        [Header("配置")]
        [SerializeField] private MapConfig mapConfig;
        [SerializeField] private GameObject anchorPrefab;      // Anchor3D.prefab

        [Header("淡入淡出")]
        [SerializeField] private CanvasGroup canvasGroup;      // UI 淡出（画布层）
        [SerializeField] private CanvasGroup 淡入淡出遮罩;     // 全屏黑遮罩（覆盖 3D 地图，ignoreParentGroups）
        [SerializeField] private float fadeOutDuration = 0.3f;
        [SerializeField] private AnimationCurve fadeOutCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        private RegionName currentRegion;
        private readonly List<MapAnchor> _spawnedAnchors = new();

        // ── IClosable 实现 ──
        public override void Close() => OnCloseClick();

        [Autowired] private PositionManager _positionManager;


        private void Start()
        {
            RegisterClosableSelf();
            if (closeButton != null)
                closeButton.onClick.AddListener(OnCloseClick);
            foreach (var entry in regionButtons)
            {
                if (entry.button != null)
                    entry.button.onClick.AddListener(() => ShowRegion(entry.region));
            }

            PushMusicVolumeSafe();

            // 固定世界坐标系：按 MapConfig 标定参数摆放地图图片 + 初始化相机边界
            ApplyMapCalibration();

            // 原神式：全图锚点常驻显示（可自由拖拽浏览），区域按钮只移动相机视野
            SpawnAllAnchors();
            ShowRegion(_positionManager.GetCurrentRegion(), fromMaxZoom: true);

            // 3D 地图不吃 CanvasGroup，入场直接揭开（遮罩归零）
            if (淡入淡出遮罩 != null)
                淡入淡出遮罩.alpha = 0f;
        }

        /// <summary>
        /// 原神式固定世界坐标系标定：按 MapConfig 的原点/单位长度摆放地图图片并初始化相机边界。
        /// 标定不依赖 sprite 导入设置（缩放按像素数×单位长度计算，PPU 变化无影响）。
        /// 扩图/换图后只需在 MapConfig 改 mapOrigin/worldUnitsPerPixel，锚点与区域数据一律不动。
        /// </summary>
        public void ApplyMapCalibration()
        {
            var sprite = 地图贴图.sprite;
            if (sprite == null)
            {
                GICLog.Warn("[MapScreen] 地图贴图未指定 sprite，跳过标定");
                return;
            }

            // 图片世界尺寸 = 像素数 × 单位长度
            float w = sprite.rect.width * mapConfig.WorldUnitsPerPixel;
            float d = sprite.rect.height * mapConfig.WorldUnitsPerPixel;

            // 垂直画布：MapPlane rotation 归零（XY 平面），缩放直接 XY，sprite.bounds 即本地尺寸
            var b = sprite.bounds.size;
            地图贴图.transform.localRotation = Quaternion.identity;
            地图贴图.transform.localScale = new Vector3(w / b.x, d / b.y, 1f);

            // 图片左上角 = mapOrigin（图片顶边在世界 +Y 上方）→ 中心 = origin + (w/2, -d/2)
            地图贴图.transform.localPosition = new Vector3(
                mapConfig.MapOrigin.x + w * 0.5f,
                mapConfig.MapOrigin.y - d * 0.5f, 0f);

            地图相机.InitBounds(w, d);
        }

        /// <summary>
        /// 切换区域视野：只更新标题并移动相机，锚点全图常驻不受影响。
        /// 重复点击当前区域 = 重新居中该区域视野。
        /// fromMaxZoom=true 时从最大远景落下（仅初次打开地图用）。
        /// </summary>
        public void ShowRegion(RegionName region, bool fromMaxZoom = false)
        {
            if (currentRegion != region)
            {
                currentRegion = region;
                if (titleText != null)
                {
                    titleText.ClearAllEntries();
                    titleText.AddEntry(region.GetEntry());
                }
            }

            var data = mapConfig.GetRegion(region);
            if (data == null)
            {
                GICLog.Warn($"[MapScreen] 未找到区域配置: {region}");
                return;
            }

            // 区域视野中心为固定世界坐标，相机直接聚焦（区域切换为平滑滑移）
            地图相机.FocusRegion(data.viewCenterWorld, data.viewHeight, fromMaxZoom);
        }

        /// <summary>生成全图所有区域的锚点（打开地图时调用一次，原神式常驻显示）</summary>
        private void SpawnAllAnchors()
        {
            ClearAnchors();
            var posManager = _positionManager;

            foreach (var data in mapConfig.AllRegions)
            {
                if (data == null) continue;
                foreach (var anchor in data.anchors)
                {
                    var go = Instantiate(anchorPrefab, 锚点容器);
                    var mapAnchor = go.GetComponent<MapAnchor>();

                    // 锚点坐标为固定世界 XY 坐标（垂直画布 z=0，扩图不变），直接落位
                    go.transform.localPosition = new Vector3(anchor.world.x, anchor.world.y, 0f);

                    mapAnchor.RefreshVisual();
                    mapAnchor.SetPositionName(anchor.positionName);
                    mapAnchor.SetMapScreen(this);

                    // 配置错误显性化：缺失或区域冲突直接 Warn，便于排查（不中断生成）
                    var posData = posManager.GetPositionData(anchor.positionName);
                    if (posData == null)
                        GICLog.Warn($"[MapScreen] 锚点 {anchor.positionName} 在 PositionConfig 中未配置");
                    else if (posData.region != data.region)
                        GICLog.Warn($"[MapScreen] 锚点 {anchor.positionName} 区域不匹配：MapConfig={data.region}, PositionConfig={posData.region}");
                    else
                        mapAnchor.SetData(posData);

                    _spawnedAnchors.Add(mapAnchor);
                }
            }

            UpdateAnchorConstantScale(force: true);
        }

        /// <summary>上次应用锚点缩放时的相机尺寸（变更检测）</summary>
        private float _anchorScaleCamSize = -1f;

        /// <summary>
        /// 锚点恒定视觉尺寸：正交相机缩放时，锚点根节点按 相机尺寸/基准尺寸 比例缩放，
        /// 与相机的视野变化正好抵消——无论缩放如何，锚点在屏幕上看起来一样大。
        /// </summary>
        private void UpdateAnchorConstantScale(bool force = false)
        {
            if (地图相机 == null) return;
            float camSize = 地图相机.CurrentSize;
            if (!force && Mathf.Approximately(camSize, _anchorScaleCamSize)) return;
            _anchorScaleCamSize = camSize;

            float scale = 地图相机.BaseViewSize > 0f ? camSize / 地图相机.BaseViewSize : 1f;
            foreach (var anchor in _spawnedAnchors)
            {
                if (anchor != null)
                    anchor.ApplyCameraScale(scale);
            }
        }

        private void Update()
        {
            UpdateAnchorConstantScale();
        }

        private void ClearAnchors()
        {
            foreach (var anchor in _spawnedAnchors)
            {
                if (anchor != null) Destroy(anchor.gameObject);
            }
            _spawnedAnchors.Clear();
        }

        private void OnCloseClick()
        {
            CloseScreen(ExitFadeCoroutine);
        }

        /// <summary>
        /// 淡出后返回大厅（关闭按钮和锚点传送共用）。
        /// 走标准关闭模板：防重入守卫、Closing 锁与音乐恢复由模板负责。
        /// </summary>
        public void CloseWithFade()
        {
            CloseScreen(ExitFadeCoroutine);
        }
        /// <summary>
        /// 双路淡出：UI 由 CanvasGroup 淡出；3D 地图由全屏黑遮罩盖住。
        /// 给大厅背景加载留出时间。
        /// </summary>
        private IEnumerator ExitFadeCoroutine()
        {
            float startTime = Time.realtimeSinceStartup;
            float startAlpha = canvasGroup != null ? canvasGroup.alpha : 1f;
            float startMask = 淡入淡出遮罩 != null ? 淡入淡出遮罩.alpha : 0f;

            while (true)
            {
                float elapsed = Time.realtimeSinceStartup - startTime;
                if (elapsed >= fadeOutDuration) break;

                float t = fadeOutCurve.Evaluate(elapsed / fadeOutDuration);
                if (canvasGroup != null)
                    canvasGroup.alpha = Mathf.LerpUnclamped(startAlpha, 0f, t);
                if (淡入淡出遮罩 != null)
                    淡入淡出遮罩.alpha = Mathf.LerpUnclamped(startMask, 1f, t);
                yield return null;
            }

            if (canvasGroup != null)
                canvasGroup.alpha = 0f;
            if (淡入淡出遮罩 != null)
                淡入淡出遮罩.alpha = 1f;
        }
    }
}
