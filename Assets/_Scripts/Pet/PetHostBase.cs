using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

namespace GIC.Pet
{
    /// <summary>
    /// 派蒙宿主共用基类（2026-08-28 共用化重构，docs/19 §6.4 复用边界）：桌面版（PetWindowController，
    /// Win32 窗口）与游戏画面内版（PetInGameHostController，全屏 RT 画中画）的共用层——
    /// 拖拽拎起姿势应用（根旋转+绕骨盆枢轴补偿+挣扎摆扭）、四肢摆动叠加、命中网格一次性烘焙、
    /// 缩放平滑、找骨/找本体蒙皮辅助。宿主差异（光标来源/移动"窗口"的方式/缩放上限语义/持久化/坐定语义）
    /// 以抽象成员与虚方法留在子类。
    ///
    /// 序列化字段迁移说明：拖拽物理/骨盆骨名/四肢骨名/拎起横躺角/拎起转身角/横躺融合速度/烘焙恢复宽限秒/
    /// 缩放最小/缩放最大/初始缩放倍率/缩放平滑速度/缩放步进 原为两控制器各自的同名同默认值字段——
    /// Unity 按字段名序列化（与声明类无关），移入基类同名保留：场景（PaimonPet.unity）与
    /// prefab（PaimonInGameRoot.prefab）的已接线引用与调参值不丢（改名才需要 FormerlySerializedAs）。
    /// </summary>
    public abstract class PetHostBase : MonoBehaviour, IPetHost
    {
        [Header("拖拽物理（docs/19 §6.1 刚体跟随+四肢摆动；纯模拟在 PetDragPhysicsController）")]
        [Tooltip("拖拽物理模拟组件（纯数学：刚体直跟+四肢跟拍弹簧+挣扎，松手即停），根旋转/四肢骨应用由宿主执行")]
        [InspectorName("拖拽物理")]
        [SerializeField] protected PetDragPhysicsController dragPhysics;

        [Header("拖拽锚点（两形态同骨名）")]
        [Tooltip("拖拽物理的锚点骨名（骨盆目标投影与拎起姿势旋转枢轴基准）")]
        [InspectorName("骨盆骨名")]
        [SerializeField] protected string pelvisBoneName = "Bip001 Pelvis";
        [Tooltip("四肢摆动的驱动骨名（拖拽跟拍弹簧），顺序=[左臂,右臂,左腿,右腿]——物理组件按此索引分配增益/频率（手臂摆幅大频率低、腿相反）。取本体骨架骨（自动排除影子壳）")]
        [InspectorName("四肢骨名")]
        [SerializeField] protected string[] limbBoneNames = { "Bip001 L UpperArm", "Bip001 R UpperArm", "Bip001 L Thigh", "Bip001 R Thigh" };

        [Header("拎起姿势（Drag01 瘫软式+3/4 偏左转身）")]
        [Tooltip("被拎起时身体姿势基准角（度）：0=直立垂落（主流桌宠被提起姿态，配 Drag01 专用拎起动画），90=头朝左横躺（旧仓鼠式）。松手收尾自动平滑归零")]
        [InspectorName("拎起横躺角")]
        [SerializeField] protected float liftLyingAngle = 0f;
        [Tooltip("被拎起时身体绕竖直轴转身角（度）：45=3/4 偏左（桌面版 v6 用户参考图），0=正对玩家。松手收尾自动平滑归零")]
        [InspectorName("拎起转身角")]
        [SerializeField] protected float liftYawAngle = 45f;
        [Tooltip("横躺角/转身角淡入淡出速度（每秒指数趋近率）——抓起转过去/松手转回来的快慢")]
        [InspectorName("横躺融合速度")]
        [SerializeField] protected float lyingBlendSpeed = 7f;

        [Header("命中")]
        [Tooltip("交互结束后延迟多久补烘一次（秒）：交互期（动作/拖拽）姿势变了，结束后补烘一次更新碰撞体；快速连续交互顺延，永不打扰正在交互的用户")]
        [InspectorName("烘焙恢复宽限秒")]
        [SerializeField] protected float bakeGraceSec = 2f;

        [Header("缩放")]
        [InspectorName("缩放最小")]
        [Tooltip("缩放倍率下限")] [SerializeField] protected float scaleMin = 0.4f;
        [InspectorName("缩放最大")]
        [Tooltip("缩放倍率上限（桌面版按工作区 95% 钳制；游戏内版按最大屏高占比动态钳制）")] [SerializeField] protected float scaleMax = 2f;
        [InspectorName("初始缩放倍率")]
        [Tooltip("无存档时的初始缩放倍率（有存档用存档值）")] [SerializeField] protected float initScaleFactor = 0.7f;
        [InspectorName("缩放平滑速度")]
        [Tooltip("缩放平滑过渡速度：每秒指数趋近速率，越大越跟手；0=瞬达无平滑")] [SerializeField] protected float scaleSmoothSpeed = 12f;
        [InspectorName("缩放步进")]
        [Tooltip("每格滚轮的缩放步进（乘法），越小越精细")] [SerializeField] protected float scaleStep = 1.05f;

        [Tooltip("派蒙模型根（游戏内 prefab 接线；桌面场景未接线=运行时找场景内 Paimon）")]
        [InspectorName("派蒙模型根")]
        [SerializeField] protected Transform paimonRoot;

        // ---- 共用运行时状态 ----
        protected SkinnedMeshRenderer bodyRenderer;
        protected MeshCollider hitMeshCollider;
        protected Mesh bakedMesh;
        protected Transform _pelvis;            // 拖拽物理锚点骨（本体骨架，排除影子壳）
        protected Transform[] _limbBones;        // 四肢摆动驱动骨（与 PetDragPhysicsController 索引约定一致）
        protected Quaternion _dragBaseRotation = Quaternion.identity; // 拖拽物理期根旋转基准（收尾中被再抓不重取，防旋转叠加）
        protected Vector3 _dragBaseRootPos;    // 桌面版收口精确还原根位置用（桌面模型位置本不被拖拽改，窗口在动）
        protected float _currentLyingAngle;          // 平滑中的拎起姿势基准角（度）
        protected float _currentYaw;          // 平滑中的拎起转身角（度）
        protected float targetScale = 1f;        // 滚轮缩放的目标倍率（持久化存这个值）
        protected float displayScale = 1f;        // 实际应用倍率（每帧向目标指数平滑趋近）
        protected float baseScale;             // 模型根基准 localScale.x（子类按各自画布语义折算）
        protected float effectiveMaxScale = 2f;    // 钳制后的实际上限（桌面=工作区预算；游戏内=屏高占比，烘焙后重算）
        protected float reBakeAt = -10f;
        protected bool _bakePausedLastFrame;
        protected bool _bakePending;

        /// <summary>日志前缀（子类按形态标注）</summary>
        protected virtual string logTag => "[PetHost]";

        // ---- IPetHost 共用实现 ----

        /// <summary>暂停命中网格重烘（行为层在单次动作期间置真）——一次性烘焙机制的开关，两形态同款</summary>
        public bool PauseHitBaking { get; set; }

        /// <summary>物理交互进行中（拖拽跟随/收尾归零）——行为层压制触发用</summary>
        public bool PhysicsBusy => dragPhysics != null && dragPhysics.IsActive;

        /// <summary>最近一次拖拽时长（秒，松手时冻结；-1=无）——行为层放下反应分档用</summary>
        public float DragSeconds => dragPhysics != null ? dragPhysics.LastDragDuration : -1f;

        /// <summary>命中网格的世界包围盒（行为层接近判定用）：MeshCollider（BakeMesh 烘的真实蒙皮网格+
        /// 与 SMR 同 transform，PhysX 世界包围盒正确）。勿用 SMR.bounds：GI 模型的它漏一层缩放
        /// （世界 42 单位 vs 可见 0.6，×100 错误），投影恒跨相机平面→接近判定恒 false（2026-08-26 根治）。
        /// 碰撞体未就绪时返回 false。</summary>
        public bool TryGetHitWorldBounds(out Bounds bounds)
        {
            bounds = default;
            if (hitMeshCollider == null || hitMeshCollider.sharedMesh == null) return false;
            bounds = hitMeshCollider.bounds;
            return true;
        }

        public abstract bool IsDragging { get; }
        public abstract bool TryGetCursorUnityScreenPos(out Vector2 unityScreenPos);
        public abstract bool IsSeated { get; }
        public abstract string SitAnim { get; }

        /// <summary>边缘坐接管默认=不支持（游戏内形态：屏幕坐已在物理收口评估恒 false）；桌面 override 转发边坐控制器</summary>
        public virtual bool TrySnapAndSit() => false;

        /// <summary>边缘坐掉落中默认 false（游戏内无掉落概念）；桌面 override</summary>
        public virtual bool IsEdgeFalling => false;

        // ---- 找骨/找蒙皮（排除影子壳 _DropShadow / MMD_DropShadow 下的同名骨拷贝） ----

        protected Transform FindBodyBone(string boneName)
        {
            if (paimonRoot == null) return null;
            foreach (var t in paimonRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != boneName) continue;
                bool shadowBelow = false;
                for (var p = t.parent; p != null && !shadowBelow; p = p.parent)
                    if (p.name == "_DropShadow" || p.name == "MMD_DropShadow") shadowBelow = true;
                if (shadowBelow) continue;
                return t;
            }
            return null;
        }

        /// <summary>取本体蒙皮渲染器（排除影子壳 _DropShadow/PaimonShadow 层）——static 供行为层
        /// 等非宿主继承链共用（旧版 protected 实例方法逼得行为层复制了一份过滤循环，2026-08-31 收拢）</summary>
        public static SkinnedMeshRenderer FindBodyRenderer(Transform root)
        {
            var shadowLayer = LayerMask.NameToLayer("PaimonShadow");
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (!smr.name.Contains("_DropShadow") && smr.gameObject.layer != shadowLayer)
                    return smr;
            return null;
        }

        /// <summary>接线拖拽骨骼（骨盆+四肢）。缺失肢不摆动（告警不阻断）。</summary>
        protected void WireDragBones()
        {
            if (paimonRoot == null) return;
            _pelvis = FindBodyBone(pelvisBoneName);
            _limbBones = new Transform[limbBoneNames != null ? limbBoneNames.Length : 0];
            int found = 0;
            for (int i = 0; i < _limbBones.Length; i++)
            {
                _limbBones[i] = FindBodyBone(limbBoneNames[i]);
                if (_limbBones[i] != null) found++;
            }
            if (found < _limbBones.Length)
                Debug.LogWarning($"{logTag} 四肢摆动骨缺失 {_limbBones.Length - found}/{_limbBones.Length}（缺失肢不摆动）");
        }

        // ---- 命中代理与一次性补烘（docs/19 §6.4：两形态像素级命中同配方） ----

        /// <summary>像素级命中代理：BakeMesh 烘蒙皮网格 + 同 transform MeshCollider（useScale=true
        /// 输出在 SMR 局部空间，命中节点须与 SMR 同 transform）。快烹饪：只吃 1 条 raycast/帧，
        /// UseFastMidphase 换取 cook 尖峰显著缩短（全量 cook 15-22ms 是动作切换停顿根因之一）。</summary>
        protected void BuildHitProxy()
        {
            if (bodyRenderer == null) return;
            var hitGo = new GameObject("_HitMeshProxy");
            hitGo.transform.SetParent(bodyRenderer.transform.parent, false);
            hitGo.transform.localPosition = bodyRenderer.transform.localPosition;
            hitGo.transform.localRotation = bodyRenderer.transform.localRotation;
            hitGo.transform.localScale = bodyRenderer.transform.localScale;
            hitMeshCollider = hitGo.AddComponent<MeshCollider>();
            hitMeshCollider.cookingOptions = MeshColliderCookingOptions.UseFastMidphase;
            bakedMesh = new Mesh();
            bodyRenderer.BakeMesh(bakedMesh, true);
            hitMeshCollider.sharedMesh = bakedMesh;
        }

        /// <summary>一次性补烘（2026-08-27 终案）：周期性重烘全删——交互期（动作/拖拽）姿势变了，
        /// 解除+宽限期到后补烘一次；快速连续交互不断顺延（碰撞体保持旧壳，±2-3° 姿态差=几像素无损）。
        /// 启动时 建命中代理 已烘一次。</summary>
        protected void RebakeHitMeshFrame()
        {
            bool bakePaused = PauseHitBaking || PhysicsBusy;
            if (bakePaused && !_bakePausedLastFrame) _bakePending = true;        // 进入交互：姿势要变了
            if (_bakePausedLastFrame && !bakePaused)
                reBakeAt = Time.unscaledTime + Mathf.Max(0f, bakeGraceSec); // 解除：宽限后补
            _bakePausedLastFrame = bakePaused;
            if (_bakePending && !bakePaused && Time.unscaledTime >= reBakeAt
                && bodyRenderer != null && hitMeshCollider != null)
            {
                bodyRenderer.BakeMesh(bakedMesh, true);
                hitMeshCollider.sharedMesh = null; // 强制碰撞体刷新
                hitMeshCollider.sharedMesh = bakedMesh;
                _bakePending = false;
                PetDiag.LastSkinRebake = Time.unscaledTime; // 顿挫诊断标记（PetFrameStats 回查）
                OnBakeCompleted();
            }
        }

        /// <summary>补烘完成钩子（游戏内版重算动态缩放上限——烘焙姿势变→模型高变→上限跟随）</summary>
        protected virtual void OnBakeCompleted() { }

        // ---- 拎起姿势应用（docs/19 §6.1：两形态视觉等价——桌面版靠移窗钉骨盆，游戏内版移根） ----

        /// <summary>拖拽起手快照根旋转基准（收尾中被再抓不重取，防旋转叠加——两形态同语义）。
        /// include位置=桌面版附加快照根位置（收口精确还原）；游戏内版模型位置=拖拽结果不还原。</summary>
        protected void SnapshotDragBaseline(bool include位置)
        {
            if (PhysicsBusy || paimonRoot == null) return;
            _dragBaseRotation = paimonRoot.localRotation;
            if (include位置) _dragBaseRootPos = paimonRoot.position;
        }

        /// <summary>拎起姿势角平滑+根旋转应用（桌面版 应用物理帧 的旋转部分与游戏内版同一段，2026-08-28 合一）：
        /// 拖拽期→拎起横躺角/拎起转身角，收尾期→0 平滑归零；+挣扎摆/扭（绕骨盆枢轴）；
        /// 旋转后补偿根平移把骨盆钉回旋转前世界位（动画微动保留，仅抵消旋转带来的位移）。</summary>
        protected void LiftPoseAngleFrame(bool isDragging)
        {
            if (_pelvis == null || paimonRoot == null || dragPhysics == null) return;
            float lyingTarget = isDragging ? liftLyingAngle : 0f;
            float yawTarget = isDragging ? liftYawAngle : 0f;
            float k = 1f - Mathf.Exp(-lyingBlendSpeed * Time.unscaledDeltaTime);
            _currentLyingAngle = Mathf.Lerp(_currentLyingAngle, lyingTarget, k);
            _currentYaw = Mathf.Lerp(_currentYaw, yawTarget, k);

            float struggleSwing_ = dragPhysics.CurrentStruggleSwing;
            float struggleTwist_ = dragPhysics.CurrentStruggleTwist;
            Vector3 pelvisBeforeRotation = _pelvis.position;
            paimonRoot.localRotation = Quaternion.AngleAxis(_currentLyingAngle + struggleSwing_, Vector3.forward)
                                   * Quaternion.AngleAxis(_currentYaw + struggleTwist_, Vector3.up)
                                   * _dragBaseRotation;
            Vector3 offset = pelvisBeforeRotation - _pelvis.position;
            if (offset.sqrMagnitude > 1e-10f) paimonRoot.position += offset;
        }

        /// <summary>四肢摆动叠加（Animation 每帧重写骨骼姿势后在其上叠加一次，不累积）：
        /// 物理组件输出的摆动角以世界 Z 轴（屏幕平面法线）旋转叠加到四肢根骨（肩/大腿）。
        /// **Transform.rotation 就是世界旋转**（localRotation 才是父系量）——直接前置乘 AngleAxis 即正确，
        /// 勿做父系共轭换算（曾致左右手反向摆）。交互结束后停止应用，动画自然覆盖残留。
        /// 由子类 LateUpdate 调用。</summary>
        protected void LimbSwingApplyFrame()
        {
            if (dragPhysics == null || !dragPhysics.IsActive || _limbBones == null) return;
            for (int i = 0; i < _limbBones.Length; i++)
            {
                var bone = _limbBones[i];
                if (bone == null) continue;
                dragPhysics.GetLimbSwing(i, out float swingOut);
                if (swingOut != 0f)
                    bone.rotation = Quaternion.AngleAxis(swingOut, Vector3.forward) * bone.rotation;
            }
        }

        /// <summary>物理交互收口的旋转归位（默认=游戏内语义：模型位置是拖拽结果要保留，只按骨盆
        /// 枢轴补偿把旋转归回基准）；桌面版 override=旋转+位置精确还原基准（桌面模型位置本不动，窗口在动）。</summary>
        protected virtual void DragSettleRestore()
        {
            if (paimonRoot != null && _pelvis != null)
            {
                Vector3 pelvisBeforeRotation = _pelvis.position;
                paimonRoot.localRotation = _dragBaseRotation;
                Vector3 offset = pelvisBeforeRotation - _pelvis.position;
                if (offset.sqrMagnitude > 1e-10f) paimonRoot.position += offset;
            }
            _currentLyingAngle = 0f;
            _currentYaw = 0f;
        }

        // ---- 缩放（两形态同款：只改目标倍率+平滑应用模型 localScale） ----

        /// <summary>缩放平滑过渡帧：显示缩放向目标指数趋近，只改模型 localScale（桌面版窗口尺寸恒定
        /// 防闪烁；游戏内版恒整屏画布）。速度=0 时步进=1（瞬达）。</summary>
        protected void ScaleSmoothFrame()
        {
            if (Mathf.Approximately(displayScale, targetScale)) return;
            float t = scaleSmoothSpeed <= 0f ? 1f : 1f - Mathf.Exp(-Time.unscaledDeltaTime * scaleSmoothSpeed);
            displayScale += (targetScale - displayScale) * t;
            if (Mathf.Abs(targetScale - displayScale) < 0.0005f) displayScale = targetScale;
            ApplyModelScale();
        }

        /// <summary>把显示缩放叠乘基准缩放应用到 Paimon 根 localScale</summary>
        protected void ApplyModelScale()
        {
            if (paimonRoot == null) return;
            float s = baseScale * displayScale;
            paimonRoot.localScale = new Vector3(s, s, s);
        }

        // ---- 对话装配（2026-08-31 批 5 下沉：两宿主 WireChat/WireReactionChannel 整段重复收拢） ----

        protected PetBehaviorController behaviorCtrl;                       // 行为层（聊天动作回调/反应通道用；两宿主各自在 Start/Awake 缓存）
        protected Chat.PetChatUIController _chatUI;                         // 聊天 UI 组件（未挂/接线失败=null）

        /// <summary>对话装配共用主体：查组件 → 建画布（宿主差异）→ WireHost → 锚点/工具注册 → 反应通道。
        /// Canvas 由宿主直传不挪物体（2026-08-29 修单击 NRE：组件挂 prefab 根时 SetParent 挪根=循环父子被拒）。
        /// 聊天是可选功能：任一环节失败只禁用聊天组件，绝不上抛（Awake/Start 异常会禁用整个宿主组件
        /// → Update 全停=点不了拖不动，2026-08-29 事故防泄漏结构——全 try-catch）。</summary>
        protected void WireChat()
        {
            _chatUI = GetComponentInChildren<Chat.PetChatUIController>(true);
            if (_chatUI == null) return; // 场景/prefab 未挂（旧场景/裁剪安装）=对话功能缺席，其余交互不受影响
            try
            {
                var canvas = EnsureChatCanvas();
                if (canvas == null) return; // 无画布（异常态）不接
                _chatUI.WireHost(canvas);
                _chatUI.headAnchorProvider = ChatHeadAnchor;
                _chatUI.footAnchorProvider = ChatFootAnchor;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"{logTag} 聊天 UI 构建失败，已禁用（交互不受影响）：{e.Message}");
                _chatUI.enabled = false;
                _chatUI = null;
                return;
            }
            RegisterChatTools();
            WireReactionChannel();
            Debug.Log($"{logTag} 对话已接线（单击派蒙开输入条）");
        }

        /// <summary>聊天画布（宿主差异）：桌面=运行时建 Overlay 画布（含 EventSystem 补建）；游戏内=既有画中画画布</summary>
        protected abstract Canvas EnsureChatCanvas();
        /// <summary>头锚点（宿主差异：投影链路——桌面无 RT 直通，游戏内 RT→屏幕换算）</summary>
        protected abstract Vector2 ChatHeadAnchor();
        /// <summary>脚锚点（输入条挂模型脚底下方用，宿主差异同上）</summary>
        protected abstract Vector2 ChatFootAnchor();
        /// <summary>Intent 工具执行分发（宿主差异）：桌面=PetIntentIpc 文件通道转发主进程；游戏内=主进程内直调。
        /// 本地工具（memory_update/do_action）由会话层拦截不经此分发（模型在宠物进程，转发主进程是错的）</summary>
        protected abstract string ExecuteChatTool(string toolName, string toolArgsJson);

        /// <summary>Intent 工具表注册（两形态同表：记忆/情绪动作/游戏时间×2/界面导航/自动抽卡）。
        /// 注册失败只少工具不影响聊天（防泄漏结构同上）。</summary>
        void RegisterChatTools()
        {
            try
            {
                var session = _chatUI != null ? _chatUI.sessionRef : null;
                if (session == null) return;
                var tools = new System.Collections.Generic.List<Chat.PetChatClient.ToolDefinition>
                {
                    Chat.PaimonChatSession.MemoryToolDefinition(),
                    Chat.PaimonChatSession.DoActionTool(), // 情绪动作（LLM 对话自主选，会话层本地拦截不经 IPC）
                    Chat.PetChatIntent.SetGameTimeTool(),
                    Chat.PetChatIntent.GetGameTimeTool(),
                    Chat.PetChatIntent.OpenScreenTool(),
                    Chat.PetChatIntent.AutoWishTool(),
                };
                session.RegisterTools(tools, (toolName, toolArgs) => ExecuteChatTool(toolName, toolArgs));
                // do_action 动作回调（会话层本地消化后回调）：行为层播单次动作
                //（拖拽物理中/退场中 PlayReaction 内部静默跳过——反应错失可接受）
                session.onPlayAction = anim => behaviorCtrl?.PlayReaction(anim);
                Debug.Log($"{logTag} 对话指令工具已注册（游戏时间/界面/自动抽卡/情绪动作）");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"{logTag} 对话指令工具注册失败（聊天基础功能不受影响）：{e.Message}");
            }
        }

        /// <summary>反应通道消费接线：主进程写入的反应事件（文本+动作+LLM 注记）→ 行为层播动作 +
        /// 气泡直出 + 会话历史注记。独立 try-catch 防泄漏（同上结构）。</summary>
        void WireReactionChannel()
        {
            try
            {
                var consumer = GetComponent<Chat.PetReactionConsumer>();
                if (consumer == null) consumer = gameObject.AddComponent<Chat.PetReactionConsumer>();
                consumer.Wire(behaviorCtrl, _chatUI);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"{logTag} 反应通道接线失败（其余功能不受影响）：{e.Message}");
            }
        }

        /// <summary>透明隔光相机统一配置（两形态同款：SolidColor 透明底 + 关 HDR/MSAA/后处理——
        /// DWM 逐像素 alpha 与 RT 光隔离的共同前提；后处理破坏 alpha 通道）</summary>
        protected static void ConfigureTransparentCamera(Camera target, Color background)
        {
            target.clearFlags = CameraClearFlags.SolidColor;
            target.backgroundColor = background;
            target.allowHDR = false;
            target.allowMSAA = false;
            var urp = target.GetUniversalAdditionalCameraData();
            if (urp != null) urp.renderPostProcessing = false;
        }
    }
}
