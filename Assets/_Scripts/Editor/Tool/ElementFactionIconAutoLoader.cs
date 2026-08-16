#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Editor
{


    /// <summary>
    /// ElementFactionIconConfig 自动加载工具
    /// 从 Resources/UI/Other/Element/Stroke/ 和 Resources/UI/Other/Faction/ 自动填入对应 Sprite
    /// </summary>
    public class ElementFactionIconAutoLoader : EditorWindow
    {
        [SerializeField] private ElementFactionIconConfig targetConfig;
        private const string ElementStrokePath = "UI/Other/Element/Stroke/";
        private const string ElementDeepPath   = "UI/Other/Element/Deep/";
        private const string FactionPath       = "UI/Other/Faction/";

        [MenuItem("Tools/图标Config/自动加载元素 & 势力图标", priority = -79)]
        public static void ShowWindow()
        {
            var window = GetWindow<ElementFactionIconAutoLoader>("自动加载图标");
            window.minSize = new Vector2(400, 260);
            window.Show();
        }

        // CreateGUI 为按名调用的魔法方法（Tuanjie 中非虚方法），不加 override
        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingLeft = 10;
            root.style.paddingRight = 10;
            root.style.paddingTop = 8;

            root.Add(ConfigEditorUITK.CreateTitleRow("元素 & 势力图标自动加载", 0));

            var objField = new ObjectField("图标配置")
            {
                objectType = typeof(ElementFactionIconConfig),
                allowSceneObjects = false,
            };
            objField.value = targetConfig;
            objField.RegisterValueChangedCallback(e => targetConfig = (ElementFactionIconConfig)e.newValue);
            root.Add(objField);

            if (targetConfig == null)
            {
                root.Add(new HelpBox(
                    "请先在 Resources/Configs/ 下创建 ElementFactionIconConfig.asset\n"
                    + "右键 → Create → Game → ElementFactionIconConfig", HelpBoxMessageType.Info));
                return;
            }

            var loadBtn = ConfigEditorUITK.CreatePrimaryButton("自动加载全部图标", LoadAll);
            loadBtn.style.height = 40;
            loadBtn.style.marginTop = 12;
            root.Add(loadBtn);

            root.Add(ConfigEditorUITK.CreateSectionHeader("资源路径"));
            AddMiniLabel(root, $"元素 Stroke: Resources/{ElementStrokePath}");
            AddMiniLabel(root, $"元素 Deep:   Resources/{ElementDeepPath}");
            AddMiniLabel(root, $"势力:       Resources/{FactionPath}");
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
            if (targetConfig == null) return;
            Undo.RecordObject(targetConfig, "Auto-load element & faction icons");

            // 元素图标 — Stroke 风格
            targetConfig.pyroStroke     = LoadSprite(ElementStrokePath + "pyro");
            targetConfig.hydroStroke    = LoadSprite(ElementStrokePath + "hydro");
            targetConfig.anemoStroke    = LoadSprite(ElementStrokePath + "anemo");
            targetConfig.electroStroke  = LoadSprite(ElementStrokePath + "electro");
            targetConfig.cryoStroke     = LoadSprite(ElementStrokePath + "cryo");
            targetConfig.dendroStroke   = LoadSprite(ElementStrokePath + "dendro");
            targetConfig.geoStroke      = LoadSprite(ElementStrokePath + "geo");
            targetConfig.physicalStroke = LoadSprite(ElementStrokePath + "geo");   // 暂无，fallback 岩
            targetConfig.lightStroke    = LoadSprite(ElementStrokePath + "geo");   // 暂无，fallback 岩

            // 元素图标 — Deep 风格
            targetConfig.pyroDeep     = LoadSprite(ElementDeepPath + "pyro");
            targetConfig.hydroDeep    = LoadSprite(ElementDeepPath + "hydro");
            targetConfig.anemoDeep    = LoadSprite(ElementDeepPath + "anemo");
            targetConfig.electroDeep  = LoadSprite(ElementDeepPath + "electro");
            targetConfig.cryoDeep     = LoadSprite(ElementDeepPath + "cryo");
            targetConfig.dendroDeep   = LoadSprite(ElementDeepPath + "dendro");
            targetConfig.geoDeep      = LoadSprite(ElementDeepPath + "geo");
            targetConfig.physicalDeep = LoadSprite(ElementDeepPath + "geo");   // 暂无，fallback 岩
            targetConfig.lightDeep    = LoadSprite(ElementDeepPath + "geo");   // 暂无，fallback 岩

            // 势力图标
            targetConfig.mondstadt  = LoadSprite(FactionPath + "mondstadt");
            targetConfig.liyue      = LoadSprite(FactionPath + "liyue");
            targetConfig.inazuma    = LoadSprite(FactionPath + "inazuma");
            targetConfig.sumeru     = LoadSprite(FactionPath + "sumeru");
            targetConfig.fontaine   = LoadSprite(FactionPath + "fontaine");
            targetConfig.natlan     = LoadSprite(FactionPath + "natlan");
            targetConfig.nodkrai    = LoadSprite(FactionPath + "nodkrai");
            targetConfig.snezhnaya  = LoadSprite(FactionPath + "natlan");   // 暂无，fallback
            targetConfig.khaenriah  = LoadSprite(FactionPath + "natlan");   // 暂无，fallback
            targetConfig.celestia   = LoadSprite(FactionPath + "natlan");   // 暂无，fallback
            targetConfig.special    = LoadSprite(FactionPath + "natlan");   // 暂无，fallback

            EditorUtility.SetDirty(targetConfig);
            AssetDatabase.SaveAssets();

            GICLog.Info("[ElementFactionIconAutoLoader] 图标加载完成，请检查 fallback 项并手动替换缺失图标。");
        }

        private static Sprite LoadSprite(string path)
        {
            var sprite = Resources.Load<Sprite>(path);
            if (sprite != null) return sprite;

            GICLog.Warn($"[ElementFactionIconAutoLoader] 未找到: Resources/{path}");
            return null;
        }
    }
    #endif

}


