// MapCameraController.cs - 大地图相机控制器（垂直画布正交版，Unity 2D 约定）
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using GIC.Framework;
using UnityEngine.Serialization;

namespace GIC.UI
{
    /// <summary>
    /// 大地图相机控制器（垂直画布正交版，Unity 2D 约定）。
    /// 地图平铺在 XY 竖直平面（z=0），相机沿 -Z 看，rotation 归零：
    /// - 拖拽平移：抓取画布点跟随（鼠标/单指）
    /// - 缩放：滚轮/双指捏合调整正交尺寸（比例式）
    /// - 惯性滑行：快速拖拽松手后按末速度继续平移，指数衰减至停（2026-09-01）
    /// - 边界：注视点按当前可见范围限制在地图矩形内（画面不露出地图外）
    /// - 入场动画：尺寸从倍数回落（快进慢出），期间持有 MapEntering 输入锁
    /// - 点击：按下与抬起位移小于阈值视为点击，射线检测 3D 锚点并分发
    /// - 双指起手防抢：两指都落在 UI 上（含游戏内派蒙挡板）不缩放地图——捏派蒙归派蒙
    /// </summary>
    public class MapCameraController : MonoBehaviour
    {
        private const float cameraFixedDist = 100f;

        [Header("缩放")]
        [InspectorName("最小尺寸")]
        [SerializeField] private float minSize = 5f;
        [InspectorName("最大尺寸")]
        [SerializeField] private float maxSize = 45f;
        [Tooltip("每滚轮一格的缩放比例（0.85 = 每格缩小 15%）。乘法缩放保证任意级别下视觉变化一致，到上下限有明确停顿感")]
        [InspectorName("滚轮缩放步进")]
        [SerializeField, Range(0.5f, 0.99f)] private float scrollStep = 0.85f;
        [Tooltip("滚轮缩放平滑时间常数（秒）：实际尺寸/注视点向目标指数逼近，越小跟得越紧（0=瞬移）。2026-09-12 反哺自战斗相机，消除滚轮瞬跳")]
        [InspectorName("缩放平滑时间常数")]
        [SerializeField, Range(0f, 0.3f)] private float zoomSmoothTau = 0.06f;

        [Header("区域聚焦")]
        [InspectorName("默认视野尺寸")]
        [SerializeField] private float defaultViewSize = 8f;
        [InspectorName("入场时长")]
        [SerializeField] private float enterDuration = 0.2f;

        [Header("点击判定")]
        [InspectorName("点击位移阈值")]
        [SerializeField] private float clickMoveThreshold = 12f;
        [InspectorName("锚点射线最大距离")]
        [SerializeField] private float anchorRayMaxDist = 500f;

        [Header("惯性滑行")]
        [Tooltip("松手滑行的指数衰减速率（每秒速度衰减比，越大停得越快；3≈1 秒衰减到 5%）")]
        [InspectorName("滑行衰减速率")]
        [SerializeField, Range(0.5f, 8f)] private float glideDamping = 3f;
        [Tooltip("松手末速度低于此值不滑行，滑行中低于此值即停（世界单位/秒）")]
        [InspectorName("滑行速度阈值")]
        [SerializeField] private float glideMinSpeed = 1.5f;

        private Camera _camera;
        private Vector2 _mapCenter;        // 地图矩形中心（世界 XY，由 InitBounds 按标定计算）
        private float _mapHalfW = 107.5f;   // 地图半宽（世界单位，X 方向）
        private float _mapHalfH = 69f;      // 地图半高（世界单位，Y 方向）

        // 相机状态：画布注视点（x→世界X，y→世界Y）+ 正交尺寸（垂直半高，世界单位）
        private Vector2 _focus;
        private float _size = 8f;

        // 滚轮平滑缩放目标值（滚轮只写目标，实际值由 SmoothZoomFrame 逐帧逼近；
        // 拖拽/捏合/入场动画/惯性滑行直写实际值并同步目标，互不干扰）
        private float _targetSize = 8f;
        private Vector2 _targetFocus;

        private Coroutine _entryCoroutine;
        private AnimationCurve _easeCurve;

        // 拖拽状态（鼠标与单指共用）
        private bool _dragging;
        private Vector2 _pressScreenPos;
        private Vector2 _grabGround;

        // 双指捏合状态
        private bool _pinching;
        private float _pinchStartDist;
        private float _pinchStartSize;

        // 惯性滑行：拖拽中=实测平滑末速度，松手后=滑行速度（同字段无缝交接——松手瞬间的
        // "当前速度"即滑行初速）
        private Vector2 _panVelocity;
        private const float velocitySmoothSec = 0.04f; // 末速度 EMA 时间常数（≈2-3 帧：只反映松手前最近手势，先停住再松≈0 速不滑）

        // 位置式 UI 命中缓存（IsScreenPointOverUI 用，避免每次分配）
        private static readonly List<RaycastResult> _uiRaycastBuffer = new List<RaycastResult>(8);

        /// <summary>入场动画是否正在播放</summary>
        public bool IsAnimating => _entryCoroutine != null;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            ResetEaseCurve();
            ApplyCameraSetup();
            SyncZoomTargets();
        }

        /// <summary>注视点目标同步（拖拽/滑行只直写 _focus：位置由手接管、清其缓动，
        /// 但**保留尺寸目标**——滑行中滚轮缩放正常生效（2026-09-12 修复：曾把 _targetSize 一并回写，滚轮目标被逐帧抹掉=滑行期缩放灵敏度极低）</summary>
        private void SyncFocusTarget()
        {
            _targetFocus = _focus;
        }

        /// <summary>全量目标同步（直控尺寸的路径用：捏合/入场动画收尾/Awake 初始化）</summary>
        private void SyncZoomTargets()
        {
            _targetSize = _size;
            _targetFocus = _focus;
        }

        private void Reset()
        {
            ResetEaseCurve();
        }

        private void OnValidate()
        {
            if (!Application.isPlaying)
            {
                ResetEaseCurve();
                ApplyCameraSetup();
            }
        }

        private void OnDestroy()
        {
            // 兜底：动画协程被销毁中断时释放本类持有的锁
            InputLocks.PopAll(this);
        }

        /// <summary>快进慢出曲线：开始陡峭，结束平缓（与原 SimpleMapZoom 一致）</summary>
        private void ResetEaseCurve()
        {
            Keyframe[] keys = new Keyframe[3];
            keys[0] = new Keyframe(0f, 0f, 0f, 2f);
            keys[1] = new Keyframe(0.5f, 0.85f, 1f, 1f);
            keys[2] = new Keyframe(1f, 1f, 0f, 0f);
            _easeCurve = new AnimationCurve(keys);
        }

        private void ApplyCameraSetup()
        {
            if (_camera == null) _camera = GetComponent<Camera>();
            if (_camera == null) return;
            _camera.orthographic = true;
            _size = Mathf.Clamp(_size, minSize, maxSize); // 硬 clamp：任何来源的尺寸都不越界
            _camera.orthographicSize = _size;
            // 垂直画布（Unity 2D 约定）：地图平铺 XY 竖直平面，相机沿 -Z 看，rotation 归零
            _camera.transform.rotation = Quaternion.identity;
            _camera.transform.position = new Vector3(_focus.x, _focus.y, -cameraFixedDist);
        }

        // ==================== 公共接口 ====================

        /// <summary>初始化地图边界：中心（世界 XY）+ 尺寸（世界单位），限制相机注视点范围。
        /// 边界围绕图中心而非世界原点——标定改变 mapOrigin 后图中心会移动（旧图恰好居中原点是巧合，勿依赖）</summary>
        public void InitBounds(Vector2 mapCenter, float mapWidth, float mapHeight)
        {
            _mapCenter = mapCenter;
            _mapHalfW = mapWidth * 0.5f;
            _mapHalfH = mapHeight * 0.5f;
        }

        /// <summary>
        /// 聚焦区域：以世界坐标点为中心播放动画。
        /// viewSize &lt;= 0 时使用默认视野尺寸。
        /// fromMaxZoom=true 从最大远景落下（初次打开地图）；
        /// false（默认）从当前视野平滑滑移到目标（原神式区域切换）。
        /// </summary>
        public void FocusRegion(Vector2 focusXY, float viewSize = 0f, bool fromMaxZoom = false)
        {
            // 未配置区域视野时落点 = 缩放区间中点（居中视野，非最大也非最底）
            float targetSize = viewSize > 0f ? Mathf.Clamp(viewSize, minSize, maxSize) : Mathf.Lerp(minSize, maxSize, 0.5f);
            PlayEntryAnimation(ClampFocus(focusXY, targetSize), targetSize, fromMaxZoom);
        }

        /// <summary>当前正交尺寸（锚点恒定视觉尺寸等外部逻辑用）</summary>
        public float CurrentSize => _size;

        /// <summary>基准视野尺寸（锚点视觉尺寸在此缩放下为 1:1 prefab 原大）</summary>
        public float BaseViewSize => defaultViewSize;

        // ==================== 每帧交互 ====================

        private void Update()
        {
            if (InputLocks.IsLocked)
            {
                // 锁期间中断进行中的手势与滑行（幂等，下一帧手势重新判定）
                _dragging = false;
                _pinching = false;
                _panVelocity = Vector2.zero;
                return;
            }

            if (Input.touchSupported && Input.touchCount > 0)
                HandleTouch();
            else
                HandleMouse();

            SmoothZoomFrame();
            GlideFrame();
        }

        private void HandleMouse()
        {
            // 滚轮缩放（以鼠标画布点为锚；乘法缩放；只写目标值，SmoothZoomFrame 平滑逼近——2026-09-12 平滑化）
            if (Input.mouseScrollDelta.y != 0f && TryGetGroundPoint(Input.mousePosition, out Vector2 anchor))
                SmoothZoomAtScreenPoint(_targetSize * Mathf.Pow(scrollStep, -Input.mouseScrollDelta.y), anchor);

            bool pressed = Input.GetMouseButtonDown(0);
            if (pressed) _panVelocity = Vector2.zero; // 任何按下都掐断滑行（抓停语义，含按在 UI 上）

            if (pressed && !IsPointerOverUI(-1))
            {
                if (TryGetGroundPoint(Input.mousePosition, out _grabGround))
                {
                    _dragging = true;
                    _pressScreenPos = Input.mousePosition;
                }
            }
            else if (_dragging && Input.GetMouseButton(0))
            {
                if (TryGetGroundPoint(Input.mousePosition, out Vector2 current))
                    DragTo(current);
            }
            else if (_dragging && Input.GetMouseButtonUp(0))
            {
                _dragging = false;
                // 抬起点也须不在 UI 上（按下于地图、滑到按钮上抬起的场景不分发点击）
                bool clicked = !IsPointerOverUI(-1) &&
                    Vector2.Distance(Input.mousePosition, _pressScreenPos) < clickMoveThreshold;
                if (clicked)
                {
                    _panVelocity = Vector2.zero; // 点击松手不滑行（快速轻点的末速可能虚高）
                    DispatchAnchorClick(Input.mousePosition);
                }
                else if (_panVelocity.magnitude < glideMinSpeed)
                    _panVelocity = Vector2.zero; // 真实拖拽但末速不足：不滑行
            }
        }

        private void HandleTouch()
        {
            if (Input.touchCount == 1)
            {
                _pinching = false;
                Touch t = Input.GetTouch(0);

                bool began = t.phase == TouchPhase.Began;
                if (began) _panVelocity = Vector2.zero; // 任何按下都掐断滑行（抓停语义，含按在 UI 上）

                if (began && !IsPointerOverUI(t.fingerId))
                {
                    if (TryGetGroundPoint(t.position, out _grabGround))
                    {
                        _dragging = true;
                        _pressScreenPos = t.position;
                    }
                }
                else if (_dragging && (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary))
                {
                    // Stationary 也走跟随：指针静止帧喂 0 位移给末速度 EMA 衰减——否则按住不动
                    // 松手会按停住前的旧速度滑（EMA 只在被更新时衰减）
                    if (TryGetGroundPoint(t.position, out Vector2 current))
                        DragTo(current);
                }
                else if (_dragging && (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled))
                {
                    _dragging = false;
                    // 抬起点也须不在 UI 上（同鼠标路径）
                    bool clicked = !IsPointerOverUI(t.fingerId) &&
                        Vector2.Distance(t.position, _pressScreenPos) < clickMoveThreshold;
                    if (clicked)
                    {
                        _panVelocity = Vector2.zero; // 点击松手不滑行（快速轻点的末速可能虚高）
                        DispatchAnchorClick(t.position);
                    }
                    else if (_panVelocity.magnitude < glideMinSpeed)
                        _panVelocity = Vector2.zero; // 真实拖拽但末速不足：不滑行
                }
            }
            else if (Input.touchCount == 2)
            {
                // 双指=捏合意图：取消单指拖拽与其惯性
                _dragging = false;
                _panVelocity = Vector2.zero;
                Touch t0 = Input.GetTouch(0);
                Touch t1 = Input.GetTouch(1);

                if (_pinching)
                {
                    float dist = Vector2.Distance(t0.position, t1.position);
                    if (dist > 1f && TryGetGroundPoint((t0.position + t1.position) * 0.5f, out Vector2 mid))
                    {
                        // 比例式捏合：两指张合比例直接映射为尺寸比例
                        float newSize = _pinchStartSize * (_pinchStartDist / dist);
                        SetSizeAtScreenPoint((t0.position + t1.position) * 0.5f, newSize, mid);
                    }

                    if (t0.phase == TouchPhase.Ended || t0.phase == TouchPhase.Canceled ||
                        t1.phase == TouchPhase.Ended || t1.phase == TouchPhase.Canceled)
                        _pinching = false;
                }
                else
                {
                    // 起手须两指都不在 UI 上（位置式检测，见 IsScreenPointOverUI）：捏派蒙（挡板=UI）
                    // 归派蒙缩放、捏按钮归按钮；两指同点（无比例基准）也不起手
                    float startDist = Vector2.Distance(t0.position, t1.position);
                    if (startDist > 1f && !IsScreenPointOverUI(t0.position) && !IsScreenPointOverUI(t1.position))
                    {
                        _pinching = true;
                        _pinchStartDist = startDist;
                        _pinchStartSize = _size;
                    }
                }
            }
        }

        // ==================== 惯性滑行 ====================

        /// <summary>拖拽跟随（鼠标/单指共用）：抓取画布点钉在指针下方，并平滑记录末速度。
        /// 记的是 clamp 后的实际画面位移——贴边拖拽只计沿边分量，速度天然正确；
        /// EMA 时间常数≈2-3 帧：甩着松手=按最近手势速度滑行，先停住再松≈0 速不滑。</summary>
        private void DragTo(Vector2 currentGround)
        {
            Vector2 next = ClampFocus(_focus + (_grabGround - currentGround), _size);
            Vector2 moved = next - _focus;
            _focus = next;
            SyncFocusTarget(); // 拖拽直写注视点：位置目标跟随手，保留滚轮尺寸目标
            ApplyCameraSetup();

            float dt = Time.unscaledDeltaTime;
            if (dt > 0.0001f)
            {
                float w = 1f - Mathf.Exp(-dt / velocitySmoothSec);
                _panVelocity = Vector2.Lerp(_panVelocity, moved / dt, w);
            }
        }

        /// <summary>滑行帧（松手后）：按末速度继续平移注视点，指数衰减。停止条件：速度低于阈值、
        /// 两轴都被边界顶死；新手势/输入锁/入场动画接管时各自清零。</summary>
        private void GlideFrame()
        {
            if (_dragging || _pinching) return; // 手势进行中：速度字段由 DragTo 维护，不滑行
            float sqrMin = glideMinSpeed * glideMinSpeed;
            if (_panVelocity.sqrMagnitude < sqrMin) { _panVelocity = Vector2.zero; return; }

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f); // 钳制防卡顿帧大跳
            Vector2 next = ClampFocus(_focus + _panVelocity * dt, _size);
            if ((next - _focus).sqrMagnitude < 1e-10f) { _panVelocity = Vector2.zero; return; } // 边界顶死
            _focus = next;
            SyncFocusTarget(); // 滑行只接管注视点：保留滚轮尺寸目标（2026-09-12 修复滑行期缩放失灵）
            _panVelocity *= Mathf.Exp(-glideDamping * dt);
            if (_panVelocity.sqrMagnitude < sqrMin) _panVelocity = Vector2.zero;
            ApplyCameraSetup();
        }

        // ==================== 几何计算 ====================

        /// <summary>屏幕点射线与画布平面（z=0）的交点（x→世界X，y→世界Y）。相机位于 -Z 侧朝 +Z 看，交点在相机前方（t&gt;0）即有效</summary>
        private bool TryGetGroundPoint(Vector3 screenPos, out Vector2 planeXY)
        {
            planeXY = default;
            if (_camera == null) return false;
            Ray ray = _camera.ScreenPointToRay(screenPos);
            if (Mathf.Abs(ray.direction.z) < 0.0001f) return false; // 射线近乎平行画布
            float t = -ray.origin.z / ray.direction.z;
            if (t < 0f) return false; // 交点在相机背后
            planeXY = new Vector2(ray.origin.x + ray.direction.x * t, ray.origin.y + ray.direction.y * t);
            return true;
        }

        /// <summary>
        /// 缩放到新尺寸，并保持锚定画布点仍位于指定屏幕点下方。
        /// 正交相机下屏幕偏移与尺寸成线性关系，可直接按比例换算注视点。
        /// 直接落位路径（捏合/外部调用）；滚轮走 SmoothZoomAtScreenPoint 平滑路径。
        /// </summary>
        private void SetSizeAtScreenPoint(Vector3 screenPos, float newSize, Vector2 anchorXY)
        {
            newSize = Mathf.Clamp(newSize, minSize, maxSize);
            if (Mathf.Approximately(newSize, _size)) return;

            float ratio = newSize / _size;
            _focus = ClampFocus(anchorXY - (anchorXY - _focus) * ratio, newSize);
            _size = newSize;
            SyncZoomTargets();
            ApplyCameraSetup();
        }

        /// <summary>
        /// 滚轮平滑缩放：只写目标尺寸与注视点（锚定鼠标画布点），实际值由 SmoothZoomFrame 逐帧逼近——
        /// 消除滚轮瞬跳（2026-09-12，反哺自战斗相机方案）。多格连滚基于目标值累积，不漂移。
        /// </summary>
        private void SmoothZoomAtScreenPoint(float newSize, Vector2 anchorXY)
        {
            newSize = Mathf.Clamp(newSize, minSize, maxSize);
            if (Mathf.Approximately(newSize, _targetSize)) return;

            float ratio = newSize / _targetSize;
            _targetFocus = ClampFocus(anchorXY - (anchorXY - _targetFocus) * ratio, newSize);
            _targetSize = newSize;
        }

        /// <summary>滚轮平滑逼近帧：入场动画期间由动画独占（其直写实际值）；拖拽/捏合/滑行每帧同步目标，无残留在缓</summary>
        private void SmoothZoomFrame()
        {
            if (IsAnimating) return;
            bool sizePending = !Mathf.Approximately(_size, _targetSize);
            bool focusPending = (_focus - _targetFocus).sqrMagnitude > 1e-8f;
            if (!sizePending && !focusPending) return;

            if (zoomSmoothTau <= 0f)
            {
                _size = _targetSize;
                _focus = _targetFocus;
            }
            else
            {
                float t = 1f - Mathf.Exp(-Time.unscaledDeltaTime / zoomSmoothTau);
                _size = Mathf.Lerp(_size, _targetSize, t);
                _focus = Vector2.Lerp(_focus, _targetFocus, t);
                if (Mathf.Abs(_size - _targetSize) < 0.0005f) _size = _targetSize;
                if ((_focus - _targetFocus).sqrMagnitude < 1e-8f) _focus = _targetFocus;
            }
            ApplyCameraSetup();
        }

        /// <summary>
        /// 注视点限制在地图矩形内：按当前正交尺寸算可见半宽/半高，围绕图中心 clamp，
        /// 保证画面四边不露出地图外（地图比可视区小时居中于图中心）
        /// </summary>
        private Vector2 ClampFocus(Vector2 focus, float size)
        {
            float aspect = _camera != null ? _camera.aspect : 16f / 9f;
            float lx = Mathf.Max(0f, _mapHalfW - size * aspect);
            float ly = Mathf.Max(0f, _mapHalfH - size);
            focus.x = Mathf.Clamp(focus.x, _mapCenter.x - lx, _mapCenter.x + lx);
            focus.y = Mathf.Clamp(focus.y, _mapCenter.y - ly, _mapCenter.y + ly);
            return focus;
        }

        // ==================== 点击分发 ====================

        private void DispatchAnchorClick(Vector3 screenPos)
        {
            if (_camera == null) return;
            Ray ray = _camera.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out RaycastHit hit, anchorRayMaxDist)) return;
            var anchor = hit.collider.GetComponentInParent<MapAnchor>();
            if (anchor != null)
                anchor.HandleClick();
        }

        private static bool IsPointerOverUI(int pointerId)
        {
            var es = EventSystem.current;
            return es != null && es.IsPointerOverGameObject(pointerId);
        }

        /// <summary>位置式 UI 命中（手动 RaycastAll）——任意帧对任意屏幕点有效。IsPointerOverGameObject
        /// 对非 Began 帧的触摸不可靠（Unity 已知行为），双指捏合起手时第二指往往已落下数帧，
        /// 手势起手判定必须用位置式。双指起手用它，单击/拖拽起手仍用 pointerId 式（Began 帧可靠）。</summary>
        private static bool IsScreenPointOverUI(Vector2 screenPos)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            var pointerData = new PointerEventData(es) { position = screenPos };
            _uiRaycastBuffer.Clear();
            es.RaycastAll(pointerData, _uiRaycastBuffer);
            return _uiRaycastBuffer.Count > 0;
        }

        // ==================== 入场动画 ====================

        private void PlayEntryAnimation(Vector2 targetFocus, float targetSize, bool fromMaxZoom = false)
        {
            StopEntryAnimation();
            _panVelocity = Vector2.zero; // 入场动画接管相机：区域聚焦凌驾于惯性滑行

            InputLocks.Push(this, InputLockReason.MapEntering);
            _entryCoroutine = StartCoroutine(EntryAnimationCoroutine(targetFocus, targetSize, fromMaxZoom));
        }

        private void StopEntryAnimation()
        {
            if (_entryCoroutine == null) return;
            StopCoroutine(_entryCoroutine);
            _entryCoroutine = null;
            // 动画被中断 — 释放锁（幂等，已释放时为空操作）
            InputLocks.Pop(this, InputLockReason.MapEntering);
        }

        private IEnumerator EntryAnimationCoroutine(Vector2 targetFocus, float targetSize, bool fromMaxZoom)
        {
            // 初次打开：从最大上限远景快→慢落到目标；区域切换：从当前视野/注视点平滑滑移（原神式）
            float startSize = fromMaxZoom ? maxSize : _size;
            Vector2 startFocus = _focus;
            bool glide = !fromMaxZoom;

            float elapsed = 0f;
            while (elapsed < enterDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / enterDuration);
                float curveValue = _easeCurve.Evaluate(t);

                _size = Mathf.Lerp(startSize, targetSize, curveValue);
                _focus = glide ? Vector2.Lerp(startFocus, targetFocus, curveValue) : targetFocus;
                ApplyCameraSetup();
                yield return null;
            }

            _size = targetSize;
            _focus = targetFocus;
            SyncZoomTargets(); // 动画直写实际值：结束同步目标（清掉期间可能残留的滚轮目标）
            ApplyCameraSetup();

            _entryCoroutine = null;
            InputLocks.Pop(this, InputLockReason.MapEntering);
        }
    }
}
