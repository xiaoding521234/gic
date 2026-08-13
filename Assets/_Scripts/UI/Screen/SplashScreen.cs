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


    public class SplashScreen : MonoBehaviour
    {
        [Header("UI组件")]
        [SerializeField] private Image logoImage;
        
        [Header("动画参数")]
        [SerializeField] private float fadeInDuration = 0.85f;      // 淡入时间
        [SerializeField] private float holdDuration = 0.5f;        // 保持时间
        [SerializeField] private float fadeOutDuration = 0.7f;     // 淡出时间
        
        [Header("调试设置")]
        [SerializeField] private bool skipAnimation = false;        // 跳过动画（测试用）
        [SerializeField] private KeyCode skipKey = KeyCode.Space;   // 跳过动画的按键
        
        private CanvasGroup logoCanvasGroup;
        private bool isSkipped = false;
        
        private void Awake()
        {
            // 获取或添加CanvasGroup组件
            logoCanvasGroup = logoImage.GetComponent<CanvasGroup>();
            if (logoCanvasGroup == null)
            {
                logoCanvasGroup = logoImage.gameObject.AddComponent<CanvasGroup>();
            }
            
            // 初始状态为完全透明
            logoCanvasGroup.alpha = 0f;
        }
        
        private void Start()
        {
            // 预加载首个祈愿角色立绘（4K），与 splash 动画并行加载
            // 比原来在 MainHall.Start() 中预加载提前了 ~2s（splash 动画时长）
            Wargame.Instance?.AssetCache?.Preload<Sprite>("WishArt/columbina");

            // 预加载大厅默认位置背景，与 splash 动画并行加载
            PreloadMainHallBackground();

            // 如果跳过动画，直接加载大厅
            if (skipAnimation)
            {
                Debug.Log("跳过启动动画，直接进入大厅");
                SceneType.MainHall.Load();
                return;
            }
            
            // 开始Logo动画
            StartCoroutine(PlayLogoAnimation());
        }
        
        private void Update()
        {
            // 按指定按键跳过动画
            if (!skipAnimation && !isSkipped && Input.GetKeyDown(skipKey))
            {
                SkipAnimation();
            }
        }
        
        private void SkipAnimation()
        {
            isSkipped = true;
            Debug.Log("跳过启动动画");
            
            // 停止所有协程
            StopAllCoroutines();
            
            // 直接进入大厅
            SceneType.MainHall.Load();
        }
        
        private IEnumerator PlayLogoAnimation()
        {
            // 淡入
            yield return StartCoroutine(FadeLogo(0f, 1f, fadeInDuration));
            
            // 保持
            yield return new WaitForSeconds(holdDuration);
            
            // 淡出
            yield return StartCoroutine(FadeLogo(1f, 0f, fadeOutDuration));
            
            // 动画完成，进入大厅
            SceneType.MainHall.Load();
        }
        
        private void PreloadMainHallBackground()
        {
            var wargame = Wargame.Instance;
            if (wargame?.PositionManager == null || wargame.AssetCache == null) return;

            var position = wargame.PositionManager.CurrentPosition;
            var positionData = wargame.PositionManager.GetPositionData(position);
            if (positionData == null) return;

            string regionName = positionData.region.ToString();
            string positionName = position.ToString().ToSnakeCase();
            string timeSuffix = TimeUtility.GetTimeSuffix();
            string bgAddress = $"PositionBack/{regionName}/{positionName}_{timeSuffix}";

            wargame.AssetCache.Preload<Sprite>(bgAddress);
        }

        private IEnumerator FadeLogo(float startAlpha, float targetAlpha, float duration)
        {
            float elapsedTime = 0f;
            
            while (elapsedTime < duration)
            {
                elapsedTime += Time.deltaTime;
                float alpha = Mathf.Lerp(startAlpha, targetAlpha, elapsedTime / duration);
                logoCanvasGroup.alpha = alpha;
                yield return null;
            }
            
            logoCanvasGroup.alpha = targetAlpha;
        }
    }
}


