// MapScreen.cs - 大地图弹层 Screen（3D 地图 + 相机交互版）
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
using GIC.Tool;
namespace GIC.UI
{


    public class MapScreen : ScreenBase
    {
        [Header("固定UI")]
        [SerializeField] private Button closeButton;
        [SerializeField] private Button nodkraiButton;
        [SerializeField] private Button mondstadtButton;
        [SerializeField] private Button liyueButton;
        [SerializeField] private Button inazumaButton;
        [SerializeField] private Button sumeruButton;
        [SerializeField] private Button fontaineButton;
        [SerializeField] private Button natlanButton;
        [SerializeField] private Button snezhnayaButton;
        [SerializeField] private Button khaenriahButton;
        [SerializeField] private TextCombiner titleText;

        [Header("3D 地图结构引用（场景中静态）")]
        [SerializeField] private MapCameraController 地图相机;      // MapCamera 上的相机控制器
        [SerializeField] private SpriteRenderer 地图贴图;           // MapPlane（平铺 XZ 地面的大地图）
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
        private readonly List<GameObject> _spawnedAnchors = new();

        // ── IClosable 实现 ──
        public override void Close() => OnCloseClick();

        [Autowired] private PositionManager _positionManager;


        private void Start()
        {
            RegisterClosableSelf();
            if (closeButton != null)
                closeButton.onClick.AddListener(OnCloseClick);
            if (nodkraiButton != null)
                nodkraiButton.onClick.AddListener(() => ShowRegion(RegionName.Nodkrai));
            if (mondstadtButton != null)
                mondstadtButton.onClick.AddListener(() => ShowRegion(RegionName.Mondstadt));
            if (liyueButton != null)
                liyueButton.onClick.AddListener(() => ShowRegion(RegionName.Liyue));
            if (inazumaButton != null)
                inazumaButton.onClick.AddListener(() => ShowRegion(RegionName.Inazuma));
            if (sumeruButton != null)
                sumeruButton.onClick.AddListener(() => ShowRegion(RegionName.Sumeru));
            if (fontaineButton != null)
                fontaineButton.onClick.AddListener(() => ShowRegion(RegionName.Fontaine));
            if (natlanButton != null)
                natlanButton.onClick.AddListener(() => ShowRegion(RegionName.Natlan));
            if (snezhnayaButton != null)
                snezhnayaButton.onClick.AddListener(() => ShowRegion(RegionName.Snezhnaya));
            if (khaenriahButton != null)
                khaenriahButton.onClick.AddListener(() => ShowRegion(RegionName.Khaenriah));

            PushMusicVolumeSafe();

            // 以地图贴图实际尺寸初始化相机边界，聚焦当前区域并播放入场动画
            var mapSize = 地图贴图.bounds.size;
            地图相机.InitBounds(mapSize.x, mapSize.z);
            ShowRegion(_positionManager.GetCurrentRegion());

            // 3D 地图不吃 CanvasGroup，入场直接揭开（遮罩归零）
            if (淡入淡出遮罩 != null)
                淡入淡出遮罩.alpha = 0f;
        }

        public void ShowRegion(RegionName region)
        {
            if (currentRegion == region) return;
            currentRegion = region;

            if (titleText != null)
            {
                titleText.ClearAllEntries();
                titleText.AddEntry(region.GetEntry());
            }

            var data = mapConfig.GetRegion(region);
            if (data == null)
            {
                GICLog.Warn($"[MapScreen] 未找到区域配置: {region}");
                return;
            }

            ClearAnchors();

            // 区域视野中心（归一化）→ 世界坐标，相机聚焦（含入场动画）
            var mapSize = 地图贴图.bounds.size;
            Vector2 focusXZ = new(
                (data.viewCenter.x - 0.5f) * mapSize.x,
                (data.viewCenter.y - 0.5f) * mapSize.z);
            地图相机.FocusRegion(focusXZ, data.viewHeight);

            SpawnAnchors(data);
        }

        private void SpawnAnchors(MapConfig.RegionData data)
        {
            var mapSize = 地图贴图.bounds.size;
            var posManager = _positionManager;

            foreach (var anchor in data.anchors)
            {
                var go = Instantiate(anchorPrefab, 锚点容器);
                var mapAnchor = go.GetComponent<MapAnchor>();

                // 归一化坐标 → 地面世界坐标（0=图片左/上；XZ 平面上 +Z 朝南）
                float wx = (anchor.normalizedX - 0.5f) * mapSize.x;
                float wz = (anchor.normalizedY - 0.5f) * mapSize.z;
                go.transform.localPosition = new Vector3(wx, 0f, wz);

                mapAnchor.RefreshVisual();
                mapAnchor.SetPositionName(anchor.positionName);
                mapAnchor.SetMapScreen(this);
                var posData = posManager.GetPositionData(anchor.positionName);
                if (posData != null && posData.region == data.region)
                    mapAnchor.SetData(posData);

                _spawnedAnchors.Add(go);
            }
        }

        private void ClearAnchors()
        {
            foreach (var go in _spawnedAnchors)
            {
                if (go != null) Destroy(go);
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

        protected override void OnDestroy()
        {
            base.OnDestroy();
            ClearAnchors();
        }
    }
}
