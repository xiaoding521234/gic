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


    public class MapScreen : MonoBehaviour, IClosable
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

        [Header("地图结构引用（场景中静态）")]
        [SerializeField] private RectTransform mapContent;      // ScrollRect.content
        [SerializeField] private RectTransform mapImage;        // MapImage (大地图纹理)
        [SerializeField] private Transform anchorsContainer;   // 锚点父节点
        [SerializeField] private SimpleMapZoom mapZoom;        // 缩放/入场动画控制器

        [Header("配置")]
        [SerializeField] private MapConfig mapConfig;
        [SerializeField] private GameObject anchorPrefab;      // Anchor.prefab

        [Header("淡出动画")]
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private float fadeOutDuration = 0.3f;
        [SerializeField] private AnimationCurve fadeOutCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        private RegionName currentRegion;
        private readonly List<GameObject> _spawnedAnchors = new();
        private bool isClosing = false;

        // ── IClosable 实现 ──
        void IClosable.Close() => OnCloseClick();

        [Autowired] private PositionManager _positionManager;
        [Autowired] private InputManager _inputManager;


        private void Start()
        {
            Wargame.Instance.Context.Inject(this);
            _inputManager?.RegisterClosable(this);
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

            AudioManager.Instance.PushMusicVolume();
            ShowRegion(_positionManager.GetCurrentRegion());
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

            // 应用地图位置、尺寸和滚动范围
            mapImage.anchoredPosition = data.mapImageAnchoredPosition;
            mapImage.sizeDelta = data.mapImageSize;
            mapContent.anchoredPosition = data.contentAnchoredPosition;
            mapContent.sizeDelta = data.contentSizeDelta;

            SpawnAnchors(data);

            // 更新缩放控制器的原始位置（使用新的 Content 位置），然后重置并播放入场动画
            if (mapZoom != null)
            {
                mapZoom.UpdateOriginalPosition();
                mapZoom.ResetMap();
                mapZoom.ReplayEntryAnimation();
            }
        }

        private void SpawnAnchors(MapConfig.RegionData data)
        {
            Vector2 mapSize = mapImage.sizeDelta;
            Vector2 mapPos = mapImage.anchoredPosition;
            var posManager = _positionManager;

            foreach (var anchor in data.anchors)
            {
                var go = Instantiate(anchorPrefab, anchorsContainer);
                var rt = go.GetComponent<RectTransform>();

                // 归一化坐标 → 像素位置（相对于 MapImage 中心，pivot=0.5,0.5）
                float px = mapPos.x + (anchor.normalizedX - 0.5f) * mapSize.x;
                float py = mapPos.y + (0.5f - anchor.normalizedY) * mapSize.y;
                rt.anchoredPosition = new Vector2(px, py);

                // 初始化 MapAnchor 数据
                var mapAnchor = go.GetComponent<MapAnchor>();
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
            if (isClosing) return;
            isClosing = true;
            InputLocks.Push(this, InputLockReason.Closing);
            AudioManager.Instance.PopMusicVolume();
            CloseWithFade();
        }

        /// <summary>
        /// 淡出后返回大厅（关闭按钮和锚点传送共用）
        /// </summary>
        public void CloseWithFade()
        {
            StartCoroutine(CloseWithFadeCoroutine());
        }

        private IEnumerator CloseWithFadeCoroutine()
        {
            // 淡出动画，给大厅背景加载留出时间
            if (canvasGroup != null)
            {
                float startTime = Time.realtimeSinceStartup;
                float startAlpha = canvasGroup.alpha;

                while (true)
                {
                    float elapsed = Time.realtimeSinceStartup - startTime;
                    if (elapsed >= fadeOutDuration) break;

                    float t = fadeOutCurve.Evaluate(elapsed / fadeOutDuration);
                    canvasGroup.alpha = Mathf.LerpUnclamped(startAlpha, 0f, t);
                    yield return null;
                }
                canvasGroup.alpha = 0f;
            }

            InputLocks.Pop(this, InputLockReason.Closing);
            GameScene.Instance.GoBack();
        }

        private void OnDestroy()
        {
            _inputManager?.UnregisterClosable(this);
            // 兜底：淡出协程被销毁中断时释放本类持有的锁
            InputLocks.PopAll(this);
            ClearAnchors();
        }
    }
}
