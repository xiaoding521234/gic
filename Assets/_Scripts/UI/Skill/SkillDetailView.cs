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
        [Tooltip("CardModeContainer 下静态预设的只读卡牌详情（场景接线，替代旧版运行时克隆）")]
        public CardDetailView cardDetailView;
        public GameObject ruleModeContainer;
        public TextCombiner relatedName;
        public TextCombiner relatedDescription;

        [Header("动画")]
        [SerializeField] private float slideDuration = 0.15f;
        [SerializeField] private AnimationCurve slideCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [SerializeField] private float slideOffset = 80f;

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

                bool isClickOnSourceIcon = IsPointerOverRect(sourceSkillIconView != null ? sourceSkillIconView.gameObject : null);

                if (isClickOnSourceIcon)
                {
                    ClosePanel();
                    sourceSkillIconView.OnReselect();
                    return;
                }

                if (!IsPointerOverRect(skillDetailPanel) && !IsPointerOverRect(relatedPanel))
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

        #region relatedPanel — cardMode

        void ShowCardMode(CardId cardId)
        {
            cardModeContainer.SetActive(true);
            ruleModeContainer.SetActive(false);

            // 静态预设实例默认未激活，进入卡片模式时显式激活（旧克隆流程 Instantiate 出来即为激活态）
            cardDetailView.gameObject.SetActive(true);
            // 静态预设实例重复 Init 即可（Init 为幂等重置：重建标签芯片/技能图标/子面板切换）
            var saveData = new SaveCardData { id = cardId, count = 1, skin = 0 };
            cardDetailView.Init(saveData);

            ShowRelatedPanel();
        }

        #endregion

        #region relatedPanel — strictMode

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

        /// <summary>本帧指针是否落在指定 RectTransform 上（2026-09-13 P3 改名：原 IsPointerOverUI 撞名 GestureHub 的
        /// "任意 UI"命中门——本方法实为"指定矩形内"判定，语义纠偏，docs/24 §5）</summary>
        bool IsPointerOverRect(GameObject target)
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

            skillDetailPanel.SetActive(false);
            relatedPanel.SetActive(false);
        }

        private void CloseRelatedPanel()
        {
            if (relatedSlideCoroutine != null)
                StopCoroutine(relatedSlideCoroutine);

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


