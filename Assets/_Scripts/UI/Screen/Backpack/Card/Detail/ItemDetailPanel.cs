using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Battle;
using GIC.Data;
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

            // 图标（缺失兜底 2026-10-06 全位点接入）
            MissingImageGuard.Assign(itemImage, raw.GetIcon(data.skin));

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

            // 描述（2026-10-16 首个可打出物品批·描述对齐返修：{Key} 占位符渲染——customParams
            // 单源、技能描述同模式（SkillDescriptionBuilder.BuildItem）；表未加载/无模板=回落
            // 本地化异步链兜底）
            _description.ClearAllEntries();
            var template = GetItemDescriptionTemplate(raw);
            if (template == null)
                _description.AddEntry(raw.GetDescriptionEntry());
            else
                _description.AddStaticEntry(SkillDescriptionBuilder.BuildItem(template, raw.customParams));

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

        /// <summary>物品描述模板同步读取（当前语言；表未加载/无条目=null——调用方回落本地化异步链）</summary>
        private string GetItemDescriptionTemplate(ItemConfig.ItemData raw)
        {
            var table = UnityEngine.Localization.Settings.LocalizationSettings.StringDatabase
                .GetTable(TableName.ItemDescription.ToString());
            return table?.GetEntry(raw.itemID.ToString())?.Value;
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


