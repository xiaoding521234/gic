#if UNITY_EDITOR
// MapTileBakeTool.cs - 大地图瓦片生成（GDI+ 切图 + Addressables 入组 + MapConfig/场景接线）
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
    /// 瓦片生成工具：从地图源图切 2048 网格瓦片（含 4px 重叠边）→ Assets/Art/Map/Tiles/，
    /// 生成 1/8 预览图 → Assets/Art/Map/Textures/all_map_preview.jpg，
    /// 瓦片入 Addressables 组 MapAssets（地址 MapAssets/Tiles/tile_{x}_{y}），
    /// 网格参数写入 MapConfig，MapScreen 场景接线（MapPlane 保持预览图 + TileRoot）。
    ///
    /// 源图约定（2026-08-18 起）：
    /// - 首选母版 Export/all_map_source.jpg（全分辨率，最高 21900×18576 已验证；不在 Assets，不触发导入）
    /// - 无母版时回退 Assets/Art/Map/Textures/all_map.jpg（编辑器预览副本，16384 档）
    /// - Assets 内的 all_map.jpg 只是编辑器目测用副本（MapEditorFullRes 换图对象），瓦片一律从母版切
    ///
    /// 切图实现：PowerShell System.Drawing（GDI+）外部进程——ffmpeg 定制构建解码上限 16384 宽
    /// （21900 宽 mjpeg 直接 Picture size invalid，2026-08-18 实测），GDI+ 无此限制且质量足够（q95）。
    /// 尺寸读取：JPEG SOF 头解析（纯 IO），避免把巨图导进 Unity（>16384 编辑器导入会 OOM，docs/14 §8.2 ③）。
    /// 幂等：重复执行先清空瓦片目录与组内瓦片条目。
    /// </summary>
    public static class MapTileBakeTool
    {
        private const string previewPath = MapPaths.preview;
        private const string TileDir = MapPaths.TileDir;
        private const string MapConfig路径 = MapPaths.MapConfig;
        private const string MapScreenScene = MapPaths.MapScreenScene;
        private const string groupName = "MapAssets";
        private const string addrPrefix = "MapAssets/Tiles/tile_";
        private const string fullAddr = "MapAssets/Textures/all_map";
        private const int tilePx = 2048;
        private const int overlapPx = 4;

        [MenuItem("Tools/地图/生成地图瓦片", priority = -57)]
        public static void Bake()
        {
            // ── 源图解析：母版优先，回退编辑器副本 ──
            string srcAbs = ResolveSourceImage(out bool useMaster);
            if (srcAbs == null) return;

            var size = ParseJpegSize(srcAbs);
            if (size == null)
            {
                GICLog.Error($"[MapTileBakeTool] JPEG 头解析失败（非 JPEG 或文件损坏）: {srcAbs}");
                return;
            }
            int W = size.Value.x, H = size.Value.y;
            if (W % 4 != 0 || H % 4 != 0)
            {
                GICLog.Error($"[MapTileBakeTool] 源图尺寸 {W}x{H} 非 4 倍数（块压缩前置条件），请外部裁齐后重试");
                return;
            }

            // 1. 清空旧瓦片 + 预览尺寸计算
            if (AssetDatabase.IsValidFolder(TileDir))
                AssetDatabase.DeleteAsset(TileDir);
            AssetDatabase.CreateFolder("Assets/Art/Map", "Tiles");
            string tileAbsDir = Path.GetFullPath(TileDir);
            int pvW = Mathf.Max(4, W / 8 / 4 * 4);
            int pvH = Mathf.Max(4, H / 8 / 4 * 4);

            // 2. GDI+ 切图（外部进程）
            int cols = Mathf.CeilToInt(W / (float)tilePx);
            int rows = Mathf.CeilToInt(H / (float)tilePx);
            GICLog.Info($"[MapTileBakeTool] 开始切图（{(useMaster ? "MasterImage" : "编辑器副本")} {W}x{H}）→ {cols}x{rows} = {cols * rows} 瓦片");
            if (!RunTileScript(srcAbs, tileAbsDir, cols, rows, W, H, pvW, pvH, Path.GetFullPath(previewPath)))
                return;

            ImportAndWire(W, H, cols, rows);
        }

        /// <summary>
        /// 导入已切好的瓦片（切图在 Unity 外部完成时用——110 片切图约 3 分钟，
        /// 与 unity_menu/桥联调时超出 330s 超时会触发自动重试把烘焙叠加多次；
        /// 外部切图 + 本入口导入，编辑器内耗时 <1 分钟不会超时）。
        /// 幂等：瓦片文件已在 Assets/Art/Map/Tiles/ 时直接走导入。
        /// </summary>
        [MenuItem("Tools/地图/导入已切瓦片", priority = -56)]
        public static void ImportOnly()
        {
            string src = ResolveSourceImage(out _);
            var s2 = src != null ? ParseJpegSize(src) : null;
            if (s2 == null)
            {
                GICLog.Error("[MapTileBakeTool] 找不到可解析尺寸的源图（母版/编辑器副本均失败）");
                return;
            }
            int W = s2.Value.x, H = s2.Value.y;
            int cols = Mathf.CeilToInt(W / (float)tilePx);
            int rows = Mathf.CeilToInt(H / (float)tilePx);

            // 完整性校验：瓦片文件必须齐（切图已在外部完成）
            int found = 0;
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                    if (File.Exists($"{TileDir}/tile_{x}_{y}.jpg")) found++;
            if (found < cols * rows)
            {
                GICLog.Error($"[MapTileBakeTool] 瓦片不全：{found}/{cols * rows}。请先在外部完成切图（Temp/map_tile_bake.ps1），再跑本入口");
                return;
            }

            AssetDatabase.Refresh();
            ImportAndWire(W, H, cols, rows);
        }

        /// <summary>源图路径解析（Bake/ImportOnly 共用）：母版优先，无母版回退编辑器全图副本；均不存在返回 null</summary>
        private static string ResolveSourceImage(out bool useMaster)
        {
            string MasterImage = Path.GetFullPath(MapPaths.MasterImage);
            if (File.Exists(MasterImage)) { useMaster = true; return MasterImage; }
            string copy = Path.GetFullPath(MapPaths.FullCopy);
            if (File.Exists(copy)) { useMaster = false; return copy; }
            GICLog.Error($"[MapTileBakeTool] 源图未找到：{MapPaths.MasterImage} 与 {MapPaths.FullCopy} 均不存在");
            useMaster = false;
            return null;
        }

        /// <summary>导入器设置 + Addressables + MapConfig + 场景接线（切图完成后的编辑器内收尾）</summary>
        private static void ImportAndWire(int W, int H, int cols, int rows)
        {
            int count = cols * rows;
            int pvW = Mathf.Max(4, W / 8 / 4 * 4);
            int pvH = Mathf.Max(4, H / 8 / 4 * 4);

            // 3. 导入设置：mip 关（大图开 mip 块压缩失效，docs/14 §8.2）+ Automatic+Compressed +
            //    Sprite 单模式 + maxSize 4096（>2056 防缩，Android 默认上限 2048 会截掉重叠边）
            foreach (var path in AllTilePaths(cols, rows))
                ApplyImportSettings(path);
            ApplyImportSettings(previewPath);

            // 5. Addressables：移除全图与旧瓦片条目，瓦片入 MapAssets 组
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                GICLog.Error("[MapTileBakeTool] AddressableAssetSettings 未初始化");
                return;
            }
            var group = settings.FindGroup(groupName);
            if (group == null)
            {
                GICLog.Error($"[MapTileBakeTool] Addressables 组 {groupName} 不存在");
                return;
            }
            var toRemove = new List<AddressableAssetEntry>();
            foreach (var e in group.entries)
                if (e.address == fullAddr || e.address.StartsWith(addrPrefix))
                    toRemove.Add(e);
            foreach (var e in toRemove)
                group.RemoveAssetEntry(e);

            foreach (var path in AllTilePaths(cols, rows))
            {
                string name = Path.GetFileNameWithoutExtension(path); // tile_x_y
                string guid = AssetDatabase.AssetPathToGUID(path);
                var entry = settings.CreateOrMoveEntry(guid, group, false, false);
                entry.address = addrPrefix + name.Substring(5); // 去掉 "tile_" 前缀重复
            }
            EditorUtility.SetDirty(group);
            AssetDatabase.SaveAssets();
            GICLog.Info($"[MapTileBakeTool] Addressables 组 {groupName}：移除 {toRemove.Count} 条（含全图），新增 {count} 瓦片");

            // 6. 网格参数写入 MapConfig（瓦片层与标定尺寸换算的数据源）
            var cfg = AssetDatabase.LoadAssetAtPath<MapConfig>(MapConfig路径);
            if (cfg == null)
            {
                GICLog.Error($"[MapTileBakeTool] MapConfig 未找到: {MapConfig路径}");
                return;
            }
            var so = new SerializedObject(cfg);
            // 等比换算标定单位：世界尺寸 = sourcePixel × unit 须保持不变（锚点/区域世界坐标不动），
            // 源图像素尺寸变化时 unit 必须随动，否则运行时地图胀缩、锚点全错位（v7.0 换图事故根因）。
            // 非同代等比图（全新地图）换算值无意义，须用取点器标定模式重标覆盖。
            var oldWProp = so.FindProperty("sourcePixelWidth");
            var oldHProp = so.FindProperty("sourcePixelHeight");
            var unitProp = so.FindProperty("worldUnitsPerPixel");
            int oldW = oldWProp.intValue, oldH = oldHProp.intValue;
            float oldUnit = unitProp.floatValue;
            if (oldW > 0 && oldUnit > 0 && oldW != W)
            {
                float newUnit = oldUnit * oldW / W;
                unitProp.floatValue = newUnit;
                GICLog.Info($"[MapTileBakeTool] 源图宽 {oldW}→{W}，标定单位已等比换算 {oldUnit:F8}→{newUnit:F8}（同代等比假设；全新地图请用取点器重标）");
            }
            oldWProp.intValue = W;
            oldHProp.intValue = H;
            so.FindProperty("tileColumns").intValue = cols;
            so.FindProperty("tileRows").intValue = rows;
            so.FindProperty("tilePixelSize").intValue = tilePx;
            so.FindProperty("tileOverlapPx").intValue = overlapPx;
            so.FindProperty("tileAddressPrefix").stringValue = addrPrefix;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();

            // 7. 场景接线：MapPlane 底图保持预览图 + TileRoot 挂 MapTileLayer（幂等）
            WireScene();

            GICLog.Info($"[MapTileBakeTool] ✓ 完成：{count} 瓦片 + 预览图 {pvW}x{pvH}。" +
                        "请 Play 目测瓦片加载、接缝与锚点对齐");
        }

        /// <summary>
        /// 解析 JPEG SOF0-SOF15 段取像素宽高（纯 IO，不解码像素）。
        /// 处理标记前填充字节（0xFF×n）、跳过普通段。
        /// </summary>
        private static Vector2Int? ParseJpegSize(string path)
        {
            using var fs = File.OpenRead(path);
            int b0 = fs.ReadByte(), b1 = fs.ReadByte();
            if (b0 != 0xFF || b1 != 0xD8) return null; // SOI

            while (fs.Position < fs.Length)
            {
                if (fs.ReadByte() != 0xFF) continue;
                int marker = fs.ReadByte();
                if (marker == 0xFF) continue;      // 填充字节
                if (marker == 0xD9 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
                    continue;                       // EOI / TEM / RSTn：无长度段
                if (marker == 0xDA) return null;   // SOS：SOF 应在其前，未遇到=异常

                int lenHi = fs.ReadByte(), lenLo = fs.ReadByte();
                if (lenHi < 0 || lenLo < 0) return null;
                int len = lenHi << 8 | lenLo;

                // SOF0-SOF15（排除 DHT/JPG/DAC）：段内 精度1B + 高2B + 宽2B
                if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                {
                    fs.ReadByte(); // 精度
                    int hHi = fs.ReadByte(), hLo = fs.ReadByte();
                    int wHi = fs.ReadByte(), wLo = fs.ReadByte();
                    if (hHi < 0 || hLo < 0 || wHi < 0 || wLo < 0) return null;
                    return new Vector2Int(wHi << 8 | wLo, hHi << 8 | hLo);
                }
                fs.Seek(len - 2, SeekOrigin.Current); // 跳过段体
            }
            return null;
        }

        /// <summary>生成并运行 GDI+ 切图 PowerShell 脚本（瓦片 + 预览图一次跑完）</summary>
        private static bool RunTileScript(string srcAbs, string tileAbsDir, int cols, int rows,
            int W, int H, int pvW, int pvH, string pvAbs)
        {
            string script = new StringBuilder()
                .AppendLine("$ErrorActionPreference='Stop'")
                .AppendLine("Add-Type -AssemblyName System.Drawing")
                .AppendLine("$src=[System.Drawing.Image]::FromFile($args[0])")            // 0=源图
                .AppendLine("$dir=$args[1]; $cols=[int]$args[2]; $rows=[int]$args[3]")     // 1=瓦片目录 2=列 3=行
                .AppendLine("$W=[int]$args[4]; $H=[int]$args[5]")                          // 4=宽 5=高
                .AppendLine("$codec=[System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' }")
                .AppendLine("$epTile=New-Object System.Drawing.Imaging.EncoderParameters(1); $epTile.Param[0]=New-Object System.Drawing.Imaging.EncoderParameter([System.Drawing.Imaging.Encoder]::Quality,[long]95)")
                .AppendLine("$epPv=New-Object System.Drawing.Imaging.EncoderParameters(1); $epPv.Param[0]=New-Object System.Drawing.Imaging.EncoderParameter([System.Drawing.Imaging.Encoder]::Quality,[long]92)")
                .AppendLine("$T=2048; $O=4")
                .AppendLine("for($y=0;$y -lt $rows;$y++){ for($x=0;$x -lt $cols;$x++){")
                .AppendLine("  $px0=[Math]::Max(0,$x*$T-$O); $py0=[Math]::Max(0,$y*$T-$O)")
                .AppendLine("  $px1=[Math]::Min($W,($x+1)*$T+$O); $py1=[Math]::Min($H,($y+1)*$T+$O)")
                .AppendLine("  $rect=New-Object System.Drawing.Rectangle($px0,$py0,($px1-$px0),($py1-$py0))")
                .AppendLine("  $bmp=$src.Clone($rect,$src.PixelFormat)")
                .AppendLine("  $bmp.Save((Join-Path $dir ('tile_{0}_{1}.jpg' -f $x,$y)),$codec,$epTile); $bmp.Dispose()")
                .AppendLine("} }")
                .AppendLine("$pv=New-Object System.Drawing.Bitmap([int]$args[6],[int]$args[7])") // 6/7=预览宽高
                .AppendLine("$g=[System.Drawing.Graphics]::FromImage($pv); $g.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic")
                .AppendLine("$g.DrawImage($src,(New-Object System.Drawing.Rectangle(0,0,$pv.Width,$pv.Height)),(New-Object System.Drawing.Rectangle(0,0,$src.Width,$src.Height)),[System.Drawing.GraphicsUnit]::Pixel); $g.Dispose()")
                .AppendLine("$pv.Save($args[8],$codec,$epPv); $pv.Dispose(); $src.Dispose()")   // 8=预览输出
                .AppendLine("Write-Output 'PS-OK'")
                .ToString();

            string scriptPath = Path.GetFullPath("Temp/map_tile_bake.ps1");
            File.WriteAllText(scriptPath, script, new UTF8Encoding(true)); // BOM：PS 正确解析

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" " +
                            $"\"{srcAbs}\" \"{tileAbsDir}\" {cols} {rows} {W} {H} {pvW} {pvH} \"{pvAbs}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0 || !stdout.Contains("PS-OK"))
            {
                GICLog.Error($"[MapTileBakeTool] GDI+ 切图失败（ExitCode={p.ExitCode}）：\n{stderr}");
                return false;
            }
            return true;
        }

        private static IEnumerable<string> AllTilePaths(int cols, int rows)
        {
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                    yield return $"{TileDir}/tile_{x}_{y}.jpg";
        }

        private static void ApplyImportSettings(string path)
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

        /// <summary>MapScreen 场景：MapPlane 保持预览图 + TileRoot/MapTileLayer 接线，保存后重开断言</summary>
        private static void WireScene()
        {
            var scene = EditorSceneManager.OpenScene(MapScreenScene, OpenSceneMode.Single);
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

            // MapPlane → 预览图（若已是则空操作；瓦片化后全图永不进场景）
            var preview = AssetDatabase.LoadAssetAtPath<Sprite>(previewPath);
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
            EditorSceneManager.OpenScene(MapScreenScene, OpenSceneMode.Single);
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
