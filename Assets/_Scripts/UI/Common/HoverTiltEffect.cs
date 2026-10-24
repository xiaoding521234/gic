using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using GIC.Framework;

namespace GIC.UI
{
    /// <summary>
    /// 悬停灵动视效（2026-10-21 卡片手感批；调研与参数锚点=.codely-cli/webrefs/card-hover-feel/NOTES.md）：
    /// 悬停时本物体轻微朝向鼠标 3D 倾斜（Marvel Snap/MTG Arena 家族手感）+ 提起缩放 + 暖金光泽跟随 + 微呼吸。
    /// 倾斜语义=光标侧压下（朝向鼠标，返修六翻向定案）；
    /// 指针位置=Update 逐帧轮询单源（返修五——事件 move 派发有失效窗口勿依赖）。
    /// （厚度边条=返修六/七实验性朝向载体，2026-10-22 用户拍板「去掉这个厚度，不再采用」全量移除——
    /// 带形 mesh 实现与黑色块教训留档 docs/14 §163 坑⑥/§164。）
    /// 朝向感载体=透视 Warp（2026-10-24 返修九）：正交画布纯旋转 180° 对称——Euler(±a,±b) 投影逐顶点全同，
    /// 对角悬停两角渲染不可分（报障「有时对有时反」的根因；商业库全部用真透视承载方向——vanilla-tilt.js
    /// perspective(1000px)/Atropos 1200px/Marvel Snap 真 3D）——EnsureWarps 给子树每个 Graphic 懒挂
    /// HoverTiltWarp 做透视等效位移（近缘放大/远缘缩小 Keystone），细节与双路径见 HoverTiltWarp.cs 头注。
    ///
    /// 通道纪律（与既有系统零冲突的关键）：
    /// - 只写本物体 localRotation / localScale——位置通道（anchoredPosition）留给布局组/入场滑动/手牌让位等系统；
    /// - 空闲落定后停写，transform 所有权归还其它系统；
    /// - OnDisable 即刻还原基准（CardPool.Release 直接 SetActive(false)——池化复用防带角度/带缩放回池）。
    ///
    /// 事件源：本物体或其子级上有 raycastTarget 即可（UGUI 指针事件沿父级冒泡到本组件）。
    /// 战斗手牌的卡内 raycast 全关、命中层在 wrapper——由 BattleHud 在 wrapper 上挂本组件（卡内组件收不到事件）。
    ///
    /// 让位协议（拖拽系统占用 transform 时）：
    /// - Suspend()=立即还原基准并停更——拖拽系统随后捕获的 baseScale 即 authored 基准，无悬停态叠加污染；
    /// - Resume()=恢复驱动，并按「组件内最后指针位置」重臂悬停（拖拽结束指针未离开时无 enter 事件重发，不丢悬停态）。
    ///
    /// OnlyDisplay 卡（祈愿轨道/详情展示）由 Card.SetViewType 关停本组件（祈愿流程自己写卡根 scale/rotation——
    /// WishDrawController L459/L559/Display.cs 实证冲突）。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public class HoverTiltEffect : MonoBehaviour,
        IPointerEnterHandler, IPointerMoveHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler
    {
        [Header("倾斜（朝向鼠标）")]
        [Tooltip("悬停最大倾斜角（度）——光标所在边缘翻向观察者；卡片 10 / 按钮 6 档")]
        [SerializeField] private float 最大倾角 = 10f;

        [Header("提起与按压")]
        [Tooltip("悬停缩放通道开关——缩放归拖拽系统管的宿主（战斗手牌 wrapper）关闭，本组件即完全不碰 localScale")]
        [SerializeField] private bool 启用缩放 = true;
        [Tooltip("悬停提起缩放倍率（绕中心）")]
        [SerializeField] private float 提起缩放 = 1.05f;
        [Tooltip("按压缩放倍率（松手回落）")]
        [SerializeField] private float 按压缩放 = 0.97f;

        [Header("光泽跟随")]
        [Tooltip("悬停时对角光泽带跟随光标（复用 UI/CardLightBand shader，与祈愿光带同源）")]
        [SerializeField] private bool 启用光泽 = true;
        [Tooltip("光泽峰值强度（0-1）")]
        [SerializeField, Range(0f, 1f)] private float 光泽强度 = 0.28f;
        [Tooltip("光泽颜色（默认暖金白，贴原神 UI 材质语言）")]
        [SerializeField] private Color 光泽颜色 = new Color(1f, 0.97f, 0.82f, 1f);
        [Tooltip("光泽带宽度（对角投影比例）")]
        [SerializeField, Range(0.03f, 0.5f)] private float 光泽宽度 = 0.18f;

        [Header("微呼吸")]
        [Tooltip("悬停中的极轻微持续摆动（「卡片永不确定格」=活着的观感）")]
        [SerializeField] private bool 启用微呼吸 = true;
        [Tooltip("微呼吸幅度（度）")]
        [SerializeField] private float 微呼吸幅度 = 0.35f;
        [Tooltip("微呼吸频率（Hz）")]
        [SerializeField] private float 微呼吸频率 = 1.1f;

        [Header("透视朝向感（返修九）")]
        [Tooltip("透视形变承载倾斜方向感（近缘放大/远缘缩小的 Keystone 梯形；vanilla-tilt.js/Atropos 的 CSS perspective 的 UGUI 等效）。关=纯旋转——正交下对角悬停渲染全同、方向感随机翻转（返修九前的病灶形态）")]
        [SerializeField] private bool 启用透视 = true;
        [Tooltip("虚拟透视距离=倍率×卡面长边世界尺寸（越小透视越强；vanilla-tilt.js 默认 1000px≈3.3 倍卡宽、Atropos 1200px≈3 倍）")]
        [SerializeField, Min(1f)] private float 透视距离倍率 = 3f;

        [Header("层级与响应")]
        [Tooltip("悬停渲染置顶（嵌套 Canvas overrideSorting——不动 sibling 序，布局组安全）。**默认关**：当前全部场景无重叠需求（背包间距 30/45px、按钮列 280px >> 放大外扩），而嵌套画布是新射线域——机制不全时挂上即 enter/exit 闪烁循环+点击丢失（2026-10-22 返修三实锤「一直在闪」）；真需要重叠压边时再开（机制已修全：自动补 GraphicRaycaster+销毁顺序与借用自愈=返修四）")]
        [SerializeField] private bool 悬停置顶 = false;
        [Tooltip("置顶 Canvas 的 sortingOrder（1=压过同画布普通元素，勿过大防盖弹窗）")]
        [SerializeField] private int 悬停置顶层级 = 1;
        [Tooltip("悬停中的指数趋近速率（大=更跟手）")]
        [SerializeField] private float 平滑速率 = 20f;
        [Tooltip("移出后的回正速率（略慢于跟随=柔和收尾）")]
        [SerializeField] private float 回正速率 = 14f;

        private RectTransform _rt;
        private Vector3 _baseScale = Vector3.one;
        private Quaternion _baseRotation = Quaternion.identity;
        private bool _hasBase;

        private bool _hovered;
        private bool _pressed;
        private bool _suspended;

        // 指针记录（rect 归一化 [0,1]；持续记录——Resume 重臂悬停的依据）
        private Vector2 _pointerNorm = new Vector2(0.5f, 0.5f);
        private Vector2 _lastPointerScreen;
        private Camera _lastPointerCamera;
        private bool _pointerSeen;

        // 当前平滑值
        private float _tiltX;
        private float _tiltY;
        private float _scaleMul = 1f;
        private float _sheenAlpha;

        private Canvas _topCanvas;
        private GraphicRaycaster _topRaycaster;
        private bool _ownedTopCanvas; // true=本组件创建（restore 时销毁）；false=借用宿主 authored Canvas（勿销毁）
        private bool _ownedTopRaycaster; // 返修四：借用已有画布时发现缺射线器、由本组件补建的标记（还原时移除）
        private bool _enabledBorrowedCanvas; // 返修四：借用时发现禁用态孤儿画布、由本组件启用的标记（还原时复原禁用态）

        private static Shader _bandShader;
        private static Shader BandShader => _bandShader != null ? _bandShader : (_bandShader = Shader.Find("UI/CardLightBand"));

        private HoverSheenGraphic _sheen;
        private Material _sheenMaterial;

        private RectTransform Rt => _rt != null ? _rt : (_rt = (RectTransform)transform);

        // ==================== 运行时配置（宿主接线用：BattleHud 手牌 wrapper 运行时 AddComponent 后调参；
        // Inspector 序列化值仍是常驻宿主〔prefab/场景〕的单源） ====================

        public float TiltMax { get => 最大倾角; set => 最大倾角 = value; }
        public bool ScaleEnabled { get => 启用缩放; set => 启用缩放 = value; }
        public bool SheenEnabled { get => 启用光泽; set => 启用光泽 = value; }
        public float SheenIntensity { get => 光泽强度; set => 光泽强度 = Mathf.Clamp01(value); }
        public bool RaiseOnHover { get => 悬停置顶; set => 悬停置顶 = value; }
        public bool PerspectiveEnabled { get => 启用透视; set => 启用透视 = value; }
        public float PerspectiveDistanceScale { get => 透视距离倍率; set => 透视距离倍率 = Mathf.Max(1f, value); }

        // Warp 消费口（透视参数单源在本组件——子树所有 Warp 共读，勿给 Warp 单独配参；
        // public=桥脚本/探针同可访问，桥 Roslyn 脚本是独立程序集读不到 internal）
        public bool 透视生效 => enabled && isActiveAndEnabled && 启用透视;

        public float 透视距离世界
        {
            get
            {
                var rt = Rt;
                float span = rt != null ? Mathf.Max(rt.rect.width, rt.rect.height) : 0f;
                // lossyScale 同乘=d 随等比缩放伸缩（k=d/(d+z) 对等比缩放不变——旧网格在平移/等比缩放后仍数学正确）
                return 透视距离倍率 * Mathf.Max(1f, span * Mathf.Abs(transform.lossyScale.y));
            }
        }

        // ==================== 生命周期 ====================

        private void OnEnable()
        {
            CaptureBase();
            _suspended = false;
            _hovered = false;
            _pressed = false;
        }

        private void OnDisable()
        {
            RestoreImmediate();
        }

        private void OnDestroy()
        {
            DestroySheen();
        }

        /// <summary>缓存 authored 基准（铁律：运行时叠偏移的组件必须缓存基准再叠加，勿直写绝对值）</summary>
        private void CaptureBase()
        {
            _rt = (RectTransform)transform;
            _baseScale = _rt.localScale;
            _baseRotation = _rt.localRotation;
            _hasBase = true;
        }

        // ==================== 指针事件 ====================

        public void OnPointerEnter(PointerEventData eventData)
        {
            TrackPointer(eventData);
            if (_suspended) return;
            // enter 即刻定位指针归一（2026-10-21 报障修复：原实现只在 OnPointerMove 更新归一——
            // 鼠标放上去不动=无 move 事件=倾斜恒 0=「无反应」；enter 哪侧哪侧先斜，一动就有效果）
            UpdatePointerNorm(eventData.position);
            _hovered = true;
            Raise();
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            TrackPointer(eventData);
            if (!_hovered || _suspended) return;
            UpdatePointerNorm(eventData.position);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            // 悬停解除后由 Update 平滑回正；这里不立刻还原（硬切=廉价感）
            _hovered = false;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            TrackPointer(eventData);
            if (_suspended) return;
            _pressed = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _pressed = false;
        }

        private void TrackPointer(PointerEventData eventData)
        {
            _lastPointerScreen = eventData.position;
            _lastPointerCamera = eventData.enterEventCamera;
            _pointerSeen = true;
            if (_hovered) UpdatePointerNorm(eventData.position);
        }

        /// <summary>屏幕点换算本 rect 归一化 [0,1]（倾斜/光泽的目标值单源）</summary>
        private void UpdatePointerNorm(Vector2 screenPos)
        {
            var rt = Rt;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screenPos, _lastPointerCamera, out var local))
                return;
            Rect rect = rt.rect;
            if (rect.width <= 0f || rect.height <= 0f) return;
            _pointerNorm = new Vector2(
                Mathf.Clamp01((local.x - rect.xMin) / rect.width),
                Mathf.Clamp01((local.y - rect.yMin) / rect.height));
        }

        // ==================== 让位协议（拖拽系统调用） ====================

        /// <summary>立即还原基准并停更（拖拽系统占用 transform 前 MUST 先调本方法再捕获 baseScale）</summary>
        public void Suspend()
        {
            _suspended = true;
            RestoreImmediate();
        }

        /// <summary>恢复驱动；按组件内最后指针位置重臂悬停（无参版本——拖拽结束指针仍在卡上时不丢悬停态）</summary>
        public void Resume()
        {
            _suspended = false;
            if (!_pointerSeen) return;
            var rt = Rt;
            if (rt == null) return;
            if (RectTransformUtility.RectangleContainsScreenPoint(rt, _lastPointerScreen, _lastPointerCamera))
            {
                UpdatePointerNorm(_lastPointerScreen);
                _hovered = true;
                Raise();
            }
        }

        // ==================== 透视 Warp 接线（返修九） ====================

        private readonly List<HoverTiltWarp> _warps = new List<HoverTiltWarp>();

        /// <summary>悬停期懒接线：给宿主子树每个 Graphic（含 TMP 文本/遮罩模板图）挂一枚 Warp。
        /// 重复进入=GetComponent 查重跳过（池化复用零重复）；光泽层=CreateSheen 直接登记。</summary>
        private void EnsureWarps()
        {
            _warps.Clear();
            var graphics = GetComponentsInChildren<Graphic>(true);
            foreach (var g in graphics)
            {
                if (g == null) continue;
                var w = g.GetComponent<HoverTiltWarp>();
                if (w == null)
                {
                    w = g.gameObject.AddComponent<HoverTiltWarp>();
                    w.Driver = this;
                }
                else if (w.Driver == null) w.Driver = this;
                _warps.Add(w);
            }
        }

        /// <summary>倾斜角写入后必调：Warp 网格按当下 transform 重施（平置/关透视=回写原始）。</summary>
        private void MarkWarpsDirty()
        {
            for (int i = 0; i < _warps.Count; i++)
            {
                var w = _warps[i];
                if (w != null) w.MarkDirty();
            }
        }

        // ==================== 每帧驱动 ====================

        private void Update()
        {
            if (_suspended) return;

            // 返修五：悬停期逐帧轮询活体指针位置重算归一——事件驱动的 OnPointerMove 存在不派发窗口
            // （Tuanjie BaseInputModule 魔改版 move 派发=同悬停目标+IsPointerMoving 双门槛，用户实测
            // 倾斜冻结在 enter 位置/按钮进中心恒零倾角）——轮询把指针位置变成 Update 单源：静止/移动/
            // 进入位置全部恒跟手（vanilla-tilt.js 同哲学；mousePresent 门控=移动端零扰动走事件）
            if (_hovered && Input.mousePresent)
            {
                Vector2 live = Input.mousePosition;
                if ((live - _lastPointerScreen).sqrMagnitude > 0.0001f)
                {
                    _lastPointerScreen = live;
                    UpdatePointerNorm(live);
                }
            }

            bool scaleActive = 启用缩放;
            bool busy = _hovered
                || Mathf.Abs(_tiltX) > 0.001f || Mathf.Abs(_tiltY) > 0.001f
                || (scaleActive && Mathf.Abs(_scaleMul - 1f) > 0.001f)
                || _sheenAlpha > 0.002f;
            if (!busy) return; // 空闲：不写 transform（所有权在其它系统手里）

            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;
            float blend = 1f - Mathf.Exp(-dt * Mathf.Max(1f, _hovered ? 平滑速率 : 回正速率));

            // 倾斜目标（返修六翻向）：Ry·Rx 手算实证 Euler(+tx,+ty) 把 (−sign(ty),+sign(tx)) 角压向屏内
            // （观看者在 −Z，正面朝 −Z）——原 (mx−0.5)/(0.5−my) 符号组合实际压光标**对角**=朝向背离鼠标；
            // 用户约定「朝向鼠标=光标侧压下」→ 两轴翻符号。正交投影下翻向前后的投影恒同（纯对称缩短、
            // 且与对角悬停逐顶点全同——返修九根因），方向差异由 HoverTiltWarp 透视位移承载可见
            float tx = 0f, ty = 0f;
            if (_hovered)
            {
                ty = (0.5f - _pointerNorm.x) * 2f * 最大倾角;
                tx = (_pointerNorm.y - 0.5f) * 2f * 最大倾角;
                if (启用微呼吸 && 微呼吸幅度 > 0f)
                {
                    float t = Time.unscaledTime;
                    float br = Mathf.Sin(t * 微呼吸频率) + Mathf.Sin(t * 微呼吸频率 * 0.63f + 2.1f) * 0.5f;
                    tx += br * 微呼吸幅度;
                    ty += Mathf.Sin(t * 微呼吸频率 * 0.71f + 1.3f) * 微呼吸幅度 * 0.6f;
                }
            }
            _tiltX = Mathf.Lerp(_tiltX, tx, blend);
            _tiltY = Mathf.Lerp(_tiltY, ty, blend);

            // 缩放目标（通道关闭的宿主跳过——缩放所有权在拖拽系统）
            if (scaleActive)
            {
                float scaleTarget = _hovered ? (_pressed ? 按压缩放 : 提起缩放) : 1f;
                _scaleMul = Mathf.Lerp(_scaleMul, scaleTarget, blend);
            }

            // 光泽目标（按压时减淡；通道同受悬停强度平滑）
            float sheenTarget = (_hovered && 启用光泽 && 光泽强度 > 0f)
                ? 光泽强度 * (_pressed ? 0.55f : 1f)
                : 0f;
            _sheenAlpha = Mathf.Lerp(_sheenAlpha, sheenTarget, blend);
            UpdateSheen();

            // 未悬停且全通道落定 → 精确还原 + 停写
            if (!_hovered
                && Mathf.Abs(_tiltX) < 0.01f && Mathf.Abs(_tiltY) < 0.01f
                && (!scaleActive || Mathf.Abs(_scaleMul - 1f) < 0.001f)
                && _sheenAlpha < 0.004f)
            {
                RestoreImmediate();
                return;
            }

            var rt2 = Rt;
            if (rt2 == null) return;
            rt2.localRotation = _baseRotation * Quaternion.Euler(_tiltX, _tiltY, 0f);
            if (scaleActive) rt2.localScale = _baseScale * _scaleMul;
            MarkWarpsDirty(); // 旋转写入后 Warp 必重施（透视位移场随姿态变）
        }

        /// <summary>立即精确还原基准（OnDisable/让位/落定三路共用）；缩放通道关闭的宿主不碰 localScale</summary>
        private void RestoreImmediate()
        {
            _hovered = false;
            _pressed = false;
            _tiltX = 0f;
            _tiltY = 0f;
            _scaleMul = 1f;
            _sheenAlpha = 0f;
            if (!_hasBase) return;
            var rt = Rt;
            if (rt == null) return;
            rt.localRotation = _baseRotation;
            if (启用缩放) rt.localScale = _baseScale;
            UpdateSheen();
            RestoreTopCanvas();
            MarkWarpsDirty(); // 还原基准后 Warp 按原始回写（池化/让位/落定三路共用本方法）
        }

        // ==================== 渲染置顶（嵌套 Canvas overrideSorting） ====================

        /// <summary>悬停渲染置顶：嵌套 Canvas overrideSorting——**不动 sibling 序**（布局组按 sibling 分格位，
        /// SetAsLastSibling=全网格跳位〔2026-10-21 背包全卡跳变根因〕）；sortingOrder 压过同画布普通元素即可。
        /// 成本=悬停期间该卡拆独立 draw call（单卡悬停可接受）。
        /// 防重护栏（2026-10-21 返修二实锤）：Destroy 延迟到帧末——同帧 restore→再 raise 的窗口内
        /// AddComponent 报 already added 返回 null，继续写 overrideSorting 必 NRE，且 NRE 炸断 UGUI
        /// 当帧整条指针处理链（悬停+点击全灭）；已有 Canvas（authored 分组画布/pending 残留）只借用不新建。
        /// 返修四自愈：借用的残留若是禁用态孤儿画布=启用+补射线器（销毁顺序雷/域重载残留防复发）</summary>
        private void Raise()
        {
            EnsureWarps(); // 透视 Warp 懒接线先于置顶早退——任何宿主悬停即接线
            if (!悬停置顶) return;
            if (_topCanvas != null) return; // 已持有（Unity 假 null：pending-destroy 引用销毁后自动放行）
            var existing = GetComponent<Canvas>();
            if (existing != null)
            {
                // authored 宿主画布或上一轮 pending-destroy 残留：借用引用防重复 Add；
                // authored 画布不动其排序（尊重宿主）；pending 残留随帧末销毁自然消失。
                // 返修四自愈①：残留可能是禁用态孤儿（历史销毁被拒/播放中编译域重载丢追踪）——
                // 禁用画布=子树整棵不渲染，悬停全程隐形（「无效果+按钮消失」现场形态）——借用即启用。
                _topCanvas = existing;
                _ownedTopCanvas = false;
                _enabledBorrowedCanvas = !existing.enabled;
                if (!existing.enabled) existing.enabled = true;
                // 返修四自愈②：嵌套画布=独立射线域（返修三结论）——借用已有画布同样必须保证域内
                // 有 GraphicRaycaster，否则该宿主 hover+click 全死；缺则补建（owned 登记，还原时移除）
                var rc = GetComponent<GraphicRaycaster>();
                if (rc != null)
                {
                    _topRaycaster = rc;
                    _ownedTopRaycaster = false;
                }
                else
                {
                    _topRaycaster = gameObject.AddComponent<GraphicRaycaster>();
                    _ownedTopRaycaster = true;
                }
                return;
            }
            _topCanvas = gameObject.AddComponent<Canvas>();
            _ownedTopCanvas = true;
            _topCanvas.overrideSorting = true;
            _topCanvas.sortingOrder = 悬停置顶层级;
            // 返修三实锤：嵌套画布=新射线域——父画布的 GraphicRaycaster 只扫自己域内图形，
            // 不补射线器=悬停瞬间 raycast 失效→exit→衰减完还原→再命中→enter→~3Hz 抬起-衰减闪烁循环，
            // 且循环期点击 press 大概率落在失效相=点击死；必须同挂 GraphicRaycaster 保射线
            _topRaycaster = gameObject.AddComponent<GraphicRaycaster>();
            _ownedTopRaycaster = true;
            _enabledBorrowedCanvas = false;
        }

        private void RestoreTopCanvas()
        {
            // 返修四（销毁顺序雷实锤）：同帧销毁队列按调用序处理——GraphicRaycaster 的
            // RequireComponent(typeof(Canvas)) 会让「先删画布」被引擎拒绝移除
            // （console "Can't remove Canvas because GraphicRaycaster depends on it"），
            // 画布残留为禁用态=子树整棵不渲染（「悬停移开按钮消失」根因，2026-10-22 合成悬停复现实锤）。
            // 必须先销毁依赖方（射线器）再销毁被依赖方（画布）。
            if (_ownedTopCanvas)
            {
                if (_topRaycaster != null) Destroy(_topRaycaster);
                if (_topCanvas != null) Destroy(_topCanvas);
            }
            else
            {
                if (_topRaycaster != null && _ownedTopRaycaster) Destroy(_topRaycaster);
                if (_topCanvas != null && _enabledBorrowedCanvas) _topCanvas.enabled = false;
            }
            _topCanvas = null;
            _topRaycaster = null;
            _ownedTopCanvas = false;
            _ownedTopRaycaster = false;
            _enabledBorrowedCanvas = false;
        }

        // ==================== 光泽跟随（UI/CardLightBand shader 复用） ====================

        private void UpdateSheen()
        {
            if (_sheenAlpha <= 0.003f)
            {
                if (_sheen != null && _sheen.gameObject.activeSelf)
                    _sheen.gameObject.SetActive(false);
                return;
            }
            if (_sheen == null && !CreateSheen()) return;
            if (!_sheen.gameObject.activeSelf) _sheen.gameObject.SetActive(true);
            // 对角投影（shader 内 proj=(uv.x+uv.y)*0.5）——光泽带中心钉在光标处
            _sheenMaterial.SetFloat("_Progress", (_pointerNorm.x + _pointerNorm.y) * 0.5f);
            _sheen.color = new Color(1f, 1f, 1f, _sheenAlpha);
        }

        private bool CreateSheen()
        {
            if (BandShader == null)
            {
                GICLog.Warn("[HoverTiltEffect] 找不到 Shader: UI/CardLightBand——光泽跟随不可用（倾斜/缩放不受影响）");
                return false;
            }
            var go = new GameObject("HoverSheen", typeof(RectTransform), typeof(CanvasRenderer));
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.SetAsLastSibling(); // 恒在卡面内容之上
            _sheen = go.AddComponent<HoverSheenGraphic>();
            _sheen.color = Color.white;
            _sheen.raycastTarget = false;
            _sheenMaterial = new Material(BandShader);
            _sheenMaterial.SetColor("_GlowColor", 光泽颜色);
            _sheenMaterial.SetFloat("_BandWidth", 光泽宽度);
            _sheenMaterial.SetFloat("_Progress", 0.5f);
            _sheen.material = _sheenMaterial;
            _sheen.SetAllDirty();
            // 返修九：光泽层同步 Warp（与卡面同场形变，光泽恒贴卡面）
            var sheenWarp = go.AddComponent<HoverTiltWarp>();
            sheenWarp.Driver = this;
            _warps.Add(sheenWarp);
            return true;
        }

        private void DestroySheen()
        {
            if (_sheen != null)
            {
                Destroy(_sheen.gameObject);
                _sheen = null;
            }
            if (_sheenMaterial != null)
            {
                Destroy(_sheenMaterial);
                _sheenMaterial = null;
            }
        }

        /// <summary>全幅 quad 光泽载体（不依赖 Sprite；CardLightBandEffect.LightBandGraphic 同款 mesh 形态，
        /// 独立私有拷贝避免 GIC.UI → GIC.Battle 的反向依赖）</summary>
        private class HoverSheenGraphic : Graphic
        {
            protected override void OnPopulateMesh(VertexHelper vh)
            {
                vh.Clear();
                Rect rect = rectTransform.rect;
                float x0 = rect.xMin, y0 = rect.yMin, x1 = rect.xMax, y1 = rect.yMax;
                vh.AddVert(new Vector3(x0, y0), color, new Vector2(0, 0));
                vh.AddVert(new Vector3(x0, y1), color, new Vector2(0, 1));
                vh.AddVert(new Vector3(x1, y1), color, new Vector2(1, 1));
                vh.AddVert(new Vector3(x1, y0), color, new Vector2(1, 0));
                vh.AddTriangle(0, 1, 2);
                vh.AddTriangle(0, 2, 3);
            }
        }
    }
}
