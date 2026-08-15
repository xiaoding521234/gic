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

        [Header("祈愿按钮")]
        [SerializeField] private Button wish1Button;
        [SerializeField] private Button wish10Button;

        [Header("货币显示")]
        [SerializeField] private TextMeshProUGUI fateCountText;
        [SerializeField] private RectTransform primogemDisplay;
        [SerializeField] private TextMeshProUGUI starglitterCountText;

        private WishManager _wishManager;
        private WishPoolConfig _currentPool;

        // 注入字段（partial 共享，主文件 Awake 注入）
        [Autowired] private SaveManager saveManager;
        [Autowired] private UnitConfig unitConfig;
        [Autowired] private ItemConfig itemConfig;

        /// <summary>
        /// 祈愿部分的初始化（由 WishScreen.Start 调用）
        /// </summary>
        private void InitWishDraw()
        {
            if (saveManager == null) return;

            _wishManager = new WishManager(saveManager, unitConfig, itemConfig);

            if (wish1Button != null)
                wish1Button.onClick.AddListener(() => StartDraw(1));
            if (wish10Button != null)
                wish10Button.onClick.AddListener(() => StartDraw(10));

            if (drawController != null)
                drawController.OnWishComplete += UpdateFateCount;

            UpdateFateCount();
        }

        private void OnDestroy()
        {
            _inputManager?.UnregisterClosable(this);
            // 兜底：释放 WishScreen 及其祈愿流程持有的全部锁
            InputLocks.PopAll(this);

            if (drawController != null)
                drawController.OnWishComplete -= UpdateFateCount;
        }

        private void StartDraw(int count)
        {
            if (_wishManager == null) return;

            // 防重入：抽卡进行中不允许再次触发
            if (drawController != null && drawController.IsWishInProgress) return;

            if (_currentPool == null || _currentPool.units.Count == 0 && _currentPool.items.Count == 0)
            {
                PopupManager.Instance.ShowToast(new UnityEngine.Localization.LocalizedString(TableName.PopupText.ToString(), "Wish_PoolNotAvailable"));
                return;
            }

            if (!_wishManager.CanAfford(count))
            {
                PopupManager.Instance.ShowToast(new UnityEngine.Localization.LocalizedString(TableName.PopupText.ToString(), "Wish_NoPrimogem"));
                return;
            }

            // 取消按钮选中，防止按空格/回车再次触发 onClick
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);

            if (drawController != null)
            {
                drawController.StartWish(_wishManager, _currentPool, count);
            }

            UpdateFateCount();
        }

        private void UpdateFateCount()
        {
            if (fateCountText == null) return;
            var save = saveManager?.CurrentSave;
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

            // 星辉数量
            if (starglitterCountText != null)
            {
                int starglitter = 0;
                foreach (var card in save.ownedNormalItems)
                {
                    if (card.id.AsItemName() == ItemName.Starglitter)
                    {
                        starglitter = card.count;
                        break;
                    }
                }
                starglitterCountText.text = starglitter.ToString();
            }
        }

        /// <summary>
        /// 切换卡池（由角色选择时调用，直接传入该角色绑定的卡池配置）
        /// </summary>
        public void SwitchPool(WishPoolConfig pool)
        {
            _currentPool = pool;
        }
    }
}
