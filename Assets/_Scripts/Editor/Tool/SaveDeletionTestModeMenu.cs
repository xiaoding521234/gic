using UnityEditor;
using UnityEngine;
using GIC.Framework;

namespace GIC.Editor
{
    /// <summary>
    /// 删档测试模式开关（仅编辑器；打包版本 SaveManager 恒为关闭，不会误删玩家存档）
    /// </summary>
    public static class SaveDeletionTestModeMenu
    {
        [MenuItem("Tools/存档/删档测试模式")]
        private static void Toggle()
        {
            bool current = EditorPrefs.GetBool(SaveManager.DeletionTestModeKey, false);
            bool next = !current;
            EditorPrefs.SetBool(SaveManager.DeletionTestModeKey, next);
            Menu.SetChecked("Tools/存档/删档测试模式", next);
            Debug.Log($"删档测试模式: {(next ? "开启（每次启动清空存档）" : "关闭")}");
        }

        [MenuItem("Tools/存档/删档测试模式", true)]
        private static bool ValidateToggle()
        {
            bool on = EditorPrefs.GetBool(SaveManager.DeletionTestModeKey, false);
            Menu.SetChecked("Tools/存档/删档测试模式", on);
            return true;
        }
    }
}
