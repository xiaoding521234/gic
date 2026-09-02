using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Battle;
using GIC.Data;
using GIC.Data.Event;
using GIC.Tool;
namespace GIC.UI
{


    /// <summary>
    /// 物品详情面板 — 负责物品专属字段的显示
    /// </summary>
    public class ItemDetailPanel : MonoBehaviour, ICardDetailPanel
    {
        [Header("物品详情")]
        public Image itemImage;
        public TextCombiner mainTag;
        public TextCombiner composition;
        public TextCombiner maxPrepareCount;

        [Header("使用按钮")]
        [SerializeField] private Button useButton;
        [SerializeField] private TextCombiner useButtonText;

        private IUsable _usable;
        private SaveCardData _saveData;

        // 公用字段引用（由 CardDetailView 注入）
        private Image _top;
        private Image _bottomImage;
        private GameObject _stars;
        private TextCombiner _cardName;
        private TextCombiner _description;

        /// <summary>
        /// 注入 CardDetailView 的公用字段引用
        /// </summary>
        public void InjectCommon(Image top, Image bottomImage, GameObject stars,
            TextCombiner cardName, TextCombiner tags, TextCombiner description)
        {
            _top = top;
            _bottomImage = bottomImage;
            _stars = stars;
            _cardName = cardName;
            _description = description;
        }

        public void Init(Card card)
        {
            InitInternal(card.saveCardData, false);
        }

        public void Init(SaveCardData data, bool isReadOnly = false)
        {
            InitInternal(data, isReadOnly);
        }

        private void InitInternal(SaveCardData data, bool isReadOnly)
        {
            var raw = CardConfigResolver.Instance?.ItemConfig?.GetItemData(data.id.AsItemName());
            if (raw == null) return;

            _top.color = StarVisualConfig.GetStarColor(raw.starLevel);
            _bottomImage.color = StarVisualConfig.GetStarColor(raw.starLevel);

            // 星级
            RefreshStars(raw.starLevel);

            // 名称
            _cardName.ClearAllEntries();
            _cardName.AddEntry(raw.itemID.GetEntry());

            // 图标
            itemImage.sprite = raw.GetIcon(data.skin);

            // 主标签
            mainTag.ClearAllEntries();
            mainTag.AddEntry(raw.subType.GetEntry());

            // 元素构成
            composition.ClearAllEntries();
            if (raw.elements is { Length: > 0 })
            {
                for (int i = 0; i < raw.elements.Length; i++)
                {
                    composition.AddStaticEntry($"{raw.elements[i].stacks} ");
                    composition.AddEntry(raw.elements[i].elementType.GetEntry());
                    if (i < raw.elements.Length - 1) composition.AddStaticEntry(" ");
                }
            }
            else composition.AddEntry(new LocalizedString(TableName.UIText.ToString(), "None"));

            // 最大备战数
            maxPrepareCount.ClearAllEntries();
            maxPrepareCount.AddEntry(new LocalizedString(TableName.UIText.ToString(), "MaxPrepareCount"));
            maxPrepareCount.AddStaticEntry("：");
            maxPrepareCount.AddStaticEntry(raw.maxPrepareCount.ToString());

            // 描述
            _description.ClearAllEntries();
            _description.AddEntry(raw.GetDescriptionEntry());

            // 使用按钮（只读模式下隐藏）
            if (isReadOnly)
            {
                if (useButton != null) useButton.gameObject.SetActive(false);
            }
            else if (raw is IUsable usable)
                SetUsable(usable, data);
            else
                SetUsable(null, null);
        }

        public void SetActive(bool active)
        {
            gameObject.SetActive(active);
        }

        public void SetUsable(IUsable usable, SaveCardData data)
        {
            _usable = usable;
            _saveData = data;

            if (useButton == null) return;

            bool canUse = usable != null && usable.CanUse;
            useButton.gameObject.SetActive(canUse);

            if (canUse && useButtonText != null)
            {
                useButtonText.ClearAllEntries();
                useButtonText.AddEntry(usable.GetUseButtonText());
            }

            useButton.onClick.RemoveAllListeners();
            if (canUse)
            {
                useButton.onClick.AddListener(() =>
                {
                    if (_usable == null || _saveData == null) return;
                    if (_usable.TryUse(_saveData, out var msg))
                    {
                        PopupManager.Instance.ShowToast(msg);
                    }
                    else
                    {
                        PopupManager.Instance.ShowToast(msg);
                    }
                });
            }
        }

        public void RebuildLayout()
        {
            // 由 CardDetailView 统一处理
        }

        private void RefreshStars(int starLevel)
        {
            int childCount = _stars.transform.childCount;
            starLevel = Mathf.Clamp(starLevel, 1, 5);
            for (int i = 0; i < childCount; i++)
                _stars.transform.GetChild(i).gameObject.SetActive(i < starLevel);
        }
    }

}


