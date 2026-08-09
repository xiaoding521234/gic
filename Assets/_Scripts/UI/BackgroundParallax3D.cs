using UnityEngine;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
using GIC.Tool;
namespace GIC.UI
{


    /// <summary>
    /// 3D 背景视差效果，跟随鼠标移动 SpriteRenderer。
    /// 替代 Canvas 版的 BackgroundParallax。
    /// </summary>
    public class BackgroundParallax3D : MonoBehaviour
    {
        [Header("视差效果")]
        [Range(0f, 1f)] public float parallaxIntensity = 1f;
        [Range(0.01f, 0.2f)] public float smoothTime = 0.15f;

        [Header("边界控制")]
        public bool clampToBounds = true;

        [Header("铺满屏幕")]
        [Tooltip("额外边距比例(0.15=15%)，为视差移动预留空间")]
        [SerializeField] private float parallaxMargin = 0.15f;

        private SpriteRenderer _sr;
        private Camera _camera;
        private Vector3 _startPos;
        private Vector3 _currentOffset;
        private Vector3 _targetOffset;
        private Vector3 _smoothVelocity;
        private float _maxOffsetX;
        private float _maxOffsetY;
        private float _lastAspect;
        private Sprite _lastSprite;

        private void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
            _camera = Camera.main;
            _startPos = transform.position;
            FitToScreen();
            CalculateBounds();
        }

        /// <summary>
        /// 根据相机宽高比和当前 Sprite 尺寸，动态缩放背景使其始终覆盖屏幕。
        /// </summary>
        private void FitToScreen()
        {
            if (_camera == null || !_camera.orthographic || _sr == null || _sr.sprite == null) return;

            float camHeight = _camera.orthographicSize * 2f;
            float camWidth = camHeight * _camera.aspect;

            float spriteWidth = _sr.sprite.bounds.size.x;
            float spriteHeight = _sr.sprite.bounds.size.y;

            // 铺满屏幕所需的最小缩放（取宽高中较大的比例）
            float coverScale = Mathf.Max(camWidth / spriteWidth, camHeight / spriteHeight);

            // 加上视差边距
            float finalScale = coverScale * (1f + parallaxMargin);
            transform.localScale = new Vector3(finalScale, finalScale, 1f);

            _lastAspect = _camera.aspect;
            _lastSprite = _sr.sprite;
        }

        private void CalculateBounds()
        {
            if (_camera == null || !_camera.orthographic) return;

            float camHeight = _camera.orthographicSize * 2f;
            float camWidth = camHeight * _camera.aspect;

            // 背景实际尺寸（缩放后）
            float bgWidth = _sr.bounds.size.x;
            float bgHeight = _sr.bounds.size.y;

            _maxOffsetX = Mathf.Max(0, (bgWidth - camWidth) / 2f);
            _maxOffsetY = Mathf.Max(0, (bgHeight - camHeight) / 2f);
        }

        private void Update()
        {
            // 分辨率或 Sprite 变化时重新计算缩放
            if (_camera != null && (_camera.aspect != _lastAspect || _sr.sprite != _lastSprite))
            {
                FitToScreen();
                CalculateBounds();
            }

            if (_maxOffsetX <= 0 && _maxOffsetY <= 0) return;

            Vector2 input = Vector2.zero;
            if (Input.touchCount > 0)
            {
                var touch = Input.GetTouch(0);
                input = new Vector2(
                    (touch.position.x / Screen.width - 0.5f) * -2f,
                    (touch.position.y / Screen.height - 0.5f) * -2f
                );
            }
            else
            {
                input = new Vector2(
                    (Input.mousePosition.x / Screen.width - 0.5f) * -2f,
                    (Input.mousePosition.y / Screen.height - 0.5f) * -2f
                );
            }

            float targetX = _maxOffsetX * input.x * parallaxIntensity;
            float targetY = _maxOffsetY * input.y * parallaxIntensity;

            if (clampToBounds)
            {
                targetX = Mathf.Clamp(targetX, -_maxOffsetX, _maxOffsetX);
                targetY = Mathf.Clamp(targetY, -_maxOffsetY, _maxOffsetY);
            }

            _targetOffset = new Vector3(targetX, targetY, 0);
            _currentOffset = Vector3.SmoothDamp(_currentOffset, _targetOffset, ref _smoothVelocity, smoothTime);
            transform.position = _startPos + _currentOffset;
        }
    }

}

