using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GIC.Framework;
using GIC.Data;
using GIC.UI;

namespace GIC.Battle
{
    /// <summary>
    /// 原神式头顶条层（2026-09-24 拍板「把血条也这样改造，并把血条下面加一条元能条。风格遵循原神分隔」）：
    /// 与伤害数字同一思路——独立 Screen Space - Overlay 画布（sortingOrder 38，伤害数字 39 之下、HUD 40 之下），
    /// 不参与 3D 深度测试：不受场景遮挡、任何角度读正面。逐帧把立牌头顶锚点（UnitView.OverheadBarAnchor，
    /// 含 55° 后仰偏移）投影为屏幕位置——相机缩放/相机平移/队形位移时条条都贴住单位头顶。
    /// 结构（自上往下，2026-10-06 命座徽章+Buff 图标批）：
    /// **命座徽章**（血条正上方贴齐：小背景 #39444F 80% + 黑描边数字原神白；眷属/0命不显示；
    /// 满命〔MaxConstellation〕数字与背景 HSV 循环彩色渐变——拍板「血条上不再需要显示名字，改为显示
    /// 命座数字」，世界空间单位名随之退役）→ 血条（**填充色=队伍色**+圆角黑边底图+原神量子刻度）→
    /// 元能条（白色+圆角黑边+分隔量子 10）→ **Buff 图标行**（能量条下方：**复用 QueueSlot 头像牌**
    /// 〔Plate/AvatarMask/Avatar/Ring〕——Avatar=Buff 来源技能图标〔延奏 AttackUp→延奏图标、寒冰之棱→
    /// 凛冽轮舞爆发图标…；专属图标优先、无来源技能回落元素图标〕、Ring=来源单位所属玩家色、
    /// 层数右下角+剩余回合左上角〔黑描边数字原神白；永久 Buff 不计时、单层不噪〕）→ 附着小图标在
    /// 血条左缘。底图=真实资产 Resources/UI/Battle/BarBg.png/BarFill.png（2026-09-25 拍板「不要程序化
    /// 生成」——美术出图直接覆盖文件即可）。
    /// **相机缩放跟随**（2026-10-06 拍板）：玩家放大（视轴距离&lt;初始）时整簇按相对缩放的 50% 放大
    /// （1:1 会快速占满屏幕）；缩小（距离&gt;初始）不再缩小=下限 1.0。
    /// 无 GraphicRaycaster（勿补——会挡 HUD 点击）。
    /// </summary>
    public class BattleOverheadBars : MonoBehaviour
    {
        [Header("尺寸（参考分辨率 2560×1440 像素，随 CanvasScaler 等比缩放）")]
        [SerializeField] private float 血条宽 = 120f;
        [SerializeField] private float 血条高 = 10f;
        [SerializeField] private float 元能条高 = 7f;
        [Tooltip("血条与元能条之间的间隙——2026-10-06 拍板「能量条紧贴血条不要有间隙」改 0（两 BarBg 黑边框相贴=略粗分隔线，属边框非间隙）")]
        [SerializeField] private float 条间距 = 0f;
        [SerializeField] private float 附着图标宽 = 16f;
        [SerializeField] private float 分隔线宽 = 2f;

        [Header("命座徽章（2026-10-06 拍板「血条上不再显示名字，改为显示命座数字」）")]
        [Tooltip("命座数字字号（黑描边+原神白）")]
        [SerializeField] private float 命座字号 = 20f;
        [Tooltip("徽章高（宽=高+2×内边距，贴血条上缘居中）")]
        [SerializeField] private float 命座徽章高 = 26f;
        [Tooltip("徽章内边距（数字与背景边缘的水平余量）")]
        [SerializeField] private float 命座徽章内边距 = 8f;
        [Tooltip("满命彩色渐变速度（色相环/秒——0.22≈4.5 秒一圈；数字与背景同步渐变）")]
        [SerializeField] private float 满命渐变速度 = 0.22f;

        [Header("Buff 图标行（2026-10-06 拍板：能量条下方，复用头像牌+来源技能图标）")]
        [Tooltip("单个 Buff 图标尺寸（像素，参考分辨率下）")]
        [SerializeField] private float Buff图标尺寸 = 36f;
        [Tooltip("相邻 Buff 图标中心距（像素）")]
        [SerializeField] private float Buff图标间距 = 8f;
        [Tooltip("能量条底边到 Buff 图标上缘的间隙（像素）——图标不遮血条/能量条（2026-10-07 报障返修：原「到图标中心」10px 让 36px 图标上盖能量条/血条）")]
        [SerializeField] private float Buff行距 = 3f;
        [Tooltip("层数/剩余回合角标字号")]
        [SerializeField] private float Buff角标字号 = 16f;

        [Header("相机缩放跟随（2026-10-06 拍板：放大=整簇跟随放大取比率、缩小=下限不缩）")]
        [Tooltip("相对相机缩放的跟随比率（1=1:1 全跟；0.5=放大 2 倍时头顶条放大 1.5 倍——防快速占满屏幕）")]
        [SerializeField, Range(0f, 1f)] private float 相机缩放跟随比率 = 0.5f;

        [Header("分隔（原神分隔风格）")]
        [Tooltip("血条每段血量（每 50 点血一格；MaxHp 不足一段=无刻度线）")]
        [SerializeField] private int 血条每段血量 = 50;

        [Tooltip("血条分隔段数上限（大血量单位钳制防刻度拥挤）")]
        [SerializeField] private int 血条分隔段数上限 = 10;

        [Tooltip("元能条每段量子（=B6a 获取粒度 +10：每攒 10 成一格）")]
        [SerializeField] private int 元能分隔量子 = 10;

        [Tooltip("元能条段数上限（100 上限元能也限制在 10 段内，防刻度拥挤）")]
        [SerializeField] private int 元能分隔段数上限 = 10;

        [Header("数字描边（SDF 原生——与倒计时/伤害数字同口径，docs/14 §84）")]
        [Tooltip("命座/角标数字的 SDF 描边宽度（0~1 相对字形，随缩放恒定可见）")]
        [SerializeField] private float 数字描边宽度 = 0.08f;

        private Canvas _canvas;
        private Camera _camera;
        private Sprite _barBgSprite;
        private Sprite _barFillSprite;
        private static BattlePalette Palette => BattlePalette.Instance;

        /// <summary>Buff 图标描环=来源单位所属玩家色（BattleHud.Bind 注入的本端视角解析器；
        /// null=Bind 前兜底白环——建场早于 Bind 时最多一帧）</summary>
        public System.Func<string, Color> PlayerColorOf;

        /// <summary>相机缩放跟随源（BattleHud.Bind 注入；null=不跟随恒 1.0）</summary>
        public BattleCameraController CameraController;

        /// <summary>头像牌 prefab（QueueSlot：Plate/AvatarMask/Avatar/Ring 契约——Buff 图标复用角色头像同款框）</summary>
        private GameObject _queueSlotPrefab;

        /// <summary>数字 SDF 描边共享材质实例（层内全部数字共用一份——描边参数相同无 per-text 变化；
        /// OnDestroy 释放，docs/14 §63 生命周期纪律）</summary>
        private Material _outlineMat;

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
            public GameObject ConstGo;            // 命座徽章（血条上方）
            public Image ConstBg;                // 徽章背景（满命彩色渐变载体）
            public TextMeshProUGUI ConstText;    // 命座数字
            public RectTransform BuffRow;        // Buff 图标行容器（重建时清子级重铺）
            public int LastBuffRevision = -1;    // 上次重建对应的 UnitView.BuffRevision
            public int LastConstLevel = -1;      // 上次写文本的命座值（防逐帧 text 写入）
            public bool ConstRainbow;            // 当前处于满命彩色渐变态（退出时还原静态配色）
            public float LiftCurrent;            // 智能抬高当前位移（屏幕像素；指数平滑趋近目标）
        }

        private readonly List<Item> _items = new List<Item>();
        private readonly List<Item> _sortBuffer = new List<Item>();   // 深度排序复用（零逐帧分配）

        /// <summary>初始化（确保画布+相机；幂等——重复调用不重建）</summary>
        public void Init(Camera cam)
        {
            _camera = cam;
            if (_canvas != null) return;

            var canvasGo = new GameObject("OverheadBarsCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = BattleMetrics.OverheadBarsCanvasOrder; // 伤害数字 39 之下、HUD 40 之下——数字漂过条上方时数字在上
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
            var cam = _camera != null ? _camera : CameraContext.Resolve();
            if (cam == null) return;

            // 相机缩放跟随（2026-10-06 拍板）：相对初始距离的放大按「相机缩放跟随比率」半速率跟
            // （1:1 会快速占满屏幕）、缩小不缩（下限 1.0）——整簇根 localScale 统一承载
            float zoomScale = ComputeZoomScale();
            // 画布单位→屏幕像素换算（智能抬高的位移量用）
            float scaleFactor = _canvas != null ? _canvas.scaleFactor : 1f;

            foreach (var item in _items)
            {
                if (item.View == null || item.Go == null) continue; // view 被销毁时容错（正常路径 ClearAll 先行）

                // 智能整体抬高（2026-10-07 拍板「下面加了 buff 图标后，应当智能整体抬高，避免遮挡到
                // 角色立牌」）：有 Buff 图标时整簇上移「锚点以下悬垂深度」——图标底缘回到锚点（立牌顶）
                // 即整簇悬空不压立牌；无 Buff 零位移。指数平滑（0.08s 时间常数）——Buff 出现/消失时
                // 条簇柔和升降非瞬跳；悬垂深度随 zoomScale 同步放大
                float liftTarget = item.View.Buffs.Count > 0
                    ? BuffBelowExtent() * zoomScale * scaleFactor : 0f;
                item.LiftCurrent = Mathf.Approximately(item.LiftCurrent, liftTarget)
                    ? liftTarget
                    : Mathf.Lerp(item.LiftCurrent, liftTarget,
                        1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.08f));
                var projected = cam.WorldToScreenPoint(item.View.OverheadBarAnchor);
                item.Rect.position = new Vector3(projected.x, projected.y + item.LiftCurrent, projected.z);

                if (item.Rect.localScale.x != zoomScale)
                    item.Rect.localScale = Vector3.one * zoomScale;

                item.HpFill.fillAmount = item.View.MaxHp > 0
                    ? Mathf.Clamp01((float)item.View.Hp / item.View.MaxHp) : 0f;
                item.EnFill.fillAmount = item.View.EnergyMax > 0
                    ? Mathf.Clamp01((float)item.View.EnergyCurrent / item.View.EnergyMax) : 0f;

                var enGo = item.EnBarRoot;
                // 元能条显隐：EnergyMax>0 即显（2026-10-06 拍板「尸体应当同样显示血条和能量条」
                // ——旧「尸体隐藏元能条」预期作废；尸血条恒显=空条、元能=死亡残留值）
                bool showEnergy = item.View.EnergyMax > 0;
                if (enGo != null && enGo.activeSelf != showEnergy) enGo.SetActive(showEnergy);

                var element = item.View.AttachedElement;
                item.AttachIcon.gameObject.SetActive(element != ElementType.Physical);
                if (element != ElementType.Physical)
                {
                    var config = ElementFactionConfig.Instance;
                    var icon = config != null ? config.GetElementIconStroke(element) : null;
                    // 缺图兜底（2026-10-06 拍板全位点接入）：附着图标缺失=missing_image 占位
                    var target = MissingImageGuard.Ensure(icon);
                    if (target != null && item.AttachIcon.sprite != target) item.AttachIcon.sprite = target;
                }

                UpdateConstellationBadge(item);
                if (item.View.BuffRevision != item.LastBuffRevision)
                    RebuildBuffIcons(item);
            }

            SortItemsByDepth(cam);
        }

        // ==================== 命座徽章（2026-10-06 拍板「血条上不再显示名字，改为显示命座数字」） ====================

        /// <summary>命座徽章逐帧驱动：显隐（眷属无命座/0命不显示）+ 数字文本（变更才写）+
        /// 满命彩色渐变（数字与背景 HSV 循环同步；退出渐变态还原静态配色）</summary>
        private void UpdateConstellationBadge(Item item)
        {
            int level = item.View.ConstellationLevel;
            bool show = !item.View.IsFamiliar && level > 0;
            if (item.ConstGo != null && item.ConstGo.activeSelf != show)
                item.ConstGo.SetActive(show);
            if (!show || item.ConstText == null) return;

            if (level != item.LastConstLevel)
            {
                item.LastConstLevel = level;
                item.ConstText.text = level.ToString();
            }

            if (level >= ConstellationApplier.MaxConstellation)
            {
                // 满命=彩色持续变化（拍板：数字与背景都渐变）——unscaled 驱动（暂停时仍流转，纯观感）
                float h = Mathf.Repeat(Time.unscaledTime * 满命渐变速度, 1f);
                item.ConstRainbow = true;
                item.ConstText.color = Color.HSVToRGB(h, 0.55f, 1f);
                var bg = Color.HSVToRGB(h, 0.5f, 0.85f);
                bg.a = 命座背景不透明度;
                item.ConstBg.color = bg;
            }
            else if (item.ConstRainbow)
            {
                // 退出满命态（新对局复用防御——局内命座只增不会到此）：还原静态配色
                item.ConstRainbow = false;
                item.ConstText.color = Palette != null ? Palette.文字米白 : Color.white;
                item.ConstBg.color = Palette != null ? Palette.命座背景色
                    : new Color(0.224f, 0.267f, 0.310f, 命座背景不透明度);
            }
        }

        /// <summary>命座背景不透明度（拍板「不透明 80%」）</summary>
        private const float 命座背景不透明度 = 0.8f;

        /// <summary>有 Buff 图标时锚点以下的悬垂深度（画布单位=血条半高+条间距+元能条高+Buff行距+图标全高）
        /// ——智能整体抬高的位移基准（图标底缘抬回锚点=立牌顶，整簇不压立牌）</summary>
        private float BuffBelowExtent() => 血条高 * 0.5f + 条间距 + 元能条高 + Buff行距 + Buff图标尺寸;

        // ==================== Buff 图标行（2026-10-06 拍板：能量条下方） ====================

        /// <summary>Buff 图标行重建（UnitView.BuffRevision 变化触发）：每条=QueueSlot 头像牌复用
        /// （Avatar=来源技能图标/专属图标/元素回退、Ring=来源玩家色）+ 层数右下/剩余回合左上角标。
        /// **恒单行**（2026-10-07 拍板「buff 太多也只在一行显示，不要换行」——横向居中排开不折行）。
        /// 行级重建（回合/命令粒度低频）——非逐帧拼装</summary>
        private void RebuildBuffIcons(Item item)
        {
            item.LastBuffRevision = item.View.BuffRevision;
            for (int i = item.BuffRow.childCount - 1; i >= 0; i--)
                Destroy(item.BuffRow.GetChild(i).gameObject);

            if (_queueSlotPrefab == null)
                _queueSlotPrefab = Resources.Load<GameObject>("Prefabs/Battle/QueueSlot");
            if (_queueSlotPrefab == null)
            {
                Debug.LogWarning("[BattleOverheadBars] QueueSlot.prefab 未找到——头顶 Buff 图标行不显示");
                return;
            }

            var buffs = item.View.Buffs;
            int count = buffs.Count;
            float pitch = Buff图标尺寸 + Buff图标间距;
            float cornerOffset = Buff图标尺寸 * 0.34f;
            for (int i = 0; i < count; i++)
            {
                var buff = buffs[i];
                float x = (i - (count - 1) * 0.5f) * pitch;

                var tile = Instantiate(_queueSlotPrefab, item.BuffRow, false);
                tile.name = $"Buff_{buff.type}";
                var tileRt = (RectTransform)tile.transform;
                tileRt.anchorMin = tileRt.anchorMax = tileRt.pivot = new Vector2(0.5f, 0.5f);
                tileRt.anchoredPosition = new Vector2(x, 0f);
                tileRt.localScale = Vector3.one * (Buff图标尺寸 / 64f); // 64=QueueSlot 原生尺寸（描环同比）

                var avatar = tileRt.Find("AvatarMask/Avatar")?.GetComponent<Image>();
                var ring = tileRt.Find("Ring")?.GetComponent<Image>();
                if (avatar != null)
                {
                    // 缺图兜底（2026-10-06 拍板全位点接入）：未知类型/图标缺失=missing_image 占位照常显示
                    var icon = MissingImageGuard.Ensure(ResolveBuffIcon(buff));
                    if (icon != null) avatar.sprite = icon;
                }
                if (ring != null)
                {
                    // 描环=来源单位所属玩家色（拍板）——与快捷面板/执行预览头像描环同 alpha 口径；
                    // 解析器未注入（Bind 前）或无来源（理论不可达）回落白
                    var pc = PlayerColorOf != null && !string.IsNullOrEmpty(buff.sourceUnitId)
                        ? PlayerColorOf(buff.sourceUnitId)
                        : Color.white;
                    ring.color = new Color(pc.r, pc.g, pc.b, 0.8f);
                }

                // 角标（行空间绝对摆位——勿挂 tile 内：tile 带 36/64 缩放会连带缩字号）
                // 层数=右下（StatBuff 族 Level=叠层数；单层=1 不显示防噪）；剩余回合=左上
                // （永久 Buff remainingTurns<0 如歌声之环/寒冰之棱不计时——既有拍板口径）
                if (buff.level >= 2)
                    NewNumber(item.BuffRow, "Stacks", new Vector2(x + cornerOffset, -cornerOffset),
                        Buff角标字号).text = buff.level.ToString();
                if (buff.remainingTurns > 0)
                    NewNumber(item.BuffRow, "Turns", new Vector2(x - cornerOffset, cornerOffset),
                        Buff角标字号).text = buff.remainingTurns.ToString();
            }
        }

        /// <summary>Buff 图标解析（拍板「图标用来源技能图标」）：专属图标（per-type 覆盖，如歌声之环/减防）
        /// &gt; 来源技能图标（SkillConfig.icon——延奏 AttackUp=延奏图标、凛冽轮舞寒冰之棱=爆发图标）
        /// &gt; 元素图标回退（反应类 Buff〔燃烧/冻结〕无技能语境）</summary>
        private static Sprite ResolveBuffIcon(BuffState buff)
        {
            var dedicated = DedicatedBuffIcon((BuffType)buff.type);
            if (dedicated != null) return dedicated;
            if (buff.sourceSkillId > 0)
            {
                var data = BattlePlayer.LoadSkillData(buff.sourceSkillId);
                if (data != null && data.icon != null) return data.icon;
            }
            return BuffElementIconOf(buff.type);
        }

        /// <summary>per-type 专属图标覆盖（2026-10-07 Buff 配置化 docs/active/39：图标=BuffConfig 资产
        ///〔专属图标〕字段引用，2026-10-07 用户指定源图——歌声之环=Skill_S_Barbara_01〔芭芭拉战技〕、
        /// 通用减防=UI_Talent_S_Lisa_06〔丽莎 6 命〕，256² alpha 已修）。**新增专属图标=在
        /// Buff_* 资产 Inspector 挂图，零代码**；null（未指定/缺资产）回落来源技能→元素图标
        /// （决策五十回落链）</summary>
        private static Sprite DedicatedBuffIcon(BuffType type)
        {
            return GIC.Data.BuffConfig.OfType(type)?.专属图标;
        }

        /// <summary>元素图标回退（原 UnitView.BuffIconOf 映射随命座徽章批迁移至此——元素 Stroke 现成图）</summary>
        private static Sprite BuffElementIconOf(int buffType)
        {
            var config = ElementFactionConfig.Instance;
            if (config == null) return null;
            switch ((BuffType)buffType)
            {
                case BuffType.Burn: return config.GetElementIconStroke(ElementType.Pyro);
                case BuffType.Freeze: return config.GetElementIconStroke(ElementType.Cryo);
                case BuffType.AttackUp: return config.GetElementIconStroke(ElementType.Anemo);
                case BuffType.MoveSpeedUp: return config.GetElementIconStroke(ElementType.Anemo);
                case BuffType.SongOfLife: return config.GetElementIconStroke(ElementType.Hydro);
                case BuffType.Icicle: return config.GetElementIconStroke(ElementType.Cryo);
                case BuffType.DefenseDown: return config.GetElementIconStroke(ElementType.Cryo);
                default: return null;
            }
        }

        // ==================== 相机缩放跟随（2026-10-06 拍板） ====================

        /// <summary>整簇缩放系数：放大（当前距离&lt;初始）按相对缩放×跟随比率半速率放大（1:1 会快速
        /// 占满屏幕）；缩小（距离&gt;初始）不再缩小=下限 1.0。相机源未注入=恒 1.0</summary>
        private float ComputeZoomScale()
        {
            var ctrl = CameraController;
            if (ctrl == null || ctrl.InitialDistance <= 0f) return 1f;
            float zoomRatio = ctrl.InitialDistance / Mathf.Max(0.01f, ctrl.CurrentDistance); // >1=玩家放大
            return 1f + Mathf.Max(0f, zoomRatio - 1f) * 相机缩放跟随比率;
        }

        // ==================== 数字件（黑描边 SDF+原神白，层内共享描边材质） ====================

        /// <summary>新建黑描边数字（TMP_UGUI）：字体=世界字体链同源、Bold、原神白；描边=层内共享
        /// SDF 材质实例（_OutlineWidth/_OutlineColor 与倒计时/伤害数字同口径 docs/14 §84——
        /// 勿用 UGUI Outline 固定像素式，缩放视口下不可见）</summary>
        private TextMeshProUGUI NewNumber(Transform parent, string name, Vector2 pos, float fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = new Vector2(fontSize * 3f, fontSize * 1.6f);

            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = BattleViewFactory.WorldTextFont;
            text.fontSize = fontSize;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.color = Palette != null ? Palette.文字米白 : Color.white; // 原神风白（拍板）
            var mat = EnsureOutlineMaterial(text);
            if (mat != null) text.fontMaterial = mat;
            return text;
        }

        /// <summary>层内共享描边材质（懒建一份；字体材质无 _OutlineWidth 属性时 null=无描边降级）；
        /// OnDestroy 统一释放</summary>
        private Material EnsureOutlineMaterial(TMP_Text target)
        {
            if (_outlineMat != null) return _outlineMat;
            var shared = target.fontSharedMaterial;
            if (shared != null && shared.HasProperty("_OutlineWidth"))
            {
                _outlineMat = new Material(shared);
                _outlineMat.SetColor("_OutlineColor",
                    Palette != null ? Palette.伤害数字描边色 : new Color(0.04f, 0.03f, 0.03f, 0.85f));
                _outlineMat.SetFloat("_OutlineWidth", 数字描边宽度);
            }
            return _outlineMat;
        }

        /// <summary>重叠深度排序（2026-10-06 报障修复「上方 1 格的芭芭拉条盖住下方安柏的条」）：
        /// Overlay 同级条目 painter 序=sibling 序，原=建场注册序（快照单位序）与空间无关——
        /// 上下相邻单位条区屏幕重叠时（取证实证：两单位条锚点屏幕 y 仅差 1px），远处单位可能后画
        /// 盖住近处。修=按相机距离排 sibling：**远者先画（底层）、近者后画（顶层）**——近处
        /// （屏幕下方）单位的条覆盖远处单位，与空间直觉一致。顺序稳定时零写（校验后才 SetSiblingIndex），
        /// ≤70 项每帧排序可忽略</summary>
        private void SortItemsByDepth(Camera cam)
        {
            if (_items.Count < 2) return;
            _sortBuffer.Clear();
            foreach (var item in _items)
            {
                if (item.View == null || item.Go == null) continue;
                _sortBuffer.Add(item);
            }
            _depthComparer.CamPos = cam.transform.position;
            _sortBuffer.Sort(_depthComparer); // 远→近（升序=距离降序；零逐帧分配——比较器实例复用）

            bool changed = false;
            for (int i = 0; i < _sortBuffer.Count; i++)
            {
                if (_sortBuffer[i].Go.transform.GetSiblingIndex() != i) { changed = true; break; }
            }
            if (!changed) return; // 顺序稳定零写（免逐帧 canvas reorder 脏标记）

            for (int i = 0; i < _sortBuffer.Count; i++)
                _sortBuffer[i].Go.transform.SetSiblingIndex(i); // sibling 序=画序（大序后画=顶层）
        }

        /// <summary>距离降序比较器（远者在前=先画底层；CamPos 每帧注入；平方距离同序免开方）</summary>
        private sealed class DepthComparer : System.Collections.Generic.IComparer<Item>
        {
            public Vector3 CamPos;
            public int Compare(Item a, Item b)
            {
                float db = (CamPos - b.View.transform.position).sqrMagnitude;
                float da = (CamPos - a.View.transform.position).sqrMagnitude;
                return db.CompareTo(da); // b 更远=b 排前 → 升序输出=远→近
            }
        }
        private static readonly DepthComparer _depthComparer = new DepthComparer();

        /// <summary>建单个条目：容器（锚点居中）→ 命座徽章（血条上方贴齐）→ 血条（底+填充+分隔线）
        /// + 元能条 → Buff 图标行容器（能量条下方）→ 附着图标（血条左缘外）</summary>
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

            // 命座徽章（2026-10-06 拍板「血条上不再显示名字，改为显示命座数字」）：血条正上方贴齐居中；
            // 背景=#39444F 80% 圆角小片（BarFill 药丸图元复用免新素材），数字=黑描边原神白；
            // 眷属/0命显隐与满命彩色渐变由 UpdateConstellationBadge 逐帧驱动
            var constGo = new GameObject("Constellation", typeof(RectTransform), typeof(Image));
            constGo.transform.SetParent(rootRect, false);
            var constRect = (RectTransform)constGo.transform;
            constRect.anchorMin = constRect.anchorMax = constRect.pivot = new Vector2(0.5f, 0.5f);
            constRect.anchoredPosition = new Vector2(0f, 血条高 * 0.5f + 命座徽章高 * 0.5f - 1f); // −1=微叠边框「贴着血条」
            constRect.sizeDelta = new Vector2(命座徽章高 + 命座徽章内边距 * 2f, 命座徽章高);
            var constBg = constGo.GetComponent<Image>();
            constBg.sprite = _barFillSprite;      // 纯白药丸形（圆角底，tint 承载色）
            constBg.color = Palette.命座背景色;    // #39444F 不透明 80%（BattlePalette 单源）
            constBg.raycastTarget = false;
            var constText = NewNumber(constRect, "Text", Vector2.zero, 命座字号);
            constGo.SetActive(false);

            // Buff 图标行容器（能量条下方；子级由 RebuildBuffIcons 按 BuffRevision 重建）。
            // 行中心=能量条底边再往下「Buff行距+图标半高」——图标上缘贴能量条底（2026-10-07 报障返修：
            // 原按「到图标中心」摆位 10px，36px 图标上盖能量条/血条）
            var rowGo = new GameObject("BuffRow", typeof(RectTransform));
            rowGo.transform.SetParent(rootRect, false);
            var buffRow = (RectTransform)rowGo.transform;
            buffRow.anchorMin = buffRow.anchorMax = buffRow.pivot = new Vector2(0.5f, 0.5f);
            buffRow.anchoredPosition = new Vector2(0f,
                -(血条高 * 0.5f + 条间距 + 元能条高 + Buff行距 + Buff图标尺寸 * 0.5f));
            buffRow.sizeDelta = Vector2.zero;

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
                ConstGo = constGo, ConstBg = constBg, ConstText = constText, BuffRow = buffRow,
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

        /// <summary>层销毁：数字 SDF 描边共享材质实例释放（TMP 不自销；不释放则跨战斗累积，
        /// docs/14 §63 生命周期纪律）</summary>
        private void OnDestroy()
        {
            if (_outlineMat != null) Destroy(_outlineMat);
            _outlineMat = null;
        }
    }
}
