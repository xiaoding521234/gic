using UnityEngine;
using UnityEngine.UI;
using GIC.Tool;
namespace GIC.UI
{


    /// <summary>
    /// 属性行视图 — 行底 + 行首图标槽（预留）+ 标签（本地化条目烘焙于 prefab）+ 右侧数值
    /// </summary>
    public class StatRowView : MonoBehaviour
    {
        public Image 行底;
        public Image 图标;
        public TextCombiner 标签;
        public TMPro.TMP_Text 数值;

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
    }


}
