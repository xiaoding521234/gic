using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;

namespace GIC.UI
{
    /// <summary>
    /// 指令建议行（InputPopupDialog 命令模式）：主词 + 灰色提示 + 选中态背景。
    /// 结构与样式（字体/字号/颜色）全部在 prefab 模板序列化，运行时只设文本与选中态。
    /// 2026-09-13 独立成文件：原与 InputPopupDialog 同文件时，新建类写入 prefab 的
    /// m_Script 被序列化为 {fileID: 0}（missing script，实例化报错——docs/14 §49）；
    /// 类名=文件名的 MonoBehaviour 走主类 FileID 解析，绕开该坑。
    /// </summary>
    public class CommandSuggestRow : MonoBehaviour
    {
        [InspectorName("主词文本")] [SerializeField] private TextMeshProUGUI mainText;
        [InspectorName("提示文本")] [SerializeField] private TextMeshProUGUI hintText;
        [InspectorName("背景")] [SerializeField] private Image background;
        [InspectorName("选中背景色")] [SerializeField] private Color selectedColor = new Color(0.16f, 0.22f, 0.30f, 1f);
        [InspectorName("常态背景色")] [SerializeField] private Color normalColor = new Color(1f, 1f, 1f, 0f);

        private Button _button;

        /// <summary>点击整行触发补全（模板 inactive，Awake 在实例激活时才跑）</summary>
        public Button Button => _button != null ? _button : (_button = GetComponent<Button>());

        public void Set(in CommandSuggestion suggestion)
        {
            if (mainText != null) mainText.text = suggestion.main;
            if (hintText != null) hintText.text = string.IsNullOrEmpty(suggestion.hint) ? "" : suggestion.hint;
        }

        public void SetSelected(bool selected)
        {
            if (background != null) background.color = selected ? selectedColor : normalColor;
        }
    }
}
