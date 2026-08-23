using System;
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

        [Header("缩放")]
        [Tooltip("滚轮缩放派蒙大小（光标命中模型时生效，与拖拽一致）")] [SerializeField] private bool 允许滚轮缩放 = true;
        [Tooltip("缩放倍率下限")] [SerializeField] private float 缩放最小 = 0.4f;
        [Tooltip("缩放倍率上限")] [SerializeField] private float 缩放最大 = 2f;
        [Tooltip("初始缩放倍率（Start 时应用一次，窗口同步调整，构图全程恒定）")] [SerializeField] private float 初始缩放倍率 = 0.7f;
        [Tooltip("每格滚轮的缩放步进（乘法），越小越精细")] [SerializeField] private float 缩放步进 = 1.05f;
        [Tooltip("窗口尺寸随缩放同步扩大（防模型被窗口截断），以派蒙中心为锚点")] [SerializeField] private bool 窗口随缩放 = true;
        [Tooltip("缩放=1 时的窗口逻辑宽度（96 DPI 基准像素；物理尺寸=逻辑×dpi/96，对齐主流桌宠 DPI 感知）")] [SerializeField] private int 窗口逻辑宽 = 480;
        [Tooltip("缩放=1 时的窗口逻辑高度（96 DPI 基准像素）")] [SerializeField] private int 窗口逻辑高 = 720;

        [Header("调试")]
        [SerializeField] private bool 打印状态日志 = false;

        private Camera cam;
        // 2026-08-23 像素级命中：MeshCollider 动态烘焙蒙皮网格，替代胶囊链（头冠/脚 100% 贴合、右下零空气）。
        // BakeMesh(useScale=true) 输出在 SMR 局部空间（骨骼空间），命中节点须与 SMR 同 transform。
        private SkinnedMeshRenderer 蒙皮渲染器;
        private MeshCollider 命中网格碰撞体;
        private Mesh 烘焙网格;
        private float 上次烘焙时间 = -10f;
        [Tooltip("蒙皮网格重烘间隔秒（低频即可，呼吸/裙摆微动不需要逐帧）")] [SerializeField] private float 烘焙间隔 = 0.15f;
        private IntPtr hwnd = IntPtr.Zero;
        private bool restyled;
        private bool dragging;
        private Vector2Int dragGrabOffset;
        private bool passThroughOn;
        private float lastClickTime = -10f; // 双击退出判定：上次有效单击时刻
        private bool prevLmbDown;
        private Vector2Int dragStartCursor; // 拖拽起点（区分单击与真实拖动）
        private float 当前缩放 = 1f; // 滚轮缩放倍率（叠乘到 Paimon 根 localScale）
        private float 初始缩放;       // Start 时 Paimon 根 localScale.x（场景基准值）
        private Transform _paimon根;
        private int 基准窗口宽, 基准窗口高; // 缩放=1 时的窗口客户区物理像素（=逻辑尺寸×dpi/96，窗口缩放的基准）
        private float dpi缩放 = 1f;        // GetDpiForWindow/96（exe 清单 PerMonitorV2：客户区物理像素=渲染像素）
        private float 有效缩放最大 = 2f;    // 钳制到工作区后的实际上限（RestyleWindow 时重算）
        // WH_MOUSE_LL 钩子截 WM_MOUSEWHEEL（Input.mouseScrollDelta 在窗口穿透/无焦点时常返回 0）
        private IntPtr _mouseHook = IntPtr.Zero;
        private HookProc _mouseHookProc; // 防 GC 回收委托
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
            Application.targetFrameRate = 目标帧率;
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
                // 初始缩放倍率启动时应用一次（相机不动，只改模型缩放；窗口尺寸在 RestyleWindow→DockBottomRight 按同一倍率同步，构图恒定）
                if (!Mathf.Approximately(初始缩放倍率, 1f))
                {
                    当前缩放 = 初始缩放倍率;
                    float s = 初始缩放 * 当前缩放;
                    _paimon根.localScale = new Vector3(s, s, s);
                }
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
            当前缩放 = Mathf.Clamp(当前缩放, 缩放最小, 有效缩放最大);
            // 钳制后重放模型缩放（极端小屏：初始倍率超上限时保持模型与窗口同步）
            if (_paimon根 != null)
            {
                float s = 初始缩放 * 当前缩放;
                _paimon根.localScale = new Vector3(s, s, s);
            }

            if (启动时停靠右下角)
            {
                DockBottomRight();
            }

            // 挂低级鼠标钩子截滚轮（窗口穿透/无焦点时 Input.mouseScrollDelta 不可靠）
            _mouseHookProc = MouseHookCallback;
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseHookProc, GetModuleHandle(null), 0);
            if (_mouseHook == IntPtr.Zero)
            {
                Debug.LogWarning("[PetSpike] 鼠标钩子安装失败，滚轮缩放退回 Input.mouseScrollDelta");
            }

            restyled = true;
            Debug.Log($"[PetSpike] 窗口改造完成 hwnd=0x{hwnd.ToInt64():X} mode={(使用DWM透明 ? "DWM-alpha" : $"colorKey=0x{key:X6}")} render={Screen.width}x{Screen.height} dpi={dpi缩放:F2} baseClient={基准窗口宽}x{基准窗口高} scale={当前缩放:F2} maxScale={有效缩放最大:F2}");
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

        /// <summary>停靠右下角：客户区尺寸=基准×当前缩放（初始缩放启动即应用，构图全程恒定），边距按 DPI 换算</summary>
        private void DockBottomRight()
        {
            GetFrameSize(out int frameW, out int frameH, out _, out _);
            int winW = Mathf.RoundToInt(基准窗口宽 * 当前缩放) + frameW;
            int winH = Mathf.RoundToInt(基准窗口高 * 当前缩放) + frameH;
            int margin = Mathf.RoundToInt(停靠边距 * dpi缩放);
            SystemParametersInfo(SPI_GETWORKAREA, 0, out RECT work, 0);
            int x = work.Right - winW - margin;
            int y = work.Bottom - winH - margin;
            SetWindowPos(hwnd, IntPtr.Zero, x, y, winW, winH, SWP_NOZORDER | SWP_SHOWWINDOW);
        }

        /// <summary>WH_MOUSE_LL 回调：截 WM_MOUSEWHEEL 的 delta（高位 short），写入待消费队列</summary>
        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam.ToInt32() == WM_MOUSEWHEEL)
            {
                var data = (MSLLHOOKSTRUCT)System.Runtime.InteropServices.Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                // mouseData 高位 = wheel delta（120 一格，正=向前/上）
                short delta = (short)((data.mouseData >> 16) & 0xFFFF);
                // 累加而非覆盖：高分辨率滚轮/触控板一帧内可发多个小 delta，覆盖会丢导致手感发涩
                System.Threading.Interlocked.Add(ref _pendingWheelDelta, delta);
            }
            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        void OnDestroy()
        {
            if (_mouseHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
        }

        /// <summary>
        /// 窗口随缩放同步调整（主流桌宠：窗口=模型包围盒，任何倍率构图恒定）。
        /// 相机不动（透视构图不变），模型缩放与窗口客户区尺寸 1:1 同步，不截断也不过大。
        /// 锚点=客户区中心（派蒙身体中心，2026-08-23 用户拍板）——她绕自身中心长个儿/缩小，不随光标漂移。
        /// 编辑器内不会被调用（restyled 恒 false），无需平台守卫。
        /// </summary>
        private void 应用缩放窗口()
        {
            if (hwnd == IntPtr.Zero) return;

            GetFrameSize(out int frameW, out int frameH, out int frameLeft, out int frameTop);
            GetClientRect(hwnd, out RECT cr);
            int clientW = cr.Right - cr.Left;
            int clientH = cr.Bottom - cr.Top;
            var origin = new POINT { X = 0, Y = 0 };
            ClientToScreen(hwnd, ref origin);

            // 中心锚点：客户区中心（=模型视觉中心）的屏幕物理坐标
            float anchorX = origin.X + clientW * 0.5f;
            float anchorY = origin.Y + clientH * 0.5f;

            int newClientW = Mathf.RoundToInt(基准窗口宽 * 当前缩放);
            int newClientH = Mathf.RoundToInt(基准窗口高 * 当前缩放);
            int newW = newClientW + frameW;
            int newH = newClientH + frameH;
            int newX = Mathf.RoundToInt(anchorX - newClientW * 0.5f) - frameLeft;
            int newY = Mathf.RoundToInt(anchorY - newClientH) - frameTop;

            // 钳制到屏幕工作区（不吞任务栏；拖到屏幕下方放大时窗口会被推回屏内，派蒙上移——
            // 用户拍板 2026-08-23：宁可上移也不让派蒙出屏看不见）
            SystemParametersInfo(SPI_GETWORKAREA, 0, out RECT work, 0);
            newX = Mathf.Clamp(newX, work.Left, work.Right - newW);
            newY = Mathf.Clamp(newY, work.Top, work.Bottom - newH);

            SetWindowPos(hwnd, IntPtr.Zero, newX, newY, newW, newH, SWP_NOZORDER | SWP_SHOWWINDOW);
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

            // 低频重烘蒙皮网格（跟随呼吸/裙摆/姿势变化；11k 顶点 0.15s 一次开销可忽略）
            if (蒙皮渲染器 != null && 命中网格碰撞体 != null && Time.unscaledTime - 上次烘焙时间 >= 烘焙间隔)
            {
                蒙皮渲染器.BakeMesh(烘焙网格, true);
                命中网格碰撞体.sharedMesh = null; // 强制碰撞体刷新
                命中网格碰撞体.sharedMesh = 烘焙网格;
                上次烘焙时间 = Time.unscaledTime;
            }

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
                    float 新缩放 = Mathf.Clamp(当前缩放 * Mathf.Pow(缩放步进, scroll), 缩放最小, 有效缩放最大);
                    if (!Mathf.Approximately(新缩放, 当前缩放))
                    {
                        当前缩放 = 新缩放;
                        float s = 初始缩放 * 当前缩放;
                        _paimon根.localScale = new Vector3(s, s, s);
                        if (窗口随缩放)
                        {
                            应用缩放窗口();
                        }
                    }
                }
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
        }
    }
}
