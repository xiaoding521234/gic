#if UNITY_EDITOR
// MapEditorFullRes.cs - 编辑器全图预览守卫（防止 16384 全图引用被写进场景 YAML 拉进构建）
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using GIC.UI;

namespace GIC.Editor
{
    /// <summary>
    /// 瓦片化后 MapPlane 场景里只挂低清预览图（2048 档，运行时作底图，瓦片层在其上加载全分辨率切片），
    /// 16384 全图 all_map.jpg 不再被场景引用 → 不进构建。
    /// 编辑器取点/标定需要全分辨率目测：取点器打开时把全图临时换上，关闭时还原预览图。
    /// 保存守卫：sceneSaving 时若已换全图，先还原再落盘（场景 YAML 始终只含预览图引用）；
    /// sceneSaved 后若取点器仍开着，重新换上（仅内存，不落盘）。
    /// </summary>
    [InitializeOnLoad]
    public static class MapEditorFullRes
    {
        private const string 全图路径 = "Assets/Art/Map/Textures/all_map.jpg";
        private const string 预览图路径 = "Assets/Art/Map/Textures/all_map_preview.jpg";
        private const string MapScreen场景 = "Assets/Scenes/MapScreen.unity";

        private static bool _已换全图;
        private static Sprite _预览图缓存;

        private static Sprite 预览图 => _预览图缓存 != null
            ? _预览图缓存
            : (_预览图缓存 = AssetDatabase.LoadAssetAtPath<Sprite>(预览图路径));

        static MapEditorFullRes()
        {
            EditorSceneManager.sceneSaving += (scene, _) =>
            {
                // 任何场景保存前还原（找不到 MapPlane 时为空操作）
                if (_已换全图) 还原();
            };
            EditorSceneManager.sceneSaved += scene =>
            {
                if (scene.path == MapScreen场景 && EditorWindow.HasOpenInstances<MapPickPointTool>())
                    换上();
            };
        }

        public static void 换上()
        {
            var plane = 找MapPlane();
            if (plane == null || !plane.gameObject.scene.IsValid()) return;
            var full = AssetDatabase.LoadAssetAtPath<Sprite>(全图路径);
            if (full == null || plane.sprite == full) return;
            plane.sprite = full;
            _已换全图 = true;
        }

        public static void 还原()
        {
            var plane = 找MapPlane();
            if (plane != null && 预览图 != null && plane.sprite != 预览图)
                plane.sprite = 预览图;
            _已换全图 = false;
        }

        /// <summary>取 MapScreen.地图贴图（场景未开/组件异常时返回 null）</summary>
        private static SpriteRenderer 找MapPlane()
        {
            var screens = Object.FindObjectsOfType<MapScreen>(true);
            if (screens.Length != 1) return null;
            var so = new SerializedObject(screens[0]);
            var prop = so.FindProperty("地图贴图");
            return prop?.objectReferenceValue as SpriteRenderer;
        }
    }
}
#endif
