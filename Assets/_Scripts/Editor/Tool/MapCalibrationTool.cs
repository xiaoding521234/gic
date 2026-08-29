#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using GIC.Framework;

namespace GIC.Editor
{
    /// <summary>
    /// 地图标定工具 — 场景应用逻辑（无菜单入口，由取点器面板按钮调用）。
    /// 换图流程：替换 all_map.jpg → 取点器「标定地图」两点解算 → 保存到磁盘 →
    /// 面板「应用地图标定到场景」重摆 MapPlane 并保存，编辑器中目测对齐。
    /// 运行时 MapScreen.Start 会按同样参数重新应用，场景值仅用于编辑期可视化。
    /// </summary>
    public static class MapCalibrationTool
    {
        private const string MapScreenScenePath = MapPaths.MapScreenScene;

        /// <summary>
        /// 应用标定到 MapScreen 场景（唯一入口）：开场景 → Undo 注册 → 重摆 → 保存。
        /// </summary>
        public static void ApplyFromPickTool()
        {
            var scene = EditorSceneManager.OpenScene(MapScreenScenePath, OpenSceneMode.Single);
            var screens = Object.FindObjectsOfType<GIC.UI.MapScreen>(true);
            if (screens.Length != 1)
            {
                GICLog.Error($"[MapCalibrationTool] MapScreen 组件数量异常: {screens.Length}（预期 1），未做任何修改");
                return;
            }

            var planeField = typeof(GIC.UI.MapScreen).GetField("地图贴图",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var plane = planeField?.GetValue(screens[0]) as SpriteRenderer;
            if (plane != null)
                Undo.RegisterCompleteObjectUndo(plane.transform, "应用地图标定");

            screens[0].ApplyMapCalibration();

            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene);
            // 保存后再换全图（SceneView 目测对齐用全分辨率；只改内存不落盘，sceneSaving 守卫保证场景 YAML 只含预览图）
            MapEditorFullRes.Apply();
            GICLog.Info($"[MapCalibrationTool] 标定已应用到 MapPlane 并保存场景（saved={saved}）。请目测地图与锚点对齐情况\n" +
                        "撤回说明：场景摆放可 Ctrl+Z；但 MapConfig 标定参数才是源头——参数撤回用取点器 Ctrl+Z（未保存时）或 git checkout");
        }
    }
}
#endif
