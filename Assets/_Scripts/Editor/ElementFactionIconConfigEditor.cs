// ============================================
// ElementFactionIconConfigEditor - 图标配置 Inspector
// ============================================
// 非列表型配置：全字段默认绘制（Sprite 走预览控件）+ 底部自动加载工具。
// 取代原独立窗口 ElementFactionIconAutoLoader（2026-08-16 迁入）。

using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using GIC.Framework;
using GIC.Data;

namespace GIC.Editor
{
    [CustomEditor(typeof(ElementFactionIconConfig))]
    public class ElementFactionIconConfigEditor : UnityEditor.Editor
    {
        private const string ElementStrokePath = "UI/Other/Element/Stroke/";
        private const string ElementDeepPath   = "UI/Other/Element/Deep/";
        private const string FactionPath       = "UI/Other/Faction/";

        public override VisualElement CreateInspectorGUI()
        {
            var so = serializedObject;
            var root = new VisualElement();

            ConfigEditorUITK.ApplyGameFont(root);

            // 全字段默认绘制（Sprite 字段自动走预览行）
            var it = so.GetIterator();
            bool enterChildren = true;
            while (it.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (it.name == "m_Script") continue;
                root.Add(ConfigEditorUITK.CreateField(so, it.Copy()));
            }
            root.Bind(so);

            // ===== 批量填充工具 =====
            var section = ConfigEditorUITK.CreateToolsSection("批量填充工具");
            var loadBtn = ConfigEditorUITK.CreatePrimaryButton("自动加载全部图标", LoadAll);
            loadBtn.style.marginTop = 8;
            section.Add(loadBtn);

            AddMiniLabel(section, $"元素 Stroke: Resources/{ElementStrokePath}");
            AddMiniLabel(section, $"元素 Deep:   Resources/{ElementDeepPath}");
            AddMiniLabel(section, $"势力:       Resources/{FactionPath}");

            root.Add(section);
            return root;
        }

        private static void AddMiniLabel(VisualElement parent, string text)
        {
            var label = new Label(text);
            label.style.fontSize = 11;
            label.style.color = new Color(0.6f, 0.6f, 0.6f);
            label.style.marginLeft = 9;
            parent.Add(label);
        }

        private void LoadAll()
        {
            var config = (ElementFactionIconConfig)target;
            if (config == null) return;
            Undo.RecordObject(config, "Auto-load element & faction icons");

            // 元素图标 — Stroke 风格
            config.pyroStroke     = LoadSprite(ElementStrokePath + "pyro");
            config.hydroStroke    = LoadSprite(ElementStrokePath + "hydro");
            config.anemoStroke    = LoadSprite(ElementStrokePath + "anemo");
            config.electroStroke  = LoadSprite(ElementStrokePath + "electro");
            config.cryoStroke     = LoadSprite(ElementStrokePath + "cryo");
            config.dendroStroke   = LoadSprite(ElementStrokePath + "dendro");
            config.geoStroke      = LoadSprite(ElementStrokePath + "geo");
            config.physicalStroke = LoadSprite(ElementStrokePath + "geo");   // 暂无，fallback 岩
            config.lightStroke    = LoadSprite(ElementStrokePath + "geo");   // 暂无，fallback 岩

            // 元素图标 — Deep 风格
            config.pyroDeep     = LoadSprite(ElementDeepPath + "pyro");
            config.hydroDeep    = LoadSprite(ElementDeepPath + "hydro");
            config.anemoDeep    = LoadSprite(ElementDeepPath + "anemo");
            config.electroDeep  = LoadSprite(ElementDeepPath + "electro");
            config.cryoDeep     = LoadSprite(ElementDeepPath + "cryo");
            config.dendroDeep   = LoadSprite(ElementDeepPath + "dendro");
            config.geoDeep      = LoadSprite(ElementDeepPath + "geo");
            config.physicalDeep = LoadSprite(ElementDeepPath + "geo");   // 暂无，fallback 岩
            config.lightDeep    = LoadSprite(ElementDeepPath + "geo");   // 暂无，fallback 岩

            // 势力图标
            config.mondstadt  = LoadSprite(FactionPath + "mondstadt");
            config.liyue      = LoadSprite(FactionPath + "liyue");
            config.inazuma    = LoadSprite(FactionPath + "inazuma");
            config.sumeru     = LoadSprite(FactionPath + "sumeru");
            config.fontaine   = LoadSprite(FactionPath + "fontaine");
            config.natlan     = LoadSprite(FactionPath + "natlan");
            config.nodkrai    = LoadSprite(FactionPath + "nodkrai");
            config.snezhnaya  = LoadSprite(FactionPath + "natlan");   // 暂无，fallback
            config.khaenriah  = LoadSprite(FactionPath + "natlan");   // 暂无，fallback
            config.celestia   = LoadSprite(FactionPath + "natlan");   // 暂无，fallback
            config.special    = LoadSprite(FactionPath + "natlan");   // 暂无，fallback

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            GICLog.Info("[ElementFactionIconConfig] 图标加载完成，请检查 fallback 项并手动替换缺失图标。");
        }

        private static Sprite LoadSprite(string path)
        {
            var sprite = Resources.Load<Sprite>(path);
            if (sprite != null) return sprite;

            GICLog.Warn($"[ElementFactionIconConfig] 未找到: Resources/{path}");
            return null;
        }
    }
}
