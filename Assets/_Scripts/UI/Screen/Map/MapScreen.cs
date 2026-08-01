using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
using GIC.Tool;
namespace GIC.UI
{


    public class MapScreen : MonoBehaviour
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
        [SerializeField] private TextCombiner titleText;  // 改为 TextCombiner

        [Header("地图容器")]
        [SerializeField] private Transform mapContainer;

        [Header("性能优化")]
        [SerializeField] private bool unloadUnusedOnSwitch = true;

        [SerializeField] private GameObject currentMap;
        private RegionName currentRegion;
        


        private void Start()
        {
            // 绑定按钮事件（保持不变）
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
            ShowRegion(Wargame.Instance.PositionManager.GetCurrentRegion());
        }

        public void ShowRegion(RegionName region)
        {
            if (currentRegion == region && currentMap != null) return;

            currentRegion = region;

            if (titleText != null)
            {
                titleText.ClearAllEntries();
                titleText.AddEntry(region.GetEntry());
            }

            // 销毁实例
            if (currentMap != null)
            {
                Destroy(currentMap);
                currentMap = null;
            }

            // 关键：强制清理未被引用的资源
            if (unloadUnusedOnSwitch)
            {
                StartCoroutine(UnloadUnusedAssetsCoroutine());
            }

            // 加载新地图
            LoadMapPrefab(region);
        }

        private void LoadMapPrefab(RegionName region)
        {
            string path = region.GetResourcePath();
            GameObject prefab = Resources.Load<GameObject>(path);

            if (prefab == null)
            {
                Debug.LogError($"未找到地图预制体: {path}");
                return;
            }

            currentMap = Instantiate(prefab, mapContainer);
            
            Canvas mapCanvas = currentMap.GetComponent<Canvas>();
            if (mapCanvas != null)
            {
                mapCanvas.overrideSorting = true;
                mapCanvas.sortingOrder = 50;
            }
            
            InitializeMapAnchors(currentMap, region);
            
            // 加载完成后也清理一次，释放可能残留的旧资源
            if (unloadUnusedOnSwitch)
            {
                StartCoroutine(UnloadUnusedAssetsCoroutine());
            }
        }

        private void InitializeMapAnchors(GameObject mapInstance, RegionName region)
        {
            var anchors = mapInstance.GetComponentsInChildren<MapAnchor>(true);
            foreach (var anchor in anchors)
            {
                var data = Wargame.Instance.PositionManager.GetPositionData(anchor.PositionName);
                if (data != null && data.region == region)
                    anchor.SetData(data);
            }
        }

        private void OnCloseClick()
        {
            AudioManager.Instance.PopMusicVolume();
            GameScene.Instance.GoBack();
        }

        private IEnumerator UnloadUnusedAssetsCoroutine()
        {
            yield return null; // 等待一帧，确保 Destroy 操作完成
            
            // 调用两次以确保彻底清理
            AsyncOperation op = Resources.UnloadUnusedAssets();
            yield return op;
            
            // 可选：第二次调用
            yield return Resources.UnloadUnusedAssets();
            
            Debug.Log("未使用资源已清理");
        }
    }
}


