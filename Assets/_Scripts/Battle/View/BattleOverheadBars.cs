using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using GIC.Data;

namespace GIC.Battle
{
    /// <summary>
    /// 原神式头顶条层（2026-09-24 拍板「把血条也这样改造，并把血条下面加一条元能条。风格遵循原神分隔」）：
    /// 与伤害数字同一思路——独立 Screen Space - Overlay 画布（sortingOrder 38，伤害数字 39 之下、HUD 40 之下），
    /// 不参与 3D 深度测试：不受场景遮挡、任何角度读正面。逐帧把立牌头顶锚点（UnitView.OverheadBarAnchor，
    /// 含 55° 后仰偏移）投影为屏幕位置——相机缩放/相机平移/队形位移时条条都贴住单位头顶。
    /// 结构（自上往下）：血条（**填充色=队伍色**（2026-09-25 目检拍板：与底座同色，替换原敌我绿/红）+圆角黑边底图
    /// +原神量子刻度（2026-09-26 拍板「每 50 段，上限 10 段」：血条每 50 点血一格、上限 10 段，MaxHp 不足一段=无刻度线）→ 元能条（**白色**（同日拍板，原元能蓝退役）+圆角黑边，分隔量子=10 与 B6a 获取粒度
    /// 一致，段数上限 10）→ 附着小图标在血条左缘（风格沿用原神血条左侧附着的排法）。
    /// 底图=BattleViewFactory.BarBgSprite/BarFillSprite 运行时程序化圆角 sprite（黑边框 rgb=0 恒黑、白芯乘 tint=本色）；
    /// 名字/Buff 徽章仍挂立牌倾斜组（「都应当斜」拍板未推翻）。无 GraphicRaycaster（勿补——会挡 HUD 点击）。
    /// </summary>
    public class BattleOverheadBars : MonoBehaviour
    {
        [Header("尺寸（参考分辨率 2560×1440 像素，随 CanvasScaler 等比缩放）")]
        [SerializeField] private float 血条宽 = 120f;
        [SerializeField] private float 血条高 = 10f;
        [SerializeField] private float 元能条高 = 7f;
        [SerializeField] private float 条间距 = 4f;
        [SerializeField] private float 附着图标宽 = 16f;
        [SerializeField] private float 分隔线宽 = 2f;

        [Header("分隔（原神分隔风格）")]
        [Tooltip("血条每段血量（每 50 点血一格；MaxHp 不足一段=无刻度线）")]
        [SerializeField] private int 血条每段血量 = 50;

        [Tooltip("血条分隔段数上限（大血量单位钳制防刻度拥挤）")]
        [SerializeField] private int 血条分隔段数上限 = 10;

        [Tooltip("元能条每段量子（=B6a 获取粒度 +10：每攒 10 成一格）")]
        [SerializeField] private int 元能分隔量子 = 10;

        [Tooltip("元能条段数上限（100 上限元能也限制在 10 段内，防刻度拥挤）")]
        [SerializeField] private int 元能分隔段数上限 = 10;

        private Canvas _canvas;
        private Camera _camera;
        private Sprite _barBgSprite;
        private Sprite _barFillSprite;
        private static BattlePalette Palette => BattlePalette.Instance;

        /// <summary>黑边框厚度占条高比（=资产 BarBg.png 内 5px/24px；填充内缩=该值×条高，露出底图边框）</summary>
        private const float 边框厚度占比 = 5f / 24f;

        private sealed class Item
        {
            public UnitView View;
            public GameObject Go;
            public RectTransform Rect;
            public Image HpFill;
            public Image EnFill;
            public GameObject EnBarRoot; // 显式引用（曾用 transform.parent.parent 错链到条目根，2026-09-25 修）
            public Image AttachIcon;
        }

        private readonly List<Item> _items = new List<Item>();

        /// <summary>初始化（确保画布+相机；幂等——重复调用不重建）</summary>
        public void Init(Camera cam)
        {
            _camera = cam;
            if (_canvas != null) return;

            var canvasGo = new GameObject("OverheadBarsCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 38; // 伤害数字 39 之下、HUD 40 之下——数字漂过条上方时数字在上
            // 勿加 GraphicRaycaster：Overlay 画布无射线器即不吃射线（加了会挡 HUD 按钮/棋盘）
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560f, 1440f);   // 与 BattleHud.prefab 一致，观感同源
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // 圆角条底图=真实资产（2026-09-25 拍板「不要程序化生成」——运行时零生成；美术出图直接覆盖文件即可）
            _barBgSprite = Resources.Load<Sprite>("UI/Battle/BarBg");
            _barFillSprite = Resources.Load<Sprite>("UI/Battle/BarFill");
            if (_barBgSprite == null || _barFillSprite == null)
                Debug.LogWarning("[BattleOverheadBars] 头顶条圆角底图缺失（Resources/UI/Battle/BarBg.png|BarFill.png）");
        }

        /// <summary>登记一个单位的头顶条（整个战斗期跟随；view 消亡前先 ClearAll）</summary>
        public void Register(UnitView view)
        {
            if (_canvas == null || view == null) return;
            _items.Add(BuildItem(view));
        }

        /// <summary>清空全部头顶条（ClearViews 同步）</summary>
        public void ClearAll()
        {
            foreach (var item in _items)
                if (item.Go != null) Destroy(item.Go);
            _items.Clear();
        }

        private void Update()
        {
            var cam = _camera != null ? _camera : Camera.main;
            if (cam == null) return;

            foreach (var item in _items)
            {
                if (item.View == null || item.Go == null) continue; // view 被销毁时容错（正常路径 ClearAll 先行）
                item.Rect.position = cam.WorldToScreenPoint(item.View.OverheadBarAnchor);

                item.HpFill.fillAmount = item.View.MaxHp > 0
                    ? Mathf.Clamp01((float)item.View.Hp / item.View.MaxHp) : 0f;
                item.EnFill.fillAmount = item.View.EnergyMax > 0
                    ? Mathf.Clamp01((float)item.View.EnergyCurrent / item.View.EnergyMax) : 0f;

                var enGo = item.EnBarRoot;
                bool showEnergy = item.View.EnergyMax > 0 && !item.View.IsCorpse;
                if (enGo != null && enGo.activeSelf != showEnergy) enGo.SetActive(showEnergy);

                var element = item.View.AttachedElement;
                item.AttachIcon.gameObject.SetActive(element != ElementType.Physical);
                if (element != ElementType.Physical)
                {
                    var config = ElementFactionConfig.Instance;
                    var icon = config != null ? config.GetElementIconStroke(element) : null;
                    if (icon != null && item.AttachIcon.sprite != icon) item.AttachIcon.sprite = icon;
                }
            }
        }

        /// <summary>建单个条目：容器（锚点居中）→ 血条（底+填充+分隔线）+ 元能条→ 附着图标（血条左缘外）</summary>
        private Item BuildItem(UnitView view)
        {
            var root = new GameObject($"OverheadBars_{view.UnitId}", typeof(RectTransform));
            root.transform.SetParent(_canvas.transform, false);
            var rootRect = (RectTransform)root.transform;

            float totalH = 血条高 + 条间距 + 元能条高;

            // 血条（容器 y=0 中线；填充色=队伍色——2026-09-25 目检拍板与底座同色）
            var hpBar = NewBar("HpBar", rootRect, new Vector2(0f, 0f), 血条宽, 血条高,
                Palette.血条底, view.TeamColor,
                out var hpFill);

            // 元能条（血条下方；白色——2026-09-25 目检拍板）
            var enBar = NewBar("EnergyBar", rootRect, new Vector2(0f, -(血条高 * 0.5f + 条间距 + 元能条高 * 0.5f)),
                血条宽, 元能条高, Palette.血条底, Palette.元能条色, out var enFill);

            // 量子刻度（2026-09-26 拍板「每 50 段，上限 10 段」）：刻度线落在 每段值×k/总量 处——
            // 血条每 50 点血一格（MaxHp 非 50 倍数时末段不满，线位仍=50 的整数倍血）；元能=每 10 点一格
            BuildTicks(hpBar, 血条宽, 血条高, 血条每段血量, view.MaxHp, 血条分隔段数上限);
            BuildTicks(enBar, 血条宽, 元能条高, 元能分隔量子, view.EnergyMax, 元能分隔段数上限);

            // 附着图标（血条左缘外；Physical=隐藏由 Update 驱动）
            var iconGo = new GameObject("AttachIcon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(rootRect, false);
            var iconRect = (RectTransform)iconGo.transform;
            iconRect.anchoredPosition = new Vector2(-(血条宽 * 0.5f + 附着图标宽 * 0.8f), 0f);
            iconRect.sizeDelta = new Vector2(附着图标宽, 附着图标宽);
            var iconImage = iconGo.GetComponent<Image>();
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            iconGo.SetActive(false);

            _ = totalH;
            return new Item
            {
                View = view, Go = root, Rect = rootRect,
                HpFill = hpFill, EnFill = enFill, EnBarRoot = enBar.gameObject, AttachIcon = iconImage,
            };
        }

        /// <summary>单条：圆角底（黑边框+白芯乘底色 tint；sprite=资产 BarBg.png）+ 填充（圆角 BarFill.png，
        /// **内缩边框厚度**——同尺寸会整盖住底图边框环致黑边不可见，2026-09-25 目检实锤；Image Filled 左起，
        /// Filled 必须赋 sprite 才裁切，空 sprite 时 fillAmount 被忽略条恒满，docs/14 §75）。
        /// 撤 Outline 组件（四角偏移重投细边在条形上糊；边框烘进底图 crisply）。</summary>
        private RectTransform NewBar(string name, Transform parent, Vector2 center, float w, float h,
            Color bgColor, Color fillColor, out Image fill)
        {
            var barGo = new GameObject(name, typeof(RectTransform));
            barGo.transform.SetParent(parent, false);
            var barRect = (RectTransform)barGo.transform;
            barRect.anchoredPosition = center;
            barRect.sizeDelta = new Vector2(w, h);

            var bg = NewImage(barRect, "Bg", Vector2.zero, new Vector2(w, h), bgColor);
            bg.sprite = _barBgSprite; // 圆角+黑边框（白芯乘 bgColor=血条底）

            float inset = h * 边框厚度占比; // 填充内缩露出底图黑边框（关键：勿与底图同尺寸）
            fill = NewImage(barRect, "Fill", Vector2.zero, new Vector2(w - inset * 2f, h - inset * 2f), fillColor);
            fill.sprite = _barFillSprite; // 纯白药丸形（无边框）
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 1f;

            return barRect;
        }

        /// <summary>分隔线组（原神分隔：count 段=count−1 条竖线，均布于条内；count≤1 不画）</summary>
        /// <summary>量子刻度线组（原神分隔）：每段=quantum 个单位，段数=ceil(total/quantum) 钳 [0, maxSegments]；
        /// 刻度线落在 quantum×k / total 比例处（k=1..段数−1）——total 为 quantum 整数倍时=均分（元能条恒如此，
        /// 与旧均分画法数学恒等）；非整数倍时末段不满、线位仍标在 quantum 整数倍血量处。quantum/total≤0 不画</summary>
        private void BuildTicks(RectTransform bar, float w, float h, int quantum, float total, int maxSegments)
        {
            if (quantum <= 0 || total <= 0f) return;
            int segments = Mathf.Clamp(Mathf.CeilToInt(total / quantum), 0, Mathf.Max(0, maxSegments));
            if (segments <= 1) return;
            var tickColor = Palette.血条底;
            for (int i = 1; i < segments; i++)
            {
                float x = -w * 0.5f + w * (quantum * i) / total;
                NewImage(bar, $"Tick{i}", new Vector2(x, 0f), new Vector2(分隔线宽, h), tickColor);
            }
        }

        private static Image NewImage(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }
    }
}
