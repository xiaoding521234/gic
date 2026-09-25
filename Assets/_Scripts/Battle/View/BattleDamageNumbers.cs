using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
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
    /// 池化复用；本图层不挂 GraphicRaycaster（Overlay 画布无射线器即不吃射线，勿补——会挡 HUD 按钮）。
    /// </summary>
    public class BattleDamageNumbers : MonoBehaviour
    {
        [Header("曲线参数（原神式：首帧爆裂→easeOut 收缩→停留→段尾渐隐）")]
        [Tooltip("首帧爆裂尺寸对停留尺寸的倍数（2.5=首帧大 150%；2026-09-24 目检后用户拍板调大）")]
        [SerializeField] private float 初始停留比 = 2.5f;

        [Tooltip("从爆裂尺寸 easeOut 收缩回停留尺寸的时长（秒，现默认值）")]
        [SerializeField] private float 收缩时长 = 0.35f;

        [Tooltip("数字总存活时长（秒，含收缩+停留+淡出）")]
        [SerializeField] private float 总时长 = 0.95f;

        [Tooltip("淡出开始的总进程比例（此比例前 alpha 恒 1，此后线性收至 0）")]
        [SerializeField, Range(0f, 0.95f)] private float 淡出起点比例 = 0.62f;

        [Tooltip("上浮位移（参考分辨率 2560×1440 下的像素值，随 CanvasScaler 随分辨率等比缩放）")]
        [SerializeField] private float 上浮像素 = 60f;

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
        [Tooltip("SDF 原生描边宽度（0~1 相对字形，随视口缩放/数字缩放恒定可见；与倒计时同口径，迭代链 0.22→0.15）")]
        [SerializeField] private float 描边宽度 = 0.15f;

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
            public Coroutine Co;
        }

        private readonly Queue<Entry> _pool = new Queue<Entry>();
        /// <summary>全部池条目（含已回收的——释放时统一销材质）</summary>
        private readonly List<Entry> _allEntries = new List<Entry>();

        /// <summary>初始化（确保画布+相机；幂等——重复调用不重建）</summary>
        public void Init(Camera cam)
        {
            _camera = cam;
            if (_canvas != null) return;

            var canvasGo = new GameObject("DamageNumberCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 39; // 战斗 HUD=40 之下——数字是战场反馈非面板，面板应盖过它
            // 勿加 GraphicRaycaster：Overlay 画布无射线器即不吃射线（加了会挡 HUD 按钮/棋盘）
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560f, 1440f);   // 与 BattleHud.prefab 一致，观感同源
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        /// <summary>弹一个数字。worldAnchor=受击点世界坐标（投影为屏幕锚点）；magnitude=量值绝对值（驱动尺寸映射）；speed=播放速率倍率（与 BattlePlayer._playbackSpeed 同语义）</summary>
        public void Spawn(Vector3 worldAnchor, string text, Color color, float magnitude, float speed)
        {
            if (_canvas == null || text == null || text.Length == 0) return;

            var entry = _pool.Count > 0 ? _pool.Dequeue() : CreateEntry();
            if (entry.Co != null) StopCoroutine(entry.Co);

            entry.Text.text = text;
            entry.Text.color = color;
            entry.Group.alpha = 1f;
            entry.Go.SetActive(true);

            float settleScale = Mathf.Clamp(
                尺寸基准 + 尺寸对数系数 * Mathf.Log10(Mathf.Max(1f, magnitude)), 尺寸下限, 尺寸上限);
            entry.Co = StartCoroutine(Play(entry, worldAnchor, settleScale, Mathf.Max(0.1f, speed)));
        }

        private IEnumerator Play(Entry entry, Vector3 anchor, float settleScale, float speed)
        {
            float total = 总时长 / speed;
            float shrink = 收缩时长 / speed;
            float popScale = settleScale * 初始停留比;
            float fadeSpan = 1f - 淡出起点比例;

            yield return BattleViewTween.Over(total, t =>
            {
                // 位置：锚点逐帧投影（相机微调/缩放下仍贴住单位）+ 先快后慢上浮
                var cam = _camera != null ? _camera : Camera.main;
                if (cam == null) return;
                float rise = 上浮像素 * (1f - (1f - t) * (1f - t));
                entry.Rect.position = cam.WorldToScreenPoint(anchor) + Vector3.up * rise;

                // 缩放：收缩段内 easeOutCubic 从爆裂回停留，其后恒定
                float sT = shrink > 0f ? Mathf.Clamp01(t * total / shrink) : 1f;
                float eOut = 1f - (1f - sT) * (1f - sT) * (1f - sT);
                entry.Rect.localScale = Vector3.one * Mathf.Lerp(popScale, settleScale, eOut);

                // 渐隐：淡出起点前恒不透明，段尾线性收至 0
                entry.Group.alpha = t <= 淡出起点比例 ? 1f : 1f - (t - 淡出起点比例) / fadeSpan;
            });

            entry.Go.SetActive(false);
            entry.Co = null;
            _pool.Enqueue(entry);
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
        }
    }
}
