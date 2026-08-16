#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Localization.Tables;
using UnityEditor.Localization;
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Editor
{


    /// <summary>
    /// 本地化 CSV 导出导入工具
    /// Menu: Tools/Localization/CSV 导出导入
    /// CSV 格式: Key,Id,zh-Hans,zh-TW,en,ja,ru
    /// 一个表一个文件: {TableName}.csv (如 SkillDescription.csv)
    /// </summary>
    public class LocalizationCsvTool : EditorWindow
    {
        [SerializeField] private string csvFolder = "Export/Localization";
        private Dictionary<string, bool> tableSelectionMap = new Dictionary<string, bool>();
        private VisualElement tableListContainer;

        [MenuItem("Tools/Localization/CSV 导出导入", priority = -78)]
        public static void ShowWindow()
        {
            var window = GetWindow<LocalizationCsvTool>("本地化 CSV 工具");
            window.minSize = new Vector2(600, 500);
            window.Initialize();
            window.Show();
        }

        private void Initialize()
        {
            tableSelectionMap.Clear();
            foreach (TableName name in Enum.GetValues(typeof(TableName)))
            {
                tableSelectionMap[name.ToString()] = true;
            }
        }

        // CreateGUI 为按名调用的魔法方法（Tuanjie 中非虚方法），不加 override
        private void CreateGUI()
        {
            if (tableSelectionMap.Count == 0) Initialize();

            var root = rootVisualElement;
            root.style.paddingLeft = 10;
            root.style.paddingRight = 10;
            root.style.paddingTop = 8;

            root.Add(ConfigEditorUITK.CreateTitleRow("本地化 CSV 导出导入工具", 0));

            var pathField = new TextField("CSV 文件夹路径（相对项目根目录）");
            pathField.value = csvFolder;
            pathField.RegisterValueChangedCallback(e => csvFolder = e.newValue);
            root.Add(pathField);

            root.Add(new HelpBox(
                "导出: 每张表生成 {TableName}.csv 文件\n" +
                "导入: 从该文件夹读取 {TableName}.csv 文件\n" +
                "CSV 可用 Excel / Google Sheets / VSCode 编辑", HelpBoxMessageType.Info));

            // ===== 工具行：全选/全不选 | 打开文件夹 | 刷新 =====
            var toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.marginTop = 8;
            root.Add(toolbar);

            var selectAllBtn = new Button(() => { SetAll(true); RebuildTableList(); }) { text = "全选" };
            selectAllBtn.style.width = 60;
            toolbar.Add(selectAllBtn);

            var selectNoneBtn = new Button(() => { SetAll(false); RebuildTableList(); }) { text = "全不选" };
            selectNoneBtn.style.width = 60;
            selectNoneBtn.style.marginLeft = 4;
            toolbar.Add(selectNoneBtn);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1f;
            toolbar.Add(spacer);

            var openBtn = new Button(OpenFolder) { text = "打开文件夹" };
            openBtn.style.width = 90;
            toolbar.Add(openBtn);

            var refreshBtn = new Button(RebuildTableList) { text = "刷新" };
            refreshBtn.style.width = 60;
            refreshBtn.style.marginLeft = 4;
            toolbar.Add(refreshBtn);

            // ===== 表列表 =====
            root.Add(ConfigEditorUITK.CreateSectionHeader("选择要操作的表"));

            var scroll = new ScrollView();
            scroll.style.flexGrow = 1f;
            tableListContainer = new VisualElement();
            scroll.Add(tableListContainer);
            root.Add(scroll);

            // ===== 动作按钮 =====
            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.marginTop = 10;
            root.Add(actions);

            var exportBtn = ConfigEditorUITK.CreatePrimaryButton("导出选中 → CSV", () =>
            {
                ExportSelected();
                RebuildTableList();
            });
            exportBtn.style.height = 40;
            exportBtn.style.flexGrow = 1f;
            actions.Add(exportBtn);

            var importBtn = ConfigEditorUITK.CreatePrimaryButton("CSV → 导入选中", () =>
            {
                ImportSelected();
                RebuildTableList();
            });
            importBtn.style.height = 40;
            importBtn.style.flexGrow = 1f;
            importBtn.style.marginLeft = 8;
            actions.Add(importBtn);

            RebuildTableList();
        }

        private void OpenFolder()
        {
            string fullPath = Path.GetFullPath(csvFolder);
            if (Directory.Exists(fullPath))
                EditorUtility.RevealInFinder(fullPath);
            else
                EditorUtility.DisplayDialog("提示", $"文件夹不存在: {fullPath}\n请先导出一次", "确定");
        }

        /// <summary>重建表列表（条目数只在此处查询，不再每帧刷新）</summary>
        private void RebuildTableList()
        {
            if (tableListContainer == null) return;
            tableListContainer.Clear();

            foreach (TableName name in Enum.GetValues(typeof(TableName)))
            {
                string enumName = name.ToString();
                string displayName = name.GetInspectorName();

                if (!tableSelectionMap.ContainsKey(enumName))
                    tableSelectionMap[enumName] = true;

                // 查询条目数
                var collection = LocalizationEditorSettings.GetStringTableCollection(enumName);
                int entryCount = collection?.SharedData?.Entries?.Count ?? 0;
                string info = entryCount > 0 ? $"({entryCount}条)" : "(空)";

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.paddingLeft = 4;
                row.style.paddingTop = 2;
                row.style.paddingBottom = 2;

                var toggle = new Toggle($"{displayName} [{enumName}]")
                {
                    value = tableSelectionMap[enumName],
                    tooltip = enumName,
                };
                toggle.style.flexGrow = 1f;
                toggle.RegisterValueChangedCallback(e => tableSelectionMap[enumName] = e.newValue);
                row.Add(toggle);

                var count = new Label(info);
                count.style.color = new Color(0.6f, 0.6f, 0.6f);
                count.style.fontSize = 11;
                count.style.marginRight = 6;
                row.Add(count);

                tableListContainer.Add(row);
            }
        }

        private void SetAll(bool selected)
        {
            var keys = new List<string>(tableSelectionMap.Keys);
            foreach (var key in keys)
                tableSelectionMap[key] = selected;
        }

        #region 导出

        private void ExportSelected()
        {
            string folderPath = Path.GetFullPath(csvFolder);
            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);

            int totalTables = 0;
            int totalEntries = 0;
            int skipped = 0;

            foreach (var kvp in tableSelectionMap)
            {
                if (!kvp.Value) continue;

                var collection = LocalizationEditorSettings.GetStringTableCollection(kvp.Key);
                if (collection == null || collection.SharedData == null)
                {
                    skipped++;
                    continue;
                }

                int count = ExportTable(collection, kvp.Key, folderPath);
                if (count >= 0)
                {
                    totalTables++;
                    totalEntries += count;
                }
            }

            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("导出完成",
                $"已导出 {totalTables} 张表，共 {totalEntries} 条数据\n跳过 {skipped} 张（不存在）\n路径: {folderPath}", "确定");
            GICLog.Info($"[LocalizationCsvTool] 导出完成: {totalTables} 表, {totalEntries} 条, 跳过 {skipped}, 路径: {folderPath}");
        }

        private int ExportTable(StringTableCollection collection, string tableName, string folderPath)
        {
            var sharedData = collection.SharedData;
            var entries = sharedData.Entries;
            if (entries == null || entries.Count == 0) return 0;

            // 收集所有 locale table
            var localeCodes = new List<string>();
            var localeTables = new List<StringTable>();
            foreach (var tableRef in collection.Tables)
            {
                var st = tableRef.asset as StringTable;
                if (st != null)
                {
                    localeCodes.Add(st.LocaleIdentifier.Code);
                    localeTables.Add(st);
                }
            }

            var sb = new StringBuilder();

            // Header: Key,Id,locale1,locale2,...
            sb.Append("Key,Id");
            foreach (var code in localeCodes)
                sb.Append(",").Append(code);
            sb.Append("\n");

            // 数据行
            foreach (var entry in entries)
            {
                sb.Append(EscapeCsvField(entry.Key));
                sb.Append(",").Append(entry.Id);

                foreach (var table in localeTables)
                {
                    var te = table.GetEntry(entry.Id);
                    string value = te?.Value ?? "";
                    sb.Append(",").Append(EscapeCsvField(value));
                }
                sb.Append("\n");
            }

            string filePath = Path.Combine(folderPath, $"{tableName}.csv");
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);

            GICLog.Info($"[LocalizationCsvTool] 导出 {tableName}: {entries.Count} 条, {localeCodes.Count} 语言 → {filePath}");
            return entries.Count;
        }

        #endregion

        #region 导入

        private void ImportSelected()
        {
            string folderPath = Path.GetFullPath(csvFolder);
            if (!Directory.Exists(folderPath))
            {
                EditorUtility.DisplayDialog("错误", $"文件夹不存在: {folderPath}", "确定");
                return;
            }

            int totalTables = 0;
            int totalUpdated = 0;
            int totalCreated = 0;
            int skipped = 0;

            foreach (var kvp in tableSelectionMap)
            {
                if (!kvp.Value) continue;

                string filePath = Path.Combine(folderPath, $"{kvp.Key}.csv");
                if (!File.Exists(filePath))
                {
                    skipped++;
                    continue;
                }

                var collection = LocalizationEditorSettings.GetStringTableCollection(kvp.Key);
                if (collection == null)
                {
                    GICLog.Warn($"[LocalizationCsvTool] 表 {kvp.Key} 在 Unity 中不存在，跳过。请先在 Localization Window 中创建。");
                    skipped++;
                    continue;
                }

                int updated, created;
                ImportTable(collection, kvp.Key, filePath, out updated, out created);
                totalTables++;
                totalUpdated += updated;
                totalCreated += created;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("导入完成",
                $"已导入 {totalTables} 张表\n更新 {totalUpdated} 条，新增 {totalCreated} 条\n跳过 {skipped} 张", "确定");
            GICLog.Info($"[LocalizationCsvTool] 导入完成: {totalTables} 表, 更新 {totalUpdated}, 新增 {totalCreated}, 跳过 {skipped}");
        }

        private void ImportTable(StringTableCollection collection, string tableName, string filePath,
            out int updateCount, out int createCount)
        {
            updateCount = 0;
            createCount = 0;

            var sharedData = collection.SharedData;

            // locale code → string table
            var localeMap = new Dictionary<string, StringTable>();
            foreach (var tableRef in collection.Tables)
            {
                var st = tableRef.asset as StringTable;
                if (st != null)
                    localeMap[st.LocaleIdentifier.Code] = st;
            }

            // 读取 CSV
            string content = File.ReadAllText(filePath, Encoding.UTF8);
            var rows = ParseCsv(content);
            if (rows.Count < 2) return;

            // 解析 header
            var header = rows[0];
            if (header.Length < 2 || header[0] != "Key" || header[1] != "Id")
            {
                GICLog.Warn($"[LocalizationCsvTool] {tableName}.csv 格式错误: 首行应为 Key,Id,locale1,locale2,...");
                return;
            }

            // 列索引 → StringTable
            var localeColumns = new Dictionary<int, StringTable>();
            for (int i = 2; i < header.Length; i++)
            {
                string code = header[i];
                if (localeMap.TryGetValue(code, out var t))
                    localeColumns[i] = t;
                else
                    GICLog.Warn($"[LocalizationCsvTool] {tableName}: 找不到 locale '{code}' 的表，该列将被忽略");
            }

            // 数据行
            for (int r = 1; r < rows.Count; r++)
            {
                var fields = rows[r];
                if (fields.Length < 2) continue;

                string key = fields[0];
                if (string.IsNullOrEmpty(key)) continue;

                long id = 0;
                long.TryParse(fields[1], out id);

                // 查找或创建 shared entry
                SharedTableData.SharedTableEntry sharedEntry = null;

                if (id > 0)
                    sharedEntry = sharedData.GetEntry(id);

                if (sharedEntry == null)
                {
                    // 按 Key 查找
                    foreach (var e in sharedData.Entries)
                    {
                        if (e.Key == key)
                        {
                            sharedEntry = e;
                            break;
                        }
                    }
                }

                if (sharedEntry == null)
                {
                    // 新建
                    sharedEntry = sharedData.AddKey(key);
                    createCount++;
                }

                // 更新各 locale 的值
                foreach (var kvp in localeColumns)
                {
                    int col = kvp.Key;
                    var table = kvp.Value;

                    if (col >= fields.Length) continue;

                    string value = fields[col];

                    var existing = table.GetEntry(sharedEntry.Id);
                    if (existing != null)
                    {
                        if (existing.Value != value)
                        {
                            existing.Value = value;
                            updateCount++;
                        }
                    }
                    else if (!string.IsNullOrEmpty(value))
                    {
                        table.AddEntry(sharedEntry.Id, value);
                        updateCount++;
                    }
                }
            }

            // 标记脏
            EditorUtility.SetDirty(sharedData);
            foreach (var t in localeMap.Values)
                EditorUtility.SetDirty(t);

            GICLog.Info($"[LocalizationCsvTool] 导入 {tableName}: 更新 {updateCount}, 新增 {createCount}");
        }

        #endregion

        #region CSV 工具

        /// <summary>
        /// 转义 CSV 字段（RFC 4180）
        /// </summary>
        private static string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r"))
            {
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            }
            return field;
        }

        /// <summary>
        /// 解析 CSV 内容为行数组（支持引号内换行）
        /// </summary>
        private static List<string[]> ParseCsv(string content)
        {
            var rows = new List<string[]>();
            var currentRow = new List<string>();
            var currentField = new StringBuilder();
            bool inQuotes = false;
            int i = 0;

            while (i < content.Length)
            {
                char c = content[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < content.Length && content[i + 1] == '"')
                        {
                            currentField.Append('"');
                            i += 2;
                        }
                        else
                        {
                            inQuotes = false;
                            i++;
                        }
                    }
                    else
                    {
                        currentField.Append(c);
                        i++;
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inQuotes = true;
                        i++;
                    }
                    else if (c == ',')
                    {
                        currentRow.Add(currentField.ToString());
                        currentField.Clear();
                        i++;
                    }
                    else if (c == '\r')
                    {
                        currentRow.Add(currentField.ToString());
                        currentField.Clear();
                        rows.Add(currentRow.ToArray());
                        currentRow.Clear();
                        i++;
                        if (i < content.Length && content[i] == '\n') i++;
                    }
                    else if (c == '\n')
                    {
                        currentRow.Add(currentField.ToString());
                        currentField.Clear();
                        rows.Add(currentRow.ToArray());
                        currentRow.Clear();
                        i++;
                    }
                    else
                    {
                        currentField.Append(c);
                        i++;
                    }
                }
            }

            // 最后一行
            if (currentField.Length > 0 || currentRow.Count > 0)
            {
                currentRow.Add(currentField.ToString());
                rows.Add(currentRow.ToArray());
            }

            return rows;
        }

        #endregion
    }
    #endif

}


