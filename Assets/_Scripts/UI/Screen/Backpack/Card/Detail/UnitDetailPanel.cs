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

        /// <summary>标签芯片容器宿主回填（只读模式用）：Init(Card) 路径标签容器随 Card 自带，
        /// Init(SaveCardData) 只读路径拿不到 Card——由 CardDetailView.Init(SaveCardData) 把自身
        /// tagContainer 注入此处（2026-10-10 战斗手牌详情接入；关联面板卡模式同路径受益）</summary>
        public Transform TagContainer { private get; set; }

        private UnitConfig.UnitData _lastRaw;

        // ===== 技能行 tag 标签规格（2026-10-11 决策六十四「卡片详情里也需要显示」）=====
        // SkillPanel 行内 Skill 实例=115×115（烘焙基准）——标签为战斗被动盘 154 基准的同比例缩放
        //（被动字号 42×115/154≈31；宽 180×0.747≈134、高 27×0.747≈20、底距 38×0.747≈28）
        private const float 详情标签宽 = 134f;
        private const float 详情标签高 = 20f;
        private const float 详情标签底距 = 28f;
        private const float 详情标签字号 = 31f;

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
            InitInternal(raw, card.saveCardData, card?.cardDetailView?.tagContainer, false);
        }

        public void Init(SaveCardData data, bool isReadOnly = false)
        {
            var raw = CardConfigResolver.Instance?.UnitConfig?.GetUnitData(data.id.AsUnitName());
            if (raw == null) return;
            InitInternal(raw, data, TagContainer, isReadOnly);
        }

        private void InitInternal(UnitConfig.UnitData raw, SaveCardData data, Transform tagContainerFromCard, bool isReadOnly)
        {
            _lastRaw = raw;
            _top.color = StarVisualConfig.GetStarColor(raw.starLevel);
            _bottomImage.color = StarVisualConfig.GetStarColor(raw.starLevel);
            MissingImageGuard.Assign(nameCard, raw.nameCard); // 名片缺失兜底（调用点显式）

            // 星级
            RefreshStars(raw.starLevel);

            // 名称（建筑无称号——2026-10-13 拍板「建筑不需要称号」，只显示名称，unitType 单一判据同口径）
            _cardName.ClearAllEntries();
            if (raw.unitType != UnitType.Building)
            {
                _cardName.AddEntry(raw.GetTitleEntry());
                _cardName.AddStaticEntry("·");
            }
            _cardName.AddEntry(raw.GetNameEntry());

            // 势力
            mainFaction.ClearAllEntries();
            if (raw.factions is { Length: > 0 })
                mainFaction.AddEntry(raw.factions[0].GetEntry());

            // 元素
            element.ClearAllEntries();
            element.AddEntry(raw.selfElement.GetEntry());

            // 武器/移动行：**武器行建筑显示**（决策八十六批F 拍板 #2 推翻 2026-10-13 隐藏拍板武器
            // 部分——建筑统一臂铠图标）；移动行维持隐藏（建筑不移动）；势力/元素行保留——
            // 建筑可有势力（如西风大教堂=蒙德），协议核心无势力自然留空
            bool isBuilding = raw.unitType == UnitType.Building;
            if (weapon != null) weapon.gameObject.SetActive(true);
            if (move != null) move.gameObject.SetActive(!isBuilding);

            // 武器（建筑=臂铠，拍板 #2 统一）
            weapon.ClearAllEntries();
            weapon.AddEntry(raw.weaponType.GetEntry());

            if (!isBuilding)
            {
                // 移动
                move.ClearAllEntries();
                move.AddEntry(raw.normalMoveType.GetEntry());
                move.AddEntry(new TextEntry(null, "："));
                move.AddEntry(new TextEntry(null, raw.GetEffectiveMoveSpeed().ToString()));
            }

            // 标签
            RefreshTagChips(raw, tagContainerFromCard);

            // 技能面板（只读模式看宿主接线：已接 skillDetailView（如战斗手牌详情，2026-10-10 追拍
            // 「应当和背包里一样可以点击」）=Display 可点选开详情同背包；未接宿主=OnlyDisplay 纯展示
            // ——Display 态点选会空引用开面板，无接线宿主必须降级防 NPE）
            RefreshSkillsPanel(raw, isReadOnly && skillDetailView == null);

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

        private void RefreshSkillsPanel(UnitConfig.UnitData unitData, bool isReadOnly = false)
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
                    skillIconView.InitWithData(skillData, unitData,
                        isReadOnly ? ViewType.OnlyDisplay : ViewType.Display, skillDetailView);
                    if (skillIconView.selectButton != null && selectionGroup != null)
                        skillIconView.selectButton.Group = selectionGroup;
                }
                // 2026-10-11 决策六十四追拍「卡片详情里也需要显示」：技能行图标挂技能 tag 标签
                // （战斗被动盘同款共享建件；None=回落技能名；字体走项目 TMP 默认 zh-cn SDF）
                SkillTagLabelBuilder.Build(skillViewObj.transform, skillData,
                    详情标签宽, 详情标签高, 详情标签底距, 详情标签字号);
            }
        }
    }

}


