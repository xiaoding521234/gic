using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

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
    public class PetInGameHostController : PetHostBase
    {
        [Header("引用（空=自动找）")]
        [InspectorName("派蒙相机")]
        [SerializeField] private Camera petCamera;

        [Header("全屏画布")]
        [Tooltip("RT 超采样倍率（1=与屏幕 1:1 像素；2=4K 屏超采样，全屏 RT 体积大按需开）")]
        [SerializeField, Range(1f, 2f)] private float rt倍率 = 1f;

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
        [Tooltip("松手时骨盆在坐线上方多少屏幕像素内触发坐（与桌面版 PetEdgeSitController 同参 120px；旧 0.15 归一化在高分屏≈300px+ 太大）")]
        [InspectorName("上吸附范围像素")]
        [SerializeField] private float snapRangeTopPx = 120f;
        [Tooltip("坐线下方多少屏幕像素内松手也算坐（与桌面版同参 60px；骨盆被拖出屏底一小段的兜底）")]
        [InspectorName("下吸附范围像素")]
        [SerializeField] private float snapRangeBottomPx = 60f;
        [Tooltip("坐姿动画名（与桌面版 PetEdgeSitController 同款 SitUpright）")]
        [InspectorName("坐姿动作名")]
        [SerializeField] private string sitAnimName = "Ani_NPC_Kanban_Paimon_SitUpright";
        [Tooltip("坐定后骨盆距屏幕底部的归一化偏移=坐线高度，对齐桌面版坐任务栏顶——任务栏高度随 DPI 等比（48px@96dpi≈4.4% 屏高），0.045≈任意 DPI 的任务栏顶线；旧值 0.02 目检坐得太低（沉进屏底）")]
        [InspectorName("坐定底部偏移")]
        [SerializeField] private float sitBottomOffset = 0.045f;
        [Tooltip("落座后逐帧贴正骨盆的时长（秒）——与桌面版 坐定贴正秒 同参：站→坐姿势过渡期骨盆渐降，逐帧钉回坐线=平滑落座观感（旧版一次性摆位无贴正）")]
        [InspectorName("坐定贴正秒")]
        [SerializeField] private float sitSettleSec = 1.2f;
        [Tooltip("坐定中滚轮缩放时切回的站立待机动作（桌面版 PetEdgeSitController.站立动作名 同参——缩放露馅修正：模型绕脚底长高，骨盆钉坐线的位置不动→屁股浮离坐线；缩放稳定后重新落座）")]
        [InspectorName("站立动作名")]
        [SerializeField] private string standAnimName = "Ani_NPC_Kanban_Paimon_Standby";
        [Tooltip("坐定中缩放后等多久算稳定（秒）——平滑过渡结束+宽限后重新落座（桌面版 0.3s 同参）")]
        [InspectorName("缩放稳定宽限秒")]
        [SerializeField] private float scaleStableGraceSec = 0.3f;

        // ---- 运行时状态 ----
        private RenderTexture _rt;
        private RawImage _画面;          // 全屏显示层（raycastTarget 恒 false）
        private Canvas _canvas;            // 画中画 Canvas（对话 UI 接线用）
        private Image _eventBlocker;             // 全屏透明事件挡板（raycastTarget 动态=命中状态）
        private float worldPerPixel;      // 相机平面世界单位 / 屏幕像素（k=H/Screen.height）
        private Vector2Int _上次屏幕尺寸;
        private PetBehaviorController _行为控制器; // 退场中冻结拖拽/滚轮交互（热切换退场期间不可抓取）

        // 拖拽状态（对齐桌面版 PetWindowController 字段语义）
        private bool dragging;
        private bool prevLmbDown;
        private bool _pressPending;           // 命中模型已按下但未升级为拖拽（单击判定窗口内）
        private Vector2 dragStartPointerRT;      // RT 像素系（鼠标×RT/屏比）
        private Vector2 dragStartPelvisRT;     // 起手骨盆屏幕投影（RT 像素系）——骨盆钉位基准
        private float _单击按下时刻 = -10f;   // 单击（非拖拽）判定：按下时刻
        private Vector3 _单击按下位置;        // 按下时鼠标位（<8px 位移=没拖=单击）

        // 对话（2026-08-28）：单击弹输入框+流式回复气泡；组件随 prefab 预挂（素材/字体接线在 prefab）
        private GIC.Pet.Chat.PetChatUIController _聊天UI;
        private Transform _头骨;              // 气泡锚点（头骨屏幕投影）

        private float dirtyUntil = -1f;

        // 屏幕边缘坐状态（游戏内形态专属；PetBehaviorController 经 IPetHost.坐定中 切换待机动作）
        private bool _屏幕坐定中;
        private float _坐定x;               // 贴正期保持的水平位置
        private float _贴正截止时刻 = -10f;  // 坐定贴正秒 内逐帧钉骨盆到坐线（站→坐过渡平滑）
        // 坐定中缩放重坐（桌面版 缩放调整中 状态的等价，2026-08-28 移植）
        private bool _缩放调整中;
        private float _上次缩放 = -1f;      // 坐定中监听的目标缩放（-1=未初始化）
        private float _scaleStableAt = -10f;  // 缩放过渡结束后的重新落座时刻（<0=未起算）
        /// <summary>屏幕坐定中（兼容保留的公开查询）</summary>
        public bool IsScreenSeated => _屏幕坐定中;
        /// <summary>坐姿动作名（兼容保留的公开查询）</summary>
        public string ScreenSitAnim => sitAnimName;

        public override bool IsDragging => dragging || PhysicsBusy;
        public override bool IsSeated => _屏幕坐定中;
        public override string SitAnim => sitAnimName;
        protected override string logTag => "[PetInGame]";

        void Awake()
        {
            petCamera = petCamera != null ? petCamera : GetComponentInChildren<Camera>(true);
            dragPhysics = dragPhysics != null ? dragPhysics : GetComponentInChildren<PetDragPhysicsController>(true);
            if (bodyRenderer == null) bodyRenderer = FindBodyRenderer();
            _行为控制器 = GetComponentInChildren<PetBehaviorController>(true);
            var paimonGo = transform.Find("Paimon");
            paimon根 = paimon根 != null ? paimon根 : (paimonGo != null ? paimonGo : transform).transform;

            if (petCamera != null)
            {
                // 安全带（2026-08-28 背景"缩的很小"根因）：prefab 相机 tag 遗留 MainCamera——常驻实例
                // 抢占 Camera.main，MainHall 的 BackgroundParallax3D.FitToScreen（正交铺屏）解析到本相机
                // （orthoSize 0.7）会把大厅背景缩到 ~0.13 倍（编辑器直开场景 Play 无派蒙实例故正常）。
                // prefab 已改 Untagged，此行防 prefab 被旧版本覆盖回 MainCamera。
                if (petCamera.CompareTag("MainCamera")) petCamera.tag = "Untagged";

                petCamera.clearFlags = CameraClearFlags.SolidColor;
                petCamera.backgroundColor = new Color(0, 0, 0, 0);
                petCamera.allowHDR = false;
                petCamera.allowMSAA = false;
                var urpData = petCamera.GetUniversalAdditionalCameraData();
                if (urpData != null) urpData.renderPostProcessing = false;

                // 透视→正交（2026-08-28 用户复测"越拖到边缘角度越大"根治）：透视投影下模型偏离光轴
                // 即斜视畸变（视野边缘尤甚）；桌面版模型恒在窗口中心=恒正对。正交投影视线处处平行——
                // 模型在屏内任何位置都正对玩家，且世界/屏幕像素比恒定（拖拽/缩放标尺不随位置漂移）。
                // 可见世界高=原透视视野高（FOV 45.27 按模型距离折算），保持模型屏幕占比不变。
                float d = Vector3.Dot(paimon根.position - petCamera.transform.position, petCamera.transform.forward);
                if (d <= 0.01f) d = 1f;
                float perspectiveHeight = 2f * d * Mathf.Tan(petCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                petCamera.orthographic = true;
                petCamera.orthographicSize = perspectiveHeight * 0.5f;
            }

            WireDragBones();
            // 头骨锚点取 GI 本体（Bip001 系——视线层同款配置）。勿用日文名"頭"：那是 MMD 兜底模型
            // （Paimon_arm，禁用留存）的骨名，找本体骨 只过滤影子壳不过滤它——命中后气泡锚在
            // 不动的 MMD 头上（2026-08-29 首测气泡错位根因之一）
            _头骨 = FindBodyBone("Bip001 Head");
            BuildFullScreenPIP();
            BuildHitProxy();
            calcBaseAndLimit();
            restorePrefs();
            接聊天();
        }

        /// <summary>对话接线（2026-08-28 docs/19 §6.5）：聊天三组件（PetChatUIController+会话+客户端）
        /// 挂 prefab 根，素材/字体在 prefab 接线。Canvas 由宿主直传——不挪物体（2026-08-29 修单击 NRE：
        /// 组件在 prefab 根=宿主根，旧版 SetParent 把宿主根挪进自己的子 Canvas=循环父子被拒→组件向上
        /// 找不到 Canvas→自禁用→_输入条根 恒 null→单击必炸）。prefab 未挂（旧 prefab/裁剪安装）
        /// =对话功能缺席，其余交互不受影响。</summary>
        void 接聊天()
        {
            _聊天UI = GetComponentInChildren<GIC.Pet.Chat.PetChatUIController>(true);
            if (_聊天UI == null) return;
            if (_canvas == null) return; // 无画中画（异常态）不接
            // 聊天是可选功能：构建失败绝不能上抛——本方法在 Awake 末尾，任何异常都会让 Unity
            // 禁用整个宿主组件 → Update（全部输入轮询）停摆=点不了拖不动（2026-08-29 实证：
            // BuildUI 里 SendBtn 的 Image+TMP 同物体冲突 NRE 一路穿透，宿主陪葬）。失败=禁用
            // 聊天组件+置空引用，交互照常。
            try
            {
                _聊天UI.WireHost(_canvas); // 此刻才建界面（建到画中画 Canvas 下）
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[PetInGame] 聊天 UI 构建失败，已禁用（派蒙交互不受影响）：{e.Message}");
                _聊天UI.enabled = false;
                _聊天UI = null;
                return;
            }
            // 头锚点：模型包围盒顶（x 跟头骨水平位）→ RT 相机屏幕位 → Canvas 坐标（ConstantPixelSize：
            // canvas=屏幕像素系直通）。2026-08-29 修"气泡遮住头一部分"：旧版锚头骨+固定 60px 偏移——
            // 骨锚在颈部，Q 版头高出骨锚 100px+（缩放 1.365 实测），气泡底压在头发/脸上；改锚包围盒顶
            // =任意缩放恒在头顶之上，抬手类动作包围盒顶升高时气泡随让位（顺带正确）。
            _聊天UI.headAnchorProvider = () =>
            {
                if (petCamera == null || hitMeshCollider == null || hitMeshCollider.sharedMesh == null) return Vector2.zero;
                var bounds = hitMeshCollider.bounds;
                float x = _头骨 != null ? _头骨.position.x : bounds.center.x; // 水平跟头骨（歪头/侧移气泡跟脸）
                Vector3 sp = petCamera.WorldToScreenPoint(new Vector3(x, bounds.max.y, bounds.center.z));
                return new Vector2(sp.x * Screen.width / Mathf.Max(1f, _rt.width), sp.y * Screen.height / Mathf.Max(1f, _rt.height));
            };
            // 底锚点：模型包围盒底中心（烘焙命中碰撞体的世界包围盒）→ RT 相机屏幕位 → Canvas 坐标
            // ——输入条挂在模型脚底下方（2026-08-29 布局重构：聊天 UI 跟随模型，不再钉屏底）
            _聊天UI.footAnchorProvider = () =>
            {
                if (hitMeshCollider == null || petCamera == null) return Vector2.zero;
                var bounds = hitMeshCollider.bounds;
                Vector3 sp = petCamera.WorldToScreenPoint(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
                return new Vector2(sp.x * Screen.width / Mathf.Max(1f, _rt.width), sp.y * Screen.height / Mathf.Max(1f, _rt.height));
            };

            // Intent 工具接线（2026-08-29 首批指令，docs/19 §6.5）：set_game_time / get_game_time
            // ——游戏内形态直调主进程系统。open_screen 2026-08-30 重做恢复（与手动同路径）+ auto_wish 自动抽卡。
            // 注册失败只少工具不影响聊天（防泄漏结构同上）。
            try
            {
                var session = _聊天UI.sessionRef;
                if (session != null)
                {
                    var tools = new System.Collections.Generic.List<GIC.Pet.Chat.PetChatClient.ToolDefinition>
                    {
                        GIC.Pet.Chat.PaimonChatSession.MemoryToolDefinition(),
                        GIC.Pet.Chat.PetChatIntent.SetGameTimeTool(),
                        GIC.Pet.Chat.PetChatIntent.GetGameTimeTool(),
                        GIC.Pet.Chat.PetChatIntent.OpenScreenTool(),
                        GIC.Pet.Chat.PetChatIntent.AutoWishTool(),
                    };
                    session.RegisterTools(tools, (toolName, toolArgs) =>
                    {
                        return GIC.Pet.Chat.PetChatIntent.Execute(toolName, toolArgs);
                    });
                    Debug.Log("[PetInGame] 对话指令工具已注册（游戏时间/界面/自动抽卡）");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[PetInGame] 对话指令工具注册失败（聊天基础功能不受影响）：{e.Message}");
            }
            接反应通道();
            Debug.Log("[PetInGame] 对话已接线（单击派蒙开输入条）");
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
                consumer.Wire(_行为控制器, _聊天UI);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[PetInGame] 反应通道接线失败（其余功能不受影响）：{e.Message}");
            }
        }

        /// <summary>全屏画中画：RT=屏幕尺寸×rt倍率；RawImage 铺满全屏（raycastTarget=false，
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
            _画面 = imageGo.GetComponent<RawImage>();
            var rt = imageGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _画面.raycastTarget = false; // 显示层不吃事件——交互全走轮询+挡板

            var blockerGo = new GameObject("PetEventBlocker", typeof(RectTransform), typeof(Image));
            blockerGo.transform.SetParent(canvasGo.transform, false);
            var brt = blockerGo.GetComponent<RectTransform>();
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = brt.offsetMax = Vector2.zero;
            _eventBlocker = blockerGo.GetComponent<Image>();
            _eventBlocker.color = new Color(0f, 0f, 0f, 0f); // 全透明纯挡事件
            _eventBlocker.raycastTarget = false;             // 默认穿透；命中派蒙时逐帧开

            重建RT();
            _上次屏幕尺寸 = new Vector2Int(Screen.width, Screen.height);
            Debug.Log($"[PetInGame] 全屏画中画建立 screen={Screen.width}x{Screen.height} rt={(int)(Screen.width * rt倍率)}x{(int)(Screen.height * rt倍率)}");
        }

        void 重建RT()
        {
            int w = Mathf.Max(2, Mathf.RoundToInt(Screen.width * rt倍率));
            int h = Mathf.Max(2, Mathf.RoundToInt(Screen.height * rt倍率));
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
            if (_画面 != null) _画面.texture = _rt;
        }

        /// <summary>算基准缩放（prefab 值 × 等效画布/屏幕参考 折算——保持屏幕显示尺寸与桌面版一致）
        /// 与动态缩放上限（模型最大屏高占比 ≤95%——放大到最大恰好不截屏）+ 世界/屏幕像素比。
        /// 每次补烘后重算（姿势微变 h 微变，上限跟随）。</summary>
        void calcBaseAndLimit()
        {
            if (paimon根 == null || petCamera == null) return;
            float convert = screenRefH > 1f ? equivCanvasH / screenRefH : 1f;
            baseScale = paimon根.localScale.x * convert;
            worldPerPixel = 取视野世界高() / Mathf.Max(1f, Screen.height);
            UpdateEffectiveMaxScale();
        }

        float 取视野世界高()
        {
            if (petCamera == null) return 1f;
            if (petCamera.orthographic) return petCamera.orthographicSize * 2f; // 正交：恒定可见高
            float d = Vector3.Dot(paimon根.position - petCamera.transform.position, petCamera.transform.forward);
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
                    float H = 取视野世界高();
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
            // 退场中（热切换/退场的 Disappear 播放期+播完冻结期）交互全冻结——拖拽会抓住正在退场的
            // 模型（桌面版 PetWindowController.已请求退出 同款守卫，2026-08-28 用户要求"播完就杀"配套）
            bool interactFrozen = _行为控制器 != null && _行为控制器.IsExiting;
            bool modelHit = !interactFrozen && GetHitState(Input.mousePosition);
            RebakeHitMeshFrame();
            ScaleSmoothFrame();
            if (!interactFrozen)
            {
                dragPollFrame(modelHit);
                ScrollZoomFrame(modelHit);
            }
            else
            {
                _pressPending = false; // 退场冻结期作废待定按下（防解冻松手误判单击）
            }
            blockerToggleFrame(modelHit);
            sitSettleFrame();
            sitScaleResitFrame();
            if (dirtyUntil > 0f && Time.unscaledTime >= dirtyUntil) SavePrefs();
        }

        /// <summary>坐定中缩放重坐（桌面版 PetEdgeSitController 缩放调整中 状态的等价，2026-08-28 移植）：
        /// 模型绕脚底长高而坐线不动，坐定中滚轮缩放→屁股浮离坐线（放大上浮/缩小下沉）。处理=坐定中
        /// 检测目标缩放变化→切站立待机+清坐定标志→缩放平滑落稳+宽限后按原水平位重新落座（贴正钉回）。</summary>
        void sitScaleResitFrame()
        {
            if (_缩放调整中)
            {
                if (!Mathf.Approximately(targetScale, _上次缩放)) { _上次缩放 = targetScale; _scaleStableAt = -10f; } // 连续滚轮：重等稳定
                if (ScaleTransitioning()) { _scaleStableAt = -10f; return; }               // 平滑过渡进行中
                if (_scaleStableAt < 0f) { _scaleStableAt = Time.unscaledTime + Mathf.Max(0f, scaleStableGraceSec); return; } // 刚到位，宽限
                if (Time.unscaledTime < _scaleStableAt) return;
                _缩放调整中 = false;
                reSit();
                return;
            }
            if (!_屏幕坐定中) return;
            if (!Mathf.Approximately(targetScale, _上次缩放))
            {
                _上次缩放 = targetScale;
                _缩放调整中 = true;
                _scaleStableAt = -10f;
                _屏幕坐定中 = false; // 行为层待机切回站立（切动画见下）
                var swapper = paimon根 != null ? paimon根.GetComponent<PetAnimSwapper>() : null;
                if (swapper != null && swapper.HasAnim(standAnimName))
                    swapper.Play(standAnimName);
                Debug.Log("[PetInGame] 坐定中缩放：切站立待机，稳定后重新落座");
            }
        }

        /// <summary>缩放显示值未追上目标（对齐桌面版 缩放过渡中 语义）</summary>
        bool ScaleTransitioning() => !Mathf.Approximately(displayScale, targetScale);

        /// <summary>按原水平位重新落座（缩放后骨盆高度变了，重新贴坐线）</summary>
        void reSit()
        {
            _屏幕坐定中 = true;
            _坐定x = Mathf.Clamp01(_坐定x);
            摆位到归一化(new Vector2(_坐定x, sitBottomOffset));
            _贴正截止时刻 = Time.unscaledTime + sitSettleSec;
            _上次缩放 = targetScale;
            var swapper = paimon根 != null ? paimon根.GetComponent<PetAnimSwapper>() : null;
            if (swapper != null && swapper.HasAnim(sitAnimName))
                swapper.Play(sitAnimName);
        }

        /// <summary>坐定贴正（桌面版 坐定帧 的贴正段同构）：落座后 坐定贴正秒 内逐帧把骨盆钉在
        /// (_坐定x, 坐定底部偏移)——站→坐姿势过渡期骨盆渐降，根随之下移=她"缓缓坐进去"。
        /// 退场中跳过（Disappear 是飞离编排，贴正会把退场动画拽回坐线）。被拖拽=拖拽起手已清坐定。</summary>
        void sitSettleFrame()
        {
            if (!_屏幕坐定中 || Time.unscaledTime >= _贴正截止时刻) return;
            if (_行为控制器 != null && _行为控制器.IsExiting) return;
            摆位到归一化(new Vector2(_坐定x, sitBottomOffset));
        }

        /// <summary>分辨率变化（全屏切换/窗口拖拽 resize）→ 重建 RT+重摆位+像素比重算</summary>
        void resolutionChangedFrame()
        {
            if (Screen.width == _上次屏幕尺寸.x && Screen.height == _上次屏幕尺寸.y) return;
            _上次屏幕尺寸 = new Vector2Int(Screen.width, Screen.height);
            重建RT();
            worldPerPixel = 取视野世界高() / Mathf.Max(1f, Screen.height);
            UpdateEffectiveMaxScale();
            // 按骨盆归一化位重摆（模型回到屏幕上原相对位置）
            if (TryGet骨盆归一化屏幕位(out Vector2 uv)) 摆位到归一化(uv);
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

        /// <summary>挡板穿透切换（桌面版 穿透切换帧 同构）：命中模型或拖拽中=可交互挡板吃点击；
        /// 其余区域点击穿透到主游戏 UI。</summary>
        void blockerToggleFrame(bool modelHit)
        {
            if (_eventBlocker == null) return;
            bool want = modelHit || dragging;
            if (_eventBlocker.raycastTarget != want) _eventBlocker.raycastTarget = want;
        }

        // ---- 拖拽轮询（桌面版 拖拽与双击帧 的 Unity 轮询等价；双击退出在游戏内形态无意义）----

        /// <summary>鼠标屏幕坐标 → RT 像素系（rt倍率>1 时放大）</summary>
        Vector2 指针RT() => new Vector2(
            Input.mousePosition.x * _rt.width / Screen.width,
            Input.mousePosition.y * _rt.height / Screen.height);

        void dragPollFrame(bool modelHit)
        {
            if (dragPhysics == null || _骨盆 == null || petCamera == null) return;
            bool lmbDown = Input.GetMouseButton(0);
            bool lmbPressed = lmbDown && !prevLmbDown;

            // ①物理推进（含松手与收尾）**不受聊天输入门控**（2026-08-29 修双报障：旧版输入期整体早退
            // =收尾轮询停摆，dragPhysics.IsActive 永真 → 行为层卡 dragPhysicsPhase（Drag01 循环+视线
            // 静默）=永久拎起姿势+头不再跟踪鼠标——两症状同根）。输入期不会开着 dragging（新拖拽被
            // ③拦截），此处恒走收尾分支。
            if (dragPhysics.IsActive)
            {
                if (dragging)
                {
                    if (!lmbDown)
                    {
                        dragging = false;
                        dragPhysics.Release(); // 松手即停：骨盆冻结原地，四肢弹簧收尾归零
                    }
                    else
                    {
                        dragPhysics.DragFrame(指针RT(), Time.unscaledDeltaTime);
                        LiftPoseAngleFrame(true);   // ①姿势旋转（基类：角平滑+挣扎+绕骨盆枢轴补偿）
                        PelvisPinToTarget(); // ②骨盆钉位（刚体 1:1 直跟）
                    }
                }
                else
                {
                    bool 收尾完成 = !dragPhysics.SettleFrame(Time.unscaledDeltaTime);
                    LiftPoseAngleFrame(false); // 收尾期角度平滑归零
                    if (!dragPhysics.IsActive || 收尾完成) PhysicsSettle();
                }
            }

            // ②聊天输入期：不起新拖拽；点击"对话元素（输入条/气泡）以外"任何地方=关闭对话
            //（2026-08-29 用户拍板：点派蒙=原有 Toggle 收起，点游戏其它区域同样收——含气泡判定，
            // 回看气泡时不误关）。点输入条本身=正常 UI 交互（EventSystem 吃掉，这里仍会看到一次
            // 按下——靠 IsPointOnInputBar 排除）。
            bool chatInputActive = _聊天UI != null && _聊天UI.IsInputVisible;
            if (chatInputActive)
            {
                if (lmbPressed && !_聊天UI.IsPointOnInputBar(Input.mousePosition) && !_聊天UI.IsPointOnBubble(Input.mousePosition))
                    _聊天UI.CloseChat();
                _pressPending = false;
                prevLmbDown = lmbDown;
                return;
            }

            // ③正常交互：按下待定（2026-08-29 修"单击也立刻摆出拖拽姿势"）：命中模型按下**不立刻起手**
            // ——按住超时（0.15s，用户拍板的单击阈值）或位移超阈值（8px）才升级为真拖拽（此刻才起手
            // +拎起姿势+物理）；期间松手且几乎没动=单击→切对话输入框。单击全程零姿势零物理。升级不看
            // 当前命中（按下起点在模型上，快速甩出模型的拖拽也要能抓——桌面版"按下即抓"的同构语义）。
            if (_pressPending)
            {
                if (!lmbDown)
                {
                    _pressPending = false;
                    if (Time.unscaledTime - _单击按下时刻 <= 0.15f
                        && (Input.mousePosition - _单击按下位置).magnitude < 8f)
                    {
                        _聊天UI?.ToggleInput();
                    }
                }
                else if (Time.unscaledTime - _单击按下时刻 > 0.15f
                         || (Input.mousePosition - _单击按下位置).magnitude >= 8f)
                {
                    _pressPending = false;
                    dragStart();
                }
            }
            else if (!dragging && modelHit && lmbPressed)
            {
                _pressPending = true;
                _单击按下时刻 = Time.unscaledTime;
                _单击按下位置 = Input.mousePosition;
            }
            prevLmbDown = lmbDown;
        }

        /// <summary>拖拽起手：快照指针/骨盆 RT 位+根旋转基准（收尾中被再抓不重取基准，防旋转叠加——桌面版语义）。
        /// 物理组件喂 RT 像素系（指针与骨盆同系即可，组件内部只做差分）。</summary>
        void dragStart()
        {
            dragging = true;
            _屏幕坐定中 = false; // 被拖=立即解除坐定
            _缩放调整中 = false; // 缩放重坐流程一并取消（拖走了自然不重坐）
            dragStartPointerRT = 指针RT();
            SnapshotDragBaseline(false); // 根旋转基准（模型位置=拖拽结果，不快照还原基准）
            dragStartPelvisRT = petCamera.WorldToScreenPoint(_骨盆.position);
            dragPhysics.BeginDrag(dragStartPointerRT, dragStartPelvisRT);
        }

        /// <summary>骨盆钉位（桌面版 按骨盆目标定位窗口 的游戏内等价）：目标=起手骨盆+光标位移（刚体 1:1
        /// 直跟），根平移使骨盆投影钉到目标——Drag01 动画微动/旋转残余位移全部被吸收（桌面版靠移窗吸收，
        /// 此处靠移根，视觉等价）。【2026-08-28 修复：旧版只直跟根位置，动画微动致骨盆漂移】</summary>
        void PelvisPinToTarget()
        {
            if (paimon根 == null || petCamera == null || _骨盆 == null) return;
            Vector3 currentProjection = petCamera.WorldToScreenPoint(_骨盆.position);
            Vector2 target = dragPhysics.CurrentPelvisScreen;
            Vector2 dRT = target - (Vector2)currentProjection;
            // RT 像素 → 屏幕像素（rt倍率>1 时缩回）→ 世界位移（k 对 x/y 同比）
            float d屏幕x = dRT.x * Screen.width / Mathf.Max(1f, _rt.width);
            float d屏幕y = dRT.y * Screen.height / Mathf.Max(1f, _rt.height);
            paimon根.position += petCamera.transform.right * (d屏幕x * worldPerPixel)
                               + petCamera.transform.up * (d屏幕y * worldPerPixel);
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
        /// 参数同款）：坐线=屏底上方 坐定底部偏移（≈桌面版任务栏顶高度）；磁吸窗=坐线上 120px/下 60px
        /// （桌面版同参，像素语义——归一化随分辨率换算，高分屏不再放大）；落座贴正 骨盆到坐线+播
        /// SitUpright+置坐定标志。返回 true=已落座接管。拖拽中不触发（拖拽起手已清坐定）。
        /// 坐标系注意：Unity 视口/屏幕 y=0 是**屏底**（y=1 顶部）——坐线=坐定底部偏移本身；
        /// 2026-08-28 曾误按 y=1 为底写 `1-偏移`，磁吸窗落到屏幕顶部=拖底不坐/拖顶瞬移坐（用户实测报障）。</summary>
        bool evalScreenEdgeSit()
        {
            if (_骨盆 == null || petCamera == null || dragPhysics == null) return false;
            Vector3 vp = petCamera.WorldToViewportPoint(_骨盆.position);
            if (vp.z <= 0f) return false;
            float 坐线 = sitBottomOffset; // 视口 y=0=屏底，坐线即"屏底上方偏移量"
            float topRange = snapRangeTopPx / Mathf.Max(1f, Screen.height);
            float bottomRange = snapRangeBottomPx / Mathf.Max(1f, Screen.height);
            if (vp.y < 坐线 - bottomRange || vp.y > 坐线 + topRange) return false; // 磁吸窗：坐线上 120px / 下 60px

            // 磁吸：骨盆贴到坐线（贴正期逐帧钉住，见 坐定贴正帧）
            _坐定x = Mathf.Clamp01(vp.x);
            摆位到归一化(new Vector2(_坐定x, sitBottomOffset));
            _屏幕坐定中 = true;
            _贴正截止时刻 = Time.unscaledTime + sitSettleSec;
            _上次缩放 = targetScale; // 落座时同步当前倍率（防陈旧值立即误触发缩放重坐）
            _缩放调整中 = false;
            // 播坐姿动画
            var swapper = paimon根.GetComponent<PetAnimSwapper>();
            if (swapper != null && swapper.HasAnim(sitAnimName))
                swapper.Play(sitAnimName);
            Debug.Log($"[PetInGame] 屏幕边坐触发（骨盆y={vp.y:F2}∈[{坐线 - bottomRange:F2},{坐线 + topRange:F2}]）→ 播 {sitAnimName}");
            return true;
        }

        /// <summary>松手防丢（桌面版 拖拽松手防丢拉回 同款）：骨盆完全出屏才拉回贴边（有交集=用户可及不动）</summary>
        void pelvisReelIn()
        {
            if (_骨盆 == null || petCamera == null) return;
            Vector3 vp = petCamera.WorldToViewportPoint(_骨盆.position);
            if (vp.z <= 0f) return;
            float edge = antiLossMargin;
            if (vp.x > -edge && vp.x < 1f + edge && vp.y > -edge && vp.y < 1f + edge) return; // 屏上或贴边
            float nx = Mathf.Clamp(vp.x, edge, 1f - edge);
            float ny = Mathf.Clamp(vp.y, edge, 1f - edge);
            摆位到归一化(new Vector2(nx, ny));
        }

        /// <summary>滚轮缩放（桌面版 滚轮缩放帧 同款：仅光标命中模型时响应；只改目标倍率走平滑）。
        /// 缩放改变模型尺寸→烘焙碰撞体（按旧尺寸烘的）尺寸失配=点击范围错位（放大后点视觉边缘点不中/
        /// 缩小后周围空气误中）——目标变化时排一次延迟补烘（2026-08-28 修复"有时候点不中"）。连续滚轮
        /// 顺延补烘时刻，落稳后一次烘到位。</summary>
        void ScrollZoomFrame(bool modelHit)
        {
            float scroll = Input.mouseScrollDelta.y;
            if (!modelHit || Mathf.Abs(scroll) < 0.01f) return;
            float newScale = Mathf.Clamp(targetScale * Mathf.Pow(scaleStep, scroll), scaleMin, effectiveMaxScale);
            if (!Mathf.Approximately(newScale, targetScale))
            {
                targetScale = newScale;
                _烘焙待补 = true;                                    // 缩放改了模型尺寸：补烘碰撞体
                reBakeAt = Time.unscaledTime + bakeGraceSec;    // 平滑落稳后再烘（连续滚轮顺延）
                MarkDirty();
            }
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
        public override bool TryGet光标Unity屏幕位置(out Vector2 unityScreenPos)
        {
            if (_rt == null) { unityScreenPos = default; return false; }
            float sx = (float)_rt.width / Screen.width;
            float sy = (float)_rt.height / Screen.height;
            Vector2 m = Input.mousePosition;
            unityScreenPos = new Vector2(m.x * sx, m.y * sy);
            return true;
        }

        // ---- 持久化（PetPrefs 游戏内字段：骨盆归一化屏幕位+缩放）----

        bool TryGet骨盆归一化屏幕位(out Vector2 uv)
        {
            uv = default;
            if (_骨盆 == null || petCamera == null) return false;
            Vector3 vp = petCamera.WorldToViewportPoint(_骨盆.position);
            if (vp.z <= 0f) return false;
            uv = new Vector2(vp.x, vp.y);
            return true;
        }

        /// <summary>把骨盆摆到屏幕归一化点（0..1 左下原点）——移动模型根（相机静止全屏视野）</summary>
        void 摆位到归一化(Vector2 uv)
        {
            if (_骨盆 == null || paimon根 == null || petCamera == null) return;
            Vector3 currentRT = petCamera.WorldToScreenPoint(_骨盆.position);
            Vector3 目标RT = new Vector3(uv.x * Screen.width, uv.y * Screen.height, currentRT.z);
            Vector2 dRT = (Vector2)目标RT - (Vector2)currentRT;
            // RT 像素 → 屏幕像素（rt倍率>1 时）→ 世界位移
            float d屏幕x = dRT.x * Screen.width / Mathf.Max(1f, _rt.width);
            float d屏幕y = dRT.y * Screen.height / Mathf.Max(1f, _rt.height);
            paimon根.position += petCamera.transform.right * (d屏幕x * worldPerPixel)
                               + petCamera.transform.up * (d屏幕y * worldPerPixel);
        }

        void restorePrefs()
        {
            var d = PetPrefs.Load();
            targetScale = d.游戏内缩放 > 0f ? d.游戏内缩放 : initScaleFactor;
            targetScale = Mathf.Clamp(targetScale, scaleMin, effectiveMaxScale);
            displayScale = targetScale;
            if (baseScale <= 0.001f) calcBaseAndLimit(); // Awake 顺序兜底
            ApplyModelScale();
            // 位置：骨盆归一化屏幕位（旧版语义=画布位置，可兼容——同为屏幕归一化点）
            if (d.游戏内位置X >= 0f)
                摆位到归一化(new Vector2(Mathf.Clamp01(d.游戏内位置X), Mathf.Clamp01(d.游戏内位置Y)));
            else
                摆位到归一化(new Vector2(0.85f, 0.06f)); // 无存档：右下角（对齐桌面版停靠）

            // 启动期尺寸诊断（Warning 级=Release 构建日志也可见；对齐桌宠"建成只信日志"方法论）：
            // 一行打全尺寸链关键值，"导出包派蒙看起来远/小"类问题直接从 Player.log 读数定位
            TryGet骨盆归一化屏幕位(out Vector2 uvDiag);
            float colliderH = (hitMeshCollider != null && hitMeshCollider.sharedMesh != null) ? hitMeshCollider.bounds.size.y : -1f;
            Debug.LogWarning($"[PetInGame] 状态 screen={Screen.width}x{Screen.height} rt={(_rt != null ? _rt.width + "x" + _rt.height : "null")} " +
                $"ortho={petCamera != null && petCamera.orthographic} orthoSize={(petCamera != null ? petCamera.orthographicSize : -1f):F3} " +
                $"初始缩放={baseScale:F3} 存档缩放={d.游戏内缩放:F3} 目标={targetScale:F3} 有效最大={effectiveMaxScale:F2} " +
                $"碰撞体高={colliderH:F3} 骨盆UV=({uvDiag.x:F2},{uvDiag.y:F2}) k={worldPerPixel:F5}");
        }

        void MarkDirty() => dirtyUntil = Time.unscaledTime + 1f;

        void SavePrefs()
        {
#if !UNITY_EDITOR
            try
            {
                var d = PetPrefs.Load();
                d.游戏内缩放 = targetScale;
                if (TryGet骨盆归一化屏幕位(out Vector2 uv))
                {
                    d.游戏内位置X = uv.x;
                    d.游戏内位置Y = uv.y;
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
