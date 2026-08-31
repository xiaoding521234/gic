using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using static GIC.Pet.PetWin32; // Win32 声明集中在 PetWin32（2026-08-27 抽取去重），调用点免限定
using UnityEngine.Serialization;

namespace GIC.Pet
{
    /// <summary>
    /// 桌宠窗口控制器：Win32 无边框 + 透明 + 置顶 + 固定小窗画布 +
    /// 鼠标轮询命中检测动态切换 WS_EX_TRANSPARENT 输入穿透 + 抓住模型物理拖拽（刚体跟随+四肢摆动，2026-08-26）+ 限帧。
    /// 透明双方案（2026-08-21 拍板主流优先）：默认 DWM 逐像素 alpha（DwmExtendFrameIntoClientArea，
    /// 边缘无毛边、支持半透明）；色键 LWA_COLORKEY 保留作兜底开关。
    /// 命中检测/拖拽全部走 Win32 轮询（GetCursorPos/GetAsyncKeyState），不依赖窗口焦点与 Unity 输入系统。
    /// 固定画布体制（2026-08-26 夜三次重构，VPet/eSheep 源码逐行核验终案）：窗口恒=基准×DPI×缩放上限
    /// 固定尺寸；**相机/模型根/窗口在一切动画期间完全静止**——待机/单次/仪式动作只在固定相机画布内演
    /// （VPet 画布余量哲学），窗口位置绝不作为"播动画的副作用"改变。
    /// 源码实证（2026-08-26 拉 GitHub main）：VPet PNGAnimation img.Width=500——全部动画渲染进固定
    /// 500px 逻辑画布；Main.xaml.cs MoveTimer_Elapsed 仅 GraphType.Move（走路图）按显式速度向量
    /// MoveTimerPoint 移窗，其余动画一律 MoveTimer.Stop() 窗口静止；画布出界只打日志"当前动画移动
    /// 设计错误"不修正（接受裁剪）。eSheep FormPet 唯一权威状态 PositionX/Y 仅由动画 XML 显式编写的
    /// TMovement 步进累加（待机步进=0），窗位每帧=取整(Position)。主流桌宠位置精确=结构性：位置只经
    /// 显式移动（走路步进/拖拽/飞行）变更，绝无动画副作用。
    /// 旧"窗口跟随动画编排位移"体制（2026-08-25~26 根绝对补偿版）两轮实证失败废弃：归零依赖"混合回
    /// 待机恰好回到锚点捕获时的待机相位"——待机是带骨盆微动的循环，相位永不对齐；动作被拖拽打断时
    /// 拖拽基准根=动作中途补偿值；落地反应在混合中途重锚。每次偏差成为永久残差烤进下一锚点→随机游走
    /// 漂出窗口被截断（用户 2026-08-26 报"动作越多越漂，最终完全不可见"）+宽限期窗口跟待机微摆（"待机
    /// 时窗口也动"）。勿恢复任何形式的"动画期移窗/移根"。
    /// 窗口唯一移动源：①启动停靠/pet.json 恢复 ②拖拽物理绝对定位（骨盆客户区投影钉物理目标）③松手
    /// 防丢拉回+防隐形守卫（完全出虚拟屏才干预）。曾试全屏覆盖体制：3200×2000@200%DPI 实测帧率腰斩+
    /// 单核 93%，废弃。**拖拽移动窗口无屏边钳制（2026-08-25 拍板，为边缘交互铺路）**；松手后窗口
    /// 完全出虚拟屏才拉回屏内（VPet CheckCurrentScreen 同款防丢）；pet.json 持久化缩放+窗口原点。
    ///
    /// 拖拽物理（2026-08-26 深夜定案：刚体跟随+四肢摆动，纯模拟在 PetDragPhysicsController）：
    /// 身体=刚体 1:1 直跟光标（eSheep 直移语义；骨盆目标=抓取时骨盆位+光标位移）——无钟摆/重力/
    /// 倾角弹簧（用户拍板"拖拽中不需要任何摆动，摆动的应当只有四肢"）。刚体平移下按住的点天然
    /// 钉在光标下，旧钟摆的斗篷锚点随之失去意义已移除。窗口按"骨盆客户区投影钉物理目标位"定位
    /// （根平移全程不动，仅根旋转=拎起姿势基准）。四肢摆动=4 条欠阻尼角弹簧（跟拍 secondary
    /// motion）：光标速度→肩/大腿骨世界 Z 轴旋转（水平滞后+竖直外展），LateUpdate 叠加在 Drag01
    /// 垂落姿势之上。松手即停：骨盆停原地，姿势与四肢摆动 ~0.5s 平滑归零。
    /// 旧钟摆/挣扎/飞行/落地反应链路已全删。
    ///
    /// 拎起姿势（2026-08-26 v6 瘫软式+3/4 偏左转身）：拖拽期播专用 Drag01 垂落动画（四肢常量垂落+
    /// 躯干保留待机微动），根姿势基准=拎起转身角（默认 45° 3/4 偏左：用户新参考图，身体略朝左而非全侧挂；
    /// Drag01 的 C 型前屈朝模型前方，转身后即朝屏幕左前方）×拎起横躺角（默认 0 直立），摆动感由
    /// 四肢跟拍弹簧单独承担。沿革：v1 程序化四肢垂落叠加层（PetDanglePoseController，已弃用留库）→ v2 Sleep01
    /// 躺姿+横躺 90°（仓鼠式）→ v3 专用垂落动画 → v4 瘫软低头+90° 侧挂 → v5 全侧挂深化（KO 式头折向地面，
    /// 已废）→ v6 瘫软 45° 头朝观众+3/4 偏左。
    /// </summary>
    public class PetWindowController : PetHostBase
    {
        [Header("窗口设置")]
        [InspectorName("使用DWM透明")]
        [SerializeField] private bool useDwmTransparent = true; // 主流做法；关闭则退回色键（有洋红毛边）
        [InspectorName("色键颜色")]
        [SerializeField] private Color colorKey = new Color(1f, 0f, 1f, 1f); // 仅色键兜底模式使用
        [InspectorName("停靠边距")]
        [SerializeField] private float dockMargin = 24f;
        [InspectorName("启动时停靠右下角")]
        [SerializeField] private bool dockBottomRight = true;
        [InspectorName("允许双击退出")]
        [SerializeField] private bool allowDoubleClickExit = true; // 派蒙独立存活后的手动关闭方式（后续可换右键菜单）

        [Header("性能")]
        [InspectorName("目标帧率")]
        [SerializeField] private int targetFramerate = 30;
        [Tooltip("垂直同步：0=关（仅用目标帧率限帧）/ 1=每个刷新一帧 / 2=隔一个刷新一帧 / 3=自适应（默认：刷新率≥120Hz→2 否则→1，任何屏都≥60fps 且帧预算有余量；60Hz 屏固定 2 会变 30fps 勿用）。DWM帧对齐开启时本项被忽略")]
        [InspectorName("垂直同步")]
        [SerializeField] private int vSyncCount = 3;
        [Tooltip("DWM 帧对齐（2026-08-27 根治匀速动画 judder）：每帧 DwmFlush 把主循环钉到桌面合成网格（刷新率的整数倍间隔）——等效硬件 vsync 的帧节拍整律器。分层窗口 present 不阻塞（blt 模型），vSyncCount 实际无效（Player.log 实证 min=6.05/max=13.3 混杂节拍、有效帧率 82-165 波动=匀速动画全程 judder，头部因指数阻尼免疫）——本开关是唯一有效杠杆。开=强制 vsync=0+不限帧，DwmFlush 吸收渲染方差：165Hz 屏 → 恒 12.1ms 节拍 82.5fps")]
        [SerializeField] private bool DWM帧对齐 = true;

        [Header("缩放（共用缩放参数在 PetHostBase）")]
        [InspectorName("允许滚轮缩放")]
        [Tooltip("滚轮缩放派蒙大小（光标命中模型时生效，与拖拽一致）")] [SerializeField] private bool allowScrollZoom = true;
        [InspectorName("窗口逻辑宽")]
        [Tooltip("窗口客户区逻辑宽度基准（96 DPI 像素；实际窗口=基准×DPI×有效缩放上限，运行期恒定不随缩放变化——缩放只改模型，杜绝逐帧改窗口的闪烁；含阴影落脚边距。2026-08-26 550→750：高度被工作区 95% 钳制已顶格，横向加宽=同尺寸派蒙两侧余量各+230px；画布像素性能实测 3.28MP≈CPU 84% 单核（--pet-canvas-px 矩阵）")] [SerializeField] private int windowLogicW = 750;
        [InspectorName("窗口逻辑高")]
        [Tooltip("窗口客户区逻辑高度基准（96 DPI 像素；含阴影落脚边距，与 FOV 45.27 配套保持派蒙像素尺寸——高度动不得：maxScale 由工作区高度钳定，调高基准只会压缩放上限缩小派蒙）")] [SerializeField] private int windowLogicH = 825;

        [Header("调试")]
        [InspectorName("打印状态日志")]
        [SerializeField] private bool printStateLog = false;

        private Camera cam;
        private IntPtr hwnd = IntPtr.Zero;
        private bool restyled;
        private bool dragging;

        /// <summary>是否正在拖拽派蒙（行为层打断打招呼、随机小动作等用；含收尾全程）</summary>
        public override bool IsDragging => dragging || PhysicsBusy;

        // ---- IPetHost 宿主实现（其余共用成员在 PetHostBase，2026-08-28 共用化重构） ----
        public override bool TryGet光标Unity屏幕位置(out Vector2 pos) => TryGetCursorUnityScreenPos(out pos);

        public override bool IsSeated
        {
            get
            {
                var es = FindObjectOfType<PetEdgeSitController>();
                return es != null && es.IsSeated;
            }
        }
        public override string SitAnim
        {
            get
            {
                var es = FindObjectOfType<PetEdgeSitController>();
                return es != null ? es.SitAnim : null;
            }
        }

        protected override string logTag => "[PetWindow]";

        /// <summary>窗口是否已完成 Win32 改造（构建版 true；编辑器恒 false——桌宠形态仅存在于构建产物）。
        /// 边坐等窗口级子系统据此在编辑器内安全空转。</summary>
        public bool IsWindowRestyled => restyled;

        /// <summary>自身窗口句柄（(IntPtr)0 = 未改造）——外部子系统做 Win32 查询/排除自身用</summary>
        public IntPtr WindowHandle => hwnd;

        /// <summary>当前窗口 DPI 缩放（GetDpiForWindow/96）——外部子系统把逻辑像素阈值换算物理像素用</summary>
        public float Dpi缩放 => dpi缩放 > 0.01f ? dpi缩放 : 1f;

        /// <summary>滚轮缩放目标倍率——边坐层监听缩放变化（坐姿下缩放=切站立重坐）用</summary>
        public float TargetScaleValue => targetScale;

        /// <summary>缩放平滑过渡是否进行中（显示缩放未追上目标倍率）</summary>
        public bool ScaleTransitioning => !Mathf.Approximately(displayScale, targetScale);

        /// <summary>取接触点屏幕坐标（骨盆=屁股投影，物理像素，y 向下）——边坐判定/贴合基准。
        /// 2026-08-27 目检纠正：坐姿接触线是屁股不是脚，脚线判定会把整条腿沉入窗下。</summary>
        public bool TryGet接触点屏幕位置(out Vector2 contactScreen)
        {
            contactScreen = default;
            if (hwnd == IntPtr.Zero || cam == null) return false;
            // 2026-08-27 目检纠正：判定/贴合基准=骨盆（屁股）不是脚（包围盒底）——坐姿时骨盆落在
            // 横框上、腿垂窗前才是"坐"；脚线判定会把整条腿沉入窗下。骨盆缺失时保底包围盒底中心。
            Vector3 baseWorld;
            if (_骨盆 != null) baseWorld = _骨盆.position;
            else if (TryGet命中世界包围盒(out Bounds b)) baseWorld = new Vector3(b.center.x, b.min.y, b.center.z);
            else return false;
            if (!WorldToClientPixel(baseWorld, out Vector2 baseClient)) return false;
            var origin = GetClientOriginScreen();
            contactScreen = new Vector2(origin.X + baseClient.x, origin.Y + baseClient.y);
            return true;
        }

        /// <summary>移动窗口使接触点（骨盆=屁股）对齐到指定屏幕坐标（物理像素；x=接触点水平位置，y=坐落线）。
        /// 边坐吸附/跟随/掉落共用；不改变窗口尺寸，动画期间模型照常在画布内演。</summary>
        public void SetContactScreenPos(float screenX, float screenY)
        {
            if (hwnd == IntPtr.Zero || cam == null || _骨盆 == null) return;
            if (!WorldToClientPixel(_骨盆.position, out Vector2 baseClient)) return;
            GetFrameSize(out _, out _, out int frameLeft, out int frameTop);
            SetWindowPos(hwnd, IntPtr.Zero,
                Mathf.RoundToInt(screenX - baseClient.x) - frameLeft,
                Mathf.RoundToInt(screenY - baseClient.y) - frameTop,
                0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_SHOWWINDOW);
        }

        /// <summary>标记当前窗口位置待落盘（pet.json 防抖写）——外部系统（边坐吸附/落地）移动窗口后调用</summary>
        public void MarkPosDirty() { MarkDirty(); }

        private bool passThroughOn;
        private bool prevLmbDown;
        private Vector2Int dragStartCursor; // 拖拽起点（区分单击与真实拖动）
        private float lastClickTime = -10f; // 双击退出判定：上次有效单击时刻
        private PetBehaviorController behaviorCtrl; // 双击退出的退场动画协作（播 Disappear 后再关进程）

        // ---- 对话（2026-08-29 桌面版补齐，复用游戏内形态三组件 docs/19 §6.5）----
        private GIC.Pet.Chat.PetChatUIController _聊天UI;
        private Transform _头骨;                    // 气泡水平锚（同游戏内：包围盒顶+头骨水平位）
        private float _单击待开对话时刻 = -1f;      // 单击→过 0.4s 双击窗口才开对话（不与双击退出互抢）
        private bool _pressPending;                 // 命中模型按下但未升级为拖拽（单击判定窗口内，2026-08-29 移植游戏内单击阈值）
        private float _单击按下时刻 = -10f;
        private POINT _单击按下pt;

        private bool exitRequested;              // 退场动画进行中：屏蔽重复双击与新拖拽
        private int baseWinW, baseWinH; // 基准客户区物理像素（=逻辑尺寸×dpi/96）
        private int fixedWinW, fixedWinH; // 实际窗口客户区物理像素（=基准×有效缩放上限，运行期恒定不随缩放变化）
        private float dpi缩放 = 1f;        // GetDpiForWindow/96（exe 清单 PerMonitorV2：客户区物理像素=渲染像素）

        // 统一存档（2026-08-27 用户拍板"统一 pet.json"）：桌面/游戏内形态状态共存 PetPrefs.Pet存档
        // （v2），本控制器只读写桌面字段；旧 v1 字段由 PetPrefs 读取时迁移。锚点字段（v2 全屏体制
        // 已废弃）的换算迁移保留在 恢复保存位置 内直接读旧文件语义已不可能（缓存统一 v2 结构）——
        // 桌面侧迁移后的有锚点档极旧（2026-08-25 前后一周），按无位置处理=右下角停靠，可接受。
        private PetPrefs.PetSave loadedPrefs;
        private float dirtyUntil = -1f; // >0 = 有未落盘修改（防抖：最后一次修改后 1s 写盘）
        // WH_MOUSE_LL 钩子截 WM_MOUSEWHEEL（Input.mouseScrollDelta 在窗口穿透/无焦点时常返回 0）
        private IntPtr _mouseHook = IntPtr.Zero;
        private HookProc _mouseHookProc; // 防 GC 回收委托
        private float _hookKeepUntil = -10f; // 滞回：离开模型 0.5s 后才摘钩
        private float _上次窗口体检 = -10f;  // 防隐形守卫低频节流（0.5s 一次）
        private float _上次置顶检查 = -10f;  // 置顶守卫低频节流（0.5s 一次）
        private readonly System.Text.StringBuilder _类名缓存 = new System.Text.StringBuilder(64); // GetClassName 复用（置顶守卫）
        private static int _pendingWheelDelta; // 钩子线程累加写入，Update 主线程取走清零（120=一格）

        // Win32 互操作（DllImport/结构体/常量）集中在 PetWin32 —— 见文件头 using static
        // 已随抽取删除的死声明（2026-08-27 核验从未被引用）：MSLLHOOKSTRUCT（滚轮回调走
        // Marshal.ReadInt32 偏移直读零封送）、GetCurrentThreadId（全局钩子线程号传 0）。

        private void Awake()
        {
#if UNITY_EDITOR
            // 编辑器预览不碰 QualitySettings（运行时改 vSyncCount 退出 Play 不回滚，会污染编辑器）
            _ = vSyncCount; // 字段仅供构建版使用，读一次消 CS0414
            _ = DWM帧对齐; // 同上（DwmFlush/DWM 分支均 #if !UNITY_EDITOR）
            Application.targetFrameRate = targetFramerate;
#else
            // 帧节奏演化史（2026-08-26 终案=自适应分频，Player.log 实证）：
            // v0=vsync0+30 限帧（165Hz 上 5.5 不整除→顿挫，弃）→ v1=vSyncCount=2 → v1.5=2026-08-24 改
            // vsync=1 满刷 165fps → v2=2026-08-26 实测打回 vsync=2（满刷是零余量假象：渲染 avg≈6ms 踩线
            // vsync 间隔 6.06ms，~73% 帧实为 12.1ms（错过刷新）+ 15-21ms 帧成片（94% 无 GC/烘焙/眨眼标记）
            // → 匀速动画 judder；视线跟随的指数阻尼=低通滤波器对抖动免疫——"头部丝滑、动作不丝滑"的根因）
            // → v3=**自适应**：固定 vsync=2 在 60Hz 屏=30fps 必卡（vsync 帧率=刷新率÷N）；启动时读刷新率，
            // ≥120Hz→2（165→82.5/144→72/120→60，帧预算 12.1-16.7ms 余量 100%+）、否则→1（60Hz→60fps
            // 预算 16.7ms 余量 178%，75Hz→75）。任何屏都≥60fps 且帧预算远超渲染 6ms=节奏恒定无 judder。
            // 官方文档：vsync=硬件同步（平滑帧节拍），targetFrameRate=软件限帧有 microstutter——勿用
            // vsync=0+限帧替代。仅宠物进程执行，不影响主游戏画质。
            // v4=DWM帧对齐（2026-08-27）：vsync 上述"整律"假设在分层窗口上破产——blt 模型 present
            // 不阻塞（canvas 矩阵实证 min=6.05ms），自适应 vsync 实为无效设置，节拍仍 6-13ms 混杂
            // （~130fps 自由跑）→ 匀速动画全程 judder（"任何单动作期间都不丝滑"用户目检实证）。
            // DwmFlush 每帧阻塞到下一次桌面合成=把主循环钉到刷新率网格，渲染方差被等待吸收：
            // 165Hz 屏恒 12.1ms 节拍（82.5fps，与 vsync=2 理论值相同但真实生效）。
            int refresh = (int)Screen.currentResolution.refreshRateRatio.value;
            if (refresh <= 0) refresh = 60; // 取不到时保守按 60Hz 走 vsync=1
            if (DWM帧对齐)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1; // 不限帧：节拍由 DwmFlush 决定（LateUpdate 每帧调用）
                Debug.Log($"[PetWindow] 帧节奏：DWM帧对齐 开（refresh={refresh}Hz，DwmFlush 钉合成网格）");
            }
            else
            {
                int vsync = vSyncCount >= 3 ? (refresh >= 120 ? 2 : 1) : vSyncCount;
                QualitySettings.vSyncCount = Mathf.Clamp(vsync, 0, 4);
                if (QualitySettings.vSyncCount == 0)
                {
                    Application.targetFrameRate = targetFramerate;
                }
                Debug.Log($"[PetWindow] 帧节奏：refresh={refresh}Hz vsync={QualitySettings.vSyncCount} → " +
                          $"{(QualitySettings.vSyncCount > 0 ? $"{refresh / (float)QualitySettings.vSyncCount:F1}fps（帧预算 {1000f * QualitySettings.vSyncCount / refresh:F1}ms）" : $"限帧{targetFramerate}")}");
            }
#endif
            Application.runInBackground = true;
        }

        private void Start()
        {
            cam = Camera.main;
            behaviorCtrl = FindObjectOfType<PetBehaviorController>();
            if (dragPhysics == null) dragPhysics = FindObjectOfType<PetDragPhysicsController>();
            if (dragPhysics == null) Debug.LogError("[PetWindow] 未找到 PetDragPhysicsController（拖拽物理）——物理拖拽不可用，检查 PaimonPet 场景接线");
            if (cam != null)
            {
                // 透明要求相机输出恒定背景；关 HDR 防浮点缓冲漂移
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.allowHDR = false;
                if (useDwmTransparent)
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
                    cam.backgroundColor = colorKey;
                }
            }
            // 命中链路：Paimon 下的蒙皮渲染器 + 同 transform 的 MeshCollider 节点（动态烘焙，共用基类配方）
            var paimonRoot = GameObject.Find("Paimon");
            if (paimonRoot != null)
            {
                paimon根 = paimonRoot.transform;
                WireDragBones();
                baseScale = paimon根.localScale.x;
                // 缩放目标：构建版读 pet.json（持久化），无存档/编辑器用 Inspector 默认倍率。
                // 启动即到位（无平滑动画）。
#if !UNITY_EDITOR
                LoadPrefs();
#endif
                targetScale = (loadedPrefs != null && loadedPrefs.桌面缩放 > 0f) ? loadedPrefs.桌面缩放 : initScaleFactor;
                displayScale = targetScale;
                ApplyModelScale();
                bodyRenderer = paimonRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
                BuildHitProxy();
            }
            if (hitMeshCollider == null)
            {
                Debug.LogError("[PetWindow] 未找到蒙皮渲染器，命中判定失效");
            }

#if UNITY_EDITOR

            // 编辑器内禁止 Win32 窗口改造——GetActiveWindow 拿到的是编辑器自身窗口，会破坏编辑器 UI。

            // 桌宠形态仅存在于构建产物（主进程自动拉起 / gic.exe --pet-mode）；编辑器 Play 本场景只做模型预览。

            Debug.Log("[PetWindow] 编辑器模式：跳过窗口改造。桌宠由主进程自动拉起（Builds/PetSpike/gic.exe）");

#else

            if (Screen.fullScreen)

            {

                Screen.fullScreen = false;

            }

            acquireWindow();

#endif

            // 聊天接线放在窗口改造之后、且不受改造失败影响（2026-08-30 事故：句柄获取竞态 return 把

            // 接聊天() 一起吞掉=聊天 UI 无 Canvas 每帧 NRE 刷屏 9.6 万条）。聊天只依赖 Unity Canvas，

            // 与 Win32 窗口无关——窗口改造整体失败（5s 重试超时）也只是"普通带边框窗口"，聊天照常。

            接聊天();

        }


        /// <summary>取窗口句柄并改造（2026-08-30 事故根治）：主游戏在启动期拉起桌宠时，用户焦点

        /// 常在游戏窗口（快速跳过启动动画按键时尤其）——桌宠窗口从未被激活，GetActiveWindow() 返回 0

        /// （它只返回调用线程消息队列的激活窗口，与进程自己拥有窗口无关）。三级获取：

        /// ①GetActiveWindow（有焦点时，最快）②EnumWindows 按进程号+标题找本进程主窗口（焦点无关）

        /// ③窗口创建晚于 Start 的竞态→协程每帧重试 5s。</summary>

        void acquireWindow()

        {

            hwnd = GetActiveWindow();

            if (hwnd == IntPtr.Zero) hwnd = findOwnMainWindow();

            if (hwnd == IntPtr.Zero)

            {

                StartCoroutine(acquireWindowRetry());

                return;

            }

            RestyleWindow();

        }


        System.Collections.IEnumerator acquireWindowRetry()

        {

            float deadline = Time.realtimeSinceStartup + 5f;

            while (Time.realtimeSinceStartup < deadline)

            {

                yield return null;

                hwnd = GetActiveWindow();

                if (hwnd == IntPtr.Zero) hwnd = findOwnMainWindow();

                if (hwnd != IntPtr.Zero)

                {

                    RestyleWindow();

                    yield break;

                }

            }

            Debug.LogError("[PetWindow] 5 秒内未取到窗口句柄，窗口改造失败（透明/置顶/拖拽不可用，聊天不受影响）");

        }


        /// <summary>枚举顶层窗口找本进程主窗口（GetActiveWindow 的焦点无关替代）：按进程 PID +

        /// 可见 + 标题=产品名匹配（Unity 播放器主窗口标题=Application.productName；进程内的隐藏

        /// 辅助窗口按可见性排除）。EnumWindows 同步执行，lambda 闭包无生命周期问题。</summary>

        static IntPtr findOwnMainWindow()

        {

            IntPtr found = IntPtr.Zero;

            uint selfPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

            var title = new System.Text.StringBuilder(64);

            EnumWindows((h, l) =>

            {

                GetWindowThreadProcessId(h, out uint pid);

                if (pid != selfPid || !IsWindowVisible(h)) return true;

                title.Length = 0;

                GetWindowText(h, title, title.Capacity);

                if (title.ToString() == Application.productName) { found = h; return false; }

                return true;

            }, IntPtr.Zero);

            return found;

        }

        /// <summary>对话接线（2026-08-29 桌面版补齐，docs/19 §6.5）：聊天三组件挂 PetWindow 物体
        /// （场景序列化接线素材/字体/会话），此处运行时建画布+EventSystem 并注入锚点——复刻游戏内
        /// PetInGameHostController.接聊天 的模式。桌面差异：①无 RT——锚点=相机屏幕位直通
        /// （Overlay 画布 ConstantPixelSize=客户像素系，与 WorldToScreenPoint 同空间）；
        /// ②场景自带 EventSystem 是 PetEditorOnly（构建版自禁用）——构建版此处补建；
        /// ③交互走本控制器的 Win32 轮询（DragAndClickFrame 单击开对话/外点关闭）。
        /// 聊天是可选功能：构建失败绝不能上抛（同游戏内防泄漏结构——Awake/Start 异常会禁用组件）。</summary>
        void 接聊天()
        {
            _聊天UI = GetComponentInChildren<GIC.Pet.Chat.PetChatUIController>(true);
            if (_聊天UI == null) return; // 场景未挂（旧场景/裁剪安装）=对话功能缺席，其余交互不受影响
            try
            {
                var canvasGo = new GameObject("PetChatCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
                canvasGo.transform.SetParent(transform, false); // 挂 PetWindow 下（随宿主销毁）
                var canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay; // 恒铺客户区（固定窗口=客户像素）
                canvas.sortingOrder = 10;
                var scaler = canvasGo.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; // 屏幕像素系（锚点直通的前提）
                // 桌宠场景的 EventSystem 是编辑器测试面板配套（PetEditorOnly 构建自禁用）——构建版补建
                if (UnityEngine.EventSystems.EventSystem.current == null)
                    new GameObject("PetChatEventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.EventSystems.StandaloneInputModule));
                _聊天UI.WireHost(canvas);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[PetWindow] 聊天 UI 构建失败，已禁用（桌宠交互不受影响）：{e.Message}");
                _聊天UI.enabled = false;
                _聊天UI = null;
                return;
            }
            // 头骨锚（水平跟头骨，同游戏内）；找本体骨过滤 MMD 兜底（日文骨名系）——GI=Bip001 系
            _头骨 = FindBodyBone("Bip001 Head");
            // 锚点提供器：烘焙碰撞体包围盒 → 相机屏幕位（桌面无 RT 换算，屏幕像素=画布像素直通）
            _聊天UI.headAnchorProvider = () =>
            {
                if (cam == null || hitMeshCollider == null || hitMeshCollider.sharedMesh == null) return Vector2.zero;
                var bounds = hitMeshCollider.bounds;
                float x = _头骨 != null ? _头骨.position.x : bounds.center.x; // 水平跟头骨（歪头/侧移气泡跟脸）
                Vector3 sp = cam.WorldToScreenPoint(new Vector3(x, bounds.max.y, bounds.center.z));
                return new Vector2(sp.x, sp.y);
            };
            _聊天UI.footAnchorProvider = () =>
            {
                if (hitMeshCollider == null || cam == null) return Vector2.zero;
                var bounds = hitMeshCollider.bounds;
                Vector3 sp = cam.WorldToScreenPoint(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
                return new Vector2(sp.x, sp.y);
            };

            // Intent 工具接线（2026-08-30 桌面版补齐，docs/19 §6.5）：桌面形态是独立进程、无主进程
            // 引用——工具经 PetIntentIpc 文件通道转发主游戏进程执行（主进程 PetIntentIpcHost 消费），
            // 与游戏内形态的直调执行行为对齐。主游戏未运行时通道超时报错（LLM 自行解释）。
            // open_screen 已按用户拍板移除（2026-08-30）。注册失败只少工具不影响聊天（防泄漏结构同上）。
            try
            {
                var session = _聊天UI.sessionRef;
                if (session != null)
                {
                    var tools = new System.Collections.Generic.List<GIC.Pet.Chat.PetChatClient.ToolDefinition>
                    {
                        GIC.Pet.Chat.PaimonChatSession.MemoryToolDefinition(),
                        GIC.Pet.Chat.PaimonChatSession.DoActionTool(), // 情绪动作（LLM 对话自主选，会话层本地拦截不经 IPC，2026-08-31）
                        GIC.Pet.Chat.PetChatIntent.SetGameTimeTool(),
                        GIC.Pet.Chat.PetChatIntent.GetGameTimeTool(),
                        GIC.Pet.Chat.PetChatIntent.OpenScreenTool(),
                        GIC.Pet.Chat.PetChatIntent.AutoWishTool(),
                    };
                    session.RegisterTools(tools, (toolName, toolArgs) =>
                    {
                        return GIC.Pet.Chat.PetIntentIpc.RequestWithWait(toolName, toolArgs);
                    });
                    // do_action 动作回调（会话层本地消化——模型在本进程，走 IPC 转发主进程是错的）：
                    // 行为层播单次动作（拖拽物理中/退场中 PlayReaction 内部静默跳过——反应错失可接受）
                    session.onPlayAction = anim => behaviorCtrl?.PlayReaction(anim);
                    Debug.Log("[PetWindow] 对话指令工具已注册（文件通道转发主游戏进程执行，含界面/自动抽卡；情绪动作本地播放）");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[PetWindow] 对话指令工具注册失败（聊天基础功能不受影响）：{e.Message}");
            }
            接反应通道();
            Debug.Log("[PetWindow] 对话已接线（单击派蒙开输入条）");
        }

        /// <summary>反应通道消费接线（2026-08-30 AI 抽卡配套）：主进程写入的反应事件
        /// （文本+动作+LLM 注记）→ 行为层播动作 + 气泡直出 + 会话历史注记。
        /// 独立 try-catch 防泄漏（接聊天 内可选功能结构同款）。</summary>
        void 接反应通道()
        {
            try
            {
                var consumer = GetComponent<GIC.Pet.Chat.PetReactionConsumer>();
                if (consumer == null) consumer = gameObject.AddComponent<GIC.Pet.Chat.PetReactionConsumer>();
                consumer.Wire(behaviorCtrl, _聊天UI);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[PetWindow] 反应通道接线失败（其余功能不受影响）：{e.Message}");
            }
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
            if (useDwmTransparent)
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
                key = (uint)((int)(colorKey.r * 255f)
                          | ((int)(colorKey.g * 255f) << 8)
                          | ((int)(colorKey.b * 255f) << 16));
                SetLayeredWindowAttributes(hwnd, key, 0, LWA_COLORKEY);
            }
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);

            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_SHOWWINDOW | SWP_FRAMECHANGED);

            // DPI 换算（主流桌宠做法：任何显示器缩放下派蒙视觉物理大小一致）。
            // exe 清单 PerMonitorV2 → 客户区物理像素=渲染像素；物理尺寸 = 逻辑尺寸 × dpi/96。
            dpi缩放 = GetDpiForWindow(hwnd) / 96f;
            if (dpi缩放 <= 0.01f) dpi缩放 = 1f;
            baseWinW = Mathf.RoundToInt(windowLogicW * dpi缩放);
            baseWinH = Mathf.RoundToInt(windowLogicH * dpi缩放);

            // 有效缩放上限：窗口不超过工作区 95%（防巨大化后被屏幕裁切/吞任务栏）
            SystemParametersInfo(SPI_GETWORKAREA, 0, out RECT work, 0);
            float maxW = (work.Right - work.Left) * 0.95f / baseWinW;
            float maxH = (work.Bottom - work.Top) * 0.95f / baseWinH;
            effectiveMaxScale = Mathf.Min(scaleMax, maxW, maxH);
            targetScale = Mathf.Clamp(targetScale, scaleMin, effectiveMaxScale);
            displayScale = targetScale; // 启动直接到位（无平滑动画）
            // 钳制后重放模型缩放（极端小屏：初始倍率超上限时保持模型与窗口同步）
            ApplyModelScale();

            // 固定窗口（2026-08-24 闪烁根治）：客户区尺寸恒=基准×有效缩放上限，运行期不随缩放变化——
            // 滚轮平滑过渡只改模型 localScale，不再逐帧 SetWindowPos 改窗口（逐帧 resize 令 swapchain/DWM
            // 高频重建合成，派蒙肉眼高频闪烁）。窗口 oversized 部分全透明+穿透，无视觉/交互代价。
            // 2026-08-26 固定画布体制：窗口 move-only 仍只发生在拖拽物理（绝对定位），动画期间窗口
            // 完全静止（VPet 哲学：动画只在画布内演，绝无"播动画的副作用"移动窗口）。
            fixedWinW = Mathf.RoundToInt(baseWinW * effectiveMaxScale);
            fixedWinH = Mathf.RoundToInt(baseWinH * effectiveMaxScale);

            // 位置恢复优先于右下角停靠（拖拽停留位置持久化，docs/19 §3.1）；
            // 无存档且未开停靠则维持 Unity 默认位置
            bool restoredPos = RestoreSavedPos();
            if (!restoredPos && dockBottomRight)
            {
                DockBottomRight();
            }

            // 画布性能测试钩子（2026-08-26）：--pet-canvas-px=WxH 直接覆盖客户区物理像素尺寸，
            // 绕过工作区 95% 钳制（模型缩放不变——同一模型不同画布像素量，隔离 GPU 填充成本）。
            // 以当前窗口中心为锚重设尺寸，防止大窗出屏。无参数时零作用。
#if !UNITY_EDITOR
            {
                var args = System.Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (args[i] != "--pet-canvas-px") continue;
                    var wh = args[i + 1].Split('x');
                    if (wh.Length == 2 && int.TryParse(wh[0], out int cw) && int.TryParse(wh[1], out int ch) && cw > 0 && ch > 0)
                    {
                        fixedWinW = cw;
                        fixedWinH = ch;
                        GetWindowRect(hwnd, out RECT wr0);
                        int ccx = (wr0.Left + wr0.Right) / 2, ccy = (wr0.Top + wr0.Bottom) / 2;
                        GetFrameSize(out int fw0, out int fh0, out int fl0, out int ft0);
                        SetWindowPos(hwnd, IntPtr.Zero, ccx - cw / 2 - fl0, ccy - ch / 2 - ft0, cw + fw0, ch + fh0, SWP_NOZORDER | SWP_SHOWWINDOW);
                        Debug.Log($"[PetWindow] 画布测试覆盖 client={cw}x{ch}px");
                    }
                }
            }
#endif

            // 鼠标钩子不在此常驻安装——UpdateHookForHit 按命中状态挂/摘（2026-08-24 顿挫优化：
            // 常驻钩子对全系统鼠标消息做封送分配+主线程回调，鼠标移动时灌爆主线程）

            restyled = true;
            Debug.Log($"[PetWindow] 窗口改造完成 hwnd=0x{hwnd.ToInt64():X} mode={(useDwmTransparent ? "DWM-alpha" : $"colorKey=0x{key:X6}")} render={Screen.width}x{Screen.height} dpi={dpi缩放:F2} fixedClient={fixedWinW}x{fixedWinH} scale={targetScale:F2} maxScale={effectiveMaxScale:F2} pos={(restoredPos ? "restored" : "dock/default")}");
        }

        /// <summary>恢复存档窗口位置（客户区原点，钳制到虚拟屏幕防显示器拔掉后找不到派蒙）。成功=true。
        /// v2 锚点档（全屏体制遗留）已随统一存档（PetPrefs v2）退役：桌面位置只认 桌面客户区X/Y。</summary>
        private bool RestoreSavedPos()
        {
            if (hwnd == IntPtr.Zero) return false;
            int clientW = fixedWinW;
            int clientH = fixedWinH;

            int nx, ny;
            if (loadedPrefs != null && loadedPrefs.桌面有位置)
            {
                nx = loadedPrefs.桌面客户区X;
                ny = loadedPrefs.桌面客户区Y;
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
            return true;
        }

        /// <summary>停靠右下角：窗口尺寸恒=固定窗口（不随缩放），边距按 DPI 换算</summary>
        private void DockBottomRight()
        {
            GetFrameSize(out int frameW, out int frameH, out _, out _);
            int winW = fixedWinW + frameW;
            int winH = fixedWinH + frameH;
            int margin = Mathf.RoundToInt(dockMargin * dpi缩放);
            SystemParametersInfo(SPI_GETWORKAREA, 0, out RECT work, 0);
            int x = work.Right - winW - margin;
            int y = work.Bottom - winH - margin;
            SetWindowPos(hwnd, IntPtr.Zero, x, y, winW, winH, SWP_NOZORDER | SWP_SHOWWINDOW);
        }

        /// <summary>拖拽松手防丢：窗口与虚拟屏完全无交集时拉回屏内（贴最近边，VPet CheckCurrentScreen
        /// 同款）。拖拽/动作移动本身无屏边钳制（2026-08-25 拍板，为边缘交互铺路）——仅松手时兜底。</summary>
        private void PostDragReelIn()
        {
            if (hwnd == IntPtr.Zero) return;
            GetWindowRect(hwnd, out RECT wr);
            int vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            // 有交集（含贴边/半出屏）=用户可及，不动
            if (wr.Right > vx && wr.Left < vx + vw && wr.Bottom > vy && wr.Top < vy + vh) return;
            int nx = Mathf.Clamp(wr.Left, vx, vx + vw - (wr.Right - wr.Left));
            int ny = Mathf.Clamp(wr.Top, vy, vy + vh - (wr.Bottom - wr.Top));
            SetWindowPos(hwnd, IntPtr.Zero, nx, ny, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_SHOWWINDOW);
        }

        #region dragPhysics（docs/19 §6.1 rigidFollow+limbSwing：窗口定位/rootRotationApply，pureSimIn PetDragPhysicsController）

        /// <summary>拖拽起手（2026-08-26 定案刚体跟随）：快照骨盆屏幕位与光标交给物理组件——
        /// 骨盆目标=骨盆基准+光标位移（1:1 直跟零摆动），四肢摆动弹簧由光标速度驱动。
        /// 根旋转基准仅在从静止起手时快照（收尾中被再抓不叠加）。</summary>
        private void BeginPhysicalDrag(POINT pt)
        {
            if (dragPhysics == null || _骨盆 == null || cam == null || hwnd == IntPtr.Zero || paimon根 == null) return;
            if (!WorldToClientPixel(_骨盆.position, out Vector2 pc)) return;
            var origin = GetClientOriginScreen();
            Vector2 pelvisScreen = new Vector2(origin.X + pc.x, origin.Y + pc.y);

            SnapshotDragBaseline(true); // 根旋转+位置基准（收尾中被再抓不重取，防旋转叠加）
            dragPhysics.BeginDrag(new Vector2(pt.X, pt.Y), pelvisScreen);
            if (printStateLog)
                Debug.Log($"[PetWindow] 物理拖拽起手 骨盆屏幕=({pelvisScreen.x:F0},{pelvisScreen.y:F0}) 四肢骨={(_四肢骨 != null ? System.Linq.Enumerable.Count(_四肢骨, b => b != null) : 0)}/{(_四肢骨 != null ? _四肢骨.Length : 0)}");
        }

        /// <summary>DWM 帧对齐（2026-08-27）：阻塞到下一次桌面合成完成——主循环钉到合成网格，渲染方差
        /// 被 flush 等待吸收 → 帧节拍恒为刷新间隔整数倍（165Hz 屏=12.1ms）。分层窗口 present
        /// 不阻塞、vSyncCount 无效（canvas 矩阵 min=6.05 实证），这是匀速动画不 judder 的唯一
        /// 有效节拍器（问题①"任何单动作全程不丝滑"根治）。置于本方法一切早退之前=每帧必执行。
        /// 四肢摆动叠加走共用基类（PetHostBase.四肢摆动应用帧，2026-08-28 合一）。</summary>
        void LateUpdate()
        {
#if !UNITY_EDITOR
            if (DWM帧对齐 && restyled) DwmFlush();
#endif
            LimbSwingApplyFrame();
        }

        /// <summary>窗口定位：客户区原点 = 骨盆屏幕目标 - 本帧骨盆客户区偏移（旋转后动态投影——根平移
        /// 全程不动，旋转/动画致骨盆在窗内位移由窗口位置吸收，骨盆屏幕位恒钉物理目标）。
        /// 拎起姿势旋转（含挣扎+绕骨盆枢轴补偿）已合一进 PetHostBase.拎起姿势角帧（2026-08-28 共用化）。</summary>
        private void PositionWindowByPelvis()
        {
            if (dragPhysics == null || !WorldToClientPixel(_骨盆.position, out Vector2 pelvisClient)) return;
            GetFrameSize(out _, out _, out int frameLeft, out int frameTop);
            Vector2 target = dragPhysics.CurrentPelvisScreen;
            SetWindowPos(hwnd, IntPtr.Zero,
                Mathf.RoundToInt(target.x - pelvisClient.x) - frameLeft,
                Mathf.RoundToInt(target.y - pelvisClient.y) - frameTop,
                0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_SHOWWINDOW);
        }

        /// <summary>桌面版收口归位：旋转+位置精确还原基准（桌面模型位置本不被拖拽改——移动的是窗口；
        /// 游戏内版基类默认=骨盆枢轴补偿归位保留拖拽位置，模型位置=拖拽结果）。</summary>
        protected override void DragSettleRestore()
        {
            if (paimon根 != null)
            {
                paimon根.localRotation = _拖拽基准旋转;
                paimon根.position = _拖拽基准根位置;
            }
            _当前横躺角 = 0f;
            _currentYaw = 0f;
        }

        /// <summary>物理交互收口（拖拽/收尾全部结束）：根旋转/根位置精确归位（横躺清零）、窗口完全出
        /// 虚拟屏才拉回（防丢兜底，VPet CheckCurrentScreen 同款）、位置落盘（松手即停=停在摆放处）。</summary>
        private void PhysicsSettle()
        {
            DragSettleRestore();
            PostDragReelIn();
            MarkDirty();
        }

        /// <summary>世界坐标 → 客户区像素（x 自左、y 自顶，物理像素=渲染像素）。</summary>
        private bool WorldToClientPixel(Vector3 world, out Vector2 clientPx)
        {
            clientPx = default;
            if (cam == null || hwnd == IntPtr.Zero) return false;
            Vector3 vp = cam.WorldToViewportPoint(world);
            if (vp.z <= 0f) return false;
            GetClientRect(hwnd, out RECT cr);
            int cw = cr.Right - cr.Left, ch = cr.Bottom - cr.Top;
            if (cw <= 0 || ch <= 0) return false;
            clientPx = new Vector2(vp.x * cw, (1f - vp.y) * ch);
            return true;
        }

        /// <summary>客户区原点 (0,0) 的屏幕坐标（虚拟桌面物理像素系）——客户区↔屏幕换算的统一基准</summary>
        private POINT GetClientOriginScreen()
        {
            var origin = new POINT { X = 0, Y = 0 };
            ClientToScreen(hwnd, ref origin);
            return origin;
        }

        #endregion

        /// <summary>窗口矩形与客户区的差值（无边框后理论上≈0，实测兜底；含隐形边框）</summary>
        private void GetFrameSize(out int frameW, out int frameH, out int frameLeft, out int frameTop)
        {
            GetWindowRect(hwnd, out RECT wr);
            GetClientRect(hwnd, out RECT cr);
            var origin = GetClientOriginScreen();
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
                        Debug.LogWarning("[PetWindow] 鼠标钩子安装失败，滚轮缩放退回 Input.mouseScrollDelta");
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
            SavePrefs(); // 进程销毁兜底落盘（无待写入则跳过）
        }

        void OnApplicationQuit()
        {
            SavePrefs(); // 双击退出/正常退出路径兜底（与 OnDestroy 幂等）
        }

        #region independentSave（pet.json——petNeverWriteMainSave，docs/19 §5.8 dualProcConstraint）

        private void MarkDirty()
        {
            dirtyUntil = Time.unscaledTime + 1f; // 防抖：最后一次修改后 1s 才写盘
        }

        private void LoadPrefs()
        {
            loadedPrefs = PetPrefs.Load(); // 统一存档（v2）：桌面/游戏内字段共存，本控制器用桌面侧
        }

        /// <summary>落盘缩放+窗口位置（客户区原点）——写进统一存档桌面字段（PetPrefs.Pet存档，
        /// 游戏内字段不动）。仅在构建版有待写入时执行，编辑器恒跳过。</summary>
        private void SavePrefs()
        {
#if !UNITY_EDITOR
            if (dirtyUntil <= 0f || hwnd == IntPtr.Zero) return;
            try
            {
                var origin = GetClientOriginScreen();
                var d = PetPrefs.Load();
                d.桌面缩放 = targetScale;
                d.桌面客户区X = origin.X;
                d.桌面客户区Y = origin.Y;
                d.桌面有位置 = true;
                PetPrefs.Save();
            }
            finally
            {
                dirtyUntil = -1f;
            }
#endif
        }

        #endregion

        /// <summary>取鼠标光标的 Unity 屏幕坐标（左下原点），供视线跟随等全局追踪使用。
        /// 与命中检测不同：光标在窗口外同样有效（线性外推，ScreenPointToRay 可处理屏外点）。
        /// 构建版走 Win32 全局轮询（窗口无焦点/穿透时也能追踪）；编辑器退回 Input.mousePosition。
        /// IPetHost 接口方法（游戏内版宿主同名实现，行为层经接口分发）。
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
            var origin = GetClientOriginScreen();
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

        // 找本体骨/找拖拽骨骼已上移 PetHostBase（两形态同款，2026-08-28 共用化）

        private void Update()
        {
            if (!restyled) return;

            // 置顶守卫（2026-08-28）：点击任务栏等 shell 窗口激活会被抬到 topmost 链内我们之上——
            // 坐任务栏时派蒙下半身被任务栏遮挡的根因。退场动画期间同样守卫（Disappear 被
            // 任务栏盖住半截更难看），故置于 已请求退出 早退之前
            TopmostGuardFrame();

            // 退出请求检测（2026-08-28：热切换→游戏内形态时主进程写 quit_request 文件，
            // 桌宠进程检测到后播 Disappear 退场动画再退出，非硬杀）
            if (!exitRequested && PetSingleInstance.HasQuitRequest())
            {
                exitRequested = true;
                PetSingleInstance.ClearQuitRequest();
                Debug.Log("[PetWindow] 收到退出请求，播放退场动画");
                bool exitTakeover = behaviorCtrl != null && behaviorCtrl.RequestExit(() =>
                {
                    PetSingleInstance.ClearQuitRequest();
                    Application.Quit();
                });
                if (!exitTakeover) { PetSingleInstance.ClearQuitRequest(); Application.Quit(); }
                return; // 退场期间不跑常规交互
            }
            if (exitRequested) return;

            WindowHealthCheckFrame();
            bool modelHit = GetHitState(out POINT pt);
            RebakeHitMeshFrame();
            // 钩子按需挂/摘（2026-08-24：滚轮缩放只在命中模型时消费，常驻钩子平白吃全系统鼠标消息）
            UpdateHookForHit(modelHit && allowScrollZoom);
            DragAndClickFrame(pt, modelHit);
            ScrollZoomFrame(modelHit);
            ScaleSmoothFrame();
            PassthroughFrame(modelHit || ChatUiHitFrame());
            SaveDebounceFrame();
        }

        /// <summary>光标是否在聊天对话元素（输入条/气泡）上（2026-08-29 桌面对话补齐）：输入期这些
        /// 区域必须非穿透（uGUI 要吃到点击/聚焦输入框）；未开输入条时恒 false（气泡只读不挡桌面）。</summary>
        private bool ChatUiHitFrame()
        {
            if (_聊天UI == null || !_聊天UI.IsInputVisible) return false;
            if (!TryGet光标Unity屏幕位置(out Vector2 cursor)) return false;
            return _聊天UI.IsPointOnInputBar(cursor) || _聊天UI.IsPointOnBubble(cursor);
        }

        /// <summary>置顶守卫（2026-08-28）：点击任务栏/开始菜单等 shell 激活时，Windows 会把任务栏
        /// 抬到 topmost 链内我们之上（同为 WS_EX_TOPMOST 的窗口间 Z 序重排，EXSTYLE 位不变）——
        /// 派蒙坐任务栏时下半身被任务栏遮挡的根因（用户实测报障）。0.5s 低频检测：沿 Z 序向上走
        /// 几步查有没有 Shell_TrayWnd/Shell_SecondaryTrayWnd 插进来，有→重设 HWND_TOPMOST 挂回
        /// 链顶（SWP_NOACTIVATE 不抢焦点，NOMOVE|NOSIZE 不动位置）。**只对任务栏类窗口触发**：
        /// 不与其它 topmost 应用（置顶播放器/截图工具等）打 Z 序战争——对方也周期重设会死循环。
        /// 恒在链顶时 GW_HWNDPREV 返回 NULL，零写放大。VPet 源码核验：其 Topmost 一次性设置无守卫
        /// （WPF 同款问题），它无"坐任务栏"场景故未暴露——本项目边缘坐强需求此守卫。</summary>
        private void TopmostGuardFrame()
        {
            if (Time.unscaledTime - _上次置顶检查 < 0.5f) return;
            _上次置顶检查 = Time.unscaledTime;

            IntPtr above = GetWindow(hwnd, GW_HWNDPREV);
            for (int i = 0; i < 8 && above != IntPtr.Zero; i++)
            {
                _类名缓存.Clear();
                if (GetClassName(above, _类名缓存, _类名缓存.Capacity) > 0)
                {
                    string cls = _类名缓存.ToString();
                    if (cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd")
                    {
                        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                        return;
                    }
                }
                above = GetWindow(above, GW_HWNDPREV); // 继续向上找（任务栏可能不紧邻）
            }
        }

        /// <summary>防隐形守卫（2026-08-26）：Win+D/显示桌面/显示器休眠重排等系统事件会把窗口停靠到
        /// 屏外停车位（实测 -16384,-16384，IsIconic=False——不是真最小化，SW_RESTORE 拉不回），
        /// 桌宠置顶常驻"看不见=死亡"。0.5s 低频体检：iconic→复活（不抢焦点）；整窗与虚拟屏
        /// 零交集且非用户主动拖拽/物理收尾→拉回屏内（复用松手防丢）。用户交互期不干预
        /// （拖拽无屏边钳制是 2026-08-25 拍板）。</summary>
        private void WindowHealthCheckFrame()
        {
            if (Time.unscaledTime - _上次窗口体检 < 0.5f) return;
            _上次窗口体检 = Time.unscaledTime;
            bool 用户在移动 = dragging || (dragPhysics != null && dragPhysics.IsActive);
            if (用户在移动) return;
            if (IsIconic(hwnd))
            {
                ShowWindow(hwnd, SW_SHOWNOACTIVATE);
                return;
            }
            GetWindowRect(hwnd, out RECT wr);
            int vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            if (wr.Right <= vx || wr.Left >= vx + vw || wr.Bottom <= vy || wr.Top >= vy + vh)
                PostDragReelIn();
        }

        /// <summary>命中检测：光标→客户区物理像素→Unity 屏幕坐标→射线 vs 烘焙蒙皮碰撞体。
        /// 输出光标屏幕物理像素 pt（拖拽/双击共用），返回=是否命中模型（穿透/滚轮/抓取判定源）。</summary>
        private bool GetHitState(out POINT pt)
        {
            GetCursorPos(out pt);
            GetClientRect(hwnd, out RECT clientRect);
            var origin = GetClientOriginScreen();
            int winW = clientRect.Right - clientRect.Left;
            int winH = clientRect.Bottom - clientRect.Top;
            int clientX = pt.X - origin.X;
            int clientY = pt.Y - origin.Y;

            bool inWindow = clientX >= 0 && clientX < winW && clientY >= 0 && clientY < winH;
            if (!inWindow || cam == null || hitMeshCollider == null) return false;
            // 客户区物理像素（=渲染像素，PerMonitorV2）→ Unity 屏幕坐标（左下原点）
            float sx = clientX * ((float)Screen.width / winW);
            float sy = clientY * ((float)Screen.height / winH);
            float unityY = Screen.height - sy;
            Ray ray = cam.ScreenPointToRay(new Vector3(sx, unityY, 0f));
            return hitMeshCollider.Raycast(ray, out _, 100f);
        }

        /// <summary>一次性补烘与烘焙暂停机制已上移 PetHostBase（两形态同款，2026-08-28 共用化）。</summary>

        /// <summary>拖拽与双击交互帧：全局轮询左键（不依赖窗口焦点）——双击退出、单击开对话
        /// （2026-08-29：过 0.4s 双击窗口才开，不与双击退出互抢）、拖拽起手/每帧/松手、拎起姿势基准
        /// 角平滑。抓住模型后由物理组件接管窗口定位。
        /// 帧序铁律（2026-08-29 游戏内形态双报障教训）：物理推进（含收尾）**不受聊天输入门控**——
        /// 输入期停摆收尾=dragPhysics.IsActive 永真→行为层卡 Drag01 循环+视线静默=永久拎起姿势+
        /// 头不跟踪鼠标。聊天门控只拦"新拖拽起手/单击判定"。</summary>
        private void DragAndClickFrame(POINT pt, bool modelHit)
        {
            bool lmbDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
            bool lmbPressed = lmbDown && !prevLmbDown;

            // 双击派蒙退出（独立存活的关闭方式）：在拖拽启动前判定，两次命中单击间隔 <0.4s
            // 启动冷却 1s：防进程启动瞬间误吞"上一次双击退出旧进程"的残余按键状态（2026-08-23 实测）
            if (lmbPressed && modelHit && Time.unscaledTime > 1f)
            {
                if (allowDoubleClickExit && !exitRequested && Time.unscaledTime - lastClickTime < 0.4f)
                {
                    _单击待开对话时刻 = -1f; // 双击退出优先：取消待开的对话
                    Debug.Log("[PetWindow] 双击退出，桌宠再见");
                    exitRequested = true;
                    // 退场动画（2026-08-24）：先播退场动画再真正退出；无动画可用则立即退出
                    bool exitTakeover = behaviorCtrl != null && behaviorCtrl.RequestExit(Application.Quit);
                    if (!exitTakeover) Application.Quit();
                }
                lastClickTime = Time.unscaledTime;
                dragStartCursor = new Vector2Int(pt.X, pt.Y);
            }

            // ---- 拖拽物理（docs/19 §6.1 刚体跟随+四肢摆动）：身体 1:1 直跟光标（无任何摆动），
            // 四肢由物理组件的跟拍弹簧摆动（LateUpdate 应用到肩/大腿骨）。松手即停原地收尾。
            if (dragPhysics != null && dragPhysics.IsActive)
            {
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
                        dragPhysics.Release(); // 松手即停：骨盆冻结原地，四肢弹簧收尾归零
                    }
                    else
                    {
                        dragPhysics.DragFrame(new Vector2(pt.X, pt.Y), Time.unscaledDeltaTime);
                    }
                }
                else
                {
                    dragPhysics.SettleFrame(Time.unscaledDeltaTime);
                }

                if (dragPhysics.IsActive)
                {
                    // 拎起姿势帧（共用基类）：角平滑+根旋转（含挣扎摆扭+绕骨盆枢轴补偿）；
                    // 拖拽期（拎起中）窗口按"骨盆客户区投影钉物理目标位"定位，收尾期窗口冻结
                    bool lifting = dragPhysics.phase == PetDragPhysicsController.DragPhase.拖拽;
                    LiftPoseAngleFrame(lifting);
                    if (lifting) PositionWindowByPelvis();
                }
                else PhysicsSettle();
            }

            // ---- 聊天输入期：点击对话元素（输入条/气泡）以外任何地方=关闭对话（2026-08-29 用户拍板，
            // 游戏内形态同款）；不起新拖拽/不推进待定按下。全局轮询看得见穿透到别处的点击——
            // 点其它应用同样收对话。物理收尾已在上方无条件推进（勿挪进门控内）。
            if (_聊天UI != null && _聊天UI.IsInputVisible)
            {
                if (lmbPressed)
                {
                    bool 在对话元素上 = TryGet光标Unity屏幕位置(out Vector2 cursor)
                        && (_聊天UI.IsPointOnInputBar(cursor) || _聊天UI.IsPointOnBubble(cursor));
                    if (!在对话元素上) _聊天UI.CloseChat();
                }
                _pressPending = false;
                prevLmbDown = lmbDown;
                return;
            }

            // ---- 单击待开对话：0.4s 双击窗口过后才开（窗口内来了第二次点击=退出路径已取消）
            if (_单击待开对话时刻 > 0f && Time.unscaledTime >= _单击待开对话时刻)
            {
                _单击待开对话时刻 = -1f;
                if (!exitRequested) _聊天UI?.ToggleInput();
            }

            // ---- 按下待定（2026-08-29 移植游戏内单击阈值）：命中模型按下不立刻起手——按住超时
            // （0.15s）或位移超阈值（8px）才升级为真拖拽；期间松手且几乎没动=单击（对话在双击窗口
            // 后开，见上）。单击全程零姿势零物理（旧版按下即抓=每次单击闪拎起姿势，开对话高频后不可接受）。
            if (_pressPending)
            {
                if (!lmbDown)
                {
                    _pressPending = false;
                    if (Time.unscaledTime - _单击按下时刻 <= 0.15f
                        && Mathf.Abs(pt.X - _单击按下pt.X) + Mathf.Abs(pt.Y - _单击按下pt.Y) < 8)
                    {
                        _单击待开对话时刻 = Time.unscaledTime + 0.4f;
                    }
                }
                else if (Time.unscaledTime - _单击按下时刻 > 0.15f
                         || Mathf.Abs(pt.X - _单击按下pt.X) + Mathf.Abs(pt.Y - _单击按下pt.Y) >= 8)
                {
                    _pressPending = false;
                    if (!exitRequested)
                    {
                        dragging = true;
                        BeginPhysicalDrag(pt);
                    }
                }
            }
            else if (!dragging && !exitRequested && modelHit && lmbPressed && dragPhysics != null)
            {
                _pressPending = true;
                _单击按下时刻 = Time.unscaledTime;
                _单击按下pt = pt;
            }
            prevLmbDown = lmbDown;
        }

        /// <summary>滚轮缩放帧：WH_MOUSE_LL 钩子截滚轮（穿透/无焦点可靠）；
        /// 仅当光标命中模型时响应，与拖拽一致——避免滚其他窗口/桌面时误缩放。
        /// 只改目标倍率，实际应用走缩放平滑帧（对齐主流桌宠滚轮渐变手感）。</summary>
        private void ScrollZoomFrame(bool modelHit)
        {
            int wheelRaw = System.Threading.Interlocked.Exchange(ref _pendingWheelDelta, 0);
            if (allowScrollZoom && modelHit && paimon根 != null && wheelRaw != 0)
            {
                float scroll = wheelRaw / 120f; // 120=一格，正=向前/上=放大
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    float newScale = Mathf.Clamp(targetScale * Mathf.Pow(scaleStep, scroll), scaleMin, effectiveMaxScale);
                    if (!Mathf.Approximately(newScale, targetScale))
                    {
                        targetScale = newScale;
                        MarkDirty();
                    }
                }
            }
        }

        /// <summary>穿透切换帧：命中模型或正在拖拽时可交互，其余区域点击穿透到下层窗口</summary>
        private void PassthroughFrame(bool modelHit)
        {
            bool wantPassThrough = !modelHit && !dragging;
            if (wantPassThrough == passThroughOn) return;
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
            if (printStateLog)
            {
                Debug.Log($"[PetWindow] 穿透切换 -> {wantPassThrough}");
            }
        }

        /// <summary>存档防抖落盘帧（缩放/拖拽后 1s 无新修改才写，连续滚轮不产生 IO 风暴）</summary>
        private void SaveDebounceFrame()
        {
            if (dirtyUntil > 0f && Time.unscaledTime >= dirtyUntil)
            {
                SavePrefs();
            }
        }
    }
}
