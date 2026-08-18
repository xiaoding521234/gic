#if UNITY_EDITOR
// MapEditorFullRes.cs - 编辑器高清瓦片层（母版级目测精度，随取点器开关装卸）
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using GIC.Data;
using GIC.Framework;
using GIC.UI;

namespace GIC.Editor
{
    /// <summary>
    /// 母版 21900px 无法整体导入编辑器（>16384 上限，导入 OOM），高清目测改用瓦片层实现：
    /// 取点器打开时在场景铺全部瓦片（瓦片即母版原生像素切片，与运行时同源同精度），
    /// 关闭取点器 / 进 Play / 切场景时销毁。瓦片对象 HideFlags.DontSave，永不落入场景 YAML。
    /// MapPlane 底图恒为预览图（不再换 16384 全图副本——瓦片全覆盖后底图不可见；
    /// 全图副本仅作 MapTileBakeTool 无母版时的回退源）。
    /// 瓦片几何 = MapTileLayer.GetTileWorldRect（唯一公式），Z 用场景瓦片层的 瓦片前移 序列化值。
    /// </summary>
    [InitializeOnLoad]
    public static class MapEditorFullRes
    {
        private const string 层名 = "EditorMapTiles";

        private static GameObject _tileRoot;
        private static Sprite _预览图缓存;

        private static Sprite 预览图 => _预览图缓存 != null
            ? _预览图缓存
            : (_预览图缓存 = AssetDatabase.LoadAssetAtPath<Sprite>(MapPaths.预览图));

        static MapEditorFullRes()
        {
            // DontSave 已排除序列化，此守卫为双保险：保存/关闭 MapScreen 场景前销毁，存后重铺
            EditorSceneManager.sceneSaving += (scene, _) =>
            {
                if (_tileRoot != null && scene.path == MapPaths.MapScreen场景) 销毁瓦片层();
            };
            EditorSceneManager.sceneSaved += scene =>
            {
                if (scene.path == MapPaths.MapScreen场景 && EditorWindow.HasOpenInstances<MapPickPointTool>())
                    换上();
            };
            EditorSceneManager.sceneClosing += (_, _) => 销毁瓦片层();
            // 进 Play 前必须销毁：运行时 MapTileLayer 会铺正式瓦片，编辑器层会与之重叠
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.ExitingEditMode) 销毁瓦片层();
                else if (s == PlayModeStateChange.EnteredEditMode
                         && EditorWindow.HasOpenInstances<MapPickPointTool>())
                    换上();
            };
        }

        public static void 换上()
        {
            销毁瓦片层();
            var plane = 找MapPlane();
            if (plane == null || !plane.gameObject.scene.IsValid()) return;
            if (预览图 != null && plane.sprite != 预览图)
            {
                plane.sprite = 预览图;
                重摆标定();
            }
            生成瓦片层(plane);
        }

        public static void 还原() => 销毁瓦片层();

        private static void 生成瓦片层(SpriteRenderer plane)
        {
            var cfg = 取MapConfig(plane);
            if (cfg == null || !cfg.IsTiled) return;

            var root = new GameObject(层名) { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(plane.transform.parent, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            float z = 取瓦片Z();
            int missing = 0;
            for (int y = 0; y < cfg.TileRows; y++)
                for (int x = 0; x < cfg.TileColumns; x++)
                {
                    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{MapPaths.瓦片目录}/tile_{x}_{y}.jpg");
                    if (sprite == null) { missing++; continue; }
                    MapTileLayer.GetTileWorldRect(cfg, new Vector2Int(x, y), out var center, out var size);
                    var b = sprite.bounds.size;
                    var go = new GameObject($"tile_{x}_{y}") { hideFlags = HideFlags.DontSave };
                    go.transform.SetParent(root.transform, false);
                    var r = go.AddComponent<SpriteRenderer>();
                    r.sprite = sprite;
                    go.transform.localPosition = new Vector3(center.x, center.y, z);
                    go.transform.localScale = new Vector3(size.x / b.x, size.y / b.y, 1f);
                }
            _tileRoot = root;
            if (missing > 0)
                GICLog.Warn($"[MapEditorFullRes] {missing} 片瓦片缺失（先跑 Tools/地图/生成地图瓦片），缺失处显示预览图底图");
            GICLog.Info($"[MapEditorFullRes] 已铺编辑器瓦片层（母版级 {cfg.TileColumns}x{cfg.TileRows} 片，编辑器专用不落盘）");
        }

        private static void 销毁瓦片层()
        {
            if (_tileRoot == null) return;
            Object.DestroyImmediate(_tileRoot);
            _tileRoot = null;
        }

        /// <summary>瓦片 Z 偏移：读场景瓦片层的 瓦片前移 序列化值（无瓦片层时兜底默认）</summary>
        private static float 取瓦片Z()
        {
            var layers = Object.FindObjectsOfType<MapTileLayer>(true);
            if (layers.Length == 1)
            {
                var p = new SerializedObject(layers[0]).FindProperty("瓦片前移");
                if (p != null) return p.floatValue;
            }
            return -0.02f; // 与 MapTileLayer.瓦片前移 默认一致
        }

        private static MapConfig 取MapConfig(SpriteRenderer plane)
        {
            var screens = Object.FindObjectsOfType<MapScreen>(true);
            if (screens.Length != 1) return null;
            return new SerializedObject(screens[0]).FindProperty("mapConfig")?.objectReferenceValue as MapConfig;
        }

        /// <summary>按 MapScreen.ApplyMapCalibration（SourcePixel×unit，与运行时同源）重摆 MapPlane</summary>
        private static void 重摆标定()
        {
            var screens = Object.FindObjectsOfType<MapScreen>(true);
            if (screens.Length != 1) return;
            screens[0].ApplyMapCalibration();
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
