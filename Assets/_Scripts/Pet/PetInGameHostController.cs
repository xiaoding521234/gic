using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using GIC.Framework;

namespace GIC.Pet
{
    /// <summary>
    /// 游戏画面内版宿主控制器 v2（docs/19 §6.4，2026-08-28 全屏画布重构）——挂 PaimonInGameRoot 实例根，
    /// 继承 PetHostBase（2026-08-28 共用化：拎起姿势应用/四肢摆动/命中网格烘焙/缩放平滑/找骨均在基类，
    /// 宿主只保留差异层——RT 画布/Unity 输入轮询/移根钉骨盆/PetPrefs 持久化/屏幕底坐）。
    /// 渲染=全屏 RT 画中画：prefab 内 PreviewCamera 渲到屏幕同尺寸 RenderTexture，
    /// DontDestroyOnLoad 的 ScreenSpaceOverlay Canvas + 全屏 RawImage 显示（高 sortingOrder 悬浮层）。
    ///
    /// v2 全屏画布（2026-08-28 用户拍板"画布范围=整个游戏画面"，根治放大到最大被 750×825 小画布截断）：
    /// - 旧版：RawImage 750×825 小画布在屏幕上移动，模型在 RT 内恒定——放大超过画布即被裁。
    /// - 新版：RT=整屏，模型根在 RT 相机视野内移动（相机静止）——视野=全屏，放大到上限也不截。
    /// 与桌面版的窗口体制对应：桌面=固定窗口+模型在窗内演+窗口移动；游戏内=固定"窗口"=整屏+模型移动。
    ///
    /// 交互=全轮询（对齐桌面版 Win32 轮询语义：GetCursorPos/GetAsyncKeyState 的 Unity 等价物——
    /// Input.mousePosition/GetMouseButton/mouseScrollDelta，不依赖 EventSystem 焦点）：
    /// - 命中检测：光标→RT 视口→射线 vs 烘焙蒙皮 MeshCollider（像素级，桌面版同配方）
    /// - 事件挡板：全屏透明 Image，命中派蒙时 raycastTarget=true 吃掉主游戏点击（桌面版
    ///   WS_EX_TRANSPARENT 穿透切换的同构——命中模型=可交互不穿透，未命中=穿透到游戏 UI）
    /// - 拖拽物理：复用 PetDragPhysicsController（四肢跟拍弹簧+挣扎），拎起姿势应用层
    ///   在基类 PetHostBase.拎起姿势角帧（2026-08-28 修复"拖拽时不转身"后合一）
    ///
    /// 缩放基准适配：全屏视野下模型基准 localScale ×(等效画布逻辑高/屏幕参考逻辑高)（825/1080），
    /// 保持屏幕显示尺寸与桌面版 750×825 逻辑窗口一致；缩放上限动态钳制（模型最大屏高占比 ≤95%，
    /// 对齐桌面版"工作区 95% 预算"语义——放大到最大恰好占满屏不截断）。
    /// 持久化：PetPrefs 游戏内字段（骨盆归一化屏幕位+缩放，分辨率无关）。
    /// </summary>
    public class PetInGameHostController : PetHostBase, IGestureSurface
    {
        [Header("引用（空=自动找）")]
        [InspectorName("派蒙相机")]
        [SerializeField] private Camera petCamera;

        [Header("全屏画布")]
        [Tooltip("RT 超采样倍率（1=与屏幕 1:1 像素；2=4K 屏超采样，全屏 RT 体积大按需开）")]
        [InspectorName("RT倍率")]
        [SerializeField, Range(1f, 2f)] private float rtMultiplier = 1f;

        [Header("缩放（其余缩放参数在 PetHostBase）")]
        [Tooltip("模型最大屏高占比（动态缩放上限的预算，对齐桌面版工作区 95% 语义）")]
        [SerializeField, Range(0.5f, 1f)] private float maxScreenHeightRatio = 0.95f;

        [Header("基准适配（全屏视野折算）")]
        [Tooltip("等效画布逻辑高：桌面版窗口逻辑高 825——全屏视野下模型基准缩放按 825/屏幕参考逻辑高 折算，保持屏幕显示尺寸与桌面版一致")]
        [InspectorName("等效画布逻辑高")]
        [SerializeField] private float equivCanvasH = 825f;
        [Tooltip("屏幕参考逻辑高（CanvasScaler 旧参考分辨率高度）")]
        [InspectorName("屏幕参考逻辑高")]
        [SerializeField] private float screenRefH = 1080f;

        [Header("命中")]
        [Tooltip("松手防丢：骨盆完全出屏时拉回屏内的边距（归一化）")]
        [InspectorName("防丢边距")]
        [SerializeField] private float antiLossMargin = 0.05f;

        [Header("屏幕边缘坐（游戏内形态专属，桌面版走 PetEdgeSitController）")]
        [Tooltip("松手时骨盆在sitLine上方多少屏幕像素内触发坐（与桌面版 PetEdgeSitController 同参 120px；旧 0.15 归一化在高分屏≈300px+ 太大）")]
        [InspectorName("上吸附范围像素")]
        [SerializeField] private float snapRangeTopPx = 120f;
        [Tooltip("sitLine下方多少屏幕像素内松手也算坐（与桌面版同参 60px；骨盆被拖出屏底一小段的兜底）")]
        [InspectorName("下吸附范围像素")]
        [SerializeField] private float snapRangeBottomPx = 60f;
        [Tooltip("坐姿动画名（与桌面版 PetEdgeSitController 同款 SitUpright）")]
        [InspectorName("坐姿动作名")]
        [SerializeField] private string sitAnimName = "Ani_NPC_Kanban_Paimon_SitUpright";
        [Tooltip("坐定后骨盆距屏幕底部的归一化偏移=sitLine高度，对齐桌面版坐任务栏顶——任务栏高度随 DPI 等比（48px@96dpi≈4.4% 屏高），0.045≈任意 DPI 的任务栏顶线；旧值 0.02 目检坐得太低（沉进屏底）")]
        [InspectorName("坐定底部偏移")]
        [SerializeField] private float sitBottomOffset = 0.045f;
        [Tooltip("落座后逐帧贴正骨盆的时长（秒）——与桌面版 坐定贴正秒 同参：站→坐姿势过渡期骨盆渐降，逐帧钉回sitLine=平滑落座观感（旧版一次性摆位无贴正）")]
        [InspectorName("坐定贴正秒")]
        [SerializeField] private float sitSettleSec = 1.2f;
        [Tooltip("坐定中滚轮缩放时切回的站立待机动作（桌面版 PetEdgeSitController.站立动作名 同参——缩放露馅修正：模型绕脚底长高，骨盆钉sitLine的位置不动→屁股浮离sitLine；缩放稳定后重新落座）")]
        [InspectorName("站立动作名")]
        [SerializeField] private string standAnimName = "Ani_NPC_Kanban_Paimon_Standby";
        [Tooltip("坐定中缩放后等多久算稳定（秒）——平滑过渡结束+宽限后重新落座（桌面版 0.3s 同参）")]
        [InspectorName("缩放稳定宽限秒")]
        [SerializeField] private float scaleStableGraceSec = 0.3f;

        // ---- 运行时状态 ----
        private RenderTexture _rt;
        private RawImage _screenImage;          // 全屏显示层（raycastTarget 恒 false）
        private Canvas _canvas;            // 画中画 Canvas（对话 UI 接线用）
        private Image _eventBlocker;             // 全屏透明事件挡板（raycastTarget 动态=命中状态）
        private float worldPerPixel;      // 相机平面世界单位 / 屏幕像素（k=H/Screen.height）
        private Vector2Int _lastScreenSize;
        // behaviorCtrl/_chatUI 已上移 PetHostBase（批 5 下沉；本类 Awake 里 GetComponentInChildren 赋值）

        // 拖拽状态（docs/24 P4：起手/升级/单击判定移交 DragRecognizer(OnSlopOrHold)——
        // 按住 150ms 升级=GestureMetrics.HoldToDragTimeout（原拍板值），位移阈值=双档 slop；本类只留响应）
        private bool dragging;
        private Vector2 dragStartPointerRT;      // RT 像素系（屏幕点×RT/屏比）
        private Vector2 dragStartPelvisRT;     // 起手骨盆屏幕投影（RT 像素系）——骨盆钉位基准

        // ── 手势层接线（docs/24 P4）──
        [Autowired] private GestureHub _gestureHub;
        private readonly DragRecognizer _dragRecognizer = new DragRecognizer(DragBeginMode.OnSlopOrHold);
        private readonly List<GestureRecognizer> _recognizers = new List<GestureRecognizer>();
        private bool _chatClosedByThisPress; // 本次按下已承载"关对话"：不得再起拖/判单击（旧分支② return 语义）

        // 双指捏合缩放（移动端，2026-09-01）：滚轮缩放的触屏等价。桌面形态=Win32 单光标进程
        // 无多指输入，属形态本质差异允许单侧（双形态对齐口径见 docs/19 §6）
        private bool _pinching;
        private float _pinchStartDist;     // 起手两指屏距（px）——比例式缩放基准
        private float _pinchStartScale;    // 起手目标倍率

        // ---- 三连击切换形态（2026-09-01，桌面版同构手势；连击状态机在 PetHostBase 共用；
        //      游戏内宿主在主进程内直调切换，无需 IPC） ----

        [Tooltip("三连击派蒙切换为桌面形态（0.4s 内第 3 次命中单击触发，播退场动画后拉起桌面版）")]
        [InspectorName("允许三连击切换形态")]
        [SerializeField] private bool allowTripleClickSwitch = true;

        // 对话（2026-08-28）：单击弹输入框+流式回复气泡；组件随 prefab 预挂（素材/字体接线在 prefab）
        // _chatUI 在 PetHostBase；此处仅本形态的锚点辅助引用
        private Transform _headBone;              // 气泡锚点（头骨屏幕投影）

        private float dirtyUntil = -1f;

        // 屏幕边缘坐状态（游戏内形态专属；PetBehaviorController 经 IPetHost.坐定中 切换待机动作）
        private bool _screenSeated;
        private float _sitX;               // 贴正期保持的水平位置
        private float _settleUntil = -10f;  // 坐定贴正秒 内逐帧钉骨盆到sitLine（站→坐过渡平滑）
        // 坐定中缩放重坐（桌面版 缩放调整中 状态的等价，2026-08-28 移植）
        private bool _scaleAdjusting;
        private float _lastScale = -1f;      // 坐定中监听的目标缩放（-1=未初始化）
        private float _scaleStableAt = -10f;  // 缩放过渡结束后的重新落座时刻（<0=未起算）
        /// <summary>屏幕坐定中（兼容保留的公开查询）</summary>
        public bool IsScreenSeated => _screenSeated;
        /// <summary>坐姿动作名（兼容保留的公开查询）</summary>
        public string ScreenSitAnim => sitAnimName;

        public override bool IsDragging => dragging || PhysicsBusy;
        public override bool IsSeated => _screenSeated;
        public override string SitAnim => sitAnimName;
        protected override string logTag => "[PetInGame]";

        void Awake()
        {
            petCamera = petCamera != null ? petCamera : GetComponentInChildren<Camera>(true);
            dragPhysics = dragPhysics != null ? dragPhysics : GetComponentInChildren<PetDragPhysicsController>(true);
            if (bodyRenderer == null) bodyRenderer = FindBodyRenderer(transform);
            behaviorCtrl = GetComponentInChildren<PetBehaviorController>(true);
            var paimonGo = transform.Find("Paimon");
            paimonRoot = paimonRoot != null ? paimonRoot : (paimonGo != null ? paimonGo : transform).transform;

            if (petCamera != null)
            {
                // 安全带（2026-08-28 背景"缩的很小"根因）：prefab 相机 tag 遗留 MainCamera——常驻实例
                // 抢占 Camera.main，MainHall 的 BackgroundParallax3D.FitToScreen（正交铺屏）解析到本相机
                // （orthoSize 0.7）会把大厅背景缩到 ~0.13 倍（编辑器直开场景 Play 无派蒙实例故正常）。
                // prefab 已改 Untagged，此行防 prefab 被旧版本覆盖回 MainCamera。
                if (petCamera.CompareTag("MainCamera")) petCamera.tag = "Untagged";

                // 透明隔光相机统一配置（两形态同款，PetHostBase）：SolidColor 透明底+关 HDR/MSAA/后处理
                ConfigureTransparentCamera(petCamera, new Color(0f, 0f, 0f, 0f));

                // 透视→正交（2026-08-28 用户复测"越拖到边缘角度越大"根治）：透视投影下模型偏离光轴
                // 即斜视畸变（视野边缘尤甚）；桌面版模型恒在窗口中心=恒正对。正交投影视线处处平行——
                // 模型在屏内任何位置都正对玩家，且世界/屏幕像素比恒定（拖拽/缩放标尺不随位置漂移）。
                // 可见世界高=原透视视野高（FOV 45.27 按模型距离折算），保持模型屏幕占比不变。
                float d = Vector3.Dot(paimonRoot.position - petCamera.transform.position, petCamera.transform.forward);
                if (d <= 0.01f) d = 1f;
                float perspectiveHeight = 2f * d * Mathf.Tan(petCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                petCamera.orthographic = true;
                petCamera.orthographicSize = perspectiveHeight * 0.5f;
            }

            WireDragBones();
            _recognizers.Add(_dragRecognizer);
            WireGestureHandlers();
            // 头骨锚点取 GI 本体（Bip001 系——视线层同款配置）。勿用日文名"頭"：那是 MMD 兜底模型
            // （Paimon_arm，禁用留存）的骨名，找本体骨 只过滤影子壳不过滤它——命中后气泡锚在
            // 不动的 MMD 头上（2026-08-29 首测气泡错位根因之一）
            _headBone = FindBodyBone("Bip001 Head");
            BuildFullScreenPIP();
            BuildHitProxy();
            calcBaseAndLimit();
            restorePrefs();
            WireChat();
        }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            Wargame.Instance?.Context?.Inject(this); // [Autowired] GestureHub（同 BattleExitConfirmDialog 迟到注入模式）
            if (_gestureHub == null)
            {
                Debug.LogWarning("[PetInGame] GestureHub 未注入——拖拽/单击手势不可用（Wargame 未初始化？捏合/滚轮不受影响）");
                return;
            }
            _gestureHub.RegisterSurface(this);
            _gestureHub.AnyPointerBegan += OnPetPointerBegan;
        }

        private void OnDisable()
        {
            if (_gestureHub == null) return;
            _gestureHub.UnregisterSurface(this);
            _gestureHub.AnyPointerBegan -= OnPetPointerBegan;
        }

        // ==================== IGestureSurface（docs/24 §4.4，P4） ====================

        /// <summary>退场中（Disappear 播放期+播完冻结期）不收新指针（旧 interactFrozen 守卫）</summary>
        public bool Enabled => behaviorCtrl == null || !behaviorCtrl.IsExiting;

        /// <summary>桌宠挡板/输入条自身即 UI（raycastTarget 动态开关），不豁免则永远收不到自己的指针</summary>
        public bool BypassUIGate => true;

        /// <summary>元游戏陪伴体：旧全轮询实现从不理 InputLocks，游戏弹窗/转场锁不冻结它</summary>
        public bool IgnoresInputLocks => true;

        /// <summary>
        /// 指针准入=命中烘焙蒙皮碰撞体（像素级，桌面版同配方）+ 聊天输入期不起新手势。
        /// 聊天开着时：本次按下只承载"关对话"（OnPetPointerBegan 内判定），不进手势——输入条/气泡上
        /// 的按下归 EventSystem/气泡自身，命中模型（元素以外）的按下=收起意图，均不绑面（旧分支② return 语义）。
        /// </summary>
        public bool ShouldReceivePointer(in PointerEvent e)
        {
            if (_chatClosedByThisPress) return false;
            if (!GetHitState(e.Position)) return false;
            if (_chatUI != null && _chatUI.IsInputVisible) return false;
            return true;
        }

        public IReadOnlyList<GestureRecognizer> Recognizers => _recognizers;

        /// <summary>识别器回调接线（一次即可——事件订阅跨 OnEnable/OnDisable 存续，注册到 hub 才开始收事件）</summary>
        private void WireGestureHandlers()
        {
            _dragRecognizer.OnDragBegan += OnDragBeganHandler;
            _dragRecognizer.OnDragDelta += OnDragDeltaHandler;
            _dragRecognizer.OnTapCandidate += OnTapCandidateHandler;
            _dragRecognizer.OnDragEnded += pos => EndDrag();
            _dragRecognizer.StateChanged += (oldState, newState) =>
            {
                // 双指起手取代单指（hub CancelSingles）或输入锁外的强制取消：拖拽物理收尾走同一路径
                if (newState == GestureState.Cancelled) EndDrag();
            };
        }

        /// <summary>
        /// 任意指针按下（hub AnyPointerBegan，全部门之前）：承载旧 dragPollFrame 的**按下瞬间**逻辑——
        /// ⓪三连击切换形态（连击状态机 PetHostBase.CountClickChain 与桌面版共用：按下计数、
        /// 第 3 击按下瞬间触发，2026-09-01 拍板保持）②聊天输入期点"对话元素以外"关对话。
        /// 拖拽升级/单击判定不在此处（识别器承担），故无 _pressPending。
        /// </summary>
        private void OnPetPointerBegan(PointerEvent e)
        {
            _chatClosedByThisPress = false;
            if (behaviorCtrl != null && behaviorCtrl.IsExiting) return;
            if (_pinching) return; // 捏合中不计链不关对话（旧：整段 dragPollFrame 被 _pinching 跳过）

            bool modelHit = GetHitState(e.Position);

            // ⓪三连击切换形态：0.4s 窗口内第 3 次命中单击→切桌面形态（置于聊天门控之前——连击期间
            //   含输入条已开的场景照常计数）
            if (modelHit && CountClickChain() && allowTripleClickSwitch)
            {
                _chatUI?.CloseChat(); // 收起输入条（第 1 击单击可能已开）再退场
                Debug.Log("[PetInGame] 三连击：切换为桌面形态");
                PetInGameHost.GestureSwitchTo(PetInGameHost.FormDesktop);
                return; // 退场已起（IsExiting→Enabled=false 冻结后续），本帧不再推进
            }

            // ②聊天输入期：点击"对话元素（输入条/气泡）以外"任何地方=关闭对话（2026-08-29 用户拍板：
            //   点派蒙=原有 Toggle 收起，点游戏其它区域同样收——含气泡判定，回看气泡时不误关）。
            //   三连击进行中（点模型且计数≥2）不收起——第 3 击将触发切换
            if (_chatUI != null && _chatUI.IsInputVisible
                && !_chatUI.IsPointOnInputBar(e.Position) && !_chatUI.IsPointOnBubble(e.Position))
            {
                if (ShouldCloseChatOnClick(modelHit))
                {
                    _chatUI.CloseChat();
                    _chatClosedByThisPress = true; // 本按下只关对话：不起拖/不判单击（旧分支② return 语义）
                }
            }
        }

        // ---- 拖拽/单击响应（判定在 DragRecognizer(OnSlopOrHold)，docs/24 P4）----

        /// <summary>鼠标/指针屏幕坐标 → RT 像素系（rtMultiplier&gt;1 时放大）</summary>
        Vector2 ScreenToRT(Vector2 screenPos) => new Vector2(
            screenPos.x * _rt.width / Mathf.Max(1f, Screen.width),
            screenPos.y * _rt.height / Mathf.Max(1f, Screen.height));

        /// <summary>拖拽起手（识别器宣胜：按住 150ms 或位移过 slop）：快照指针/骨盆 RT 位+根旋转基准
        /// （收尾中被再抓不重取基准，防旋转叠加——桌面版语义）。物理组件喂 RT 像素系。</summary>
        void OnDragBeganHandler(Vector2 screenPos)
        {
            if (dragPhysics == null || _pelvis == null || petCamera == null) return;
            dragging = true;
            ResetClickChain(); // 真实拖拽打断连击计次（防误触切换）
            _screenSeated = false; // 被拖=立即解除坐定
            _scaleAdjusting = false; // 缩放重坐流程一并取消（拖走了自然不重坐）
            dragStartPointerRT = ScreenToRT(screenPos);
            SnapshotDragBaseline(false); // 根旋转基准（模型位置=拖拽结果，不快照还原基准）
            dragStartPelvisRT = petCamera.WorldToScreenPoint(_pelvis.position);
            dragPhysics.BeginDrag(dragStartPointerRT, dragStartPelvisRT);
        }

        void OnDragDeltaHandler(Vector2 delta, Vector2 screenPos)
        {
            if (dragPhysics == null || !dragPhysics.IsActive) return;
            dragPhysics.DragFrame(ScreenToRT(screenPos), Time.unscaledDeltaTime);
            LiftPoseAngleFrame(true);   // 姿势旋转（基类：角平滑+挣扎+绕骨盆枢轴补偿）
            PelvisPinToTarget();         // 骨盆钉位（刚体 1:1 直跟）
        }

        /// <summary>拖拽收口（正常抬起/取消同路）：松手即停——骨盆冻结原地，四肢弹簧收尾归零。
        /// 收尾帧（SettleFrame→PhysicsSettle）在 Update 推进。</summary>
        void EndDrag()
        {
            if (!dragging) return;
            dragging = false;
            dragPhysics?.Release();
        }

        /// <summary>单击（早退点击候选：未过 150s 也未过 slop 即抬起）——立即开对话
        /// （2026-09-01 用户拍板"对话框立刻出现"；三连击兼容：第 1 击开输入条后，第 2 击在
        /// OnPetPointerBegan 的关对话判定中被计数≥2 豁免，第 3 击触发切换）</summary>
        void OnTapCandidateHandler(Vector2 screenPos)
        {
            if (_chatClosedByThisPress) return; // 本次按下已关对话：不再 Toggle（旧一按只收起语义）
            _chatUI?.ToggleInput();
        }

        // ---- 对话装配差异钩子（共用主体在 PetHostBase.WireChat，2026-08-31 批 5 下沉） ----

        /// <summary>聊天画布=既有画中画 Canvas（BuildFullScreenPIP 建；null=异常态不接）。
        /// Canvas 由宿主直传不挪物体（2026-08-29 修单击 NRE：组件挂 prefab 根=宿主根，SetParent
        /// 挪根进自己的子 Canvas=循环父子被拒→自禁用→单击必炸）。</summary>
        protected override Canvas EnsureChatCanvas() => _canvas;

        /// <summary>头锚点：模型包围盒顶（x 跟头骨水平位）→ RT 相机屏幕位 → ×(屏幕/RT) 换算 Canvas 坐标
        ///（ConstantPixelSize=屏幕像素系）。改锚包围盒顶=任意缩放恒在头顶之上、抬手类动作气泡随让位
        ///（2026-08-29 教训：锚头骨+固定偏移，Q 版头高出骨锚 100px+，气泡底压在头发/脸上）。</summary>
        protected override Vector2 ChatHeadAnchor()
        {
            if (petCamera == null || hitMeshCollider == null || hitMeshCollider.sharedMesh == null) return Vector2.zero;
            var bounds = hitMeshCollider.bounds;
            float x = _headBone != null ? _headBone.position.x : bounds.center.x; // 水平跟头骨（歪头/侧移气泡跟脸）
            Vector3 sp = petCamera.WorldToScreenPoint(new Vector3(x, bounds.max.y, bounds.center.z));
            return new Vector2(sp.x * Screen.width / Mathf.Max(1f, _rt.width), sp.y * Screen.height / Mathf.Max(1f, _rt.height));
        }

        /// <summary>脚锚点：模型包围盒底中心 → RT 相机屏幕位 → ×(屏幕/RT) 换算——输入条挂模型脚底下方</summary>
        protected override Vector2 ChatFootAnchor()
        {
            if (hitMeshCollider == null || petCamera == null) return Vector2.zero;
            var bounds = hitMeshCollider.bounds;
            Vector3 sp = petCamera.WorldToScreenPoint(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
            return new Vector2(sp.x * Screen.width / Mathf.Max(1f, _rt.width), sp.y * Screen.height / Mathf.Max(1f, _rt.height));
        }

        /// <summary>Intent 工具执行分发（游戏内差异）：跑在主进程内——直调执行（与桌面形态 IPC 转发行为对齐）</summary>
        protected override string ExecuteChatTool(string toolName, string toolArgsJson)
        {
            return GIC.Pet.Chat.PetChatIntent.Execute(toolName, toolArgsJson);
        }

        /// <summary>全屏画中画：RT=屏幕尺寸×rtMultiplier；RawImage 铺满全屏（raycastTarget=false，
        /// 交互走轮询）；挡板=全屏透明 Image（命中派蒙时开 raycastTarget 吃掉主游戏点击，
        /// 未命中穿透——桌面版 WS_EX_TRANSPARENT 穿透切换的同构）。</summary>
        void BuildFullScreenPIP()
        {
            if (petCamera == null) { Debug.LogWarning("[PetInGame] 无相机，画中画未建"); return; }

            var canvasGo = new GameObject("PetInGameCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500; // 悬浮层：高于常规 UI（弹窗走更高/独立层如需遮挡再调）
            _canvas = canvas;
            // ConstantPixelSize：RawImage 直接以屏幕像素铺满（全屏 RT 1:1 显示，无缩放损失）
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            var imageGo = new GameObject("PetImage", typeof(RectTransform), typeof(RawImage));
            imageGo.transform.SetParent(canvasGo.transform, false);
            _screenImage = imageGo.GetComponent<RawImage>();
            var rt = imageGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _screenImage.raycastTarget = false; // 显示层不吃事件——交互全走轮询+挡板

            var blockerGo = new GameObject("PetEventBlocker", typeof(RectTransform), typeof(Image));
            blockerGo.transform.SetParent(canvasGo.transform, false);
            var brt = blockerGo.GetComponent<RectTransform>();
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = brt.offsetMax = Vector2.zero;
            _eventBlocker = blockerGo.GetComponent<Image>();
            _eventBlocker.color = new Color(0f, 0f, 0f, 0f); // 全透明纯挡事件
            _eventBlocker.raycastTarget = false;             // 默认穿透；命中派蒙时逐帧开

            RebuildRT();
            _lastScreenSize = new Vector2Int(Screen.width, Screen.height);
            Debug.Log($"[PetInGame] 全屏画中画建立 screen={Screen.width}x{Screen.height} rt={(int)(Screen.width * rtMultiplier)}x{(int)(Screen.height * rtMultiplier)}");
        }

        void RebuildRT()
        {
            int w = Mathf.Max(2, Mathf.RoundToInt(Screen.width * rtMultiplier));
            int h = Mathf.Max(2, Mathf.RoundToInt(Screen.height * rtMultiplier));
            if (_rt != null)
            {
                petCamera.targetTexture = null;
                _rt.Release();
                Destroy(_rt);
            }
            _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Bilinear };
            _rt.Create();
            petCamera.targetTexture = _rt;
            petCamera.enabled = true;
            if (_screenImage != null) _screenImage.texture = _rt;
        }

        /// <summary>算基准缩放（prefab 值 × 等效画布/屏幕参考 折算——保持屏幕显示尺寸与桌面版一致）
        /// 与动态缩放上限（模型最大屏高占比 ≤95%——放大到最大恰好不截屏）+ 世界/屏幕像素比。
        /// 每次补烘后重算（姿势微变 h 微变，上限跟随）。</summary>
        void calcBaseAndLimit()
        {
            if (paimonRoot == null || petCamera == null) return;
            float convert = screenRefH > 1f ? equivCanvasH / screenRefH : 1f;
            baseScale = paimonRoot.localScale.x * convert;
            worldPerPixel = GetFrustumHeight() / Mathf.Max(1f, Screen.height);
            UpdateEffectiveMaxScale();
        }

        float GetFrustumHeight()
        {
            if (petCamera == null) return 1f;
            if (petCamera.orthographic) return petCamera.orthographicSize * 2f; // 正交：恒定可见高
            float d = Vector3.Dot(paimonRoot.position - petCamera.transform.position, petCamera.transform.forward);
            if (d <= 0.01f) d = 1f;
            return 2f * d * Mathf.Tan(petCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        }

        void UpdateEffectiveMaxScale()
        {
            float upperLimit = scaleMax;
            if (hitMeshCollider != null && hitMeshCollider.sharedMesh != null)
            {
                float h = hitMeshCollider.bounds.size.y; // 世界包围盒高（烘焙姿势）
                if (h > 0.01f)
                {
                    float H = GetFrustumHeight();
                    upperLimit = Mathf.Min(upperLimit, maxScreenHeightRatio * H / h);
                }
            }
            effectiveMaxScale = Mathf.Max(upperLimit, scaleMin);
        }

        /// <summary>补烘完成钩子：烘焙姿势变→模型高变→动态缩放上限跟随重算</summary>
        protected override void OnBakeCompleted() => UpdateEffectiveMaxScale();

        void Update()
        {
            resolutionChangedFrame();
            // 退场中（热切换/退场的 Disappear 播放期+播完冻结期）交互全冻结——Enabled=false 让 hub
            // 不派发新手势；在途识别器逐帧强制取消（防退场起手瞬间按住升级成拖拽"抓住正在退场的模型"，
            // 2026-08-28 用户要求"播完就杀"配套）
            bool interactFrozen = behaviorCtrl != null && behaviorCtrl.IsExiting;
            bool modelHit = !interactFrozen && GetHitState(Input.mousePosition);
            RebakeHitMeshFrame();
            ScaleSmoothFrame();
            if (!interactFrozen)
            {
                PinchZoomFrame();
                if (!_pinching)
                    ScrollZoomFrame(modelHit);
            }
            else
            {
                _dragRecognizer.ForceCancel(); // 退场冻结期作废在途按下/拖拽（旧 _pressPending=false 同语义）
                if (_pinching) EndPinch();
            }
            // 拖拽物理收尾帧（拖拽中的 DragFrame 已移入 OnDragDeltaHandler；本处只管松手后的
            // SettleFrame 收尾——旧 dragPollFrame ①段收尾分支：捏合中/退场中不推进，行为保持）
            if (!interactFrozen && !_pinching && dragPhysics != null && dragPhysics.IsActive && !dragging)
            {
                bool settleDone = !dragPhysics.SettleFrame(Time.unscaledDeltaTime);
                LiftPoseAngleFrame(false); // 收尾期角度平滑归零
                if (!dragPhysics.IsActive || settleDone) PhysicsSettle();
            }
            blockerToggleFrame(modelHit);
            sitSettleFrame();
            sitScaleResitFrame();
            if (dirtyUntil > 0f && Time.unscaledTime >= dirtyUntil) SavePrefs();
        }

        /// <summary>坐定中缩放重坐（桌面版 PetEdgeSitController 缩放调整中 状态的等价，2026-08-28 移植）：
        /// 模型绕脚底长高而sitLine不动，坐定中滚轮缩放→屁股浮离sitLine（放大上浮/缩小下沉）。处理=坐定中
        /// 检测目标缩放变化→切站立待机+清坐定标志→缩放平滑落稳+宽限后按原水平位重新落座（贴正钉回）。</summary>
        void sitScaleResitFrame()
        {
            if (_scaleAdjusting)
            {
                if (!Mathf.Approximately(targetScale, _lastScale)) { _lastScale = targetScale; _scaleStableAt = -10f; } // 连续滚轮：重等稳定
                if (ScaleTransitioning()) { _scaleStableAt = -10f; return; }               // 平滑过渡进行中
                if (_scaleStableAt < 0f) { _scaleStableAt = Time.unscaledTime + Mathf.Max(0f, scaleStableGraceSec); return; } // 刚到位，宽限
                if (Time.unscaledTime < _scaleStableAt) return;
                _scaleAdjusting = false;
                reSit();
                return;
            }
            if (!_screenSeated) return;
            if (!Mathf.Approximately(targetScale, _lastScale))
            {
                _lastScale = targetScale;
                _scaleAdjusting = true;
                _scaleStableAt = -10f;
                _screenSeated = false; // 行为层待机切回站立（切动画见下）
                var swapper = paimonRoot != null ? paimonRoot.GetComponent<PetAnimSwapper>() : null;
                if (swapper != null && swapper.HasAnim(standAnimName))
                    swapper.Play(standAnimName);
                Debug.Log("[PetInGame] 坐定中缩放：切站立待机，稳定后重新落座");
            }
        }

        /// <summary>缩放显示值未追上目标（对齐桌面版 缩放过渡中 语义）</summary>
        bool ScaleTransitioning() => !Mathf.Approximately(displayScale, targetScale);

        /// <summary>按原水平位重新落座（缩放后骨盆高度变了，重新贴sitLine）</summary>
        void reSit()
        {
            _screenSeated = true;
            _sitX = Mathf.Clamp01(_sitX);
            PlaceToNormalized(new Vector2(_sitX, sitBottomOffset));
            _settleUntil = Time.unscaledTime + sitSettleSec;
            _lastScale = targetScale;
            var swapper = paimonRoot != null ? paimonRoot.GetComponent<PetAnimSwapper>() : null;
            if (swapper != null && swapper.HasAnim(sitAnimName))
                swapper.Play(sitAnimName);
        }

        /// <summary>坐定贴正（桌面版 坐定帧 的贴正段同构）：落座后 坐定贴正秒 内逐帧把骨盆钉在
        /// (_sitX, 坐定底部偏移)——站→坐姿势过渡期骨盆渐降，根随之下移=她"缓缓坐进去"。
        /// 退场中跳过（Disappear 是飞离编排，贴正会把退场动画拽回sitLine）。被拖拽=拖拽起手已清坐定。</summary>
        void sitSettleFrame()
        {
            if (!_screenSeated || Time.unscaledTime >= _settleUntil) return;
            if (behaviorCtrl != null && behaviorCtrl.IsExiting) return;
            PlaceToNormalized(new Vector2(_sitX, sitBottomOffset));
        }

        /// <summary>分辨率变化（全屏切换/窗口拖拽 resize）→ 重建 RT+重摆位+像素比重算</summary>
        void resolutionChangedFrame()
        {
            if (Screen.width == _lastScreenSize.x && Screen.height == _lastScreenSize.y) return;
            _lastScreenSize = new Vector2Int(Screen.width, Screen.height);
            RebuildRT();
            worldPerPixel = GetFrustumHeight() / Mathf.Max(1f, Screen.height);
            UpdateEffectiveMaxScale();
            // 按骨盆归一化位重摆（模型回到屏幕上原相对位置）
            if (TryGetPelvisNormalizedScreenPos(out Vector2 uv)) PlaceToNormalized(uv);
            Debug.Log($"[PetInGame] 分辨率变化 → {Screen.width}x{Screen.height}，RT 重建+重摆位");
        }

        /// <summary>命中检测：屏幕光标→RT 视口→射线 vs 烘焙蒙皮碰撞体（像素级，桌面版同配方）</summary>
        bool GetHitState(Vector2 screenPoint)
        {
            if (petCamera == null || hitMeshCollider == null || hitMeshCollider.sharedMesh == null) return false;
            if (screenPoint.x < 0f || screenPoint.x > Screen.width || screenPoint.y < 0f || screenPoint.y > Screen.height) return false;
            Ray ray = petCamera.ScreenPointToRay(screenPoint);
            return hitMeshCollider.Raycast(ray, out _, 100f);
        }

        /// <summary>挡板穿透切换（桌面版 穿透切换帧 同构）：命中模型/拖拽中/捏合缩放中=可交互
        /// 挡板吃点击；其余区域点击穿透到主游戏 UI。</summary>
        void blockerToggleFrame(bool modelHit)
        {
            if (_eventBlocker == null) return;
            bool want = modelHit || dragging || _pinching;
            if (_eventBlocker.raycastTarget != want) _eventBlocker.raycastTarget = want;
        }

        // ---- 拖拽/单击（docs/24 P4 迁移至手势层）----
        // （dragPollFrame/PointerRT/dragStart/_pressPending/prevLmbDown 已删——起手升级（150ms 按住
        //  或双档 slop 位移）/单击早退判定在 DragRecognizer(OnSlopOrHold)，按下瞬间逻辑（三连击链/
        //  聊天关对话）在 OnPetPointerBegan（hub AnyPointerBegan），拖拽物理响应在 OnDragBegan/
        //  OnDragDelta/EndDrag，收尾帧在 Update。阈值=GestureMetrics 全局统一拍板。）

        /// <summary>骨盆钉位（桌面版 按骨盆目标定位窗口 的游戏内等价）：目标=起手骨盆+光标位移（刚体 1:1
        /// 直跟），根平移使骨盆投影钉到目标——Drag01 动画微动/旋转残余位移全部被吸收（桌面版靠移窗吸收，
        /// 此处靠移根，视觉等价）。【2026-08-28 修复：旧版只直跟根位置，动画微动致骨盆漂移】</summary>
        void PelvisPinToTarget()
        {
            if (paimonRoot == null || petCamera == null || _pelvis == null) return;
            Vector3 currentProjection = petCamera.WorldToScreenPoint(_pelvis.position);
            Vector2 target = dragPhysics.CurrentPelvisScreen;
            Vector2 dRT = target - (Vector2)currentProjection;
            // RT 像素 → 屏幕像素（rtMultiplier>1 时缩回）→ 世界位移（k 对 x/y 同比）
            float dxScreen = dRT.x * Screen.width / Mathf.Max(1f, _rt.width);
            float dyScreen = dRT.y * Screen.height / Mathf.Max(1f, _rt.height);
            paimonRoot.position += petCamera.transform.right * (dxScreen * worldPerPixel)
                               + petCamera.transform.up * (dyScreen * worldPerPixel);
        }

        /// <summary>物理交互收口：旋转归位（基类默认=骨盆枢轴补偿版——模型位置=拖拽结果保留，只把旋转
        /// 归回基准）+ 松手后评估屏幕边坐/防丢拉回 + 位置落盘。
        /// （桌面版 override=旋转+位置精确还原基准——那是因为桌面模型位置从未被拖拽改（窗口在动）。）</summary>
        void PhysicsSettle()
        {
            DragSettleRestore();
            // 松手后评估屏幕边坐（桌面版 PetEdgeSitController.评估吸附并坐 的游戏内等价）：
            // 骨盆在屏幕底部 坐触发范围 内→磁吸落座播 SitUpright
            if (evalScreenEdgeSit())
            {
                // 已接管：播了坐姿动画
            }
            else
            {
                pelvisReelIn();
            }
            MarkDirty();
        }

        /// <summary>屏幕底部磁吸坐（游戏内形态专属，桌面版 PetEdgeSitController 屏幕锚定分支的等价，
        /// 参数同款）：sitLine=屏底上方 坐定底部偏移（≈桌面版任务栏顶高度）；磁吸窗=sitLine上 120px/下 60px
        /// （桌面版同参，像素语义——归一化随分辨率换算，高分屏不再放大）；落座贴正 骨盆到sitLine+播
        /// SitUpright+置坐定标志。返回 true=已落座接管。拖拽中不触发（拖拽起手已清坐定）。
        /// 坐标系注意：Unity 视口/屏幕 y=0 是**屏底**（y=1 顶部）——sitLine=坐定底部偏移本身；
        /// 2026-08-28 曾误按 y=1 为底写 `1-偏移`，磁吸窗落到屏幕顶部=拖底不坐/拖顶瞬移坐（用户实测报障）。</summary>
        bool evalScreenEdgeSit()
        {
            if (_pelvis == null || petCamera == null || dragPhysics == null) return false;
            Vector3 vp = petCamera.WorldToViewportPoint(_pelvis.position);
            if (vp.z <= 0f) return false;
            float sitLine = sitBottomOffset; // 视口 y=0=屏底，sitLine即"屏底上方偏移量"
            float topRange = snapRangeTopPx / Mathf.Max(1f, Screen.height);
            float bottomRange = snapRangeBottomPx / Mathf.Max(1f, Screen.height);
            if (vp.y < sitLine - bottomRange || vp.y > sitLine + topRange) return false; // 磁吸窗：sitLine上 120px / 下 60px

            // 磁吸：骨盆贴到sitLine（贴正期逐帧钉住，见 坐定贴正帧）
            _sitX = Mathf.Clamp01(vp.x);
            PlaceToNormalized(new Vector2(_sitX, sitBottomOffset));
            _screenSeated = true;
            _settleUntil = Time.unscaledTime + sitSettleSec;
            _lastScale = targetScale; // 落座时同步当前倍率（防陈旧值立即误触发缩放重坐）
            _scaleAdjusting = false;
            // 播坐姿动画
            var swapper = paimonRoot.GetComponent<PetAnimSwapper>();
            if (swapper != null && swapper.HasAnim(sitAnimName))
                swapper.Play(sitAnimName);
            Debug.Log($"[PetInGame] 屏幕边坐触发（骨盆y={vp.y:F2}∈[{sitLine - bottomRange:F2},{sitLine + topRange:F2}]）→ 播 {sitAnimName}");
            return true;
        }

        /// <summary>松手防丢（桌面版 拖拽松手防丢拉回 同款）：骨盆完全出屏才拉回贴边（有交集=用户可及不动）</summary>
        void pelvisReelIn()
        {
            if (_pelvis == null || petCamera == null) return;
            Vector3 vp = petCamera.WorldToViewportPoint(_pelvis.position);
            if (vp.z <= 0f) return;
            float edge = antiLossMargin;
            if (vp.x > -edge && vp.x < 1f + edge && vp.y > -edge && vp.y < 1f + edge) return; // 屏上或贴边
            float nx = Mathf.Clamp(vp.x, edge, 1f - edge);
            float ny = Mathf.Clamp(vp.y, edge, 1f - edge);
            PlaceToNormalized(new Vector2(nx, ny));
        }

        /// <summary>滚轮缩放（桌面版 滚轮缩放帧 同款：仅光标命中模型时响应）。倍率生成在滚轮侧，
        /// 落地与双指捏合共用 ApplyZoom。</summary>
        void ScrollZoomFrame(bool modelHit)
        {
            float scroll = Input.mouseScrollDelta.y;
            if (!modelHit || Mathf.Abs(scroll) < 0.01f) return;
            ApplyZoom(Mathf.Clamp(targetScale * Mathf.Pow(scaleStep, scroll), scaleMin, effectiveMaxScale));
        }

        /// <summary>双指捏合缩放（移动端，2026-09-01）：滚轮缩放的触屏等价——任一指命中模型即起手
        /// （抓着派蒙捏，两指都在身上或一指在旁边都成立），两指张合比例→目标倍率。方向与地图捏合相反：
        /// 正交相机的 size 是"可见范围"（张大=缩小），本处的倍率是放大率（张大=放大）。
        /// 起手接管：取消进行中的拖拽/待定按下/连击计次（两指手势取代单指手势，同地图双指取消单指拖拽）。</summary>
        void PinchZoomFrame()
        {
            if (Input.touchCount != 2)
            {
                if (_pinching) EndPinch();
                return;
            }
            Touch t0 = Input.GetTouch(0);
            Touch t1 = Input.GetTouch(1);

            if (!_pinching)
            {
                if (!GetHitState(t0.position) && !GetHitState(t1.position)) return;
                _pinchStartDist = Vector2.Distance(t0.position, t1.position);
                if (_pinchStartDist < 1f) return; // 两指同点无比例基准，等分离后再起手
                _pinching = true;
                _pinchStartScale = targetScale;
                _dragRecognizer.ForceCancel(); // 双指取代单指：取消在途拖拽识别器（Cancelled→EndDrag 释放物理；
                                               // 手指2未命中模型未绑本面时 hub 的 CancelSingles 不会代劳，这里手动）
                ResetClickChain(); // 捏合是明确非点击手势，打断三连击计次（同真实拖拽语义）
                return;           // 起手帧只建基准不缩放
            }

            float dist = Vector2.Distance(t0.position, t1.position);
            if (dist < 1f) return; // 捏死防除零抖动
            ApplyZoom(Mathf.Clamp(_pinchStartScale * (dist / _pinchStartDist), scaleMin, effectiveMaxScale));
        }

        /// <summary>结束捏合。（prevLmbDown"按钮早已按住"hack 已废：PointerInputPump 触摸期间完全抑制
        /// 鼠标流——捏合残留单指只产触摸事件，Drag 识别器 Idle 态不接序列中途的 Moved；两指松尽后
        /// 鼠标流按真实按键状态恢复，不再有幻影按下。）</summary>
        void EndPinch()
        {
            _pinching = false;
        }

        /// <summary>落地缩放目标（滚轮/捏合共用）：只改目标倍率走平滑（ScaleSmoothFrame 每帧趋近）。
        /// 缩放改变模型尺寸→烘焙碰撞体（按旧尺寸烘的）尺寸失配=点击范围错位（放大后点视觉边缘点不中/
        /// 缩小后周围空气误中）——目标变化时排一次延迟补烘（2026-08-28 修复"有时候点不中"），连续缩放
        /// 顺延补烘时刻，落稳后一次烘到位。坐定中缩放的重坐由 sitScaleResitFrame 监听 targetScale 自动接管。</summary>
        void ApplyZoom(float newScale)
        {
            if (Mathf.Approximately(newScale, targetScale)) return;
            targetScale = newScale;
            _bakePending = true;                                    // 缩放改了模型尺寸：补烘碰撞体
            reBakeAt = Time.unscaledTime + bakeGraceSec;    // 平滑落稳后再烘（连续缩放顺延）
            MarkDirty();
        }

        /// <summary>四肢摆动叠加走共用基类（PetHostBase.四肢摆动应用帧，2026-08-28 合一）：
        /// 物理组件输出的摆动角以世界 Z 轴旋转叠加到四肢根骨——Animation 每帧重写骨骼姿势，
        /// 本层在其上叠加一次不累积。交互结束后停止应用，动画自然覆盖残留。</summary>
        void LateUpdate()
        {
            LimbSwingApplyFrame();
        }

        // ---- IPetHost：光标位置（视线跟随/接近判定）----
        // 全屏 RT 与屏幕同 aspect：光标屏幕坐标×(RT/屏幕)=RT 像素空间——视线层头骨投影/行为层
        // 包围盒投影（WorldToScreenPoint 输出 RT 像素）同空间，两套坐标一致（2026-08-28 修复
        // 旧版坐标系错位致视线恒满角扭转）。
        public override bool TryGetCursorUnityScreenPos(out Vector2 unityScreenPos)
        {
            if (_rt == null) { unityScreenPos = default; return false; }
            float sx = (float)_rt.width / Screen.width;
            float sy = (float)_rt.height / Screen.height;
            Vector2 m = Input.mousePosition;
            unityScreenPos = new Vector2(m.x * sx, m.y * sy);
            return true;
        }

        // ---- 持久化（PetPrefs 游戏内字段：骨盆归一化屏幕位+缩放）----

        bool TryGetPelvisNormalizedScreenPos(out Vector2 uv)
        {
            uv = default;
            if (_pelvis == null || petCamera == null) return false;
            Vector3 vp = petCamera.WorldToViewportPoint(_pelvis.position);
            if (vp.z <= 0f) return false;
            uv = new Vector2(vp.x, vp.y);
            return true;
        }

        /// <summary>把骨盆摆到屏幕归一化点（0..1 左下原点）——移动模型根（相机静止全屏视野）。
        /// 坐标系统一（2026-08-31 修复）：WorldToScreenPoint 输出的是 **RT 像素系**（相机挂着
        /// targetTexture，0..rt.width）——目标必须同为 RT 系（uv×RT 尺寸）。旧版用 uv×Screen，
        /// rtMultiplier=1 时两系数值巧合相等掩盖了 bug，开超采样（rtMultiplier=2）后sitLine/防丢/重摆位全部错位。</summary>
        void PlaceToNormalized(Vector2 uv)
        {
            if (_pelvis == null || paimonRoot == null || petCamera == null || _rt == null) return;
            Vector3 currentRT = petCamera.WorldToScreenPoint(_pelvis.position);
            Vector3 targetRT = new Vector3(uv.x * _rt.width, uv.y * _rt.height, currentRT.z);
            Vector2 dRT = (Vector2)targetRT - (Vector2)currentRT;
            // RT 像素 → 屏幕像素（rtMultiplier>1 时）→ 世界位移
            float dxScreen = dRT.x * Screen.width / Mathf.Max(1f, _rt.width);
            float dyScreen = dRT.y * Screen.height / Mathf.Max(1f, _rt.height);
            paimonRoot.position += petCamera.transform.right * (dxScreen * worldPerPixel)
                               + petCamera.transform.up * (dyScreen * worldPerPixel);
        }

        void restorePrefs()
        {
            var d = PetPrefs.Load();
            targetScale = d.ingameScale > 0f ? d.ingameScale : initScaleFactor;
            targetScale = Mathf.Clamp(targetScale, scaleMin, effectiveMaxScale);
            displayScale = targetScale;
            if (baseScale <= 0.001f) calcBaseAndLimit(); // Awake 顺序兜底
            ApplyModelScale();
            // 位置：骨盆归一化屏幕位（旧版语义=画布位置，可兼容——同为屏幕归一化点）
            if (d.ingamePosX >= 0f)
                PlaceToNormalized(new Vector2(Mathf.Clamp01(d.ingamePosX), Mathf.Clamp01(d.ingamePosY)));
            else
                PlaceToNormalized(new Vector2(0.85f, 0.06f)); // 无存档：右下角（对齐桌面版停靠）

            // 启动期尺寸诊断（Warning 级=Release 构建日志也可见；对齐桌宠"建成只信日志"方法论）：
            // 一行打全尺寸链关键值，"导出包派蒙看起来远/小"类问题直接从 Player.log 读数定位
            TryGetPelvisNormalizedScreenPos(out Vector2 uvDiag);
            float colliderH = (hitMeshCollider != null && hitMeshCollider.sharedMesh != null) ? hitMeshCollider.bounds.size.y : -1f;
            Debug.LogWarning($"[PetInGame] 状态 screen={Screen.width}x{Screen.height} rt={(_rt != null ? _rt.width + "x" + _rt.height : "null")} " +
                $"ortho={petCamera != null && petCamera.orthographic} orthoSize={(petCamera != null ? petCamera.orthographicSize : -1f):F3} " +
                $"初始缩放={baseScale:F3} 存档缩放={d.ingameScale:F3} 目标={targetScale:F3} 有效最大={effectiveMaxScale:F2} " +
                $"碰撞体高={colliderH:F3} 骨盆UV=({uvDiag.x:F2},{uvDiag.y:F2}) k={worldPerPixel:F5}");
        }

        void MarkDirty() => dirtyUntil = Time.unscaledTime + 1f;

        void SavePrefs()
        {
#if !UNITY_EDITOR
            try
            {
                var d = PetPrefs.Load();
                d.ingameScale = targetScale;
                if (TryGetPelvisNormalizedScreenPos(out Vector2 uv))
                {
                    d.ingamePosX = uv.x;
                    d.ingamePosY = uv.y;
                }
                PetPrefs.Save();
            }
            finally
            {
                dirtyUntil = -1f;
            }
#endif
        }

        void OnDestroy()
        {
            SavePrefs();
            if (_rt != null) { _rt.Release(); Destroy(_rt); _rt = null; }
        }
    }
}
