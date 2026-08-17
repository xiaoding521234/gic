#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using GIC.Framework;

namespace GIC.Editor
{
    /// <summary>
    /// 地图标定工具 — 原神式固定世界坐标系的手动标定入口。
    /// 扩图/换图流程：替换 all_map.jpg → 在 MapConfig.asset 调整 mapOrigin / worldUnitsPerPixel
    /// → 运行本工具把标定应用到 MapScreen 场景（保存），即可在编辑器中检查对齐。
    /// 运行时 MapScreen.Start 会按同样参数重新应用，场景值仅用于编辑期可视化。
    /// </summary>
    public static class MapCalibrationTool
    {
        private const string MapScreenScenePath = "Assets/Scenes/MapScreen.unity";

        [MenuItem("Tools/地图/应用地图标定到 MapScreen 场景", priority = -59)]
        public static void Apply()
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
            GICLog.Info($"[MapCalibrationTool] 标定已应用到 MapPlane 并保存场景（saved={saved}）。请目测地图与锚点对齐情况\n" +
                        "撤回说明：场景摆放可 Ctrl+Z；但 MapConfig 标定参数才是源头——参数撤回用取点器 Ctrl+Z（未保存时）或 git checkout");
        }
    }
}
#endif
