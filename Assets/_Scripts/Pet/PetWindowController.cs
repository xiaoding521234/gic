using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace GIC.Pet
{
    /// <summary>
    /// 桌宠窗口控制器：Win32 无边框 + 透明 + 置顶 + 固定小窗跟随 +
    /// 鼠标轮询命中检测动态切换 WS_EX_TRANSPARENT 输入穿透 + 抓住模型拖拽移动 + 限帧。
    /// 透明双方案（2026-08-21 拍板主流优先）：默认 DWM 逐像素 alpha（DwmExtendFrameIntoClientArea，
    /// 边缘无毛边、支持半透明）；色键 LWA_COLORKEY 保留作兜底开关。
    /// 命中检测/拖拽全部走 Win32 轮询（GetCursorPos/GetAsyncKeyState），不依赖窗口焦点与 Unity 输入系统。
    /// 小窗+窗口跟随体制（2026-08-25 定案，对齐 VPet/Desktop Mate 主流）：窗口恒=基准×DPI×缩放上限
    /// 固定尺寸；GI 编排位移驱动模型根自由移动，相机+窗口每帧跟随根增量（模型窗内渲染恒定、
    /// 窗口平移=派蒙屏幕上真实移动）——大幅动作满屏游走不被裁且渲染/合成成本保持小窗水平。
    /// 曾试全屏覆盖体制（同日）：3200×2000@200%DPI 实测帧率腰斩+单核 93%（DWM 整屏合成+fillrate×2.7），
    /// 违背桌宠常驻功耗定位，废弃。**拖拽/动作移动窗口无任何屏边钳制（2026-08-25 拍板）**——
    /// 后续做屏幕边缘交互（下水平边框坐下、上水平+竖直边框探头）；待机机架（相机+窗口）整体
    /// 归位滑回家（只回窗口不回相机会左漂累积）；拖拽=移根+跟随=窗口跟手；pet.json 持久化缩放+窗口原点。
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
        [Tooltip("垂直同步：0=关（仅用目标帧率限帧）/ 1=每个刷新一帧（默认，随屏幕满刷）/ 2=隔一个刷新一帧（165Hz→82.5fps，60Hz→30fps）")]
        [SerializeField] private int 垂直同步 = 1;

        [Header("缩放")]
        [Tooltip("滚轮缩放派蒙大小（光标命中模型时生效，与拖拽一致）")] [SerializeField] private bool 允许滚轮缩放 = true;
        [Tooltip("缩放倍率下限")] [SerializeField] private float 缩放最小 = 0.4f;
        [Tooltip("缩放倍率上限")] [SerializeField] private float 缩放最大 = 2f;
        [Tooltip("无 pet.json 存档时的初始缩放倍率（有存档用存档值）")] [SerializeField] private float 初始缩放倍率 = 0.7f;
        [Tooltip("缩放平滑过渡速度：每秒指数趋近速率，越大越跟手；0=瞬达无平滑。对齐主流桌宠滚轮渐变手感")] [SerializeField] private float 缩放平滑速度 = 12f;
        [Tooltip("每格滚轮的缩放步进（乘法），越小越精细")] [SerializeField] private float 缩放步进 = 1.05f;
        [Tooltip("窗口客户区逻辑宽度基准（96 DPI 像素；实际窗口=基准×DPI×有效缩放上限，运行期恒定不随缩放变化——缩放只改模型，杜绝逐帧改窗口的闪烁；含阴影落脚边距）")] [SerializeField] private int 窗口逻辑宽 = 550;
        [Tooltip("窗口客户区逻辑高度基准（96 DPI 像素；含阴影落脚边距，与 FOV 45.27 配套保持派蒙像素尺寸）")] [SerializeField] private int 窗口逻辑高 = 825;

        [Header("跟随与归位（小窗跟随体制）")]
        [Tooltip("跟随锚点骨名（模型根位移经此骨投影换算窗口平移量）")] [SerializeField] private string 骨盆骨名 = "Bip001 Pelvis";
        [Tooltip("待机时窗口向家位置缓慢滑回的速度（指数阻尼；0=不归位停在动作结束处）")]
        [SerializeField] private float 归位速度 = 1.2f;

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
        private bool passThroughOn;
        private Vector3 _拖拽根起点;        // 拖拽中移动模型根（窗口跟随机制自动把窗口带过去=拖窗视觉）
        private Vector3 _拖拽光标世界起点;   // 拖拽起点光标在模型平面上的世界点
        private float lastClickTime = -10f; // 双击退出判定：上次有效单击时刻
        private PetBehaviorController 行为控制器; // 双击退出的退场动画协作（播 Disappear 后再关进程）
        private bool 已请求退出;              // 退场动画进行中：屏蔽重复双击与新拖拽
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

        // ---- 小窗跟随体制（2026-08-25）----
        private Transform _骨盆;            // 跟随锚点骨（本体骨架，排除影子壳）
        private Vector3 _骨盆基准;           // 上帧骨盆世界位置（每帧跟随后重置）
        private Vector2 _骨盆基准视口;        // 上帧骨盆视口（相机移动后投影，换算窗口平移量）
        private bool _跟随基准有效;           // 跟随期间为真；待机退出跟随置假（重进时重取基准）
        private Vector2Int _家窗口原点;       // 归位目标（客户区原点，物理像素，Win32 屏幕坐标）
        private Vector3 _家相机位置;          // 归位目标（相机位置——机架整体归位，2026-08-25 左漂根治）
        private bool _家已记录;
        private bool _归位完成已标记;         // 归位到位后标记存档（防把中间位置写盘）
        private bool _待记录家;               // 拖拽松手→本帧 LateUpdate 钳制后记录家（位置才是真实落点）

        /// <summary>单次动作进行中（含仪式/退场）：窗口跟随照常（退场飞离也跟随，2026-08-25 拍板），
        /// 但归位暂停——动作期间不往家滑。行为层播单次时置真、回待机置假。</summary>
        public bool 单次动作中 { get; set; }

        // 独立存档（桌宠永不读写主存档，docs/19 §3.1/§5.8 约定）：{persistentDataPath}/pet.json
        // v1（小窗体制，2026-08-25 回归）：缩放 + 窗口客户区原点（物理像素，虚拟桌面坐标系）。
        // v2（全屏体制，已废弃）的锚点字段保留反序列化兼容：读取时换算迁移为窗口原点。
        [Serializable] private class Pet窗口存档
        {
            public int 版本 = 1;
            public float 缩放 = -1f;   // <0 = 无记录
            public int 客户区X, 客户区Y; // 客户区原点（物理像素，虚拟桌面坐标系）
            public bool 有位置 = false;
            // v2 兼容字段（读旧档迁移用）
            public float 锚点X, 锚点Y;
            public bool 有锚点 = false;
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
            // 帧节奏演化史：v0=vsync0+30 限帧（165Hz 上 5.5 不整除→顿挫，弃）→ v1=vSyncCount=2
            // （每 2 刷新一帧节奏均匀，165Hz→82.5fps）→ 2026-08-24 用户拍板改回 vSyncCount=1
            // （每个刷新一帧=屏幕满刷，165Hz→165fps）。vsync=2 时期配套的 v22 C1 切线本就是为
            // "播放帧率>key 密度"设计的插值，更高渲染帧率下依然正确（切线插值密度更高更平滑）。
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
            行为控制器 = FindObjectOfType<PetBehaviorController>();
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
                _骨盆 = 找本体骨(骨盆骨名);
                初始缩放 = _paimon根.localScale.x;
                // 缩放目标：构建版读 pet.json（持久化），无存档/编辑器用 Inspector 默认倍率。
                // 启动即到位（无平滑动画）。
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
            // 高频重建合成，派蒙肉眼高频闪烁）。窗口 oversized 部分全透明+穿透，无视觉/交互代价。
            // 2026-08-25 小窗跟随体制：窗口移动（move-only）很便宜（不触发 swapchain 重建），
            // LateUpdate 每帧平移窗口跟随模型根位移=派蒙满屏游走。
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

        /// <summary>恢复存档窗口位置（客户区原点，钳制到虚拟屏幕防显示器拔掉后找不到派蒙）。成功=true。
        /// v2 锚点档（全屏体制遗留）换算迁移：锚点视口×全屏尺寸-窗口半宽高≈旧窗口原点。</summary>
        private bool 恢复保存位置()
        {
            if (hwnd == IntPtr.Zero) return false;
            int clientW = 固定窗口宽;
            int clientH = 固定窗口高;

            int nx, ny;
            if (载入存档 != null && 载入存档.有位置)
            {
                nx = 载入存档.客户区X;
                ny = 载入存档.客户区Y;
            }
            else if (载入存档 != null && 载入存档.有锚点)
            {
                // v2 迁移：全屏体制的骨盆视口锚点（Unity 左下原点）→ 近似窗口原点
                int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
                int vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
                nx = Mathf.RoundToInt(载入存档.锚点X * vw) - clientW / 2;
                ny = Mathf.RoundToInt((1f - 载入存档.锚点Y) * vh) - clientH / 2;
            }
            else return false;

            // 虚拟屏幕矩形（多显示器并集，物理像素）
            int vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int vw2 = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int vh2 = GetSystemMetrics(SM_CYVIRTUALSCREEN);

            // 完整收进虚拟屏幕；范围倒挂（窗口比虚拟屏还大）时居中兜底
            int cx = vw2 <= clientW ? vx + (vw2 - clientW) / 2 : Mathf.Clamp(nx, vx, vx + vw2 - clientW);
            int cy = vh2 <= clientH ? vy + (vh2 - clientH) / 2 : Mathf.Clamp(ny, vy, vy + vh2 - clientH);

            GetFrameSize(out int frameW, out int frameH, out int frameLeft, out int frameTop);
            SetWindowPos(hwnd, IntPtr.Zero, cx - frameLeft, cy - frameTop, clientW + frameW, clientH + frameH, SWP_NOZORDER | SWP_SHOWWINDOW);
            记录家位置();
            return true;
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
            记录家位置();
        }

        /// <summary>记录家=当前窗口客户区原点+相机位置（待机归位目标；拖拽松手/停靠/恢复时调用）</summary>
        private void 记录家位置()
        {
            if (hwnd == IntPtr.Zero) return;
            var origin = new POINT { X = 0, Y = 0 };
            ClientToScreen(hwnd, ref origin);
            _家窗口原点 = new Vector2Int(origin.X, origin.Y);
            if (cam != null) _家相机位置 = cam.transform.position;
            _家已记录 = true;
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

        /// <summary>落盘缩放+窗口位置（客户区原点）。仅在构建版有待写入时执行，编辑器恒跳过。</summary>
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

        /// <summary>取鼠标光标的 Unity 屏幕坐标（左下原点），供视线跟随等全局追踪使用。
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

        /// <summary>光标屏幕点 → 模型平面（z=Paimon 根平面）上的世界点（拖拽用）。
        /// 相机看向 +Z，与根平面求交稳定；光标在窗口外同样有效（线性外推）。</summary>
        private Vector3 光标模型平面世界点(POINT pt)
        {
            GetClientRect(hwnd, out RECT cr);
            var origin = new POINT { X = 0, Y = 0 };
            ClientToScreen(hwnd, ref origin);
            int winW = Mathf.Max(1, cr.Right - cr.Left);
            int winH = Mathf.Max(1, cr.Bottom - cr.Top);
            float sx = (pt.X - origin.X) * ((float)Screen.width / winW);
            float sy = (pt.Y - origin.Y) * ((float)Screen.height / winH);
            Ray ray = cam.ScreenPointToRay(new Vector3(sx, Screen.height - sy, 0f));
            float planeZ = _paimon根 != null ? _paimon根.position.z : 0f;
            if (Mathf.Abs(ray.direction.z) < 1e-6f) return _paimon根 != null ? _paimon根.position : Vector3.zero;
            float t = (planeZ - ray.origin.z) / ray.direction.z;
            return ray.origin + ray.direction * t;
        }

        /// <summary>
        /// 小窗跟随（2026-08-25 主流体制核心，VPet/Desktop Mate 同款"窗口跟着角色走"）：
        /// **仅单次动作/拖拽期间激活**（大幅编排位移才移窗="派蒙真的在动"语义；退场飞离也跟随）；
        /// 待机时相机+窗口完全静止（呼吸/摇晃微动在窗内呈现，窗边距余量充足——零窗口操作=零 DWM
        /// 重合成，派蒙停靠感稳定）。实现：骨盆 XY 位置投影到基准深度平面与上帧基准视口作差
        /// （同深度比较剔除 Z 位移的透视假平移——Z 只呈现窗内近大远小）→ 相机 XY 平移根增量
        /// （模型窗内渲染恒定不被裁）+ 窗口平移相同屏幕像素量（move-only，不触发 swapchain 重建）。
        /// 之后窗口钳制虚拟屏（模型贴屏边自然停住）；待机窗口向家缓慢滑回（归位）。
        /// </summary>
        private void LateUpdate()
        {
            if (!restyled || hwnd == IntPtr.Zero || _骨盆 == null || cam == null || _paimon根 == null) return;

            // ---- 1) 跟随（仅单次动作/拖拽期间激活，2026-08-25 晚收紧）----
            // 待机（呼吸/摇晃微动）不跟随：相机+窗口完全静止，模型在窗内自由微动（窗边距余量充足）
            // ——窗口零移动=零 DWM 重合成（省电）+ 派蒙停靠感稳定；大幅编排位移/拖拽才移窗。
            bool 跟随中 = 单次动作中 || dragging;
            if (跟随中)
            {
                if (!_跟随基准有效)
                {
                    记录跟随基准(); // 进入跟随首帧：从当前位置起算（不回跳）
                    _跟随基准有效 = true;
                }
                else
                {
                    // 纯 XY 投影差：本帧骨盆 XY 位置投影到基准深度平面再比较——剔除 Z 位移的
                    // 透视假平移（Z 位移只呈现窗内近大远小，不触发窗口移动）
                    var now = _骨盆.position;
                    var vpSame = cam.WorldToViewportPoint(new Vector3(now.x, now.y, _骨盆基准.z));
                    if (vpSame.z > 0f)
                    {
                        float dxVP = vpSame.x - _骨盆基准视口.x;
                        float dyVP = vpSame.y - _骨盆基准视口.y;

                        GetClientRect(hwnd, out RECT cr);
                        int clientW = cr.Right - cr.Left;
                        int clientH = cr.Bottom - cr.Top;
                        if (clientW > 0 && clientH > 0 && (Mathf.Abs(dxVP) > 1e-5f || Mathf.Abs(dyVP) > 1e-5f))
                        {
                            // 相机 XY 平移本帧骨盆世界增量（模型窗内渲染恒定；Z 不跟=纵深呈现大小变化）
                            cam.transform.position += new Vector3(now.x - _骨盆基准.x, now.y - _骨盆基准.y, 0f);

                            // 窗口平移同量像素（视口差 × 客户区物理像素；视口 Y 向上 → Win32 Y 向下取负）
                            int dxPx = Mathf.RoundToInt(dxVP * clientW);
                            int dyPx = -Mathf.RoundToInt(dyVP * clientH);
                            GetWindowRect(hwnd, out RECT wr);
                            SetWindowPos(hwnd, IntPtr.Zero, wr.Left + dxPx, wr.Top + dyPx, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_SHOWWINDOW);
                        }
                    }
                    记录跟随基准(); // 相机移动后投影（含 Z 残留透视=窗内呈现，下帧同深度比较自动剔净）
                }
            }
            else
            {
                _跟随基准有效 = false; // 退出跟随：下次进入时从当前位置重取基准
            }

            // 拖拽刚松手（本帧 Update 已置 dragging=false）：当前位置即家
            if (_待记录家)
            {
                _待记录家 = false;
                记录家位置();
                标记待写入();
            }

            // ---- 待机归位：相机+窗口整体（机架）向家缓慢滑回（动作期/拖拽期暂停）----
            // 相机必须随窗口一起归位（2026-08-25 左漂根治）：动作期机架跟随骨盆位移、
            // 动作结束后 Standby 骨盆曲线值≠动作结束值（CrossFade 混回），若只回窗口不回相机，
            // 模型窗内位置每次动作净偏一段（方向一致）→ 持续漂出窗左边。机架同 k 指数回归，
            // 模型回到窗内原位（动作期屏幕游走语义完整保留）。
            if (!单次动作中 && !dragging && 归位速度 > 0f && _家已记录)
            {
                GetWindowRect(hwnd, out RECT wr2);
                var cur = new Vector2Int(wr2.Left, wr2.Top);
                var delta = _家窗口原点 - cur;
                bool 相机未归 = cam != null && (cam.transform.position - _家相机位置).sqrMagnitude > 1e-8f;
                if (Mathf.Abs(delta.x) > 1 || Mathf.Abs(delta.y) > 1 || 相机未归)
                {
                    float k = 1f - Mathf.Exp(-归位速度 * Time.deltaTime);
                    int nx = Mathf.RoundToInt(cur.x + delta.x * k);
                    int ny = Mathf.RoundToInt(cur.y + delta.y * k);
                    SetWindowPos(hwnd, IntPtr.Zero, nx, ny, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_SHOWWINDOW);
                    if (相机未归) cam.transform.position = Vector3.Lerp(cam.transform.position, _家相机位置, k);
                    _归位完成已标记 = false;
                }
                else if (!_归位完成已标记)
                {
                    _归位完成已标记 = true;
                    标记待写入(); // 归位到位后把家位置落盘（防把中间位置写进档）
                }
            }
        }

        /// <summary>记录跟随基准：骨盆世界位置与视口（下一帧差分的起点）</summary>
        private void 记录跟随基准()
        {
            _骨盆基准 = _骨盆.position;
            var vp = cam.WorldToViewportPoint(_骨盆.position);
            _骨盆基准视口 = new Vector2(vp.x, vp.y);
        }

        /// <summary>取本体骨架上的骨（排除影子壳 _DropShadow / MMD_DropShadow 下的同名骨拷贝）</summary>
        private Transform 找本体骨(string boneName)
        {
            foreach (var t in _paimon根.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != boneName) continue;
                bool 影子下 = false;
                for (var p = t.parent; p != null && !影子下; p = p.parent)
                    if (p.name == "_DropShadow" || p.name == "MMD_DropShadow") 影子下 = true;
                if (影子下) continue;
                return t;
            }
            return null;
        }

        private void Update()
        {
            if (!restyled)
            {
                return;
            }

            GetCursorPos(out POINT pt);
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
                if (允许双击退出 && !已请求退出 && Time.unscaledTime - lastClickTime < 0.4f)
                {
                    Debug.Log("[PetSpike] 双击退出，桌宠再见");
                    已请求退出 = true;
                    // 退场动画（2026-08-24）：先播退场动画再真正退出；无动画可用则立即退出
                    bool 退场接管 = 行为控制器 != null && 行为控制器.请求退场(Application.Quit);
                    if (!退场接管) Application.Quit();
                }
                lastClickTime = Time.unscaledTime;
                dragStartCursor = new Vector2Int(pt.X, pt.Y);
            }

            if (!dragging && !已请求退出 && modelHit && lmbDown && !prevLmbDown)
            {
                dragging = true;
                _拖拽根起点 = _paimon根.position;
                _拖拽光标世界起点 = 光标模型平面世界点(pt);
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
                    _待记录家 = true; // 松手处即家（docs/19 §3.1；家在 LateUpdate 钳制后记录=真实落点）
                }
                else
                {
                    // 拖拽=移动模型根（XY 跟随光标在模型平面上的世界增量）——
                    // LateUpdate 跟随机制自动把窗口带过去（拖根=拖窗的视觉，VPet 同款）。
                    var cur = 光标模型平面世界点(pt);
                    _paimon根.position = _拖拽根起点 + (cur - _拖拽光标世界起点);
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
