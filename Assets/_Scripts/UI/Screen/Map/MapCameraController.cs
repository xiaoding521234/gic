// MapCameraController.cs - 3D 大地图相机控制器（正交俯视平面版）
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using GIC.Framework;

namespace GIC.UI
{
    /// <summary>
    /// 大地图相机控制器（正交垂直俯视，无 3D 透视效果）。
    /// 地图平铺在 XZ 地面，相机从正上方往下看，观感等同原 UGUI 平面地图：
    /// - 拖拽平移：抓取地面点跟随（鼠标/单指）
    /// - 缩放：滚轮/双指捏合调整正交尺寸（比例式）
    /// - 边界：注视点按当前可见范围限制在地图矩形内（画面不露出地图外）
    /// - 入场动画：尺寸从倍数回落（快进慢出），期间持有 MapEntering 输入锁
    /// - 点击：按下与抬起位移小于阈值视为点击，射线检测 3D 锚点并分发
    /// </summary>
    public class MapCameraController : MonoBehaviour
    {
        private const float 相机固定高度 = 100f;

        [Header("缩放")]
        [SerializeField] private float 最小尺寸 = 5f;
        [SerializeField] private float 最大尺寸 = 45f;
        [Tooltip("每滚轮一格的缩放比例（0.85 = 每格缩小 15%）。乘法缩放保证任意级别下视觉变化一致，到上下限有明确停顿感")]
        [SerializeField, Range(0.5f, 0.99f)] private float 滚轮缩放步进 = 0.85f;

        [Header("区域聚焦")]
        [SerializeField] private float 默认视野尺寸 = 8f;
        [SerializeField] private float 入场时长 = 0.2f;
        [SerializeField] private float 入场起始倍数 = 1.35f;

        [Header("点击判定")]
        [SerializeField] private float 点击位移阈值 = 12f;
        [SerializeField] private float 锚点射线最大距离 = 500f;

        private Camera _camera;
        private float _mapHalfW = 107.5f;   // 地图半宽（世界单位，X 方向）
        private float _mapHalfH = 69f;      // 地图半高（世界单位，Z 方向）

        // 相机状态：地面注视点（x→世界X，y→世界Z）+ 正交尺寸（垂直半高，世界单位）
        private Vector2 _focus;
        private float _size = 8f;

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

        /// <summary>入场动画是否正在播放</summary>
        public bool IsAnimating => _entryCoroutine != null;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            ResetEaseCurve();
            ApplyCameraSetup();
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
            _size = Mathf.Clamp(_size, 最小尺寸, 最大尺寸); // 硬 clamp：任何来源的尺寸都不越界
            _camera.orthographicSize = _size;
            // 正交俯视：绕 X 正 90° = 垂直向下看（Unity 旋转约定，勿写负值）
            _camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _camera.transform.position = new Vector3(_focus.x, 相机固定高度, _focus.y);
        }

        // ==================== 公共接口 ====================

        /// <summary>初始化地图边界（世界单位），限制相机注视点范围</summary>
        public void InitBounds(float mapWidth, float mapHeight)
        {
            _mapHalfW = mapWidth * 0.5f;
            _mapHalfH = mapHeight * 0.5f;
        }

        /// <summary>
        /// 聚焦区域：以世界坐标点为中心播放入场动画（尺寸从倍数回落）。
        /// viewSize &lt;= 0 时使用默认视野尺寸。
        /// </summary>
        public void FocusRegion(Vector2 focusXZ, float viewSize = 0f)
        {
            float targetSize = viewSize > 0f ? Mathf.Clamp(viewSize, 最小尺寸, 最大尺寸) : 默认视野尺寸;
            PlayEntryAnimation(ClampFocus(focusXZ, targetSize), targetSize);
        }

        // ==================== 每帧交互 ====================

        private void Update()
        {
            if (InputLocks.IsLocked)
            {
                // 锁期间中断进行中的手势（幂等，下一帧手势重新判定）
                _dragging = false;
                _pinching = false;
                return;
            }

            if (Input.touchSupported && Input.touchCount > 0)
                HandleTouch();
            else
                HandleMouse();
        }

        private void HandleMouse()
        {
            // 滚轮缩放（以鼠标地面点为锚；乘法缩放）
            if (Input.mouseScrollDelta.y != 0f && TryGetGroundPoint(Input.mousePosition, out Vector2 anchor))
                SetSizeAtScreenPoint(Input.mousePosition, _size * Mathf.Pow(滚轮缩放步进, -Input.mouseScrollDelta.y), anchor);

            if (Input.GetMouseButtonDown(0) && !IsPointerOverUI(-1))
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
                {
                    _focus = ClampFocus(_focus + (_grabGround - current), _size);
                    ApplyCameraSetup();
                }
            }
            else if (_dragging && Input.GetMouseButtonUp(0))
            {
                _dragging = false;
                Vector2 upPos = Input.mousePosition;
                if (Vector2.Distance(upPos, _pressScreenPos) < 点击位移阈值)
                    DispatchAnchorClick(Input.mousePosition);
            }
        }

        private void HandleTouch()
        {
            if (Input.touchCount == 1)
            {
                _pinching = false;
                Touch t = Input.GetTouch(0);

                if (t.phase == TouchPhase.Began && !IsPointerOverUI(t.fingerId))
                {
                    if (TryGetGroundPoint(t.position, out _grabGround))
                    {
                        _dragging = true;
                        _pressScreenPos = t.position;
                    }
                }
                else if (_dragging && t.phase == TouchPhase.Moved)
                {
                    if (TryGetGroundPoint(t.position, out Vector2 current))
                    {
                        _focus = ClampFocus(_focus + (_grabGround - current), _size);
                        ApplyCameraSetup();
                    }
                }
                else if (_dragging && (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled))
                {
                    _dragging = false;
                    if (Vector2.Distance(t.position, _pressScreenPos) < 点击位移阈值)
                        DispatchAnchorClick(t.position);
                }
            }
            else if (Input.touchCount == 2)
            {
                _dragging = false;
                Touch t0 = Input.GetTouch(0);
                Touch t1 = Input.GetTouch(1);

                if (!_pinching)
                {
                    _pinching = true;
                    _pinchStartDist = Vector2.Distance(t0.position, t1.position);
                    _pinchStartSize = _size;
                }

                if (_pinchStartDist > 1f)
                {
                    float dist = Vector2.Distance(t0.position, t1.position);
                    if (dist > 1f && TryGetGroundPoint((t0.position + t1.position) * 0.5f, out Vector2 mid))
                    {
                        // 比例式捏合：两指张合比例直接映射为尺寸比例
                        float newSize = _pinchStartSize * (_pinchStartDist / dist);
                        SetSizeAtScreenPoint((t0.position + t1.position) * 0.5f, newSize, mid);
                    }
                }

                if (t0.phase == TouchPhase.Ended || t0.phase == TouchPhase.Canceled ||
                    t1.phase == TouchPhase.Ended || t1.phase == TouchPhase.Canceled)
                    _pinching = false;
            }
        }

        // ==================== 几何计算 ====================

        /// <summary>屏幕点射线与地面（y=0）的交点（x→世界X，y→世界Z）。正交相机射线方向为垂直向下</summary>
        private bool TryGetGroundPoint(Vector3 screenPos, out Vector2 groundXZ)
        {
            groundXZ = default;
            if (_camera == null) return false;
            Ray ray = _camera.ScreenPointToRay(screenPos);
            if (ray.direction.y >= -0.0001f) return false;
            float t = -ray.origin.y / ray.direction.y;
            if (t < 0f) return false;
            groundXZ = new Vector2(ray.origin.x + ray.direction.x * t, ray.origin.z + ray.direction.z * t);
            return true;
        }

        /// <summary>
        /// 缩放到新尺寸，并保持锚定地面点仍位于指定屏幕点下方。
        /// 正交相机下屏幕偏移与尺寸成线性关系，可直接按比例换算注视点。
        /// </summary>
        private void SetSizeAtScreenPoint(Vector3 screenPos, float newSize, Vector2 anchorXZ)
        {
            newSize = Mathf.Clamp(newSize, 最小尺寸, 最大尺寸);
            if (Mathf.Approximately(newSize, _size)) return;

            float ratio = newSize / _size;
            _focus = ClampFocus(anchorXZ - (anchorXZ - _focus) * ratio, newSize);
            _size = newSize;
            ApplyCameraSetup();
        }

        /// <summary>
        /// 注视点限制在地图矩形内：按当前正交尺寸算可见半宽/半高，
        /// 保证画面四边不露出地图外（地图比可视区小时居中）
        /// </summary>
        private Vector2 ClampFocus(Vector2 focus, float size)
        {
            float aspect = _camera != null ? _camera.aspect : 16f / 9f;
            float lx = Mathf.Max(0f, _mapHalfW - size * aspect);
            float lz = Mathf.Max(0f, _mapHalfH - size);
            focus.x = Mathf.Clamp(focus.x, -lx, lx);
            focus.y = Mathf.Clamp(focus.y, -lz, lz);
            return focus;
        }

        // ==================== 点击分发 ====================

        private void DispatchAnchorClick(Vector3 screenPos)
        {
            if (_camera == null) return;
            Ray ray = _camera.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out RaycastHit hit, 锚点射线最大距离)) return;
            var anchor = hit.collider.GetComponentInParent<MapAnchor>();
            if (anchor != null)
                anchor.HandleClick();
        }

        private static bool IsPointerOverUI(int pointerId)
        {
            var es = EventSystem.current;
            return es != null && es.IsPointerOverGameObject(pointerId);
        }

        // ==================== 入场动画 ====================

        private void PlayEntryAnimation(Vector2 targetFocus, float targetSize)
        {
            StopEntryAnimation();

            InputLocks.Push(this, InputLockReason.MapEntering);
            _entryCoroutine = StartCoroutine(EntryAnimationCoroutine(targetFocus, targetSize));
        }

        private void StopEntryAnimation()
        {
            if (_entryCoroutine == null) return;
            StopCoroutine(_entryCoroutine);
            _entryCoroutine = null;
            // 动画被中断 — 释放锁（幂等，已释放时为空操作）
            InputLocks.Pop(this, InputLockReason.MapEntering);
        }

        private IEnumerator EntryAnimationCoroutine(Vector2 targetFocus, float targetSize)
        {
            float startSize = Mathf.Min(targetSize * 入场起始倍数, 最大尺寸);

            float elapsed = 0f;
            while (elapsed < 入场时长)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / 入场时长);
                float curveValue = _easeCurve.Evaluate(t);

                _size = Mathf.Lerp(startSize, targetSize, curveValue);
                _focus = targetFocus;
                ApplyCameraSetup();
                yield return null;
            }

            _size = targetSize;
            _focus = targetFocus;
            ApplyCameraSetup();

            _entryCoroutine = null;
            InputLocks.Pop(this, InputLockReason.MapEntering);
        }
    }
}
