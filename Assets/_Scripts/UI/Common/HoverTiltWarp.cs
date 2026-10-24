using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GIC.UI
{
    /// <summary>
    /// 悬停透视 Warp（2026-10-24 返修九——悬停朝向「有时正确有时相反」根因修复；返修十——果冻感细分修复）。
    ///
    /// 根因（数学定案）：Overlay 正交画布上纯旋转是 **180° 对称**的——
    /// Quaternion.Euler(+a,+b) 与 Euler(−a,−b) 的正交投影逐顶点全同（两轴投影各自只含 cos 项、
    /// xy 交叉项 sin(a)sin(b) 双负不变），故「光标角」与「对角」两种悬停渲染完全一致；
    /// 光泽带 _Progress=(nx+ny)/2 同样 180° 不变（反对角撞值 0.5）。方向感只剩光泽带半个自由度
    /// + 脑补——用户报障「有时这角低下去（对），有时却相反」即此；中心点/首入位置假设均排除
    /// （归一是活体逐帧重算的，返修五已根治）。
    ///
    /// 商业成熟做法（调研=.codely-cli/webrefs/card-hover-feel/NOTES.md+返修九增补）全部用**真透视**承载方向：
    /// - vanilla-tilt.js（4014★ Web 事实标准，一手源码）：transform 链 = perspective(1000px) rotateX rotateY scale3d；
    /// - Atropos（3611★，一手源码）：容器 CSS perspective:1200px + transform-style:preserve-3d；
    /// - Marvel Snap / MTG Arena / LoR = 真 3D 透视场景（NOTES.md 实机观察）；
    /// - 原神七圣召唤（正交 2D）= 索性不做倾斜（抬起+放大+边框微光）。
    /// 正交纯旋转 = 商业真空区——本组件把「透视」补进 UGUI 正交画布：对已倾斜卡面上每个顶点，按
    /// 「虚拟透视相机（pivot 平面 −Z 侧、距离 d）的投影」做平面内位移（近缘放大/远缘缩小=Keystone 梯形），
    /// 数学上与真透视渲染逐点一致——对角两角悬停从此渲染互异，「光标角压下」恒可读（返修六拍板语义不变）。
    ///
    /// 返修十（2026-10-24 用户报障「观感像果冻」）：正交光栅化=**仿射插值**——梯形 Warp 后大四边形的
    /// 贴图沿三角形对角线弯折（真透视靠 GPU 的 w 除法做透视校正采样，正交画布无此机制=经典 PS1 弯曲；
    /// 卡面立绘对角弯折+折痕≈画在橡胶上）。修=**自适应细分**：位移前把每个三角形按投影跨度细分
    /// （大跨 2 级=仿射误差 1/16≈亚像素、中跨 1 级、小跨不动；细分中点同样逐顶点位移=精确透视像点），
    /// 仿射误差随三角形边长平方衰减——细分后整张卡（轮廓+贴图）与真透视渲染逐像素一致。
    /// TMP 不细分（glyph 跨度小误差恒亚像素，且 meshInfo 数组归 TMP 管理）。
    ///
    /// 接线：HoverTiltEffect.EnsureWarps 悬停期懒挂到宿主子树每个 Graphic（含 TMP 文本/遮罩模板图/光泽层），
    /// 勿手工挂载。双路径（TMP 3.0.9 全包源码实证不走 IMeshModifier，ModifyMesh 对 TMP 永不调用）：
    /// - Image 家族：BaseMeshEffect.ModifyMesh（UGUI 原生顶点修改器管线）；
    /// - TextMeshProUGUI：文档化事件 OnPreRenderText（网格生成后、上传前）抓原始顶点+就地 Warp；
    ///   倾斜变化无重生成时=从原始快照重施+UpdateVertexData 直传。
    ///
    /// 稳定性设计：
    /// - 射线域零扰动：只动网格顶点不动 RectTransform——命中区域=未形变矩形（边缘不因形变抖动触发 enter/exit 循环）；
    /// - 平移/等比缩放不失效：位移场以 pivot 为锚、以局部坐标表达，且 d 随 lossyScale 等比伸缩
    ///   （k=D/(D+z) 对等比缩放不变）——父级平移/等比缩放后旧网格依然数学正确；仅旋转需重刷
    ///   （驱动写完旋转必 MarkDirty；拖拽期 Suspend 已还原=拖拽速度倾斜照旧行为零冲突）；
    /// - 模板遮罩随卡面：Card.prefab 的 CardMask=Image+Mask（stencil）——模板网格同经本组件位移，裁切随卡面一致形变；
    /// - 平置零成本：全网格无显著位移时 ModifyMesh 原样返回（不细分不位移——非悬停卡重建保持原始拓扑，
    ///   悬停结束后首次重建自动回到细分前网格）。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Graphic))]
    public class HoverTiltWarp : BaseMeshEffect
    {
        public HoverTiltEffect Driver;

        private TextMeshProUGUI _tmp;
        private Vector3[][] _pristine;   // TMP：textInfo.meshInfo[i].vertices 的未 Warp 快照（每次重生成重抓）
        private bool _pristineValid;

        private static readonly List<UIVertex> _stream = new List<UIVertex>(64); // 复用勿逐帧分配

        protected override void OnEnable()
        {
            base.OnEnable();
            _tmp = GetComponent<TextMeshProUGUI>();
            if (_tmp != null)
            {
                _tmp.OnPreRenderText += OnTmpPreRender;
                // 池化复用防线：启用即强制重生成——事件里重抓原始顶点并按当前姿态回写
                // （否则池里带角度回池的旧 Warp 网格可能在文本未变时残留在 canvasRenderer 上）
                _tmp.SetAllDirty();
            }
        }

        protected override void OnDisable()
        {
            if (_tmp != null) _tmp.OnPreRenderText -= OnTmpPreRender;
            _pristine = null;
            _pristineValid = false;
            base.OnDisable();
        }

        /// <summary>驱动写完倾斜角后调用。Image 族=脏标记走重建管线（重建时 ModifyMesh）；
        /// TMP=从原始快照重施当前 Warp 并直传（0 倾角/透视关闭=按原始回写=还原）</summary>
        public void MarkDirty()
        {
            if (!IsActive()) return;
            if (_tmp == null)
            {
                if (graphic != null) graphic.SetVerticesDirty();
                return;
            }
            ApplyTmpWarp();
        }

        // ==================== Image 家族路径 ====================

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || Driver == null) return;
            var root = Driver.transform;
            if (root == null) return;
            float d = Driver.透视距离世界;
            if (d <= 0.01f) return;
            Matrix4x4 l2w = transform.localToWorldMatrix;
            Matrix4x4 w2l = transform.worldToLocalMatrix;
            Vector3 pivotW = root.position;
            int count = vh.currentVertCount;
            if (count <= 0) return;

            // 返修十：显著透视位移时先细分再逐顶点位移（贴图仿射弯折=果冻感根源）
            if (HasSignificantDepth(vh, l2w, pivotW, d))
                SubdivideAndEmit(vh, l2w);

            count = vh.currentVertCount;
            UIVertex v = default;
            for (int i = 0; i < count; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                if (Displace(ref v.position, l2w, w2l, pivotW, d)) vh.SetUIVertex(v, i);
            }
        }

        /// <summary>平置快速路径：全网格透视深度可忽略（|z|/d ≤ 0.0005≈位移 0.06px）时不动原始网格</summary>
        private static bool HasSignificantDepth(VertexHelper vh, Matrix4x4 l2w, Vector3 pivotW, float d)
        {
            int count = vh.currentVertCount;
            UIVertex v = default;
            for (int i = 0; i < count; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                Vector3 rel = l2w.MultiplyPoint3x4(v.position) - pivotW;
                if (Mathf.Abs(rel.z) > d * 0.0005f) return true;
            }
            return false;
        }

        /// <summary>按投影跨度自适应细分：重读三角形流→逐三角细分级数（大跨 2/中跨 1/小跨 0）→重建网格。
        /// 逐三角独立细分（共享边重复顶点）：位置逐点一致无裂纹；重建始终从 OnPopulateMesh 原始顶点出发=无累积。</summary>
        private static void SubdivideAndEmit(VertexHelper vh, Matrix4x4 l2w)
        {
            _stream.Clear();
            vh.GetUIVertexStream(_stream);
            vh.Clear();
            int triCount = _stream.Count / 3;
            for (int i = 0; i < triCount; i++)
            {
                var a = _stream[i * 3];
                var b = _stream[i * 3 + 1];
                var c = _stream[i * 3 + 2];
                Vector3 wa = l2w.MultiplyPoint3x4(a.position);
                Vector3 wb = l2w.MultiplyPoint3x4(b.position);
                Vector3 wc = l2w.MultiplyPoint3x4(c.position);
                float span = Mathf.Max(
                    Mathf.Max((wa - wb).magnitude, (wb - wc).magnitude),
                    (wa - wc).magnitude);
                int depth = span > 96f ? 2 : (span > 24f ? 1 : 0);
                EmitSubdivided(vh, a, b, c, depth);
            }
        }

        private static void EmitSubdivided(VertexHelper vh, UIVertex a, UIVertex b, UIVertex c, int depth)
        {
            if (depth <= 0)
            {
                vh.AddVert(a);
                vh.AddVert(b);
                vh.AddVert(c);
                int i0 = vh.currentVertCount - 3;
                vh.AddTriangle(i0, i0 + 1, i0 + 2);
                return;
            }
            var ab = Mid(a, b);
            var bc = Mid(b, c);
            var ca = Mid(c, a);
            EmitSubdivided(vh, a, ab, ca, depth - 1);
            EmitSubdivided(vh, ab, b, bc, depth - 1);
            EmitSubdivided(vh, ca, bc, c, depth - 1);
            EmitSubdivided(vh, ab, bc, ca, depth - 1);
        }

        /// <summary>全通道中点（位置/法线/切线/颜色/uv0~3——颜色与 uv 在平面上线性，细分中点=正确插值）</summary>
        private static UIVertex Mid(UIVertex a, UIVertex b)
        {
            var v = new UIVertex();
            v.position = (a.position + b.position) * 0.5f;
            v.normal = (a.normal + b.normal) * 0.5f;
            v.tangent = (a.tangent + b.tangent) * 0.5f;
            v.color = Color32.Lerp(a.color, b.color, 0.5f);
            v.uv0 = (a.uv0 + b.uv0) * 0.5f;
            v.uv1 = (a.uv1 + b.uv1) * 0.5f;
            v.uv2 = (a.uv2 + b.uv2) * 0.5f;
            v.uv3 = (a.uv3 + b.uv3) * 0.5f;
            return v;
        }

        // ==================== TMP 路径 ====================

        private void OnTmpPreRender(TMP_TextInfo info)
        {
            if (info == null) return;
            var meshInfo = info.meshInfo;
            if (_pristine == null || _pristine.Length != meshInfo.Length)
                _pristine = new Vector3[meshInfo.Length][];
            for (int i = 0; i < meshInfo.Length; i++)
            {
                var src = meshInfo[i].vertices;
                if (src == null) { _pristine[i] = null; continue; }
                var copy = new Vector3[src.Length];
                System.Array.Copy(src, copy, src.Length);
                _pristine[i] = copy;
            }
            _pristineValid = true;
            // 事件返回后 TMP 自行上传 meshInfo——原地 Warp 即进渲染
            if (IsActive()) WarpFromPristine(info);
        }

        private void ApplyTmpWarp()
        {
            if (_tmp == null || !_pristineValid || _pristine == null) return;
            var info = _tmp.textInfo;
            if (info == null) return;
            WarpFromPristine(info);
            if (IsActive()) _tmp.UpdateVertexData();
        }

        /// <summary>按原始快照 + 当前 transform 重施 Warp（透视关闭/平置=按原始回写=还原）</summary>
        private void WarpFromPristine(TMP_TextInfo info)
        {
            var root = Driver != null ? Driver.transform : null;
            if (root == null || _pristine == null || info == null) return;
            bool active = Driver.透视生效 && IsActive();
            float d = Driver.透视距离世界;
            bool warpOn = active && d > 0.01f;
            Matrix4x4 l2w = transform.localToWorldMatrix;
            Matrix4x4 w2l = transform.worldToLocalMatrix;
            Vector3 pivotW = root.position;
            var meshInfo = info.meshInfo;
            for (int i = 0; i < _pristine.Length && i < meshInfo.Length; i++)
            {
                var src = _pristine[i];
                var dst = meshInfo[i].vertices;
                if (src == null || dst == null || dst.Length != src.Length) continue;
                for (int j = 0; j < src.Length; j++)
                {
                    var p = src[j];
                    if (warpOn) Displace(ref p, l2w, w2l, pivotW, d);
                    dst[j] = p;
                }
            }
        }

        // ==================== 共享位移核（两条路径单源；离线探针直测） ====================

        /// <summary>透视位移：顶点按「虚拟透视相机（pivot 平面 −Z 侧、距离 d）对当前已倾斜卡面的投影」
        /// 做平面内位移——理想透视像点 = pivot + rel·k（k=d/(d+z)），UGUI 正面朝 −Z 故 z&lt;0=近观众（放大）、
        /// z&gt;0=远离观众（缩小=被压下的一角）。返回 false=位移可忽略（平置快速路径）。
        /// public=桥脚本探针同可访问（独立程序集读不到 internal）。</summary>
        public static bool Displace(ref Vector3 local, Matrix4x4 l2w, Matrix4x4 w2l, Vector3 pivotW, float d)
        {
            Vector3 w = l2w.MultiplyPoint3x4(local);
            Vector3 rel = w - pivotW;
            float denom = d + rel.z;
            float k = denom > d * 0.25f ? d / denom : 4f; // 除零护栏（倾角≤20°永不触及）
            if (k > 0.9999f && k < 1.0001f) return false;
            local += w2l.MultiplyVector(new Vector3(rel.x * (k - 1f), rel.y * (k - 1f), 0f));
            return true;
        }
    }
}
