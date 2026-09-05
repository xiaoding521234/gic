#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization.Tables;
using GIC.Data;

namespace GIC.Tool
{
    /// <summary>
    /// 存档系统分区重组配套初始化工具（2026-09-05）：
    /// ① 创建 Resources/Configs/InitialSaveConfig.asset（初始卡牌/货币/默认卡组，数据=旧 PlayerSaveData.InitCards 硬编码迁移）；
    /// ② PopupText 本地化表补 Save_WriteFailed 键（存档写盘失败 toast 文案）。
    /// 两步各自幂等（资产已存在/键已存在则跳过）。自动执行协议：项目根存在
    /// .codely-cli/tmp/SaveSystemSetup.run.flag 时自动执行一次并写 result.txt，然后删除 flag。
    /// </summary>
    public static class SaveSystemSetupTool
    {
        private const string AssetPath = "Assets/Resources/Configs/InitialSaveConfig.asset";
        private const string LocTable = "PopupText";
        private const string LocKey = "Save_WriteFailed";
        private const string LocValueZh = "存档保存失败，请检查磁盘空间";
        private const string LocValueEn = "Failed to save game data. Please check disk space.";

        [MenuItem("Tools/TG/存档系统初始化配置")]
        public static void Run()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[SaveSystemSetup] " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            CreateInitialSaveConfig(sb);
            AddLocalizationKey(sb);
            sb.AppendLine("DONE");
            Debug.Log(sb.ToString());
            WriteResult(sb.ToString());
        }

        /// <summary>建初始存档配置资产（幂等：已存在则报告 exists 跳过）</summary>
        private static void CreateInitialSaveConfig(System.Text.StringBuilder sb)
        {
            if (AssetDatabase.LoadAssetAtPath<InitialSaveConfig>(AssetPath) != null)
            {
                sb.AppendLine($"InitialSaveConfig.asset: already exists, skipped");
                return;
            }

            var cfg = ScriptableObject.CreateInstance<InitialSaveConfig>();

            // ── 初始角色（count=1；decks=所属卡组）——数据迁移自旧 PlayerSaveData.InitCards ──
            cfg.initialUnits.Add(new InitialSaveConfig.UnitEntry { unit = UnitName.Paimon });
            cfg.initialUnits.Add(new InitialSaveConfig.UnitEntry { unit = UnitName.Zibai,      decks = new List<int> { 1 } });
            cfg.initialUnits.Add(new InitialSaveConfig.UnitEntry { unit = UnitName.Linnea,     decks = new List<int> { 1 } });
            cfg.initialUnits.Add(new InitialSaveConfig.UnitEntry { unit = UnitName.Illuga,     decks = new List<int> { 1 } });
            cfg.initialUnits.Add(new InitialSaveConfig.UnitEntry { unit = UnitName.Amber,      decks = new List<int> { 0 } });
            cfg.initialUnits.Add(new InitialSaveConfig.UnitEntry { unit = UnitName.Kaeya,      decks = new List<int> { 0 } });
            cfg.initialUnits.Add(new InitialSaveConfig.UnitEntry { unit = UnitName.Barbara,    decks = new List<int> { 0 } });
            cfg.initialUnits.Add(new InitialSaveConfig.UnitEntry { unit = UnitName.Xingqiu,    decks = new List<int> { 2 } });
            cfg.initialUnits.Add(new InitialSaveConfig.UnitEntry { unit = UnitName.Beidou,      decks = new List<int> { 2 } });
            cfg.initialUnits.Add(new InitialSaveConfig.UnitEntry { unit = UnitName.Hutao,       decks = new List<int> { 2 } });
            cfg.initialUnits.Add(new InitialSaveConfig.UnitEntry { unit = UnitName.Mizuki,     decks = new List<int> { 3 } });
            cfg.initialUnits.Add(new InitialSaveConfig.UnitEntry { unit = UnitName.Gorou,       decks = new List<int> { 3 } });
            cfg.initialUnits.Add(new InitialSaveConfig.UnitEntry { unit = UnitName.Kirara,      decks = new List<int> { 3 } });

            // ── 初始物品/货币 ──
            cfg.initialItems.Add(new InitialSaveConfig.ItemEntry { item = ItemName.Mora,            count = 100 });
            cfg.initialItems.Add(new InitialSaveConfig.ItemEntry { item = ItemName.IntertwinedFate, count = 60,  decks = new List<int> { 0 } });
            cfg.initialItems.Add(new InitialSaveConfig.ItemEntry { item = ItemName.Stamina,         count = 100, decks = new List<int> { 0 } });
            cfg.initialItems.Add(new InitialSaveConfig.ItemEntry { item = ItemName.Primogem,        count = 16000 });
            cfg.initialItems.Add(new InitialSaveConfig.ItemEntry { item = ItemName.Apple,           count = 10 });
            cfg.initialItems.Add(new InitialSaveConfig.ItemEntry { item = ItemName.RawMeat,         count = 10 });
            cfg.initialItems.Add(new InitialSaveConfig.ItemEntry { item = ItemName.Egg,             count = 10 });
            cfg.initialItems.Add(new InitialSaveConfig.ItemEntry { item = ItemName.Wheat,           count = 10 });
            cfg.initialItems.Add(new InitialSaveConfig.ItemEntry { item = ItemName.Radish,         count = 10 });

            cfg.defaultDeck = 1;

            AssetDatabase.CreateAsset(cfg, AssetPath);
            AssetDatabase.SaveAssets();
            sb.AppendLine($"InitialSaveConfig.asset: created ({AssetPath})");
        }

        /// <summary>PopupText 表补 Save_WriteFailed 键（幂等：键已存在则报告 exists 跳过；逐 locale 报告）</summary>
        private static void AddLocalizationKey(System.Text.StringBuilder sb)
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection(LocTable);
            if (collection == null)
            {
                sb.AppendLine($"Localization: table '{LocTable}' NOT FOUND (skip)");
                return;
            }

            var sharedData = collection.SharedData;

            // 幂等守卫：按 Key 查重（gic-save-system/本地化脚本幂等铁律）
            SharedTableData.SharedTableEntry sharedEntry = null;
            foreach (var e in sharedData.Entries)
            {
                if (e.Key == LocKey) { sharedEntry = e; break; }
            }

            if (sharedEntry == null)
            {
                sharedEntry = sharedData.AddKey(LocKey);
                EditorUtility.SetDirty(sharedData);
                sb.AppendLine($"Localization: key '{LocKey}' created (shared)");
            }
            else
            {
                sb.AppendLine($"Localization: key '{LocKey}' already in shared data (skip create)");
            }

            int added = 0;
            foreach (var tableRef in collection.Tables)   // LocalizationCsvTool 同款枚举（StringTableCollection 无 GetTables()——2026-09-05 实证）
            {
                var st = tableRef.asset as StringTable;
                if (st == null) continue;

                if (st.GetEntry(sharedEntry.Id) != null)
                    continue;   // 该 locale 已有值，幂等跳过

                // zh 系 locale 给中文，其余给英文兜底（LocaleIdentifier 是 struct，勿与 null 比较——恒真警告）
                string code = st.LocaleIdentifier.Code;
                string value = !string.IsNullOrEmpty(code) && code.StartsWith("zh")
                    ? LocValueZh
                    : LocValueEn;
                st.AddEntry(sharedEntry.Id, value);
                EditorUtility.SetDirty(st);
                added++;
                sb.AppendLine($"Localization: entry added for locale '{st.LocaleIdentifier}'");
            }

            if (added > 0)
                AssetDatabase.SaveAssets();
            if (added == 0)
                sb.AppendLine("Localization: no new locale entries needed");
        }

        [InitializeOnLoadMethod]
        private static void AutoRunHook()
        {
            void Check()
            {
                string flag = Path.Combine(Application.dataPath, "..", ".codely-cli", "tmp", "SaveSystemSetup.run.flag");
                if (File.Exists(flag))
                {
                    File.Delete(flag);
                    Run();
                }
            }
            EditorApplication.delayCall += Check;
            EditorApplication.projectChanged += () => EditorApplication.delayCall += Check;
        }

        private static void WriteResult(string content)
        {
            try
            {
                string outPath = Path.Combine(Application.dataPath, "..", ".codely-cli", "tmp", "SaveSystemSetup.result.txt");
                File.WriteAllText(outPath, content);
            }
            catch { }
        }
    }
}
#endif
