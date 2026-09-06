#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization.Tables;
using GIC.Data;

namespace GIC.Tool
{
    /// <summary>
    /// 祈愿命运之缘物品化配套（2026-09-06）：
    /// ① ItemConfig.asset：纠缠之缘(1002) countPerServing 10→1；新增相遇之缘(1004) 条目
    ///    （镜像纠缠之缘配置，icon=acquaint_fate.png，countPerServing=1）；
    ///    星辉/相遇之缘/纠缠之缘 maxPrepareCount=0（不可入卡组，2026-09-06 拍板）；
    /// ② 所有 WishPoolConfig：纠缠之线升级权重落盘（必升 1-4 次 = 40/30/20/10，2026-09-06 拍板差异化）；
    /// ③ ItemName/ItemDescription 本地化表补 AcquaintFate 键（Id 对齐枚举值 1004，走 AddKey→RemapId 三步绕行）；
    /// ④ 遗留 Id 错位全链归位（docs/14 §28，RemapId+语言表搬家三步法）；
    /// ⑤ 语言表标准值校对修复（WishFateCanonicalValues，源自 git 1129959）；
    /// ⑥ InitialSaveConfig 命运之缘清零+清卡组成员资格（2026-09-06 拍板：新档 0/0、不可入卡组）；
    /// ⑦ 重导出 ItemName/ItemDescription CSV（格式与 LocalizationCsvTool.ExportTable 完全一致）。
    /// 全部幂等（资产/键已存在则跳过）。自动执行协议：项目根存在
    /// .codely-cli/tmp/WishFateItemsSetup.run.flag 时自动执行一次并写 result.txt，然后删除 flag。
    /// </summary>
    public static class WishFateItemsSetupTool
    {
        private const string ItemConfigPath = "Assets/Resources/Configs/ItemConfig.asset";
        private const string AcquaintIconPath = "Assets/Resources/UI/Items/acquaint_fate.png";
        private const string CsvFolder = "Export/Localization";
        private const string LocKey = "AcquaintFate";
        /// <summary>本地化 Entry Id 对齐 ItemName.AcquaintFate 枚举值</summary>
        private const long AcquaintFateId = 1004;

        [MenuItem("Tools/TG/祈愿命运之缘物品配置")]
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[WishFateItemsSetup] " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            try
            {
                SetupItemConfig(sb);
                SetupIntertwinedWeights(sb);
                AddLocalization(sb, "ItemName", ItemNameValues);
                AddLocalization(sb, "ItemDescription", ItemDescriptionValues);
                AlignLegacyItemIds(sb);
                RestoreCanonicalValues(sb);
                FixInitialSaveFates(sb);
                AssetDatabase.SaveAssets();
                ExportCsv(sb, "ItemName");
                ExportCsv(sb, "ItemDescription");
                AssetDatabase.Refresh();
                sb.AppendLine("DONE");
            }
            catch (System.Exception e)
            {
                sb.AppendLine($"EXCEPTION: {e}");
                Debug.LogError("[WishFateItemsSetup] failed:\n" + e);
            }
            Debug.Log(sb.ToString());
            WriteResult(sb.ToString());
        }

        // ── ① ItemConfig ──

        /// <summary>纠缠之缘每份改 1 + 新增相遇之缘条目（幂等）</summary>
        private static void SetupItemConfig(StringBuilder sb)
        {
            var cfg = AssetDatabase.LoadAssetAtPath<ItemConfig>(ItemConfigPath);
            if (cfg == null)
            {
                sb.AppendLine($"ItemConfig: NOT FOUND at {ItemConfigPath} (skip)");
                return;
            }

            // 纠缠之缘(1002) 每份数量 → 1
            var intertwined = cfg.itemDataList.FirstOrDefault(d => d != null && d.itemID == ItemName.IntertwinedFate);
            if (intertwined == null)
                sb.AppendLine("IntertwinedFate(1002): NOT FOUND in itemDataList");
            else if (intertwined.countPerServing == 1)
                sb.AppendLine("IntertwinedFate(1002): countPerServing already 1 (skip)");
            else
            {
                sb.AppendLine($"IntertwinedFate(1002): countPerServing {intertwined.countPerServing} -> 1");
                intertwined.countPerServing = 1;
                EditorUtility.SetDirty(cfg);
            }

            // 相遇之缘(1004) 新条目（幂等：已存在跳过）
            if (cfg.itemDataList.Any(d => d != null && d.itemID == ItemName.AcquaintFate))
                sb.AppendLine("AcquaintFate(1004): already exists (skip)");
            else if (intertwined == null)
                sb.AppendLine("AcquaintFate(1004): no IntertwinedFate template to mirror (skip)");
            else
            {
                var icon = AssetDatabase.LoadAssetAtPath<Sprite>(AcquaintIconPath);
                if (icon == null)
                    sb.AppendLine($"AcquaintFate(1004): icon missing at {AcquaintIconPath} (skip)");
                else
                {
                    // 镜像纠缠之缘配置（元素/子类型/堆叠/回收等族内一致），每份数量按需求为 1
                    var nd = new ItemConfig.ItemData
                    {
                        itemID = ItemName.AcquaintFate,
                        icon = new List<Sprite> { icon },
                        starLevel = intertwined.starLevel,
                        elements = intertwined.elements == null ? null : (Element[])intertwined.elements.Clone(),
                        subType = intertwined.subType,
                        maxStack = intertwined.maxStack,
                        maxPrepareCount = intertwined.maxPrepareCount,
                        countPerServing = 1,
                        recycleAfterBattle = intertwined.recycleAfterBattle,
                        recycleLossCount = intertwined.recycleLossCount,
                    };
                    cfg.itemDataList.Add(nd);
                    EditorUtility.SetDirty(cfg);
                    sb.AppendLine($"AcquaintFate(1004): created (icon=acquaint_fate.png, countPerServing=1, starLevel={nd.starLevel})");
                }
            }

            // 货币类物品不可入卡组（2026-09-06 拍板）：星辉/相遇之缘/纠缠之缘 maxPrepareCount=0
            // （maxPrepareCount=0 在卡组编辑模式触发遮罩锁定，见 ItemCardViewStrategy）
            foreach (var d in cfg.itemDataList)
            {
                if (d == null) continue;
                if (d.itemID != ItemName.IntertwinedFate && d.itemID != ItemName.AcquaintFate && d.itemID != ItemName.Starglitter)
                    continue;
                if (d.maxPrepareCount == 0)
                {
                    sb.AppendLine($"{d.itemID}: maxPrepareCount already 0 (skip)");
                    continue;
                }
                sb.AppendLine($"{d.itemID}: maxPrepareCount {d.maxPrepareCount} -> 0");
                d.maxPrepareCount = 0;
                EditorUtility.SetDirty(cfg);
            }

            // 体力(1003) 最大备战数 → 60（2026-09-06 拍板；可入卡组不受遮罩限制）
            var stamina = cfg.itemDataList.FirstOrDefault(d => d != null && d.itemID == ItemName.Stamina);
            if (stamina == null)
                sb.AppendLine("Stamina(1003): NOT FOUND in itemDataList");
            else if (stamina.maxPrepareCount == 60)
                sb.AppendLine("Stamina(1003): maxPrepareCount already 60 (skip)");
            else
            {
                sb.AppendLine($"Stamina(1003): maxPrepareCount {stamina.maxPrepareCount} -> 60");
                stamina.maxPrepareCount = 60;
                EditorUtility.SetDirty(cfg);
            }
        }

        // ── ② 纠缠之线升级权重落盘（2026-09-06 拍板：必升 1-4 次 = 40/30/20/10） ──

        /// <summary>把纠缠之线升级权重显式写入所有 WishPoolConfig 资产
        /// （新字段反序列化本就取代码默认 40/30/20/10，此步为把值固化进 YAML——Inspector 可见可改、不随代码默认漂移）</summary>
        private static void SetupIntertwinedWeights(StringBuilder sb)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:WishPoolConfig"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var pool = AssetDatabase.LoadAssetAtPath<WishPoolConfig>(path);
                if (pool == null) continue;

                bool changed = false;
                if (pool.intertwinedUpgrade1Weight != 40f) { pool.intertwinedUpgrade1Weight = 40f; changed = true; }
                if (pool.intertwinedUpgrade2Weight != 30f) { pool.intertwinedUpgrade2Weight = 30f; changed = true; }
                if (pool.intertwinedUpgrade3Weight != 20f) { pool.intertwinedUpgrade3Weight = 20f; changed = true; }
                if (pool.intertwinedUpgrade4Weight != 10f) { pool.intertwinedUpgrade4Weight = 10f; changed = true; }

                // 无论是否改值都 SetDirty：首次运行把新字段序列化进 YAML
                EditorUtility.SetDirty(pool);
                sb.AppendLine(changed
                    ? $"WishPool '{pool.poolName}' ({path}): intertwined weights set 40/30/20/10"
                    : $"WishPool '{pool.poolName}' ({path}): intertwined weights already 40/30/20/10 (serialized)");
            }
        }

        // ── ③ 本地化 ──

        private static readonly Dictionary<string, string> ItemNameValues = new Dictionary<string, string>
        {
            { "zh-Hans", "相遇之缘" },
            { "zh-TW",   "相逢之緣" },
            { "zh-Hant", "相逢之緣" },
            { "en",      "Acquaint Fate" },
            { "ja",      "出会いの縁" },
            { "ru",      "Судьбоносные встречи" },
        };

        private static readonly Dictionary<string, string> ItemDescriptionValues = new Dictionary<string, string>
        {
            { "zh-Hans", "点亮星空的希望之种。无论相隔多远，命定相遇的人都会受缘石辉光的指引，在星空下相会。" },
            { "zh-TW",   "點亮星空的希望之種。無論相隔多遠，命定相遇的人都會受緣石輝光的指引，在星空下相會。" },
            { "zh-Hant", "點亮星空的希望之種。無論相隔多遠，命定相遇的人都會受緣石輝光的指引，在星空下相會。" },
            { "en",      "A seed that lights up the night. No matter the distance apart, guided by the stone's glimmer, the fated will meet under the stars." },
            { "ja",      "星空を照らす希望の種。どんなに離れていても、縁石の導きで運命の二人は星空の下で巡り合える。" },
            { "ru",      "Семена надежды, освещающие ночное небо. Несмотря на расстояние, те, кому суждено встретиться, обязательно найдут друг друга под звёздами." },
        };

        /// <summary>补单表 AcquaintFate 键值（幂等：shared 键按 Key 查重，locale 值按 Id 查重；
        /// AddKey(key,id) 带参版在 Tuanjie 1.9.3 NRE——走 AddKey(key)→RemapId(1004) 三步绕行）</summary>
        private static void AddLocalization(StringBuilder sb, string tableName, Dictionary<string, string> valuesByCode)
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection(tableName);
            if (collection == null)
            {
                sb.AppendLine($"{tableName}: table collection NOT FOUND (skip)");
                return;
            }
            var sharedData = collection.SharedData;

            // 幂等守卫：按 Key 查重（AddKey 对已存在 key 重复调用会新建第二个同 key 条目）
            var sharedEntry = sharedData.Entries.FirstOrDefault(e => e.Key == LocKey);
            if (sharedEntry == null)
            {
                sharedEntry = sharedData.AddKey(LocKey);
                EditorUtility.SetDirty(sharedData);
                sb.AppendLine($"{tableName}: key '{LocKey}' created (auto id {sharedEntry.Id})");
            }
            else
            {
                sb.AppendLine($"{tableName}: key '{LocKey}' already in shared data (id {sharedEntry.Id})");
            }

            // Id 对齐枚举值 1004
            if (sharedEntry.Id != AcquaintFateId)
            {
                if (sharedData.Entries.Any(e => e.Id == AcquaintFateId))
                {
                    sb.AppendLine($"{tableName}: WARN id {AcquaintFateId} already occupied, keep auto id {sharedEntry.Id}");
                }
                else
                {
                    sharedData.RemapId(sharedEntry.Id, AcquaintFateId);
                    EditorUtility.SetDirty(sharedData);
                    sharedEntry = sharedData.Entries.First(e => e.Id == AcquaintFateId);
                    sb.AppendLine($"{tableName}: id remapped {LocKey} -> {AcquaintFateId}");
                }
            }

            int added = 0;
            foreach (var tableRef in collection.Tables)
            {
                var st = tableRef.asset as StringTable;
                if (st == null) continue;

                if (st.GetEntry(sharedEntry.Id) != null)
                {
                    sb.AppendLine($"{tableName}[{st.LocaleIdentifier}]: exists (skip)");
                    continue;
                }

                string code = st.LocaleIdentifier.Code;
                string value = valuesByCode.TryGetValue(code, out var v) ? v : valuesByCode["en"];
                st.AddEntry(sharedEntry.Id, value);
                EditorUtility.SetDirty(st);
                added++;
                sb.AppendLine($"{tableName}[{st.LocaleIdentifier}]: added");
            }

            if (added > 0)
                sb.AppendLine($"{tableName}: {added} locale entries added");
        }

        // ── ④ 遗留 Id 错位归位（skill Id 对齐规范：Entry Id 应与 ItemName 枚举值一致） ──

        /// <summary>
        /// 遗留错位链：Magatama 占 1004（应 3001）、DandelionWine 占 3001（应 5001）、
        /// FireCrystal 占 5001（应 6001）、MysteryKey 占 6001（应 7001）……AcquaintFate 需要 1004。
        /// 必须按依赖序从链尾逐级腾位（plan 顺序即腾位顺序），每步 RemapId + 各语言表条目搬家三步法。
        /// 幂等：已在目标位的跳过；目标位被占的跳过并报告（安全失败，不产生重复 Id）。
        /// </summary>
        private static void AlignLegacyItemIds(StringBuilder sb)
        {
            var plan = new (string key, long targetId)[]
            {
                ("MysteryKey",     7001), // 7001/7002 空闲，先清链尾
                ("AncientScroll",   7002),
                ("FireCrystal",     6001), // 6001/6002 由上两步腾出
                ("WaterCrystal",    6002),
                ("ThunderCrystal",  6003),
                ("WindCrystal",     6004),
                ("IceCrystal",      6005),
                ("RockCrystal",     6006),
                ("GrassCrystal",    6007),
                ("DandelionWine",   5001), // 5001/5002 由晶体腾出
                ("DionaSpecial",    5002),
                ("Magatama",        3001), // 3001 由蒲公英酒腾出
                ("AcquaintFate",    1004), // 1004 由勾玉腾出——本次新键落位
            };

            foreach (var tableName in new[] { "ItemName", "ItemDescription" })
                ApplyIdPlan(sb, tableName, plan);
        }

        /// <summary>单表执行 Id 归位计划（RemapId + 语言表搬家 + 孤儿清理 + 重复 Id 校验）</summary>
        private static void ApplyIdPlan(StringBuilder sb, string tableName, (string key, long targetId)[] plan)
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection(tableName);
            if (collection == null)
            {
                sb.AppendLine($"AlignIds {tableName}: collection NOT FOUND (skip)");
                return;
            }
            var sharedData = collection.SharedData;

            var localeTables = new List<StringTable>();
            foreach (var tableRef in collection.Tables)
            {
                if (tableRef.asset is StringTable st)
                    localeTables.Add(st);
            }

            foreach (var (key, targetId) in plan)
            {
                var entry = sharedData.Entries.FirstOrDefault(e => e.Key == key);
                if (entry == null)
                {
                    sb.AppendLine($"AlignIds {tableName}: key '{key}' not found (skip)");
                    continue;
                }
                if (entry.Id == targetId)
                {
                    sb.AppendLine($"AlignIds {tableName}: {key} already at {targetId} (skip)");
                    continue;
                }
                if (sharedData.Entries.Any(e => e.Id == targetId))
                {
                    sb.AppendLine($"AlignIds {tableName}: {key} -> {targetId} BLOCKED (id occupied), keep {entry.Id}");
                    continue;
                }

                long oldId = entry.Id;
                if (!sharedData.RemapId(oldId, targetId))
                {
                    sb.AppendLine($"AlignIds {tableName}: RemapId {key} {oldId}->{targetId} FAILED (skip)");
                    continue;
                }
                EditorUtility.SetDirty(sharedData);

                // 各语言表条目搬家：旧 id 取值 → 删旧 → 新 id 重加（RemapId 不搬语言表条目）
                foreach (var t in localeTables)
                {
                    var oldTe = t.GetEntry(oldId);
                    if (oldTe == null) continue;
                    string value = oldTe.Value;
                    t.RemoveEntry(oldId);
                    if (t.GetEntry(targetId) == null)
                        t.AddEntry(targetId, value);
                    EditorUtility.SetDirty(t);
                }
                sb.AppendLine($"AlignIds {tableName}: {key} {oldId} -> {targetId} (moved {localeTables.Count} tables)");
            }

            // 校验：shared 无重复 Id；各语言表条目数报告。
            // 注：不做孤儿扫描——真孤儿条目（SharedEntry 引用断裂）访问 KeyId 会递归崩栈，
            // 本次搬家步骤本身即条目跟移（不产生孤儿），孤儿仅可能来自历史 RemoveKey 半执行，风险大于收益。
            var dupes = sharedData.Entries.GroupBy(e => e.Id).Where(g => g.Count() > 1).ToList();
            if (dupes.Count > 0)
                sb.AppendLine($"AlignIds {tableName}: WARN duplicate ids {string.Join(",", dupes.Select(g => g.Key))}");
            foreach (var t in localeTables)
                sb.AppendLine($"AlignIds {tableName}[{t.LocaleIdentifier}]: {t.Values.Count} entries");
        }

        // ── ⑤ 语言表标准值校对修复（WishFateCanonicalValues，源自 git 1129959） ──

        /// <summary>
        /// 2026-07-31 提交 06b8b5e 曾把语言表值挪到枚举位但 Shared 层未同步（三个物品名错挂、
        /// 火/水晶体值丢失、ru 描述表整表被日语覆盖）。今天 Id 归位后按最后已知正确值
        /// （git 1129959，2026-07-29）逐键校对：值不一致的写入标准值。幂等：全对时零改动。
        /// </summary>
        private static void RestoreCanonicalValues(StringBuilder sb)
        {
            foreach (var family in WishFateCanonicalValues.Tables)
                RestoreFamily(sb, family.Key, family.Value);
        }

        private static void RestoreFamily(StringBuilder sb, string tableName, Dictionary<string, Dictionary<string, string>> byLocale)
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection(tableName);
            if (collection == null)
            {
                sb.AppendLine($"Restore {tableName}: collection NOT FOUND (skip)");
                return;
            }
            var sharedData = collection.SharedData;

            foreach (var tableRef in collection.Tables)
            {
                var st = tableRef.asset as StringTable;
                if (st == null) continue;

                string code = st.LocaleIdentifier.Code;
                if (!byLocale.TryGetValue(code, out var canonical))
                {
                    sb.AppendLine($"Restore {tableName}[{code}]: no canonical data (skip)");
                    continue;
                }

                int fixedCount = 0, checkedCount = 0;
                foreach (var kv in canonical)
                {
                    var sharedEntry = sharedData.Entries.FirstOrDefault(e => e.Key == kv.Key);
                    if (sharedEntry == null)
                    {
                        sb.AppendLine($"Restore {tableName}[{code}]: key {kv.Key} missing in shared (skip)");
                        continue;
                    }
                    checkedCount++;

                    var te = st.GetEntry(sharedEntry.Id);
                    string cur = te?.Value;
                    if (cur != kv.Value)
                    {
                        st.AddEntry(sharedEntry.Id, kv.Value); // AddEntry 为 add-or-update
                        EditorUtility.SetDirty(st);
                        fixedCount++;
                        string oldShort = cur == null ? "<missing>" : (cur.Length > 20 ? cur.Substring(0, 20) + "..." : cur);
                        string newShort = kv.Value.Length > 20 ? kv.Value.Substring(0, 20) + "..." : kv.Value;
                        sb.AppendLine($"Restore {tableName}[{code}]: {kv.Key} \"{oldShort}\" -> \"{newShort}\"");
                    }
                }
                sb.AppendLine($"Restore {tableName}[{code}]: checked {checkedCount}, fixed {fixedCount}");
            }
        }

        // ── ⑥ 初始存货命运之缘清零（2026-09-06 拍板：新档不赠送，全靠星辉里程碑获得） ──

        /// <summary>InitialSaveConfig 中相遇之缘/纠缠之缘条目 count 清零 + 清除卡组成员资格
        /// （2026-09-06 拍板：命运之缘不可入卡组；幂等，无条目=天然 0 不动）</summary>
        private static void FixInitialSaveFates(StringBuilder sb)
        {
            var cfg = AssetDatabase.LoadAssetAtPath<InitialSaveConfig>("Assets/Resources/Configs/InitialSaveConfig.asset");
            if (cfg == null)
            {
                sb.AppendLine("InitialSaveConfig: NOT FOUND (skip)");
                return;
            }
            int fixedCount = 0;
            foreach (var e in cfg.initialItems)
            {
                if (e == null) continue;
                if (e.item != ItemName.IntertwinedFate && e.item != ItemName.AcquaintFate) continue;

                if (e.count != 0)
                {
                    sb.AppendLine($"InitialSaveConfig: {e.item} count {e.count} -> 0");
                    e.count = 0;
                    fixedCount++;
                }
                if (e.decks != null && e.decks.Count > 0)
                {
                    sb.AppendLine($"InitialSaveConfig: {e.item} decks cleared ({e.decks.Count} entries)");
                    e.decks = new List<int>();
                    fixedCount++;
                }
            }
            if (fixedCount > 0)
            {
                EditorUtility.SetDirty(cfg);
                AssetDatabase.SaveAssets();
            }
            else
            {
                sb.AppendLine("InitialSaveConfig: fate entries already 0/0 no decks (skip)");
            }
        }

        // ── ⑦ CSV 重导出（与 LocalizationCsvTool.ExportTable 同格式） ──

        private static void ExportCsv(StringBuilder sb, string tableName)
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection(tableName);
            if (collection == null)
            {
                sb.AppendLine($"CSV {tableName}: collection NOT FOUND (skip)");
                return;
            }
            var entries = collection.SharedData.Entries;
            if (entries == null || entries.Count == 0)
            {
                sb.AppendLine($"CSV {tableName}: no entries (skip)");
                return;
            }

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

            var csv = new StringBuilder();
            csv.Append("Key,Id");
            foreach (var code in localeCodes)
                csv.Append(",").Append(code);
            csv.Append("\n");

            foreach (var entry in entries)
            {
                csv.Append(EscapeCsvField(entry.Key));
                csv.Append(",").Append(entry.Id);
                foreach (var table in localeTables)
                {
                    var te = table.GetEntry(entry.Id);
                    string value = te?.Value ?? "";
                    csv.Append(",").Append(EscapeCsvField(value));
                }
                csv.Append("\n");
            }

            string outPath = Path.Combine(Application.dataPath, "..", CsvFolder, $"{tableName}.csv");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, csv.ToString(), System.Text.Encoding.UTF8);
            sb.AppendLine($"CSV {tableName}: exported {entries.Count} entries, {localeCodes.Count} locales -> {outPath}");
        }

        private static string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r"))
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            return field;
        }

        // ── flag 自愈协议（Play 期不消费 flag，退场后下次刷新自愈） ──

        [InitializeOnLoadMethod]
        private static void AutoRunHook()
        {
            void Check()
            {
                string flag = Path.Combine(Application.dataPath, "..", ".codely-cli", "tmp", "WishFateItemsSetup.run.flag");
                if (!File.Exists(flag)) return;
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                File.Delete(flag);
                Run();
            }
            EditorApplication.delayCall += Check;
            EditorApplication.projectChanged += () => EditorApplication.delayCall += Check;
        }

        private static void WriteResult(string content)
        {
            try
            {
                string outPath = Path.Combine(Application.dataPath, "..", ".codely-cli", "tmp", "WishFateItemsSetup.result.txt");
                File.WriteAllText(outPath, content);
            }
            catch { }
        }
    }
}
#endif
