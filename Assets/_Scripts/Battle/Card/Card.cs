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

    private ICardViewStrategy _strategy;

    public void Awake()
    {
        toggle.onValueChanged.AddListener(OnToggleValueChanged);
    }

    private void OnEnable()
    {
        PlayFadeIn();
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

        while (elapsed < fadeInDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeInDuration;
            canvasGroup.alpha = fadeCurve.Evaluate(t);
            yield return null;
        }

        canvasGroup.alpha = 1f;
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
            if (isEditMode)
            {
                bool inDeck = saveCardData?.HasInDeck(GetCurrentDeckId()) ?? false;
                EventBusHub.Instance.Publish(new OnCardClickedInEditModeEvent
                {
                    CardData = saveCardData,
                    IsInDeck = inDeck
                });
                select.gameObject.SetActive(false);
            }

            cardDetailView.gameObject.Reactivate();
            cardDetailView.Init(this);
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
