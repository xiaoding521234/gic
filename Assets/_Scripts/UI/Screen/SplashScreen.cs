using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using GIC.Framework;
using GIC.Data;
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

        // ── 大厅背景视频预热（2026-10-11 方案 A）：Preload 只读资产不碰解码器，此协程补预解码首帧 ──
        private string _prewarmBgAddress;
        private RenderTexture _prewarmRt;   // 未移交的预热 RT（协程被跳过/场景切换打断时兜底销毁）
        private bool _prewarmRefHeld;       // LoadAsync 引用计数是否已归还（打断路径由 OnDestroy 补还）

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

            if (WaitForAnyTap.Any())
            {
                SkipAnimation();
            }
        }

        // OnDestroy 兜底（注销可关闭 + PopAll 输入锁）由 ScreenBase 统一处理；
        // 本类 override 只补预热资源清理（见下 PrewarmHallBackgroundVideo 尾部）

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

            var timePeriod = TimeUtility.GetCurrentTimePeriod();
            string bgAddress = positionData.GetBackgroundAddress(timePeriod);

            if (positionData.GetMediaType(timePeriod) == PositionMediaType.Video)
            {
                _assetCache.Preload<VideoClip>(bgAddress);
                StartCoroutine(PrewarmHallBackgroundVideo(bgAddress)); // 解码预热（手机端黑屏根因）
            }
            else
                _assetCache.Preload<Sprite>(bgAddress);
        }

        /// <summary>大厅背景视频解码预热：等资产进缓存 → 临时 VideoPlayer Prepare → 首帧渲入 RT
        /// → 移交 HallBackgroundPrewarm（MainHall 认领后开播近零黑窗）。
        /// 全程超时兜底回落「进大厅现建 RT」旧行为——预热失败零回归；被跳过/切场景打断则由 OnDestroy 清理。</summary>
        private IEnumerator PrewarmHallBackgroundVideo(string address)
        {
            _prewarmBgAddress = address;

            // 1. 等资产进缓存（Preload 已入队；手机冷启动闪存慢，预算给足——超时=放弃预热）
            float loadDeadline = Time.unscaledTime + 15f;
            while (!_assetCache.IsCached(address) && Time.unscaledTime < loadDeadline)
                yield return null;
            if (!_assetCache.IsCached(address)) yield break;

            // 2. 取 clip（缓存命中=同步回调）；引用计数配对归还（persistent 预载常驻不受影响）
            VideoClip clip = null;
            _assetCache.LoadAsync<VideoClip>(address, c => clip = c, LoadPriority.High);
            _prewarmRefHeld = true;
            if (clip == null)
            {
                _assetCache.Release(address);
                _prewarmRefHeld = false;
                yield break;
            }

            // 3. 临时播放器 Prepare → 首帧渲入 RT（照抄战斗立牌 PrewarmClipRoutine 模式，决策四十五同款根因）
            _prewarmRt = new RenderTexture((int)clip.width, (int)clip.height, 0);
            var host = new GameObject("HallBgVideoPrewarm");
            host.transform.SetParent(transform, false); // 随 Splash 场景销毁，无残留
            var vp = host.AddComponent<VideoPlayer>();
            vp.playOnAwake = false;
            vp.clip = clip;
            vp.renderMode = VideoRenderMode.RenderTexture;
            vp.targetTexture = _prewarmRt;
            vp.audioOutputMode = VideoAudioOutputMode.None;
            vp.Prepare();

            float prepareDeadline = Time.unscaledTime + 5f;
            while (!vp.isPrepared && Time.unscaledTime < prepareDeadline)
                yield return null;
            if (vp.isPrepared)
            {
                vp.Play();
                // 判 frame≥1 比固定等两帧稳（Android MediaCodec 首帧远慢于 WMF）；拿不到帧=预热失败回落
                float frameDeadline = Time.unscaledTime + 3f;
                while (vp.frame < 1 && Time.unscaledTime < frameDeadline)
                    yield return null;
            }
            bool gotFrame = vp.frame >= 1;
            vp.Stop();
            vp.clip = null;
            Destroy(host);

            // 4. 成功才移交（RT 带首帧常驻静态槽，跨场景存活）；失败就地销毁不污染认领口
            if (gotFrame)
            {
                var rt = _prewarmRt;
                _prewarmRt = null;
                HallBackgroundPrewarm.Deposit(address, rt);
                GICLog.Info($"[SplashScreen] 大厅背景视频预热完成（首帧已渲入 RT）: {address}");
            }
            else
            {
                DestroyPrewarmRt();
                GICLog.Warn($"[SplashScreen] 大厅背景视频预热超时，回落进大厅现建 RT: {address}");
            }
            _assetCache.Release(address);
            _prewarmRefHeld = false;
        }

        private void DestroyPrewarmRt()
        {
            if (_prewarmRt == null) return;
            _prewarmRt.Release();
            Destroy(_prewarmRt);
            _prewarmRt = null;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy(); // 注销可关闭 + PopAll 输入锁（ScreenBase 统一兜底）

            // 协程被跳过/场景切换打断的兜底：未移交的预热 RT 与引用计数当场清理（已移交则归 MainHall 所有）
            DestroyPrewarmRt();
            if (_prewarmRefHeld && _prewarmBgAddress != null)
            {
                _assetCache?.Release(_prewarmBgAddress);
                _prewarmRefHeld = false;
            }
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
