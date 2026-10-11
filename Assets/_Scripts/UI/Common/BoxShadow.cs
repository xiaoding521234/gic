// BoxShadow.cs - 按钮软阴影（仿 CSS box-shadow，docs/17 §5.5 / docs/18 决策六十二）
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Pool;

namespace GIC.UI
{
    /// <summary>
    /// 网页 CSS box-shadow 的 UGUI 复刻：以「偏移」为圆心、「模糊半径」为半径的圆盘上，
    /// 把本 Graphic 的 mesh 复制 K 份（Vogel 螺旋均匀采样 + 高斯核权重分解每份 alpha），
    /// 画家算法排在原图顶点之前——零新增 draw call，阴影自动跟随任意 sprite 形状
    /// （九宫/圆形/异形通用，无需任何阴影素材）。浓度语义：底缘可见峰值（屏幕感知值）
    /// = 阴影颜色.a（Gamma 含 URP Linear→sRGB 编码补偿，2026-10-11 渲染探针定标）。
    /// 成本：顶点数 = 原图 × (模糊层数+1)；层数越多越柔。按下变色时阴影随整体 tint
    /// （渲染器级乘法，预期行为）；视口 Mask 会裁掉出界阴影（同网页 overflow:hidden）。
    /// 模糊层数&lt;2 或模糊半径=0 时退化为 ugui Shadow 同款单份硬投影。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("UI/Effects/BoxShadow 按钮阴影", 81)]
    public class BoxShadow : BaseMeshEffect
    {
        [Header("按钮阴影（CSS box-shadow）")]
        [SerializeField] private Color 阴影颜色 = new Color(0f, 0f, 0f, 0.22f);
        [SerializeField] private Vector2 偏移 = new Vector2(0f, -2.5f); // 负 y = 向下（UI 系，与 ugui Shadow 同语义）
        [SerializeField] private float 模糊半径 = 4f;
        [SerializeField, Range(2, 16)] private int 模糊层数 = 8;
        [SerializeField] private bool 乘图形Alpha = true;

        private const float GoldenAngle = 2.39996323f; // 黄金角（弧度）
        // 底缘峰值浓度校准（含 URP Linear 管线 sRGB 编码补偿：线性域混合后感知浓度≈设计值×0.46，
        // 2026-10-11 渲染探针实测 0.153 设计→0.075 实测逐位吻合，故按 1-(1-A)^2.2 补偿）
        private const float Gamma = 2.7f;
        private const float MaxParam = 600f;

        public Color 阴影颜色P
        {
            get => 阴影颜色;
            set { 阴影颜色 = value; Dirty(); }
        }

        public Vector2 偏移P
        {
            get => 偏移;
            set
            {
                value.x = Mathf.Clamp(value.x, -MaxParam, MaxParam);
                value.y = Mathf.Clamp(value.y, -MaxParam, MaxParam);
                if (偏移 == value) return;
                偏移 = value; Dirty();
            }
        }

        public float 模糊半径P
        {
            get => 模糊半径;
            set
            {
                value = Mathf.Clamp(value, 0f, MaxParam);
                if (Mathf.Approximately(模糊半径, value)) return;
                模糊半径 = value; Dirty();
            }
        }

        public int 模糊层数P
        {
            get => 模糊层数;
            set
            {
                模糊层数 = Mathf.Clamp(value, 2, 16);
                Dirty();
            }
        }

        private void Dirty()
        {
            if (graphic != null) graphic.SetVerticesDirty();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            偏移P = 偏移;
            模糊半径P = 模糊半径;
            base.OnValidate();
            Dirty();
        }
#endif

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0 || 阴影颜色.a <= 0f)
                return;

            var original = ListPool<UIVertex>.Get();
            var output = ListPool<UIVertex>.Get();
            try
            {
                vh.GetUIVertexStream(original);
                vh.Clear();

                int count = original.Count;
                bool soft = 模糊层数 >= 2 && 模糊半径 > 0f;
                int copies = soft ? 模糊层数 : 1;

                // 每份 alpha：高斯核权重（σ=模糊半径/2）按 Vogel 半径取值后归一化，
                // a_j = 浓度 × w_j × Gamma（小 alpha 线性近似，全叠合成≈目标浓度）；
                // 单份退化=直接目标浓度（等价 ugui Shadow 硬投影语义）。
                var alphas = new float[copies];
                if (soft)
                {
                    float sigma = 模糊半径 * 0.5f;
                    float sum = 0f;
                    for (int j = 1; j <= copies; j++)
                    {
                        float r = 模糊半径 * Mathf.Sqrt((float)j / copies);
                        alphas[j - 1] = Mathf.Exp(-(r * r) / (2f * sigma * sigma));
                        sum += alphas[j - 1];
                    }
                    float total = 阴影颜色.a * Gamma;
                    for (int j = 0; j < copies; j++)
                        alphas[j] = Mathf.Min(1f, total * alphas[j] / sum);
                }
                else
                {
                    alphas[0] = 阴影颜色.a;
                }

                var baseColor = (Color32)阴影颜色;
                for (int j = 0; j < copies; j++)
                {
                    float ox = 偏移.x, oy = 偏移.y;
                    if (soft)
                    {
                        float r = 模糊半径 * Mathf.Sqrt((float)(j + 1) / copies);
                        float theta = (j + 1) * GoldenAngle;
                        ox += r * Mathf.Cos(theta);
                        oy += r * Mathf.Sin(theta);
                    }

                    var col = baseColor;
                    col.a = (byte)Mathf.Round(255f * alphas[j]);

                    for (int i = 0; i < count; i++)
                    {
                        UIVertex vt = original[i];
                        Vector3 p = vt.position;
                        p.x += ox;
                        p.y += oy;
                        vt.position = p;
                        var c = col;
                        if (乘图形Alpha) c.a = (byte)((c.a * vt.color.a) / 255);
                        vt.color = c;
                        output.Add(vt);
                    }
                }

                // 原图顶点排在最后：画家算法先画阴影（在下层）再画本体
                for (int i = 0; i < count; i++)
                    output.Add(original[i]);

                vh.AddUIVertexTriangleStream(output);
            }
            finally
            {
                ListPool<UIVertex>.Release(original);
                ListPool<UIVertex>.Release(output);
            }
        }
    }
}
