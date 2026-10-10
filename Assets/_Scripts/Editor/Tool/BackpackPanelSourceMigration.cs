using System.Text;
using UnityEditor;
using UnityEngine;
using GIC.UI;

namespace GIC.Editor
{
    /// <summary>
    /// 背包系面板源 prefab 自含化一次性迁移（2026-10-10 架构收口批，用户拍板「开始」——提案三步之 1+2 的最终可落地面）。
    ///
    /// 目标：源 CardDetailPanel.prefab 自含（UDP/IDP/TagContainer 等实例级覆写全部烘焙进源）——
    /// 战斗侧直接 Resources.Load 源实例化+宿主回填（快照资产退役、源改动自动传播）。
    ///
    /// 最终形态（v6，2026-10-10 凌晨五轮引擎 SIGSEGV 实证后收缩；dump 存档=
    /// .codely-cli/tmp/crash-1010-unloadcontents/，BugHunter 候选）：
    /// · 崩法一：LoadPrefabContents 内复合手术后 UnloadPrefabContents→ClosePreviewScene 必 SIGSEGV；
    /// · 崩法二：场景工作台（Unpack OutermostRoot）对嵌套实例 ApplyPrefabInstance 必 SIGSEGV；
    /// · 崩法三：场景工作台结构手术后 OpenScene 恢复必 SIGSEGV；
    /// · 崩法四：编辑态脚本 DestroyImmediate 删 Canvas 层级必 SIGSEGV；
    /// · 崩法五：编辑态脚本对 Canvas 层级 SetActive(false) 同必 SIGSEGV（CanvasRenderer 悬空被删）。
    /// 故 v6=**对 BackpackScreen 零改动**：只做「实例根展平烘焙进源」一项（=快照管线同型操作，
    /// 项目内数百次实证安全）——若烘焙保持源内 fileID，背包既有覆写全部转为冗余但有效（值与新源一致），
    /// 背包无需任何写入；迁移后立即回读校验 fileID 存活，漂移则 git checkout 源文件当场回退。
    /// 统计面板（提案②）因触碰 Canvas 层级必崩而**放弃背包侧换嵌套实例**：BackpackScreen 保留烘焙节点，
    /// 战斗侧用独立资产 Resources/Prefabs/UI/UnitStatsPanel.prefab（本工具「同步统计面板资产」菜单
    /// 从背包烘焙节点一键重生成——内容拷贝、背包容后续改动后重跑即同步）。
    ///
    /// 快照退役：BattleCardDetailPanel.prefab（被源直载取代）、BattleStatsPanel.prefab（被 UI 资产取代）。
    /// </summary>
    public static class BackpackPanelSourceMigration
    {
        private const string BackpackPrefabPath = "Assets/Resources/Prefabs/UIPanels/BackpackScreen.prefab";
        private const string CardDetailSourcePath = "Assets/Resources/Prefabs/Backpack/CardDetailPanel.prefab";
        private const string StatsPrefabPath = "Assets/Resources/Prefabs/UI/UnitStatsPanel.prefab";
        private const string CardSnapshotPath = "Assets/Resources/Prefabs/Battle/BattleCardDetailPanel.prefab";
        private const string StatsSnapshotPath = "Assets/Resources/Prefabs/Battle/BattleStatsPanel.prefab";

        [MenuItem("Tools/TG/背包系面板源 prefab 自含化迁移（一次性）")]
        public static void Migrate()
        {
            string report;
            try
            {
                report = Run();
            }
            catch (System.Exception ex)
            {
                report = "迁移失败：" + ex;
                Debug.LogError($"[BackpackPanelSourceMigration] {report}");
                return;
            }
            Debug.Log($"[BackpackPanelSourceMigration] {report}");
        }

        private static string Run()
        {
            var sb = new StringBuilder();
            string bpGuidBefore = AssetDatabase.AssetPathToGUID(BackpackPrefabPath);
            string srcGuidBefore = AssetDatabase.AssetPathToGUID(CardDetailSourcePath);

            // —— 唯一写操作：卡详情实例根展平烘焙进源（快照管线同型：load→save→unload 实证安全；
            //    会话内零结构手术/零 Apply/零 Destroy/零 SetActive——崩法一~五全规避）——
            var bpRoot = PrefabUtility.LoadPrefabContents(BackpackPrefabPath);
            try
            {
                var cardInst = bpRoot.transform.Find("Canvas/CardDetailPanel");
                if (cardInst == null) throw new System.Exception("缺 Canvas/CardDetailPanel（结构契约变更？）");
                var cardAsset = PrefabUtility.SaveAsPrefabAsset(cardInst.gameObject, CardDetailSourcePath);
                if (cardAsset == null) throw new System.Exception("源展平烘焙保存失败（返回空）");
                sb.AppendLine("① CardDetailPanel 实例根展平烘焙 -> " + CardDetailSourcePath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(bpRoot);
            }

            // —— 源宿主字段核空（展平烘焙时会话外对象引用自动断空；幂等兜底置空——纯字段小会话实证安全）——
            var srcRoot = PrefabUtility.LoadPrefabContents(CardDetailSourcePath);
            try
            {
                var srcUdp = srcRoot.GetComponentInChildren<UnitDetailPanel>(true);
                if (srcUdp == null) throw new System.Exception("烘焙后源缺 UnitDetailPanel（fileID 漂移或烘焙失败？）");
                var so = new SerializedObject(srcUdp);
                bool dirty = false;
                if (so.FindProperty("skillDetailView").objectReferenceValue != null)
                { so.FindProperty("skillDetailView").objectReferenceValue = null; dirty = true; }
                if (so.FindProperty("全部数据面板").objectReferenceValue != null)
                { so.FindProperty("全部数据面板").objectReferenceValue = null; dirty = true; }
                if (dirty)
                {
                    so.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(srcRoot, CardDetailSourcePath);
                    sb.AppendLine("② 源 UDP 宿主字段补清空（skillDetailView/全部数据面板）");
                }
                else sb.AppendLine("② 源宿主字段已空（展平烘焙自动断空）——跳过");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(srcRoot);
            }

            // —— 快照资产退役（战斗侧同批改载源/UI 资产）——
            if (AssetDatabase.DeleteAsset(CardSnapshotPath)) sb.AppendLine("③ 退役 " + CardSnapshotPath);
            if (AssetDatabase.DeleteAsset(StatsSnapshotPath)) sb.AppendLine("③ 退役 " + StatsSnapshotPath);

            AssetDatabase.SaveAssets();
            sb.AppendLine(Validate(bpGuidBefore, srcGuidBefore));
            return sb.ToString();
        }

        /// <summary>统计面板资产同步（战斗消费 Resources/Prefabs/UI/UnitStatsPanel.prefab——内容拷贝自
        /// BackpackScreen 烘焙节点；背包容统计面板改动后重跑本菜单即同步；load→save→unload 实证安全管线）</summary>
        [MenuItem("Tools/TG/同步统计面板资产（BackpackScreen→战斗 UI/UnitStatsPanel）")]
        public static void SyncStatsPanel()
        {
            var bpRoot = PrefabUtility.LoadPrefabContents(BackpackPrefabPath);
            try
            {
                var statsNode = bpRoot.transform.Find("Canvas/UnitStatsPanel");
                if (statsNode == null) { Debug.LogError("[BackpackPanelSourceMigration] 缺 Canvas/UnitStatsPanel"); return; }
                var saved = PrefabUtility.SaveAsPrefabAsset(statsNode.gameObject, StatsPrefabPath);
                Debug.Log(saved != null
                    ? "[BackpackPanelSourceMigration] 统计面板资产已同步 -> " + StatsPrefabPath
                    : "[BackpackPanelSourceMigration] 统计面板同步失败（返回空）");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(bpRoot);
            }
        }

        /// <summary>迁移产物校验：源自含、guid 不变、背包既有覆写存活（fileID 保持=冗余有效；漂移=立即回退信号）</summary>
        private static string Validate(string bpGuidBefore, string srcGuidBefore)
        {
            var sb = new StringBuilder("校验：");
            int issues = 0;

            if (AssetDatabase.AssetPathToGUID(BackpackPrefabPath) != bpGuidBefore)
            { sb.Append("[BackpackScreen guid 变更]"); issues++; }
            if (AssetDatabase.AssetPathToGUID(CardDetailSourcePath) != srcGuidBefore)
            { sb.Append("[CardDetailPanel guid 变更]"); issues++; }

            // —— 源自含 ——
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(CardDetailSourcePath);
            if (src == null) return sb.Append("源 prefab 加载失败！").ToString();
            var view = src.GetComponent<CardDetailView>();
            if (view == null) { sb.Append("[源缺 CardDetailView]"); issues++; }
            else
            {
                var so = new SerializedObject(view);
                if (so.FindProperty("unitDetailPanel").objectReferenceValue == null) { sb.Append("[源 unitDetailPanel 空]"); issues++; }
                if (so.FindProperty("itemDetailPanel").objectReferenceValue == null) { sb.Append("[源 itemDetailPanel 空]"); issues++; }
                if (so.FindProperty("tagContainer").objectReferenceValue == null) { sb.Append("[源 tagContainer 空]"); issues++; }
            }
            var srcUdps = src.GetComponentsInChildren<UnitDetailPanel>(true);
            if (srcUdps.Length != 1) { sb.Append($"[源 UDP 数 {srcUdps.Length}≠1]"); issues++; }
            else
            {
                var so = new SerializedObject(srcUdps[0]);
                if (so.FindProperty("skillDetailView").objectReferenceValue != null) { sb.Append("[源 skillDetailView 应为空]"); issues++; }
                if (so.FindProperty("全部数据面板").objectReferenceValue != null) { sb.Append("[源 全部数据面板 应为空]"); issues++; }
                if (so.FindProperty("数据小面板").objectReferenceValue == null) { sb.Append("[源 数据小面板 空]"); issues++; }
                if (so.FindProperty("skillsPanel").objectReferenceValue == null) { sb.Append("[源 skillsPanel 空]"); issues++; }
            }
            var srcIdps = src.GetComponentsInChildren<ItemDetailPanel>(true);
            if (srcIdps.Length != 1) { sb.Append($"[源 IDP 数 {srcIdps.Length}≠1]"); issues++; }
            else
            {
                var so = new SerializedObject(srcIdps[0]);
                if (so.FindProperty("itemImage").objectReferenceValue == null) { sb.Append("[源 IDP.itemImage 空]"); issues++; }
            }
            var tagGo = FindDeep(src.transform, "TagContainer");
            if (tagGo == null) { sb.Append("[源缺 TagContainer]"); issues++; }
            var srcRows = src.GetComponentsInChildren<StatRowView>(true);
            if (srcRows.Length != 7) { sb.Append($"[源 StatRowView {srcRows.Length}≠7]"); issues++; }

            // —— BackpackScreen 既有覆写存活（fileID 保持则值与新源冗余一致；漂移则此处报缺=回退信号）——
            var bp = AssetDatabase.LoadAssetAtPath<GameObject>(BackpackPrefabPath);
            if (bp == null) { sb.Append("[BackpackScreen 加载失败]"); issues++; }
            else
            {
                var card = bp.transform.Find("Canvas/CardDetailPanel");
                if (card == null) { sb.Append("[缺 CardDetailPanel 实例]"); issues++; }
                else
                {
                    var udp = card.GetComponentInChildren<UnitDetailPanel>(true);
                    if (udp == null) { sb.Append("[实例 UDP 覆写丢失=fileID 漂移——git checkout 源文件回退]"); issues++; }
                    else
                    {
                        var so = new SerializedObject(udp);
                        if (so.FindProperty("skillDetailView").objectReferenceValue == null) { sb.Append("[实例 skillDetailView 覆写丢失=fileID 漂移——回退]"); issues++; }
                        var full = so.FindProperty("全部数据面板").objectReferenceValue;
                        if (full == null) { sb.Append("[实例 全部数据面板 覆写丢失=fileID 漂移——回退]"); issues++; }
                        if (so.FindProperty("数据小面板").objectReferenceValue == null) { sb.Append("[实例 数据小面板 覆写丢失——回退]"); issues++; }
                    }
                    var tagInst = FindDeep(card, "TagContainer");
                    if (tagInst == null) { sb.Append("[实例 TagContainer 添加物丢失=fileID 漂移——回退]"); issues++; }
                }
            }

            // —— 快照退役 ——
            if (AssetDatabase.LoadAssetAtPath<GameObject>(CardSnapshotPath) != null) { sb.Append("[卡详情快照未删净]"); issues++; }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(StatsSnapshotPath) != null) { sb.Append("[统计快照未删净]"); issues++; }

            sb.Append(issues == 0 ? "全部通过" : $"发现 {issues} 处问题！");
            return sb.ToString();
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var f = FindDeep(root.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
        }
    }
}
