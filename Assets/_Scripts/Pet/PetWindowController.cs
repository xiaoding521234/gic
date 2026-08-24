using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace GIC.Pet
{
    /// <summary>
    /// 桌宠窗口控制器：Win32 无边框 + 透明 + 置顶 + 右下角停靠 +
    /// 鼠标轮询命中检测动态切换 WS_EX_TRANSPARENT 输入穿透 + 抓住模型拖拽窗口 + 限帧。
    /// 透明双方案（2026-08-21 拍板主流优先）：默认 DWM 逐像素 alpha（DwmExtendFrameIntoClientArea，
    /// 边缘无毛边、支持半透明）；色键 LWA_COLORKEY 保留作兜底开关。
    /// 命中检测/拖拽全部走 Win32 轮询（GetCursorPos/GetAsyncKeyState），不依赖窗口焦点与 Unity 输入系统。
    /// 滚轮缩放（2026-08-24 对齐主流桌宠）：固定窗口（=基准×有效缩放上限，运行期不改尺寸）+
    /// 纯模型缩放平滑过渡（相机/窗口恒定→脚底客户区位置天然锚定，绕脚底原地长高）+ pet.json 持久化（缩放与停留位置）。
    /// </summary>
    public class PetWindowController : MonoBehaviour
    {
        [Header("窗口设置")]
        [SerializeField] private bool 使用DWM透明 = true; // 主流做法；关闭则退回色键（有洋红毛边）
        [SerializeField] private Color 色键颜色 = new Color(1f, 0f, 1f, 1f); // 仅色键兜底模式使用
        [SerializeField] private float 停靠边距 = 24f;
        [SerializeField] private bool 启动时停靠右下角 = true;
        [SerializeField] private bool 允许双击退出 = true; // 派蒙独立存活后的手动关闭方式（后续可换右键菜单）

        [Header("性能")]
        [SerializeField] private int 目标帧率 = 30;
        [Tooltip("垂直同步：0=关（仅用目标帧率限帧，高刷屏上节奏不均会顿挫）/ 1=每个刷新一帧 / 2=隔一个刷新一帧（默认，任何刷新率下节奏均匀）")]
        [SerializeField] private int 垂直同步 = 2;

        [Header("缩放")]
        [Tooltip("滚轮缩放派蒙大小（光标命中模型时生效，与拖拽一致）")] [SerializeField] private bool 允许滚轮缩放 = true;
        [Tooltip("缩放倍率下限")] [SerializeField] private float 缩放最小 = 0.4f;
        [Tooltip("缩放倍率上限")] [SerializeField] private float 缩放最大 = 2f;
        [Tooltip("无 pet.json 存档时的初始缩放倍率（有存档用存档值）")] [SerializeField] private float 初始缩放倍率 = 0.7f;
        [Tooltip("缩放平滑过渡速度：每秒指数趋近速率，越大越跟手；0=瞬达无平滑。对齐主流桌宠滚轮渐变手感")] [SerializeField] private float 缩放平滑速度 = 12f;
        [Tooltip("每格滚轮的缩放步进（乘法），越小越精细")] [SerializeField] private float 缩放步进 = 1.05f;
        [Tooltip("窗口客户区逻辑宽度基准（96 DPI 像素；实际窗口=基准×DPI×有效缩放上限，运行期恒定不随缩放变化——缩放只改模型，杜绝逐帧改窗口的闪烁；含阴影落脚边距，2026-08-24 由 480 扩到 550）")] [SerializeField] private int 窗口逻辑宽 = 550;
        [Tooltip("窗口客户区逻辑高度基准（96 DPI 像素；含阴影落脚边距，2026-08-24 由 720 扩到 825，同步 FOV 40→45.27 保持派蒙像素尺寸不变）")] [SerializeField] private int 窗口逻辑高 = 825;

        [Header("调试")]
        [SerializeField] private bool 打印状态日志 = false;

        private Camera cam;
        // 2026-08-23 像素级命中：MeshCollider 动态烘焙蒙皮网格，替代胶囊链（头冠/脚 100% 贴合、右下零空气）。
        // BakeMesh(useScale=true) 输出在 SMR 局部空间（骨骼空间），命中节点须与 SMR 同 transform。
        private SkinnedMeshRenderer 蒙皮渲染器;
        private MeshCollider 命中网格碰撞体;
        private Mesh 烘焙网格;
        private float 上次烘焙时间 = -10f;
        [Tooltip("蒙皮网格重烘间隔秒（低频即可，呼吸/裙摆微动不需要逐帧）")] [SerializeField] private float 烘焙间隔 = 0.3f;
        /// <summary>暂停命中网格重烘（行为层在单次动作期间置真）——运行时 BakeMesh 每次改写顶点，
        /// MeshCollider 重赋值=PhysX 全量重 cook（实测 15-22ms 主线程尖峰，摆手时每 0.15s 一次=肉眼顿挫，
        /// 2026-08-24 Player.log 烘焙Δ≈dt 实证）。动作中命中精度无关紧要（拖拽退化用旧壳），暂停零副作用。</summary>
        public bool 暂停命中烘焙 { get; set; }
        private IntPtr hwnd = IntPtr.Zero;
        private bool restyled;
        private bool dragging;

        /// <summary>是否正在拖拽派蒙（行为层打断打招呼等触发用）</summary>
        public bool 正在拖拽 => dragging;
        private Vector2Int dragGrabOffset;
        private bool passThroughOn;
        private float lastClickTime = -10f; // 双击退出判定：上次有效单击时刻
        private bool prevLmbDown;
        private Vector2Int dragStartCursor; // 拖拽起点（区分单击与真实拖动）
        private float 目标缩放 = 1f; // 滚轮缩放的目标倍率（持久化存这个值）
        private float 显示缩放 = 1f; // 实际应用倍率（每帧向目标指数平滑趋近）
        private float 初始缩放;       // Start 时 Paimon 根 localScale.x（场景基准值）
        private Transform _paimon根;
        private int 基准窗口宽, 基准窗口高; // 基准客户区物理像素（=逻辑尺寸×dpi/96）
        private int 固定窗口宽, 固定窗口高; // 实际窗口客户区物理像素（=基准×有效缩放上限，运行期恒定不随缩放变化）
        private float dpi缩放 = 1f;        // GetDpiForWindow/96（exe 清单 PerMonitorV2：客户区物理像素=渲染像素）
        private float 有效缩放最大 = 2f;    // 钳制到工作区后的实际上限（RestyleWindow 时重算）

        // 独立存档（桌宠永不读写主存档，docs/19 §3.1/§5.8 约定）：{persistentDataPath}/pet.json
        [Serializable] private class Pet窗口存档
        {
            public int 版本 = 1;
            public float 缩放 = -1f;   // <0 = 无记录
            public int 客户区X, 客户区Y; // 客户区原点（物理像素，虚拟桌面坐标系）
            public bool 有位置 = false;
        }
        private Pet窗口存档 载入存档;
        private float 待写入时刻 = -1f; // >0 = 有未落盘修改（防抖：最后一次修改后 1s 写盘）
        private string 存档路径 => Path.Combine(Application.persistentDataPath, "pet.json");
        // WH_MOUSE_LL 钩子截 WM_MOUSEWHEEL（Input.mouseScrollDelta 在窗口穿透/无焦点时常返回 0）
        private IntPtr _mouseHook = IntPtr.Zero;
        private HookProc _mouseHookProc; // 防 GC 回收委托
        private float _hookKeepUntil = -10f; // 滞回：离开模型 0.5s 后才摘钩
        private static int _pendingWheelDelta; // 钩子线程累加写入，Update 主线程取走清零（120=一格）

        #region Win32

        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, out RECT pvParam, uint fWinIni);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("dwmapi.dll")] private static extern uint DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_SYSMENU = 0x00080000;
        private const int WS_MINIMIZEBOX = 0x00020000;
        private const int WS_MAXIMIZEBOX = 0x00010000;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const uint LWA_COLORKEY = 0x00000001;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint SPI_GETWORKAREA = 0x0030;
        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;
        private const int VK_LBUTTON = 0x01;
        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEWHEEL = 0x020A;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MARGINS { public int cxLeftWidth; public int cxRightWidth; public int cyTopHeight; public int cyBottomHeight; }
        [StructLayout(LayoutKind.Sequential)] private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public IntPtr dwExtraInfo; }

        #endregion

        private void Awake()
        {
#if UNITY_EDITOR
            // 编辑器预览不碰 QualitySettings（运行时改 vSyncCount 退出 Play 不回滚，会污染编辑器）
            _ = 垂直同步; // 字段仅供构建版使用，读一次消 CS0414
            Application.targetFrameRate = 目标帧率;
#else
            // 2026-08-24 顿挫根治：vsync=0 + 30fps 限帧在高刷屏（实测 165Hz）上不整除——
            // 33.3ms 帧周期 / 6.06ms 刷新节拍 = 5.5，每帧显示 5/6 个刷新交替 + 无 vblank 约束的
            // 产出抖动直接透传给 DWM 合成 → 运动节奏忽快忽慢（帧率不低但动作有顿挫感的根因）。
            // vSyncCount=2：每 2 个刷新呈现一帧，任何刷新率下节奏精确均匀（165Hz→82.5fps，60Hz→30fps）。
            // 注意 vSyncCount>0 时 Application.targetFrameRate 被忽略（保留作 vsync=0 时的后备）。
            // 仅宠物进程执行：本组件只在 PaimonPet 场景（--pet-mode 独占），不影响主游戏画质。
            QualitySettings.vSyncCount = Mathf.Clamp(垂直同步, 0, 4);
            if (QualitySettings.vSyncCount == 0)
            {
                Application.targetFrameRate = 目标帧率;
            }
#endif
            Application.runInBackground = true;
        }

        private void Start()
        {
            cam = Camera.main;
            if (cam != null)
            {
                // 透明要求相机输出恒定背景；关 HDR 防浮点缓冲漂移
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.allowHDR = false;
                if (使用DWM透明)
                {
                    // DWM：alpha=0 全透明背景（HUD 之外的像素透出桌面）；MSAA/后处理会破坏 alpha 通道，须关
                    cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                    cam.allowMSAA = false;
                    var urpData = cam.GetUniversalAdditionalCameraData();
                    if (urpData != null) urpData.renderPostProcessing = false;
                }
                else
                {
                    // 色键兜底：背景与抠色完全一致
                    cam.backgroundColor = 色键颜色;
                }
            }
            // 命中链路：Paimon 下的蒙皮渲染器 + 同 transform 的 MeshCollider 节点（动态烘焙）
            var paimonRoot = GameObject.Find("Paimon");
            if (paimonRoot != null)
            {
                _paimon根 = paimonRoot.transform;
                初始缩放 = _paimon根.localScale.x;
                // 缩放目标：构建版读 pet.json（持久化），无存档/编辑器用 Inspector 默认倍率。
                // 启动即到位（无平滑动画），窗口尺寸在 RestyleWindow 按同一倍率同步。
#if !UNITY_EDITOR
                读取存档();
#endif
                目标缩放 = (载入存档 != null && 载入存档.缩放 > 0f) ? 载入存档.缩放 : 初始缩放倍率;
                显示缩放 = 目标缩放;
                应用模型缩放();
                蒙皮渲染器 = paimonRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (蒙皮渲染器 != null)
                {
                    var hitGo = new GameObject("_HitMeshProxy");
                    hitGo.transform.SetParent(蒙皮渲染器.transform.parent, false);
                    hitGo.transform.localPosition = 蒙皮渲染器.transform.localPosition;
                    hitGo.transform.localRotation = 蒙皮渲染器.transform.localRotation;
                    hitGo.transform.localScale = 蒙皮渲染器.transform.localScale;
                    命中网格碰撞体 = hitGo.AddComponent<MeshCollider>();
                    烘焙网格 = new Mesh();
                    蒙皮渲染器.BakeMesh(烘焙网格, true);
                    命中网格碰撞体.sharedMesh = 烘焙网格;
                }
            }
            if (命中网格碰撞体 == null)
            {
                Debug.LogError("[PetSpike] 未找到蒙皮渲染器，命中判定失效");
            }

#if UNITY_EDITOR
            // 编辑器内禁止 Win32 窗口改造——GetActiveWindow 拿到的是编辑器自身窗口，会破坏编辑器 UI。
            // 桌宠形态仅存在于构建产物（主进程自动拉起 / gic.exe --pet-mode）；编辑器 Play 本场景只做模型预览。
            Debug.Log("[PetSpike] 编辑器模式：跳过窗口改造。桌宠由主进程自动拉起（Builds/PetSpike/gic.exe）");
#else
            if (Screen.fullScreen)
            {
                Screen.fullScreen = false;
            }

            hwnd = GetActiveWindow();
            if (hwnd == IntPtr.Zero)
            {
                Debug.LogError("[PetSpike] 未取到窗口句柄，窗口改造失败");
                return;
            }

            RestyleWindow();
#endif
        }

        /// <summary>去掉标题栏边框，透明化（DWM 或色键），置顶并停靠。</summary>
        private void RestyleWindow()
        {
            int style = GetWindowLong(hwnd, GWL_STYLE);
            style &= ~(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
            SetWindowLong(hwnd, GWL_STYLE, style);

            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            ex |= WS_EX_TOOLWINDOW;
            uint key = 0;
            if (使用DWM透明)
            {
                // DWM 逐像素 alpha：玻璃框架扩展到整个客户区，像素按 swapchain alpha 混合桌面。
                // 必须同时挂 WS_EX_LAYERED（原漏挂）：无 LAYERED 的 DWM 窗口逐像素 alpha 行为不可靠
                //（可能整窗"玻璃"或矩形不裁剪），穿透/双击因此失效——2026-08-23 实测踩坑。
                ex |= WS_EX_LAYERED;
                SetWindowLong(hwnd, GWL_EXSTYLE, ex);
                var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref margins);
            }
            else
            {
                ex |= WS_EX_LAYERED;
                SetWindowLong(hwnd, GWL_EXSTYLE, ex);
                // COLORREF 布局 0x00BBGGRR
                key = (uint)((int)(色键颜色.r * 255f)
                          | ((int)(色键颜色.g * 255f) << 8)
                          | ((int)(色键颜色.b * 255f) << 16));
                SetLayeredWindowAttributes(hwnd, key, 0, LWA_COLORKEY);
            }
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);

            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_SHOWWINDOW | SWP_FRAMECHANGED);

            // DPI 换算（主流桌宠做法：任何显示器缩放下派蒙视觉物理大小一致）。
            // exe 清单 PerMonitorV2 → 客户区物理像素=渲染像素；物理尺寸 = 逻辑尺寸 × dpi/96。
            dpi缩放 = GetDpiForWindow(hwnd) / 96f;
            if (dpi缩放 <= 0.01f) dpi缩放 = 1f;
            基准窗口宽 = Mathf.RoundToInt(窗口逻辑宽 * dpi缩放);
            基准窗口高 = Mathf.RoundToInt(窗口逻辑高 * dpi缩放);

            // 有效缩放上限：窗口不超过工作区 95%（防巨大化后被屏幕裁切/吞任务栏）
            SystemParametersInfo(SPI_GETWORKAREA, 0, out RECT work, 0);
            float 限宽 = (work.Right - work.Left) * 0.95f / 基准窗口宽;
            float 限高 = (work.Bottom - work.Top) * 0.95f / 基准窗口高;
            有效缩放最大 = Mathf.Min(缩放最大, 限宽, 限高);
            目标缩放 = Mathf.Clamp(目标缩放, 缩放最小, 有效缩放最大);
            显示缩放 = 目标缩放; // 启动直接到位（无平滑动画）
            // 钳制后重放模型缩放（极端小屏：初始倍率超上限时保持模型与窗口同步）
            应用模型缩放();

            // 固定窗口（2026-08-24 闪烁根治）：客户区尺寸恒=基准×有效缩放上限，运行期不随缩放变化——
            // 滚轮平滑过渡只改模型 localScale，不再逐帧 SetWindowPos 改窗口（逐帧 resize 令 swapchain/DWM
            // 高频重建合成，派蒙肉眼高频闪烁）。窗口 oversized 部分全透明+穿透，无视觉/交互代价；
            // 相机与窗口恒定 → 脚底客户区位置天然恒定（缩放绕脚底原地长高）。
            固定窗口宽 = Mathf.RoundToInt(基准窗口宽 * 有效缩放最大);
            固定窗口高 = Mathf.RoundToInt(基准窗口高 * 有效缩放最大);

            // 位置恢复优先于右下角停靠（拖拽停留位置持久化，docs/19 §3.1）；
            // 无存档且未开停靠则维持 Unity 默认位置
            bool 恢复了位置 = 恢复保存位置();
            if (!恢复了位置 && 启动时停靠右下角)
            {
                DockBottomRight();
            }

            // 鼠标钩子不在此常驻安装——UpdateHookForHit 按命中状态挂/摘（2026-08-24 顿挫优化：
            // 常驻钩子对全系统鼠标消息做封送分配+主线程回调，鼠标移动时灌爆主线程）

            restyled = true;
            Debug.Log($"[PetSpike] 窗口改造完成 hwnd=0x{hwnd.ToInt64():X} mode={(使用DWM透明 ? "DWM-alpha" : $"colorKey=0x{key:X6}")} render={Screen.width}x{Screen.height} dpi={dpi缩放:F2} fixedClient={固定窗口宽}x{固定窗口高} scale={目标缩放:F2} maxScale={有效缩放最大:F2} pos={(恢复了位置 ? "restored" : "dock/default")}");
        }

        /// <summary>窗口矩形与客户区的差值（无边框后理论上≈0，实测兜底；含隐形边框）</summary>
        private void GetFrameSize(out int frameW, out int frameH, out int frameLeft, out int frameTop)
        {
            GetWindowRect(hwnd, out RECT wr);
            GetClientRect(hwnd, out RECT cr);
            var origin = new POINT { X = 0, Y = 0 };
            ClientToScreen(hwnd, ref origin);
            frameLeft = origin.X - wr.Left;
            frameTop = origin.Y - wr.Top;
            frameW = (wr.Right - wr.Left) - (cr.Right - cr.Left);
            frameH = (wr.Bottom - wr.Top) - (cr.Bottom - cr.Top);
        }

        /// <summary>停靠右下角：窗口尺寸恒=固定窗口（不随缩放），边距按 DPI 换算</summary>
        private void DockBottomRight()
        {
            GetFrameSize(out int frameW, out int frameH, out _, out _);
            int winW = 固定窗口宽 + frameW;
            int winH = 固定窗口高 + frameH;
            int margin = Mathf.RoundToInt(停靠边距 * dpi缩放);
            SystemParametersInfo(SPI_GETWORKAREA, 0, out RECT work, 0);
            int x = work.Right - winW - margin;
            int y = work.Bottom - winH - margin;
            SetWindowPos(hwnd, IntPtr.Zero, x, y, winW, winH, SWP_NOZORDER | SWP_SHOWWINDOW);
        }

        /// <summary>
        /// WH_MOUSE_LL 回调：截 WM_MOUSEWHEEL 的 delta（高位 short），写入待消费队列。
        /// 2026-08-24 零分配重写：原 Marshal.PtrToStructure(lParam, typeof(...)) 对每条鼠标消息
        /// 装箱分配+封送（回调又跑在主线程消息泵）——鼠标移动时分配风暴+主线程灌爆，
        /// 是顿挫元凶之一（Player.log HITCH 实测）。改为直接 ReadInt32 读
        /// MSLLHOOKSTRUCT.mouseData（偏移 8），全程零分配零封送。钩子改为"命中模型时才挂"
        /// （滚轮缩放只在命中时消费，光标不在模型上时钩子毫无用途）——平时零开销。
        /// </summary>
        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam.ToInt32() == WM_MOUSEWHEEL)
            {
                // MSLLHOOKSTRUCT(x64)：POINT pt(8B) @0，DWORD mouseData @8——wheel delta=HIWORD
                short delta = (short)(Marshal.ReadInt32(lParam, 8) >> 16);
                // 累加而非覆盖：高分辨率滚轮/触控板一帧内可发多个小 delta，覆盖会丢导致手感发涩
                System.Threading.Interlocked.Add(ref _pendingWheelDelta, delta);
            }
            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        /// <summary>按命中状态挂/摘鼠标钩子（带 0.5s 滞回防边缘抖动）——平时不挂，零开销</summary>
        private void UpdateHookForHit(bool hit)
        {
            if (hit)
            {
                if (_mouseHook == IntPtr.Zero)
                {
                    _mouseHookProc = MouseHookCallback;
                    _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseHookProc, GetModuleHandle(null), 0);
                    if (_mouseHook == IntPtr.Zero)
                        Debug.LogWarning("[PetSpike] 鼠标钩子安装失败，滚轮缩放退回 Input.mouseScrollDelta");
                }
                _hookKeepUntil = Time.unscaledTime + 0.5f;
            }
            else if (_mouseHook != IntPtr.Zero && Time.unscaledTime > _hookKeepUntil)
            {
                UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
        }

        void OnDestroy()
        {
            if (_mouseHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
            写入存档(); // 进程销毁兜底落盘（无待写入则跳过）
        }

        void OnApplicationQuit()
        {
            写入存档(); // 双击退出/正常退出路径兜底（与 OnDestroy 幂等）
        }

        #region 独立存档（pet.json——桌宠永不读写主存档，docs/19 §5.8 双进程约束）

        private void 标记待写入()
        {
            待写入时刻 = Time.unscaledTime + 1f; // 防抖：最后一次修改后 1s 才写盘
        }

        private void 读取存档()
        {
            try
            {
                if (!File.Exists(存档路径)) return;
                载入存档 = JsonUtility.FromJson<Pet窗口存档>(File.ReadAllText(存档路径));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PetSpike] pet.json 读取失败（按无存档处理）：{e.Message}");
                载入存档 = null;
            }
        }

        /// <summary>落盘缩放+停留位置（客户区原点，物理像素）。仅在构建版有待写入时执行，编辑器恒跳过。</summary>
        private void 写入存档()
        {
#if !UNITY_EDITOR
            if (待写入时刻 <= 0f || hwnd == IntPtr.Zero) return;
            try
            {
                var origin = new POINT { X = 0, Y = 0 };
                ClientToScreen(hwnd, ref origin);
                var data = new Pet窗口存档 { 缩放 = 目标缩放, 客户区X = origin.X, 客户区Y = origin.Y, 有位置 = true };
                File.WriteAllText(存档路径, JsonUtility.ToJson(data, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PetSpike] pet.json 写入失败：{e.Message}");
            }
            finally
            {
                待写入时刻 = -1f;
            }
#endif
        }

        #endregion

        /// <summary>把显示缩放叠乘到 Paimon 根 localScale（相机与窗口均不动——脚底屏幕位置恒定，绕脚底原地长高）</summary>
        private void 应用模型缩放()
        {
            if (_paimon根 == null) return;
            float s = 初始缩放 * 显示缩放;
            _paimon根.localScale = new Vector3(s, s, s);
        }

        /// <summary>恢复存档的窗口位置（客户区原点，钳制到虚拟屏幕防显示器拔掉后找不到派蒙）。成功=true。
        /// 窗口尺寸恒=固定窗口（旧存档保存时窗口按旧缩放尺寸，恢复后偏大属预期——停留语义不变）。</summary>
        private bool 恢复保存位置()
        {
            if (载入存档 == null || !载入存档.有位置 || hwnd == IntPtr.Zero) return false;

            GetFrameSize(out int frameW, out int frameH, out int frameLeft, out int frameTop);
            int clientW = 固定窗口宽;
            int clientH = 固定窗口高;

            // 虚拟屏幕矩形（多显示器并集，物理像素）
            int vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);

            // 完整收进虚拟屏幕；范围倒挂（窗口比虚拟屏还大）时居中兜底
            int cx = vw <= clientW ? vx + (vw - clientW) / 2 : Mathf.Clamp(载入存档.客户区X, vx, vx + vw - clientW);
            int cy = vh <= clientH ? vy + (vh - clientH) / 2 : Mathf.Clamp(载入存档.客户区Y, vy, vy + vh - clientH);

            SetWindowPos(hwnd, IntPtr.Zero, cx - frameLeft, cy - frameTop, clientW + frameW, clientH + frameH, SWP_NOZORDER | SWP_SHOWWINDOW);
            return true;
        }

        /// <summary>
        /// 取鼠标光标的 Unity 屏幕坐标（左下原点），供视线跟随等全局追踪使用。
        /// 与命中检测不同：光标在窗口外同样有效（线性外推，ScreenPointToRay 可处理屏外点）。
        /// 构建版走 Win32 全局轮询（窗口无焦点/穿透时也能追踪）；编辑器退回 Input.mousePosition。
        /// </summary>
        public bool TryGetCursorUnityScreenPos(out Vector2 unityScreenPos)
        {
#if UNITY_EDITOR
            unityScreenPos = Input.mousePosition;
            return true;
#else
            unityScreenPos = default;
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }
            GetCursorPos(out POINT pt);
            GetClientRect(hwnd, out RECT cr);
            var origin = new POINT { X = 0, Y = 0 };
            ClientToScreen(hwnd, ref origin);
            int winW = cr.Right - cr.Left;
            int winH = cr.Bottom - cr.Top;
            if (winW <= 0 || winH <= 0)
            {
                return false;
            }
            // 与 Update 命中链路同一套换算：客户区物理像素（=渲染像素，PerMonitorV2）→ Unity 左下原点
            float sx = (pt.X - origin.X) * ((float)Screen.width / winW);
            float sy = (pt.Y - origin.Y) * ((float)Screen.height / winH);
            unityScreenPos = new Vector2(sx, Screen.height - sy);
            return true;
#endif
        }

        private void Update()
        {
            if (!restyled)
            {
                return;
            }

            GetCursorPos(out POINT pt);
            GetWindowRect(hwnd, out RECT wr); // 窗口矩形（含边框）仅供拖拽定位用
            GetClientRect(hwnd, out RECT clientRect);
            var clientOrigin = new POINT { X = 0, Y = 0 };
            ClientToScreen(hwnd, ref clientOrigin);
            int winW = clientRect.Right - clientRect.Left;
            int winH = clientRect.Bottom - clientRect.Top;
            int clientX = pt.X - clientOrigin.X;
            int clientY = pt.Y - clientOrigin.Y;

            bool inWindow = clientX >= 0 && clientX < winW && clientY >= 0 && clientY < winH;
            bool modelHit = false;

            if (inWindow && cam != null && 命中网格碰撞体 != null)
            {
                // 客户区物理像素（=渲染像素，PerMonitorV2）→ Unity 屏幕坐标（左下原点）
                float sx = clientX * ((float)Screen.width / winW);
                float sy = clientY * ((float)Screen.height / winH);
                float unityY = Screen.height - sy;
                Ray ray = cam.ScreenPointToRay(new Vector3(sx, unityY, 0f));
                modelHit = 命中网格碰撞体.Raycast(ray, out _, 100f);
            }

            // 低频重烘蒙皮网格（跟随呼吸/裙摆/姿势变化）。
            // 光标不在窗口内时命中判定恒 false，重烘结果无人消费——跳过（省无谓的烘焙+碰撞体重建）。
            // 单次动作期间暂停（2026-08-24 顿挫根治：烘焙帧=掉帧帧，见 暂停命中烘焙 注释）。
            if (inWindow && !暂停命中烘焙 && 蒙皮渲染器 != null && 命中网格碰撞体 != null && Time.unscaledTime - 上次烘焙时间 >= 烘焙间隔)
            {
                蒙皮渲染器.BakeMesh(烘焙网格, true);
                命中网格碰撞体.sharedMesh = null; // 强制碰撞体刷新
                命中网格碰撞体.sharedMesh = 烘焙网格;
                上次烘焙时间 = Time.unscaledTime;
                PetDiag.上次蒙皮重烘 = Time.unscaledTime; // 顿挫诊断标记（PetFrameStats 回查）
            }

            // 钩子按需挂/摘（2026-08-24：滚轮缩放只在命中模型时消费，常驻钩子平白吃全系统鼠标消息）
            UpdateHookForHit(modelHit && 允许滚轮缩放);

            // 拖拽：全局轮询左键，不依赖焦点；抓住模型后跟随光标移动窗口
            bool lmbDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
            bool lmbPressed = lmbDown && !prevLmbDown;

            // 双击派蒙退出（独立存活的关闭方式）：在拖拽启动前判定，两次命中单击间隔 <0.4s
            // 启动冷却 1s：防进程启动瞬间误吞"上一次双击退出旧进程"的残余按键状态（2026-08-23 实测）
            if (lmbPressed && modelHit && Time.unscaledTime > 1f)
            {
                if (允许双击退出 && Time.unscaledTime - lastClickTime < 0.4f)
                {
                    Debug.Log("[PetSpike] 双击退出，桌宠再见");
                    Application.Quit();
                }
                lastClickTime = Time.unscaledTime;
                dragStartCursor = new Vector2Int(pt.X, pt.Y);
            }

            if (!dragging && modelHit && lmbDown && !prevLmbDown)
            {
                dragging = true;
                dragGrabOffset = new Vector2Int(pt.X - wr.Left, pt.Y - wr.Top);
            }
            if (dragging)
            {
                if (!lmbDown)
                {
                    // 发生过实际位移的拖拽不算单击，清除双击计次防误触退出
                    if (Mathf.Abs(pt.X - dragStartCursor.x) + Mathf.Abs(pt.Y - dragStartCursor.y) > 8)
                    {
                        lastClickTime = -10f;
                    }
                    dragging = false;
                    标记待写入(); // 停留位置持久化（docs/19 §3.1）
                }
                else
                {
                    SetWindowPos(hwnd, IntPtr.Zero, pt.X - dragGrabOffset.x, pt.Y - dragGrabOffset.y, 0, 0, SWP_NOSIZE | SWP_SHOWWINDOW);
                }
            }
            prevLmbDown = lmbDown;

            // 滚轮缩放派蒙大小（WH_MOUSE_LL 钩子截滚轮，穿透/无焦点可靠；
            // 仅当光标命中模型时响应，与拖拽一致——避免滚其他窗口/桌面时误缩放）
            int wheelRaw = System.Threading.Interlocked.Exchange(ref _pendingWheelDelta, 0);
            if (允许滚轮缩放 && modelHit && _paimon根 != null && wheelRaw != 0)
            {
                float scroll = wheelRaw / 120f; // 120=一格，正=向前/上=放大
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    float 新缩放 = Mathf.Clamp(目标缩放 * Mathf.Pow(缩放步进, scroll), 缩放最小, 有效缩放最大);
                    if (!Mathf.Approximately(新缩放, 目标缩放))
                    {
                        // 只改目标倍率：实际应用走下方平滑过渡（对齐主流桌宠滚轮渐变手感）；持久化防抖标记
                        目标缩放 = 新缩放;
                        标记待写入();
                    }
                }
            }

            // 平滑过渡：显示缩放向目标指数趋近，只改模型 localScale——窗口尺寸恒定（2026-08-24 闪烁根治，
            // 见 RestyleWindow 注释），相机/脚底客户区位置不动，天然绕脚底原地长高；拖拽中同样安全
            // （唯一窗口写入源是拖拽本身，模型缩放与其无耦合）。速度=0 时步进=1（瞬达，退回离散行为）。
            if (!Mathf.Approximately(显示缩放, 目标缩放))
            {
                float 步进 = 缩放平滑速度 <= 0f ? 1f : 1f - Mathf.Exp(-Time.unscaledDeltaTime * 缩放平滑速度);
                显示缩放 += (目标缩放 - 显示缩放) * 步进;
                if (Mathf.Abs(目标缩放 - 显示缩放) < 0.0005f) 显示缩放 = 目标缩放;
                应用模型缩放();
            }

            // 命中模型或正在拖拽时可交互，其余区域点击穿透到下层窗口
            bool wantPassThrough = !modelHit && !dragging;
            if (wantPassThrough != passThroughOn)
            {
                int exNow = GetWindowLong(hwnd, GWL_EXSTYLE);
                if (wantPassThrough)
                {
                    exNow |= WS_EX_TRANSPARENT;
                }
                else
                {
                    exNow &= ~WS_EX_TRANSPARENT;
                }
                SetWindowLong(hwnd, GWL_EXSTYLE, exNow);
                passThroughOn = wantPassThrough;
                if (打印状态日志)
                {
                    Debug.Log($"[PetSpike] 穿透切换 -> {wantPassThrough}");
                }
            }

            // 存档防抖落盘（缩放/拖拽后 1s 无新修改才写，连续滚轮不产生 IO 风暴）
            if (待写入时刻 > 0f && Time.unscaledTime >= 待写入时刻)
            {
                写入存档();
            }
        }
    }
}
