using System.Text;
using UnityEditor;
using UnityEngine;
using GIC.UI;

namespace GIC.Editor
{
    /// <summary>
    /// 战斗手牌卡牌详情面板 prefab 快照工具（2026-10-10 点击手牌开详情批，BattleHud.Resources 加载消费）：
    /// 源 CardDetailPanel.prefab 的 CardDetailView.unitDetailPanel/itemDetailPanel/tagContainer 均未接线
    /// ——UDP@NameCard/IDP@ItemImage/TagContainer 是 BackpackScreen 主实例的实例级覆写（2026-10-10
    /// 滚动批「将来启用需按主面板同款补」），新开一份裸嵌套实例须整套手工复刻覆写、易错易漏。
    /// 本工具改为把 BackpackScreen 主实例（含全部实例覆写+添加组件+添加物体）整体快照成独立
    /// prefab 资产 Resources/Prefabs/Battle/BattleCardDetailPanel.prefab，运行时 Resources.Load 实例化
    /// （同 BattleExitConfirmDialog.Show 先例）。外引用（skillDetailView/全部数据面板→背包宿主内物件）
    /// 快照时自然断空=战斗只读语义（技能图标 OnlyDisplay、小数据面板点击无全部面板 Warn 兜底）。
    /// 再跑=重生成快照（CardDetailPanel.prefab 源或背包实例覆写演进后同步用）；BattleHud 侧按
    /// Resources 路径加载不持 guid 引用，重跑零迁移。
    /// </summary>
    public static class BattleCardDetailSnapshot
    {
        private const string BackpackPrefabPath = "Assets/Resources/Prefabs/UIPanels/BackpackScreen.prefab";
        private const string SnapshotPath = "Assets/Resources/Prefabs/Battle/BattleCardDetailPanel.prefab";

        [MenuItem("Tools/TG/生成战斗卡牌详情面板 prefab（快照）")]
        public static void Generate()
        {
            string report;
            try
            {
                report = Run();
            }
            catch (System.Exception ex)
            {
                report = "快照失败：" + ex;
                Debug.LogError($"[BattleCardDetailSnapshot] {report}");
                return;
            }
            Debug.Log($"[BattleCardDetailSnapshot] {report}");
        }

        private static string Run()
        {
            var sb = new StringBuilder();

            // —— 快照：BackpackScreen 主实例（Canvas 直下 CardDetailPanel，含全套覆写）→ 新 prefab ——
            var backpackRoot = PrefabUtility.LoadPrefabContents(BackpackPrefabPath);
            try
            {
                var instance = backpackRoot.transform.Find("Canvas/CardDetailPanel");
                if (instance == null)
                    return "BackpackScreen.prefab 缺 Canvas/CardDetailPanel 实例（结构契约变更？）";
                PrefabUtility.SaveAsPrefabAsset(instance.gameObject, SnapshotPath);
                sb.AppendLine("快照 -> " + SnapshotPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(backpackRoot);
            }

            // —— 战斗弹窗摆位：快照 root 沿背包右侧停靠锚，战斗改屏幕居中弹出（尺寸对齐技能详情族 900 高）——
            var snapRoot = PrefabUtility.LoadPrefabContents(SnapshotPath);
            try
            {
                var rt = (RectTransform)snapRoot.transform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, 70f);
                rt.sizeDelta = new Vector2(680.23145f, 900f);
                PrefabUtility.SaveAsPrefabAsset(snapRoot, SnapshotPath);
                sb.AppendLine("战斗摆位（居中 (0,70)、680×900）已写入");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(snapRoot);
            }

            AssetDatabase.SaveAssets();
            sb.AppendLine(Validate());
            return sb.ToString();
        }

        /// <summary>烘焙产物校验：接线断空判定（UDP/IDP/TagContainer 三件是实例覆写的核心）、摆位、激活态</summary>
        private static string Validate()
        {
            var sb = new StringBuilder("校验：");
            int issues = 0;

            var root = AssetDatabase.LoadAssetAtPath<GameObject>(SnapshotPath);
            if (root == null) return sb.Append("快照 prefab 加载失败！").ToString();

            var view = root.GetComponent<CardDetailView>();
            if (view == null) { sb.Append("[缺 CardDetailView]"); issues++; }
            else
            {
                var so = new SerializedObject(view);
                if (so.FindProperty("unitDetailPanel").objectReferenceValue == null) { sb.Append("[unitDetailPanel 未接线]"); issues++; }
                if (so.FindProperty("itemDetailPanel").objectReferenceValue == null) { sb.Append("[itemDetailPanel 未接线]"); issues++; }
                if (so.FindProperty("tagContainer").objectReferenceValue == null) { sb.Append("[tagContainer 未接线]"); issues++; }
            }

            var udps = root.GetComponentsInChildren<UnitDetailPanel>(true);
            if (udps.Length == 0) { sb.Append("[缺 UDP 添加组件]"); issues++; }
            else
            {
                var so = new SerializedObject(udps[0]);
                if (so.FindProperty("nameCard").objectReferenceValue == null) { sb.Append("[UDP.nameCard 未接线]"); issues++; }
                if (so.FindProperty("skillsPanel").objectReferenceValue == null) { sb.Append("[UDP.skillsPanel 未接线]"); issues++; }
                if (so.FindProperty("数据小面板").objectReferenceValue == null) { sb.Append("[UDP.数据小面板 未接线]"); issues++; }
                if (so.FindProperty("skillDetailView").objectReferenceValue != null) { sb.Append("[UDP.skillDetailView 应为空（外引用断空——战斗只读语义）]"); issues++; }
            }
            var idps = root.GetComponentsInChildren<ItemDetailPanel>(true);
            if (idps.Length == 0) { sb.Append("[缺 IDP 添加组件]"); issues++; }
            else
            {
                var so = new SerializedObject(idps[0]);
                if (so.FindProperty("itemImage").objectReferenceValue == null) { sb.Append("[IDP.itemImage 未接线]"); issues++; }
                if (so.FindProperty("composition").objectReferenceValue == null) { sb.Append("[IDP.composition 未接线]"); issues++; }
            }

            var rtV = (RectTransform)root.transform;
            if (rtV.anchorMin != new Vector2(0.5f, 0.5f) || rtV.anchorMax != new Vector2(0.5f, 0.5f))
            { sb.Append("[root 锚点非居中]"); issues++; }
            if (!root.activeSelf) { sb.Append("[root 烘焙为隐藏态——运行时管理显隐，烘焙应为激活]"); issues++; }

            sb.Append(issues == 0 ? "全部通过" : $"发现 {issues} 处问题！");
            return sb.ToString();
        }
    }
}
