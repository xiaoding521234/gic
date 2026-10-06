using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Battle;
using GIC.Data;
using GIC.Tool;
namespace GIC.UI
{


    /// <summary>
    /// 角色详情面板 — 负责角色专属字段的显示
    /// </summary>
    public class UnitDetailPanel : MonoBehaviour, ICardDetailPanel
    {
        [Header("角色详情")]
        public Image nameCard;
        public TextCombiner mainFaction;
        public TextCombiner element;
        public TextCombiner weapon;
        public TextCombiner move;
        public GameObject skillsPanel;
        public GameObject skillViewPrefab;
        public SkillDetailView skillDetailView;
        public SelectionGroup selectionGroup;

        [Header("标签芯片")]
        [SerializeField] private GameObject tagChipPrefab;

        [Header("数据面板")]
        [SerializeField] private UnitStatBriefPanel 数据小面板;
        public UnitStatsPanel 全部数据面板; // 宿主界面接线（BackpackScreen 内为两处实例覆写；未接线的宿主点击小面板无效果）

        private UnitConfig.UnitData _lastRaw;

        // 公用字段引用（由 CardDetailView 注入）
        private Image _top;
        private Image _bottomImage;
        private GameObject _stars;
        private TextCombiner _cardName;
        private TextCombiner _tags;
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
            _tags = tags;
            _description = description;
        }

        private void Awake()
        {
            if (数据小面板 != null && 数据小面板.点击按钮 != null)
                数据小面板.点击按钮.onClick.AddListener(OpenFullStatsPanel);
        }

        /// <summary>点击小数据面板 → 打开全部数据面板（未接线宿主为空操作）；诊断日志用于点击链定位（2026-10-07 报障排查）</summary>
        public void OpenFullStatsPanel()
        {
            if (_lastRaw == null)
            {
                GICLog.Warn("[UnitDetailPanel] 点击小数据面板但 _lastRaw 为空，忽略");
                return;
            }
            if (全部数据面板 == null)
            {
                GICLog.Warn("[UnitDetailPanel] 点击小数据面板但 全部数据面板 未接线（该宿主不支持），忽略");
                return;
            }
            GICLog.Info("[UnitDetailPanel] 打开全部数据面板: " + _lastRaw.unitName);
            全部数据面板.Open(_lastRaw);
        }

        public void Init(Card card)
        {
            var raw = CardConfigResolver.Instance?.UnitConfig?.GetUnitData(card.saveCardData.id.AsUnitName());
            if (raw == null) return;
            InitInternal(raw, card.saveCardData, card?.cardDetailView?.tagContainer);
        }

        public void Init(SaveCardData data, bool isReadOnly = false)
        {
            var raw = CardConfigResolver.Instance?.UnitConfig?.GetUnitData(data.id.AsUnitName());
            if (raw == null) return;
            InitInternal(raw, data, null);
        }

        private void InitInternal(UnitConfig.UnitData raw, SaveCardData data, Transform tagContainerFromCard)
        {
            _lastRaw = raw;
            _top.color = StarVisualConfig.GetStarColor(raw.starLevel);
            _bottomImage.color = StarVisualConfig.GetStarColor(raw.starLevel);
            MissingImageGuard.Assign(nameCard, raw.nameCard); // 名片缺失兜底（调用点显式）

            // 星级
            RefreshStars(raw.starLevel);

            // 名称
            _cardName.ClearAllEntries();
            _cardName.AddEntry(raw.GetTitleEntry());
            _cardName.AddStaticEntry("·");
            _cardName.AddEntry(raw.GetNameEntry());

            // 势力
            mainFaction.ClearAllEntries();
            if (raw.factions is { Length: > 0 })
                mainFaction.AddEntry(raw.factions[0].GetEntry());

            // 元素
            element.ClearAllEntries();
            element.AddEntry(raw.selfElement.GetEntry());

            // 武器
            weapon.ClearAllEntries();
            weapon.AddEntry(raw.weaponType.GetEntry());

            // 移动
            move.ClearAllEntries();
            move.AddEntry(raw.normalMoveType.GetEntry());
            move.AddEntry(new TextEntry(null, "："));
            move.AddEntry(new TextEntry(null, raw.GetEffectiveMoveSpeed().ToString()));

            // 标签
            RefreshTagChips(raw, tagContainerFromCard);

            // 技能面板
            RefreshSkillsPanel(raw);

            // 小数据面板（技能区块之下、介绍区块之上的属性摘要；默认隐藏，仅角色卡激活）
            if (数据小面板 != null)
            {
                数据小面板.gameObject.SetActive(true);
                数据小面板.Init(raw);
            }

            // 描述
            _description.ClearAllEntries();
            _description.AddEntry(raw.GetDescriptionEntry());
        }

        public void SetActive(bool active)
        {
            gameObject.SetActive(active);
            if (skillsPanel != null)
                skillsPanel.SetActive(active);
            if (数据小面板 != null)
                数据小面板.gameObject.SetActive(active);
        }

        public void SetUsable(IUsable usable, SaveCardData data)
        {
            // 角色面板不支持使用功能
        }

        public void RebuildLayout()
        {
            // 由 CardDetailView 统一处理
        }

        private void RefreshTagChips(UnitConfig.UnitData unitData, Transform container)
        {
            if (container == null || tagChipPrefab == null) return;

            // 清除旧芯片
            for (int i = 0; i < container.childCount; i++)
                Destroy(container.GetChild(i).gameObject);

            if (unitData.tags is not { Length: > 0 }) return;

            foreach (var tag in unitData.tags)
            {
                var chipObj = Instantiate(tagChipPrefab, container);
                var chip = chipObj.GetComponent<TagChip>();
                chip?.SetEntry(tag.GetEntry());
            }
        }

        private void RefreshStars(int starLevel)
        {
            int childCount = _stars.transform.childCount;
            starLevel = Mathf.Clamp(starLevel, 1, 5);
            for (int i = 0; i < childCount; i++)
                _stars.transform.GetChild(i).gameObject.SetActive(i < starLevel);
        }

        private void RefreshSkillsPanel(UnitConfig.UnitData unitData)
        {
            if (skillsPanel == null) return;
            for (int i = 0; i < skillsPanel.transform.childCount; i++)
                Destroy(skillsPanel.transform.GetChild(i).gameObject);

            if (unitData.skills == null || unitData.skills.Count == 0) return;

            for (int i = 0; i < unitData.skills.Count; i++)
            {
                SkillConfig.SkillData skillData = unitData.skills[i]?.data;
                if (skillData == null) continue;

                GameObject skillViewObj = Instantiate(skillViewPrefab, skillsPanel.transform);
                SkillIconView skillIconView = skillViewObj.GetComponent<SkillIconView>();
                if (skillIconView != null)
                {
                    skillIconView.InitWithData(skillData, unitData, ViewType.Display, skillDetailView);
                    if (skillIconView.selectButton != null && selectionGroup != null)
                        skillIconView.selectButton.Group = selectionGroup;
                }
            }
        }
    }

}


