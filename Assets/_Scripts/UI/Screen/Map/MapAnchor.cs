using UnityEngine;
using UnityEngine.UI;
using static GIC.Data.PositionConfig;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
using GIC.Tool;
namespace GIC.UI
{



    public class MapAnchor : MonoBehaviour
    {
        [Header("锚点配置")]
        [SerializeField] private PositionName positionName;
        
        [Header("未解锁效果")]
        [SerializeField] private Color unlockedColor = Color.white;

        [SerializeField] private Color lockedColor = new Color(0.5f, 0.5f, 0.5f, 1);
        
        private Image iconImage;
        private Button button;
        private PositionData cachedData;
        
        public PositionName PositionName => positionName;
        
        private void Awake()
        {
            iconImage = GetComponent<Image>();
            button = GetComponent<Button>();
            
            if (button != null)
                button.onClick.AddListener(OnClick);
        }
        
        public void SetData(PositionData data)
        {
            cachedData = data;
            
            if (button != null)
                button.interactable = data.isUnlocked;
            
            // 图标变灰，但不隐藏
            if (iconImage != null)
            {
                iconImage.color = data.isUnlocked ? unlockedColor : lockedColor;
            }
                
        }
        
        private void OnClick()
        {
            if(cachedData == null)
            {
                Debug.Log($"未配置: {positionName}");
            }
            if (cachedData != null && cachedData.isUnlocked)
            {
                Debug.Log($"移动到: {positionName}");
                Wargame.Instance.PositionManager.MoveToPosition(positionName);
            }
            else
            {
                Debug.Log($"未解锁: {positionName}");
            }
        }
    }
}


