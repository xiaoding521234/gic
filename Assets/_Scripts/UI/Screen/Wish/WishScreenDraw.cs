using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GIC.Framework;
using GIC.Data;
using GIC.Battle;
using GIC.Tool;

namespace GIC.UI
{
    /// <summary>
    /// WishScreen 的祈愿抽卡部分
    /// </summary>
    public partial class WishScreen
    {
        [Header("祈愿抽卡")]
        [SerializeField] private WishDrawController drawController;
        [SerializeField] private List<WishPoolConfig> wishPools;
        [SerializeField] private int currentPoolIndex = 0;

        [Header("祈愿按钮")]
        [SerializeField] private Button wish1Button;
        [SerializeField] private Button wish10Button;

        [Header("货币显示")]
        [SerializeField] private TextMeshProUGUI fateCountText;
        [SerializeField] private RectTransform primogemDisplay;

        private WishManager _wishManager;

        /// <summary>
        /// 祈愿部分的初始化（由 WishScreen.Start 调用）
        /// </summary>
        private void InitWishDraw()
        {
            var saveManager = Wargame.Instance?.SaveManager;
            var configManager = Wargame.Instance?.ConfigManager;
            if (saveManager == null || configManager == null) return;

            var unitConfig = configManager.GetUnitConfig();
            var itemConfig = configManager.GetItemConfig();
            _wishManager = new WishManager(saveManager, unitConfig, itemConfig);

            if (wish1Button != null)
                wish1Button.onClick.AddListener(() => StartDraw(1));
            if (wish10Button != null)
                wish10Button.onClick.AddListener(() => StartDraw(10));

            UpdateFateCount();
        }

        private void StartDraw(int count)
        {
            if (_wishManager == null) return;
            if (wishPools == null || wishPools.Count == 0 || currentPoolIndex >= wishPools.Count) return;

            if (!_wishManager.CanAfford(count))
            {
                GameScene.Instance.ShowLocalizedPopup("Wish_NoPrimogem");
                return;
            }

            var pool = wishPools[currentPoolIndex];
            if (drawController != null)
            {
                drawController.StartWish(_wishManager, pool, count);
            }

            UpdateFateCount();
        }

        private void UpdateFateCount()
        {
            if (fateCountText == null) return;
            var save = Wargame.Instance?.SaveManager?.CurrentSave;
            if (save == null) return;

            int primogem = 0;
            foreach (var card in save.ownedNormalItems)
            {
                if (card.id.AsItemName() == ItemName.Primogem)
                {
                    primogem = card.count;
                    break;
                }
            }
            fateCountText.text = primogem.ToString();
        }

        /// <summary>
        /// 切换卡池
        /// </summary>
        public void SwitchPool(int index)
        {
            if (index < 0 || index >= wishPools.Count) return;
            currentPoolIndex = index;
        }
    }
}
