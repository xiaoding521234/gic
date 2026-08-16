// ============================================
// WishPoolConfigEditor - 卡池配置 Inspector
// ============================================
// 全字段默认绘制 + 底部「概率模拟测试」按钮（10万次完整模拟，结果输出到 Console）。

using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using GIC.Framework;
using GIC.Data;

namespace GIC.Editor
{
    [CustomEditor(typeof(WishPoolConfig))]
    public class WishPoolConfigEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var so = serializedObject;
            var root = new VisualElement();

            ConfigEditorUITK.ApplyGameFont(root);

            var it = so.GetIterator();
            bool enterChildren = true;
            while (it.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (it.name == "m_Script") continue;
                root.Add(ConfigEditorUITK.CreateField(so, it.Copy()));
            }
            root.Bind(so);

            // ===== 工具区 =====
            var section = ConfigEditorUITK.CreateToolsSection("测试工具");
            var testBtn = ConfigEditorUITK.CreatePrimaryButton("概率模拟测试 (10万次)",
                () => WishProbabilityTest.RunTest((WishPoolConfig)target));
            testBtn.style.marginTop = 8;
            section.Add(testBtn);

            var hint = new Label("完整复现祈愿流程（星级降级/物品回退/重复转星辉/相遇之线），统计结果输出到 Console");
            hint.style.fontSize = 11;
            hint.style.color = new Color(0.6f, 0.6f, 0.6f);
            hint.style.marginLeft = 9;
            hint.style.whiteSpace = UnityEngine.UIElements.WhiteSpace.Normal;
            section.Add(hint);

            root.Add(section);
            return root;
        }
    }
}
