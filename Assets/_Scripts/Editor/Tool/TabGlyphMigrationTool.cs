#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using GIC.Data;
using GIC.UI;

namespace GIC.Tool
{
    /// <summary>
    /// 背包页签图标原神式重构迁移工具（幂等，可重复执行）：
    /// 1. InventoryCategoryButton.prefab：SelectIcon 拆为 SelectDisc(公共圆盘)+SelectGlyph(glyph) 两层；
    /// 2. 场景 BackpackScreen 8 个页签按钮：NormalIcon/SelectGlyph 换新白 glyph sprite，SelectDisc 换公共圆盘，设置染色。
    /// 迁移后旧 16 张两图制 PNG 不再被引用（保留在库中备回滚）。
    /// </summary>
    public static class TabGlyphMigrationTool
    {
        private const string GlyphDir = "Assets/Resources/UI/Backpack/TabGlyphs";
        private static readonly (BackpackTab tab, int glyph)[] Map =
        {
            (BackpackTab.Character,  3),
            (BackpackTab.Creation,   6),
            (BackpackTab.Building,   9),
            (BackpackTab.Equipment,  1),
            (BackpackTab.Consumable, 4),
            (BackpackTab.Material,   5),
            (BackpackTab.Currency,   8),
            (BackpackTab.Quest,      7),
        };

        // 采样自原神截图：暗态灰蓝 / 选中 glyph 深蓝灰 / 圆盘米白
        private static readonly Color DimColor = new(0.647f, 0.627f, 0.616f, 1f);   // (165,160,157)
        private static readonly Color GlyphColor = new(0.353f, 0.384f, 0.447f, 1f); // (90,98,114)
        private static readonly Color DiscColor = new(0.914f, 0.886f, 0.839f, 1f);  // (233,226,214)

        [MenuItem("Tools/TG/背包页签图标重构迁移")]
        public static void Run()
        {
            RunAndReport();
        }

        // 自动执行协议：项目根存在 .codely-cli/tmp/TabGlyphMigration.run.flag 时，
        // 编辑器每次刷新后自动执行一次迁移并删除 flag，结果写入 result.txt（幂等，可重跑）。
        [InitializeOnLoadMethod]
        private static void AutoRunHook()
        {
            void Check()
            {
                string flag = System.IO.Path.Combine(Application.dataPath, "..", ".codely-cli", "tmp", "TabGlyphMigration.run.flag");
                if (System.IO.File.Exists(flag))
                {
                    System.IO.File.Delete(flag);
                    RunAndReport();
                }
            }
            EditorApplication.delayCall += Check;
            EditorApplication.projectChanged += () => EditorApplication.delayCall += Check;
        }

        private static void RunAndReport()
        {
            var log = new List<string>();
            try
            {
                MigratePrefab(ref log);
                MigrateScene(ref log);
            }
            catch (Exception e)
            {
                log.Add("EXCEPTION: " + e);
            }
            var report = string.Join("\n", log);
            Debug.Log("[TabGlyphMigration] 完成\n" + report);
            try
            {
                string outPath = System.IO.Path.Combine(Application.dataPath, "..", ".codely-cli", "tmp", "TabGlyphMigration.result.txt");
                System.IO.File.WriteAllText(outPath, "[TabGlyphMigration] " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n" + report + "\nDONE");
            }
            catch { }
        }

        private static Sprite LoadGlyph(int i)
        {
            var p = $"{GlyphDir}/glyph_{i}.png";
            return AssetDatabase.LoadAssetAtPath<Sprite>(p);
        }

        private static Sprite LoadDisc()
            => AssetDatabase.LoadAssetAtPath<Sprite>($"{GlyphDir}/disc.png");

        private static void MigratePrefab(ref List<string> log)
        {
            var path = "Assets/Resources/Prefabs/Backpack/InventoryCategoryButton.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { log.Add("PREFAB NOT FOUND: " + path); return; }

            var view = prefab.GetComponent<ItemCategoryView>();
            if (view == null) { log.Add("PREFAB no ItemCategoryView"); return; }

            var selectIconT = view.selectIcon != null ? view.selectIcon.transform : null;

            // LoadPrefabContents 全量重建拆层（幂等：重复执行会回到同一最终态）
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var v = root.GetComponent<ItemCategoryView>();
                var iconT = v.selectIcon != null ? v.selectIcon.transform : null;
                if (iconT == null) { log.Add("PREFAB selectIcon null, skip"); return; }

                var go = iconT.gameObject;
                var img = go.GetComponent<Image>();
                var rt = go.GetComponent<RectTransform>();

                if (v.selectDisc == null)
                {
                    var discGo = new GameObject("SelectDisc", typeof(RectTransform), typeof(Image));
                    var discRt = discGo.GetComponent<RectTransform>();
                    discRt.SetParent(iconT.parent, false);
                    discRt.SetSiblingIndex(iconT.GetSiblingIndex());
                    discRt.anchoredPosition = Vector2.zero;
                    discRt.sizeDelta = rt.sizeDelta;
                    discRt.localScale = Vector3.zero;
                    v.selectDisc = discGo.GetComponent<Image>();
                }
                go.name = "SelectGlyph";
                v.selectGlyph = img;
                v.selectIcon = null;

                v.selectDisc.sprite = LoadDisc();
                v.selectDisc.color = DiscColor;
                v.selectDisc.raycastTarget = false;
                img.sprite = LoadGlyph(3);
                img.color = GlyphColor;
                img.raycastTarget = false;
                rt.anchoredPosition = Vector2.zero;
                rt.localScale = Vector3.zero;

                foreach (var child in root.GetComponentsInChildren<Image>(true))
                {
                    if (child.name == "NormalIcon")
                    {
                        child.sprite = LoadGlyph(3);
                        child.color = DimColor;
                    }
                }
                EditorUtility.SetDirty(v);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                log.Add("PREFAB migrated (LoadPrefabContents): SelectIcon -> SelectDisc+SelectGlyph");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void MigrateScene(ref List<string> log)
        {
            var path = "Assets/Scenes/BackpackScreen.unity";
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path, UnityEditor.SceneManagement.OpenSceneMode.Additive);
            bool dirty = false;
            foreach (var view in UnityEngine.Object.FindObjectsByType<ItemCategoryView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var g = Map.FirstOrDefault(m => m.tab == view.tab).glyph;
                if (g == 0) { log.Add($"SCENE tab={view.tab} not in map, skip"); continue; }
                var glyph = LoadGlyph(g);
                if (glyph == null) { log.Add($"SCENE glyph_{g} missing"); continue; }
                var disc = LoadDisc();
                bool layered = view.selectDisc != null && view.selectGlyph != null;
                if (!layered)
                {
                    // 尚未拆层：把 selectIcon GameObject 拆成两层
                    if (view.selectIcon == null) { log.Add($"SCENE tab={view.tab} selectIcon null"); continue; }
                    var rt = view.selectIcon.transform;
                    var go = rt.gameObject;
                    go.name = "SelectGlyph";
                    var img = go.GetComponent<Image>();
                    var discGo = new GameObject("SelectDisc", typeof(RectTransform), typeof(Image));
                    var discRt = discGo.GetComponent<RectTransform>();
                    discRt.SetParent(rt.parent, false);
                    discRt.SetSiblingIndex(rt.GetSiblingIndex());
                    discRt.anchoredPosition = Vector2.zero;
                    discRt.sizeDelta = (rt as RectTransform).sizeDelta;
                    discRt.localScale = Vector3.zero;
                    var discImg = discGo.GetComponent<Image>();
                    view.selectDisc = discImg;
                    view.selectGlyph = img;
                }
                // 统一设置 sprite 与颜色（幂等）
                view.selectDisc.sprite = disc;
                view.selectDisc.color = DiscColor;
                view.selectDisc.raycastTarget = false;
                view.selectGlyph.sprite = glyph;
                view.selectGlyph.color = GlyphColor;
                view.selectGlyph.raycastTarget = false;
                // NormalIcon：换 glyph + 灰蓝
                foreach (var child in view.GetComponentsInChildren<Image>(true))
                {
                    if (child.name == "NormalIcon")
                    {
                        child.sprite = glyph;
                        child.color = DimColor;
                    }
                }
                // 清理孤儿层：同名 SelectDisc/SelectGlyph 但未被字段引用的对象（v1 遗留 added objects）
                var referenced = new HashSet<UnityEngine.Object> { view.selectDisc, view.selectGlyph, view.selectIcon, view.toggle };
                foreach (var child in view.GetComponentsInChildren<Image>(true))
                {
                    if ((child.name == "SelectDisc" || child.name == "SelectGlyph") && !referenced.Contains(child))
                    {
                        log.Add($"SCENE tab={view.tab} remove orphan {child.name}");
                        UnityEngine.Object.DestroyImmediate(child.gameObject);
                    }
                }
                EditorUtility.SetDirty(view);
                dirty = true;
                log.Add($"SCENE tab={view.tab} -> glyph_{g} {(layered ? "(refs refresh)" : "(layered)")}");
            }
            if (dirty)
            {
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
                log.Add("SCENE saved");
            }
            UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
        }
    }
}
#endif