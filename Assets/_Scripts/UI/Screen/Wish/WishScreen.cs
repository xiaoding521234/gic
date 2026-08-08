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


    public partial class WishScreen : MonoBehaviour
    {
        [Serializable]
        public class CharacterEntry
        {
            public Button button;
            public CharacterPanelController panel;
            [Tooltip("选中时显示的图片（可选，不填则用 button 的 TargetGraphic）")]
            public Image buttonImage;
        }

        public AudioClip wishClip;
        public Button closeButton;
        public RectTransform selector;

        [Header("面板动画")]
        public RectTransform topPanel;
        public RectTransform leftPanel;
        public RectTransform bottomPanel;
        public CanvasGroup panelCanvasGroup;
        [SerializeField] private float panelSlideDuration = 0.2f;
        [SerializeField] private float panelSlideDistance = 200f;
        [SerializeField] private AnimationCurve panelSlideCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("角色列表")]
        [SerializeField] private CharacterEntry[] characters;

        [Header("按钮颜色")]
        [SerializeField] private Color selectedColor = Color.white;
        [SerializeField] private Color unselectedColor = new Color(0.5f, 0.5f, 0.5f, 1f);

        [Header("氛围特效")]
        [SerializeField] private WishAmbienceController ambience;

        private int currentIndex = -1;
        private bool isSwitching;

        private Vector2? selectorOffset;

        // 缓存的面板目标位置
        private Vector2 topPanelTargetPos;
        private Vector2 leftPanelTargetPos;
        private Vector2 bottomPanelTargetPos;

        public void Awake()
        {
            closeButton.onClick.AddListener(Close);

            if (selector != null)
                selectorOffset = selector.anchoredPosition;

            CachePanelPositions();
            SetPanelsToStartOffset();  // Awake 就移到偏移位，避免首帧闪烁

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

        private void Start()
        {
            if (wishClip != null)
            {
                AudioManager.Instance.PushMusicState(wishClip, MusicType.Relaxed, loop: true, fadeInTime: 1f);
            }

            InitWishDraw();

            // 确保所有 Layout Group 计算完成，并等渲染管线跑完一帧
            Canvas.ForceUpdateCanvases();
            StartCoroutine(PlaySlideInAnimationAfterLayoutReady());
        }

        private IEnumerator PlaySlideInAnimationAfterLayoutReady()
        {
            yield return new WaitForEndOfFrame();

            if (characters.Length > 0)
            {
                StartCoroutine(PlaySlideInAnimation());
                StartCoroutine(SelectFirstCharacterDelayed());
            }
            else
            {
                StartCoroutine(PlaySlideInAnimation());
            }
        }

        /// <summary>
        /// 入场动画开始播放延迟 0.1s 选中第一个角色
        /// </summary>
        private IEnumerator SelectFirstCharacterDelayed()
        {
            yield return new WaitForSeconds(0.1f);
            SelectCharacter(0);
        }

        private void Close()
        {
            AudioManager.Instance.PopMusicState();
            StartCoroutine(CloseCoroutine());
        }

        private IEnumerator CloseCoroutine()
        {
            // 三面板退场 + 角色面板淡出 同时进行
            Coroutine slideOut = StartCoroutine(PlaySlideOutAnimation());
            if (currentIndex >= 0 && characters[currentIndex].panel != null)
                characters[currentIndex].panel.FadeOut();

            yield return slideOut;
            GameScene.Instance.GoBack();
        }

        public void SelectCharacter(int index)
        {
            if (isSwitching || index == currentIndex) return;
            if (index < 0 || index >= characters.Length) return;

            StartCoroutine(SwitchCharacterCoroutine(index));
        }

        private IEnumerator SwitchCharacterCoroutine(int newIndex)
        {
            isSwitching = true;

            var oldEntry = currentIndex >= 0 ? characters[currentIndex] : null;
            var newEntry = characters[newIndex];

            // 淡出当前面板
            if (oldEntry?.panel != null)
            {
                oldEntry.panel.FadeOut();
                yield return new WaitForSeconds(oldEntry.panel.FadeDuration);
            }

            currentIndex = newIndex;

            // selector 移动 + 面板淡入 同时进行
            MoveSelectorTo(newEntry.button);

            // 更新按钮颜色
            RefreshButtonColors();

            // 淡入新面板
            if (newEntry.panel != null)
            {
                newEntry.panel.FadeIn();
            }

            // 更新氛围特效的元素颜色
            if (ambience != null)
            {
                var config = Wargame.Instance?.ConfigManager?.GetUnitConfig();
                var unitData = config?.GetUnitData(newEntry.panel.UnitName);
                if (unitData != null)
                    ambience.SetElementColor(ElementColor.GetColor(unitData.selfElement));
            }

            isSwitching = false;
        }

        private void RefreshButtonColors()
        {
            for (int i = 0; i < characters.Length; i++)
            {
                var entry = characters[i];
                Color target = (i == currentIndex) ? selectedColor : unselectedColor;
                ApplyButtonColor(entry, target);
            }
        }

        private void MoveSelectorTo(Button targetButton)
        {
            if (selector == null || targetButton == null) return;
            if (selector.parent == targetButton.transform) return;

            selector.SetParent(targetButton.transform, worldPositionStays: false);
            selector.anchoredPosition = selectorOffset ?? Vector2.zero;
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

        // ==================== 面板入场/退场动画 ====================

        private void CachePanelPositions()
        {
            if (topPanel != null)    topPanelTargetPos    = topPanel.anchoredPosition;
            if (leftPanel != null)   leftPanelTargetPos   = leftPanel.anchoredPosition;
            if (bottomPanel != null) bottomPanelTargetPos = bottomPanel.anchoredPosition;
        }

        private void SetPanelsToStartOffset()
        {
            if (topPanel != null)
                topPanel.anchoredPosition = topPanelTargetPos + Vector2.up * panelSlideDistance;
            if (leftPanel != null)
                leftPanel.anchoredPosition = leftPanelTargetPos + Vector2.left * panelSlideDistance;
            if (bottomPanel != null)
                bottomPanel.anchoredPosition = bottomPanelTargetPos + Vector2.down * panelSlideDistance;
            if (panelCanvasGroup != null)
                panelCanvasGroup.alpha = 0f;
        }

        private IEnumerator PlaySlideInAnimation()
        {
            // Awake 已设置偏移位，这里只做动画

            float startTime = Time.realtimeSinceStartup;

            while (true)
            {
                float elapsed = Time.realtimeSinceStartup - startTime;
                elapsed = Mathf.Min(elapsed, panelSlideDuration);

                if (elapsed >= panelSlideDuration)
                    break;

                float t = panelSlideCurve.Evaluate(elapsed / panelSlideDuration);

                if (topPanel != null)
                    topPanel.anchoredPosition = topPanelTargetPos + Vector2.up * Mathf.LerpUnclamped(panelSlideDistance, 0f, t);
                if (leftPanel != null)
                    leftPanel.anchoredPosition = leftPanelTargetPos + Vector2.left * Mathf.LerpUnclamped(panelSlideDistance, 0f, t);
                if (bottomPanel != null)
                    bottomPanel.anchoredPosition = bottomPanelTargetPos + Vector2.down * Mathf.LerpUnclamped(panelSlideDistance, 0f, t);
                if (panelCanvasGroup != null)
                    panelCanvasGroup.alpha = t;

                yield return null;
            }

            SnapPanelsToTarget();
            if (panelCanvasGroup != null) panelCanvasGroup.alpha = 1f;
        }

        private IEnumerator PlaySlideOutAnimation()
        {
            float startAlpha = panelCanvasGroup != null ? panelCanvasGroup.alpha : 1f;
            float startTime = Time.realtimeSinceStartup;

            while (true)
            {
                float elapsed = Time.realtimeSinceStartup - startTime;
                elapsed = Mathf.Min(elapsed, panelSlideDuration);

                if (elapsed >= panelSlideDuration)
                    break;

                float t = panelSlideCurve.Evaluate(elapsed / panelSlideDuration);

                if (topPanel != null)
                    topPanel.anchoredPosition = topPanelTargetPos + Vector2.up * Mathf.LerpUnclamped(0f, panelSlideDistance, t);
                if (leftPanel != null)
                    leftPanel.anchoredPosition = leftPanelTargetPos + Vector2.left * Mathf.LerpUnclamped(0f, panelSlideDistance, t);
                if (bottomPanel != null)
                    bottomPanel.anchoredPosition = bottomPanelTargetPos + Vector2.down * Mathf.LerpUnclamped(0f, panelSlideDistance, t);
                if (panelCanvasGroup != null)
                    panelCanvasGroup.alpha = Mathf.LerpUnclamped(startAlpha, 0f, t);

                yield return null;
            }

            if (panelCanvasGroup != null) panelCanvasGroup.alpha = 0f;
        }

        private void SnapPanelsToTarget()
        {
            if (topPanel != null)    topPanel.anchoredPosition    = topPanelTargetPos;
            if (leftPanel != null)   leftPanel.anchoredPosition   = leftPanelTargetPos;
            if (bottomPanel != null) bottomPanel.anchoredPosition = bottomPanelTargetPos;
        }
    }
}


