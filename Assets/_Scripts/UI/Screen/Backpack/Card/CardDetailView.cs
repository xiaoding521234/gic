using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 卡牌详情面板容器 — 管理公用字段，按 CardType 委托给对应子面板
/// </summary>
public class CardDetailView : MonoBehaviour
{
    public CardType cardType => card?.cardType ?? CardType.Unit;
    public Card card;
    public Image top;
    public TextCombiner cardName;
    public Image bottomImage;
    public GameObject stars;
    public TextCombiner tags;
    public Transform tagContainer;
    public TextCombiner description;
    public RectTransform descriptionContent;
    public Button skinButton;
    public TextMeshProUGUI skin;

    [Header("子面板（场景中挂载）")]
    [SerializeField] private UnitDetailPanel unitDetailPanel;
    [SerializeField] private ItemDetailPanel itemDetailPanel;

    private ICardDetailPanel _activePanel;
    private ICardViewStrategy _strategy;

    private void Awake()
    {
        skinButton.onClick.AddListener(OnSkinButtonClicked);

        // 注入公用字段引用
        if (unitDetailPanel != null)
            unitDetailPanel.InjectCommon(top, bottomImage, stars, cardName, tags, description);
        if (itemDetailPanel != null)
            itemDetailPanel.InjectCommon(top, bottomImage, stars, cardName, tags, description);
    }

    public void Init(Card c)
    {
        card = c;
        _strategy = CardViewStrategyFactory.Get(cardType);

        // 切换子面板
        SwitchPanel(cardType);
        _activePanel?.Init(c);

        RefreshSkinDisplay();
        RebuildLayout();
    }

    private void SwitchPanel(CardType type)
    {
        if (unitDetailPanel != null) unitDetailPanel.SetActive(type == CardType.Unit);
        if (itemDetailPanel != null) itemDetailPanel.SetActive(type == CardType.Item);

        // 标签显示模式：角色用 tagContainer（方形背景芯片），物品不显示描述区标签
        if (tags != null) tags.gameObject.SetActive(false);
        if (tagContainer != null) tagContainer.gameObject.SetActive(type == CardType.Unit);

        _activePanel = type switch
        {
            CardType.Unit => unitDetailPanel,
            CardType.Item => itemDetailPanel,
            _ => null,
        };
    }

    private void OnSkinButtonClicked()
    {
        if (card == null || _strategy == null) return;

        int totalSkins = _strategy.GetTotalSkins(card);
        if (totalSkins <= 1)
        {
            GameScene.Instance.ShowLocalizedPopup("Skin_OnlyOne");
            return;
        }

        card.saveCardData.skin = (card.saveCardData.skin + 1) % totalSkins;
        _strategy.ApplySkin(card, card.saveCardData.skin);
        RefreshSkinDisplay();
    }

    private void RefreshSkinDisplay()
    {
        if (card?.saveCardData == null || _strategy == null)
        {
            skin.gameObject.SetActive(false);
            return;
        }

        int totalSkins = _strategy.GetTotalSkins(card);
        skin.gameObject.SetActive(totalSkins > 0);
        skin.text = $"{card.saveCardData.skin + 1}/{totalSkins}";
    }

    public void RebuildLayout()
    {
        LayoutRebuilder.ForceRebuildLayoutImmediate(descriptionContent);
    }
}
