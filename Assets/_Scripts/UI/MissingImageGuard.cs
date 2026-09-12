// MissingImageGuard.cs - 缺失图片兜底（用户拍板 2026-09-13，方案二改定）
// 兜底图=Resources/UI/missing_image（醒目诡异人脸，一眼可辨）；接入方式=**调用点显式**：
// 立绘/名片等"应当有图"的 Image 在赋值处走 Ensure/Assign——为空即兜底并拉伸填满期望尺寸。
// 全局自动扫描方案已实证不可行并撤除：prefab 序列化保存的 null 加载后即 fake-null，
// 与真断链运行时不可区分，扫描必误伤纯色块（Dropdown 背景/Slider 滑块/BackDim 等）。
using UnityEngine;
using UnityEngine.UI;
namespace GIC.UI
{


    /// <summary>
    /// 缺失图片兜底：只由调用点显式使用——"应当有图"的 Image 赋值时 Ensure/Assign，
    /// 加载失败或为 null 时替换为醒目兜底图并拉伸填满目标 RectTransform。
    /// "故意无图"的纯色块不经过本守卫，天然不受影响。
    /// </summary>
    public static class MissingImageGuard
    {
        private const string FallbackPath = "UI/missing_image";
        private static Sprite _fallback;

        /// <summary>兜底图（Resources 同步加载；加载失败返回 null——此时各方法退化为原值直通）</summary>
        public static Sprite Fallback => _fallback != null ? _fallback : (_fallback = Resources.Load<Sprite>(FallbackPath));

        /// <summary>
        /// 图片兜底：sprite 为空时返回兜底图，否则原样返回。
        /// 供异步加载回调/取值处使用：img.sprite = MissingImageGuard.Ensure(loaded);
        /// </summary>
        public static Sprite Ensure(Sprite sprite)
        {
            if (sprite != null) return sprite;
            return Fallback;
        }

        /// <summary>
        /// 赋值并兜底（用户拍板：拉伸填充为期望尺寸）：sprite 为空时用兜底图**拉伸填满**
        /// 目标 RectTransform（preserveAspect 关闭，不留比例黑边），否则原样赋值。
        /// 供立绘/名片等"应当有图"的 Image 赋值点使用。
        /// </summary>
        public static void Assign(Image image, Sprite sprite)
        {
            if (image == null) return;

            if (sprite == null && Fallback != null)
            {
                image.sprite = Fallback;
                image.preserveAspect = false; // 拉伸填满期望尺寸（兜底图不按原比例，铺满即最醒目）
                return;
            }
            image.sprite = sprite;
        }
    }
}
