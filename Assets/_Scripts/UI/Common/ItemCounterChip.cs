using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GIC.Data;

namespace GIC.UI
{


    /// <summary>
    /// 物品计数条（公用组件，2026-09-25 B6d 抽自祈愿界面货币显示改造公用化——用户拍板
    /// 「复用祈愿界面的，不是预制体则改造变为公用」）：半透明黑底条+物品图标+数量文本，
    /// HorizontalLayoutGroup+ContentSizeFitter 宽度自适应（结构源自 WishScreen 货币显示区）。
    /// 使用方：祈愿界面货币显示（原地组件化，视觉零漂移）/ 战斗 HUD 左上角摩拉·体力计数
    /// ——体力/摩拉=玩家持有的物品牌（2026-09-25 用户拍板口径：ItemName.Stamina/Mora），
    /// 计数即持牌数。
    /// </summary>
    public class ItemCounterChip : MonoBehaviour
    {
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text countText;

        public void SetIcon(Sprite sprite)
        {
            if (icon == null) return;
            icon.sprite = sprite;
            icon.gameObject.SetActive(sprite != null);
        }

        /// <summary>按物品配置初始化图标（ItemConfig 数据链单源——物品图标不散落 prefab 手塞）</summary>
        public void InitItem(ItemConfig config, ItemName itemId)
        {
            var data = config != null ? config.GetItemData(itemId) : null;
            SetIcon(data != null ? data.GetIcon(0) : null);
        }

        /// <summary>刷新数量（纯数字——祈愿界面现状口径，语言无关）</summary>
        public void SetCount(int count)
        {
            if (countText != null)
                countText.text = count.ToString();
        }
    }
}
