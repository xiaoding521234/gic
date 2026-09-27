using System.Collections;
using GIC.Data.Event;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;
using GIC.UI;
using GIC.Data;
using GIC.Tool;
namespace GIC.Battle
{


    public class Card : MonoBehaviour
    {
        public CardType cardType => saveCardData?.id.cardType ?? CardType.Item;
        public ViewType viewType = ViewType.Display;
        public bool isEditMode = false;
        public bool skipFadeIn = false;

        // 卡组面板内卡牌（点击=移出卡组，纯边沿动作不选）——置位时同步关点按选中
        [SerializeField] private bool _isDeckPanelMode;
        public bool isDeckPanelMode
        {
            get => _isDeckPanelMode;
            set { _isDeckPanelMode = value; RefreshClickSelects(); }
        }

        public SelectButton selectButton;
        public Image cardBack;
        public Image select;
        public Image overlay;
        public TextCombiner obtainText;
        public Image onDeck;
        public CardDetailView cardDetailView;
        public TextMeshProUGUI countText;
        public SaveCardData saveCardData;

        [Header("发光叠加")]
        public Image glowImage;

        [Header("角色显示")]
        public Image unitImage;

        [Header("物品显示")]
        public Image countImage;
        public Image itemImage;

        [Header("动画")]
        [SerializeField] private CanvasGroup canvasGroup;
        private float fadeInDuration = 0.2f;
        [SerializeField] private AnimationCurve fadeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [SerializeField] private float slideOffset = 30f;
        [SerializeField] private RectTransform content; // 卡片内容容器，位移作用于此

        private ICardViewStrategy _strategy;

        [Autowired] private SaveManager _saveManager;

        public void Awake()
        {
            Wargame.Instance?.Context?.Inject(this);
            if (selectButton != null)
            {
                selectButton.onClick.AddListener(OnClicked);
                selectButton.onSelectedChanged.AddListener(OnSelectedChanged);
            }
        }

        private void OnEnable()
        {
            ResetContentPosition();
            if (!skipFadeIn)
                PlayFadeIn();
            else
            {
                var cg = GetComponent<CanvasGroup>();
                if (cg != null) cg.alpha = 1f;
            }
        }

        private void ResetContentPosition()
        {
            if (content != null)
                content.anchoredPosition = Vector2.zero;
        }

        public void PlayFadeIn()
        {
            StopAllCoroutines();
            StartCoroutine(FadeInCoroutine());
        }

        private IEnumerator FadeInCoroutine()
        {
            float elapsed = 0f;
            canvasGroup.alpha = 0f;

            RectTransform rt = content != null ? content : GetComponent<RectTransform>();
            Vector2 targetPos = rt.anchoredPosition;
            Vector2 startPos = targetPos - new Vector2(0f, slideOffset);
            rt.anchoredPosition = startPos;

            while (elapsed < fadeInDuration)
            {
                elapsed += Time.deltaTime;
                float t = fadeCurve.Evaluate(elapsed / fadeInDuration);
                canvasGroup.alpha = t;
                rt.anchoredPosition = Vector2.Lerp(startPos, targetPos, t);
                yield return null;
            }

            canvasGroup.alpha = 1f;
            rt.anchoredPosition = targetPos;
        }

        public void Init(SaveCardData data, CardDetailView detailView)
        {
            saveCardData = data;
            _strategy = CardViewStrategyFactory.Get(cardType);
            _strategy?.InitCardDisplay(this, data, detailView);
        }

        public void SetViewType(ViewType type)
        {
            viewType = type;
            if (type == ViewType.OnlyDisplay)
            {
                skipFadeIn = true;
                if (selectButton != null) selectButton.enabled = false;
            }
        }

        public void EnterEditMode()
        {
            isEditMode = true;
            RefreshClickSelects();
            _strategy?.EnterEditMode(this);
        }

        public void ExitEditDeck()
        {
            isEditMode = false;
            RefreshClickSelects();
            _strategy?.ExitEditDeck(this);
        }

        /// <summary>点按是否改变选中：仅浏览模式选（编辑/卡组面板=纯边沿动作，
        /// Toggle→SelectButton 改版 2026-09-27——旧「Toggle 当 Button 用、选中后手动关视觉」拧巴语义根除）</summary>
        private void RefreshClickSelects()
        {
            if (selectButton != null)
                selectButton.ClickChangesSelected = !(isEditMode || _isDeckPanelMode);
        }

        /// <summary>点击=边沿动作：浏览开详情 / 编辑与卡组面板发事件（onClick，Button 原生含置灰门）</summary>
        private void OnClicked()
        {
            if (isDeckPanelMode)
            {
                // 面板内卡片点击 → 移出卡组
                EventBusHub.Instance.SendImmediate(new OnCardClickedInEditModeEvent
                {
                    CardData = saveCardData,
                    IsInDeck = true
                });
                select.gameObject.SetActive(false);
            }
            else if (isEditMode)
            {
                bool inDeck = saveCardData?.HasInDeck(GetCurrentDeckId()) ?? false;
                EventBusHub.Instance.SendImmediate(new OnCardClickedInEditModeEvent
                {
                    CardData = saveCardData,
                    IsInDeck = inDeck
                });
                select.gameObject.SetActive(false);
            }
            else
            {
                OpenDetailView();
            }
        }

        /// <summary>浏览模式打开本卡详情（点击与背包初始选中共用；2026-09-27 SelectButton 改版：
        /// 旧 Toggle 时代由 onValueChanged(true) 承载（初始选中也走它），改版后开详情=onClick 边沿独占，
        /// 故初始选中须显式调用恢复等价行为——进背包=第一张卡选中+详情面板自动开）</summary>
        public void OpenDetailView()
        {
            cardDetailView.gameObject.Reactivate();
            cardDetailView.Init(this);
        }

        /// <summary>选中态视觉（select 图层开关——仅浏览模式会变化，视觉链唯一职责）</summary>
        private void OnSelectedChanged(bool isSelected)
        {
            select.gameObject.SetActive(isSelected);
        }

        private int GetCurrentDeckId()
        {
            return _saveManager?.CurrentSave?.progress.currentDeck ?? 0;
        }

        public void SetCount(int count)
        {
            countText.text = count.ToString();
            saveCardData.count = count;
        }

        /// <summary>
        /// 播放光带特效（正向：左下→右上）
        /// </summary>
        public void PlayLightBand()
        {
            var effect = GetComponent<CardLightBandEffect>();
            if (effect == null)
                effect = gameObject.AddComponent<CardLightBandEffect>();
            effect.Play();
        }

        /// <summary>
        /// 播放光带特效（反向：右上→左下）
        /// </summary>
        public void PlayLightBandReverse()
        {
            var effect = GetComponent<CardLightBandEffect>();
            if (effect == null)
                effect = gameObject.AddComponent<CardLightBandEffect>();
            effect.PlayReverse();
        }
    }

}


