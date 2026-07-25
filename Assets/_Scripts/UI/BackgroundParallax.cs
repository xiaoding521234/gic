using UnityEngine;
using UnityEngine.UI;

public class BackgroundParallax : MonoBehaviour
{
    [Header("背景设置")]
    [SerializeField] private Image backgroundImage;
    [Range(0.05f, 0.5f)]
    [SerializeField] private float extraSize = 0.2f; // 额外大小（20%）

    [Header("视差效果")]
    [Range(0f, 1f)]
    [SerializeField] private float parallaxIntensity = 1f; // 移动强度
    [Range(0.01f, 0.1f)]
    [SerializeField] private float smoothSpeed = 0.04f; // 平滑移动速度

    [Header("边界控制")]
    [SerializeField] private bool clampToBounds = true; // 限制边界
    [SerializeField] private bool autoUpdateOnResolutionChange = true; // 分辨率改变时自动更新

    private RectTransform _rectTransform;
    private Canvas _canvas;
    private RectTransform _canvasRectTransform;
    private Vector2 _currentOffset;
    private Vector2 _targetOffset;
    private Vector2 _originalSize;
    private float _maxOffsetX;
    private float _maxOffsetY;
    private float _spriteOriginalWidth;
    private float _spriteOriginalHeight;
    private float _spriteAspect;
    private Vector2 _lastCanvasSize;

    private void Awake()
    {
        // 获取背景Image组件
        if (backgroundImage == null)
            backgroundImage = GetComponent<Image>();

        if (backgroundImage == null)
        {
            Debug.LogError("BackgroundParallax: 未找到Image组件！");
            return;
        }

        // 获取RectTransform
        _rectTransform = backgroundImage.GetComponent<RectTransform>();

        // 获取父级Canvas
        _canvas = GetComponentInParent<Canvas>();
        if (_canvas == null)
        {
            Debug.LogError("BackgroundParallax: 未找到父级Canvas组件！");
            return;
        }

        _canvasRectTransform = _canvas.GetComponent<RectTransform>();

        // 获取原始图片尺寸
        if (backgroundImage.sprite != null)
        {
            _spriteOriginalWidth = backgroundImage.sprite.rect.width;
            _spriteOriginalHeight = backgroundImage.sprite.rect.height;
            _spriteAspect = _spriteOriginalWidth / _spriteOriginalHeight;
        }
        else
        {
            Debug.LogError("BackgroundParallax: 背景图片未设置！");
        }
    }

    private void Start()
    {
        SetupBackground();
        RecordCanvasSize();
    }

    private void Update()
    {
        // 检查Canvas尺寸是否改变（分辨率或缩放变化）
        if (autoUpdateOnResolutionChange && HasCanvasSizeChanged())
        {
            SetupBackground();
            ResetPosition();
            RecordCanvasSize();
        }

        // 没有移动空间时跳过视差效果
        if (_maxOffsetX <= 0 && _maxOffsetY <= 0) return;

        CalculateTargetOffset();

        // 平滑移动
        _currentOffset = Vector2.Lerp(_currentOffset, _targetOffset, smoothSpeed);

        // 应用偏移
        _rectTransform.anchoredPosition = _currentOffset;
    }

    /// <summary>
    /// 设置背景尺寸
    /// </summary>
    private void SetupBackground()
    {
        if (backgroundImage.sprite == null)
        {
            Debug.LogError("背景图片未设置！");
            return;
        }

        if (_canvasRectTransform == null)
        {
            Debug.LogError("Canvas RectTransform未找到！");
            return;
        }

        // 获取Canvas的实际尺寸（玩家实际看到的区域）
        Vector2 canvasSize = _canvasRectTransform.rect.size;

        // 处理Canvas尺寸为0的情况（可能尚未初始化）
        if (canvasSize.x <= 0 || canvasSize.y <= 0)
        {
            Debug.LogWarning($"Canvas尺寸异常: {canvasSize.x}x{canvasSize.y}，等待下一帧更新");
            return;
        }

        // 目标尺寸 = Canvas尺寸 + 额外边距
        float targetWidth = canvasSize.x * (1 + extraSize);
        float targetHeight = canvasSize.y * (1 + extraSize);

        float scale;



        // 保持原始比例，计算缩放使图片至少覆盖目标区域
        float scaleByWidth = targetWidth / _spriteOriginalWidth;
        float scaleByHeight = targetHeight / _spriteOriginalHeight;

        // 取最大值确保完全覆盖
        scale = Mathf.Max(scaleByWidth, scaleByHeight);

        // 计算最终尺寸
        _originalSize = new Vector2(_spriteOriginalWidth * scale, _spriteOriginalHeight * scale);



        // 应用尺寸
        _rectTransform.sizeDelta = _originalSize;

        // 计算最大偏移量（可移动范围）
        // 背景比Canvas大多少，就能移动多少
        _maxOffsetX = (_originalSize.x - canvasSize.x) / 2f;
        _maxOffsetY = (_originalSize.y - canvasSize.y) / 2f;

        // 确保偏移量不为负数
        _maxOffsetX = Mathf.Max(0, _maxOffsetX);
        _maxOffsetY = Mathf.Max(0, _maxOffsetY);

        // 输出调试信息
        //PrintDebugInfo(canvasSize, targetWidth, targetHeight, scale);

        // 验证覆盖情况
        ValidateCoverage(canvasSize);
    }

    /// <summary>
    /// 检查Canvas尺寸是否改变
    /// </summary>
    private bool HasCanvasSizeChanged()
    {
        if (_canvasRectTransform == null) return false;

        Vector2 currentSize = _canvasRectTransform.rect.size;
        bool hasChanged = Mathf.Abs(currentSize.x - _lastCanvasSize.x) > 0.1f ||
                         Mathf.Abs(currentSize.y - _lastCanvasSize.y) > 0.1f;

        return hasChanged;
    }

    /// <summary>
    /// 记录当前Canvas尺寸
    /// </summary>
    private void RecordCanvasSize()
    {
        if (_canvasRectTransform != null)
        {
            _lastCanvasSize = _canvasRectTransform.rect.size;
        }
    }

    /// <summary>
    /// 计算目标偏移位置（基于鼠标/触摸位置）
    /// </summary>
    private void CalculateTargetOffset()
    {
        // 获取输入位置（-1 到 1范围）
        Vector2 input = GetInputPosition();

        // 应用强度计算目标偏移
        float targetX = _maxOffsetX * input.x * parallaxIntensity;
        float targetY = _maxOffsetY * input.y * parallaxIntensity;

        // 限制边界
        if (clampToBounds)
        {
            targetX = Mathf.Clamp(targetX, -_maxOffsetX, _maxOffsetX);
            targetY = Mathf.Clamp(targetY, -_maxOffsetY, _maxOffsetY);
        }

        _targetOffset = new Vector2(targetX, targetY);
    }

    /// <summary>
    /// 获取输入位置（支持鼠标和触摸）
    /// </summary>
    private Vector2 GetInputPosition()
    {
        Vector2 input = Vector2.zero;

        // 支持触摸输入
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            input = new Vector2(
                (touch.position.x / Screen.width - 0.5f) * -2f,
                (touch.position.y / Screen.height - 0.5f) * -2f
            );
        }
        // 支持鼠标输入
        else
        {
            input = new Vector2(
                (Input.mousePosition.x / Screen.width - 0.5f) * -2f,
                (Input.mousePosition.y / Screen.height - 0.5f) * -2f
            );
        }

        return input;
    }

    /// <summary>
    /// 打印调试信息
    /// </summary>
    private void PrintDebugInfo(Vector2 canvasSize, float targetWidth, float targetHeight, float scale)
    {
        Debug.Log($"=== 背景设置详情 ===");
        Debug.Log($"Canvas实际尺寸: {canvasSize.x:F0}x{canvasSize.y:F0}");
        Debug.Log($"目标最小尺寸: {targetWidth:F0}x{targetHeight:F0}");
        Debug.Log($"原始图片尺寸: {_spriteOriginalWidth:F0}x{_spriteOriginalHeight:F0} (比例: {_spriteAspect:F2})");
        Debug.Log($"缩放比例: {scale:F3}");
        Debug.Log($"最终背景尺寸: {_originalSize.x:F0}x{_originalSize.y:F0}");
        Debug.Log($"可移动范围: X:{_maxOffsetX:F0} Y:{_maxOffsetY:F0}");

        // 显示Canvas缩放信息
        if (_canvas != null)
        {
            Debug.Log($"Canvas模式: {_canvas.renderMode}");
            if (_canvas.renderMode == RenderMode.ScreenSpaceCamera && _canvas.worldCamera != null)
            {
                Debug.Log($"关联相机: {_canvas.worldCamera.name}");
            }

            CanvasScaler scaler = _canvas.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                Debug.Log($"Canvas缩放模式: {scaler.uiScaleMode}");
                if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
                {
                    Debug.Log($"参考分辨率: {scaler.referenceResolution.x}x{scaler.referenceResolution.y}");
                }
            }
        }
    }

    /// <summary>
    /// 验证背景是否正确覆盖Canvas
    /// </summary>
    private void ValidateCoverage(Vector2 canvasSize)
    {
        bool widthCovered = _originalSize.x >= canvasSize.x;
        bool heightCovered = _originalSize.y >= canvasSize.y;

    }

    /// <summary>
    /// 重置背景位置到中心
    /// </summary>
    public void ResetPosition()
    {
        _targetOffset = Vector2.zero;
        _currentOffset = Vector2.zero;
        _rectTransform.anchoredPosition = Vector2.zero;
    }

    /// <summary>
    /// 设置额外大小（运行时动态调整）
    /// </summary>
    public void SetExtraSize(float newSize)
    {
        extraSize = Mathf.Clamp(newSize, 0.05f, 0.5f);
        SetupBackground();
        ResetPosition();
    }

    /// <summary>
    /// 设置视差强度
    /// </summary>
    public void SetParallaxIntensity(float intensity)
    {
        parallaxIntensity = Mathf.Clamp01(intensity);
    }

    /// <summary>
    /// 手动刷新背景设置（当Canvas尺寸改变时调用）
    /// </summary>
    public void RefreshBackground()
    {
        SetupBackground();
        ResetPosition();
    }

    /// <summary>
    /// 获取当前背景尺寸
    /// </summary>
    public Vector2 GetBackgroundSize()
    {
        return _originalSize;
    }

    /// <summary>
    /// 获取当前Canvas尺寸
    /// </summary>
    public Vector2 GetCanvasSize()
    {
        return _canvasRectTransform != null ? _canvasRectTransform.rect.size : Vector2.zero;
    }

    /// <summary>
    /// 当RectTransform尺寸改变时自动更新（处理窗口大小变化）
    /// </summary>
    private void OnRectTransformDimensionsChange()
    {
        if (gameObject.activeInHierarchy && isActiveAndEnabled && autoUpdateOnResolutionChange)
        {
            // 延迟一帧执行，避免在布局阶段重复计算
            Invoke(nameof(DelayedRefresh), 0.02f);
        }
    }

    /// <summary>
    /// 延迟刷新
    /// </summary>
    private void DelayedRefresh()
    {
        if (this != null && gameObject.activeInHierarchy)
        {
            RefreshBackground();
        }
    }

    /// <summary>
    /// 编辑器调试可视化（仅在编辑器中生效）
    /// </summary>
    private void OnDrawGizmos()
    {
#if UNITY_EDITOR
        if (Application.isPlaying && _rectTransform != null && _canvasRectTransform != null)
        {
            // 绘制Canvas边界（绿色）
            Gizmos.color = Color.green;
            Vector3[] canvasCorners = new Vector3[4];
            _canvasRectTransform.GetWorldCorners(canvasCorners);
            for (int i = 0; i < 4; i++)
            {
                Gizmos.DrawLine(canvasCorners[i], canvasCorners[(i + 1) % 4]);
            }

            // 绘制背景边界（红色）
            Gizmos.color = Color.red;
            Vector3[] bgCorners = new Vector3[4];
            _rectTransform.GetWorldCorners(bgCorners);
            for (int i = 0; i < 4; i++)
            {
                Gizmos.DrawLine(bgCorners[i], bgCorners[(i + 1) % 4]);
            }

            // 在中心绘制文字标记
            //UnityEditor.Handles.Label(_rectTransform.position, $"背景: {_originalSize.x:F0}x{_originalSize.y:F0}");
            //UnityEditor.Handles.Label(_canvasRectTransform.position, $"Canvas: {_canvasRectTransform.rect.size.x:F0}x{_canvasRectTransform.rect.size.y:F0}");
        }
#endif
    }
}