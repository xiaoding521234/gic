using System;
using UnityEngine;
using UnityEngine.EventSystems;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Tool;
namespace GIC.Battle
{


    /// <summary>
    /// 战场 RTS 相机控制器（2026-09-12 用户拍板：缩放+平移像 RTS 游戏）。
    /// 透视相机俯角 55° 固定，状态 = 棋盘注视点（XZ）+ 视轴距离：
    /// - 滚轮缩放：以屏幕中心缩放（乘法步进，近远双限；平滑缓动逼近而非瞬跳；不随鼠标锚点——2026-09-12 用户拍板）
    /// - WASD/方向键平移（速度随距离缩放，任意缩放级别手感一致）
    /// - 左键拖拽平移（抓取棋盘点跟随；右键留给退出弹窗，2026-09-12 用户拍板弃中键用左键）
    /// - 输入锁期间冻结（弹窗/场景切换），UI 上滚轮与拖拽不抢占
    /// 手感参考 MapScreen 的 MapCameraController（乘法缩放/锚点缩放/边界 clamp）。
    /// </summary>
    public class BattleCameraController : MonoBehaviour
    {
        [Header("俯角（度，固定）")]
        [SerializeField] private float _pitchDegrees = 55f;

        [Header("缩放")]
        [Tooltip("最近视轴距离")]
        [SerializeField] private float _minDistance = 12f;
        [Tooltip("最远视轴距离")]
        [SerializeField] private float _maxDistance = 60f;
        [Tooltip("每滚轮一格的缩放比例（0.925 = 每格拉近 7.5%；乘法缩放各级别手感一致。2026-09-12 用户反馈 0.85 太灵敏，减半）")]
        [SerializeField, Range(0.5f, 0.99f)] private float _scrollStep = 0.925f;
        [Tooltip("缩放平滑时间常数（秒）：实际距离向目标距离指数逼近，越小跟得越紧（0=瞬移）")]
        [SerializeField, Range(0f, 0.3f)] private float _zoomSmoothTau = 0.06f;

        [Header("平移")]
        [Tooltip("键盘平移速度 = 视轴距离 × 此系数（单位/秒）")]
        [SerializeField] private float _keyPanSpeedFactor = 0.35f;

        [Header("边界")]
        [Tooltip("注视点允许范围（棋盘半宽 10 + 余量，世界单位）")]
        [SerializeField] private float _focusBounds = 12f;

        private Camera _camera;
        private float _distance = 35f;
        private float _targetDistance = 35f;   // 缩放目标距离（滚轮只改它，实际距离逐帧平滑逼近）
        private Vector2 _focus = Vector2.zero;   // 棋盘平面注视点（XZ）
        private bool _dragging;
        private Vector2 _grabPoint;

        /// <summary>当前视轴距离（探针/未来 UI 缩放按钮用）</summary>
        public float CurrentDistance => _distance;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            InitFromTransform();
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
            ApplyTransform();
        }

        private void Update()
        {
            if (InputLocks.IsLocked)
            {
                _dragging = false;
                return;
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

            // 左键拖拽平移（2026-09-12 用户拍板；按在 UI 上不起手，面板/按钮不受影响；
            // B6 落地左键点击交互（选单位/选格）时需加位移阈值区分点击与拖拽，参照 MapCameraController）
            if (Input.GetMouseButtonDown(0) && !IsPointerOverUI())
            {
                if (TryGetBoardPoint(Input.mousePosition, out Vector3 grab))
                {
                    _dragging = true;
                    _grabPoint = new Vector2(grab.x, grab.z);
                }
            }
            else if (_dragging && Input.GetMouseButton(0))
            {
                if (TryGetBoardPoint(Input.mousePosition, out Vector3 current))
                {
                    var current2 = new Vector2(current.x, current.z);
                    _focus = ClampFocus(_focus + (_grabPoint - current2));
                }
            }
            else if (_dragging && Input.GetMouseButtonUp(0))
            {
                _dragging = false;
            }

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

            ApplyTransform();
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

        /// <summary>屏幕点射线与棋盘平面（y=0，地块底面）的交点</summary>
        private bool TryGetBoardPoint(Vector3 screenPos, out Vector3 point)
        {
            point = default;
            if (_camera == null) return false;
            var ray = _camera.ScreenPointToRay(screenPos);
            var plane = new Plane(Vector3.up, Vector3.zero);
            if (!plane.Raycast(ray, out float enter) || enter <= 0f) return false;
            point = ray.GetPoint(enter);
            return true;
        }

        private static bool IsPointerOverUI()
        {
            var es = EventSystem.current;
            return es != null && es.IsPointerOverGameObject();
        }
    }
}
