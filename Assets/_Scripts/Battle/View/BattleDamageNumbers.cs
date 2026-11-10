using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GIC.Framework;
using GIC.Data;

namespace GIC.Battle
{
    /// <summary>
    /// 原神式伤害数字层（2026-09-24 拍板「按照原神的做法」）：独立于 3D 场景的 Screen Space - Overlay 画布
    /// （sortingOrder=39，战斗 HUD=40 之下），数字完全不参与深度测试——任何角度看都正面、永不被场景物体遮挡；
    /// 只把受击点世界坐标投影为屏幕锚点，上浮/缩放/渐隐全在屏幕像素系完成。
    /// 曲线语义（原神式，2026-09-24 用户确认）：首帧即爆大尺寸（=停留尺寸 × 初始停留比），收缩时长内 easeOut
    /// 收回到停留尺寸；初值与终值同乘 f(|量值|)=Clamp(尺寸基准+尺寸对数系数×log10(max(1,|量值|)), 下限, 上限)
    /// ——伤害越高初始大小和最后大小都越大（对数防大数字占屏）；段末随渐隐至透明回收。
    /// 停留时长映射（2026-09-26 拍板「数值越大，停留时间越长」）：total=Clamp(时长基准+时长对数系数×
    /// log10(max(1,|量值|)), 时长下限, 时长上限)——与尺寸映射同构（对数），小伤害≈原 0.95s、大伤害渐长封顶。
    /// 屏幕边缘夹取（2026-09-26 拍板「玩家屏幕里没有该伤害数字时，数字完整显示在屏幕边缘」）：
    /// 投影点按文本实测半尺寸夹到视口内——屏外/贴边目标受伤在最近边缘完整可见=战斗方向提醒。
    /// 回血类（吸血/治疗，Spawn isHeal=true）曲线例外（2026-10-02 拍板「不应和伤害一样从大变小，直接以
    /// 最终大小出现+上浮更多+比伤害更小」）：不爆裂——全程恒定停留尺寸；尺寸=同映射×回血尺寸倍率（更小）；
    /// 上浮取回血上浮像素（比伤害更高）。
    /// 2026-11-10 拍板三件：①全部弹出数字整体尺寸 ×0.5（整体尺寸倍率）；②同一目标同类别同色同反应的高频
    /// 连击（如 2 秒 25 段）在聚合窗口内并入同一个数字、数值持续上涨并每次并入重播一次小回弹（入场动画），
    /// 不再刷屏；③新数字在屏幕空间避开已有数字的包围盒（垂直优先、其次对角），避免多个数字堆在一起看不清。
    /// 池化复用；本图层不挂 GraphicRaycaster（Overlay 画布无射线器即不吃射线，勿补——会挡 HUD 按钮）。
    /// Update 驱动（聚合需要延长存活/重播回弹，协程固定时长表达不了——2026-11-10 从协程改为逐帧状态机）。
    /// </summary>
    public class BattleDamageNumbers : MonoBehaviour
    {
        [Header("曲线参数（原神式：首帧爆裂→easeOut 收缩→停留→段尾渐隐）")]
        [Tooltip("首帧爆裂尺寸对停留尺寸的倍数（2.5=首帧大 150%；2026-09-24 目检后用户拍板调大）")]
        [SerializeField] private float 初始停留比 = 2.5f;

        [Tooltip("从爆裂尺寸 easeOut 收缩回停留尺寸的时长（秒，现默认值）")]
        [SerializeField] private float 收缩时长 = 0.35f;

        [Tooltip("淡出开始的总进程比例（此比例前 alpha 恒 1，此后线性收至 0）")]
        [SerializeField, Range(0f, 0.95f)] private float 淡出起点比例 = 0.62f;

        [Tooltip("上浮位移（参考分辨率 2560×1440 下的像素值，随 CanvasScaler 随分辨率等比缩放）")]
        [SerializeField] private float 上浮像素 = 60f;

        [Tooltip("回血类（吸血/治疗）上浮位移（同上浮像素口径）——比伤害上浮更高，2026-10-02 拍板「上浮更多」")]
        [SerializeField] private float 回血上浮像素 = 100f;

        [Tooltip("回血类（吸血/治疗）停留尺寸对伤害的倍率（尺寸映射结果整体再乘本系数）——比伤害数字更小，2026-10-02 拍板")]
        [SerializeField] private float 回血尺寸倍率 = 0.75f;

        [Tooltip("暴击数字停留尺寸倍率（尺寸映射结果整体再乘本系数；爆裂初值随停留尺寸同乘）——GI 式暴击=更大同色+后缀!，2026-10-02 幸运暴击批；同日拍板 1.4→1.2")]
        [SerializeField] private float 暴击尺寸倍率 = 1.2f;

        [Tooltip("屏幕边缘夹取留白（屏幕像素）——屏幕外/贴边伤害数字夹到视口内完整显示=战斗方向提醒，2026-09-26 拍板")]
        [SerializeField] private float 屏幕边缘留白 = 12f;

        [Header("整体尺寸（2026-11-10 拍板：伤害/治疗等一切弹出数字整体缩小 50%）")]
        [Tooltip("尺寸映射结果的整体倍率（1=原尺寸，0.5=缩小一半）——回血/暴击倍率在其后再乘")]
        [SerializeField] private float 整体尺寸倍率 = 0.5f;

        [Header("高频连击聚合（同目标同类别同色同反应在窗口内合并为一个数字持续上涨）")]
        [Tooltip("聚合窗口（秒）：与上一次并入的间隔小于本值即并入同一数字（2 秒 25 段连击=0.08s 间隔全并）")]
        [SerializeField] private float 聚合窗口 = 0.6f;

        [Tooltip("单条聚合数字最长总寿命（秒）：超过后新伤害另起一个数字，避免一条数字永不消失")]
        [SerializeField] private float 聚合最长总时长 = 3.2f;

        [Tooltip("每次并入时的小回弹峰值倍率（叠加在停留尺寸上的入场动画）")]
        [SerializeField] private float 聚合脉冲倍率 = 1.28f;

        [Tooltip("小回弹时长（秒）")]
        [SerializeField] private float 聚合脉冲时长 = 0.14f;

        [Header("数字避让（屏幕空间：新数字避开已有数字包围盒，避免堆积看不清）")]
        [Tooltip("数字之间的最小屏幕间距（像素）")]
        [SerializeField] private float 避让间距 = 12f;

        [Tooltip("垂直候选位尝试次数（向上/向下交替）——垂直优先，因为伤害数字天然向上堆叠")]
        [SerializeField] private int 避让垂直尝试 = 4;

        [Tooltip("垂直步长对「数字高+间距」的倍率（1=紧邻）")]
        [SerializeField] private float 避让垂直步长 = 1.0f;

        [Header("伤害→停留时长映射（2026-09-26 拍板「数值越大，停留时间越长」——与尺寸映射同构，替换旧固定总时长）")]
        [Tooltip("停留时长基准（秒）：total=基准+系数×log10(max(1,|量值|))——小伤害不短于原 0.95s 观感")]
        [SerializeField] private float 时长基准 = 0.95f;

        [Tooltip("时长对数系数（伤害每×10，多停留本秒数）")]
        [SerializeField] private float 时长对数系数 = 0.25f;

        [Tooltip("时长下限（秒）")]
        [SerializeField] private float 时长下限 = 0.95f;

        [Tooltip("时长上限（秒，防大数字长时间占屏）")]
        [SerializeField] private float 时长上限 = 2.2f;

        [Header("伤害→尺寸映射（初值与终值同乘该因子）")]
        [Tooltip("基础字号（UGUI TMP 固定值；缩放由 RectTransform 承载，字体本身不变）")]
        [SerializeField] private int 基础字号 = 55;

        [Tooltip("尺寸映射基准：f=基准+对数系数×log10(max(1,|量值|))")]
        [SerializeField] private float 尺寸基准 = 0.8f;

        [Tooltip("对数敏感系数（伤害每×10，尺寸+本系数）")]
        [SerializeField] private float 尺寸对数系数 = 0.22f;

        [Tooltip("尺寸映射下限（保底清晰度）")]
        [SerializeField] private float 尺寸下限 = 0.8f;

        [Tooltip("尺寸映射上限（防大数字占屏）")]
        [SerializeField] private float 尺寸上限 = 1.65f;

        [Header("描边（2026-09-26 返修：UGUI Outline 固定像素式缩放视口下不可见——改 SDF 原生，docs/14 §84）")]
        [Tooltip("SDF 原生描边宽度（0~1 相对字形，随视口缩放/数字缩放恒定可见；与倒计时同口径，迭代链 0.22→0.15→0.08（2026-09-26 拍板「从 0.15 改为 0.08」））")]
        [SerializeField] private float 描边宽度 = 0.08f;

        private Camera _camera;
        private Canvas _canvas;
        private static BattlePalette Palette => BattlePalette.Instance;

        private sealed class Entry
        {
            public GameObject Go;
            public RectTransform Rect;
            public TextMeshProUGUI Text;
            public CanvasGroup Group;
            public Material OutlineMat; // SDF 描边材质实例（池条目持有；OnDestroy 统一释放）

            public bool Active;
            public string GroupKey;     // 聚合键（null=不聚合）
            public string FixedText;    // 不聚合时的固定文案（如「格挡」）
            public string Prefix;       // 聚合重建文本：前缀（反应名/吸血名）
            public string Suffix;       // 聚合重建文本：后缀（暴击「!」）
            public bool PlusSign;       // 聚合重建文本：治疗/资源类带 +
            public bool IsHeal;
            public bool IsCrit;
            public Vector3 Anchor;      // 世界锚点（固定，不随单位移动）
            public Vector2 ScreenOffset;// 避让偏移（屏幕像素）
            public Vector2 PosPx;       // 当前屏幕位置（避让判定用）
            public Vector2 HalfPx;      // 当前屏幕半尺寸（含留白）
            public Vector2 HalfRef;     // 文本本地半尺寸（不含缩放）
            public float Magnitude;     // 累计量值（驱动尺寸/时长映射）
            public float SettleScale;
            public float PopScale;
            public float CurrentScale;
            public float Speed;
            public float Age;           // 首次出现至今（上浮进度/聚合寿命）
            public float SinceAccum;    // 距上次出现/并入
            public float PulseLeft;     // 聚合回弹剩余时长
        }

        private readonly Queue<Entry> _pool = new Queue<Entry>();
        /// <summary>全部池条目（含已回收的——释放时统一销材质）</summary>
        private readonly List<Entry> _allEntries = new List<Entry>();
        /// <summary>在场（活跃）数字</summary>
        private readonly List<Entry> _active = new List<Entry>();
        /// <summary>聚合键 → 在场数字</summary>
        private readonly Dictionary<string, Entry> _byKey = new Dictionary<string, Entry>();

        /// <summary>初始化（确保画布+相机；幂等——重复调用不重建）</summary>
        public void Init(Camera cam)
        {
            _camera = cam;
            if (_canvas != null) return;

            var canvasGo = new GameObject("DamageNumberCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = BattleMetrics.DamageNumbersCanvasOrder; // 战斗 HUD=40 之下——数字是战场反馈非面板，面板应盖过它
            // 勿加 GraphicRaycaster：Overlay 画布无射线器即不吃射线（加了会挡 HUD 按钮/棋盘）
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560f, 1440f);   // 与 BattleHud.prefab 一致，观感同源
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        /// <summary>弹一个数字。worldAnchor=受击点世界坐标（投影为屏幕锚点）；magnitude=量值绝对值（驱动尺寸映射）；
        /// speed=播放速率倍率（与 BattlePlayer._playbackSpeed 同语义）；isHeal=回血类（吸血/治疗）——不爆裂、
        /// 上浮更高、尺寸更小（2026-10-02 拍板）；isCrit=暴击（2026-10-02 幸运暴击批）——停留尺寸×暴击尺寸倍率；
        /// groupKey=聚合键（同一目标的同类高频连击传相同键；null=不聚合，如「格挡」固定文案）——键内应含目标与
        /// 类别/反应/暴击等语义，颜色由本方法自动附加（异色不合并）；prefix/suffix/plusSign=聚合时重建文本用
        /// （反应名前缀、暴击后缀、治疗 + 号）。</summary>
        public void Spawn(Vector3 worldAnchor, string text, Color color, float magnitude, float speed,
            bool isHeal = false, bool isCrit = false, string groupKey = null,
            string prefix = null, string suffix = null, bool plusSign = false)
        {
            if (_canvas == null || string.IsNullOrEmpty(text)) return;

            speed = Mathf.Max(0.1f, speed);
            magnitude = Mathf.Max(0f, magnitude);

            string key = null;
            if (!string.IsNullOrEmpty(groupKey))
            {
                var c = (Color32)color;
                uint packed = ((uint)c.r << 24) | ((uint)c.g << 16) | ((uint)c.b << 8) | c.a;
                key = groupKey + "|" + packed;
            }

            // 高频连击并入（2026-11-10 拍板）：窗口内 + 未超单条最长寿命——数值上涨、重播小回弹
            if (key != null && _byKey.TryGetValue(key, out var exist) && exist != null && exist.Active
                && exist.SinceAccum <= 聚合窗口 && exist.Age <= 聚合最长总时长)
            {
                Accumulate(exist, magnitude, prefix, suffix, plusSign);
                return;
            }

            var entry = _pool.Count > 0 ? _pool.Dequeue() : CreateEntry();
            if (key != null) _byKey[key] = entry;

            entry.Active = true;
            entry.GroupKey = key;
            entry.FixedText = key == null ? text : null;
            entry.Prefix = prefix;
            entry.Suffix = suffix;
            entry.PlusSign = plusSign;
            entry.IsHeal = isHeal;
            entry.IsCrit = isCrit;
            entry.Anchor = worldAnchor;
            entry.Speed = speed;
            entry.Magnitude = magnitude;
            entry.Age = 0f;
            entry.SinceAccum = 0f;
            entry.PulseLeft = 0f;
            entry.ScreenOffset = Vector2.zero;
            entry.SettleScale = ComputeSettleScale(magnitude, isHeal, isCrit);
            entry.PopScale = isHeal ? entry.SettleScale : entry.SettleScale * 初始停留比;
            entry.CurrentScale = entry.PopScale;

            entry.Text.text = text;
            entry.Text.color = color;
            entry.Group.alpha = 1f;
            entry.Go.SetActive(true);
            entry.Text.ForceMeshUpdate(); // 避让/尺寸要用本文本的实测包围盒（文本在生命周期内可能再变，见 Accumulate）
            CacheBounds(entry);

            entry.Rect.localScale = Vector3.one * entry.CurrentScale;
            var cam = _camera != null ? _camera : CameraContext.Resolve();
            if (cam != null)
            {
                entry.PosPx = ScreenPointOf(cam, entry.Anchor, 0f);
                float sf = _canvas != null ? _canvas.scaleFactor : 1f;
                // 避让占位按「停留尺寸」而非首帧爆裂尺寸：爆裂只持续 0.35s，按它排位会把数字推得过散
                entry.HalfPx = new Vector2(
                    entry.HalfRef.x * entry.SettleScale * sf + 屏幕边缘留白,
                    entry.HalfRef.y * entry.SettleScale * sf + 屏幕边缘留白);
                entry.ScreenOffset = ChooseOffset(entry.PosPx, entry.HalfPx);
                var clamped = ClampToViewport(entry.PosPx + entry.ScreenOffset, entry.HalfPx);
                entry.PosPx = clamped;
                entry.Rect.position = new Vector3(clamped.x, clamped.y, 0f);
            }
            _active.Add(entry);
        }

        /// <summary>并入一次高频伤害/治疗：累计数值、按新量值重算停留尺寸、重播小回弹（入场动画）。
        /// 位置不变（避免数字在连击途中乱跳）；文本即时上涨。</summary>
        private void Accumulate(Entry e, float magnitude, string prefix, string suffix, bool plusSign)
        {
            if (prefix != null) e.Prefix = prefix;
            if (suffix != null) e.Suffix = suffix;
            if (plusSign) e.PlusSign = true;

            e.Magnitude += magnitude;

            float shown = e.CurrentScale;
            e.SettleScale = ComputeSettleScale(e.Magnitude, e.IsHeal, e.IsCrit);
            e.PopScale = Mathf.Max(shown, e.SettleScale); // 从当前可见尺寸连续收缩到新停留尺寸（不突跳）
            e.SinceAccum = 0f;
            e.PulseLeft = Mathf.Max(0f, 聚合脉冲时长);

            e.Text.text = BuildText(e, e.Magnitude);
            e.Group.alpha = 1f;
            e.Text.ForceMeshUpdate();
            CacheBounds(e);
        }

        /// <summary>逐帧状态机（聚合需要延长存活/重播回弹，协程固定时长表达不了）</summary>
        private void Update()
        {
            if (_active.Count == 0) return;

            var cam = _camera != null ? _camera : CameraContext.Resolve();
            float dt = Time.deltaTime;
            float sf = _canvas != null ? _canvas.scaleFactor : 1f;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var e = _active[i];
                if (e == null || !e.Active) { _active.RemoveAt(i); continue; }

                e.Age += dt;
                e.SinceAccum += dt;
                if (e.PulseLeft > 0f) e.PulseLeft = Mathf.Max(0f, e.PulseLeft - dt);

                float total = TotalSecondsOf(e.Magnitude) / e.Speed;
                if (e.SinceAccum >= total) { Recycle(e); _active.RemoveAt(i); continue; }

                // 缩放：伤害=首帧爆裂 easeOut 收缩回停留尺寸；回血类不爆裂（恒定停留尺寸）；
                // 聚合回弹=叠加的正弦小脉冲（每次并入都是一次「入场」）
                float shrink = 收缩时长 / e.Speed;
                float sT = shrink > 0f ? Mathf.Clamp01(e.SinceAccum / shrink) : 1f;
                float eOut = 1f - (1f - sT) * (1f - sT) * (1f - sT);
                float scale = e.IsHeal ? e.SettleScale : Mathf.Lerp(e.PopScale, e.SettleScale, eOut);
                if (e.PulseLeft > 0f && 聚合脉冲时长 > 0f)
                    scale *= 1f + (聚合脉冲倍率 - 1f) * Mathf.Sin(Mathf.PI * (1f - e.PulseLeft / 聚合脉冲时长));
                e.CurrentScale = scale;
                e.Rect.localScale = Vector3.one * scale;

                // 位置：锚点逐帧投影 + 先快后慢上浮（上浮进度用「首次出现至今」——聚合不能让它回落）
                // + 避让偏移；再按文本实测半尺寸夹到视口内
                if (cam != null)
                {
                    float riseDur = Mathf.Max(0.05f, 时长基准 / e.Speed);
                    float riseT = Mathf.Clamp01(e.Age / riseDur);
                    float rise = (e.IsHeal ? 回血上浮像素 : 上浮像素) * (1f - (1f - riseT) * (1f - riseT));
                    Vector2 p = ScreenPointOf(cam, e.Anchor, rise) + e.ScreenOffset;
                    var half = new Vector2(
                        e.HalfRef.x * scale * sf + 屏幕边缘留白,
                        e.HalfRef.y * scale * sf + 屏幕边缘留白);
                    e.HalfPx = half;
                    var clamped = ClampToViewport(p, half);
                    e.PosPx = clamped;
                    e.Rect.position = new Vector3(clamped.x, clamped.y, 0f);
                }

                // 渐隐：淡出起点前恒不透明，段尾线性收至 0
                float fadeStart = total * 淡出起点比例;
                e.Group.alpha = e.SinceAccum <= fadeStart
                    ? 1f
                    : 1f - (e.SinceAccum - fadeStart) / Mathf.Max(0.0001f, total - fadeStart);
            }
        }

        private void Recycle(Entry e)
        {
            e.Active = false;
            e.Go.SetActive(false);
            if (e.GroupKey != null && _byKey.TryGetValue(e.GroupKey, out var cur) && cur == e)
                _byKey.Remove(e.GroupKey);
            e.GroupKey = null;
            _pool.Enqueue(e);
        }

        /// <summary>文本重建（聚合数值上涨用）：固定文案原样返回；否则 前缀+加号+累计值+后缀</summary>
        private static string BuildText(Entry e, float value)
        {
            if (e.FixedText != null) return e.FixedText;
            string v = Mathf.RoundToInt(value).ToString();
            return e.Prefix + (e.PlusSign ? "+" : string.Empty) + v + e.Suffix;
        }

        /// <summary>尺寸映射（整体尺寸倍率在最后乘——2026-11-10 拍板整体缩小 50%）</summary>
        private float ComputeSettleScale(float magnitude, bool isHeal, bool isCrit)
        {
            float s = Mathf.Clamp(
                尺寸基准 + 尺寸对数系数 * Mathf.Log10(Mathf.Max(1f, magnitude)), 尺寸下限, 尺寸上限);
            if (isHeal) s *= 回血尺寸倍率;
            if (isCrit) s *= 暴击尺寸倍率;
            return s * 整体尺寸倍率;
        }

        private float TotalSecondsOf(float magnitude) => Mathf.Clamp(
            时长基准 + 时长对数系数 * Mathf.Log10(Mathf.Max(1f, magnitude)), 时长下限, 时长上限);

        private void CacheBounds(Entry e)
        {
            var b = e.Text.textBounds;
            var half = new Vector2(Mathf.Abs(b.extents.x), Mathf.Abs(b.extents.y));
            // 兜底：TMP 尚未完成网格重建时 textBounds 可能为 0——按字号估算，避免避让整体失效
            if (half.x <= 0.01f || half.y <= 0.01f)
                half = new Vector2(基础字号 * 0.32f * Mathf.Max(1, e.Text.text.Length), 基础字号 * 0.6f);
            e.HalfRef = half;
        }

        /// <summary>世界点→屏幕像素（相机背面投影是镜像的：先绕屏幕中心翻回正确方位，否则避让/夹取会指反）</summary>
        private static Vector2 ScreenPointOf(Camera cam, Vector3 world, float rise)
        {
            var sp = cam.WorldToScreenPoint(world);
            Vector2 p = sp + Vector3.up * rise;
            if (sp.z < 0f) { p.x = Screen.width - p.x; p.y = Screen.height - p.y; }
            return p;
        }

        private static Vector2 ClampToViewport(Vector2 p, Vector2 half)
        {
            float viewW = Screen.width, viewH = Screen.height;
            return new Vector2(
                Mathf.Clamp(p.x, half.x, Mathf.Max(half.x, viewW - half.x)),
                Mathf.Clamp(p.y, half.y, Mathf.Max(half.y, viewH - half.y)));
        }

        /// <summary>避让（2026-11-10 拍板）：原位被占则向上/向下交替挪一个数字高，再试对角；都占则回原位。
        /// 只做一次性分配、之后不重排——数字中途挪位比轻微重叠更难受。</summary>
        private Vector2 ChooseOffset(Vector2 baseP, Vector2 half)
        {
            var origin = ClampToViewport(baseP, half);
            if (!OverlapsAny(origin, half)) return Vector2.zero;

            float stepY = Mathf.Max(4f, (half.y * 2f + 避让间距) * Mathf.Max(1f, 避让垂直步长));
            float stepX = half.x * 2f + 避让间距;
            int tries = Mathf.Max(1, 避让垂直尝试);

            for (int i = 1; i <= tries; i++)
            {
                var up = ClampToViewport(baseP + new Vector2(0f, stepY * i), half);
                if (!OverlapsAny(up, half)) return up - baseP;
                var down = ClampToViewport(baseP - new Vector2(0f, stepY * i), half);
                if (!OverlapsAny(down, half)) return down - baseP;
            }
            for (int i = 1; i <= tries; i++)
            {
                var dy = stepY * i;
                var upRight = ClampToViewport(baseP + new Vector2(stepX, dy), half);
                if (!OverlapsAny(upRight, half)) return upRight - baseP;
                var upLeft = ClampToViewport(baseP + new Vector2(-stepX, dy), half);
                if (!OverlapsAny(upLeft, half)) return upLeft - baseP;
            }
            return Vector2.zero;
        }

        private bool OverlapsAny(Vector2 pos, Vector2 half)
        {
            float pad = Mathf.Max(0f, 避让间距);
            for (int i = 0; i < _active.Count; i++)
            {
                var e = _active[i];
                if (e == null || !e.Active) continue;
                if (Mathf.Abs(pos.x - e.PosPx.x) < half.x + e.HalfPx.x + pad &&
                    Mathf.Abs(pos.y - e.PosPx.y) < half.y + e.HalfPx.y + pad) return true;
            }
            return false;
        }

        /// <summary>NEW 池条目：RectTransform + TextMeshProUGUI + CanvasGroup + 描边（配色一律 BattlePalette，勿写字面量）</summary>
        private Entry CreateEntry()
        {
            var go = new GameObject("DamageNumber", typeof(RectTransform));
            go.transform.SetParent(_canvas.transform, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(600f, 90f);

            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = BattleViewFactory.WorldTextFont;
            text.fontSize = 基础字号;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow; // 长前缀（"蒸发 99999"）不裁切
            text.raycastTarget = false;

            // SDF 原生描边（2026-09-26 返修：旧 UGUI Outline 固定像素 2.2px 在缩放视口下 ~1 屏幕像素
            // 不可见；SDF 宽度相对字形、随数字缩放（RectTransform localScale）恒定等比，docs/14 §84）。
            // 独立材质实例勿改共享字体材质；条目池化常驻 → OnDestroy 统一释放
            Material outlineMat = null;
            var sharedMat = text.fontSharedMaterial;
            if (sharedMat != null && sharedMat.HasProperty("_OutlineWidth"))
            {
                outlineMat = new Material(sharedMat);
                outlineMat.SetColor("_OutlineColor",
                    Palette != null ? Palette.伤害数字描边色 : new Color(0.04f, 0.03f, 0.03f, 0.85f));
                outlineMat.SetFloat("_OutlineWidth", 描边宽度);
                text.fontMaterial = outlineMat;
            }

            var group = go.AddComponent<CanvasGroup>();
            var entry = new Entry { Go = go, Rect = rect, Text = text, Group = group, OutlineMat = outlineMat };
            _allEntries.Add(entry);
            return entry;
        }

        /// <summary>层销毁：池条目 SDF 描边材质实例统一释放（TMP 不自销；不释放则跨战斗累积，
        /// docs/14 §63 生命周期纪律）</summary>
        private void OnDestroy()
        {
            foreach (var entry in _allEntries)
                if (entry != null && entry.OutlineMat != null)
                    Destroy(entry.OutlineMat);
            _allEntries.Clear();
            _pool.Clear();
            _active.Clear();
            _byKey.Clear();
        }
    }
}
