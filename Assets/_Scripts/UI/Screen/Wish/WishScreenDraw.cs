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

        protected override void OnDestroy()
        {
            if (drawController != null)
                drawController.OnWishComplete -= UpdateFateCount;

            // 基类收尾：注销可关闭 + PopAll 输入锁 + 音乐 pop 兜底（UnsubscribeOwner 此前无订阅）
            base.OnDestroy();
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
                // 派蒙对玩家手气的反应（2026-08-31）：玩家手抽轮挂观察者——抽到好卡/连烂时
                // 派蒙有动作+LLM 话语（与派蒙代抽严格区分，观察者内部校验 IsAutoDraw 防串场）
                GIC.Pet.Chat.PetWishPlayerObserver.Observe(drawController, count);
            }

            UpdateFateCount();
        }

        // ==================== AI 自动抽卡入口（PetChatIntent.auto_wish，2026-08-30） ====================

        /// <summary>抽卡是否进行中（含最终展示期——直到玩家点击关掉最终展示才结束）</summary>
        public bool IsWishInProgress => drawController != null && drawController.IsWishInProgress;

        /// <summary>AI 自动抽卡基础就绪：管理器已建+无角色切换进行中（Start 的默认首选完成后即满足）。
        /// 注意卡池可能仍为空——卡池界面首个角色未必绑定卡池（如 Columbina），由 EnsurePoolSelected 补选</summary>
        public bool IsBaseReady => _wishManager != null && !isSwitching;

        /// <summary>确保选中了绑定可用卡池的角色（AI 自动抽卡用，2026-08-30）：当前卡池有效则不动；
        /// 否则选第一个绑定了卡池的角色（与手动点击角色按钮同路径——默认选中的首个角色可能无卡池，
        /// 直接开抽会被卡池空守卫拒绝）。返回 false=没有任何角色绑定卡池。
        /// 切换是异步协程（含面板淡出淡入）——调用后等 IsAutoDrawReady 变 true 再开抽。</summary>
        public bool EnsurePoolSelected()
        {
            if (_currentPool != null) return true;
            for (int i = 0; i < characters.Length; i++)
            {
                if (characters[i] != null && characters[i].pool != null)
                {
                    SelectCharacter(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>AI 自动抽卡就绪：管理器已建 + 卡池已选 + 非切换中（Start 后默认角色选中完成）。
        /// 不含 IsWishInProgress——进行中由 TryStartAutoDraw 判 Busy</summary>
        public bool IsAutoDrawReady => _wishManager != null && _currentPool != null && !isSwitching;

        /// <summary>抽卡控制器（PetWishAutoRunner 订阅 OnShotPlanned/OnWishComplete 用）</summary>
        public WishDrawController DrawController => drawController;

        /// <summary>自动抽卡启动结果</summary>
        public enum AutoDrawStartResult
        {
            Started,        // 已启动（自动射击模式）
            Busy,           // 抽卡进行中（含最终展示期）
            PoolEmpty,      // 当前卡池未开放
            NoPrimogem,     // 原石不足
        }

        /// <summary>AI 自动抽卡入口：与 StartDraw 同守卫，以自动射击模式启动。
        /// 失败原因由调用方（PetWishAutoRunner）转成派蒙反应消息。</summary>
        public AutoDrawStartResult TryStartAutoDraw(int count)
        {
            if (_wishManager == null) return AutoDrawStartResult.PoolEmpty;
            if (drawController == null) return AutoDrawStartResult.PoolEmpty;
            if (drawController.IsWishInProgress) return AutoDrawStartResult.Busy;
            if (_currentPool == null || _currentPool.units.Count == 0 && _currentPool.items.Count == 0)
                return AutoDrawStartResult.PoolEmpty;
            if (!_wishManager.CanAfford(count)) return AutoDrawStartResult.NoPrimogem;

            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            drawController.StartWish(_wishManager, _currentPool, count, autoShoot: true);
            UpdateFateCount();
            return AutoDrawStartResult.Started;
        }

        private void UpdateFateCount()
        {
            if (fateCountText == null) return;
            var save = saveManager?.CurrentSave;
            if (save == null) return;

            fateCountText.text = save.GetItemCount(ItemName.Primogem).ToString();

            if (starglitterCountText != null)
                starglitterCountText.text = save.GetItemCount(ItemName.Starglitter).ToString();
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
