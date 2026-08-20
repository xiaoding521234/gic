// ============================================
// PositionConfigEditor - UI Toolkit 版 Inspector 与编辑窗口
// ============================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using GIC.Framework;
using GIC.Data;
using GIC.Tool;

namespace GIC.Editor
{
    [CustomEditor(typeof(PositionConfig))]
    public class PositionConfigEditor : ConfigInspectorBase
    {
        private const string DAYTIME_PATH = "Assets/Resources/Audios/Daytime";
        private const string NIGHT_PATH = "Assets/Resources/Audios/Night";

        protected override string ListPropertyName => "mapDataList";
        protected override string ListHeaderTitle => "地图数据列表";

        protected override string GetElementName(SerializedProperty element)
            => ((PositionName)element.FindPropertyRelative("position").intValue).GetInspectorName();

        protected override string GetElementBadge(SerializedProperty element)
            => ((AudioTheme)element.FindPropertyRelative("audioTheme").intValue) switch
            {
                AudioTheme.None => null,
                var t => t.GetInspectorName()
            };

        protected override void EditElement(int index)
            => PositionDataEditorWindow.OpenWindow((PositionConfig)target, index);

        protected override VisualElement BuildTools(SerializedObject so)
        {
            var config = (PositionConfig)target;
            var section = ConfigEditorUITK.CreateToolsSection("批量填充工具");
            section.Add(ConfigEditorUITK.CreateToolButton("根据前缀自动填充所有位置的音乐", () => AutoFillAllPositions(config)));
            section.Add(ConfigEditorUITK.CreateToolButton("清空所有位置的音乐", () => ClearAllAudios(config), danger: true));
            return section;
        }

        #region 批量填充（逻辑与 IMGUI 版一致，日志走 GICLog）

        private void AutoFillAllPositions(PositionConfig config)
        {
            Dictionary<string, AudioClip> daytimeClips = LoadAllClipsFromFolder(DAYTIME_PATH);
            Dictionary<string, AudioClip> nightClips = LoadAllClipsFromFolder(NIGHT_PATH);

            GICLog.Info($"找到 {daytimeClips.Count} 个白天音频, {nightClips.Count} 个晚上音频");

            int filledCount = 0;
            int skippedCount = 0;

            foreach (var positionData in config.mapDataList)
            {
                if (positionData.audioTheme == AudioTheme.None)
                {
                    skippedCount++;
                    continue;
                }

                string prefix = positionData.audioTheme.ToString().ToSnakeCase();
                bool filled = false;

                var matchedDayClips = FindClipsByPrefix(daytimeClips, prefix);
                if (matchedDayClips.Count > 0)
                {
                    if (positionData.dayAudios == null)
                    {
                        positionData.dayAudios = new AudioClipRandom();
                    }
                    else
                    {
                        positionData.dayAudios.Clear();
                    }

                    foreach (var clip in matchedDayClips)
                    {
                        positionData.dayAudios.AddClip(clip);
                    }
                    filled = true;
                }

                var matchedNightClips = FindClipsByPrefix(nightClips, prefix);
                if (matchedNightClips.Count > 0)
                {
                    if (positionData.nightAudios == null)
                    {
                        positionData.nightAudios = new AudioClipRandom();
                    }
                    else
                    {
                        positionData.nightAudios.Clear();
                    }

                    foreach (var clip in matchedNightClips)
                    {
                        positionData.nightAudios.AddClip(clip);
                    }
                    filled = true;
                }

                if (filled)
                {
                    filledCount++;
                    GICLog.Info($"已填充: {positionData.position} (主题: {prefix}), 白天:{matchedDayClips.Count}首, 晚上:{matchedNightClips.Count}首");
                }
                else
                {
                    GICLog.Warn($"未找到匹配音频: {positionData.position} (主题: {prefix})");
                }
            }

            EditorUtility.SetDirty(config);
            GICLog.Info($"填充完成: 成功 {filledCount} 个位置, 跳过 {skippedCount} 个位置 (无主题)");
        }

        private void ClearAllAudios(PositionConfig config)
        {
            foreach (var positionData in config.mapDataList)
            {
                positionData.dayAudios?.Clear();
                positionData.nightAudios?.Clear();
            }

            EditorUtility.SetDirty(config);
            GICLog.Info("已清空所有位置的音乐");
        }

        private Dictionary<string, AudioClip> LoadAllClipsFromFolder(string folderPath)
        {
            Dictionary<string, AudioClip> clipDict = new Dictionary<string, AudioClip>();

            if (!Directory.Exists(folderPath))
            {
                GICLog.Warn($"文件夹不存在: {folderPath}");
                return clipDict;
            }

            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { folderPath });

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);

                if (clip != null)
                {
                    string fileName = Path.GetFileNameWithoutExtension(assetPath).ToLower();

                    if (!clipDict.ContainsKey(fileName))
                    {
                        clipDict.Add(fileName, clip);
                    }
                }
            }

            return clipDict;
        }

        private List<AudioClip> FindClipsByPrefix(Dictionary<string, AudioClip> clipDict, string prefix)
        {
            List<AudioClip> matchedClips = new List<AudioClip>();
            var sortedKeys = clipDict.Keys.OrderBy(k => k);

            foreach (string fileName in sortedKeys)
            {
                if (fileName.StartsWith(prefix))
                {
                    matchedClips.Add(clipDict[fileName]);
                }
            }

            return matchedClips;
        }

        #endregion
    }

    // ========== 编辑窗口：继承 ElementDataEditorWindowBase（全字段滚动 + 保存到磁盘） ==========

    public class PositionDataEditorWindow : ElementDataEditorWindowBase
    {
        protected override string ListPropertyName => "mapDataList";
        protected override string WindowTitle => "编辑地图数据";
        protected override string MissingDataMessage => "地图数据不存在，可能已被删除";

        protected override string ReadDisplayName(SerializedProperty element)
            => ((PositionName)element.FindPropertyRelative("position").intValue).GetInspectorName();

        public static void OpenWindow(PositionConfig config, int index)
        {
            var window = GetWindow<PositionDataEditorWindow>("编辑地图数据");
            window.OpenInternal(config, index, 500f, 900f);
        }
    }
}
