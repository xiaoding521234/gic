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
    /// <summary>
    /// 启动动画屏 — 淡入/保持/淡出 Logo 后进入大厅。
    /// 跳过方式：任意键或鼠标按钮。
    /// 启动后 0.3 秒内通过 InputLock 保护，防止误触。
    /// </summary>
    public class SplashScreen : ScreenBase
    {
        [Header("UI组件")]
        [SerializeField] private Image logoImage;
        
        [Header("动画参数")]
        [SerializeField] private float fadeInDuration = 0.85f;
        [SerializeField] private float holdDuration = 0.5f;
        [SerializeField] private float fadeOutDuration = 0.7f;
        
        [Header("调试设置")]
        [SerializeField] private bool skipAnimation = false;

        private CanvasGroup logoCanvasGroup;
        private bool isSkipped = false;

        [Autowired] private AssetCache _assetCache;
        [Autowired] private PositionManager _positionManager;

        // ── IClosable 实现：跳过动画直接进大厅（不走标准关闭模板，Splash 无 GoBack 语义） ──
        public override void Close() => SkipAnimation();

        protected override void Awake()
        {
            base.Awake(); // 注入（Boot 链路容器已就绪）

            logoCanvasGroup = logoImage.GetComponent<CanvasGroup>();
            if (logoCanvasGroup == null)
                logoCanvasGroup = logoImage.gameObject.AddComponent<CanvasGroup>();
            logoCanvasGroup.alpha = 0f;
        }
        
        private void Start()
        {
            RegisterClosableSelf();

            // 启动后 0.3 秒内锁定输入，防止误触
            InputLocks.Push(this, InputLockReason.SplashProtection);
            StartCoroutine(ReleaseProtectionAfter(0.3f));

            _assetCache?.Preload<Sprite>("WishArt/columbina");
            PreloadMainHallBackground();

            if (skipAnimation)
            {
                GICLog.Info("跳过启动动画，直接进入大厅");
                SceneType.MainHall.Load();
                return;
            }
            
            StartCoroutine(PlayLogoAnimation());
        }

        private IEnumerator ReleaseProtectionAfter(float delay)
        {
            yield return Wait.Seconds(delay);
            InputLocks.Pop(this, InputLockReason.SplashProtection);
        }

        private void Update()
        {
            if (skipAnimation || isSkipped) return;

            // InputLock 激活时 Update 仍会执行（MonoBehaviour.Update 独立于 InputManager.Update）
            // 但 SplashScreen 的跳过是本地检测，不受 InputManager 管
            // InputLock 期间 SplashScreen 自己也不跳过
            if (InputLocks.IsLocked) return;

            if (Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))
            {
                SkipAnimation();
            }
        }

        // OnDestroy 兜底（注销可关闭 + PopAll 输入锁）由 ScreenBase 统一处理

        private void SkipAnimation()
        {
            if (isSkipped) return;
            isSkipped = true;
            GICLog.Info("跳过启动动画");
            
            StopAllCoroutines();
            SceneType.MainHall.Load();
        }
        
        private IEnumerator PlayLogoAnimation()
        {
            yield return StartCoroutine(FadeLogo(0f, 1f, fadeInDuration));
            yield return Wait.Seconds(holdDuration);
            yield return StartCoroutine(FadeLogo(1f, 0f, fadeOutDuration));
            
            SceneType.MainHall.Load();
        }
        
        private void PreloadMainHallBackground()
        {
            if (_positionManager == null || _assetCache == null) return;

            var position = _positionManager.CurrentPosition;
            var positionData = _positionManager.GetPositionData(position);
            if (positionData == null) return;

            string regionName = positionData.region.ToString();
            string positionName = position.ToString().ToSnakeCase();
            string timeSuffix = TimeUtility.GetTimeSuffix();
            string bgAddress = $"PositionBack/{regionName}/{positionName}_{timeSuffix}";

            _assetCache.Preload<Sprite>(bgAddress);
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
