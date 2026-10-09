using UnityEngine;
using UnityEngine.UI;

namespace GIC.UI
{
    /// <summary>粘性头部钳制 — 详情面板滚动时头部块（立绘/物品图/星级/分隔线）最多让出一半高度后钉住，
    /// 下半恒显（2026-10-09 拍板）。挂在 Content 内的 HeaderBlock（ignoreLayout 绝对定位组）上；
    /// 随滚动区 onValueChanged 逐帧随动，换卡归顶时随复位事件自动回正。</summary>
    public class StickyHeaderClamp : MonoBehaviour
    {
        [Header("粘性头部")]
        [SerializeField] private ScrollRect 滚动区;
        [Tooltip("头部块保留高度：滚出让出量达到该值后钉住（默认=头部块半高 166.31）")]
        [SerializeField] private float 固定保留高度 = 166.31f;

        private RectTransform _headerBlock;

        private void Awake()
        {
            _headerBlock = (RectTransform)transform;
            if (滚动区 != null)
                滚动区.onValueChanged.AddListener(OnScroll);
        }

        private void OnDestroy()
        {
            if (滚动区 != null)
                滚动区.onValueChanged.RemoveListener(OnScroll);
        }

        private void OnScroll(Vector2 _)
        {
            // 向下滚动的让出量：Content 顶缘高出视口顶的距离（未滚=0，向下滚=正）
            float offset = 滚动区.content.anchoredPosition.y;
            // 让出量未达保留高度：随内容自然滚动（偏移 0）；超过：向下拉回差值钉住，头部下半恒显
            float shift = Mathf.Min(0f, 固定保留高度 - offset);
            _headerBlock.anchoredPosition = new Vector2(0f, shift);
        }
    }
}
