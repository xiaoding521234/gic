// MapAnchor.cs - 3D 大地图锚点（世界空间面片 + 射线点击，替代原 UGUI Button 版）
using UnityEngine;
using static GIC.Data.PositionConfig;
using GIC.Framework;
using GIC.Data;
namespace GIC.UI
{


    /// <summary>
    /// 3D 大地图锚点：SpriteRenderer 直立面片 + BoxCollider，
    /// 点击由 MapCameraController 射线检测后分发到 HandleClick()。
    /// 图标面片在 RefreshVisual 中直立面向相机，沿画布法线抬离地图。
    /// </summary>
    public class MapAnchor : MonoBehaviour
    {
        [Autowired] private PositionManager _positionManager;

        [Header("锚点配置")]
        [SerializeField] private PositionName positionName;

        [Header("显示状态")]
        [SerializeField] private Color 解锁颜色 = Color.white;
        [SerializeField] private Color 未解锁颜色 = new Color(0.5f, 0.5f, 0.5f, 1f);
        [Tooltip("图标面片沿画布法线抬离地图的偏移（世界单位，-Z 靠相机侧）")]
        [SerializeField] private float 悬浮偏移 = 0.7f;

        private SpriteRenderer _icon;
        private PositionData cachedData;
        private MapScreen _mapScreen;
        private bool _interactable;
        private Vector3 _baseScale;

        public PositionName PositionName => positionName;

        /// <summary>基准缩放（Awake 时取 prefab 根缩放；视觉大小直接改 prefab 根缩放即可）</summary>
        public Vector3 BaseScale => _baseScale;

        /// <summary>
        /// 应用相机比例缩放（恒定视觉尺寸）：根缩放 = prefab 基准 × 相机视野比例。
        /// 由 MapScreen 在相机尺寸变化时调用。
        /// </summary>
        public void ApplyCameraScale(float scale)
        {
            transform.localScale = _baseScale * scale;
        }

        public void SetPositionName(PositionName name)
        {
            positionName = name;
        }

        public void SetMapScreen(MapScreen screen)
        {
            _mapScreen = screen;
        }

        private void Awake()
        {
            _icon = GetComponentInChildren<SpriteRenderer>();
            // Instantiate 时 Awake 先于任何外部改动执行，此时即 prefab 原始根缩放
            _baseScale = transform.localScale;
        }

        /// <summary>
        /// 摆放图标面片（垂直画布：icon 直立即面向相机，与 MapPlane 同朝向）。
        /// 在锚点被摆到画布点之后由 MapScreen 调用。
        /// </summary>
        public void RefreshVisual()
        {
            if (_icon == null) return;
            _icon.transform.localRotation = Quaternion.identity;
            // 悬浮偏移：向画布外侧（-Z，靠相机侧）抬离，避免与地图穿插
            _icon.transform.localPosition = new Vector3(0f, 0f, -悬浮偏移);
        }

        /// <summary>按解锁状态刷新颜色与可点击标记</summary>
        public void SetData(PositionData data)
        {
            cachedData = data;
            _interactable = data.isUnlocked;

            // 图标变灰，但不隐藏
            if (_icon != null)
                _icon.color = data.isUnlocked ? 解锁颜色 : 未解锁颜色;
        }

        /// <summary>点击处理（由 MapCameraController 分发；含锁定与未配置守卫）</summary>
        public void HandleClick()
        {
            if (cachedData == null)
            {
                GICLog.Info($"未配置: {positionName}");
                return;
            }

            if (!_interactable)
            {
                GICLog.Info($"未解锁: {positionName}");
                return;
            }

            GICLog.Info($"移动到: {positionName}");
            Wargame.Instance.Context.Inject(this);

            // 切换位置（触发 OnPositionChangedEvent，大厅开始预加载新背景），
            // 然后通过 MapScreen 淡出后 GoBack
            _positionManager.MoveToPosition(positionName);
            _mapScreen?.CloseWithFade();
        }
    }
}
