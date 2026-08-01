using System.Collections;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Localization;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Battle;
using GIC.Data;
using GIC.Data.Event;
using GIC.Tool;
namespace GIC.UI
{


    public class SkillDetailView : MonoBehaviour
    {
        public GameObject skillDetailPanel;
        public TextCombiner skillType;
        public SkillIconView skillIconView;
        public TextCombiner skillName;

        public GameObject content;
        public TextCombiner skillDescription;
        public GameObject paramPrefab;

        [Header("关联面板")]
        public GameObject relatedPanel;
        public GameObject cardModeContainer;
        [Tooltip("场景中背包的 CardDetailView，用于运行时实例化副本")]
        public CardDetailView cardDetailViewTemplate;
        public GameObject ruleModeContainer;
        public TextCombiner relatedName;
        public TextCombiner relatedDescription;

        [Header("动画")]
        [SerializeField] private float slideDuration = 0.15f;
        [SerializeField] private AnimationCurve slideCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [SerializeField] private float slideOffset = 80f;

        private ViewType viewType;
        private SkillConfig.SkillData skillData;
        private UnitConfig.UnitData unitData;
        private BaseSkill skill;
        private SkillIconView sourceSkillIconView;

        private RectTransform panelRect;
        private Vector2 panelTargetPosition;
        private Coroutine slideCoroutine;

        private RectTransform relatedPanelRect;
        private Vector2 relatedPanelTargetPosition;
        private Coroutine relatedSlideCoroutine;

        private Camera uiCamera;

        private void Awake()
        {
            if (skillDetailPanel != null)
            {
                panelRect = skillDetailPanel.GetComponent<RectTransform>();
                if (panelRect != null)
                    panelTargetPosition = panelRect.anchoredPosition;
            }

            if (relatedPanel != null)
            {
                relatedPanelRect = relatedPanel.GetComponent<RectTransform>();
                if (relatedPanelRect != null)
                    relatedPanelTargetPosition = relatedPanelRect.anchoredPosition;
            }

            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                uiCamera = canvas.worldCamera;
            }
        }

        void Update()
        {
            if (!skillDetailPanel.activeSelf) return;

            if (Input.GetMouseButtonDown(0))
            {
                // 检测技能描述中的链接
                if (IsPointerOverLink(skillDescription, out string linkId))
                {
                    OnLinkClicked(linkId);
                    return;
                }

                // 检测规则描述中的嵌套链接（RuleMode 下）
                if (relatedPanel.activeSelf && ruleModeContainer.activeSelf)
                {
                    if (IsPointerOverLink(relatedDescription, out string nestedLinkId))
                    {
                        OnLinkClicked(nestedLinkId);
                        return;
                    }
                }

                bool isClickOnSourceIcon = IsPointerOverSourceIcon();

                if (isClickOnSourceIcon)
                {
                    ClosePanel();
                    sourceSkillIconView.OnReselect();
                    return;
                }

                if (!IsPointerOverUI(skillDetailPanel) && !IsPointerOverUI(relatedPanel))
                {
                    ClosePanel();
                    sourceSkillIconView.SetSelected(false);
                }
            }
        }

        bool IsPointerOverLink(TextCombiner textCombiner, out string linkId)
        {
            linkId = null;

            if (textCombiner == null) return false;

            var tmpText = textCombiner.GetComponent<TextMeshProUGUI>();
            if (tmpText == null) return false;

            int linkIndex = TMP_TextUtilities.FindIntersectingLink(tmpText, Input.mousePosition, uiCamera);

            if (linkIndex != -1)
            {
                TMP_LinkInfo linkInfo = tmpText.textInfo.linkInfo[linkIndex];
                linkId = linkInfo.GetLinkID();
                return true;
            }

            return false;
        }

        void OnLinkClicked(string linkId)
        {
            var (type, id) = LinkParser.Parse(linkId);

            switch (type)
            {
                case "Unit":
                    if (Enum.TryParse<UnitName>(id, out var unitName))
                        ShowCardMode(new CardId(unitName));
                    break;
                case "Item":
                    if (Enum.TryParse<ItemName>(id, out var itemName))
                        ShowCardMode(new CardId(itemName));
                    break;
                case "Concept":
                default:
                    ShowRuleMode(id);
                    break;
            }
        }

        #region 关联面板 — 卡片模式

        private CardDetailView _cardDetailClone;

        void ShowCardMode(CardId cardId)
        {
            // 清理上一次的副本
            ClearCardDetailClone();

            // 从场景中已配好的 CardDetailView 实例化副本
            var cloneGO = Instantiate(cardDetailViewTemplate.gameObject, cardModeContainer.transform, false);
            var cloneRect = cloneGO.GetComponent<RectTransform>();
            cloneRect.anchorMin = Vector2.zero;
            cloneRect.anchorMax = Vector2.one;
            cloneRect.offsetMin = Vector2.zero;
            cloneRect.offsetMax = Vector2.zero;

            _cardDetailClone = cloneGO.GetComponent<CardDetailView>();

            // 副本的 UnitDetailPanel.skillDetailView 指向当前 SkillDetailView（Layer 2）
            var unitPanel = _cardDetailClone.GetComponentInChildren<UnitDetailPanel>(true);
            if (unitPanel != null)
                unitPanel.skillDetailView = this;

            cardModeContainer.SetActive(true);
            ruleModeContainer.SetActive(false);

            var saveData = new SaveCardData { id = cardId, count = 1, skin = 0 };
            _cardDetailClone.Init(saveData);

            ShowRelatedPanel();
        }

        void ClearCardDetailClone()
        {
            if (_cardDetailClone != null)
            {
                Destroy(_cardDetailClone.gameObject);
                _cardDetailClone = null;
            }
        }

        #endregion

        #region 关联面板 — 规则模式

        void ShowRuleMode(string id)
        {
            cardModeContainer.SetActive(false);
            ruleModeContainer.SetActive(true);

            relatedName.ClearAllEntries();
            relatedName.AddEntry(new LocalizedString(TableName.RelatedName.ToString(), id));

            relatedDescription.textProcessor = null;
            relatedDescription.ClearAllEntries();
            relatedDescription.AddEntry(new LocalizedString(TableName.RelatedDescription.ToString(), id));

            ShowRelatedPanel();
        }

        #endregion

        bool IsPointerOverSourceIcon()
        {
            if (sourceSkillIconView == null) return false;
            GameObject target = sourceSkillIconView.gameObject;
            if (target == null || !target.activeInHierarchy) return false;
            RectTransform rectTransform = target.GetComponent<RectTransform>();
            if (rectTransform == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(rectTransform, Input.mousePosition, null);
        }

        bool IsPointerOverUI(GameObject target)
        {
            if (target == null || !target.activeInHierarchy) return false;
            RectTransform rectTransform = target.GetComponent<RectTransform>();
            if (rectTransform == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(rectTransform, Input.mousePosition, null);
        }

        public void ClosePanel()
        {
            if (slideCoroutine != null)
                StopCoroutine(slideCoroutine);
            if (relatedSlideCoroutine != null)
                StopCoroutine(relatedSlideCoroutine);

            ClearCardDetailClone();
            skillDetailPanel.SetActive(false);
            relatedPanel.SetActive(false);
        }

        private void CloseRelatedPanel()
        {
            if (relatedSlideCoroutine != null)
                StopCoroutine(relatedSlideCoroutine);

            ClearCardDetailClone();
            relatedPanel.SetActive(false);
        }

        public void OpenPanel()
        {
            skillDetailPanel.SetActive(true);
            relatedPanel.SetActive(false);

            if (slideCoroutine != null)
                StopCoroutine(slideCoroutine);
            slideCoroutine = StartCoroutine(SlideInAnimation(panelRect, panelTargetPosition));
        }

        private void ShowRelatedPanel()
        {
            relatedPanel.SetActive(true);

            if (relatedSlideCoroutine != null)
                StopCoroutine(relatedSlideCoroutine);
            relatedSlideCoroutine = StartCoroutine(SlideInAnimation(relatedPanelRect, relatedPanelTargetPosition));

            RefreshLayout();
        }

        private IEnumerator SlideInAnimation(RectTransform rectTransform, Vector2 targetPosition)
        {
            if (rectTransform == null) yield break;

            float elapsed = 0f;
            Vector2 startPos = targetPosition + Vector2.left * slideOffset;

            while (elapsed < slideDuration)
            {
                elapsed += Time.deltaTime;
                float t = slideCurve.Evaluate(elapsed / slideDuration);
                rectTransform.anchoredPosition = Vector2.Lerp(startPos, targetPosition, t);
                yield return null;
            }

            rectTransform.anchoredPosition = targetPosition;
        }

        public void InitWithData(SkillConfig.SkillData skillData, UnitConfig.UnitData unitData, SkillIconView sourceSkillIconView)
        {
            viewType = ViewType.Display;
            this.skillData = skillData;
            this.unitData = unitData;

            // 如果关联面板开着，关掉（从 CardMode 内的技能图标跳转过来的场景）
            CloseRelatedPanel();

            // 技能类型
            skillType.ClearAllEntries();
            skillType.AddEntry(skillData.skillType.GetEntry());

            skillIconView.InitWithData(skillData, unitData, ViewType.OnlyDisplay, this);

            // 技能名称
            skillName.ClearAllEntries();
            skillName.AddEntry(skillData.skillID.GetEntry());

            // 技能描述
            // 先设置处理器（AddEntry 内部会同步触发 RefreshString → UpdateDisplay，必须在之前设置）
            skillDescription.textProcessor = (text) =>
                SkillDescriptionBuilder.Build(text, skillData.customParams);
            skillDescription.ClearAllEntries();
            skillDescription.AddEntry(skillData.GetDescriptionEntry());

            this.sourceSkillIconView = sourceSkillIconView;
            SetParamDisplay(skillData);
            RefreshLayout();
        }

        public void InitWithSkill(BaseSkill skill, SkillIconView sourceSkillIconView)
        {
            viewType = ViewType.Actual;
            this.skill = skill;
            RefreshLayout();
        }

        private void SetParamDisplay(SkillConfig.SkillData skillData)
        {
            for (int i = 0; i < content.transform.childCount; i++)
            {
                var child = content.transform.GetChild(i);
                if (child.GetComponent<ParamView>() != null)
                {
                    Destroy(child.gameObject);
                }
            }

            foreach (var param in skillData.customParams)
            {
                GameObject paramView = Instantiate(paramPrefab, content.transform);
                paramView.GetComponent<ParamView>().InitWithParam(param.GetNameEntry(), param.GetValueEntry());
            }
        }

        private void RefreshLayout()
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content.GetComponent<RectTransform>());
            if (relatedPanel != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(relatedPanel.GetComponent<RectTransform>());
            }
        }
    }

}


