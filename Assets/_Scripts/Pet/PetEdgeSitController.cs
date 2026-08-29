using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using static GIC.Pet.PetWin32; // Win32 声明集中在 PetWin32（2026-08-27 抽取去重），调用点免限定
using UnityEngine.Serialization;

namespace GIC.Pet
{
    /// <summary>
    /// 边缘坐控制器（2026-08-27，用户需求"派蒙坐在屏幕或窗口的横框上"；2026-08-28 补窗口下边框）：
    /// 拖拽松手时探测附近"横框"（窗口顶边=标题栏上沿 / 窗口底边=坐下沿身体在窗前腿垂窗下 /
    /// 任务栏顶=工作区底）→ 磁吸贴合播坐姿动画
    /// （SitUpright 合成直坐；SitLoop 原版定性"生病坐姿"已弃用，2026-08-27）；
    /// 坐定后跟随锚定窗口移动（窗口拖走/改尺寸她贴着走）；锚定窗口最小化/关闭/隐藏 → 沿 eSheep 语义
    /// 重力掉落，途中可落在更低窗口顶边，兜底落任务栏顶，全程无缝衔接回坐姿。
    ///
    /// 机制源流（2026-08-27 网检一手源码，gic-pet skill 铁律 11）：
    /// - eSheep FormPet.cs（Adrianotiger/desktopPet，C#）：FallDetect=EnumWindows 遍历可见+有标题栏窗口，
    ///   落点判定"精灵底边自上而下跨越窗口顶边且水平在窗口范围（半宽容差）"；FollowWindow=窗口矩形
    ///   变化时按水平比例重定位、垂直直跟；窗口失效（GetWindowRect 失败/归零）→ 掉落。
    /// - 本实现按固定画布体制重写：移动的是窗口本身（SetWindowPos），模型在画布内照常演——与
    ///   PetWindowController"窗口唯一移动源"清单一致（本组件=第④个显式移动源：边缘坐交互，
    ///   非动画副作用）。接触基准=骨盆投影（TryGet接触点屏幕位置，2026-08-27 目检纠正：坐姿接触线
    ///   是屁股不是脚），贴合目标=可见帧顶边。
    ///
    /// 设计决策（2026-08-27 V1）：
    /// ①松手触发采用"磁吸窗"而非 Shimeji 式"必掉落"——VPet（无重力派）松手即停 + 本项目已拍板
    ///   "松手即停原地"（2026-08-26 拖拽终案）不能推翻：仅在松手位置落在横框附近（上 120px/下 60px，
    ///   2026-08-27 目检调大后终值）时磁吸落座，其余位置维持原地站立（既有行为零变化）。
    /// ②窗口消失的掉落是 eSheep 语义（她坐着的东西没了必然下落）：重力加速+逐帧跨越检测+途中窗口
    ///   顶边可接住+任务栏顶兜底，永不无限下落（工作区底恒存在）。
    /// ③窗口顶边取 DWM 可见帧（DWMWA_EXTENDED_FRAME_BOUNDS）而非 GetWindowRect——Win10/11 DWM 窗口
    ///   GetWindowRect 含 7px 不可见阴影边，直接贴会"悬空 7px"；DWM 帧才是肉眼可见边框（主流桌宠
    ///   Desktop Mate/VPet 同款做法）。取不到时回退 GetWindowRect。
    /// ④窗口枚举过滤=eSheep 标准（可见+标题非空+非自身）+现代补充（排除最小化/WS_EX_TOOLWINDOW/
    ///   DWM cloaked 挂起窗口/最小尺寸），坐不上幽灵窗口。
    /// ⑤掉落/坐定期间暂停命中烘焙（姿势在变），坐定 2s 后一次性补烘坐姿碰撞体（既有机制自动接管）。
    ///
    /// 行为层接线：PetBehaviorController 物理收口（无放下反应档）先问 评估吸附并坐()，true=本组件接管
    /// （播坐姿动画），false=原逻辑回待机；坐定中行为层的"待机动作"动态切换为坐姿；坐定期间
    /// 打招呼/随机小动作一切单次触发全压制（2026-08-27 用户拍板：坐下就纯坐）。被拖拽=立即解除坐定。
    /// 判定/贴合基准=骨盆（屁股）投影（2026-08-27 目检纠正：脚线判定腿沉入窗下）；松手时是站立/
    /// Drag01 姿势、骨盆高于坐姿——落座后 坐定贴正秒 内逐帧把骨盆钉在窗顶（姿势过渡中骨盆渐降=
    /// 平滑落座观感；坐姿动画骨盆曲线近恒定（范围&lt;0.05），钉住后窗口稳定不抖）。
    /// </summary>
    public class PetEdgeSitController : MonoBehaviour
    {
        [Header("引用")]
        [InspectorName("窗口控制器")]
        [SerializeField] private PetWindowController winController;
        [InspectorName("动作播放器")]
        [SerializeField] private PetAnimSwapper animPlayer;

        [Header("动作")]
        [InspectorName("坐姿动作名")]
        [Tooltip("坐姿动作（loop；情绪映射无匹配=默认脸）")] [SerializeField] private string sitAnimName = "Ani_NPC_Kanban_Paimon_SitUpright";
        [InspectorName("坐定贴正秒")]
        [Tooltip("落座后逐帧贴正骨盆的时长（秒）：松手时站立姿势骨盆高于坐姿，坐姿动画过渡期（惯性化 0.6s+余量）骨盆渐降，期间逐帧钉在窗顶=平滑落座观感。坐姿骨盆曲线近恒定，钉住后窗口稳定")] [SerializeField] private float sitSettleSec = 1.2f;
        [InspectorName("站立动作名")]
        [Tooltip("坐姿下滚轮缩放时切回的站立动作（缩放露馅修正：模型绕脚底长高，骨盆钉窗顶的窗口不动→屁股浮离窗沿；缩放稳定后按锚点重新落座）")] [SerializeField] private string standAnimName = "Ani_NPC_Kanban_Paimon_Standby";

        [Header("磁吸判定（松手位置探测）")]
        [InspectorName("上吸附范围像素")]
        [Tooltip("横框上方多少物理像素内松手算坐得上（从上往下掉几个像素落座，磁吸直落；120=2026-08-27 目检终值）")] [SerializeField] private float snapRangeTopPx = 120f;
        [InspectorName("下吸附范围像素")]
        [Tooltip("横框下方多少物理像素内松手算坐得上（拖到横框下方区域如标题栏内松手即落座；60=2026-08-27 目检终值）")] [SerializeField] private float snapRangeBottomPx = 60f;
        [InspectorName("水平容差像素")]
        [Tooltip("水平容差：接触点超出窗口左右边多少物理像素内仍算坐得上（≈模型半宽+余量；160=2026-08-27 目检终值）")] [SerializeField] private float hTolerancePx = 160f;
        [InspectorName("最小窗口尺寸")]
        [Tooltip("可坐窗口的最小宽高（物理像素），滤掉小悬浮窗/提示条")] [SerializeField] private Vector2 minWinSize = new Vector2(240f, 160f);

        [Header("掉落（锚定窗口消失时）")]
        [InspectorName("重力加速度")]
        [Tooltip("重力加速度（屏幕物理像素/秒²）——观感值：掉 500px 约 0.6s")] [SerializeField] private float gravity = 2800f;
        [InspectorName("最大下落速度")]
        [Tooltip("终端下落速度上限（物理像素/秒），防跨屏长掉落过快")] [SerializeField] private float maxFallSpeed = 2800f;

        [Header("调试")]
        [InspectorName("打印状态日志")]
        [SerializeField] private bool printStateLog = false;

        private enum EdgeSitState { none, IsFalling, Sit, scaleAdjusting }
        private EdgeSitState state = EdgeSitState.none;

        /// <summary>坐定中的锚定窗口句柄；IntPtr.Zero=屏幕锚定（任务栏顶/工作区底，永不失效）</summary>
        private IntPtr anchorHwnd = IntPtr.Zero;
        private bool anchoredBottom;             // true=锚定窗口底边（坐下沿：跟随/贴正取 r.Bottom）；false=顶边（取 r.Top）——2026-08-28 补
        private RECT anchorRect;              // 坐定时的锚定窗口可见帧（跟随变化检测）
        private float anchorRatioX;           // 接触x相对窗口宽的比例（eSheep FollowWindow 同款）
        private float contactX, contactY;         // 接触线屏幕坐标（物理像素，虚拟桌面系）
        private float fallSpeed;
        private float settleUntil = -10f; // 坐定后 坐定贴正秒 内逐帧贴正骨盆（站→坐姿势过渡补偿）
        private float _上次缩放 = -1f;      // 坐定中监听的目标缩放（缩放露馅修正入口；-1=未初始化）
        private float _scaleStableAt = -10f;  // 缩放过渡结束后的重新落座时刻（<0=未起算）
        private float lastEnumAt = -10f;
        private float lastCloakCheckAt = -10f; // 坐定期 cloaked 低频检查节流
        private readonly List<CandidateEdge> candidates = new List<CandidateEdge>(32);
        private readonly StringBuilder titleBuf = new StringBuilder(256);
        private EnumWindowsProc enumProc;    // 防 GC（枚举期间回调被引用即可，字段保底）

        private struct CandidateEdge { public IntPtr hwnd; public RECT rect; }

        /// <summary>坐定中（行为层：待机动作切换为坐姿）</summary>
        public bool IsSeated => state == EdgeSitState.Sit;
        /// <summary>掉落中（行为层：压制打招呼/小动作新触发）</summary>
        public bool IsFalling => state == EdgeSitState.IsFalling;
        /// <summary>坐姿动作名（行为层动态待机用）</summary>
        public string SitAnim => sitAnimName;

        // Win32 互操作（DllImport/结构体/常量）集中在 PetWin32 —— 见文件头 using static

        /// <summary>拖拽松手（物理收口，无放下反应档）时由行为层调用：探测附近横框并磁吸落座。
        /// true=已接管（已播坐姿）；false=附近无可坐横框，行为层走原回待机逻辑。
        /// 编辑器（窗口未改造）恒 false——桌宠形态仅存在于构建产物。</summary>
        public bool EvaluateSnapAndSit()
        {
            if (winController == null || !winController.IsWindowRestyled) return false;
            if (!winController.TryGet接触点屏幕位置(out Vector2 footBottom)) return false;

            EnumerateEdges();
            CandidateEdge best = default; bool found = false; float 最小距 = float.MaxValue; bool best下边 = false;
            // 屏幕底横框（任务栏顶）同场参评：拖到任务栏上松手=坐任务栏
            int 工作区底 = GetWorkAreaBottomAt(footBottom.x);
            if (InWorkAreaX(footBottom.x) && footBottom.y >= 工作区底 - snapRangeTopPx && footBottom.y <= 工作区底 + snapRangeBottomPx)
            {
                最小距 = Mathf.Abs(footBottom.y - 工作区底);
                found = true;
                best = new CandidateEdge { hwnd = IntPtr.Zero, rect = new RECT { Left = int.MinValue, Top = 工作区底, Right = int.MaxValue, Bottom = int.MaxValue } };
            }
            // 每个候选窗口两条横框参评（2026-08-28 补下边框）：顶边（坐上沿，腿垂窗前）与底边
            // （坐下沿，身体在窗前、腿垂窗下）；磁吸窗=边线上 120px / 下 60px（终值，与 Inspector
            // 默认一致），全体横框（含任务栏顶）里最近者胜
            foreach (var c in candidates)
            {
                if (!ContainsX(footBottom.x, c.rect)) continue;
                float dTop = footBottom.y - c.rect.Top;
                if (dTop >= -snapRangeTopPx && dTop <= snapRangeBottomPx)
                {
                    float d = Mathf.Abs(dTop);
                    if (d < 最小距) { 最小距 = d; found = true; best = c; best下边 = false; }
                }
                float dBottom = footBottom.y - c.rect.Bottom;
                if (dBottom >= -snapRangeTopPx && dBottom <= snapRangeBottomPx)
                {
                    float d = Mathf.Abs(dBottom);
                    if (d < 最小距) { 最小距 = d; found = true; best = c; best下边 = true; }
                }
            }
            if (!found) return false;

            contactX = footBottom.x;
            contactY = best下边 ? best.rect.Bottom : best.rect.Top;
            Sit(best.hwnd, best.rect, best下边);
            return true;
        }

        void Update()
        {
            if (winController == null || !winController.IsWindowRestyled) return;

            // 被拖拽=立即解除一切（物理层接管，行为层播 Drag01）
            if (winController.IsDragging)
            {
                if (state != EdgeSitState.none) Release(false);
                return;
            }

            switch (state)
            {
                case EdgeSitState.IsFalling:
                    FallingFrame(Time.unscaledDeltaTime);
                    break;
                case EdgeSitState.Sit:
                {
                    // 缩放露馅修正（2026-08-27 用户拍板）：模型绕脚底长高而窗口不动，坐姿骨盆会浮离窗沿
                    // （放大上浮/缩小下沉）。坐姿下滚轮改缩放=先切回站立待机，缩放稳定后按锚点重新落座。
                    float scale = winController.TargetScaleValue;
                    if (!Mathf.Approximately(scale, _上次缩放))
                    {
                        _上次缩放 = scale;
                        state = EdgeSitState.scaleAdjusting;
                        _scaleStableAt = -10f;
                        if (animPlayer != null && animPlayer.HasAnim(standAnimName))
                            animPlayer.Play(standAnimName);
                        if (printStateLog) Debug.Log("[PetEdgeSit] 坐姿中缩放：切站立待机，稳定后重新落座");
                        break;
                    }
                    SeatedFrame();
                    break;
                }
                case EdgeSitState.scaleAdjusting:
                {
                    float scale = winController.TargetScaleValue;
                    if (!Mathf.Approximately(scale, _上次缩放)) { _上次缩放 = scale; _scaleStableAt = -10f; } // 连续滚轮：重等稳定
                    if (winController.ScaleTransitioning) { _scaleStableAt = -10f; break; }               // 平滑过渡进行中
                    if (_scaleStableAt < 0f) { _scaleStableAt = Time.unscaledTime + 0.3f; break; } // 刚到位，宽限
                    if (Time.unscaledTime < _scaleStableAt) break;
                    // 稳定：按锚点重新落座（缩放后骨盆高度变了，重新贴窗沿）
                    if (anchorHwnd == IntPtr.Zero)
                    {
                        contactY = GetWorkAreaBottomAt(contactX);
                        Sit(IntPtr.Zero, default, false); // 屏幕锚定（任务栏顶）
                    }
                    else if (IsWindowVisible(anchorHwnd) && !IsIconic(anchorHwnd)
                             && GetVisibleRect(anchorHwnd, out RECT r2) && r2.Width > 0 && r2.Height > 0)
                    {
                        contactX = r2.Left + anchorRatioX * r2.Width;
                        contactY = anchoredBottom ? r2.Bottom : r2.Top;
                        Sit(anchorHwnd, r2, anchoredBottom);
                    }
                    else
                    {
                        // 锚定窗口已消失：磁吸探测兜底，仍无可坐=站立交还行为层（正常待机）
                        anchorHwnd = IntPtr.Zero;
                        state = EdgeSitState.none;
                        EvaluateSnapAndSit();
                    }
                    break;
                }
            }
        }

        /// <summary>坐定维护：贴正窗口（站→坐过渡期逐帧钉骨盆）+窗口锚定=矩形变化跟随（比例重定位）
        /// +失效检测（关闭/最小化/隐藏/cloaked）→失效即掉落；屏幕锚定（任务栏顶）恒稳态只剩贴正。</summary>
        private void SeatedFrame()
        {
            // 落座贴正：松手时是站立/Drag01 姿势（骨盆高），坐姿动画过渡期骨盆渐降——逐帧把骨盆钉在
            // (接触x, 坐落线)。窗口随姿势渐降平滑下移=她"缓缓坐进去"。坐姿骨盆曲线近恒定，
            // 过渡结束后投影偏移恒定，SetWindowPos 输出稳定（取整后亚像素无感）。
            if (Time.unscaledTime < settleUntil)
            {
                winController.SetContactScreenPos(contactX, contactY);
                if (anchorHwnd == IntPtr.Zero) return; // 屏幕锚定：贴正后无事可做
            }

            if (anchorHwnd == IntPtr.Zero) return; // 屏幕锚定：任务栏顶不会消失

            if (!IsWindowVisible(anchorHwnd) || IsIconic(anchorHwnd))
            {
                OnAnchorWindowGone();
                return;
            }
            // cloaked 低频检查（挂起的 UWP/另一虚拟桌面的窗口：可见位仍真但已被 cloak，坐上去=坐空气）
            if (Time.unscaledTime - lastCloakCheckAt >= 0.5f)
            {
                lastCloakCheckAt = Time.unscaledTime;
                if (DwmGetWindowAttribute(anchorHwnd, DWMWA_CLOAKED, out int cloak, 4) == 0 && cloak != 0)
                {
                    OnAnchorWindowGone();
                    return;
                }
            }
            if (!GetVisibleRect(anchorHwnd, out RECT r) || (r.Width <= 0 && r.Height <= 0))
            {
                OnAnchorWindowGone();
                return;
            }
            // 跟随（eSheep FollowWindow）：垂直恒贴锚定边（顶边=r.Top / 底边=r.Bottom），水平按锚定比例
            // 随窗口宽度缩放（窗口改宽她按比例挪）
            if (r.Left != anchorRect.Left || r.Top != anchorRect.Top || r.Right != anchorRect.Right || r.Bottom != anchorRect.Bottom)
            {
                anchorRect = r;
                contactX = r.Left + anchorRatioX * r.Width;
                contactY = anchoredBottom ? r.Bottom : r.Top;
                winController.SetContactScreenPos(contactX, contactY);
                winController.MarkPosDirty();
            }
        }

        /// <summary>掉落帧：重力加速+顶边跨越检测（防隧穿：比较上一帧/本帧脚底 y 夹住顶边即停）+
        /// 任务栏顶兜底接住；期间 0.15s 重枚举（窗口在动）。掉落中姿势保持当前动画不变。</summary>
        private void FallingFrame(float dt)
        {
            fallSpeed = Mathf.Min(fallSpeed + gravity * dt, maxFallSpeed);
            float lastY = contactY;
            contactY += fallSpeed * dt;

            if (Time.unscaledTime - lastEnumAt >= 0.15f) EnumerateEdges();

            // 跨越检测：所有"上帧在上、本帧到达/越过"的顶边里取最高的（eSheep FallDetect 语义）
            CandidateEdge crossed = default; bool stop = false; int topMost = int.MaxValue;
            foreach (var c in candidates)
            {
                if (!ContainsX(contactX, c.rect)) continue;
                if (lastY < c.rect.Top && contactY >= c.rect.Top && c.rect.Top < topMost)
                {
                    topMost = c.rect.Top; crossed = c; stop = true;
                }
            }
            if (stop)
            {
                contactY = crossed.rect.Top;
                Sit(crossed.hwnd, crossed.rect, false); // 下落只能落在上方表面（顶边）——从上往下跨过底边前必先跨过同窗顶边
                return;
            }
            // 任务栏顶/屏幕底兜底：跨越或已越界都接住（后者覆盖"锚点本就在工作区底以下"的奇态）
            int 工作区底 = GetWorkAreaBottomAt(contactX);
            if (contactY >= 工作区底)
            {
                contactY = 工作区底;
                Sit(IntPtr.Zero, default, false); // 屏幕锚定
                return;
            }
            winController.SetContactScreenPos(contactX, contactY);
        }

        /// <summary>落座：贴合横框+播坐姿+登记锚点。hwnd=Zero 是屏幕锚定（任务栏顶，恒顶边）；
        /// 下边=true 锚定窗口底边（坐下沿：身体在窗前、腿垂窗下——2026-08-28 补）。</summary>
        private void Sit(IntPtr hwnd, RECT rect, bool bottom)
        {
            state = EdgeSitState.Sit;
            anchorHwnd = hwnd;
            anchorRect = rect;
            anchoredBottom = bottom && hwnd != IntPtr.Zero;
            anchorRatioX = rect.Width > 0 ? (contactX - rect.Left) / rect.Width : 0.5f;
            winController.SetContactScreenPos(contactX, contactY);
            winController.PauseHitBaking = false; // 解除暂停→2s 宽限后补烘坐姿碰撞体（既有一次性机制）
            winController.MarkPosDirty();
            settleUntil = Time.unscaledTime + sitSettleSec;
            _上次缩放 = winController.TargetScaleValue; // 防陈旧值误触发缩放重坐（坐下时同步当前倍率）
            if (animPlayer != null && animPlayer.HasAnim(sitAnimName))
                animPlayer.Play(sitAnimName);
            if (printStateLog)
                Debug.Log($"[PetEdgeSit] 坐定 {(anchoredBottom ? "底边" : "顶边")} hwnd={(hwnd == IntPtr.Zero ? "屏幕(任务栏顶)" : $"0x{hwnd.ToInt64():X}")} 脚底=({contactX:F0},{contactY:F0}) 比例={anchorRatioX:F2}");
        }

        /// <summary>锚定窗口失效：原地开始掉落（eSheep 语义——坐着的东西没了）。
        /// 掉落中保持当前动画（坐姿悬空下坠），落到哪坐哪。</summary>
        private void OnAnchorWindowGone()
        {
            if (printStateLog) Debug.Log("[PetEdgeSit] 锚定窗口失效，开始掉落");
            BeginFall();
        }

        private void BeginFall()
        {
            state = EdgeSitState.IsFalling;
            fallSpeed = 0f;
            if (!winController.TryGet接触点屏幕位置(out Vector2 footBottom)) { Release(true); return; }
            contactX = footBottom.x;
            contactY = footBottom.y;
            EnumerateEdges();
            winController.PauseHitBaking = true; // 掉落期姿势未稳，停烘；落座解除
        }

        /// <summary>解除边坐状态（被拖拽/异常兜底）。播动画交行为层/物理层既有流程。</summary>
        private void Release(bool restoreBaking)
        {
            state = EdgeSitState.none;
            anchorHwnd = IntPtr.Zero;
            if (restoreBaking) winController.PauseHitBaking = false;
        }

        #region windowEnumGeometry

        /// <summary>枚举可坐横框：eSheep FallDetect 标准（可见+标题非空+非自身）+现代过滤
        /// （非最小化/非工具窗/非 cloaked/最小尺寸）。矩形用 DWM 可见帧（Win10/11 阴影边修正）。</summary>
        private void EnumerateEdges()
        {
            candidates.Clear();
            lastEnumAt = Time.unscaledTime;
            enumProc = enumProc ?? CollectWindow;
            EnumWindows(enumProc, winController.WindowHandle);
        }

        private bool CollectWindow(IntPtr hWnd, IntPtr lParam)
        {
            if (hWnd == lParam) return true;                       // 自身窗口
            if (!IsWindowVisible(hWnd)) return true;
            if (IsIconic(hWnd)) return true;                       // 最小化
            titleBuf.Clear();
            if (GetWindowText(hWnd, titleBuf, titleBuf.Capacity) == 0) return true; // 无标题=工具/悬浮窗
            if ((GetWindowLong(hWnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) return true;
            if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloak, 4) == 0 && cloak != 0) return true; // 挂起/别的桌面
            if (!GetVisibleRect(hWnd, out RECT r)) return true;
            if (r.Width < minWinSize.x || r.Height < minWinSize.y) return true;
            candidates.Add(new CandidateEdge { hwnd = hWnd, rect = r });
            return true;
        }

        /// <summary>DWM 可见帧矩形（不含 Win10/11 不可见阴影边——直接贴 GetWindowRect 会悬空 ~7px）；
        /// DWM 查询失败（老窗口）回退 GetWindowRect。</summary>
        private static bool GetVisibleRect(IntPtr hWnd, out RECT rect)
        {
            if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, 16) == 0) return true;
            return GetWindowRect(hWnd, out rect);
        }

        private bool ContainsX(float x, RECT r)
        {
            return x >= r.Left - hTolerancePx && x <= r.Right + hTolerancePx;
        }

        private bool InWorkAreaX(float x)
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            var pt = new POINT { X = Mathf.RoundToInt(x), Y = 0 };
            IntPtr mon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            if (mon == IntPtr.Zero) return false;
            return GetMonitorInfoW(mon, ref mi) && x >= mi.rcWork.Left - hTolerancePx && x <= mi.rcWork.Right + hTolerancePx;
        }

        /// <summary>脚底所在显示器的工作区底（=任务栏顶/屏幕底，物理像素）——掉落兜底地面。
        /// 显示器取脚底当前位置最近的（脚底可能在两屏间，掉落中 X 不动所以地面恒定）。</summary>
        private int GetWorkAreaBottomAt(float x)
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            var pt = new POINT { X = Mathf.RoundToInt(x), Y = 0 };
            IntPtr mon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            if (mon == IntPtr.Zero || !GetMonitorInfoW(mon, ref mi)) return Screen.currentResolution.height; // 兜底：主屏高
            return mi.rcWork.Bottom;
        }

        #endregion
    }
}
