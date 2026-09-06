using UnityEngine;

namespace GIC.Tool
{
    /// <summary>
    /// 面板尺寸自适应（通用组件，2026-09-06 从卡组面板提取）：
    /// 项目 CanvasScaler=ScaleWithScreenSize+纯匹配宽度（参考 2560×1440）——超宽分辨率
    /// （如 3200×1440，垂直可用仅 1440×2560/3200=1152 单位）下，写死首选尺寸的居中面板会溢出屏幕。
    /// 本组件在 Apply() 时按当前画布空间收敛目标 RectTransform：size = min(首选尺寸, 画布空间-边距)。
    /// 目标内部元素应锚定其边缘，尺寸变化时自动跟随。
    /// 用法：挂在任意子物体上（目标默认=自身 RectTransform），宿主在面板打开/分辨率变化后调 Apply()。
    /// </summary>
    public class PanelFitToCanvas : MonoBehaviour
    {
        [Tooltip("自适应目标（默认=自身 RectTransform）")]
        [InspectorName("目标面板")]
        [SerializeField] private RectTransform targetRect;
        [Tooltip("面板首选尺寸（画布空间足够时用这个）")]
        [InspectorName("首选宽度")]
        [SerializeField] private float preferredWidth = 2200f;
        [InspectorName("首选高度")]
        [SerializeField] private float preferredHeight = 1380f;
        [Tooltip("与画布边缘保留的余量（UI 单位）")]
        [InspectorName("边缘余量")]
        [SerializeField] private float edgeMargin = 48f;

        /// <summary>按当前画布空间收敛目标尺寸（幂等）。宿主在面板打开/分辨率变化后调用。</summary>
        public void Apply()
        {
            var rt = targetRect != null ? targetRect : (RectTransform)transform;
            var canvas = GetComponentInParent<Canvas>();
            if (rt == null || canvas == null) return;

            var canvasRt = (RectTransform)canvas.transform;
            float w = Mathf.Min(preferredWidth, canvasRt.rect.width - edgeMargin);
            float h = Mathf.Min(preferredHeight, canvasRt.rect.height - edgeMargin);
            rt.sizeDelta = new Vector2(w, h);
        }
    }
}
