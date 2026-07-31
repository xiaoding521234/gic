using UnityEngine;

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

    private SpriteRenderer _sr;
    private Vector3 _startPos;
    private Vector3 _currentOffset;
    private Vector3 _targetOffset;
    private Vector3 _smoothVelocity;
    private float _maxOffsetX;
    private float _maxOffsetY;

    private void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
        _startPos = transform.position;
        CalculateBounds();
    }

    private void CalculateBounds()
    {
        // 相机可见区域
        var cam = Camera.main;
        if (cam == null || !cam.orthographic) return;

        float camHeight = cam.orthographicSize * 2f;
        float camWidth = camHeight * cam.aspect;

        // 背景实际尺寸
        float bgWidth = _sr.bounds.size.x;
        float bgHeight = _sr.bounds.size.y;

        _maxOffsetX = Mathf.Max(0, (bgWidth - camWidth) / 2f);
        _maxOffsetY = Mathf.Max(0, (bgHeight - camHeight) / 2f);
    }

    private void Update()
    {
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
