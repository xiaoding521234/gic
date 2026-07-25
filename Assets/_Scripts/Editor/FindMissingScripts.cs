using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using UnityEditor.SceneManagement;

public class FindMissingScripts : EditorWindow
{
    [MenuItem("Tools/查找丢失的脚本引用")]
    static void FindMissingScriptsInAll()
    {
        Debug.Log("========== 开始查找丢失的脚本 ==========");
        int count = 0;
        
        // 1. 保存当前场景
        string currentScenePath = SceneManager.GetActiveScene().path;
        
        // 2. 检查所有场景（包括未加载的）
        string[] allScenePaths = AssetDatabase.FindAssets("t:Scene");
        foreach (string guid in allScenePaths)
        {
            string scenePath = AssetDatabase.GUIDToAssetPath(guid);
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        }
        
        // 3. 现在查找所有已加载场景中的 GameObject
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            
            GameObject[] rootObjects = scene.GetRootGameObjects();
            foreach (GameObject root in rootObjects)
            {
                CheckGameObjectRecursive(root, ref count);
            }
        }
        
        // 4. 关闭额外打开的场景
        for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.path != currentScenePath && scene.path != "")
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
        
        // 5. 检查所有 Prefab
        string[] allPrefabs = AssetDatabase.FindAssets("t:Prefab");
        foreach (string guid in allPrefabs)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            
            if (prefab == null) continue;
            
            Component[] components = prefab.GetComponentsInChildren<Component>(true);
            foreach (Component comp in components)
            {
                if (comp == null)
                {
                    Debug.LogError($"Prefab 发现丢失脚本: {path}", prefab);
                    count++;
                }
            }
        }
        
        // 6. 检查 ScriptableObject 资源
        string[] allScriptableObjects = AssetDatabase.FindAssets("t:ScriptableObject");
        foreach (string guid in allScriptableObjects)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ScriptableObject so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            
            if (so == null) continue;
            
            var soObj = new SerializedObject(so);
            var iterator = soObj.GetIterator();
            while (iterator.NextVisible(true))
            {
                if (iterator.propertyType == SerializedPropertyType.ObjectReference && 
                    iterator.objectReferenceValue == null && 
                    !string.IsNullOrEmpty(iterator.displayName) &&
                    iterator.objectReferenceInstanceIDValue != 0)
                {
                    Debug.LogError($"ScriptableObject 发现丢失引用: {path} -> {iterator.propertyPath}", so);
                    count++;
                }
            }
        }
        
        Debug.Log($"========== 找到 {count} 个丢失的脚本引用 ==========");
        if (count == 0)
        {
            EditorUtility.DisplayDialog("完成", "未发现丢失的脚本引用！", "好的");
        }
        else
        {
            EditorUtility.DisplayDialog("完成", $"发现 {count} 个丢失的脚本引用，请查看 Console 详情。", "好的");
        }
    }
    
    private static void CheckGameObjectRecursive(GameObject obj, ref int count)
    {
        Component[] components = obj.GetComponents<Component>();
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] == null)
            {
                Debug.LogError($"发现丢失脚本: {GetGameObjectPath(obj)}", obj);
                count++;
            }
        }
        
        foreach (Transform child in obj.transform)
        {
            CheckGameObjectRecursive(child.gameObject, ref count);
        }
    }
    
    private static string GetGameObjectPath(GameObject obj)
    {
        string path = obj.name;
        Transform parent = obj.transform.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }
        return path;
    }
}