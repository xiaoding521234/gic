#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using GIC.Data;
using GIC.Battle;

namespace GIC.Framework
{
    /// <summary>
    /// 祈愿概率完整模拟 — 10万次连续祈愿，忠实复现完整流程：
    ///   1. DrawRandomTrackCard（星级降级、角色→物品回退、Mora兜底）
    ///   2. 重复角色卡→星辉→累积→满20触发相遇之线
    ///   3. 相遇之线：RollUpgradeCount→逐级提升→每级判重发星辉→最终卡入账
    ///
    /// 菜单：Tools/测试/祈愿概率测试 (10万次)
    /// </summary>
    public static class WishProbabilityTest
    {
        private const int Iterations = 100_000;
        private const int EncounterThreshold = 20;

        // 星辉转换表
        private static readonly int[] StarglitterByStar = { 0, 3, 8, 15, 25, 50 }; // index 1~5

        [MenuItem("Tools/测试/祈愿概率测试 (10万次)")]
        public static void RunTest()
        {
            var pool = LoadAsset<WishPoolConfig>("WishPoolConfig");
            var unitConfig = LoadAsset<UnitConfig>("UnitConfig");
            var itemConfig = LoadAsset<ItemConfig>("ItemConfig");
            if (pool == null || unitConfig == null || itemConfig == null)
            {
                Debug.LogError("[祈愿测试] 配置加载失败。");
                return;
            }

            Debug.Log($"[祈愿测试] 卡池: {pool.poolName} ({AssetDatabase.GetAssetPath(pool)})");

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
            int encounterUsed = 0;

            // ── 统计 ──
            var finalStarCounts = new int[6];      // 最终入账卡的星级
            var rawStarCounts = new int[6];         // 原始 track card 星级
            int encounterTriggered = 0;
            int encounterRefunded = 0;              // 原始5★→退回
            int normalShotCount = 0;
            int unitResultCount = 0, itemResultCount = 0;
            int totalStarglitterEarned = 0;
            var encounterUpgradeDist = new int[5];  // 升0~4级
            var encounterFinalStar = new int[6];    // 相遇之线最终星级
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

                // === 2. 检查相遇之线 ===
                int encounterCharges = starglitterEarned / EncounterThreshold - encounterUsed;
                bool isEncounter = encounterCharges > 0;

                if (isEncounter)
                {
                    encounterTriggered++;
                    encounterUsed++;

                    // Roll 升级次数（独立权重表）
                    int upgradeCount = pool.RollUpgradeCount();
                    upgradeCount = Mathf.Min(upgradeCount, 5 - resolvedStar);
                    encounterUpgradeDist[upgradeCount]++;

                    // 原始5★→退回
                    if (resolvedStar >= 5)
                    {
                        encounterRefunded++;
                        encounterUsed--;
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
                            int sg = StarglitterByStar[currentStar];
                            starglitterEarned += sg;
                            totalStarglitterEarned += sg;
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
                                int sg = StarglitterByStar[currentStar];
                                starglitterEarned += sg;
                                totalStarglitterEarned += sg;
                            }
                        }

                        // 最终卡入账
                        resolvedStar = currentStar;
                        cardId = currentCardId;
                        if (cardIsUnit) ownedUnits.Add(cardId.AsUnitName());
                        encounterFinalStar[resolvedStar]++;
                    }
                }

                if (!isEncounter)
                {
                    // === 普通射击 ===
                    normalShotCount++;

                    // 判重发星辉
                    if (cardIsUnit && ownedUnits.Contains(cardId.AsUnitName()))
                    {
                        int sg = StarglitterByStar[resolvedStar];
                        starglitterEarned += sg;
                        totalStarglitterEarned += sg;
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
            sb.AppendLine($"[祈愿测试] ========== {Iterations:N0}次完整模拟（含相遇之线）==========");
            sb.AppendLine($"  普通射击: {normalShotCount:N0} ({normalShotCount / (float)Iterations * 100f:F1}%)");
            sb.AppendLine($"  相遇之线: {encounterTriggered - encounterRefunded:N0} ({(encounterTriggered - encounterRefunded) / (float)Iterations * 100f:F1}%)");
            sb.AppendLine($"  相遇退回(原5★): {encounterRefunded:N0}");
            sb.AppendLine($"  累计获得星辉: {totalStarglitterEarned:N0}");
            sb.AppendLine($"  最终 starglitterEarned: {starglitterEarned:N0} → 相遇余量: {starglitterEarned / EncounterThreshold - encounterUsed}");

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

            // 相遇之线详情
            sb.AppendLine();
            sb.AppendLine("─ 相遇之线升级次数分布 ─");
            int totalEnc = encounterTriggered - encounterRefunded;
            for (int u = 4; u >= 0; u--)
            {
                float pct = totalEnc > 0 ? encounterUpgradeDist[u] / (float)totalEnc * 100f : 0;
                sb.AppendLine($"  升{u}级: {encounterUpgradeDist[u],6:N0} ({pct:F2}%)");
            }

            sb.AppendLine();
            sb.AppendLine("─ 相遇之线最终星级分布 ─");
            for (int s = 5; s >= 1; s--)
            {
                float pct = totalEnc > 0 ? encounterFinalStar[s] / (float)totalEnc * 100f : 0;
                sb.AppendLine($"  最终{s}★: {encounterFinalStar[s],6:N0} ({pct:F2}%)");
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

            Debug.Log(sb.ToString());
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
