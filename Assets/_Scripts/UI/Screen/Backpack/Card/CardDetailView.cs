using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Battle;
using GIC.Data;
using GIC.Data.Event;
using GIC.Tool;
namespace GIC.UI
{


    /// <summary>
    /// 卡牌详情面板容器 — 管理公用字段，按 CardType 委托给对应子面板
    /// </summary>
    public class CardDetailView : MonoBehaviour
    {
        public CardType cardType => _saveData?.cardType ?? card?.cardType ?? CardType.Unit;
        public Card card;
        public SaveCardData saveCardData => _saveData ?? card?.saveCardData;
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
        private SaveCardData _saveData;

        /// <summary>
        /// 只读模式：隐藏所有可交互按钮（皮肤切换、使用按钮等），用于关联面板
        /// </summary>
        public bool IsReadOnly { get; private set; }

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
            _saveData = null;
            _strategy = CardViewStrategyFactory.Get(cardType);

            // 切换子面板
            SwitchPanel(cardType);
            _activePanel?.Init(c);

            RefreshSkinDisplay();
            RebuildLayout();
        }

        /// <summary>
        /// 不依赖 Card 组件的初始化（用于关联面板等无 Card 实例的场景）
        /// </summary>
        public void Init(SaveCardData data)
        {
            IsReadOnly = true;

            card = null;
            _saveData = data;
            _strategy = CardViewStrategyFactory.Get(cardType);

            EnsureInjected();
            SwitchPanel(cardType);
            _activePanel?.Init(data, true);

            // 只读模式：隐藏皮肤按钮
            if (skinButton != null) skinButton.gameObject.SetActive(false);

            RebuildLayout();
        }

        private void EnsureInjected()
        {
            if (unitDetailPanel != null)
                unitDetailPanel.InjectCommon(top, bottomImage, stars, cardName, tags, description);
            if (itemDetailPanel != null)
                itemDetailPanel.InjectCommon(top, bottomImage, stars, cardName, tags, description);
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
            if (saveCardData == null || _strategy == null) return;

            if (card == null)
            {
                GameScene.Instance.ShowLocalizedPopup("Skin_OnlyOne");
                return;
            }

            int totalSkins = _strategy.GetTotalSkins(card);
            if (totalSkins <= 1)
            {
                GameScene.Instance.ShowLocalizedPopup("Skin_OnlyOne");
                return;
            }

            card.saveCardData.skin = (card.saveCardData.skin + 1) % totalSkins;
            _strategy.ApplySkin(card, card.saveCardData.skin);
            RefreshSkinDisplay();

            card.PlayLightBand();
        }

        private void RefreshSkinDisplay()
        {
            if (saveCardData == null || _strategy == null)
            {
                skin.gameObject.SetActive(false);
                return;
            }

            if (card == null)
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

}


