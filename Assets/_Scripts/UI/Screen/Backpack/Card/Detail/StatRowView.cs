using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Tool;
namespace GIC.UI
{


    /// <summary>
    /// 属性行视图 — 行底 + 行首图标槽 + 标签（本地化条目烘焙于 prefab）+ 右侧数值
    /// 行图标=原神官方属性图标（UI/Stats/stat_*，Resources.Load 零接线）；无官方对应的属性槽位留空
    /// </summary>
    public class StatRowView : MonoBehaviour
    {
        public Image 行底;
        public Image 图标;
        public TextCombiner 标签;
        public TMPro.TMP_Text 数值;

        private static readonly Dictionary<string, Sprite> _iconCache = new Dictionary<string, Sprite>();

        /// <summary>按名加载属性图标（Resources/UI/Stats/ 下，带缓存）；null/空=返回 null</summary>
        public static Sprite LoadStatIcon(string iconName)
        {
            if (string.IsNullOrEmpty(iconName)) return null;
            if (_iconCache.TryGetValue(iconName, out var cached)) return cached;
            var sprite = Resources.Load<Sprite>("UI/Stats/" + iconName);
            if (sprite == null)
                GICLog.Warn("[StatRowView] 属性图标缺失: " + iconName);
            _iconCache[iconName] = sprite;
            return sprite;
        }

        /// <summary>填充数值文本</summary>
        public void SetValue(string text)
        {
            if (数值 != null)
                数值.text = text;
        }

        /// <summary>设置行首图标；null=隐藏（图标槽位保留，标签位置不动）</summary>
        public void SetIcon(Sprite sprite)
        {
            if (图标 == null) return;
            if (sprite == null)
            {
                图标.sprite = null;
                图标.color = new Color(1f, 1f, 1f, 0f);
                return;
            }
            图标.sprite = sprite;
            图标.color = Color.white;
        }

        /// <summary>设置行底条不透明度（保留现 RGB；全部面板=白色统一条、小面板=元素主题色斑马纹）</summary>
        public void SetStripAlpha(float alpha)
        {
            if (行底 == null) return;
            var c = 行底.color;
            行底.color = new Color(c.r, c.g, c.b, alpha);
        }

        /// <summary>设置行底条颜色（RGB 替换 + 不透明度；小面板=元素主题色斑马纹）</summary>
        public void SetStripColor(Color rgb, float alpha)
        {
            if (行底 == null) return;
            行底.color = new Color(rgb.r, rgb.g, rgb.b, alpha);
        }
    }


}
