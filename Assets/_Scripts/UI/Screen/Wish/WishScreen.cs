using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
using GIC.Tool;
namespace GIC.UI
{


    public partial class WishScreen : ScreenBase
    {
        [Serializable]
        public class CharacterEntry
        {
            public Button button;
            public CharacterPanelController panel;
            [Tooltip("选中时显示的图片（可选，不填则用 button 的 TargetGraphic）")]
            public Image buttonImage;
            [Tooltip("该角色对应的祈愿卡池（为空表示卡池未开放）")]
            public WishPoolConfig pool;
        }

        public AudioClip wishClip;
        public Button closeButton;
        public RectTransform selector;

        [Header("面板动画")]
        [Tooltip("毛玻璃动画公共组件（挂面板根；祈愿=无模糊层纯滑动+整组淡入）")]
        [SerializeField] private GlassPanelAnimator 毛玻璃动画器;

        [Header("角色列表")]
        [SerializeField] private CharacterEntry[] characters;

        [Header("按钮颜色")]
        [SerializeField] private Color selectedColor = Color.white;
        [SerializeField] private Color unselectedColor = new Color(0.5f, 0.5f, 0.5f, 1f);

        [Header("氛围特效")]
        [SerializeField] private WishAmbienceController ambience;

        private int _currentIndex = -1;
        private bool _isSwitching;

        private Vector2? _selectorOffset;

        protected override void Awake()
        {
            base.Awake(); // 注入（Boot 链路容器已就绪）

            closeButton.onClick.AddListener(Close);

            if (selector != null)
                _selectorOffset = selector.anchoredPosition;

            // 目标位缓存由公共组件 GlassPanelAnimator 在自身 Awake 自动完成（§39）

            // 尽早预加载所有角色立绘（4K 纹理提前加载，首次选中即可丝滑淡入）
            foreach (var entry in characters)
            {
                if (entry.panel != null)
                    entry.panel.PreloadSprite();
            }

            for (int i = 0; i < characters.Length; i++)
            {
                int index = i;
                var entry = characters[i];
                if (entry.button != null)
                    entry.button.onClick.AddListener(() => SelectCharacter(index));
                if (entry.panel != null)
                    entry.panel.gameObject.SetActive(false);
            }
        }

        // prefab 面板：静态身份（场景名寻址在面板实例化进宿主场景后失效，P2 定则）
        protected override ScreenId Id => Screens.Wish;

        /// <summary>
        /// 每次打开（池化生命周期，docs/14 §38）：音乐状态切换 + 池化状态复位 + 抽卡部分重初始化 + 入场动画。
        /// 起始态已在 Awake 设过（幂等再设，docs/14 §37 纪律①）。
        /// </summary>
        protected override void OnShow(object args)
        {
            if (wishClip != null)
            {
                PushMusicStateSafe(wishClip, MusicType.Relaxed, loop: true, fadeInTime: 1f);
            }

            // 起始态：只设偏移（目标位缓存由公共组件 Awake 自动完成，勿重复缓存——
            // 重复缓存会把偏移位覆写成目标位=面板永远滑不回屏幕，docs/14 §39 用户目检实证）
            if (毛玻璃动画器 != null) 毛玻璃动画器.SetEntryOffsets();

            // 池化状态复位：选中态回未选、切换锁复位（旧场景制靠重载天然复位）
            _currentIndex = -1;
            _isSwitching = false;

            // 池化连坐复位（docs/14 §39）：全部角色面板强制隐藏——上次会话的 FadeOut 被
            // 入池打断时卡在"半透明+激活"（重开双卡池叠加实证）；首个面板由首选角色协程 FadeIn 拉起
            foreach (var entry in characters)
            {
                if (entry != null && entry.panel != null)
                    entry.panel.ResetHidden();
            }

            // 抽卡部分：管理器重建+货币数刷新（按钮/事件订阅已拆入 OnInit，一次 wiring 防池化叠监听）
            InitWishDraw();

            // 确保所有 Layout Group 计算完成，并等渲染管线跑完一帧
            Canvas.ForceUpdateCanvases();
            StartCoroutine(PlaySlideInAnimationAfterLayoutReady());
        }

        private IEnumerator PlaySlideInAnimationAfterLayoutReady()
        {
            yield return new WaitForEndOfFrame();

            // 入场走公共组件+基类包装（Entering 锁+isClosing 守卫，2026-09-13 统一——原实现无锁）
            StartCoroutine(PlayGlassEnter(毛玻璃动画器));
            if (characters.Length > 0)
                StartCoroutine(SelectFirstCharacterDelayed());
        }

        /// <summary>
        /// 入场动画开始播放延迟 0.1s 选中第一个角色
        /// </summary>
        private IEnumerator SelectFirstCharacterDelayed()
        {
            yield return Wait.Seconds(0.1f);
            SelectCharacter(0);
        }

        // ── IClosable 实现（标准关闭模板 + 三面板退场） ──
        public override void Close()
        {
            CloseScreen(ExitAnimation);
        }

        [Autowired] private UnitConfig _unitConfig;

        /// <summary>退场动画：三面板滑出+整组淡出走公共组件（系数 1=保持原 0.2s 等时长退场）；
        /// 当前角色面板淡出为祈愿特有表现，保留在编排层（收尾由模板统一处理）</summary>
        private IEnumerator ExitAnimation()
        {
            if (characters != null && _currentIndex >= 0 && _currentIndex < characters.Length
                && characters[_currentIndex].panel != null)
                characters[_currentIndex].panel.FadeOut();

            if (毛玻璃动画器 != null)
                yield return 毛玻璃动画器.ExitRoutine(1f);
        }

        public void SelectCharacter(int index)
        {
            if (_isSwitching || index == _currentIndex) return;
            if (index < 0 || index >= characters.Length) return;

            StartCoroutine(SwitchCharacterCoroutine(index));
        }

        private IEnumerator SwitchCharacterCoroutine(int newIndex)
        {
            _isSwitching = true;

            // 立即触发粒子加速，不等淡出
            if (ambience != null)
                ambience.OnPoolSwitching();

            var oldEntry = _currentIndex >= 0 ? characters[_currentIndex] : null;
            var newEntry = characters[newIndex];

            // 预加载新角色立绘（利用旧面板淡出的时间并行加载 4K 纹理）
            if (newEntry.panel != null)
                newEntry.panel.PreloadSprite();

            // 淡出当前面板
            if (oldEntry?.panel != null)
            {
                oldEntry.panel.FadeOut();
                yield return Wait.Seconds(oldEntry.panel.FadeDuration);
            }

            _currentIndex = newIndex;

            // selector 移动 + 面板淡入 同时进行
            MoveSelectorTo(newEntry.button);

            // 更新按钮颜色
            RefreshButtonColors();

            // 淡入新面板
            if (newEntry.panel != null)
            {
                newEntry.panel.FadeIn();
            }

            // 切换对应的祈愿卡池
            SwitchPool(newEntry.pool);

            // 更新氛围特效的元素颜色（panel 可为空——与上面 FadeIn 同守卫，防 NRE 中断协程卡死切换锁）
            if (ambience != null && newEntry.panel != null)
            {
                var unitData = _unitConfig?.GetUnitData(newEntry.panel.UnitName);
                if (unitData != null)
                    ambience.SetElementColor(ElementFactionConfig.Instance.GetElementColor(unitData.selfElement));
            }

            _isSwitching = false;
        }

        private void RefreshButtonColors()
        {
            for (int i = 0; i < characters.Length; i++)
            {
                var entry = characters[i];
                Color target = (i == _currentIndex) ? selectedColor : unselectedColor;
                ApplyButtonColor(entry, target);
            }
        }

        private void MoveSelectorTo(Button targetButton)
        {
            if (selector == null || targetButton == null) return;
            if (selector.parent == targetButton.transform) return;

            selector.SetParent(targetButton.transform, worldPositionStays: false);
            selector.anchoredPosition = _selectorOffset ?? Vector2.zero;
        }

        private void ApplyButtonColor(CharacterEntry entry, Color color)
        {
            // 优先设置 buttonImage，否则用 button 自身的 TargetGraphic
            if (entry.buttonImage != null)
            {
                entry.buttonImage.color = color;
            }
            else if (entry.button != null)
            {
                var graphic = entry.button.targetGraphic;
                if (graphic != null) graphic.color = color;
            }
        }

        // 面板入场/退场动画：已由公共组件 GlassPanelAnimator 承载（三面板滑动+整组淡入，
        // 无模糊层），本类仅保留 OnShow 起始态与退场编排（角色面板 FadeOut）
    }
}


