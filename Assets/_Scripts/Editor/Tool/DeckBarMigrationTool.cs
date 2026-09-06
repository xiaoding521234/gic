#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using UnityEngine.UI;
using TMPro;
using GIC.Data;
using GIC.Framework;
using GIC.UI;

namespace GIC.Tool
{
    /// <summary>
    /// 卡组管理面板迁移工具（王者荣耀式，v3）：幂等，可重复执行。
    /// 1. 本地化：UIText 11000 段（Deck_* 键）+ PopupText 顺序续（提示文案）→ 三步法加键、五语言写值、CSV 重导出；
    /// 2. 素材：Wish/UI/button.png border=37（米白胶囊底板）、dropdown_triangle.png（程序化生成▼，缺失时创建）；
    /// 3. 场景 BackpackScreen.unity：
    ///    · DeckBar 长条按钮 = 原神"品质顺序"下拉条样式（米白胶囊 640×80 + 左侧粗深墨蓝文字 + 右侧▼）；
    ///    · Canvas 根下重建 DeckSwitchPanel：全屏遮罩 + medium_popup 面板 + 标题/关闭钮 + DragLayer（拖动行置顶层）+
    ///      ScrollView（Viewport+Mask+Content 纵向布局+ContentSizeFitter+滚动条）+ 底部[新增卡组][导入密语]双按钮；
    ///    · 新建 DeckRow.prefab（CanvasGroup/把手三横条/编号/名称/卡牌预览容器/复制粘贴导出删除钮/高亮框/拖拽浮层）；
    ///    · 接线全部序列化引用；保存场景。
    /// 行由面板运行时按卡组数量实例化；打开面板时行逐个淡入上滑（同背包卡片节奏）。
    /// 执行入口：菜单 Tools/TG/卡组管理面板迁移；或项目根 .codely-cli/tmp/DeckBarMigration.run.flag 自愈协议。
    /// </summary>
    public static class DeckBarMigrationTool
    {
        // ─────────── 路径常量 ───────────
        private const string ScenePath = "Assets/Scenes/BackpackScreen.unity";
        private const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/zh-cn SDF.asset";
        private const string PillSpritePath = "Assets/Resources/UI/Wish/UI/button.png"; // 交易商城按钮底板（290×75 米白胶囊）
        private const string PanelBgSpritePath = "Assets/Resources/UI/Popup/medium_popup.png";
        private const string CloseSpritePath = "Assets/Resources/UI/Buttons/close_button.png";
        private const string RowPrefabPath = "Assets/Resources/Prefabs/Backpack/DeckRow.prefab";
        private const string InputPopupPrefabPath = "Assets/Resources/Prefabs/Popup/InputPopupDialog.prefab";
        private const string TriangleSpritePath = "Assets/Resources/UI/Backpack/dropdown_triangle.png"; // 程序化生成的▼实心三角
        private const string CsvFolder = "Export/Localization";

        // ─────────── 样式基线（原神式米白/深藏青/金；均为场景/预制体默认值，可在编辑器里改） ───────────
        private static readonly Color CreamyWhite = new(0.914f, 0.886f, 0.839f, 1f);
        private static readonly Color DarkGrayText = new(0.196f, 0.196f, 0.196f, 1f); // 交易商城按钮文字色（采样自 WishScreen）
        private static readonly Color InkBlueText = new(0.236f, 0.290f, 0.361f, 1f);  // 原神下拉条文字色（#3C4A5C，采样自用户截图）
        private static readonly Color RowBgColor = new(0.10f, 0.12f, 0.18f, 0.92f);
        private static readonly Color GoldAccent = new(0.75f, 0.62f, 0.32f, 1f); // 选中行整行金色实底（调暗版）
        private static readonly Color BackdropColor = new(0f, 0f, 0f, 0.55f);
        private static readonly Color DragOverlayColor = new(1f, 1f, 1f, 0.15f);

        private const float PanelW = 2200f, PanelH = 1380f;
        private const float RowW = 2100f, RowH = 170f;

        // ─────────── 本地化数据（UIText 11000 段=卡组管理；PopupText 顺序续 6 起） ───────────
        private static readonly (string key, long id, string zh, string tw, string en, string ja, string ru)[] UiKeys =
        {
            ("Deck_PanelTitle",  11001, "切换卡组", "切換卡組", "Switch Decks", "デッキ切替", "Колоды"),
            ("Deck_DefaultName", 11002, "卡组{0}", "卡組{0}", "Deck {0}", "デッキ{0}", "Колода {0}"),
            ("Deck_RenameTitle", 11003, "修改卡组名称", "修改卡組名稱", "Rename Deck", "デッキ名を変更", "Переименовать колоду"),
            ("Deck_ImportTitle", 11004, "输入卡组密语", "輸入卡組密語", "Enter Deck Code", "デッキコードを入力", "Введите код колоды"),
            ("Deck_Copy",        11005, "复制", "複製", "Copy", "コピー", "Копир."),
            ("Deck_Paste",       11006, "粘贴", "貼上", "Paste", "貼り付け", "Вставить"),
            ("Deck_Export",      11007, "导出", "匯出", "Export", "書き出し", "Экспорт"),
            ("Deck_Import",      11008, "导入密语", "導入密語", "Import Code", "コード読込", "Импорт кода"),
            ("Deck_Add",         11009, "新增卡组", "新增卡組", "Add Deck", "デッキ追加", "Добавить"),
            ("Deck_Delete",      11010, "删除", "刪除", "Delete", "削除", "Удалить"),
        };

        private static readonly (string key, long id, string zh, string tw, string en, string ja, string ru)[] PopupKeys =
        {
            ("Deck_Copied",         6, "已复制卡组，点击其他卡组的「粘贴」即可使用", "已複製卡組，點擊其他卡組的「貼上」即可使用", "Deck copied. Paste it onto another deck.", "デッキをコピーしました。他のデッキに貼り付けられます", "Колода скопирована"),
            ("Deck_Pasted",         7, "卡组已粘贴", "卡組已貼上", "Deck pasted", "貼り付けました", "Колода вставлена"),
            ("Deck_EmptyClipboard", 8, "还没有复制过卡组哦", "還沒有複製過卡組哦", "No deck copied yet", "まだコピーしていません", "Сначала скопируйте колоду"),
            ("Deck_ExportCopied",   9, "卡组密语已复制到剪贴板，快分享给好友吧！", "卡組密語已複製到剪貼板，快分享給好友吧！", "Deck code copied to clipboard! Share it with friends!", "コードをコピーしました！友達に共有しよう！", "Код колоды скопирован в буфер обмена!"),
            ("Deck_ImportSuccess", 10, "密语导入成功，已配置当前卡组", "密語導入成功，已配置當前卡組", "Deck code imported!", "コード読込完了！", "Код импортирован!"),
            ("Deck_ImportFailed", 11, "密语无效，请检查后重试", "密語無效，請檢查後重試", "Invalid deck code. Try again.", "無効なコードです", "Неверный код колоды."),
            ("Deck_MissingCards", 12, "有{0}张卡尚未获得，已自动跳过", "有{0}張卡尚未獲得，已自動跳過", "{0} unowned cards were skipped", "未所持のカード{0}枚をスキップしました", "{0} пропущено (нет в коллекции)"),
            ("Deck_Deleted",      13, "已删除卡组「{0}」", "已刪除卡組「{0}」", "Deck \"{0}\" deleted", "デッキ「{0}」を削除しました", "Колода \"{0}\" удалена"),
            ("Deck_MaxReached",   14, "卡组已达上限（{0}个）", "卡組已達上限（{0}個）", "Deck limit reached ({0})", "デッキ上限です（{0}）", "Достигнут предел колод ({0})"),
            ("Deck_MinReached",   15, "至少保留一个卡组哦", "至少保留一個卡組哦", "Keep at least one deck", "最低1つのデッキが必要です", "Нужна хотя бы одна колода"),
        };

        [MenuItem("Tools/TG/卡组管理面板迁移")]
        public static void Run() => RunAndReport();

        // 自动执行协议：项目根存在 .codely-cli/tmp/DeckBarMigration.run.flag 时，
        // 编辑器每次刷新后自动执行一次迁移并删除 flag，结果写入 result.txt（幂等，可重跑）。
        [InitializeOnLoadMethod]
        private static void AutoRunHook()
        {
            void Check()
            {
                string flag = Path.Combine(Application.dataPath, "..", ".codely-cli", "tmp", "DeckBarMigration.run.flag");
                if (!File.Exists(flag)) return;
                // Play 模式守卫（2026-09-06 实证）：OpenScene 在 Play 中被引擎禁止——
                // Play 期间不消费 flag，留到退场后的下一次刷新自愈（否则失败运行会吃掉 flag，永不重试）
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                File.Delete(flag);
                RunAndReport();
            }
            EditorApplication.delayCall += Check;
            EditorApplication.projectChanged += () => EditorApplication.delayCall += Check;
        }

        private static void RunAndReport()
        {
            var log = new List<string>();
            try
            {
                AddLocalization(ref log);
                EnsureSpriteBorders(ref log);
                MigrateScene(ref log);
            }
            catch (Exception e)
            {
                log.Add("EXCEPTION: " + e);
            }
            var report = string.Join("\n", log);
            Debug.Log("[DeckBarMigration] 完成\n" + report);
            try
            {
                string outPath = Path.Combine(Application.dataPath, "..", ".codely-cli", "tmp", "DeckBarMigration.result.txt");
                File.WriteAllText(outPath, "[DeckBarMigration] " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n" + report + "\nDONE");
            }
            catch { }
        }

        // ═══════════════ 第 1 步：本地化加键 + CSV 重导出 ═══════════════

        private static void AddLocalization(ref List<string> log)
        {
            MigrateTable("UIText", UiKeys, ref log);
            MigrateTable("PopupText", PopupKeys, ref log);
        }

        private static void MigrateTable(string tableName,
            (string key, long id, string zh, string tw, string en, string ja, string ru)[] keys,
            ref List<string> log)
        {
            var collection = LocalizationEditorSettings.GetStringTableCollections()
                .FirstOrDefault(c => c.name == tableName);
            if (collection == null || collection.SharedData == null)
            {
                log.Add($"LOCAL {tableName}: collection NOT FOUND, skip");
                return;
            }
            var sharedData = collection.SharedData;

            var localeTables = new List<StringTable>();
            foreach (var tr in collection.Tables)
                if (tr.asset is StringTable st)
                    localeTables.Add(st);

            foreach (var (key, id, zh, tw, stringEn, ja, ru) in keys)
            {
                var values = new Dictionary<string, string>
                {
                    ["zh-Hans"] = zh, ["zh-TW"] = tw, ["en"] = stringEn, ["ja"] = ja, ["ru"] = ru,
                };
                EnsureLocalizedKey(sharedData, localeTables, key, id, values, ref log);
            }

            EditorUtility.SetDirty(sharedData);
            foreach (var t in localeTables) EditorUtility.SetDirty(t);
            AssetDatabase.SaveAssets();

            ExportTableCsv(collection, tableName, ref log);
        }

        /// <summary>三步法加键（gic-localization skill）：AddKey 无参 → RemapId 归位分段 id → 各语言写值；幂等可重跑</summary>
        private static void EnsureLocalizedKey(SharedTableData sharedData, List<StringTable> localeTables,
            string key, long targetId, Dictionary<string, string> values, ref List<string> log)
        {
            // ① 同 key 重复条目防御（AddKey 无参版不查重）：只留一个
            var dupes = sharedData.Entries.Where(e => e.Key == key).ToList();
            for (int i = 1; i < dupes.Count; i++)
            {
                foreach (var t in localeTables) t.RemoveEntry(dupes[i].Id);
                sharedData.RemoveKey(dupes[i].Id);
                log.Add($"LOCAL {key}: removed duplicate id={dupes[i].Id}");
            }

            var entry = sharedData.Entries.FirstOrDefault(e => e.Key == key);
            if (entry == null)
            {
                entry = sharedData.AddKey(key); // 无参版（带参版在 1.5.3 NRE，勿用）
                log.Add($"LOCAL {key}: AddKey -> temp id {entry.Id}");
            }
            long oldId = entry.Id;

            // ② 归位分段 id（目标被占则顺延找空位）
            long finalId = targetId;
            while (sharedData.Entries.Any(e => e.Id == finalId && e.Key != key)) finalId++;
            if (oldId != finalId)
            {
                if (!sharedData.RemapId(oldId, finalId))
                {
                    log.Add($"LOCAL {key}: RemapId {oldId}->{finalId} FAILED (keep {oldId})");
                    finalId = oldId;
                }
                else
                {
                    foreach (var t in localeTables)
                    {
                        var oldEntry = t.GetEntry(oldId);
                        if (oldEntry != null) t.RemoveEntry(oldId);
                    }
                }
            }

            // ③ 五语言写值（缺语言回退 en）
            foreach (var t in localeTables)
            {
                string value = values.TryGetValue(t.LocaleIdentifier.Code, out var v) ? v : values["en"];
                var existing = t.GetEntry(finalId);
                if (existing != null)
                {
                    if (existing.Value != value) { existing.Value = value; log.Add($"LOCAL {key}[{t.LocaleIdentifier.Code}]: updated"); }
                }
                else
                {
                    t.AddEntry(finalId, value);
                }
            }
        }

        /// <summary>CSV 重导出（与 LocalizationCsvTool.ExportTable 完全同格式，保持 CSV 与表一致）</summary>
        private static void ExportTableCsv(StringTableCollection collection, string tableName, ref List<string> log)
        {
            var sharedData = collection.SharedData;
            var entries = sharedData.Entries;
            if (entries == null || entries.Count == 0) return;

            var localeCodes = new List<string>();
            var localeTables = new List<StringTable>();
            foreach (var tableRef in collection.Tables)
                if (tableRef.asset is StringTable st)
                {
                    localeCodes.Add(st.LocaleIdentifier.Code);
                    localeTables.Add(st);
                }

            var sb = new StringBuilder();
            sb.Append("Key,Id");
            foreach (var code in localeCodes) sb.Append(",").Append(code);
            sb.Append("\n");
            foreach (var entry in entries)
            {
                sb.Append(EscapeCsvField(entry.Key)).Append(",").Append(entry.Id);
                foreach (var table in localeTables)
                {
                    var te = table.GetEntry(entry.Id);
                    sb.Append(",").Append(EscapeCsvField(te?.Value ?? ""));
                }
                sb.Append("\n");
            }

            string folderPath = Path.GetFullPath(CsvFolder);
            if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);
            string filePath = Path.Combine(folderPath, $"{tableName}.csv");
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            log.Add($"LOCAL {tableName}.csv re-exported: {entries.Count} entries");
        }

        private static string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r"))
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            return field;
        }

        // ═══════════════ 第 2 步：素材九切片 ═══════════════

        private static void EnsureSpriteBorders(ref List<string> log)
        {
            EnsureSpriteBorder(PillSpritePath, new Vector4(37, 37, 37, 37), ref log);       // 米白胶囊底板（条/按钮）
        }

        /// <summary>长条按钮右侧的▼下拉三角（64×64 白色实心三角，颜色由 Image.color 染）；缺失时程序化生成，幂等</summary>
        private static Sprite EnsureTriangleSprite(ref List<string> log)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(TriangleSpritePath);
            if (existing != null) return existing;

            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var pixels = new Color32[64 * 64];
            for (int y = 0; y < 64; y++)
            {
                // ▼：顶行全宽，向下收成尖点（y=0 底、y=63 顶）
                float half = (y + 1) / 64f * 32f;
                for (int x = 0; x < 64; x++)
                {
                    bool inside = Mathf.Abs(x - 31.5f) <= half;
                    pixels[y * 64 + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            File.WriteAllBytes(TriangleSpritePath, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(TriangleSpritePath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(TriangleSpritePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            log.Add("SPRITE dropdown_triangle.png generated (64x64)");
            return AssetDatabase.LoadAssetAtPath<Sprite>(TriangleSpritePath);
        }

        private static void EnsureSpriteBorder(string path, Vector4 border, ref List<string> log)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) { log.Add($"SPRITE importer NOT FOUND: {path}"); return; }
            if (importer.spriteBorder != border)
            {
                importer.spriteBorder = border;
                importer.SaveAndReimport();
                log.Add($"SPRITE {Path.GetFileName(path)} border -> {border.x} (reimported)");
            }
            else
            {
                log.Add($"SPRITE {Path.GetFileName(path)} border already {border.x}");
            }
        }

        // ═══════════════ 第 3 步：场景手术 ═══════════════

        private static void MigrateScene(ref List<string> log)
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
            try
            {
                var screen = UnityEngine.Object.FindObjectsByType<BackpackScreen>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
                if (screen == null) { log.Add("SCENE BackpackScreen component NOT FOUND"); return; }

                if (screen.bottomPanel == null) { log.Add("SCENE screen.bottomPanel NOT wired"); return; }
                Transform canvasRoot = screen.bottomPanel.transform.parent;
                if (canvasRoot == null) { log.Add("SCENE bottomPanel has no parent (canvas root?)"); return; }

                var buttonsTf = screen.bottomPanel.transform.Find("Buttons");
                if (buttonsTf == null) { log.Add("SCENE Buttons container NOT FOUND"); return; }

                // v1 遗留清理：DeckChoose 实例（v1 已删，重跑防御）与旧面板（重建保持幂等）
                int removed = 0;
                for (int i = buttonsTf.childCount - 1; i >= 0; i--)
                {
                    var child = buttonsTf.GetChild(i);
                    if (child.name.StartsWith("DeckChoose", StringComparison.Ordinal))
                    {
                        UnityEngine.Object.DestroyImmediate(child.gameObject);
                        removed++;
                    }
                }
                if (removed > 0) log.Add($"SCENE removed {removed} DeckChoose instances");

                var oldPanel = canvasRoot.transform.Find("DeckSwitchPanel");
                if (oldPanel != null) { UnityEngine.Object.DestroyImmediate(oldPanel.gameObject); log.Add("SCENE removed old DeckSwitchPanel (rebuild)"); }

                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
                var pillSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PillSpritePath);
                var panelBgSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PanelBgSpritePath);
                var closeSprite = AssetDatabase.LoadAssetAtPath<Sprite>(CloseSpritePath);
                var inputPopupPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(InputPopupPrefabPath);
                if (font == null || pillSprite == null || panelBgSprite == null || closeSprite == null)
                {
                    log.Add($"SCENE assets missing: font={font} pill={pillSprite} bg={panelBgSprite} close={closeSprite}");
                    return;
                }

                // ── 3.1 长条卡组按钮（原神"品质顺序"下拉条样式：米白胶囊 + 左侧粗深墨蓝文字 + 右侧▼；重建保持幂等） ──
                var barTf = buttonsTf.transform.Find("DeckBar");
                if (barTf != null) { UnityEngine.Object.DestroyImmediate(barTf.gameObject); log.Add("SCENE removed old DeckBar (rebuild)"); }

                var triangleSprite = EnsureTriangleSprite(ref log);

                var barGo = new GameObject("DeckBar", typeof(RectTransform), typeof(Image), typeof(Button));
                var barRt = barGo.GetComponent<RectTransform>();
                barRt.SetParent(buttonsTf, false);
                barRt.sizeDelta = new Vector2(640, 80); // 约 8:1，同原神下拉条比例
                var barImg = barGo.GetComponent<Image>();
                barImg.sprite = pillSprite;
                barImg.type = Image.Type.Sliced;
                var barBtn = barGo.GetComponent<Button>();
                barBtn.targetGraphic = barImg;

                var barNumber = MakeTMP("Number", barGo.transform, font, 52, InkBlueText, TextAlignmentOptions.Center);
                // 字重不加粗：与背包页其它按钮文字（分类切换等，fontStyle=0）一致
                barNumber.rectTransform.anchorMin = barNumber.rectTransform.anchorMax = new Vector2(0, 0.5f);
                barNumber.rectTransform.pivot = new Vector2(0, 0.5f);
                barNumber.rectTransform.anchoredPosition = new Vector2(48, 0);
                barNumber.rectTransform.sizeDelta = new Vector2(76, 80);
                var barNumberComb = barNumber.gameObject.AddComponent<TextCombiner>();
                barNumberComb.textComponent = barNumber;

                var barName = MakeTMP("Name", barGo.transform, font, 40, InkBlueText, TextAlignmentOptions.Left);
                barName.rectTransform.anchorMin = barName.rectTransform.anchorMax = new Vector2(0, 0.5f);
                barName.rectTransform.pivot = new Vector2(0, 0.5f);
                barName.rectTransform.anchoredPosition = new Vector2(128, 0);
                barName.rectTransform.sizeDelta = new Vector2(420, 80); // 10 字上限：40 号×10=400 ≤ 420，单行不换行
                var barNameComb = barName.gameObject.AddComponent<TextCombiner>();
                barNameComb.textComponent = barName;

                // 右侧▼下拉指示（同文字色）
                if (triangleSprite != null)
                {
                    var tri = MakeImage("DropdownArrow", barGo.transform, triangleSprite, Image.Type.Simple, InkBlueText);
                    tri.rectTransform.anchorMin = tri.rectTransform.anchorMax = tri.rectTransform.pivot = new Vector2(1, 0.5f);
                    tri.rectTransform.anchoredPosition = new Vector2(-34, 0);
                    tri.rectTransform.sizeDelta = new Vector2(30, 30);
                    tri.raycastTarget = false;
                }
                log.Add("SCENE DeckBar rebuilt (Genshin dropdown style, 640x80 + arrow)");

                // ── 3.2 卡组管理面板（全层级重建） ──
                var panelGo = new GameObject("DeckSwitchPanel", typeof(RectTransform), typeof(CanvasGroup), typeof(DeckSwitchPanel));
                var panelRt = panelGo.GetComponent<RectTransform>();
                panelRt.SetParent(canvasRoot, false);
                panelRt.SetAsLastSibling();
                panelRt.anchorMin = Vector2.zero;
                panelRt.anchorMax = Vector2.one;
                panelRt.sizeDelta = Vector2.zero; // 全屏铺开：遮罩盖住整个界面
                var panelComp = panelGo.GetComponent<DeckSwitchPanel>();
                var panelCanvas = panelGo.GetComponent<CanvasGroup>();
                panelCanvas.alpha = 0f;
                panelCanvas.interactable = false;
                panelCanvas.blocksRaycasts = false;
                panelGo.SetActive(false);

                // 遮罩（点击外部关闭）
                var backdrop = MakeImage("Backdrop", panelGo.transform, null, Image.Type.Simple, BackdropColor);
                Stretch(backdrop.rectTransform);
                backdrop.raycastTarget = true;
                var backdropBtn = backdrop.gameObject.AddComponent<Button>();
                backdropBtn.targetGraphic = backdrop;
                backdropBtn.transition = Selectable.Transition.None;

                // 面板底（medium_popup 九切片）
                var panelBg = MakeImage("PanelRoot", panelGo.transform, panelBgSprite, Image.Type.Sliced, Color.white);
                var panelBgRt = panelBg.rectTransform;
                panelBgRt.anchorMin = panelBgRt.anchorMax = new Vector2(0.5f, 0.5f);
                panelBgRt.sizeDelta = new Vector2(PanelW, PanelH);
                panelBg.raycastTarget = true;
                // 尺寸自适应组件（通用，Tool/Component/PanelFitToCanvas）：超宽屏画布垂直单位不足时收缩面板
                panelBg.gameObject.AddComponent<GIC.Tool.PanelFitToCanvas>();

                // 拖拽层：拖动中的行挂到这里（面板层级最顶 + 不受滚动遮罩影响），全屏铺开
                var dragLayerGo = new GameObject("DragLayer", typeof(RectTransform));
                var dragLayerRt = dragLayerGo.GetComponent<RectTransform>();
                dragLayerRt.SetParent(panelGo.transform, false);
                Stretch(dragLayerRt);

                // 标题
                var title = MakeTMP("Title", panelBgRt, font, 54, CreamyWhite, TextAlignmentOptions.Center);
                title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(0.5f, 1f);
                title.rectTransform.pivot = new Vector2(0.5f, 1f);
                title.rectTransform.anchoredPosition = new Vector2(0, -34);
                title.rectTransform.sizeDelta = new Vector2(700, 80);
                var titleComb = title.gameObject.AddComponent<TextCombiner>();
                titleComb.textComponent = title;
                titleComb.entries.Add(new TextEntry(new LocalizedString("UIText", "Deck_PanelTitle"), ""));

                // 关闭按钮
                var closeGo = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
                var closeRt = closeGo.GetComponent<RectTransform>();
                closeRt.SetParent(panelBgRt, false);
                closeRt.anchorMin = closeRt.anchorMax = new Vector2(1, 1);
                closeRt.pivot = new Vector2(1, 1);
                closeRt.anchoredPosition = new Vector2(-40, -40);
                closeRt.sizeDelta = new Vector2(90, 90);
                var closeImg = closeGo.GetComponent<Image>();
                closeImg.sprite = closeSprite;
                closeGo.GetComponent<Button>().targetGraphic = closeImg;

                // ── ScrollView：行列表可滚动（卡组最多 30） ──
                var scrollGo = new GameObject("ScrollView", typeof(RectTransform), typeof(ScrollRect));
                var scrollRt = scrollGo.GetComponent<RectTransform>();
                scrollRt.SetParent(panelBgRt, false);
                scrollRt.anchorMin = new Vector2(0, 0);
                scrollRt.anchorMax = new Vector2(1, 1);
                scrollRt.offsetMin = new Vector2(30, 150);   // 底部让位给[新增卡组][导入密语]
                scrollRt.offsetMax = new Vector2(-30, -140); // 顶部让位给标题+右上关闭按钮（按钮占 -40..-130，留 10px 缝）
                var scrollRect = scrollGo.GetComponent<ScrollRect>();
                scrollRect.horizontal = false;
                scrollRect.vertical = true;
                scrollRect.movementType = ScrollRect.MovementType.Elastic;
                scrollRect.scrollSensitivity = 30f;

                // 视口 + Mask
                var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
                var viewportRt = viewportGo.GetComponent<RectTransform>();
                viewportRt.SetParent(scrollRt, false);
                Stretch(viewportRt);
                var viewportImg = viewportGo.GetComponent<Image>();
                viewportImg.color = Color.white;
                viewportImg.raycastTarget = false;
                var viewportMask = viewportGo.GetComponent<Mask>();
                viewportMask.showMaskGraphic = false;

                // 滚动条（右缘细条）
                var sbGo = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
                var sbRt = sbGo.GetComponent<RectTransform>();
                sbRt.SetParent(scrollRt, false);
                sbRt.anchorMin = new Vector2(1, 0);
                sbRt.anchorMax = new Vector2(1, 1);
                sbRt.pivot = new Vector2(1, 0.5f);
                sbRt.anchoredPosition = new Vector2(-6, 0);
                sbRt.sizeDelta = new Vector2(14, 0);
                var sbTrack = sbGo.GetComponent<Image>();
                sbTrack.color = new Color(0f, 0f, 0f, 0.35f);
                sbTrack.raycastTarget = false;
                var sb = sbGo.GetComponent<Scrollbar>();
                sb.direction = Scrollbar.Direction.BottomToTop;

                var slidingGo = new GameObject("SlidingArea", typeof(RectTransform));
                var slidingRt = slidingGo.GetComponent<RectTransform>();
                slidingRt.SetParent(sbRt, false);
                Stretch(slidingRt);
                slidingRt.offsetMin = new Vector2(0, 4);
                slidingRt.offsetMax = new Vector2(0, -4);

                var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
                var handleRt = handleGo.GetComponent<RectTransform>();
                handleRt.SetParent(slidingRt, false);
                Stretch(handleRt);
                var handleImg = handleGo.GetComponent<Image>();
                handleImg.color = new Color(CreamyWhite.r, CreamyWhite.g, CreamyWhite.b, 0.9f);
                handleImg.raycastTarget = true;
                sb.handleRect = handleRt;
                sb.targetGraphic = handleImg;

                // Content（行容器：纵向布局 + 高度自适应）
                var contentGo = new GameObject("Rows", typeof(RectTransform));
                var rowsRt = contentGo.GetComponent<RectTransform>();
                rowsRt.SetParent(viewportRt, false);
                rowsRt.anchorMin = new Vector2(0, 1);
                rowsRt.anchorMax = new Vector2(1, 1);
                rowsRt.pivot = new Vector2(0.5f, 1f);
                rowsRt.anchoredPosition = Vector2.zero;
                rowsRt.sizeDelta = Vector2.zero;
                var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
                vlg.childAlignment = TextAnchor.UpperCenter;
                vlg.spacing = 16;
                vlg.childForceExpandWidth = false;
                vlg.childForceExpandHeight = false;
                vlg.childControlWidth = false;
                vlg.childControlHeight = false;
                vlg.childScaleWidth = false;
                vlg.childScaleHeight = false;
                var csf = contentGo.AddComponent<ContentSizeFitter>();
                csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                scrollRect.content = rowsRt;
                scrollRect.viewport = viewportRt;
                scrollRect.verticalScrollbar = sb;
                log.Add("SCENE ScrollView built (viewport+mask+content+scrollbar)");

                // ── 底部双按钮：[新增卡组][导入密语]（交易商城式胶囊） ──
                var addBtn = MakeTextButton(panelBgRt, "AddDeckButton", pillSprite, font, "Deck_Add",
                    new Vector2(-260, 44), new Vector2(440, 88), 38, DarkGrayText);
                var importBtn = MakeTextButton(panelBgRt, "ImportButton", pillSprite, font, "Deck_Import",
                    new Vector2(260, 44), new Vector2(440, 88), 38, DarkGrayText);

                // ── 3.3 行预制体（面板运行时按卡组数量实例化） ──
                var rowPrefab = BuildRowPrefab(font, pillSprite, ref log);

                // ── 3.4 接线 ──
                panelComp.screen = screen;
                panelComp.canvasGroup = panelCanvas;
                panelComp.panelFit = panelBg.GetComponent<GIC.Tool.PanelFitToCanvas>(); // 打开面板时 Apply() 收敛尺寸
                panelComp.backdropButton = backdropBtn;
                panelComp.closeButton = closeGo.GetComponent<Button>();
                panelComp.rowsRoot = rowsRt;
                panelComp.scrollRect = scrollRect;
                panelComp.viewportRect = viewportRt;
                panelComp.scrollbar = sb;
                panelComp.rowPrefab = rowPrefab != null ? rowPrefab.GetComponent<DeckRowView>() : null;
                panelComp.dragLayer = dragLayerRt;
                panelComp.addDeckButton = addBtn;
                panelComp.importButton = importBtn;
                panelComp.inputPopupPrefab = inputPopupPrefab != null
                    ? inputPopupPrefab.GetComponent<InputPopupDialog>() : null;
                if (panelComp.rowPrefab == null) log.Add("SCENE WARN: rowPrefab unwired");
                if (panelComp.inputPopupPrefab == null) log.Add("SCENE WARN: InputPopupDialog prefab not found/unwired");

                screen.deckSwitchButton = barBtn;
                screen.deckBarNumberText = barNumber;
                screen.deckBarNameText = barName;
                screen.deckSwitchPanel = panelComp;

                EditorUtility.SetDirty(screen);
                EditorUtility.SetDirty(panelComp);

                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
                log.Add("SCENE saved");
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
        }

        // ─────────── 行预制体构建 ───────────

        /// <summary>构建 DeckRow.prefab（幂等：已存在则删除重建，引用在本运行内重接）</summary>
        private static GameObject BuildRowPrefab(TMP_FontAsset font, Sprite pillSprite, ref List<string> log)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(RowPrefabPath) != null)
            {
                AssetDatabase.DeleteAsset(RowPrefabPath);
                log.Add("ROW prefab deleted for rebuild");
            }

            var rowGo = new GameObject("DeckRow", typeof(RectTransform), typeof(CanvasGroup), typeof(DeckRowView));
            var rowRt = rowGo.GetComponent<RectTransform>();
            rowRt.sizeDelta = new Vector2(RowW, RowH);
            // CanvasGroup：行入场动画整行淡入（面板 PlayRowsEntrance 驱动）
            var view = rowGo.GetComponent<DeckRowView>();

            // 行背景
            var bg = MakeImage("RowBackground", rowGo.transform, null, Image.Type.Simple, RowBgColor);
            Stretch(bg.rectTransform);
            view.rowBackground = bg;

            // 当前卡组高亮（整行金色实底，默认隐藏，运行时 SetCurrent 切换；与行同界铺满）
            var hi = MakeImage("CurrentHighlight", rowGo.transform, null, Image.Type.Simple, GoldAccent);
            Stretch(hi.rectTransform);
            hi.raycastTarget = false;
            hi.gameObject.SetActive(false);
            view.currentHighlight = hi;

            // 拖拽浮层（拖动中的视觉反馈，默认隐藏）
            var overlay = MakeImage("DragOverlay", rowGo.transform, null, Image.Type.Simple, DragOverlayColor);
            Stretch(overlay.rectTransform);
            overlay.raycastTarget = false;
            overlay.gameObject.SetActive(false);
            view.dragOverlay = overlay;

            // 拖拽把手：透明命中区 + 三横条视觉（拖 = 排序；点击被把手消耗）
            var handleGo = new GameObject("DragHandle", typeof(RectTransform), typeof(Image));
            var handleRt = handleGo.GetComponent<RectTransform>();
            handleRt.SetParent(rowGo.transform, false);
            handleRt.anchorMin = handleRt.anchorMax = handleRt.pivot = new Vector2(0, 0.5f);
            handleRt.anchoredPosition = new Vector2(30, 0);
            handleRt.sizeDelta = new Vector2(44, 64);
            var handleImg = handleGo.GetComponent<Image>();
            handleImg.color = new Color(1f, 1f, 1f, 0f); // 全透明仍可作射线命中区
            handleImg.raycastTarget = true;
            var handleComp = handleGo.AddComponent<DeckRowDragHandle>();
            view.dragHandle = handleComp;
            for (int b = 0; b < 3; b++)
            {
                var bar = MakeImage($"Bar_{b}", handleGo.transform, null, Image.Type.Simple,
                    new Color(CreamyWhite.r, CreamyWhite.g, CreamyWhite.b, 0.7f));
                bar.rectTransform.anchorMin = bar.rectTransform.anchorMax = bar.rectTransform.pivot = new Vector2(0, 0.5f);
                bar.rectTransform.anchoredPosition = new Vector2(9, 14 - b * 14);
                bar.rectTransform.sizeDelta = new Vector2(26, 5);
                bar.raycastTarget = false;
            }

            // 编号
            var number = MakeTMP("Number", rowGo.transform, font, 44, CreamyWhite, TextAlignmentOptions.Center);
            number.rectTransform.anchorMin = number.rectTransform.anchorMax = number.rectTransform.pivot = new Vector2(0, 0.5f);
            number.rectTransform.anchoredPosition = new Vector2(96, 0);
            number.rectTransform.sizeDelta = new Vector2(80, 90);
            var numberComb = number.gameObject.AddComponent<TextCombiner>();
            numberComb.textComponent = number;
            view.numberText = number;

            // 名称（点击改名：TMP 上叠加 Button）——10 字上限：36 号×10=360 ≤ 370 单行不换行，宽度按上限收紧
            var name = MakeTMP("Name", rowGo.transform, font, 36, Color.white, TextAlignmentOptions.Left);
            name.rectTransform.anchorMin = name.rectTransform.anchorMax = name.rectTransform.pivot = new Vector2(0, 0.5f);
            name.rectTransform.anchoredPosition = new Vector2(196, 0);
            name.rectTransform.sizeDelta = new Vector2(370, 90);
            var nameComb = name.gameObject.AddComponent<TextCombiner>();
            nameComb.textComponent = name;
            var nameBtn = name.gameObject.AddComponent<Button>();
            nameBtn.targetGraphic = name;
            name.raycastTarget = true; // 名称即改名按钮的点击热区（其余 TMP 默认不挡射线）
            view.nameText = name;
            view.nameButton = nameBtn;

            // 卡牌预览容器（运行时由面板以共享 CardPool 填充完整卡牌，0.6 缩放 96×144×8）
            var previewGo = new GameObject("CardPreview", typeof(RectTransform));
            var previewRt = previewGo.GetComponent<RectTransform>();
            previewRt.SetParent(rowGo.transform, false);
            previewRt.anchorMin = previewRt.anchorMax = previewRt.pivot = new Vector2(0, 0.5f);
            previewRt.anchoredPosition = new Vector2(620, 0);
            previewRt.sizeDelta = new Vector2(860, 160);
            view.previewRoot = previewRt;

            // 右缘四连按钮：[复制][粘贴][导出][删除]（交易商城式胶囊，间距 12）
            view.copyButton = MakeRowButton(rowGo.transform, "CopyButton", pillSprite, font, "Deck_Copy", -474);
            view.pasteButton = MakeRowButton(rowGo.transform, "PasteButton", pillSprite, font, "Deck_Paste", -332);
            view.exportButton = MakeRowButton(rowGo.transform, "ExportButton", pillSprite, font, "Deck_Export", -190);
            view.deleteButton = MakeRowButton(rowGo.transform, "DeleteButton", pillSprite, font, "Deck_Delete", -48);

            var prefab = PrefabUtility.SaveAsPrefabAsset(rowGo, RowPrefabPath);
            UnityEngine.Object.DestroyImmediate(rowGo);
            log.Add($"ROW prefab saved: {RowPrefabPath}");
            return prefab;
        }

        private static Button MakeRowButton(Transform parent, string name, Sprite sprite, TMP_FontAsset font,
            string labelKey, float x)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0);
            rt.sizeDelta = new Vector2(130, 58);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;

            var label = MakeTMP("Label", go.transform, font, 28, DarkGrayText, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);
            var comb = label.gameObject.AddComponent<TextCombiner>();
            comb.textComponent = label;
            comb.entries.Add(new TextEntry(new LocalizedString("UIText", labelKey), ""));
            return btn;
        }

        /// <summary>带本地化文字的胶囊按钮（面板底部大按钮用）</summary>
        private static Button MakeTextButton(Transform parent, string name, Sprite sprite, TMP_FontAsset font,
            string labelKey, Vector2 pos, Vector2 size, float fontSize, Color textColor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;

            var label = MakeTMP("Label", go.transform, font, fontSize, textColor, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);
            var comb = label.gameObject.AddComponent<TextCombiner>();
            comb.textComponent = label;
            comb.entries.Add(new TextEntry(new LocalizedString("UIText", labelKey), ""));
            return btn;
        }

        // ─────────── 通用构建助手 ───────────

        private static TextMeshProUGUI MakeTMP(string name, Transform parent, TMP_FontAsset font,
            float fontSize, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.font = font;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            tmp.text = ""; // 编辑器里保持干净；运行时由 TextCombiner 填充
            return tmp;
        }

        private static Image MakeImage(string name, Transform parent, Sprite sprite, Image.Type type,
            Color color, float ppuMultiplier = 1f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.type = type;
            img.color = color;
            img.pixelsPerUnitMultiplier = ppuMultiplier;
            return img;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
        }
    }
}
#endif
