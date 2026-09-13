// ==================== BackgroundParallaxUI.cs（Canvas 版背景视差——大厅 BackgroundParallax3D 的 UI 同义实现） ====================
// 挂在大图 Image 上：随鼠标/触摸反向缓动，营造景深。公式与参数逐项对齐 BackgroundParallax3D
//（强度 1 / 平滑 0.15 / 边界钳制；鼠标右移→背景左移）。
// 前提：图必须大于根画布（maxOffset=半差值），否则行程为零=组件安全空转。
// 双层纪律：本组件每帧写 anchoredPosition——若宿主图同时在 GlassPanelAnimator 的滑动组里，
// 两个写入者会互写打架；正确结构=外层 wrapper（动画器滑动）包本图（视差），参考 Coop 列表页 Background。
using UnityEngine;
using GIC.Framework;
using GIC.Battle;
using GIC.Data;
using GIC.Data.Event;
using GIC.Tool;
namespace GIC.UI
{


    public class BackgroundParallaxUI : MonoBehaviour
    {
        [Header("视差效果")]
        [Range(0f, 1f)] public float 视差强度 = 1f;
        [Range(0.01f, 0.2f)] public float 平滑时间 = 0.15f;

        [Header("边界控制")]
        public bool 边界约束 = true;

        private RectTransform _rt;
        private Canvas _canvas;
        private Vector2 _startPos;          // 视差基准位（Awake 幂等缓存，勿二次缓存——§39 同族纪律）
        private Vector2 _currentOffset;
        private Vector2 _smoothVelocity;
        private Vector2 _maxOffset;         // 图超出根画布的半差值=视差行程
        private float _lastAspect;

        private void Awake()
        {
            _rt = (RectTransform)transform;
            _canvas = GetComponentInParent<Canvas>(true)?.rootCanvas;
            _startPos = _rt.anchoredPosition;
            RecalcBounds();
        }

        /// <summary>视差行程：图与根画布的半差值（rect 只取宽高恒安全——空间陷阱 docs/14 §42）</summary>
        private void RecalcBounds()
        {
            if (_canvas == null) return;
            var cRect = (RectTransform)_canvas.transform;
            float cw = cRect.rect.width, ch = cRect.rect.height;
            _maxOffset = new Vector2(
                Mathf.Max(0f, (_rt.sizeDelta.x - cw) * 0.5f),
                Mathf.Max(0f, (_rt.sizeDelta.y - ch) * 0.5f));
            _lastAspect = ch > 0f ? cw / ch : 0f;
        }

        private void Update()
        {
            if (_canvas == null) return;

            // 分辨率/纵横比变化时重算行程（同 BackgroundParallax3D）
            var cRect = (RectTransform)_canvas.transform;
            float aspect = cRect.rect.height > 0f ? cRect.rect.width / cRect.rect.height : 0f;
            if (!Mathf.Approximately(aspect, _lastAspect)) RecalcBounds();

            if (_maxOffset.x <= 0f && _maxOffset.y <= 0f) return;

            var target = ComputeTargetOffset(GetParallaxInput());
            _currentOffset = Vector2.SmoothDamp(_currentOffset, target, ref _smoothVelocity, 平滑时间);
            _rt.anchoredPosition = _startPos + _currentOffset;
        }

        /// <summary>归一化输入：屏幕中心=(0,0)，边缘=±1，鼠标反向（右移→输入 x 为负→背景左移）</summary>
        internal Vector2 GetParallaxInput()
        {
            if (Input.touchCount > 0)
            {
                var touch = Input.GetTouch(0);
                return new Vector2(
                    (touch.position.x / Screen.width - 0.5f) * -2f,
                    (touch.position.y / Screen.height - 0.5f) * -2f);
            }
            return new Vector2(
                (Input.mousePosition.x / Screen.width - 0.5f) * -2f,
                (Input.mousePosition.y / Screen.height - 0.5f) * -2f);
        }

        /// <summary>目标偏移=行程×输入×强度（可钳制）</summary>
        internal Vector2 ComputeTargetOffset(Vector2 input)
        {
            var target = Vector2.Scale(_maxOffset, input) * 视差强度;
            if (边界约束)
                target = new Vector2(
                    Mathf.Clamp(target.x, -_maxOffset.x, _maxOffset.x),
                    Mathf.Clamp(target.y, -_maxOffset.y, _maxOffset.y));
            return target;
        }
    }
}
