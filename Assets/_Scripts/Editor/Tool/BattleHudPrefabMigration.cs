using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using GIC.Battle;

namespace GIC.Editor
{
    /// <summary>
    /// 战斗 HUD + 退出弹窗 prefab 化一次性迁移（2026-09-22 全项目统一批次）：
    /// 反射调 BattleHud.BuildUi/BuildLayoutToolbar（现程序化构建=唯一真源，烘焙零漂移）→
    /// 剥离持有场景对象引用的运行时组件（LayoutDragHandler）→ 工具栏烘焙为隐藏态 → SaveAsPrefabAsset。
    /// 弹窗走 BuildStructure（纯结构，无注入/锁副作用）。
    /// 迁移后 prefab 成为结构真源（BattleHud 运行时按名寻址接线）；本工具保留备查，重构后 Build* 已退役、再跑会报方法缺失。
    /// </summary>
    public static class BattleHudPrefabMigration
    {
        private const string HudPrefabPath = "Assets/Resources/Prefabs/Battle/BattleHud.prefab";
        private const string DialogPrefabPath = "Assets/Resources/Prefabs/Battle/BattleExitConfirmDialog.prefab";

        /// <summary>布局槽全键（BattleHud 运行时按名寻址的契约；加件=加槽节点+此处登记）</summary>
        private static readonly string[] SlotKeys =
        {
            "burst", "skill", "enso", "move", "cancel", "settings",
            "turn", "countdown", "clock", "queue", "myinfo", "enemyinfo", "hand", "tip",
        };

        [MenuItem("Tools/TG/战斗HUD与退出弹窗 prefab 化迁移（一次性）")]
        public static void Migrate()
        {
            string report;
            try
            {
                report = Run();
            }
            catch (Exception ex)
            {
                report = "迁移失败：" + ex;
                Debug.LogError($"[BattleHudPrefabMigration] {report}");
                return;
            }
            Debug.Log($"[BattleHudPrefabMigration] {report}");
        }

        public static string Run()
        {
            var sb = new StringBuilder();

            // —— HUD：现构建逻辑组装 → 剥运行时引用 → 保存 ——
            var hudGo = new GameObject("BattleHud");
            var hud = hudGo.AddComponent<BattleHud>();
            InvokePrivate(hud, "BuildUi");
            InvokePrivate(hud, "BuildLayoutToolbar");

            // 工具栏烘焙为隐藏态（运行时编辑模式激活）
            var toolbar = GetField(hud, "_editToolbar") as RectTransform;
            if (toolbar != null) toolbar.gameObject.SetActive(false);
            else sb.AppendLine("WARN: _editToolbar 未解析到，工具栏可能未烘焙");

            // 剥离 LayoutDragHandler（私有嵌套类，持 BattleHud 场景实例引用，进 prefab 必坏；运行时按槽重建）
            var handlerType = typeof(BattleHud).GetNestedType("LayoutDragHandler",
                BindingFlags.NonPublic | BindingFlags.Instance);
            int stripped = 0;
            var slotsRoot = hudGo.transform.Find("BattleHudCanvas/Slots");
            if (slotsRoot == null) throw new Exception("Slots 容器缺失——BuildUi 契约变更？");
            foreach (Transform slot in slotsRoot)
            {
                var handler = slot.GetComponent(handlerType);
                if (handler != null) { UnityEngine.Object.DestroyImmediate(handler); stripped++; }
            }
            sb.AppendLine($"HUD：{slotsRoot.childCount} 槽，剥 {stripped} 个拖拽处理器");

            System.IO.Directory.CreateDirectory("Assets/Resources/Prefabs/Battle");
            PrefabUtility.SaveAsPrefabAsset(hudGo, HudPrefabPath);
            UnityEngine.Object.DestroyImmediate(hudGo);
            sb.AppendLine("HUD prefab -> " + HudPrefabPath);

            // —— 弹窗：BuildStructure 纯结构烘焙 ——
            var dlgGo = new GameObject("BattleExitConfirmDialog");
            var dlg = dlgGo.AddComponent<BattleExitConfirmDialog>();
            InvokePrivate(dlg, "BuildStructure");
            PrefabUtility.SaveAsPrefabAsset(dlgGo, DialogPrefabPath);
            UnityEngine.Object.DestroyImmediate(dlgGo);
            sb.AppendLine("Dialog prefab -> " + DialogPrefabPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            sb.AppendLine(Validate());
            return sb.ToString();
        }

        /// <summary>烘焙产物校验：加载 prefab 资产逐项核对契约（缺名/缺子级=运行时 Warn 的前置拦截）</summary>
        private static string Validate()
        {
            var sb = new StringBuilder("校验：");
            int issues = 0;

            var hud = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath);
            if (hud == null) return sb.Append("HUD prefab 加载失败！").ToString();

            var slots = hud.transform.Find("BattleHudCanvas/Slots");
            if (slots == null) { sb.Append("[缺 Slots 容器]"); issues++; }
            else
            {
                foreach (var key in SlotKeys)
                {
                    var slot = slots.Find(key);
                    if (slot == null) { sb.Append($"[缺槽 {key}]"); issues++; continue; }
                    if (slot.childCount < 1) { sb.Append($"[槽 {key} 无控件]"); issues++; }
                    if (slot.Find("DragPlate") == null) { sb.Append($"[槽 {key} 缺拖拽板]"); issues++; }
                    if (slot.Find("SelectFrame") == null) { sb.Append($"[槽 {key} 缺金框]"); issues++; }
                }
                if (slots.childCount != SlotKeys.Length) { sb.Append($"[槽数 {slots.childCount}≠{SlotKeys.Length}]"); issues++; }
            }

            if (hud.transform.Find("BattleHudCanvas/LayoutEditToolbar") == null) { sb.Append("[缺编辑工具栏]"); issues++; }
            if (hud.transform.Find("BattleHudCanvas/BattleSkillDetail") == null) { sb.Append("[缺技能详情面板]"); issues++; }
            if (hud.transform.Find("AimHighlightRoot") == null) { sb.Append("[缺高亮根]"); issues++; }
            if (hud.transform.Find("BattleHudCanvas/LayoutEditButton") == null) { sb.Append("[缺布局入口钮]"); issues++; }
            if (hud.GetComponent<BattleHud>() == null) { sb.Append("[缺 BattleHud 组件]"); issues++; }

            var dlg = AssetDatabase.LoadAssetAtPath<GameObject>(DialogPrefabPath);
            if (dlg == null) { sb.Append("[Dialog prefab 加载失败]"); issues++; }
            else if (dlg.GetComponent<BattleExitConfirmDialog>() == null) { sb.Append("[Dialog 缺组件]"); issues++; }
            else if (dlg.transform.Find("Canvas/Panel/Buttons") == null) { sb.Append("[Dialog 缺按钮行]"); issues++; }

            sb.Append(issues == 0 ? "全部通过" : $"发现 {issues} 处问题！");
            return sb.ToString();
        }

        private static void InvokePrivate(object target, string method)
        {
            var m = target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
            if (m == null) throw new Exception($"私有方法缺失：{target.GetType().Name}.{method}");
            m.Invoke(target, null);
        }

        private static object GetField(object target, string field)
        {
            return target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(target);
        }
    }
}
