using UnityEngine;
using UnityEngine.UI;

namespace GIC.UI
{
    /// <summary>粘性头部钳制 — 详情面板滚动时头部块最多让出「固定保留高度」后钉住、下半恒显（2026-10-09 拍板）。
    /// 双层视差：Top（keep=半高）与 HeaderBlock（keep=Top半高+立绘半高）各挂一个实例，链式钉住。
    /// 挂载对象为 Content 内 ignoreLayout 绝对定位组；随滚动区 onValueChanged 逐帧随动，换卡归顶随复位事件自动回正。</summary>
    public class StickyHeaderClamp : MonoBehaviour
    {
        [Header("粘性头部")]
        [SerializeField] private ScrollRect 滚动区;
        [Tooltip("头部块保留高度：滚出让出量达到该值后钉住（Top=名称栏半高 40.25；HeaderBlock=40.25+立绘半高 166.31）")]
        [SerializeField] private float 固定保留高度 = 166.31f;

        private RectTransform _headerBlock;
        private Vector2 _authoredPos;

        private void Awake()
        {
            _headerBlock = (RectTransform)transform;
            // 钳制在 authored 位置上叠加 shift：X/Y 都必须保留 authored 值——
            // HeaderBlock authored=(0,0) 恰好成立使旧「直接写 (0,shift)」侥幸无害；
            // Top 栏 authored=(339.99,-40.25)，旧写法把名称栏抹到 Content 左上角+顶出半高（2026-10-09 报障实证）
            _authoredPos = _headerBlock.anchoredPosition;
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
            _headerBlock.anchoredPosition = new Vector2(_authoredPos.x, _authoredPos.y + shift);
        }
    }
}
