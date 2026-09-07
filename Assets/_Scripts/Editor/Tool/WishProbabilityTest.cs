#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
using GIC.Battle;

namespace GIC.Editor
{
    /// <summary>
    /// 祈愿概率完整模拟 — 10万次连续祈愿，忠实复现完整流程：
    ///   1. DrawRandomTrackCard（星级降级、角色→物品回退、Mora兜底）
    ///   2. 重复角色卡→星辉→累计星辉里程碑→每满20赠相遇之缘、每满200赠纠缠之缘（物品）
    ///   3. 命运之缘升级射击（纠缠之缘优先消耗；相遇/纠缠分权重表 Roll 升级）：逐级提升→每级判重发星辉→最终卡入账
    ///
    /// 入口：WishPoolConfig Inspector 底部「概率模拟测试」按钮（测试当前选中卡池）
    /// </summary>
    public static class WishProbabilityTest
    {
        private const int Iterations = 100_000;
        // 里程碑阈值与运行时共用（SaveProgress 常量）：20 累计星辉→1 相遇之缘，200→1 纠缠之缘
        private const int AcquaintThreshold = SaveProgress.AcquaintFateThreshold;
        private const int IntertwinedThreshold = SaveProgress.IntertwinedFateThreshold;

        // 星辉转换表
        private static readonly int[] StarglitterByStar = { 0, 3, 8, 15, 25, 50 }; // index 1~5

        public static void RunTest(WishPoolConfig pool)
        {
            var unitConfig = LoadAsset<UnitConfig>("UnitConfig");
            var itemConfig = LoadAsset<ItemConfig>("ItemConfig");
            if (pool == null || unitConfig == null || itemConfig == null)
            {
                GICLog.Error("[祈愿测试] 配置加载失败。");
                return;
            }

            GICLog.Info($"[祈愿测试] 卡池: {pool.poolName} ({AssetDatabase.GetAssetPath(pool)})");

            // ── 构建星级缓存 ──
            var unitsByStar = new Dictionary<int, List<UnitName>>();
            var itemsByStar = new Dictionary<int, List<ItemName>>();
            for (int star = 1; star <= 5; star++)
            {
                unitsByStar[star] = pool.GetUnitsByStar(unitConfig, star);
                itemsByStar[star] = pool.GetItemsByStar(itemConfig, star);
            }

            float totalStarW = pool.star5Weight + pool.star4Weight + pool.star3Weight + pool.star2Weight + pool.star1Weight;
            float totalTypeW = pool.unitWeight + pool.itemWeight;

            // ── 模拟状态（从零开始，无预拥有）──
            var ownedUnits = new HashSet<UnitName>();
            int starglitterEarned = 0;
            int acquaintFates = 0;        // 背包相遇之缘
            int intertwinedFates = 0;      // 背包纠缠之缘

            // ── 统计 ──
            var finalStarCounts = new int[6];      // 最终入账卡的星级
            var rawStarCounts = new int[6];         // 原始 track card 星级
            int encounterTriggered = 0;
            int encounterRefunded = 0;              // 相遇之线原始5★→退回
            int intertwinedRefunded = 0;           // 纠缠之线原始5★→退回
            int encounterShots = 0;                 // 相遇之线射击数
            int intertwinedShots = 0;               // 纠缠之线射击数
            int acquaintGranted = 0;                // 累计发放相遇之缘
            int intertwinedGranted = 0;            // 累计发放纠缠之缘
            int normalShotCount = 0;
            int totalStarglitterEarned = 0;

            // 星辉里程碑发放（镜像 WishManager.AddStarglitter 的跨越档位差值逻辑）
            void AddStarglitter(int sg)
            {
                int before = starglitterEarned;
                starglitterEarned += sg;
                totalStarglitterEarned += sg;
                int a = starglitterEarned / AcquaintThreshold - before / AcquaintThreshold;
                if (a > 0) { acquaintFates += a; acquaintGranted += a; }
                int t = starglitterEarned / IntertwinedThreshold - before / IntertwinedThreshold;
                if (t > 0) { intertwinedFates += t; intertwinedGranted += t; }
            }

            int unitResultCount = 0, itemResultCount = 0;
            var encounterUpgradeDist = new int[5];      // 相遇之线升0~4级
            var intertwinedUpgradeDist = new int[5];    // 纠缠之线升1~4级（0 恒空——必升表）
            var encounterFinalStar = new int[6];        // 相遇之线最终星级
            var intertwinedFinalStar = new int[6];      // 纠缠之线最终星级
            var cardFrequency = new Dictionary<CardId, int>();
            int moraFallbackCount = 0;

            // ── 10万次模拟 ──
            for (int i = 0; i < Iterations; i++)
            {
                // === 1. 抽 track card ===
                int rawStar = pool.RollStarLevel();
                bool isUnit = pool.RollIsUnit();
                int starLevel = rawStar;
                rawStarCounts[rawStar]++;

                CardId cardId = new CardId(ItemName.Mora);
                bool cardIsUnit = isUnit;
                int resolvedStar = starLevel;

                if (isUnit)
                {
                    var candidates = GetCachedByStar(unitsByStar, ref starLevel);
                    if (candidates.Count > 0)
                    {
                        cardId = new CardId(candidates[Random.Range(0, candidates.Count)]);
                        resolvedStar = starLevel;
                    }
                    else
                    {
                        cardIsUnit = false; // 回退到物品
                    }
                }

                if (!cardIsUnit)
                {
                    int itemStar = starLevel;
                    var itemCandidates = GetCachedByStar(itemsByStar, ref itemStar);
                    if (itemCandidates.Count == 0)
                    {
                        cardId = new CardId(ItemName.Mora);
                        resolvedStar = itemConfig.GetItemData(ItemName.Mora)?.starLevel ?? 1;
                        moraFallbackCount++;
                    }
                    else
                    {
                        cardId = new CardId(itemCandidates[Random.Range(0, itemCandidates.Count)]);
                        resolvedStar = itemStar;
                    }
                }

                // === 2. 命运之缘消耗（纠缠之缘优先于相遇之缘，镜像 WishManager.ConsumeFateForShot） ===
                bool isEncounter = intertwinedFates > 0 || acquaintFates > 0;
                bool usedIntertwined = intertwinedFates > 0;

                if (isEncounter)
                {
                    encounterTriggered++;
                    if (usedIntertwined) { intertwinedFates--; intertwinedShots++; }
                    else { acquaintFates--; encounterShots++; }

                    // Roll 升级次数（相遇之线=0-4 档权重表；纠缠之线=必升 1-4 档权重表，2026-09-06 拍板差异化）
                    int upgradeCount = usedIntertwined ? pool.RollIntertwinedUpgradeCount() : pool.RollUpgradeCount();
                    upgradeCount = Mathf.Min(upgradeCount, 5 - resolvedStar);
                    if (usedIntertwined) intertwinedUpgradeDist[upgradeCount]++;
                    else encounterUpgradeDist[upgradeCount]++;

                    // 原始5★→退回本发消耗的命运之缘物品
                    if (resolvedStar >= 5)
                    {
                        if (usedIntertwined) { intertwinedRefunded++; intertwinedFates++; }
                        else { encounterRefunded++; acquaintFates++; }
                        isEncounter = false; // 按普通处理
                    }
                    else
                    {
                        // 逐级提升：原始卡 + 每级中间卡都判重发星辉
                        int currentStar = resolvedStar;
                        CardId currentCardId = cardId;

                        // 原始卡判重
                        if (cardIsUnit && ownedUnits.Contains(currentCardId.AsUnitName()))
                        {
                            AddStarglitter(StarglitterByStar[currentStar]);
                        }

                        // 逐级提升
                        for (int u = 0; u < upgradeCount; u++)
                        {
                            currentStar++;
                            if (currentStar > 5) currentStar = 5;

                            // 从新星级选卡（保持角色/物品类型）
                            if (cardIsUnit)
                            {
                                var cands = unitsByStar.TryGetValue(currentStar, out var cu) ? cu : new List<UnitName>();
                                if (cands.Count == 0)
                                {
                                    // 该星级无候选，尝试降级
                                    for (int s = currentStar - 1; s >= 1; s--)
                                    {
                                        if (unitsByStar[s].Count > 0) { cands = unitsByStar[s]; currentStar = s; break; }
                                    }
                                }
                                if (cands.Count > 0)
                                    currentCardId = new CardId(cands[Random.Range(0, cands.Count)]);
                            }
                            else
                            {
                                var cands = itemsByStar.TryGetValue(currentStar, out var ci) ? ci : new List<ItemName>();
                                if (cands.Count == 0)
                                {
                                    for (int s = currentStar - 1; s >= 1; s--)
                                    {
                                        if (itemsByStar[s].Count > 0) { cands = itemsByStar[s]; currentStar = s; break; }
                                    }
                                }
                                if (cands.Count > 0)
                                    currentCardId = new CardId(cands[Random.Range(0, cands.Count)]);
                            }

                            // 判重发星辉
                            if (cardIsUnit && ownedUnits.Contains(currentCardId.AsUnitName()))
                            {
                                AddStarglitter(StarglitterByStar[currentStar]);
                            }
                        }

                        // 最终卡入账
                        resolvedStar = currentStar;
                        cardId = currentCardId;
                        if (cardIsUnit) ownedUnits.Add(cardId.AsUnitName());
                        if (usedIntertwined) intertwinedFinalStar[resolvedStar]++;
                        else encounterFinalStar[resolvedStar]++;
                    }
                }

                if (!isEncounter)
                {
                    // === 普通射击 ===
                    normalShotCount++;

                    // 判重发星辉
                    if (cardIsUnit && ownedUnits.Contains(cardId.AsUnitName()))
                    {
                        AddStarglitter(StarglitterByStar[resolvedStar]);
                    }

                    // 入账
                    if (cardIsUnit) ownedUnits.Add(cardId.AsUnitName());
                }

                // 统计
                finalStarCounts[resolvedStar]++;
                if (cardIsUnit) unitResultCount++; else itemResultCount++;
                if (cardFrequency.ContainsKey(cardId)) cardFrequency[cardId]++; else cardFrequency[cardId] = 1;
            }

            // ── 输出 ──
            var sb = new StringBuilder();

            // 卡池构成
            sb.AppendLine("[祈愿测试] ========== 卡池构成 ==========");
            for (int s = 5; s >= 1; s--)
            {
                sb.AppendLine($"  {s}★ — 角色({unitsByStar[s].Count}): [{string.Join(", ", unitsByStar[s])}]  " +
                              $"物品({itemsByStar[s].Count}): [{string.Join(", ", itemsByStar[s])}]");
            }
            sb.AppendLine($"[祈愿测试] 权重 — 5★:{pool.star5Weight} 4★:{pool.star4Weight} 3★:{pool.star3Weight} 2★:{pool.star2Weight} 1★:{pool.star1Weight} | 角色:{pool.unitWeight} 物品:{pool.itemWeight}");
            sb.AppendLine($"[祈愿测试] 初始状态: 无预拥有角色, starglitterEarned=0");

            // 总览
            sb.AppendLine();
            sb.AppendLine($"[祈愿测试] ========== {Iterations:N0}次完整模拟（含命运之缘升级射击）==========");
            sb.AppendLine($"  普通射击: {normalShotCount:N0} ({normalShotCount / (float)Iterations * 100f:F1}%)");
            sb.AppendLine($"  相遇之线(相遇之缘): {encounterShots:N0} ({encounterShots / (float)Iterations * 100f:F2}%)");
            sb.AppendLine($"  纠缠之线(纠缠之缘): {intertwinedShots:N0} ({intertwinedShots / (float)Iterations * 100f:F2}%)");
            sb.AppendLine($"  5★退回命运之缘: 相遇 {encounterRefunded:N0} / 纠缠 {intertwinedRefunded:N0}");
            sb.AppendLine($"  累计获得星辉: {totalStarglitterEarned:N0}");
            sb.AppendLine($"  命运之缘发放 — 相遇之缘: {acquaintGranted:N0} (每{AcquaintThreshold}星辉) / 纠缠之缘: {intertwinedGranted:N0} (每{IntertwinedThreshold}星辉)");
            sb.AppendLine($"  命运之缘剩余 — 相遇之缘: {acquaintFates:N0} / 纠缠之缘: {intertwinedFates:N0}（10万发后背包余量）");
            sb.AppendLine($"  最终 starglitterEarned: {starglitterEarned:N0} → 距下1相遇之缘: {AcquaintThreshold - starglitterEarned % AcquaintThreshold} / 距下1纠缠之缘: {IntertwinedThreshold - starglitterEarned % IntertwinedThreshold}");

            // 原始Roll vs 最终入账星级
            sb.AppendLine();
            sb.AppendLine("─ 星级分布：原始Roll vs 最终入账（相遇后）──");
            sb.AppendLine($"  {"星级",-4} {"原始Roll",-14} {"最终入账",-14} {"原始%",-8} {"最终%",-8} {"变化",-8}");
            for (int s = 5; s >= 1; s--)
            {
                float rawExp = StarWeight(pool, s) / totalStarW * 100f;
                float rawAct = rawStarCounts[s] / (float)Iterations * 100f;
                float finalAct = finalStarCounts[s] / (float)Iterations * 100f;
                float diff = finalAct - rawAct;
                sb.AppendLine($"  {s}★   {rawStarCounts[s],8:N0}      {finalStarCounts[s],8:N0}      {rawAct:F2}%   {finalAct:F2}%   {diff:+0.00;-0.00;0.00}%");
            }

            // 角色/物品
            sb.AppendLine();
            sb.AppendLine("─ 最终入账 角色/物品 ─");
            sb.AppendLine($"  角色: {unitResultCount:N0} ({unitResultCount / (float)Iterations * 100f:F2}%)");
            sb.AppendLine($"  物品: {itemResultCount:N0} ({itemResultCount / (float)Iterations * 100f:F2}%)");

            // 命运之缘升级射击详情（相遇/纠缠分表统计——2026-09-06 纠缠必升 1-4 档差异化）
            sb.AppendLine();
            sb.AppendLine("─ 相遇之线升级次数分布（0-4 档权重表） ─");
            for (int u = 4; u >= 0; u--)
            {
                float pct = encounterShots > 0 ? encounterUpgradeDist[u] / (float)encounterShots * 100f : 0;
                sb.AppendLine($"  升{u}级: {encounterUpgradeDist[u],6:N0} ({pct:F2}%)");
            }

            sb.AppendLine();
            sb.AppendLine("─ 纠缠之线升级次数分布（必升 1-4 档权重表 10/40/40/10） ─");
            for (int u = 4; u >= 1; u--)
            {
                float pct = intertwinedShots > 0 ? intertwinedUpgradeDist[u] / (float)intertwinedShots * 100f : 0;
                sb.AppendLine($"  升{u}级: {intertwinedUpgradeDist[u],6:N0} ({pct:F2}%)");
            }

            sb.AppendLine();
            sb.AppendLine("─ 升级射击最终星级分布（相遇 / 纠缠） ─");
            int encDone = encounterShots - encounterRefunded;
            int itwDone = intertwinedShots - intertwinedRefunded;
            for (int s = 5; s >= 1; s--)
            {
                float pe = encDone > 0 ? encounterFinalStar[s] / (float)encDone * 100f : 0;
                float pi = itwDone > 0 ? intertwinedFinalStar[s] / (float)itwDone * 100f : 0;
                sb.AppendLine($"  最终{s}★: 相遇 {encounterFinalStar[s],7:N0} ({pe:F2}%)  纠缠 {intertwinedFinalStar[s],7:N0} ({pi:F2}%)");
            }

            // Top 20 单卡
            sb.AppendLine();
            sb.AppendLine("─ 单卡出现频率 Top 20（最终入账卡）─");
            var sorted = new List<KeyValuePair<CardId, int>>(cardFrequency);
            sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
            int topN = Mathf.Min(20, sorted.Count);
            for (int i = 0; i < topN; i++)
            {
                var kv = sorted[i];
                float pct = kv.Value / (float)Iterations * 100f;
                sb.AppendLine($"  {i + 1,2}. {kv.Key,-20} {kv.Value,6:N0} ({pct:F2}%)");
            }

            if (moraFallbackCount > 0)
                sb.AppendLine($"\n  ⚠️ Mora兜底: {moraFallbackCount} 次");

            GICLog.Info(sb.ToString());
        }

        // ── 工具 ──

        private static T LoadAsset<T>(string typeName) where T : ScriptableObject
        {
            var guids = AssetDatabase.FindAssets($"t:{typeName}");
            if (guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static List<T> GetCachedByStar<T>(Dictionary<int, List<T>> cache, ref int starLevel)
        {
            var candidates = cache[starLevel];
            if (candidates.Count > 0) return candidates;
            for (int s = starLevel - 1; s >= 1; s--)
            {
                candidates = cache[s];
                if (candidates.Count > 0) { starLevel = s; return candidates; }
            }
            return candidates;
        }

        private static float StarWeight(WishPoolConfig pool, int star)
            => star switch { 5 => pool.star5Weight, 4 => pool.star4Weight, 3 => pool.star3Weight, 2 => pool.star2Weight, _ => pool.star1Weight };
    }
}
#endif
