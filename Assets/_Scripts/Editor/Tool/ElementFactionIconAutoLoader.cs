#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// ElementFactionIconConfig 自动加载工具
/// 从 Resources/UI/Other/Element/Stroke/ 和 Resources/UI/Other/Faction/ 自动填入对应 Sprite
/// </summary>
public class ElementFactionIconAutoLoader : EditorWindow
{
    private ElementFactionIconConfig targetConfig;
    private const string ElementStrokePath = "UI/Other/Element/Stroke/";
    private const string ElementDeepPath   = "UI/Other/Element/Deep/";
    private const string FactionPath       = "UI/Other/Faction/";

    [MenuItem("Tools/图标/自动加载元素 & 势力图标")]
    public static void ShowWindow()
    {
        var window = GetWindow<ElementFactionIconAutoLoader>("自动加载图标");
        window.minSize = new Vector2(400, 200);
        window.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("元素 & 势力图标自动加载", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        targetConfig = (ElementFactionIconConfig)EditorGUILayout.ObjectField(
            "图标配置", targetConfig, typeof(ElementFactionIconConfig), false);

        if (targetConfig == null)
        {
            EditorGUILayout.HelpBox(
                "请先在 Resources/Configs/ 下创建 ElementFactionIconConfig.asset\n"
                + "右键 → Create → Game → ElementFactionIconConfig", MessageType.Info);
            return;
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("自动加载全部图标", GUILayout.Height(40)))
        {
            LoadAll();
        }

        EditorGUILayout.Space();

        EditorGUILayout.LabelField("资源路径:", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"  元素 Stroke: Resources/{ElementStrokePath}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"  元素 Deep:   Resources/{ElementDeepPath}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"  势力:       Resources/{FactionPath}", EditorStyles.miniLabel);
    }

    private void LoadAll()
    {
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

        Debug.Log("[ElementFactionIconAutoLoader] 图标加载完成，请检查 fallback 项并手动替换缺失图标。");
    }

    private static Sprite LoadSprite(string path)
    {
        var sprite = Resources.Load<Sprite>(path);
        if (sprite != null) return sprite;

        Debug.LogWarning($"[ElementFactionIconAutoLoader] 未找到: Resources/{path}");
        return null;
    }
}
#endif
