#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;

public static class URPSetup
{
    [MenuItem("Tools/Setup URP")]
    public static void Setup()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Settings"))
            AssetDatabase.CreateFolder("Assets", "Settings");

        // 创建 UniversalRendererData
        var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
        rendererData.name = "URPRenderer";
        AssetDatabase.CreateAsset(rendererData, "Assets/Settings/URPRenderer.asset");

        // 创建 UniversalRenderPipelineAsset
        var urpAsset = ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();
        urpAsset.name = "URPAsset";

        // 用 SerializedObject 设置 m_RendererDataList
        var serializedAsset = new SerializedObject(urpAsset);
        var rendererListProp = serializedAsset.FindProperty("m_RendererDataList");
        if (rendererListProp != null)
        {
            rendererListProp.arraySize = 1;
            rendererListProp.GetArrayElementAtIndex(0).objectReferenceValue = rendererData;
            serializedAsset.ApplyModifiedProperties();
            Debug.Log("[URP Setup] m_RendererDataList 已设置");
        }
        else
        {
            Debug.LogError("[URP Setup] 找不到 m_RendererDataList 属性");
        }

        AssetDatabase.CreateAsset(urpAsset, "Assets/Settings/URPAsset.asset");
        EditorUtility.SetDirty(urpAsset);
        EditorUtility.SetDirty(rendererData);

        // 激活 URP
        GraphicsSettings.defaultRenderPipeline = urpAsset;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[URP Setup] 完成! URP Asset: {AssetDatabase.GetAssetPath(urpAsset)}, Renderer: {AssetDatabase.GetAssetPath(rendererData)}, Active: {GraphicsSettings.defaultRenderPipeline != null}");
    }
}
#endif
