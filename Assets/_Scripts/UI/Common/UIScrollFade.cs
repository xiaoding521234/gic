// UIScrollFade.cs - 滚动区边缘渐隐（挂在滚动区 ScrollRect 上，docs/17 §5.5）
using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;
namespace GIC.UI
{


    /// <summary>
    /// 滚动区边缘渐隐：内容在可视区边缘淡出到面板底色，替代「内容被硬切断」的观感。
    /// 装配=直接挂在 prefab 的滚动区（ScrollRect 同物体）上，渐隐色/高度/不透明度在 Inspector 单源可调；
    /// 只给「底色为单一面板色」的滚动区挂——落在照片/模糊背景/半透明 tint 底上的滚动区不挂（2026-10-11 拍板）。
    /// 渐隐条由本组件在 Viewport 内懒建（不随内容滚动、raycastTarget=false 不吃点击与拖拽）。
    /// 不透明度 = min(1, 该方向剩余可滚动距离 / 渐隐高度) × 最大不透明度——
    /// 到端头自动归零、内容不溢出自动不显示、滚动过程连续平滑；仅在数值变化时写色（避免每帧 Canvas 重建）。
    /// </summary>
    [DisallowMultipleComponent]
    public class UIScrollFade : MonoBehaviour
    {
        private enum 边缘 { 顶部, 底部, 左, 右 }

        [Header("滚动渐隐")]
        [SerializeField] private bool 启用 = true;
        [SerializeField] private Color 渐隐色 = new Color(0.043f, 0.063f, 0.086f, 1f);
        [SerializeField] private float 渐隐高度 = 48f;
        [SerializeField, Range(0f, 1f)] private float 最大不透明度 = 0.9f;
        [SerializeField] private bool 顶部渐隐 = false;
        [SerializeField] private bool 底部渐隐 = true;
        [SerializeField] private bool 横向渐隐 = false;

        private ScrollRect _滚动区;
        private RectTransform _视口;
        private RectTransform _内容;
        private Image _顶条, _底条, _左条, _右条;
        private readonly Vector3[] _角点 = new Vector3[4];
        private float _已用厚度 = -1f, _已用高 = -1f, _已用宽 = -1f;

        private void Awake()
        {
            _滚动区 = GetComponent<ScrollRect>();
            if (_滚动区 == null) _滚动区 = GetComponentInParent<ScrollRect>(true);
            if (_滚动区 == null || _滚动区.viewport == null || _滚动区.content == null)
            {
                GICLog.Warn("[UIScrollFade] 缺少 ScrollRect/Viewport/Content，组件失效: " + name);
                enabled = false;
            }
        }

        // ==================== 每帧刷新 ====================

        private void LateUpdate()
        {
            if (!启用 || _滚动区 == null) return;

            if (_视口 == null)
            {
                _视口 = _滚动区.viewport;
                _内容 = _滚动区.content;
                if (_视口 == null || _内容 == null) return;
            }

            if (!建条()) return; // 布局未就绪（prefab 授权态/尺寸为 0）

            // 内容四缘换算到 Viewport 局部坐标（世界角点 → 局部，自带缩放换算）
            _内容.GetWorldCorners(_角点);
            float 内容下 = _视口.InverseTransformPoint(_角点[0]).y;
            float 内容上 = _视口.InverseTransformPoint(_角点[1]).y;
            float 内容左 = _视口.InverseTransformPoint(_角点[0]).x;
            float 内容右 = _视口.InverseTransformPoint(_角点[3]).x;

            var 区 = _视口.rect;
            bool 纵 = _滚动区.vertical;
            bool 横 = _滚动区.horizontal;

            写条(_顶条, 顶部渐隐 && 纵 ? Mathf.Clamp01((内容上 - 区.yMax) / 渐隐高度) : 0f);
            写条(_底条, 底部渐隐 && 纵 ? Mathf.Clamp01((区.yMin - 内容下) / 渐隐高度) : 0f);
            写条(_左条, 横向渐隐 && 横 ? Mathf.Clamp01((区.xMin - 内容左) / 渐隐高度) : 0f);
            写条(_右条, 横向渐隐 && 横 ? Mathf.Clamp01((内容右 - 区.xMax) / 渐隐高度) : 0f);
        }

        private void 写条(Image 条, float 比例)
        {
            if (条 == null) return;
            float 目标 = 比例 * 最大不透明度;
            bool 需要 = 目标 > 0.002f;
            if (条.gameObject.activeSelf != 需要) 条.gameObject.SetActive(需要);
            if (!需要) return;
            if (Mathf.Abs(条.color.a - 目标) < 0.002f) return; // 未变不写（防每帧 Canvas 重建）
            条.color = new Color(渐隐色.r, 渐隐色.g, 渐隐色.b, 目标);
        }

        // ==================== 渐隐条懒建 ====================

        /// <summary>
        /// 渐隐条懒建；返回视口尺寸是否已就绪（未就绪=本帧不刷新，等布局跑过）。
        /// 注意判据是「已用尺寸」而非条对象——顶部渐隐默认关时 _顶条 恒为 null，
        /// 用它当就绪判据会让底部条永不更新（2026-10-11 现场实证「看不出渐隐」根因）。
        /// </summary>
        private bool 建条()
        {
            var 区 = _视口.rect;
            if (区.width < 1f || 区.height < 1f) return false;
            if (_已用高 < 1f || !Mathf.Approximately(_已用厚度, 渐隐高度)
                || !Mathf.Approximately(_已用高, 区.height) || !Mathf.Approximately(_已用宽, 区.width))
            {
                _已用厚度 = 渐隐高度; _已用高 = 区.height; _已用宽 = 区.width;
                _顶条 = 备条(_顶条, 边缘.顶部, 顶部渐隐);
                _底条 = 备条(_底条, 边缘.底部, 底部渐隐);
                _左条 = 备条(_左条, 边缘.左, 横向渐隐);
                _右条 = 备条(_右条, 边缘.右, 横向渐隐);
            }
            return true;
        }

        private Image 备条(Image 已有, 边缘 边, bool 需要)
        {
            if (!需要)
            {
                if (已有 != null) 已有.gameObject.SetActive(false);
                return 已有;
            }

            if (已有 == null)
            {
                var go = new GameObject("渐隐条_" + 边, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(_视口, false);
                已有 = go.GetComponent<Image>();
                已有.raycastTarget = false; // 不吃点击/拖拽，滚动照常
                已有.color = new Color(渐隐色.r, 渐隐色.g, 渐隐色.b, 0f);
            }

            var rt = (RectTransform)已有.transform;
            switch (边)
            {
                case 边缘.顶部:
                    rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
                    rt.pivot = new Vector2(0.5f, 1f); rt.anchoredPosition = Vector2.zero;
                    rt.sizeDelta = new Vector2(0f, 渐隐高度);
                    已有.sprite = 斜坡(true, false); // 上缘不透明 → 向下渐隐
                    break;
                case 边缘.底部:
                    rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f);
                    rt.pivot = new Vector2(0.5f, 0f); rt.anchoredPosition = Vector2.zero;
                    rt.sizeDelta = new Vector2(0f, 渐隐高度);
                    已有.sprite = 斜坡(true, true); // 下缘不透明 → 向上渐隐
                    break;
                case 边缘.左:
                    rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(0f, 1f);
                    rt.pivot = new Vector2(0f, 0.5f); rt.anchoredPosition = Vector2.zero;
                    rt.sizeDelta = new Vector2(渐隐高度, 0f);
                    已有.sprite = 斜坡(false, true); // 左缘不透明 → 向右渐隐
                    break;
                default:
                    rt.anchorMin = new Vector2(1f, 0f); rt.anchorMax = new Vector2(1f, 1f);
                    rt.pivot = new Vector2(1f, 0.5f); rt.anchoredPosition = Vector2.zero;
                    rt.sizeDelta = new Vector2(渐隐高度, 0f);
                    已有.sprite = 斜坡(false, false); // 右缘不透明 → 向左渐隐
                    break;
            }
            已有.transform.SetAsLastSibling(); // 盖在 Content 之上
            return 已有;
        }

        // ==================== 渐隐贴图（alpha 斜坡，静态缓存；Viewport 遮罩会裁掉多余部分） ====================

        private static Sprite _竖底, _竖顶, _横左, _横右;

        private static Sprite 斜坡(bool 竖, bool 起点不透明)
        {
            if (竖 && 起点不透明) { if (_竖底 == null) _竖底 = 造斜坡(true, true); return _竖底; }
            if (竖) { if (_竖顶 == null) _竖顶 = 造斜坡(true, false); return _竖顶; }
            if (起点不透明) { if (_横左 == null) _横左 = 造斜坡(false, true); return _横左; }
            if (_横右 == null) _横右 = 造斜坡(false, false);
            return _横右;
        }

        /// <summary>1px 宽/高的白到透明斜坡：起点=竖版下缘 / 横版左缘（贴图 y=0 是下缘）</summary>
        private static Sprite 造斜坡(bool 竖, bool 起点不透明)
        {
            const int N = 64;
            var tex = new Texture2D(竖 ? 1 : N, 竖 ? N : 1, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.hideFlags = HideFlags.HideAndDontSave;
            for (int i = 0; i < N; i++)
            {
                float t = i / (float)(N - 1);
                float a = 起点不透明 ? 1f - t : t;
                var c = new Color(1f, 1f, 1f, a);
                if (竖) tex.SetPixel(0, i, c);
                else tex.SetPixel(i, 0, c);
            }
            tex.Apply();
            var sp = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sp.hideFlags = HideFlags.HideAndDontSave;
            return sp;
        }
    }


}
