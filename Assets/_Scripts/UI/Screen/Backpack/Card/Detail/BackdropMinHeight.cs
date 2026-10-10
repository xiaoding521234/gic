using UnityEngine;

namespace GIC.UI
{
    /// <summary>背景板最小渲染高度 — Backdrop 高度随内容伸缩（顶贴头部块底、底=内容底+余量），
    /// 短内容卡（如两行描述的物品）自然高度过矮难看；本组件把高度钳到最小值
    /// （2026-10-09 拍板：至少 1 个立绘块高度）。挂在 Backdrop 上；父（Content）尺寸变化钩子自动重算，幂等收敛。</summary>
    public class BackdropMinHeight : MonoBehaviour
    {
        [Header("背景板最小高度")]
        [Tooltip("高度下限（默认=1 个立绘块高度 323.92）")]
        [SerializeField] private float 最小高度 = 323.92f;
        [Tooltip("自然态顶缘距内容顶（=头部总高：名称栏 80.5+立绘块 323.92+分隔线 8.7）")]
        [SerializeField] private float 顶缘距内容顶 = 413.12f;
        [Tooltip("自然态底部余量：底缘=内容底+该值")]
        [SerializeField] private float 底部余量 = 10f;

        private RectTransform _rt;

        private void Awake()
        {
            _rt = (RectTransform)transform;
        }

        private void OnEnable()
        {
            if (_rt == null) _rt = (RectTransform)transform;
            Apply();
        }

        // 父（Content）布局重建（换卡/内容高度变化）后触发，重算并幂等收敛
        private void OnRectTransformDimensionsChange()
        {
            Apply();
        }

        private void Apply()
        {
            // OnRectTransformDimensionsChange 可在 Awake 之前触发（Instantiate 期间引擎重建 RectTransform）
            if (_rt == null) _rt = (RectTransform)transform;
            var parent = _rt.parent as RectTransform;
            if (parent == null) return;
            float parentH = parent.rect.height;
            if (parentH <= 0f) return; // 排版未建（prefab 授权态/初始）——保持 authored 自然锚定值

            // 自然高度=内容高−顶缘+底余量；不足最小则抬到最小（顶缘恒定，底缘向下延展）
            float h = Mathf.Max(parentH - 顶缘距内容顶 + 底部余量, 最小高度);
            float sdY = h - parentH;                       // 锚定式：height = parentH + sizeDelta.y
            float posY = -顶缘距内容顶 + parentH / 2f - h / 2f;  // 顶缘恒=−顶缘距内容顶
            var sd = _rt.sizeDelta;
            var pos = _rt.anchoredPosition;
            if (!Mathf.Approximately(sd.y, sdY) || !Mathf.Approximately(pos.y, posY))
            {
                sd.y = sdY;
                _rt.sizeDelta = sd;
                pos.y = posY;
                _rt.anchoredPosition = pos;
            }
        }
    }
}
