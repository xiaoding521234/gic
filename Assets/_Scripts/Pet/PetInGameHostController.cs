using UnityEngine;
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
        [SerializeField] private Camera 派蒙相机;

        [Header("全屏画布")]
        [Tooltip("RT 超采样倍率（1=与屏幕 1:1 像素；2=4K 屏超采样，全屏 RT 体积大按需开）")]
        [SerializeField, Range(1f, 2f)] private float rt倍率 = 1f;

        [Header("缩放（其余缩放参数在 PetHostBase）")]
        [Tooltip("模型最大屏高占比（动态缩放上限的预算，对齐桌面版工作区 95% 语义）")]
        [SerializeField, Range(0.5f, 1f)] private float 最大屏高占比 = 0.95f;

        [Header("基准适配（全屏视野折算）")]
        [Tooltip("等效画布逻辑高：桌面版窗口逻辑高 825——全屏视野下模型基准缩放按 825/屏幕参考逻辑高 折算，保持屏幕显示尺寸与桌面版一致")]
        [SerializeField] private float 等效画布逻辑高 = 825f;
        [Tooltip("屏幕参考逻辑高（CanvasScaler 旧参考分辨率高度）")]
        [SerializeField] private float 屏幕参考逻辑高 = 1080f;

        [Header("命中")]
        [Tooltip("松手防丢：骨盆完全出屏时拉回屏内的边距（归一化）")]
        [SerializeField] private float 防丢边距 = 0.05f;

        [Header("屏幕边缘坐（游戏内形态专属，桌面版走 PetEdgeSitController）")]
        [Tooltip("松手时骨盆在坐线上方多少屏幕像素内触发坐（与桌面版 PetEdgeSitController 同参 120px；旧 0.15 归一化在高分屏≈300px+ 太大）")]
        [SerializeField] private float 上吸附范围像素 = 120f;
        [Tooltip("坐线下方多少屏幕像素内松手也算坐（与桌面版同参 60px；骨盆被拖出屏底一小段的兜底）")]
        [SerializeField] private float 下吸附范围像素 = 60f;
        [Tooltip("坐姿动画名（与桌面版 PetEdgeSitController 同款 SitUpright）")]
        [SerializeField] private string 坐姿动作名 = "Ani_NPC_Kanban_Paimon_SitUpright";
        [Tooltip("坐定后骨盆距屏幕底部的归一化偏移=坐线高度，对齐桌面版坐任务栏顶——任务栏高度随 DPI 等比（48px@96dpi≈4.4% 屏高），0.045≈任意 DPI 的任务栏顶线；旧值 0.02 目检坐得太低（沉进屏底）")]
        [SerializeField] private float 坐定底部偏移 = 0.045f;
        [Tooltip("落座后逐帧贴正骨盆的时长（秒）——与桌面版 坐定贴正秒 同参：站→坐姿势过渡期骨盆渐降，逐帧钉回坐线=平滑落座观感（旧版一次性摆位无贴正）")]
        [SerializeField] private float 坐定贴正秒 = 1.2f;

        // ---- 运行时状态 ----
        private RenderTexture _rt;
        private RawImage _画面;          // 全屏显示层（raycastTarget 恒 false）
        private Image _挡板;             // 全屏透明事件挡板（raycastTarget 动态=命中状态）
        private float 世界每屏幕像素;      // 相机平面世界单位 / 屏幕像素（k=H/Screen.height）
        private Vector2Int _上次屏幕尺寸;
        private PetBehaviorController _行为控制器; // 退场中冻结拖拽/滚轮交互（热切换退场期间不可抓取）

        // 拖拽状态（对齐桌面版 PetWindowController 字段语义）
        private bool 拖拽中;
        private bool prevLmbDown;
        private Vector2 拖拽起手指针RT;      // RT 像素系（鼠标×RT/屏比）
        private Vector2 拖拽起手骨盆RT;     // 起手骨盆屏幕投影（RT 像素系）——骨盆钉位基准

        private float 待写入时刻 = -1f;

        // 屏幕边缘坐状态（游戏内形态专属；PetBehaviorController 经 IPetHost.坐定中 切换待机动作）
        private bool _屏幕坐定中;
        private float _坐定x;               // 贴正期保持的水平位置
        private float _贴正截止时刻 = -10f;  // 坐定贴正秒 内逐帧钉骨盆到坐线（站→坐过渡平滑）
        /// <summary>屏幕坐定中（兼容保留的公开查询）</summary>
        public bool 屏幕坐定中 => _屏幕坐定中;
        /// <summary>坐姿动作名（兼容保留的公开查询）</summary>
        public string 屏幕坐姿动作 => 坐姿动作名;

        public override bool 正在拖拽 => 拖拽中 || 物理交互中;
        public override bool 坐定中 => _屏幕坐定中;
        public override string 坐姿动作 => 坐姿动作名;

        protected override string 日志标签 => "[PetInGame]";

        void Awake()
        {
            派蒙相机 = 派蒙相机 != null ? 派蒙相机 : GetComponentInChildren<Camera>(true);
            拖拽物理 = 拖拽物理 != null ? 拖拽物理 : GetComponentInChildren<PetDragPhysicsController>(true);
            if (蒙皮渲染器 == null) 蒙皮渲染器 = 找本体蒙皮渲染器();
            _行为控制器 = GetComponentInChildren<PetBehaviorController>(true);
            var paimonGo = transform.Find("Paimon");
            paimon根 = paimon根 != null ? paimon根 : (paimonGo != null ? paimonGo : transform).transform;

            if (派蒙相机 != null)
            {
                // 安全带（2026-08-28 背景"缩的很小"根因）：prefab 相机 tag 遗留 MainCamera——常驻实例
                // 抢占 Camera.main，MainHall 的 BackgroundParallax3D.FitToScreen（正交铺屏）解析到本相机
                // （orthoSize 0.7）会把大厅背景缩到 ~0.13 倍（编辑器直开场景 Play 无派蒙实例故正常）。
                // prefab 已改 Untagged，此行防 prefab 被旧版本覆盖回 MainCamera。
                if (派蒙相机.CompareTag("MainCamera")) 派蒙相机.tag = "Untagged";

                派蒙相机.clearFlags = CameraClearFlags.SolidColor;
                派蒙相机.backgroundColor = new Color(0, 0, 0, 0);
                派蒙相机.allowHDR = false;
                派蒙相机.allowMSAA = false;
                var urpData = 派蒙相机.GetUniversalAdditionalCameraData();
                if (urpData != null) urpData.renderPostProcessing = false;

                // 透视→正交（2026-08-28 用户复测"越拖到边缘角度越大"根治）：透视投影下模型偏离光轴
                // 即斜视畸变（视野边缘尤甚）；桌面版模型恒在窗口中心=恒正对。正交投影视线处处平行——
                // 模型在屏内任何位置都正对玩家，且世界/屏幕像素比恒定（拖拽/缩放标尺不随位置漂移）。
                // 可见世界高=原透视视野高（FOV 45.27 按模型距离折算），保持模型屏幕占比不变。
                float d = Vector3.Dot(paimon根.position - 派蒙相机.transform.position, 派蒙相机.transform.forward);
                if (d <= 0.01f) d = 1f;
                float 透视视野高 = 2f * d * Mathf.Tan(派蒙相机.fieldOfView * 0.5f * Mathf.Deg2Rad);
                派蒙相机.orthographic = true;
                派蒙相机.orthographicSize = 透视视野高 * 0.5f;
            }

            找拖拽骨骼();
            建全屏画中画();
            建命中代理();
            算基准与上限();
            恢复存档();
        }

        /// <summary>全屏画中画：RT=屏幕尺寸×rt倍率；RawImage 铺满全屏（raycastTarget=false，
        /// 交互走轮询）；挡板=全屏透明 Image（命中派蒙时开 raycastTarget 吃掉主游戏点击，
        /// 未命中穿透——桌面版 WS_EX_TRANSPARENT 穿透切换的同构）。</summary>
        void 建全屏画中画()
        {
            if (派蒙相机 == null) { Debug.LogWarning("[PetInGame] 无相机，画中画未建"); return; }

            var canvasGo = new GameObject("PetInGameCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500; // 悬浮层：高于常规 UI（弹窗走更高/独立层如需遮挡再调）
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
            _挡板 = blockerGo.GetComponent<Image>();
            _挡板.color = new Color(0f, 0f, 0f, 0f); // 全透明纯挡事件
            _挡板.raycastTarget = false;             // 默认穿透；命中派蒙时逐帧开

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
                派蒙相机.targetTexture = null;
                _rt.Release();
                Destroy(_rt);
            }
            _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Bilinear };
            _rt.Create();
            派蒙相机.targetTexture = _rt;
            派蒙相机.enabled = true;
            if (_画面 != null) _画面.texture = _rt;
        }

        /// <summary>算基准缩放（prefab 值 × 等效画布/屏幕参考 折算——保持屏幕显示尺寸与桌面版一致）
        /// 与动态缩放上限（模型最大屏高占比 ≤95%——放大到最大恰好不截屏）+ 世界/屏幕像素比。
        /// 每次补烘后重算（姿势微变 h 微变，上限跟随）。</summary>
        void 算基准与上限()
        {
            if (paimon根 == null || 派蒙相机 == null) return;
            float 折算 = 屏幕参考逻辑高 > 1f ? 等效画布逻辑高 / 屏幕参考逻辑高 : 1f;
            初始缩放 = paimon根.localScale.x * 折算;
            世界每屏幕像素 = 取视野世界高() / Mathf.Max(1f, Screen.height);
            更新有效缩放上限();
        }

        float 取视野世界高()
        {
            if (派蒙相机 == null) return 1f;
            if (派蒙相机.orthographic) return 派蒙相机.orthographicSize * 2f; // 正交：恒定可见高
            float d = Vector3.Dot(paimon根.position - 派蒙相机.transform.position, 派蒙相机.transform.forward);
            if (d <= 0.01f) d = 1f;
            return 2f * d * Mathf.Tan(派蒙相机.fieldOfView * 0.5f * Mathf.Deg2Rad);
        }

        void 更新有效缩放上限()
        {
            float 上限 = 缩放最大;
            if (命中网格碰撞体 != null && 命中网格碰撞体.sharedMesh != null)
            {
                float h = 命中网格碰撞体.bounds.size.y; // 世界包围盒高（烘焙姿势）
                if (h > 0.01f)
                {
                    float H = 取视野世界高();
                    上限 = Mathf.Min(上限, 最大屏高占比 * H / h);
                }
            }
            有效缩放最大 = Mathf.Max(上限, 缩放最小);
        }

        /// <summary>补烘完成钩子：烘焙姿势变→模型高变→动态缩放上限跟随重算</summary>
        protected override void 烘焙完成后() => 更新有效缩放上限();

        void Update()
        {
            分辨率变化帧();
            // 退场中（热切换/退场的 Disappear 播放期+播完冻结期）交互全冻结——拖拽会抓住正在退场的
            // 模型（桌面版 PetWindowController.已请求退出 同款守卫，2026-08-28 用户要求"播完就杀"配套）
            bool 交互冻结 = _行为控制器 != null && _行为控制器.退场中;
            bool modelHit = !交互冻结 && 取命中状态(Input.mousePosition);
            补烘命中网格帧();
            缩放平滑帧();
            if (!交互冻结)
            {
                拖拽轮询帧(modelHit);
                滚轮缩放帧(modelHit);
            }
            挡板切换帧(modelHit);
            坐定贴正帧();
            if (待写入时刻 > 0f && Time.unscaledTime >= 待写入时刻) 写入存档();
        }

        /// <summary>坐定贴正（桌面版 坐定帧 的贴正段同构）：落座后 坐定贴正秒 内逐帧把骨盆钉在
        /// (_坐定x, 坐定底部偏移)——站→坐姿势过渡期骨盆渐降，根随之下移=她"缓缓坐进去"。
        /// 退场中跳过（Disappear 是飞离编排，贴正会把退场动画拽回坐线）。被拖拽=拖拽起手已清坐定。</summary>
        void 坐定贴正帧()
        {
            if (!_屏幕坐定中 || Time.unscaledTime >= _贴正截止时刻) return;
            if (_行为控制器 != null && _行为控制器.退场中) return;
            摆位到归一化(new Vector2(_坐定x, 坐定底部偏移));
        }

        /// <summary>分辨率变化（全屏切换/窗口拖拽 resize）→ 重建 RT+重摆位+像素比重算</summary>
        void 分辨率变化帧()
        {
            if (Screen.width == _上次屏幕尺寸.x && Screen.height == _上次屏幕尺寸.y) return;
            _上次屏幕尺寸 = new Vector2Int(Screen.width, Screen.height);
            重建RT();
            世界每屏幕像素 = 取视野世界高() / Mathf.Max(1f, Screen.height);
            更新有效缩放上限();
            // 按骨盆归一化位重摆（模型回到屏幕上原相对位置）
            if (TryGet骨盆归一化屏幕位(out Vector2 uv)) 摆位到归一化(uv);
            Debug.Log($"[PetInGame] 分辨率变化 → {Screen.width}x{Screen.height}，RT 重建+重摆位");
        }

        /// <summary>命中检测：屏幕光标→RT 视口→射线 vs 烘焙蒙皮碰撞体（像素级，桌面版同配方）</summary>
        bool 取命中状态(Vector2 屏幕点)
        {
            if (派蒙相机 == null || 命中网格碰撞体 == null || 命中网格碰撞体.sharedMesh == null) return false;
            if (屏幕点.x < 0f || 屏幕点.x > Screen.width || 屏幕点.y < 0f || 屏幕点.y > Screen.height) return false;
            Ray ray = 派蒙相机.ScreenPointToRay(屏幕点);
            return 命中网格碰撞体.Raycast(ray, out _, 100f);
        }

        /// <summary>挡板穿透切换（桌面版 穿透切换帧 同构）：命中模型或拖拽中=可交互挡板吃点击；
        /// 其余区域点击穿透到主游戏 UI。</summary>
        void 挡板切换帧(bool modelHit)
        {
            if (_挡板 == null) return;
            bool want = modelHit || 拖拽中;
            if (_挡板.raycastTarget != want) _挡板.raycastTarget = want;
        }

        // ---- 拖拽轮询（桌面版 拖拽与双击帧 的 Unity 轮询等价；双击退出在游戏内形态无意义）----

        /// <summary>鼠标屏幕坐标 → RT 像素系（rt倍率>1 时放大）</summary>
        Vector2 指针RT() => new Vector2(
            Input.mousePosition.x * _rt.width / Screen.width,
            Input.mousePosition.y * _rt.height / Screen.height);

        void 拖拽轮询帧(bool modelHit)
        {
            if (拖拽物理 == null || _骨盆 == null || 派蒙相机 == null) return;
            bool lmbDown = Input.GetMouseButton(0);
            bool lmbPressed = lmbDown && !prevLmbDown;

            if (!拖拽中 && modelHit && lmbPressed)
            {
                拖拽起手();
            }

            if (拖拽物理.交互中)
            {
                if (拖拽中)
                {
                    if (!lmbDown)
                    {
                        拖拽中 = false;
                        拖拽物理.松手(); // 松手即停：骨盆冻结原地，四肢弹簧收尾归零
                    }
                    else
                    {
                        拖拽物理.每帧拖拽(指针RT(), Time.unscaledDeltaTime);
                        拎起姿势角帧(true);   // ①姿势旋转（基类：角平滑+挣扎+绕骨盆枢轴补偿）
                        骨盆钉位到物理目标(); // ②骨盆钉位（刚体 1:1 直跟）
                    }
                }
                else
                {
                    bool 收尾完成 = !拖拽物理.每帧收尾(Time.unscaledDeltaTime);
                    拎起姿势角帧(false); // 收尾期角度平滑归零
                    if (!拖拽物理.交互中 || 收尾完成) 物理交互收口();
                }
            }
            prevLmbDown = lmbDown;
        }

        /// <summary>拖拽起手：快照指针/骨盆 RT 位+根旋转基准（收尾中被再抓不重取基准，防旋转叠加——桌面版语义）。
        /// 物理组件喂 RT 像素系（指针与骨盆同系即可，组件内部只做差分）。</summary>
        void 拖拽起手()
        {
            拖拽中 = true;
            _屏幕坐定中 = false; // 被拖=立即解除坐定
            拖拽起手指针RT = 指针RT();
            快照拖拽基准(false); // 根旋转基准（模型位置=拖拽结果，不快照还原基准）
            拖拽起手骨盆RT = 派蒙相机.WorldToScreenPoint(_骨盆.position);
            拖拽物理.开始拖拽(拖拽起手指针RT, 拖拽起手骨盆RT);
        }

        /// <summary>骨盆钉位（桌面版 按骨盆目标定位窗口 的游戏内等价）：目标=起手骨盆+光标位移（刚体 1:1
        /// 直跟），根平移使骨盆投影钉到目标——Drag01 动画微动/旋转残余位移全部被吸收（桌面版靠移窗吸收，
        /// 此处靠移根，视觉等价）。【2026-08-28 修复：旧版只直跟根位置，动画微动致骨盆漂移】</summary>
        void 骨盆钉位到物理目标()
        {
            if (paimon根 == null || 派蒙相机 == null || _骨盆 == null) return;
            Vector3 当前投影 = 派蒙相机.WorldToScreenPoint(_骨盆.position);
            Vector2 目标 = 拖拽物理.当前骨盆屏幕;
            Vector2 dRT = 目标 - (Vector2)当前投影;
            // RT 像素 → 屏幕像素（rt倍率>1 时缩回）→ 世界位移（k 对 x/y 同比）
            float d屏幕x = dRT.x * Screen.width / Mathf.Max(1f, _rt.width);
            float d屏幕y = dRT.y * Screen.height / Mathf.Max(1f, _rt.height);
            paimon根.position += 派蒙相机.transform.right * (d屏幕x * 世界每屏幕像素)
                               + 派蒙相机.transform.up * (d屏幕y * 世界每屏幕像素);
        }

        /// <summary>物理交互收口：旋转归位（基类默认=骨盆枢轴补偿版——模型位置=拖拽结果保留，只把旋转
        /// 归回基准）+ 松手后评估屏幕边坐/防丢拉回 + 位置落盘。
        /// （桌面版 override=旋转+位置精确还原基准——那是因为桌面模型位置从未被拖拽改（窗口在动）。）</summary>
        void 物理交互收口()
        {
            拖拽收口归位();
            // 松手后评估屏幕边坐（桌面版 PetEdgeSitController.评估吸附并坐 的游戏内等价）：
            // 骨盆在屏幕底部 坐触发范围 内→磁吸落座播 SitUpright
            if (评估屏幕边坐())
            {
                // 已接管：播了坐姿动画
            }
            else
            {
                骨盆防丢拉回();
            }
            标记待写入();
        }

        /// <summary>屏幕底部磁吸坐（游戏内形态专属，桌面版 PetEdgeSitController 屏幕锚定分支的等价，
        /// 参数同款）：坐线=屏底上方 坐定底部偏移（≈桌面版任务栏顶高度）；磁吸窗=坐线上 120px/下 60px
        /// （桌面版同参，像素语义——归一化随分辨率换算，高分屏不再放大）；落座贴正 骨盆到坐线+播
        /// SitUpright+置坐定标志。返回 true=已落座接管。拖拽中不触发（拖拽起手已清坐定）。
        /// 坐标系注意：Unity 视口/屏幕 y=0 是**屏底**（y=1 顶部）——坐线=坐定底部偏移本身；
        /// 2026-08-28 曾误按 y=1 为底写 `1-偏移`，磁吸窗落到屏幕顶部=拖底不坐/拖顶瞬移坐（用户实测报障）。</summary>
        bool 评估屏幕边坐()
        {
            if (_骨盆 == null || 派蒙相机 == null || 拖拽物理 == null) return false;
            Vector3 vp = 派蒙相机.WorldToViewportPoint(_骨盆.position);
            if (vp.z <= 0f) return false;
            float 坐线 = 坐定底部偏移; // 视口 y=0=屏底，坐线即"屏底上方偏移量"
            float 上范围 = 上吸附范围像素 / Mathf.Max(1f, Screen.height);
            float 下范围 = 下吸附范围像素 / Mathf.Max(1f, Screen.height);
            if (vp.y < 坐线 - 下范围 || vp.y > 坐线 + 上范围) return false; // 磁吸窗：坐线上 120px / 下 60px

            // 磁吸：骨盆贴到坐线（贴正期逐帧钉住，见 坐定贴正帧）
            _坐定x = Mathf.Clamp01(vp.x);
            摆位到归一化(new Vector2(_坐定x, 坐定底部偏移));
            _屏幕坐定中 = true;
            _贴正截止时刻 = Time.unscaledTime + 坐定贴正秒;
            // 播坐姿动画
            var swapper = paimon根.GetComponent<PetAnimSwapper>();
            if (swapper != null && swapper.动作存在(坐姿动作名))
                swapper.Play(坐姿动作名);
            Debug.Log($"[PetInGame] 屏幕边坐触发（骨盆y={vp.y:F2}∈[{坐线 - 下范围:F2},{坐线 + 上范围:F2}]）→ 播 {坐姿动作名}");
            return true;
        }

        /// <summary>松手防丢（桌面版 拖拽松手防丢拉回 同款）：骨盆完全出屏才拉回贴边（有交集=用户可及不动）</summary>
        void 骨盆防丢拉回()
        {
            if (_骨盆 == null || 派蒙相机 == null) return;
            Vector3 vp = 派蒙相机.WorldToViewportPoint(_骨盆.position);
            if (vp.z <= 0f) return;
            float 边 = 防丢边距;
            if (vp.x > -边 && vp.x < 1f + 边 && vp.y > -边 && vp.y < 1f + 边) return; // 屏上或贴边
            float nx = Mathf.Clamp(vp.x, 边, 1f - 边);
            float ny = Mathf.Clamp(vp.y, 边, 1f - 边);
            摆位到归一化(new Vector2(nx, ny));
        }

        /// <summary>滚轮缩放（桌面版 滚轮缩放帧 同款：仅光标命中模型时响应；只改目标倍率走平滑）。
        /// 缩放改变模型尺寸→烘焙碰撞体（按旧尺寸烘的）尺寸失配=点击范围错位（放大后点视觉边缘点不中/
        /// 缩小后周围空气误中）——目标变化时排一次延迟补烘（2026-08-28 修复"有时候点不中"）。连续滚轮
        /// 顺延补烘时刻，落稳后一次烘到位。</summary>
        void 滚轮缩放帧(bool modelHit)
        {
            float scroll = Input.mouseScrollDelta.y;
            if (!modelHit || Mathf.Abs(scroll) < 0.01f) return;
            float 新缩放 = Mathf.Clamp(目标缩放 * Mathf.Pow(缩放步进, scroll), 缩放最小, 有效缩放最大);
            if (!Mathf.Approximately(新缩放, 目标缩放))
            {
                目标缩放 = 新缩放;
                _烘焙待补 = true;                                    // 缩放改了模型尺寸：补烘碰撞体
                烘焙恢复时刻 = Time.unscaledTime + 烘焙恢复宽限秒;    // 平滑落稳后再烘（连续滚轮顺延）
                标记待写入();
            }
        }

        /// <summary>四肢摆动叠加走共用基类（PetHostBase.四肢摆动应用帧，2026-08-28 合一）：
        /// 物理组件输出的摆动角以世界 Z 轴旋转叠加到四肢根骨——Animation 每帧重写骨骼姿势，
        /// 本层在其上叠加一次不累积。交互结束后停止应用，动画自然覆盖残留。</summary>
        void LateUpdate()
        {
            四肢摆动应用帧();
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
            if (_骨盆 == null || 派蒙相机 == null) return false;
            Vector3 vp = 派蒙相机.WorldToViewportPoint(_骨盆.position);
            if (vp.z <= 0f) return false;
            uv = new Vector2(vp.x, vp.y);
            return true;
        }

        /// <summary>把骨盆摆到屏幕归一化点（0..1 左下原点）——移动模型根（相机静止全屏视野）</summary>
        void 摆位到归一化(Vector2 uv)
        {
            if (_骨盆 == null || paimon根 == null || 派蒙相机 == null) return;
            Vector3 当前RT = 派蒙相机.WorldToScreenPoint(_骨盆.position);
            Vector3 目标RT = new Vector3(uv.x * Screen.width, uv.y * Screen.height, 当前RT.z);
            Vector2 dRT = (Vector2)目标RT - (Vector2)当前RT;
            // RT 像素 → 屏幕像素（rt倍率>1 时）→ 世界位移
            float d屏幕x = dRT.x * Screen.width / Mathf.Max(1f, _rt.width);
            float d屏幕y = dRT.y * Screen.height / Mathf.Max(1f, _rt.height);
            paimon根.position += 派蒙相机.transform.right * (d屏幕x * 世界每屏幕像素)
                               + 派蒙相机.transform.up * (d屏幕y * 世界每屏幕像素);
        }

        void 恢复存档()
        {
            var d = PetPrefs.读取();
            目标缩放 = d.游戏内缩放 > 0f ? d.游戏内缩放 : 初始缩放倍率;
            目标缩放 = Mathf.Clamp(目标缩放, 缩放最小, 有效缩放最大);
            显示缩放 = 目标缩放;
            if (初始缩放 <= 0.001f) 算基准与上限(); // Awake 顺序兜底
            应用模型缩放();
            // 位置：骨盆归一化屏幕位（旧版语义=画布位置，可兼容——同为屏幕归一化点）
            if (d.游戏内位置X >= 0f)
                摆位到归一化(new Vector2(Mathf.Clamp01(d.游戏内位置X), Mathf.Clamp01(d.游戏内位置Y)));
            else
                摆位到归一化(new Vector2(0.85f, 0.06f)); // 无存档：右下角（对齐桌面版停靠）

            // 启动期尺寸诊断（Warning 级=Release 构建日志也可见；对齐桌宠"建成只信日志"方法论）：
            // 一行打全尺寸链关键值，"导出包派蒙看起来远/小"类问题直接从 Player.log 读数定位
            TryGet骨盆归一化屏幕位(out Vector2 uvDiag);
            float 碰撞体高 = (命中网格碰撞体 != null && 命中网格碰撞体.sharedMesh != null) ? 命中网格碰撞体.bounds.size.y : -1f;
            Debug.LogWarning($"[PetInGame] 状态 screen={Screen.width}x{Screen.height} rt={(_rt != null ? _rt.width + "x" + _rt.height : "null")} " +
                $"ortho={派蒙相机 != null && 派蒙相机.orthographic} orthoSize={(派蒙相机 != null ? 派蒙相机.orthographicSize : -1f):F3} " +
                $"初始缩放={初始缩放:F3} 存档缩放={d.游戏内缩放:F3} 目标={目标缩放:F3} 有效最大={有效缩放最大:F2} " +
                $"碰撞体高={碰撞体高:F3} 骨盆UV=({uvDiag.x:F2},{uvDiag.y:F2}) k={世界每屏幕像素:F5}");
        }

        void 标记待写入() => 待写入时刻 = Time.unscaledTime + 1f;

        void 写入存档()
        {
#if !UNITY_EDITOR
            try
            {
                var d = PetPrefs.读取();
                d.游戏内缩放 = 目标缩放;
                if (TryGet骨盆归一化屏幕位(out Vector2 uv))
                {
                    d.游戏内位置X = uv.x;
                    d.游戏内位置Y = uv.y;
                }
                PetPrefs.写入();
            }
            finally
            {
                待写入时刻 = -1f;
            }
#endif
        }

        void OnDestroy()
        {
            写入存档();
            if (_rt != null) { _rt.Release(); Destroy(_rt); _rt = null; }
        }
    }
}
