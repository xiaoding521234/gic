using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 自动换行布局组 — 子元素按从左到右排列，超出容器宽度时自动换行
/// </summary>
[AddComponentMenu("Layout/Wrap Layout Group")]
public class WrapLayoutGroup : LayoutGroup
{
    [SerializeField] private float m_Spacing = 6f;
    [SerializeField] private float m_LineSpacing = 6f;

    private float m_TotalPreferredHeight;

    public float spacing
    {
        get => m_Spacing;
        set => SetProperty(ref m_Spacing, value);
    }

    public float lineSpacing
    {
        get => m_LineSpacing;
        set => SetProperty(ref m_LineSpacing, value);
    }

    public override void CalculateLayoutInputHorizontal()
    {
        base.CalculateLayoutInputHorizontal();
    }

    public override void CalculateLayoutInputVertical()
    {
        m_TotalPreferredHeight = CalculatePreferredHeight();
    }

    public override float minHeight => m_TotalPreferredHeight;
    public override float preferredHeight => m_TotalPreferredHeight;
    public override float flexibleHeight => 0;

    public override void SetLayoutHorizontal()
    {
        // 不在此设置子元素位置 — 留给 SetLayoutVertical 统一处理
    }

    public override void SetLayoutVertical()
    {
        float innerWidth = rectTransform.rect.width - padding.left - padding.right;
        float x = padding.left;
        float y = padding.top;
        float lineHeight = 0f;

        for (int i = 0; i < rectChildren.Count; i++)
        {
            var child = rectChildren[i];
            float childWidth = LayoutUtility.GetPreferredWidth(child);
            float childHeight = LayoutUtility.GetPreferredHeight(child);

            // 检查是否需要换行
            if (x + childWidth > innerWidth + padding.left + 0.01f && x > padding.left)
            {
                x = padding.left;
                y += lineHeight + m_LineSpacing;
                lineHeight = 0f;
            }

            SetChildAlongAxis(child, 0, x, childWidth);
            SetChildAlongAxis(child, 1, y, childHeight);

            x += childWidth + m_Spacing;
            lineHeight = Mathf.Max(lineHeight, childHeight);
        }
    }

    private float CalculatePreferredHeight()
    {
        float innerWidth = rectTransform.rect.width - padding.left - padding.right;
        float x = padding.left;
        float y = padding.top;
        float lineHeight = 0f;

        for (int i = 0; i < rectChildren.Count; i++)
        {
            var child = rectChildren[i];
            float childWidth = LayoutUtility.GetPreferredWidth(child);
            float childHeight = LayoutUtility.GetPreferredHeight(child);

            if (x + childWidth > innerWidth + padding.left + 0.01f && x > padding.left)
            {
                x = padding.left;
                y += lineHeight + m_LineSpacing;
                lineHeight = 0f;
            }

            x += childWidth + m_Spacing;
            lineHeight = Mathf.Max(lineHeight, childHeight);
        }

        return y + lineHeight + padding.bottom;
    }
}
