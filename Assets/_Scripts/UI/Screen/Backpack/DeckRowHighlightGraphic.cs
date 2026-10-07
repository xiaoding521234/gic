using UnityEngine;
using UnityEngine.UI;

namespace GIC.UI
{
    /// <summary>
    /// 卡组行当前高亮框的自绘 Graphic（docs/18 决策五十二）：
    /// 手动生成全矩形 quad、UV 明确 0,0→1,1——**不依赖 Sprite**。
    /// 实证背景（2026-10-07）：DeckRow 的 CurrentHighlight 原是「无 sprite 的 Image」，动画期换自定义
    /// shader 材质后渲染输出不上屏（shader 满格态像素测量无金——A/B 双对照皆不出，仅默认材质对照组可见）；
    /// 项目先例 CardLightBandEffect 同样绕开 Image 用自绘 Graphic（作者注释「不依赖 Sprite」）——本类同模式。
    /// 颜色（color 属性=顶点色）承载金色 tint，shader=UI/DeckRowHighlight（填充/退去过渡）。
    /// </summary>
    public class DeckRowHighlightGraphic : Graphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect rect = rectTransform.rect;
            float x0 = rect.xMin, y0 = rect.yMin, x1 = rect.xMax, y1 = rect.yMax;

            vh.AddVert(new Vector3(x0, y0), color, new Vector2(0, 0));
            vh.AddVert(new Vector3(x0, y1), color, new Vector2(0, 1));
            vh.AddVert(new Vector3(x1, y1), color, new Vector2(1, 1));
            vh.AddVert(new Vector3(x1, y0), color, new Vector2(1, 0));
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(0, 2, 3);
        }
    }
}
