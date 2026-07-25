using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Localization;
using UnityEngine.UI;

public class SkillDetailView : MonoBehaviour
{
    public GameObject skillDetailPanel;
    public TextCombiner skillType;
    public SkillIconView skillIconView;
    public TextCombiner skillName;

    public GameObject content;
    public TextCombiner skillDescription;
    public GameObject paramPrefab;

    public GameObject relatedPanel;
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
            // 检测是否点击了描述文本中的链接
            if (IsPointerOverLink(out string linkId))
            {
                OnLinkClicked(linkId);
                return;
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

    bool IsPointerOverLink(out string linkId)
    {
        linkId = null;
        
        if (skillDescription == null) return false;
        
        // 获取 skillDescription 的 TextMeshProUGUI 组件来检测链接
        var tmpText = skillDescription.GetComponent<TextMeshProUGUI>();
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
        // 根据 linkId 从 RelatedName / RelatedDescription 表中取本地化文本
        relatedName.ClearAllEntries();
        relatedName.AddEntry(new LocalizedString(TableName.RelatedName.ToString(), linkId));

        relatedDescription.ClearAllEntries();
        relatedDescription.AddEntry(new LocalizedString(TableName.RelatedDescription.ToString(), linkId));

        ShowRelatedPanel();
    }

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
            
        skillDetailPanel.SetActive(false);
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

        // 技能类型
        skillType.ClearAllEntries();
        skillType.AddEntry(skillData.skillType.GetEntry());

        skillIconView.InitWithData(skillData, unitData, ViewType.OnlyDisplay, this);

        // 技能名称
        skillName.ClearAllEntries();
        skillName.AddEntry(skillData.skillID.GetEntry());

        // 技能描述
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
        foreach (Transform child in content.transform)
        {
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
        // 先强制更新所有 Canvas，确保 SetActive 生效后再重建布局
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content.GetComponent<RectTransform>());
        if (relatedPanel != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(relatedPanel.GetComponent<RectTransform>());
        }
    }
}