using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace GIC.Pet
{
    /// <summary>
    /// 边缘坐控制器（2026-08-27，用户需求"派蒙坐在屏幕或窗口的横框上"）：
    /// 拖拽松手时探测附近"横框"（窗口顶边=标题栏上沿 / 任务栏顶=工作区底）→ 磁吸贴合播 SitLoop 坐姿；
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
    ///   "松手即停原地"（2026-08-26 拖拽终案）不能推翻：仅在松手位置落在横框附近（上 60px/下 36px≈
    ///   标题栏高度）时磁吸落座，其余位置维持原地站立（既有行为零变化）。
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
    /// （播 SitLoop），false=原逻辑回待机；坐定中行为层的"待机动作"动态切换为坐姿；坐定期间
    /// 打招呼/随机小动作一切单次触发全压制（2026-08-27 用户拍板：坐下就纯坐）。被拖拽=立即解除坐定。
    /// 判定/贴合基准=骨盆（屁股）投影（2026-08-27 目检纠正：脚线判定腿沉入窗下）；松手时是站立/
    /// Drag01 姿势、骨盆高于坐姿——落座后 坐定贴正秒 内逐帧把骨盆钉在窗顶（姿势过渡中骨盆渐降=
    /// 平滑落座观感；SitLoop 骨盆曲线近恒定（范围&lt;0.05），钉住后窗口稳定不抖）。
    /// </summary>
    public class PetEdgeSitController : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private PetWindowController 窗口控制器;
        [SerializeField] private PetAnimSwapper 动作播放器;

        [Header("动作")]
        [Tooltip("坐姿动作（loop；情绪映射无匹配=默认脸）")] [SerializeField] private string 坐姿动作名 = "Ani_NPC_Kanban_Paimon_SitUpright";
        [Tooltip("落座后逐帧贴正骨盆的时长（秒）：松手时站立姿势骨盆高于坐姿，SitLoop 过渡期（惯性化 0.6s+余量）骨盆渐降，期间逐帧钉在窗顶=平滑落座观感。SitLoop 骨盆曲线近恒定，钉住后窗口稳定")] [SerializeField] private float 坐定贴正秒 = 1.2f;
        [Tooltip("坐姿下滚轮缩放时切回的站立动作（缩放露馅修正：模型绕脚底长高，骨盆钉窗顶的窗口不动→屁股浮离窗沿；缩放稳定后按锚点重新落座）")] [SerializeField] private string 站立动作名 = "Ani_NPC_Kanban_Paimon_Standby";

        [Header("磁吸判定（松手位置探测）")]
        [Tooltip("横框上方多少物理像素内松手算坐得上（从上往下掉几个像素落座，磁吸直落）")] [SerializeField] private float 上吸附范围像素 = 60f;
        [Tooltip("横框下方多少物理像素内松手算坐得上（≈标题栏高度：拖到标题栏区域内松手即落座）")] [SerializeField] private float 下吸附范围像素 = 36f;
        [Tooltip("水平容差：脚底中心超出窗口左右边多少物理像素内仍算坐得上（≈模型半宽）")] [SerializeField] private float 水平容差像素 = 120f;
        [Tooltip("可坐窗口的最小宽高（物理像素），滤掉小悬浮窗/提示条")] [SerializeField] private Vector2 最小窗口尺寸 = new Vector2(240f, 160f);

        [Header("掉落（锚定窗口消失时）")]
        [Tooltip("重力加速度（屏幕物理像素/秒²）——观感值：掉 500px 约 0.6s")] [SerializeField] private float 重力加速度 = 2800f;
        [Tooltip("终端下落速度上限（物理像素/秒），防跨屏长掉落过快")] [SerializeField] private float 最大下落速度 = 2800f;

        [Header("调试")]
        [SerializeField] private bool 打印状态日志 = false;

        private enum 边坐状态 { 无, 掉落中, 坐定, 缩放调整中 }
        private 边坐状态 状态 = 边坐状态.无;

        /// <summary>坐定中的锚定窗口句柄；IntPtr.Zero=屏幕锚定（任务栏顶/工作区底，永不失效）</summary>
        private IntPtr 锚定窗口 = IntPtr.Zero;
        private RECT 锚定矩形;              // 坐定时的锚定窗口可见帧（跟随变化检测）
        private float 水平锚定比;           // 接触x相对窗口宽的比例（eSheep FollowWindow 同款）
        private float 接触x, 接触y;         // 接触线屏幕坐标（物理像素，虚拟桌面系）
        private float 下落速度;
        private float 贴正截止时刻 = -10f; // 坐定后 坐定贴正秒 内逐帧贴正骨盆（站→坐姿势过渡补偿）
        private float _上次缩放 = -1f;      // 坐定中监听的目标缩放（缩放露馅修正入口；-1=未初始化）
        private float _缩放稳定时刻 = -10f;  // 缩放过渡结束后的重新落座时刻（<0=未起算）
        private float 上次枚举时刻 = -10f;
        private float 上次隐身检查时刻 = -10f; // 坐定期 cloaked 低频检查节流
        private readonly List<候选边> 候选 = new List<候选边>(32);
        private readonly StringBuilder 标题缓存 = new StringBuilder(256);
        private EnumWindowsProc 枚举回调;    // 防 GC（枚举期间回调被引用即可，字段保底）

        private struct 候选边 { public IntPtr hwnd; public RECT rect; }

        /// <summary>坐定中（行为层：待机动作切换为坐姿）</summary>
        public bool 坐定中 => 状态 == 边坐状态.坐定;
        /// <summary>掉落中（行为层：压制打招呼/小动作新触发）</summary>
        public bool 掉落中 => 状态 == 边坐状态.掉落中;
        /// <summary>坐姿动作名（行为层动态待机用）</summary>
        public string 坐姿动作 => 坐姿动作名;

        #region Win32

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left; public int Top; public int Right; public int Bottom; public int 宽 => Right - Left; public int 高 => Bottom - Top; }
        [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public int dwFlags; }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;  // DWM 可见帧（Win10/11 不含阴影边）
        private const int DWMWA_CLOAKED = 14;                // 挂起/虚拟桌面隐身窗口
        private const uint MONITOR_DEFAULTTONEAREST = 2;

        #endregion

        /// <summary>拖拽松手（物理收口，无放下反应档）时由行为层调用：探测附近横框并磁吸落座。
        /// true=已接管（已播坐姿）；false=附近无可坐横框，行为层走原回待机逻辑。
        /// 编辑器（窗口未改造）恒 false——桌宠形态仅存在于构建产物。</summary>
        public bool 评估吸附并坐()
        {
            if (窗口控制器 == null || !窗口控制器.窗口已改造) return false;
            if (!窗口控制器.TryGet接触点屏幕位置(out Vector2 脚底)) return false;

            枚举候选边();
            候选边 best = default; bool 找到 = false; float 最小距 = float.MaxValue;
            // 屏幕底横框（任务栏顶）同场参评：拖到任务栏上松手=坐任务栏
            int 工作区底 = 取脚底工作区底(脚底.x);
            if (水平在工作区内(脚底.x) && 脚底.y >= 工作区底 - 上吸附范围像素 && 脚底.y <= 工作区底 + 下吸附范围像素)
            {
                最小距 = Mathf.Abs(脚底.y - 工作区底);
                找到 = true;
                best = new 候选边 { hwnd = IntPtr.Zero, rect = new RECT { Left = int.MinValue, Top = 工作区底, Right = int.MaxValue, Bottom = int.MaxValue } };
            }
            foreach (var c in 候选)
            {
                if (!水平含(脚底.x, c.rect)) continue;
                float d = 脚底.y - c.rect.Top;
                if (d < -上吸附范围像素 || d > 下吸附范围像素) continue; // 磁吸窗：上 60px / 下 36px
                d = Mathf.Abs(d);
                if (d < 最小距) { 最小距 = d; 找到 = true; best = c; }
            }
            if (!找到) return false;

            接触x = 脚底.x;
            接触y = best.rect.Top;
            坐定(best.hwnd, best.rect);
            return true;
        }

        void Update()
        {
            if (窗口控制器 == null || !窗口控制器.窗口已改造) return;

            // 被拖拽=立即解除一切（物理层接管，行为层播 Drag01）
            if (窗口控制器.正在拖拽)
            {
                if (状态 != 边坐状态.无) 解除(false);
                return;
            }

            switch (状态)
            {
                case 边坐状态.掉落中:
                    掉落帧(Time.unscaledDeltaTime);
                    break;
                case 边坐状态.坐定:
                {
                    // 缩放露馅修正（2026-08-27 用户拍板）：模型绕脚底长高而窗口不动，坐姿骨盆会浮离窗沿
                    // （放大上浮/缩小下沉）。坐姿下滚轮改缩放=先切回站立待机，缩放稳定后按锚点重新落座。
                    float 缩放 = 窗口控制器.目标缩放值;
                    if (!Mathf.Approximately(缩放, _上次缩放))
                    {
                        _上次缩放 = 缩放;
                        状态 = 边坐状态.缩放调整中;
                        _缩放稳定时刻 = -10f;
                        if (动作播放器 != null && 动作播放器.动作存在(站立动作名))
                            动作播放器.Play(站立动作名);
                        if (打印状态日志) Debug.Log("[PetEdgeSit] 坐姿中缩放：切站立待机，稳定后重新落座");
                        break;
                    }
                    坐定帧();
                    break;
                }
                case 边坐状态.缩放调整中:
                {
                    float 缩放 = 窗口控制器.目标缩放值;
                    if (!Mathf.Approximately(缩放, _上次缩放)) { _上次缩放 = 缩放; _缩放稳定时刻 = -10f; } // 连续滚轮：重等稳定
                    if (窗口控制器.缩放过渡中) { _缩放稳定时刻 = -10f; break; }               // 平滑过渡进行中
                    if (_缩放稳定时刻 < 0f) { _缩放稳定时刻 = Time.unscaledTime + 0.3f; break; } // 刚到位，宽限
                    if (Time.unscaledTime < _缩放稳定时刻) break;
                    // 稳定：按锚点重新落座（缩放后骨盆高度变了，重新贴窗沿）
                    if (锚定窗口 == IntPtr.Zero)
                    {
                        接触y = 取脚底工作区底(接触x);
                        坐定(IntPtr.Zero, default); // 屏幕锚定（任务栏顶）
                    }
                    else if (IsWindowVisible(锚定窗口) && !IsIconic(锚定窗口)
                             && 取可见矩形(锚定窗口, out RECT r2) && r2.宽 > 0 && r2.高 > 0)
                    {
                        接触x = r2.Left + 水平锚定比 * r2.宽;
                        接触y = r2.Top;
                        坐定(锚定窗口, r2);
                    }
                    else
                    {
                        // 锚定窗口已消失：磁吸探测兜底，仍无可坐=站立交还行为层（正常待机）
                        锚定窗口 = IntPtr.Zero;
                        状态 = 边坐状态.无;
                        评估吸附并坐();
                    }
                    break;
                }
            }
        }

        /// <summary>坐定维护：贴正窗口（站→坐过渡期逐帧钉骨盆）+窗口锚定=矩形变化跟随（比例重定位）
        /// +失效检测（关闭/最小化/隐藏/cloaked）→失效即掉落；屏幕锚定（任务栏顶）恒稳态只剩贴正。</summary>
        private void 坐定帧()
        {
            // 落座贴正：松手时是站立/Drag01 姿势（骨盆高），SitLoop 过渡期骨盆渐降——逐帧把骨盆钉在
            // (接触x, 坐落线)。窗口随姿势渐降平滑下移=她"缓缓坐进去"。SitLoop 骨盆曲线近恒定，
            // 过渡结束后投影偏移恒定，SetWindowPos 输出稳定（取整后亚像素无感）。
            if (Time.unscaledTime < 贴正截止时刻)
            {
                窗口控制器.设置接触点屏幕位置(接触x, 接触y);
                if (锚定窗口 == IntPtr.Zero) return; // 屏幕锚定：贴正后无事可做
            }

            if (锚定窗口 == IntPtr.Zero) return; // 屏幕锚定：任务栏顶不会消失

            if (!IsWindowVisible(锚定窗口) || IsIconic(锚定窗口))
            {
                窗口失效掉落();
                return;
            }
            // cloaked 低频检查（挂起的 UWP/另一虚拟桌面的窗口：可见位仍真但已被 cloak，坐上去=坐空气）
            if (Time.unscaledTime - 上次隐身检查时刻 >= 0.5f)
            {
                上次隐身检查时刻 = Time.unscaledTime;
                if (DwmGetWindowAttribute(锚定窗口, DWMWA_CLOAKED, out int cloak, 4) == 0 && cloak != 0)
                {
                    窗口失效掉落();
                    return;
                }
            }
            if (!取可见矩形(锚定窗口, out RECT r) || (r.宽 <= 0 && r.高 <= 0))
            {
                窗口失效掉落();
                return;
            }
            // 跟随（eSheep FollowWindow）：垂直恒贴顶边，水平按锚定比例随窗口宽度缩放（窗口改宽她按比例挪）
            if (r.Left != 锚定矩形.Left || r.Top != 锚定矩形.Top || r.Right != 锚定矩形.Right || r.Bottom != 锚定矩形.Bottom)
            {
                锚定矩形 = r;
                接触x = r.Left + 水平锚定比 * r.宽;
                接触y = r.Top;
                窗口控制器.设置接触点屏幕位置(接触x, 接触y);
                窗口控制器.标记位置待写入();
            }
        }

        /// <summary>掉落帧：重力加速+顶边跨越检测（防隧穿：比较上一帧/本帧脚底 y 夹住顶边即停）+
        /// 任务栏顶兜底接住；期间 0.15s 重枚举（窗口在动）。掉落中姿势保持当前动画不变。</summary>
        private void 掉落帧(float dt)
        {
            下落速度 = Mathf.Min(下落速度 + 重力加速度 * dt, 最大下落速度);
            float 上次y = 接触y;
            接触y += 下落速度 * dt;

            if (Time.unscaledTime - 上次枚举时刻 >= 0.15f) 枚举候选边();

            // 跨越检测：所有"上帧在上、本帧到达/越过"的顶边里取最高的（eSheep FallDetect 语义）
            候选边 crossed = default; bool 停 = false; int 最高顶 = int.MaxValue;
            foreach (var c in 候选)
            {
                if (!水平含(接触x, c.rect)) continue;
                if (上次y < c.rect.Top && 接触y >= c.rect.Top && c.rect.Top < 最高顶)
                {
                    最高顶 = c.rect.Top; crossed = c; 停 = true;
                }
            }
            if (停)
            {
                接触y = crossed.rect.Top;
                坐定(crossed.hwnd, crossed.rect);
                return;
            }
            // 任务栏顶/屏幕底兜底：跨越或已越界都接住（后者覆盖"锚点本就在工作区底以下"的奇态）
            int 工作区底 = 取脚底工作区底(接触x);
            if (接触y >= 工作区底)
            {
                接触y = 工作区底;
                坐定(IntPtr.Zero, default); // 屏幕锚定
                return;
            }
            窗口控制器.设置接触点屏幕位置(接触x, 接触y);
        }

        /// <summary>落座：贴合顶边+播坐姿+登记锚点。hwnd=Zero 是屏幕锚定（任务栏顶）。</summary>
        private void 坐定(IntPtr hwnd, RECT rect)
        {
            状态 = 边坐状态.坐定;
            锚定窗口 = hwnd;
            锚定矩形 = rect;
            水平锚定比 = rect.宽 > 0 ? (接触x - rect.Left) / rect.宽 : 0.5f;
            窗口控制器.设置接触点屏幕位置(接触x, 接触y);
            窗口控制器.暂停命中烘焙 = false; // 解除暂停→2s 宽限后补烘坐姿碰撞体（既有一次性机制）
            窗口控制器.标记位置待写入();
            贴正截止时刻 = Time.unscaledTime + 坐定贴正秒;
            _上次缩放 = 窗口控制器.目标缩放值; // 防陈旧值误触发缩放重坐（坐下时同步当前倍率）
            if (动作播放器 != null && 动作播放器.动作存在(坐姿动作名))
                动作播放器.Play(坐姿动作名);
            if (打印状态日志)
                Debug.Log($"[PetEdgeSit] 坐定 hwnd={(hwnd == IntPtr.Zero ? "屏幕(任务栏顶)" : $"0x{hwnd.ToInt64():X}")} 脚底=({接触x:F0},{接触y:F0}) 比例={水平锚定比:F2}");
        }

        /// <summary>锚定窗口失效：原地开始掉落（eSheep 语义——坐着的东西没了）。
        /// 掉落中保持当前动画（坐姿悬空下坠），落到哪坐哪。</summary>
        private void 窗口失效掉落()
        {
            if (打印状态日志) Debug.Log("[PetEdgeSit] 锚定窗口失效，开始掉落");
            开始掉落();
        }

        private void 开始掉落()
        {
            状态 = 边坐状态.掉落中;
            下落速度 = 0f;
            if (!窗口控制器.TryGet接触点屏幕位置(out Vector2 脚底)) { 解除(true); return; }
            接触x = 脚底.x;
            接触y = 脚底.y;
            枚举候选边();
            窗口控制器.暂停命中烘焙 = true; // 掉落期姿势未稳，停烘；落座解除
        }

        /// <summary>解除边坐状态（被拖拽/异常兜底）。播动画交行为层/物理层既有流程。</summary>
        private void 解除(bool 恢复烘焙)
        {
            状态 = 边坐状态.无;
            锚定窗口 = IntPtr.Zero;
            if (恢复烘焙) 窗口控制器.暂停命中烘焙 = false;
        }

        #region 窗口枚举与几何

        /// <summary>枚举可坐横框：eSheep FallDetect 标准（可见+标题非空+非自身）+现代过滤
        /// （非最小化/非工具窗/非 cloaked/最小尺寸）。矩形用 DWM 可见帧（Win10/11 阴影边修正）。</summary>
        private void 枚举候选边()
        {
            候选.Clear();
            上次枚举时刻 = Time.unscaledTime;
            枚举回调 = 枚举回调 ?? 收集候选窗口;
            EnumWindows(枚举回调, 窗口控制器.窗口句柄);
        }

        private bool 收集候选窗口(IntPtr hWnd, IntPtr lParam)
        {
            if (hWnd == lParam) return true;                       // 自身窗口
            if (!IsWindowVisible(hWnd)) return true;
            if (IsIconic(hWnd)) return true;                       // 最小化
            标题缓存.Clear();
            if (GetWindowText(hWnd, 标题缓存, 标题缓存.Capacity) == 0) return true; // 无标题=工具/悬浮窗
            if ((GetWindowLong(hWnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) return true;
            if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloak, 4) == 0 && cloak != 0) return true; // 挂起/别的桌面
            if (!取可见矩形(hWnd, out RECT r)) return true;
            if (r.宽 < 最小窗口尺寸.x || r.高 < 最小窗口尺寸.y) return true;
            候选.Add(new 候选边 { hwnd = hWnd, rect = r });
            return true;
        }

        /// <summary>DWM 可见帧矩形（不含 Win10/11 不可见阴影边——直接贴 GetWindowRect 会悬空 ~7px）；
        /// DWM 查询失败（老窗口）回退 GetWindowRect。</summary>
        private static bool 取可见矩形(IntPtr hWnd, out RECT rect)
        {
            if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, 16) == 0) return true;
            return GetWindowRect(hWnd, out rect);
        }

        private bool 水平含(float x, RECT r)
        {
            return x >= r.Left - 水平容差像素 && x <= r.Right + 水平容差像素;
        }

        private bool 水平在工作区内(float x)
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            var pt = new POINT { X = Mathf.RoundToInt(x), Y = 0 };
            IntPtr mon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            if (mon == IntPtr.Zero) return false;
            return GetMonitorInfoW(mon, ref mi) && x >= mi.rcWork.Left - 水平容差像素 && x <= mi.rcWork.Right + 水平容差像素;
        }

        /// <summary>脚底所在显示器的工作区底（=任务栏顶/屏幕底，物理像素）——掉落兜底地面。
        /// 显示器取脚底当前位置最近的（脚底可能在两屏间，掉落中 X 不动所以地面恒定）。</summary>
        private int 取脚底工作区底(float x)
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
