// MapAnchor.cs - 3D 大地图锚点（世界空间面片 + 射线点击，替代原 UGUI Button 版）
using UnityEngine;
using static GIC.Data.PositionConfig;
using GIC.Framework;
using GIC.Data;
using GIC.Battle;
using GIC.Tool;
namespace GIC.UI
{


    /// <summary>
    /// 3D 大地图锚点：SpriteRenderer 直立面片 + BoxCollider，
    /// 点击由 MapCameraController 射线检测后分发到 HandleClick()。
    /// 图标面片在 RefreshVisual 中按相机俯角倾斜并抬离地面。
    /// </summary>
    public class MapAnchor : MonoBehaviour
    {
        [Autowired] private PositionManager _positionManager;

        [Header("锚点配置")]
        [SerializeField] private PositionName positionName;

        [Header("显示状态")]
        [SerializeField] private Color 解锁颜色 = Color.white;
        [SerializeField] private Color 未解锁颜色 = new Color(0.5f, 0.5f, 0.5f, 1f);
        [Tooltip("图标面片中心离地高度（世界单位）")]
        [SerializeField] private float 悬浮高度 = 0.7f;

        private SpriteRenderer _icon;
        private PositionData cachedData;
        private MapScreen _mapScreen;
        private bool _interactable;

        public PositionName PositionName => positionName;

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
        }

        /// <summary>
        /// 摆放图标面片（正交俯视相机：平铺在地图平面上方，与 MapPlane 同旋转）。
        /// 在锚点被摆到地面点之后由 MapScreen 调用。
        /// </summary>
        public void RefreshVisual()
        {
            if (_icon == null) return;
            _icon.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _icon.transform.localPosition = new Vector3(0f, 悬浮高度, 0f);
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
