// ==================== LoadingOverlayDriver.cs（原神式加载页驱动——开局转场加载界面） ====================
// 布局复刻原神加载页：纯白底 + 中央势力徽标缓转（按所选地图势力换标）+ 灰蓝词条文案
// （多条随机，点击页面任意处换一条）+ 底部元素行**逐像素填充扫描**（Dim/Lit 双层 + RectMask2D
// 裁剪矩形从左往右展宽=进度条式点亮，扫过处线与图标整列变深，与原神一致）+ 右下版本号。
// 挂 Assets/Resources/Prefabs/LoadingOverlay.prefab 根，由 SceneFadeOverlay 门面驱动
// （Cover/Reveal=整页 CanvasGroup 淡入淡出，盖住 StopHost 尖峰与场景加载）。
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using UnityEngine.Localization;
using GIC.Framework;
using GIC.Battle;
using GIC.Data;
using GIC.Data.Event;
using GIC.Tool;
namespace GIC.UI
{


    public class LoadingOverlayDriver : MonoBehaviour, IPointerClickHandler
    {
        [Header("动效参数")]
        [Tooltip("徽标每秒旋转角速度（度）")]
        [SerializeField] private float 徽标旋转速度 = 40f;
        [Tooltip("底部元素行逐像素填充扫描的全程时长（秒）；Reveal 等填充走完才揭幕 = 加载页保底展示时长")]
        [SerializeField] private float 填充时长 = 1f;

        [Header("节点引用（prefab 接线）")]
        [SerializeField] private CanvasGroup 页面组;
        [SerializeField] private RectTransform 徽标;
        [SerializeField] private RectTransform 底部行;
        [Tooltip("Lit 层裁剪节点：rect 宽度=填充进度×行宽，子层恒全宽被裁（扫过边缘=像素级硬边）")]
        [SerializeField] private RectMask2D 填充裁剪;
        [SerializeField] private TextMeshProUGUI 标题文本;
        [SerializeField] private TextMeshProUGUI 正文文本1;
        [SerializeField] private TextMeshProUGUI 正文文本2;
        [SerializeField] private TextMeshProUGUI 版本文本;

        /// <summary>词条池：每组 = 标题/正文1/正文2 三个 UIText 键（第一组沿用历史键 LoadingTip_*）</summary>
        private static readonly string[][] 词条池 =
        {
            new[] { "LoadingTip_Title",    "LoadingTip_Line1",    "LoadingTip_Line2"    },
            new[] { "LoadingTip_02_Title",  "LoadingTip_02_Line1", "LoadingTip_02_Line2" },
            new[] { "LoadingTip_03_Title",  "LoadingTip_03_Line1", "LoadingTip_03_Line2" },
            new[] { "LoadingTip_04_Title",  "LoadingTip_04_Line1", "LoadingTip_04_Line2" },
            new[] { "LoadingTip_05_Title",  "LoadingTip_05_Line1", "LoadingTip_05_Line2" },
        };

        private float _fillElapsed; // 填充已计时长（帧时长钳制累计：单帧尖峰不吞进度）
        private Coroutine _fadeRoutine;
        private int _currentTip = -1;

        /// <summary>填充流程总时长（= 保底展示时长）</summary>
        private float FillDuration => Mathf.Max(0.01f, 填充时长);

        /// <summary>填充流程是否走完（Reveal 揭幕等待条件）</summary>
        private bool IsFillComplete => _fillElapsed >= FillDuration;

        /// <summary>整页透明度（门面冒烟断言用）</summary>
        public float 当前透明度 => 页面组 != null ? 页面组.alpha : 0f;

        private void Awake()
        {
            if (页面组 == null) 页面组 = GetComponent<CanvasGroup>();
            if (页面组 == null) 页面组 = gameObject.AddComponent<CanvasGroup>();
            页面组.alpha = 0f;

            // 词条文案：TextCombiner 接 UIText 三键（语言切换自动刷新）；开局随机一条
            ApplyTip(RandomTipIndex());

            // 版本号非本地化文案（运行时数据），直赋
            if (版本文本 != null)
                版本文本.text = $"v{Application.version}";
        }

        private TextCombiner EnsureTC(TextMeshProUGUI tmp)
        {
            if (tmp == null) return null;
            var tc = tmp.GetComponent<TextCombiner>();
            if (tc == null) tc = tmp.gameObject.AddComponent<TextCombiner>();
            return tc;
        }

        // ── 势力徽标（按所选地图换标） ──

        /// <summary>按所选地图势力换中央徽标（ElementFactionConfig 势力图标）</summary>
        public void SetFaction(FactionType faction)
        {
            if (徽标 == null) return;
            var img = 徽标.GetComponent<Image>();
            if (img == null) return;
            var config = ElementFactionConfig.Instance;
            var icon = config != null ? config.GetFactionIcon(faction) : null;
            if (icon != null) img.sprite = icon;
        }

        // ── 词条文案（多条随机 + 点击换条） ──

        private void ApplyTip(int index)
        {
            _currentTip = index;
            var keys = 词条池[index];
            EnsureTC(标题文本)?.SetSingleEntry(new LocalizedString("UIText", keys[0]));
            EnsureTC(正文文本1)?.SetSingleEntry(new LocalizedString("UIText", keys[1]));
            EnsureTC(正文文本2)?.SetSingleEntry(new LocalizedString("UIText", keys[2]));
        }

        /// <summary>随机词条下标；多条时保证与当前条不同（点击换条不撞同条）</summary>
        private int RandomTipIndex()
        {
            if (词条池.Length <= 1) return 0;
            int next;
            do { next = Random.Range(0, 词条池.Length); } while (next == _currentTip);
            return next;
        }

        /// <summary>点击加载页任意处 → 随机换一条词条</summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            ApplyTip(RandomTipIndex());
        }

        private void Update()
        {
            if (页面组.alpha <= 0f) return; // 整页隐藏时不空转

            // 徽标缓转（unscaled：转场期间 timeScale 不可控）
            if (徽标 != null)
                徽标.Rotate(0f, 0f, -徽标旋转速度 * Time.unscaledDeltaTime);

            // 底部元素行逐像素填充：帧时长钳制 0.05s——场景加载的单帧尖峰不吞填充进度，
            // 恢复渲染后用户仍能看到完整扫过过程；填满后保持全亮
            if (_fillElapsed < FillDuration)
                _fillElapsed += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            ApplyFillProgress(Mathf.Clamp01(_fillElapsed / FillDuration));
        }

        /// <summary>
        /// 把填充进度应用到 Lit 层裁剪矩形：裁剪节点宽度 = progress×行宽（锚在行左缘），
        /// 子层恒全宽（随视口自适应）——超出裁剪区的部分被 RectMask2D 裁掉，边缘=像素级硬边。
        /// </summary>
        private void ApplyFillProgress(float progress)
        {
            if (底部行 == null || 填充裁剪 == null) return;
            float barWidth = 底部行.rect.width;
            var clipRect = (RectTransform)填充裁剪.transform;
            float targetWidth = progress * barWidth;
            if (!Mathf.Approximately(clipRect.sizeDelta.x, targetWidth))
                clipRect.sizeDelta = new Vector2(targetWidth, clipRect.sizeDelta.y);
            if (clipRect.childCount > 0)
            {
                // Lit 层恒全宽：视口宽度变化时自适应（裁剪区外的部分被裁，扫过处即点亮）
                var litLayer = (RectTransform)clipRect.GetChild(0);
                if (!Mathf.Approximately(litLayer.sizeDelta.x, barWidth))
                    litLayer.sizeDelta = new Vector2(barWidth, litLayer.sizeDelta.y);
            }
        }

        /// <summary>加载页淡入（盖住画面）；填充流程从零开始，并随机换一条词条</summary>
        public void Cover(float duration)
        {
            gameObject.SetActive(true);
            _fillElapsed = 0f;
            ApplyFillProgress(0f); // 清上一轮残留
            ApplyTip(RandomTipIndex());
            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(FadeRoutine(页面组.alpha, 1f, duration, keepActive: true));
        }

        /// <summary>
        /// 加载页淡出（新场景就绪揭幕）；保底：填充流程走完才开始淡出——
        /// 加载再快也保证用户看到完整逐像素扫描；淡出完整页隐藏。
        /// </summary>
        public void Reveal(float duration)
        {
            if (页面组.alpha <= 0f) return;
            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(RevealRoutine(duration));
        }

        private IEnumerator RevealRoutine(float duration)
        {
            // 等填充走完（Update 每帧钳制推进，尖峰冻结不跳变）
            while (!IsFillComplete) yield return null;
            yield return FadeRoutine(页面组.alpha, 0f, duration, keepActive: false);
        }

        private IEnumerator FadeRoutine(float from, float to, float duration, bool keepActive)
        {
            if (duration <= 0f) duration = 0.01f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                页面组.alpha = Mathf.Lerp(from, to, t);
                yield return null;
            }
            页面组.alpha = to;
            _fadeRoutine = null;
            if (!keepActive && Mathf.Approximately(to, 0f))
                gameObject.SetActive(false); // 淡出完毕隐藏（下轮 Cover 复用）
        }
    }
}
