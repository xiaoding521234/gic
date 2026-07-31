using System.Collections;
using LocalEvents;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Card : MonoBehaviour
{
    public CardType cardType => saveCardData?.id.cardType ?? CardType.Item;
    public ViewType viewType = ViewType.Display;
    public bool isEditMode = false;
    public bool isDeckPanelMode = false;

    public Toggle toggle;
    public Image cardBack;
    public Image select;
    public Image overlay;
    public TextCombiner obtainText;
    public Image onDeck;
    public CardDetailView cardDetailView;
    public TextMeshProUGUI countText;
    public SaveCardData saveCardData;

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

    public void Awake()
    {
        toggle.onValueChanged.AddListener(OnToggleValueChanged);
    }

    private void OnEnable()
    {
        ResetContentPosition();
        PlayFadeIn();
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

    public void SetViewType(ViewType type) => viewType = type;

    public void EnterEditMode()
    {
        isEditMode = true;
        _strategy?.EnterEditMode(this);
    }

    public void ExitEditDeck()
    {
        isEditMode = false;
        overlay.gameObject.SetActive(false);
    }

    public void OnToggleValueChanged(bool isOn)
    {
        select.gameObject.SetActive(isOn);

        if (isOn)
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
                cardDetailView.gameObject.Reactivate();
                cardDetailView.Init(this);
            }
        }
    }

    private int GetCurrentDeckId()
    {
        return Wargame.Instance?.SaveManager?.CurrentSave?.currentDeck ?? 0;
    }

    public void SetCount(int count)
    {
        countText.text = count.ToString();
        saveCardData.count = count;
    }
}
