#if UNITY_EDITOR
// MapTileBakeTool.cs - 大地图瓦片生成（ffmpeg 切图 + Addressables 入组 + MapConfig/场景接线）
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
using GIC.UI;

namespace GIC.Editor
{
    /// <summary>
    /// 瓦片生成工具：从 all_map.jpg 切 2048 网格瓦片（含 4px 重叠边）→ Assets/Art/Map/Tiles/，
    /// 生成 2048 档低清预览图 → Assets/Art/Map/Textures/all_map_preview.jpg，
    /// 瓦片入 Addressables 组 MapAssets（地址 MapAssets/Tiles/tile_{x}_{y}），
    /// 移除组内全图条目（868MB 未压缩全图出构建），网格参数写入 MapConfig，
    /// MapScreen 场景 MapPlane 底图换为预览图并新建 TileRoot 挂 MapTileLayer。
    /// 幂等：重复执行先清空瓦片目录与组内瓦片条目。
    /// </summary>
    public static class MapTileBakeTool
    {
        private const string 源图路径 = "Assets/Art/Map/Textures/all_map.jpg";
        private const string 预览图路径 = "Assets/Art/Map/Textures/all_map_preview.jpg";
        private const string 瓦片目录 = "Assets/Art/Map/Tiles";
        private const string MapConfig路径 = "Assets/Resources/Configs/MapConfig.asset";
        private const string MapScreen场景 = "Assets/Scenes/MapScreen.unity";
        private const string ffmpeg路径 = @"D:\Tool\FormatFactory\ffmpeg.exe";
        private const string 组名 = "MapAssets";
        private const string 地址前缀 = "MapAssets/Tiles/tile_";
        private const string 全图地址 = "MapAssets/Textures/all_map";
        private const int 瓦片像素 = 2048;
        private const int 重叠像素 = 4;

        [MenuItem("Tools/地图/生成地图瓦片", priority = -57)]
        public static void Bake()
        {
            var src = AssetDatabase.LoadAssetAtPath<Texture2D>(源图路径);
            if (src == null)
            {
                GICLog.Error($"[MapTileBakeTool] 源图未找到: {源图路径}");
                return;
            }
            if (!File.Exists(ffmpeg路径))
            {
                GICLog.Error($"[MapTileBakeTool] ffmpeg 未找到: {ffmpeg路径}");
                return;
            }
            int W = src.width, H = src.height;
            if (W % 4 != 0 || H % 4 != 0)
            {
                GICLog.Error($"[MapTileBakeTool] 源图尺寸 {W}x{H} 非 4 倍数（块压缩前置条件），请外部缩放后重试");
                return;
            }

            int cols = Mathf.CeilToInt(W / (float)瓦片像素);
            int rows = Mathf.CeilToInt(H / (float)瓦片像素);
            int count = cols * rows;
            GICLog.Info($"[MapTileBakeTool] 开始切图 {W}x{H} → {cols}x{rows} = {count} 瓦片（{瓦片像素}px，重叠 {重叠像素}px）");

            // 1. 清空旧瓦片（目录连同 meta，保证导入器全新无平台 override 残留）
            if (AssetDatabase.IsValidFolder(瓦片目录))
                AssetDatabase.DeleteAsset(瓦片目录);
            AssetDatabase.CreateFolder("Assets/Art/Map", "Tiles");
            string tileAbsDir = Path.GetFullPath(瓦片目录);

            // 2. ffmpeg 单趟切图（filter_complex 只解码一次源图；crop=宽:高:x:y，含重叠边、边缘钳制）
            var filter = new StringBuilder();
            var outputs = new StringBuilder();
            int idx = 0;
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    int px0 = Mathf.Max(0, x * 瓦片像素 - 重叠像素);
                    int py0 = Mathf.Max(0, y * 瓦片像素 - 重叠像素);
                    int pw = Mathf.Min(W, (x + 1) * 瓦片像素 + 重叠像素) - px0;
                    int ph = Mathf.Min(H, (y + 1) * 瓦片像素 + 重叠像素) - py0;
                    filter.Append($"[0:v]crop={pw}:{ph}:{px0}:{py0}[t{idx}];");
                    outputs.Append($" -map [t{idx}] \"{Path.Combine(tileAbsDir, $"tile_{x}_{y}.jpg")}\"");
                    idx++;
                }
            }
            string srcAbs = Path.GetFullPath(源图路径);
            string tileArgs = $"-y -i \"{srcAbs}\" -filter_complex \"{filter}\"{outputs} -q:v 3";
            if (!运行ffmpeg(tileArgs, "切图"))
                return;

            // 3. 预览图（1/8 缩放，宽高向下取 4 倍数——非 4 倍数会被拒块压缩回退 RGB24，docs/14 §8.2 ②b）
            int pvW = Mathf.Max(4, W / 8 / 4 * 4);
            int pvH = Mathf.Max(4, H / 8 / 4 * 4);
            string pvAbs = Path.GetFullPath(预览图路径);
            if (!运行ffmpeg($"-y -i \"{srcAbs}\" -vf scale={pvW}:{pvH} -q:v 4 \"{pvAbs}\"", "预览图"))
                return;

            AssetDatabase.Refresh();

            // 4. 导入设置：mip 关（大图开 mip 块压缩失效，docs/14 §8.2）+ Automatic+Compressed +
            //    Sprite 单模式 + maxSize 4096（>2056 防缩，Android 默认上限 2048 会截掉重叠边）
            foreach (var path in 所有瓦片路径(cols, rows))
                设置导入参数(path);
            设置导入参数(预览图路径);

            // 5. Addressables：移除全图与旧瓦片条目，瓦片入 MapAssets 组
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                GICLog.Error("[MapTileBakeTool] AddressableAssetSettings 未初始化");
                return;
            }
            var group = settings.FindGroup(组名);
            if (group == null)
            {
                GICLog.Error($"[MapTileBakeTool] Addressables 组 {组名} 不存在");
                return;
            }
            var toRemove = new List<AddressableAssetEntry>();
            foreach (var e in group.entries)
                if (e.address == 全图地址 || e.address.StartsWith(地址前缀))
                    toRemove.Add(e);
            foreach (var e in toRemove)
                group.RemoveAssetEntry(e);

            foreach (var path in 所有瓦片路径(cols, rows))
            {
                string name = Path.GetFileNameWithoutExtension(path); // tile_x_y
                string guid = AssetDatabase.AssetPathToGUID(path);
                var entry = settings.CreateOrMoveEntry(guid, group, false, false);
                entry.address = 地址前缀 + name.Substring(5); // 去掉 "tile_" 前缀重复
            }
            EditorUtility.SetDirty(group);
            AssetDatabase.SaveAssets();
            GICLog.Info($"[MapTileBakeTool] Addressables 组 {组名}：移除 {toRemove.Count} 条（含全图），新增 {count} 瓦片");

            // 6. 网格参数写入 MapConfig（瓦片层与标定尺寸换算的数据源）
            var cfg = AssetDatabase.LoadAssetAtPath<MapConfig>(MapConfig路径);
            if (cfg == null)
            {
                GICLog.Error($"[MapTileBakeTool] MapConfig 未找到: {MapConfig路径}");
                return;
            }
            var so = new SerializedObject(cfg);
            so.FindProperty("sourcePixelWidth").intValue = W;
            so.FindProperty("sourcePixelHeight").intValue = H;
            so.FindProperty("tileColumns").intValue = cols;
            so.FindProperty("tileRows").intValue = rows;
            so.FindProperty("tilePixelSize").intValue = 瓦片像素;
            so.FindProperty("tileOverlapPx").intValue = 重叠像素;
            so.FindProperty("tileAddressPrefix").stringValue = 地址前缀;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();

            // 7. 场景接线：MapPlane 底图换预览图 + TileRoot 挂 MapTileLayer
            接线场景();

            GICLog.Info($"[MapTileBakeTool] ✓ 完成：{count} 瓦片 + 预览图 {pvW}x{pvH}；" +
                        "全图已移出 Addressables（编辑器工具仍按路径使用）。请 Play 目测瓦片加载与接缝");
        }

        private static IEnumerable<string> 所有瓦片路径(int cols, int rows)
        {
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                    yield return $"{瓦片目录}/tile_{x}_{y}.jpg";
        }

        private static void 设置导入参数(string path)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter imp) return;
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spritePixelsPerUnit = 50;
            imp.mipmapEnabled = false;
            imp.textureCompression = TextureImporterCompression.Compressed; // Automatic+Compressed（显式格式覆盖不可靠）
            imp.maxTextureSize = 4096;
            imp.SaveAndReimport();
        }

        private static bool 运行ffmpeg(string args, string 阶段)
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg路径,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            string err = p.StandardError.ReadToEnd(); // ffmpeg 全部输出走 stderr
            p.WaitForExit();
            if (p.ExitCode != 0)
            {
                GICLog.Error($"[MapTileBakeTool] ffmpeg {阶段}失败（ExitCode={p.ExitCode}）：\n{err}");
                return false;
            }
            return true;
        }

        /// <summary>MapScreen 场景：MapPlane 换预览图 + TileRoot/MapTileLayer 接线，保存后重开断言</summary>
        private static void 接线场景()
        {
            var scene = EditorSceneManager.OpenScene(MapScreen场景, OpenSceneMode.Single);
            var screens = Object.FindObjectsOfType<MapScreen>(true);
            if (screens.Length != 1)
            {
                GICLog.Error($"[MapTileBakeTool] MapScreen 组件数量异常: {screens.Length}（预期 1）");
                return;
            }
            var ms = screens[0];
            var msSo = new SerializedObject(ms);
            var plane = msSo.FindProperty("地图贴图").objectReferenceValue as SpriteRenderer;
            var mapWorld = plane.transform.parent;

            // MapPlane → 预览图（场景引用变小图；全图只在编辑器内存中临时换上）
            var preview = AssetDatabase.LoadAssetAtPath<Sprite>(预览图路径);
            var planeSo = new SerializedObject(plane);
            planeSo.FindProperty("m_Sprite").objectReferenceValue = preview;
            planeSo.ApplyModifiedPropertiesWithoutUndo();

            // TileRoot（无则建）挂 MapTileLayer
            var tileRootT = mapWorld.Find("TileRoot");
            if (tileRootT == null)
            {
                var root = new GameObject("TileRoot");
                root.transform.SetParent(mapWorld, false);
                tileRootT = root.transform;
            }
            var layer = tileRootT.GetComponent<MapTileLayer>();
            if (layer == null) layer = tileRootT.gameObject.AddComponent<MapTileLayer>();
            var layerSo = new SerializedObject(layer);
            layerSo.FindProperty("mapConfig").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<MapConfig>(MapConfig路径);
            layerSo.FindProperty("mapCamera").objectReferenceValue =
                (msSo.FindProperty("地图相机").objectReferenceValue as MapCameraController)?.GetComponent<Camera>();
            layerSo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            // 保存→重开→断言闭环（P10 铁律）
            EditorSceneManager.OpenScene(MapScreen场景, OpenSceneMode.Single);
            var re = Object.FindObjectsOfType<MapScreen>(true);
            var reSo = new SerializedObject(re[0]);
            var rePlane = reSo.FindProperty("地图贴图").objectReferenceValue as SpriteRenderer;
            var reLayer = rePlane.transform.parent.Find("TileRoot")?.GetComponent<MapTileLayer>();
            bool ok = rePlane.sprite != null && rePlane.sprite.name == "all_map_preview"
                   && reLayer != null
                   && new SerializedObject(reLayer).FindProperty("mapConfig").objectReferenceValue != null
                   && new SerializedObject(reLayer).FindProperty("mapCamera").objectReferenceValue != null;
            if (!ok)
                GICLog.Error("[MapTileBakeTool] 场景接线断言失败（TileLayer/预览图/引用不全），请检查 MapScreen 场景");
            else
                GICLog.Info("[MapTileBakeTool] 场景接线断言通过：MapPlane=预览图，TileRoot.MapTileLayer 引用齐全");
        }
    }
}
#endif
