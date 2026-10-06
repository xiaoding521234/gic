using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 战场 RTS 相机控制器（2026-09-12 用户拍板：缩放+平移像 RTS 游戏）。
    /// 透视相机俯角 55° 固定，状态 = 棋盘注视点（XZ）+ 视轴距离：
    /// - 手势输入：本类=GestureHub 的 surface（docs/24 P3）——Drag(Immediate) 识别器做拖拽判定
    ///   （按在 UI 上不起手由 hub 门2 把关），本类只做相机响应（抓取棋盘点跟随）；
    ///   B6 落地左键点击交互时直接在 _recognizers 加 TapRecognizer 即用（位移阈值=GestureMetrics 双档）
    /// - 两指捏合缩放：PinchRecognizer（2026-10-04 手机端补——判定在识别器/hub 晋升规则，
    ///   本类只按张合比缩放视轴距离，中心缩放语义与滚轮拍板一致）；滚轮缩放：轴输入维持本类
    ///   Update 轮询（以屏幕中心缩放，乘法步进，平滑缓动；不随鼠标锚点——2026-09-12 用户拍板）
    /// - WASD/方向键平移（速度随距离缩放，任意缩放级别手感一致）
    /// - 输入锁期间冻结（弹窗/场景切换——hub 门1 ForceCancel 识别器）
    /// 手感参考 MapScreen 的 MapCameraController（乘法缩放/边界 clamp）。
    /// </summary>
    public class BattleCameraController : MonoBehaviour, IGestureSurface
    {
        [Header("俯角（度，固定）")]
        [SerializeField] private float _pitchDegrees = 55f;

        [Header("缩放")]
        [Tooltip("最近视轴距离（2026-09-22 拉近 12→4：全身立牌 2.5× 后允许贴近查看单位）")]
        [SerializeField] private float _minDistance = 4f;
        [Tooltip("最远视轴距离")]
        [SerializeField] private float _maxDistance = 60f;
        [Tooltip("每滚轮一格的缩放比例（0.925 = 每格拉近 7.5%；乘法缩放各级别手感一致。2026-09-12 用户反馈 0.85 太灵敏，减半）")]
        [SerializeField, Range(0.5f, 0.99f)] private float _scrollStep = 0.925f;
        [Tooltip("缩放平滑时间常数（秒）：实际距离向目标距离指数逼近，越小跟得越紧（0=瞬移）")]
        [SerializeField, Range(0f, 0.3f)] private float _zoomSmoothTau = 0.06f;

        [Header("平移")]
        [Tooltip("键盘平移速度 = 视轴距离 × 此系数（单位/秒）")]
        [SerializeField] private float _keyPanSpeedFactor = 0.35f;

        [Header("拖动瞄准屏幕跟随（2026-09-26 拍板「当拖拽的金格在屏幕外时，屏幕会丝滑的移动过去」；2026-10-06 拍板三段改造：快进慢出 0.5s 补间+1 格余量+单位指向恒居中/出屏瞬移+取消重置回起始位）")]
        [Tooltip("跟随补间时长（秒）——快进慢出（EaseOutCubic）：方向型金格出屏触发、单位指向居中同款节奏")]
        [SerializeField] private float 拖动跟随补间时长 = 0.5f;
        [Tooltip("方向型出屏判定余量（格）——金格距视口边缘不足此格数即视为出屏触发跟随（1=多留 1 格提前量）")]
        [SerializeField] private float 拖动跟随余量格数 = 1f;

        [Header("边界")]
        [Tooltip("注视点允许范围（棋盘半宽 7.5 + 余量 2，世界单位；2026-09-30 战场 20×20→15×15 两轮缩小同步，原 12）")]
        [SerializeField] private float _focusBounds = 9.5f;

        private Camera _camera;
        private float _distance = 35f;
        private float _targetDistance = 35f;   // 缩放目标距离（滚轮只改它，实际距离逐帧平滑逼近）
        private float _initialDistance = 35f;  // 初始视轴距离（InitFromTransform 从场景摆位捕获=缩放基准；
                                                // BattleOverheadBars 相机缩放跟随消费：放大=半速率跟随、
                                                // 缩小=下限不缩，2026-10-06 拍板）
        private Vector2 _focus = Vector2.zero;   // 棋盘平面注视点（XZ）
        private Vector2 _grabPoint;              // 拖拽抓取点（棋盘 XZ；判定在 DragRecognizer，响应在本类）
        private Vector3? _dragFollowWorld;       // 拖动瞄准跟随目标（HUD 每帧喂金色待定格世界位；null=停）
        private bool _dragFollowCenter;          // 单位指向模式（2026-10-06 拍板：选中单位恒居中，MOBA 式）
        private Vector2 _dragFollowStartFocus;   // 会话起始注视点快照（2026-10-06 拍板「取消时重置摄像机位置为瞄准开始时」）
        private bool _hasDragFollowSnapshot;     // 快照有效（2026-10-06 返修：**喂 null 不清**——松手留待定后
                                                 // 点空白/取消钮取消仍须可重置；仅 CancelDragFollow/EndDragFollow 清）
        private Vector2 _followTweenFrom, _followTweenTo; // 跟随补间（快进慢出 0.5s，EaseOutCubic）
        private float _followTweenT;
        private bool _followTweenActive;

        // ── 手势层接线（docs/24 P3）──
        [Autowired] private GestureHub _gestureHub;
        // emitShortTap=true：短位移点击复合发射（2026-09-18 报障修复：漏传该参导致 HUD OnBoardTap 全链不触发，
        // 点立牌无反应；Map 同款写法。tap+pan 同体=docs/24 §7.10）
        private readonly DragRecognizer _dragRecognizer = new DragRecognizer(DragBeginMode.Immediate, emitShortTap: true);
        // 两指捏合缩放（2026-10-04 补接线：手机端无滚轮，战场此前只有滚轮一条缩放路；判定全在
        // PinchRecognizer/hub 晋升规则，本类只做相机响应——与大地图同款架构，docs/24 §5）
        private readonly PinchRecognizer _pinchRecognizer = new PinchRecognizer();
        private readonly List<GestureRecognizer> _recognizers = new List<GestureRecognizer>();
        private float _pinchStartDistance; // 捏合起手距离快照（比例基准=pinchStartDistance×张合比）

        /// <summary>当前视轴距离（探针/未来 UI 缩放按钮用）</summary>
        public float CurrentDistance => _distance;

        /// <summary>初始视轴距离（场景默认摆位推导——InitFromTransform 捕获；战斗期恒定=头顶条
        /// 相机缩放跟随的基准值）</summary>
        public float InitialDistance => _initialDistance;

        /// <summary>
        /// 板面短位移点击（docs/24 §7.10 tap+pan 同体：Immediate 拖拽面的点击由 DragRecognizer
        /// 的 ShortTap 复合发射；B6 正式 HUD 的立牌选中/瞄准拾取入口）。参数=屏幕坐标。
        /// </summary>
        public event Action<Vector2> OnBoardTap;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _recognizers.Add(_dragRecognizer);
            _recognizers.Add(_pinchRecognizer);
            _dragRecognizer.OnDragBegan += OnDragBeganHandler;
            _dragRecognizer.OnDragDelta += OnDragDeltaHandler;
            _dragRecognizer.OnShortTap += OnShortTapHandler;
            _pinchRecognizer.OnPinchBegan += OnPinchBeganHandler;
            _pinchRecognizer.OnPinchRatio += OnPinchRatioHandler;
            InitFromTransform();
        }

        private void OnPinchBeganHandler(float startDist, Vector2 midScreenPos)
        {
            _pinchStartDistance = _distance;
        }

        /// <summary>捏合缩放：战斗缩放语义=中心缩放、注视点不动（与滚轮 2026-09-12 拍板「不锚定指针」
        /// 同口径，不像大地图锚定捏合中点）。直写实际距离并同步目标——跟手零缓动，且不污染滚轮平滑链。</summary>
        private void OnPinchRatioHandler(float ratio, Vector2 midScreenPos)
        {
            _distance = Mathf.Clamp(_pinchStartDistance * ratio, _minDistance, _maxDistance);
            _targetDistance = _distance;
            ApplyTransform();
        }

        private void OnShortTapHandler(Vector2 screenPos)
        {
            OnBoardTap?.Invoke(screenPos);
        }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            Wargame.Instance?.Context?.Inject(this);
            if (_gestureHub == null)
            {
                Debug.LogWarning("[BattleCamera] GestureHub 未注入——手势层不可用（Wargame 未初始化？）");
                return;
            }
            _gestureHub.RegisterSurface(this);
        }

        private void OnDisable()
        {
            if (_gestureHub != null)
                _gestureHub.UnregisterSurface(this);
        }

        // ==================== IGestureSurface（docs/24 §4.4） ====================

        /// <summary>战场面无入场动画类自禁用场景：注册生命周期（OnEnable/OnDisable）即总开关</summary>
        public bool Enabled => true;

        /// <summary>战场世界面：指针准入恒真（UI 命中由 hub 门2 挡）</summary>
        public bool ShouldReceivePointer(in PointerEvent e) => true;

        public IReadOnlyList<GestureRecognizer> Recognizers => _recognizers;

        /// <summary>世界面：尊重 UI 命中门</summary>
        public bool BypassUIGate => false;

        /// <summary>游戏面：输入锁生效时冻结手势</summary>
        public bool IgnoresInputLocks => false;

        private void OnDragBeganHandler(Vector2 screenPos)
        {
            if (TryGetBoardPoint(screenPos, out Vector3 grab))
                _grabPoint = new Vector2(grab.x, grab.z); // 抓取棋盘点（Immediate：按下即起手）
        }

        private void OnDragDeltaHandler(Vector2 delta, Vector2 screenPos)
        {
            if (TryGetBoardPoint(screenPos, out Vector3 current))
            {
                var current2 = new Vector2(current.x, current.z);
                _focus = ClampFocus(_focus + (_grabPoint - current2)); // 原公式不变
            }
        }

        /// <summary>
        /// 从相机当前变换推导初始状态（场景默认摆位即初始值，无硬编码双源）
        /// </summary>
        private void InitFromTransform()
        {
            if (_camera == null) return;
            // 屏幕中心射线交棋盘平面（y=0）= 注视点
            if (TryGetBoardPoint(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f), out Vector3 boardPoint))
                _focus = new Vector2(boardPoint.x, boardPoint.z);
            _distance = Mathf.Clamp(
                Vector3.Distance(transform.position, new Vector3(_focus.x, 0f, _focus.y)),
                _minDistance, _maxDistance);
            _targetDistance = _distance;
            _initialDistance = _distance; // 缩放基准捕获（头顶条缩放跟随分母）
            ApplyTransform();
        }

        private void Update()
        {
            if (InputLocks.IsLocked)
            {
                return; // 锁期间冻结：识别器由 hub 门1 ForceCancel；滚轮/键盘/WASD 全部停
            }

            // 平滑缩放：实际距离逐帧向目标逼近（滚轮只写目标，避免瞬跳；指数缓动）
            if (!Mathf.Approximately(_distance, _targetDistance))
            {
                if (_zoomSmoothTau <= 0f)
                {
                    _distance = _targetDistance;
                }
                else
                {
                    float t = 1f - Mathf.Exp(-Time.unscaledDeltaTime / _zoomSmoothTau);
                    _distance = Mathf.Lerp(_distance, _targetDistance, t);
                    if (Mathf.Abs(_distance - _targetDistance) < 0.005f) _distance = _targetDistance;
                }
            }

            // 滚轮缩放：以屏幕中心缩放（不随鼠标锚点，2026-09-12 用户拍板；中心缩放与指针位置无关）
            float scroll = Input.mouseScrollDelta.y;
            if (scroll != 0f)
                Zoom(Mathf.Pow(_scrollStep, -scroll));

            // （左键拖拽判定已删——DragRecognizer(Immediate) 承担，响应见 OnDragBeganHandler/OnDragDeltaHandler；
            //  按在 UI 上不起手由 hub 门2 把关。B6 点击交互=直接加 TapRecognizer，不再"参照 Map 抄一份"）

            // WASD / 方向键平移（相机水平投影轴：右=+X，前=+Z）
            Vector2 keyMove = Vector2.zero;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) keyMove.y += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) keyMove.y -= 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) keyMove.x -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) keyMove.x += 1f;
            if (keyMove.sqrMagnitude > 0f)
            {
                keyMove.Normalize();
                _focus = ClampFocus(_focus + keyMove * (_keyPanSpeedFactor * _distance * Time.unscaledDeltaTime));
            }

            // 拖动瞄准屏幕跟随（2026-09-26 拍板+2026-10-06 三段改造）：方向型=金格出屏（余量 1 格）
            // 才发起快进慢出补间把格居中、在屏内不动；单位指向=恒居中（屏内补间/出屏瞬移）；
            // 跟随目标变化→补间 from 当前位重启（目标不变不重启防同目标反复重置进度）；
            // 取消重置走 CancelDragFollow（HUD 喂 null 仅停跟随不重置——松手留待定=相机留位）
            if (_dragFollowWorld.HasValue)
            {
                var p = _dragFollowWorld.Value;
                var s = _camera.WorldToScreenPoint(p);
                Vector2 target = new Vector2(p.x, p.z);

                if (_dragFollowCenter)
                {
                    // 单位指向（MOBA 式）：基本视口内（无余量）→补间居中；出屏（含相机背后）→瞬移居中
                    bool onScreen = s.z > 0f
                        && s.x >= 0f && s.x <= Screen.width && s.y >= 0f && s.y <= Screen.height;
                    if (onScreen)
                    {
                        BeginFollowTween(target);
                    }
                    else
                    {
                        _followTweenActive = false;
                        _focus = ClampFocus(target);
                        ApplyTransform();
                    }
                }
                else
                {
                    // 方向型：金格中心距任一视口边缘不足「余量格数」的屏幕像素（按目标点实测 1 格像素
                    // 换算——透视下各处像素尺度不同，取目标点处即可）即视为出屏→补间把格居中
                    float cellPxX = ((Vector2)_camera.WorldToScreenPoint(p + Vector3.right) - (Vector2)s).magnitude;
                    float cellPxZ = ((Vector2)_camera.WorldToScreenPoint(p + Vector3.forward) - (Vector2)s).magnitude;
                    float margin = Mathf.Max(cellPxX, cellPxZ) * 拖动跟随余量格数;
                    bool off = s.z <= 0f
                        || s.x < margin || s.x > Screen.width - margin
                        || s.y < margin || s.y > Screen.height - margin;
                    if (off) BeginFollowTween(target);
                    // 在屏内（含余量区）→不动作；进行中的补间让它跑完（中途掐断会顿挫）
                }
            }

            // 跟随补间推进（快进慢出 EaseOutCubic：快进段迅速起步、慢出段缓收）
            if (_followTweenActive)
            {
                _followTweenT = Mathf.Min(1f, _followTweenT + Time.unscaledDeltaTime / Mathf.Max(0.01f, 拖动跟随补间时长));
                float e = 1f - Mathf.Pow(1f - _followTweenT, 3f);
                _focus = ClampFocus(Vector2.Lerp(_followTweenFrom, _followTweenTo, e));
                if (_followTweenT >= 1f) _followTweenActive = false;
            }

            ApplyTransform();
        }

        /// <summary>发起/续接跟随补间：目标变化→from 当前注视点重启 0.5s 快进慢出；同目标进行中→不重启</summary>
        private void BeginFollowTween(Vector2 target)
        {
            if (_followTweenActive && _followTweenTo == target) return; // 同目标续跑（勿反复重置进度）
            _followTweenFrom = _focus;
            _followTweenTo = target;
            _followTweenT = 0f;
            _followTweenActive = true;
        }

        // ==================== 公共接口 ====================

        /// <summary>
        /// 以屏幕中心缩放（2026-09-12 用户拍板：不像大地图那样锚定鼠标点）。
        /// 滚轮只写目标距离（可多格连滚叠加），实际距离由 Update 平滑逼近——
        /// 注视点不动，纯中心缩放无横向跳变。
        /// </summary>
        public void Zoom(float stepPower)
        {
            _targetDistance = Mathf.Clamp(_targetDistance * stepPower, _minDistance, _maxDistance);
        }

        /// <summary>
        /// 视线回到棋盘中心（未来"回到棋盘"按钮/探针用；distance&lt;=0 用当前距离）。直接落位不走缓动。
        /// </summary>
        public void FocusBoardCenter(float distance = 0f)
        {
            _focus = Vector2.zero;
            if (distance > 0f) _distance = Mathf.Clamp(distance, _minDistance, _maxDistance);
            _targetDistance = _distance;
            ApplyTransform();
        }

        /// <summary>
        /// 拖动瞄准屏幕跟随喂点（HUD Update 每帧调；null=会话结束）。2026-10-06 三段改造：
        /// ①方向型（centerMode=false）：金格出屏（含「拖动跟随余量格数」提前量）才发起 0.5s
        /// 快进慢出补间把格移到屏心；在屏内则相机不动。
        /// ②单位指向型（centerMode=true，MOBA 式）：恒把选中单位居中——在屏内→0.5s 快进慢出
        /// 补间居中；不在屏内→直接瞬移居中。
        /// ③取消重置走 <see cref="CancelDragFollow"/>（松手留待定=相机留位不重置）。
        /// 首次喂点快照当前注视点=会话起始位。
        /// </summary>
        public void SetDragFollowTarget(Vector3? worldPoint, bool centerMode)
        {
            _dragFollowWorld = worldPoint;
            _dragFollowCenter = centerMode;
            if (worldPoint.HasValue && !_hasDragFollowSnapshot)
            {
                _hasDragFollowSnapshot = true;
                _dragFollowStartFocus = _focus; // 会话首点快照（取消重置基准）——**喂 null 不清快照**
            }
            // 喂 null（松手留待定/提交）只停跟随；快照留存使「留待定后再取消」仍可重置（2026-10-06 返修），
            // 提交/阶段流转由 HUD 调 EndDragFollowSession 显式清（相机留位语义）
        }

        /// <summary>拖动瞄准取消（2026-10-06 拍板「取消时，重置摄像机位置为瞄准开始时」）：
        /// 瞬移回会话起始注视点并清态。无快照（点击式瞄准/未拖过）幂等无操作。
        /// 覆盖全部取消路径：松手无待定/取消钮上松手/点空白退出瞄准/瞄准中点取消钮/换选中。
        /// 2026-10-06 二返：**必须同时清 _dragFollowWorld**——取消回调与 HUD 每帧喂点同帧竞争时
        /// （喂点先于取消），旧目标残留会让同帧相机 Update 重新发起补间；之后 HUD 喂 null 已晚
        /// （进行中的补间只看 _followTweenActive），0.5s 把已瞬移回的相机再拉回旧格——即
        /// 「瞬移回原位又被拉过去」报障根因</summary>
        public void CancelDragFollow()
        {
            if (!_hasDragFollowSnapshot) return;
            _focus = _dragFollowStartFocus;
            _hasDragFollowSnapshot = false;
            _followTweenActive = false;
            _dragFollowWorld = null;
            ApplyTransform();
        }

        /// <summary>拖动跟随会话正常终结（提交成功/阶段流转后 HUD 调）：清快照**不重置**——相机留在
        /// 拖到的位置（提交后看执行演出/阶段推进相机保持）；防下回合新拖动误用旧快照。
        /// 2026-10-06 二返：同 CancelDragFollow 清 _dragFollowWorld——提交帧存在同款喂点先于终结的
        /// 竞态窗口，残留目标会把留位相机拉走（同族隐患一并收口）</summary>
        public void EndDragFollowSession()
        {
            _hasDragFollowSnapshot = false;
            _followTweenActive = false;
            _dragFollowWorld = null;
        }

        /// <summary>快捷面板跳转聚焦（2026-10-06 拍板「点击该行则选中该角色并丝滑移动相机使其居中」）：
        /// 0.5s 快进慢出（EaseOutCubic）把注视点补间到目标位——复用拖动瞄准 BeginFollowTween 同款
        /// 机件（目标变化 from 当前位重启）；**非拖动会话**——无快照/无取消重置语义，玩家随后可自由
        /// 平移缩放；与拖动跟随并存的窗口=瞄准中点快捷行：SelectUnit 先 ExitAiming（HUD 下帧喂 null
        /// 只停跟随不掐补间），本补间继续跑完</summary>
        public void FocusWorldPoint(Vector3 worldPoint)
        {
            BeginFollowTween(new Vector2(worldPoint.x, worldPoint.z));
        }

        /// <summary>世界点→屏幕位（HUD 拖动圆盘指向锁定用；z≤0=相机背后=不可投影返回 false）</summary>
        public bool TryProjectToScreen(Vector3 worldPos, out Vector2 screenPos)
        {
            screenPos = default;
            if (_camera == null) return false;
            var s = _camera.WorldToScreenPoint(worldPos);
            if (s.z <= 0f) return false;
            screenPos = new Vector2(s.x, s.y);
            return true;
        }

        // ==================== 内部 ====================

        /// <summary>相机位姿 = 注视点 + 视轴距离（俯角固定，旋转永不被交互改变）</summary>
        private void ApplyTransform()
        {
            if (_camera == null) return;
            var rotation = Quaternion.Euler(_pitchDegrees, 0f, 0f);
            Vector3 forward = rotation * Vector3.forward; // (0, -sinθ, cosθ) 朝下前方
            transform.SetPositionAndRotation(
                new Vector3(_focus.x, 0f, _focus.y) - forward * _distance,
                rotation);
        }

        private Vector2 ClampFocus(Vector2 focus)
        {
            focus.x = Mathf.Clamp(focus.x, -_focusBounds, _focusBounds);
            focus.y = Mathf.Clamp(focus.y, -_focusBounds, _focusBounds);
            return focus;
        }

        /// <summary>屏幕点射线与 y=planeHeight 水平面的交点（板面拾取视差修正用；false=射线不与平面相交）</summary>
        public bool TryGetPlanePoint(Vector3 screenPos, float planeHeight, out Vector3 point)
        {
            point = default;
            if (_camera == null) return false;
            var ray = _camera.ScreenPointToRay(screenPos);
            var plane = new Plane(Vector3.up, Vector3.up * planeHeight);
            if (!plane.Raycast(ray, out float enter) || enter <= 0f) return false;
            point = ray.GetPoint(enter);
            return true;
        }

        /// <summary>屏幕点射线与棋盘平面（y=0，地块底面）的交点（相机平移抓取沿用，B6）。
        /// 点击取格勿用本方法——y=0 交点在俯角下沿视线向远端漂 h/tan(俯角)≈0.2~0.6 格，
        /// 而玩家视觉点击面是地块顶面（docs/14 §86）；取格走 BattleHud.TryPickBoardCell 视差修正版。</summary>
        public bool TryGetBoardPoint(Vector3 screenPos, out Vector3 point)
        {
            return TryGetPlanePoint(screenPos, 0f, out point);
        }

        // （IsPointerOverUI 已删——UI 命中收编为 GestureHub 门2 唯一实现，docs/24 §1.1-2）
    }
}
