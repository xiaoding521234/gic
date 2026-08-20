#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using GIC.Framework;

namespace GIC.Editor
{
    /// <summary>
    /// 位置背景媒体 Addressables 自动注册工具。
    /// 扫描 Assets/Art/PositionBack/（图片）和 Assets/Art/PositionVideo/（视频）目录，
    /// 按目录结构推导 Addressables 地址（如 PositionVideo/Snezhnaya/snezhnaya_castle_daytime），
    /// 自动创建/同步到 PositionBack 组。新文件导入即自动注册，无需手动加条目。
    /// </summary>
    public static class PositionMediaAddressablesTool
    {
        private const string PositionBackDir = "Assets/Art/PositionBack";
        private const string PositionVideoDir = "Assets/Art/PositionVideo";
        private const string GroupName = "PositionBack";

        /// <summary>手动触发全量同步（菜单入口）</summary>
        [MenuItem("Tools/地图/同步位置媒体到 Addressables", priority = -55)]
        public static void SyncAll()
        {
            int added = 0;
            added += SyncDirectory(PositionBackDir, "PositionBack");
            added += SyncDirectory(PositionVideoDir, "PositionVideo");

            if (added > 0)
            {
                var settings = AddressableAssetSettingsDefaultObject.Settings;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }

            GICLog.Info($"[PositionMedia] 同步完成：新增 {added} 条 Addressables 条目");
        }

        /// <summary>检查指定资产是否已注册，未注册则自动添加（AssetPostprocessor 调用）</summary>
        public static void EnsureRegistered(string assetPath)
        {
            string address = DeriveAddress(assetPath);
            if (address == null) return;

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return;

            var group = settings.FindGroup(GroupName);
            if (group == null) return;

            string guid = AssetDatabase.AssetPathToGUID(assetPath);

            // 已存在同 GUID 的条目 → 检查地址是否一致
            var existing = group.entries.FirstOrDefault(e => e.guid == guid);
            if (existing != null)
            {
                if (existing.address != address)
                {
                    existing.address = address;
                    EditorUtility.SetDirty(group);
                    AssetDatabase.SaveAssets();
                    GICLog.Info($"[PositionMedia] 更新地址: {existing.address} → {address}");
                }
                return;
            }

            // 不存在 → 创建
            settings.CreateOrMoveEntry(guid, group, false, false);
            var entry = group.entries.FirstOrDefault(e => e.guid == guid);
            if (entry != null) entry.address = address;

            EditorUtility.SetDirty(group);
            AssetDatabase.SaveAssets();
            GICLog.Info($"[PositionMedia] 自动注册: {address}");
        }

        /// <summary>
        /// 扫描目录下所有资产，按目录结构推导地址并同步到 Addressables 组。
        /// 返回新增条目数。
        /// </summary>
        private static int SyncDirectory(string dir, string addressPrefix)
        {
            if (!Directory.Exists(dir)) return 0;

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) { GICLog.Warn("[PositionMedia] Addressables settings not found"); return 0; }

            var group = settings.FindGroup(GroupName);
            if (group == null) { GICLog.Warn($"[PositionMedia] Group '{GroupName}' not found"); return 0; }

            // 收集所有支持的资产扩展名
            var extensions = new HashSet<string> { ".png", ".jpg", ".jpeg", ".mp4", ".mov", ".webm" };
            int added = 0;

            var files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()));

            foreach (var file in files)
            {
                string assetPath = file.Replace('\\', '/');
                string guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(guid)) continue;

                string address = DeriveAddress(assetPath);
                if (address == null) continue;

                var existing = group.entries.FirstOrDefault(e => e.guid == guid);
                if (existing == null)
                {
                    var entry = settings.CreateOrMoveEntry(guid, group, false, false);
                    if (entry != null) entry.address = address;
                    added++;
                }
                else if (existing.address != address)
                {
                    existing.address = address;
                }
            }

            return added;
        }

        /// <summary>
        /// 从资产路径推导 Addressables 地址。
        /// Assets/Art/PositionVideo/Snezhnaya/snezhnaya_castle_daytime.mp4 → PositionVideo/Snezhnaya/snezhnaya_castle_daytime
        /// Assets/Art/PositionBack/Nodkrai/nasha_town_daytime.png → PositionBack/Nodkrai/nasha_town_daytime
        /// </summary>
        private static string DeriveAddress(string assetPath)
        {
            assetPath = assetPath.Replace('\\', '/');

            string prefix;
            if (assetPath.StartsWith(PositionBackDir + "/"))
                prefix = "PositionBack";
            else if (assetPath.StartsWith(PositionVideoDir + "/"))
                prefix = "PositionVideo";
            else
                return null;

            // 去掉 Assets/Art/{PositionBack|PositionVideo}/ 前缀
            string relative = assetPath.Substring(assetPath.IndexOf('/') + 1); // Art/PositionVideo/...
            relative = relative.Substring(relative.IndexOf('/') + 1);          // PositionVideo/...
            relative = relative.Substring(relative.IndexOf('/') + 1);          // Snezhnaya/snezhnaya_castle_daytime.mp4

            // 去掉扩展名
            string noExt = Path.ChangeExtension(relative, null);

            return $"{prefix}/{noExt}";
        }
    }

    /// <summary>
    /// 资产导入后处理器：PositionBack/ 和 PositionVideo/ 目录下的新文件自动注册到 Addressables。
    /// </summary>
    public class PositionMediaAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            foreach (var path in importedAssets)
            {
                if (path.StartsWith("Assets/Art/PositionBack/") || path.StartsWith("Assets/Art/PositionVideo/"))
                {
                    PositionMediaAddressablesTool.EnsureRegistered(path);
                }
            }
        }
    }
}
#endif
