// ==================== LoadingOverlayDriver.cs（原神式加载页驱动——开局转场加载界面） ====================
// 布局复刻原神加载页：纯白底 + 中央势力徽标缓转（按所选地图势力换标）+ 灰蓝词条文案
// （多条随机，点击页面任意处换一条）+ 底部元素行**逐像素填充扫描**（Dim/Lit 双层 + RectMask2D
// 裁剪矩形从左往右展宽=进度条式点亮，扫过处线与图标整列变深，与原神一致）+ 右下版本号。
// 进度旋律（2026-09-13）：填充扫描每越过一个触发点播一个单音符，加载顺畅时整句连听
// 可辨势力（蒙德=《蒙德城繁忙的午后》开头 riff）；乐器编制跟随原曲配器（2026-09-13
// 拍板：不限于单乐器，按所选旋律的配器分层——现=长笛主旋律+吉他弹拨伴奏）；势力未配旋律则静默。
// 填充三段制（2026-09-13 拍板"丝滑优先"）：正常段 0→卡点(85%) 承载旋律主体 → 加载未完卡点停住
// （进度条停在 85% 明示"还在加载"）→ Reveal=真实加载完成 → 扫尾到 100%（riff 尾音 C6
// 阈值落在扫尾段=加载完成音）→ 揭幕。保底≈旋律时长，取旋律时选好 ≤3s。
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
    /// <summary>进度旋律单音符：填充进度到达触发点即播；多乐器分层=同格点多条目（各带音量倍率）</summary>
    [System.Serializable]
    public class LoadingMelodyNote
    {
        [Tooltip("单音符音源（Philharmonia 真实乐器采样；乐器编制跟随原曲配器可多乐器分层；合成音色过不了辨识关，勿回退纯合成）")]
        public AudioClip 音符;
        [Tooltip("填充进度达到此值即触发（0~1；按乐句节奏归一化铺开，非等距；伴奏层与主旋律同格点=同帧和鸣）")]
        [Range(0f, 1f)] public float 触发进度;
        [Tooltip("音量倍率（相对旋律音量）：主旋律=1，伴奏层压低（如 0.5）分层")]
        [Range(0f, 2f)] public float 音量倍率 = 1f;
    }

    /// <summary>一个势力的加载进度旋律（识别乐句按进度铺开；未配置的势力=静默）</summary>
    [System.Serializable]
    public class LoadingMelodySet
    {
        public FactionType 势力;
        public LoadingMelodyNote[] 音符序列;
    }


    public class LoadingOverlayDriver : MonoBehaviour, IPointerClickHandler
    {
        [Header("动效参数")]
        [Tooltip("徽标每秒旋转角速度（度）")]
        [SerializeField] private float 徽标旋转速度 = 40f;
        [Tooltip("正常填充段时长（秒）：进度 0→卡点，承载整段旋律；≈旋律时长，取旋律时选好 ≤3s（2026-09-13 拍板保底不过 3s）。进度旋律随此节奏联动：值越小旋律越快")]
        [SerializeField] private float 填充时长 = 1.7f;

        [Header("真实加载联动（85% 卡点规则）")]
        [Tooltip("正常填充的卡点进度：到达时若真实加载（Reveal）未完成则停在此处等，加载完成才扫尾揭幕——进度条不空转满格")]
        [SerializeField, Range(0.5f, 1f)] private float 卡点进度 = 0.85f;
        [Tooltip("真实加载完成后卡点→100% 的扫尾时长（秒）：快速收尾给完成感；旋律尾音阈值可落在此段=加载完成音")]
        [SerializeField] private float 扫尾时长 = 0.3f;

        [Header("进度旋律（按势力，填充扫描逐音符点亮）")]
        [Tooltip("各势力加载旋律：填充进度每越过一个触发点播对应音符；加载顺畅整句连听可辨势力")]
        [SerializeField] private LoadingMelodySet[] 进度旋律表;
        [Tooltip("进度音符播放音量（相对 SFX 通道）")]
        [SerializeField, Range(0f, 1f)] private float 旋律音量 = 0.8f;

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

        private float _fillElapsed; // 正常填充段已计时长（帧时长钳制累计：单帧尖峰不吞进度）
        private Coroutine _fadeRoutine;
        private int _currentTip = -1;
        private LoadingMelodyNote[] _melodyNotes; // 当前势力旋律（SetFaction 选定；null=该势力未配=静默）
        private int _melodyIndex = -1;            // 已触发到的音符下标（-1=未开始；Cover 复位）
        private bool _loadDone;                   // 真实加载完成（SceneFadeOverlay.Reveal 到达时置位；Cover 复位）
        private float _sweepElapsed;               // 卡点→100% 扫尾已计时长（帧时长钳制累计）

        /// <summary>正常填充段总时长（0→卡点，承载旋律）</summary>
        private float FillDuration => Mathf.Max(0.01f, 填充时长);

        /// <summary>扫尾段总时长</summary>
        private float SweepDuration => Mathf.Max(0.01f, 扫尾时长);

        /// <summary>填充流程是否走完（Reveal 揭幕等待条件）：正常段走完 + 真实加载完成 + 扫尾走完</summary>
        private bool IsFillComplete => _loadDone && _fillElapsed >= FillDuration && _sweepElapsed >= SweepDuration;

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

        /// <summary>按所选地图势力换中央徽标（ElementFactionConfig 势力图标），并选定该势力的进度旋律</summary>
        public void SetFaction(FactionType faction)
        {
            // 选曲与徽标无依赖，先选（徽标缺失也不静默旋律）
            _melodyNotes = null;
            if (进度旋律表 != null)
            {
                foreach (var set in 进度旋律表)
                {
                    if (set != null && set.势力 == faction && set.音符序列 != null && set.音符序列.Length > 0)
                    {
                        _melodyNotes = set.音符序列;
                        break;
                    }
                }
            }

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

            // 底部元素行填充三段制：正常段(0→卡点)→未加载完卡点→加载完成扫尾(卡点→100%)。
            // 各段帧时长钳制 0.05s——场景加载的单帧尖峰不吞进度，恢复渲染后不跳变
            if (_fillElapsed < FillDuration)
                _fillElapsed = Mathf.Min(_fillElapsed + Mathf.Min(Time.unscaledDeltaTime, 0.05f), FillDuration);

            float progress;
            if (_fillElapsed < FillDuration)
            {
                // 正常填充段：0→卡点（真实加载前从不越界，卡点内承载旋律主体）
                progress = (_fillElapsed / FillDuration) * 卡点进度;
            }
            else if (!_loadDone)
            {
                // 卡点：真实加载未完成——进度条停住明示"还在加载"，旋律主体已在此前走完
                progress = 卡点进度;
            }
            else
            {
                // 扫尾段：加载完成（Reveal 已到）→快速收尾到 100%，完成音落在此段
                if (_sweepElapsed < SweepDuration)
                    _sweepElapsed = Mathf.Min(_sweepElapsed + Mathf.Min(Time.unscaledDeltaTime, 0.05f), SweepDuration);
                progress = Mathf.Lerp(卡点进度, 1f, _sweepElapsed / SweepDuration);
            }
            ApplyFillProgress(progress);
            UpdateMelody(progress);
        }

        /// <summary>
        /// 进度旋律：填充进度只前进不回退，把新越过的触发点按表序全部播出——同格点多条目
        /// 同帧和鸣（主旋律+伴奏多乐器分层的载体）；填充为定速扫描（0.05s/帧钳制），
        /// 顺畅时整句按乐句节奏连听。
        /// </summary>
        private void UpdateMelody(float progress)
        {
            if (_melodyNotes == null || _melodyNotes.Length == 0) return;
            while (_melodyIndex + 1 < _melodyNotes.Length && progress >= _melodyNotes[_melodyIndex + 1].触发进度)
            {
                _melodyIndex++;
                var note = _melodyNotes[_melodyIndex];
                if (note != null && note.音符 != null && AudioManager.Instance != null)
                    AudioManager.Instance.PlaySFX(note.音符, 旋律音量 * note.音量倍率);
            }
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

        /// <summary>加载页淡入（盖住画面）；填充流程从零开始，随机换一条词条，旋律从头计数，加载完成标志复位</summary>
        public void Cover(float duration)
        {
            gameObject.SetActive(true);
            _fillElapsed = 0f;
            _sweepElapsed = 0f;
            _loadDone = false;
            _melodyIndex = -1;
            ApplyFillProgress(0f); // 清上一轮残留
            ApplyTip(RandomTipIndex());
            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(FadeRoutine(页面组.alpha, 1f, duration, keepActive: true));
        }

        /// <summary>真实加载完成标记（SceneFadeOverlay.Reveal 调用）：放行 85% 卡点 → 扫尾 → 揭幕</summary>
        public void MarkLoadDone() => _loadDone = true;

        /// <summary>
        /// 加载页淡出（真实加载完成后调用）；保底：三段填充走完（正常段+扫尾）才开始淡出——
        /// 旋律主体保证播完、卡点等到的加载完成后快速收尾；淡出完整页隐藏。
        /// </summary>
        public void Reveal(float duration)
        {
            if (页面组.alpha <= 0f) return;
            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(RevealRoutine(duration));
        }

        private IEnumerator RevealRoutine(float duration)
        {
            // 等三段填充走完（正常段→卡点等待由 MarkLoadDone 放行→扫尾；每帧钳制推进，尖峰冻结不跳变）
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
