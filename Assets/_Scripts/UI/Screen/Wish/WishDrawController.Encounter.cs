using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Battle;
using GIC.Tool;

namespace GIC.UI
{
    /// <summary>
    /// 相遇之线 — 星级提升 + 抖动变亮
    /// </summary>
    public partial class WishDrawController
    {
        /// <summary>
        /// 相遇之线——星级提升协程
        ///
        /// 流程：
        ///   射中卡(原始卡)
        ///     → 检查重复 → 发放星辉(累积)
        ///     → 抖动+越来越亮动画开始（贯穿整个提升过程）
        ///   逐级提升（每升1级）：
        ///     → 从新星级随机选卡 → 更新卡片显示
        ///     → 检查重复 → 发放星辉
        ///     → 光带+光柱+边缘泛光
        ///   抖动结束 → 最终卡入账（星辉已在展示时发放完毕，不重复）
        ///     → 飞到左侧
        /// </summary>
        private IEnumerator EncounterRevealCoroutine(Card card, RectTransform rect, int index)
        {
            int starLevel = card.saveCardData.StarLevel;
            bool isUnit = card.saveCardData.cardType == CardType.Unit;

            // 1. 先 Roll 提升次数（决定最终星级），不超过 5★上限
            int upgradeCount = _wishManager.RollUpgradeCount(_pool);
            upgradeCount = Mathf.Min(upgradeCount, 5 - starLevel);

            // 2. 如果已经是 5★，无法提升，返还相遇之线，按普通射击处理
            if (starLevel >= 5)
            {
                _wishManager.RefundEncounter();
                AddResultAndStarglitter(BuildResultFromCard(card));
                yield return PlayStar5TransitionCoroutine();
                PlayCardHitEffects(starLevel, card);
                UpdateStarglitterProgressBar();

                StartHoldThenFly(rect, index, starLevel);

                _isEncounterAnimating = false;
                _isEncounterShot = false;
                _cooldownTimer = StarVisualConfig.GetHoldTime(starLevel) + 0.5f;
                _totalCooldownTime = _cooldownTimer;
                UpdateStarglitterProgressBar();
                yield break;
            }

            // 3. 开始抖动+越来越亮（贯穿整个提升过程，结果已算好但保留期待感）
            Vector2 cardOriginalPos = rect.anchoredPosition;
            _isEncounterAnimating = true;
            bool hasGlow = card.glowImage != null;
            if (hasGlow) card.glowImage.gameObject.SetActive(true);
            Coroutine shakeGlow = StartCoroutine(CardShakeGlowCoroutine(rect, card.glowImage, starLevel, upgradeCount, StarVisualConfig.ShakeBaseDuration));

            // 4. 原始卡立即结算：展示特效 + 检查重复 → 发放星辉
            WishResult currentResult = BuildResultFromCard(card);
            PlayCardHitEffects(starLevel, card);
            AwardAndRainStarglitter(currentResult);

            // 5. 逐级提升：每级新卡 展示特效 + 检查重复 → 发放星辉
            for (int i = 0; i < upgradeCount; i++)
            {
                yield return new WaitForSeconds(StarVisualConfig.GetShakeDuration(starLevel));

                starLevel++;
                if (starLevel > 5) starLevel = 5;

                // 从新星级随机选卡
                var upgraded = _wishManager.RollCardByStar(_pool, starLevel, isUnit);
                if (upgraded.cardId.value == 0) break; // 该星级无候选，中止提升

                // 更新卡片显示为本级新卡
                var newSaveData = new SaveCardData();
                if (isUnit)
                    newSaveData.SaveUnit(upgraded.cardId.AsUnitName(), 1);
                else
                    newSaveData.SaveItem(upgraded.cardId.AsItemName(), 1);
                card.Init(newSaveData, null);

                // 本级新卡：展示特效 + 检查重复 → 发放星辉
                PlayCardHitEffects(starLevel, card);
                AwardAndRainStarglitter(upgraded);

                currentResult = upgraded;
            }

            // 6. 让最终星级的抖动阶段播完（5★不抖）
            if (starLevel < 5)
                yield return new WaitForSeconds(StarVisualConfig.GetShakeDuration(starLevel));

            // 7. 停止抖动，清理发光层，恢复原位置
            if (shakeGlow != null) StopCoroutine(shakeGlow);
            if (hasGlow && card.glowImage != null)
            {
                card.glowImage.color = new Color(1f, 0.95f, 0.6f, 0f);
                card.glowImage.gameObject.SetActive(false);
            }
            rect.anchoredPosition = cardOriginalPos;
            rect.localEulerAngles = Vector3.zero;

            // 8. 最终卡入账（星辉已在展示阶段发放完毕，这里不重复发放）
            _wishManager.AddFinalResultToInventory(currentResult);
            _results.Add(currentResult);

            // 8.5 五星过渡视频
            if (starLevel >= 5)
                yield return PlayStar5TransitionCoroutine();

            // 9. 播放最终星级特效
            PlayCardHitEffects(starLevel, card);
            UpdateStarglitterProgressBar();

            // 10. 停留+飞到左侧（与普通射击一致的 holdThenFly 节奏）
            StartHoldThenFly(rect, index, starLevel);

            // 重置冷却覆盖 hold+fly 时长，避免 ShowFinalDisplay 提前杀掉 HoldThenFly
            _cooldownTimer = StarVisualConfig.GetHoldTime(starLevel) + 0.5f;
            _totalCooldownTime = _cooldownTimer;

            // 相遇之线结束，恢复正常进度条
            _isEncounterAnimating = false;
            _isEncounterShot = false;
            UpdateStarglitterProgressBar();
        }

        /// <summary>
        /// 检查重复并发放星辉 + 播放星辉雨（相遇之线每张展示卡统一调用）
        /// </summary>
        private void AwardAndRainStarglitter(WishResult result)
        {
            int sg = _wishManager.AwardDuplicateStarglitter(result);
            if (sg > 0)
            {
                StartCoroutine(StarglitterRainCoroutine(sg));
                UpdateStarglitterProgressBar();
            }
        }

        /// <summary>
        /// 卡片抖动+越来越亮动画
        /// 每个星级阶段有独立的正弦波，幅度和持续时间都随星级递增
        /// 通过预制体上的 glowImage（Additive材质）控制亮度，不修改原始 Image 颜色
        /// </summary>
        private IEnumerator CardShakeGlowCoroutine(RectTransform rect, Image glowImage, int startStarLevel, int upgradeCount, float baseDuration)
        {
            Vector2 basePos = rect.anchoredPosition;

            float[] phaseDurations = new float[upgradeCount + 1];
            float totalDuration = 0f;
            for (int i = 0; i <= upgradeCount; i++)
            {
                int star = Mathf.Clamp(startStarLevel + i, 1, 5);
                phaseDurations[i] = baseDuration * (1f + (star - 1) * 0.3f);
                totalDuration += phaseDurations[i];
            }

            float elapsed = 0f;
            float shakeTimer = 0f;
            Vector2 currentShake = Vector2.zero;
            Vector2 targetShake = Vector2.zero;
            while (elapsed < totalDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / totalDuration;

                float phaseStart = 0f;
                int phaseIndex = 0;
                for (int i = 0; i < phaseDurations.Length; i++)
                {
                    if (elapsed < phaseStart + phaseDurations[i])
                    {
                        phaseIndex = i;
                        break;
                    }
                    phaseStart += phaseDurations[i];
                    phaseIndex = i;
                }
                float phaseT = (elapsed - phaseStart) / phaseDurations[phaseIndex];
                int currentStar = Mathf.Clamp(startStarLevel + phaseIndex, 1, 5);

                float baseIntensity = StarVisualConfig.GetShakeIntensity(currentStar);
                float shakeIntensity = baseIntensity * phaseT;

                shakeTimer += Time.deltaTime;
                if (shakeTimer >= StarVisualConfig.ShakeInterval)
                {
                    shakeTimer = 0f;
                    targetShake = new Vector2(
                        Random.Range(-shakeIntensity, shakeIntensity),
                        Random.Range(-shakeIntensity, shakeIntensity));
                }
                currentShake = Vector2.Lerp(currentShake, targetShake, Time.deltaTime / StarVisualConfig.ShakeInterval);
                rect.anchoredPosition = basePos + currentShake;

                if (glowImage != null)
                {
                    float maxGlow = StarVisualConfig.GetMaxGlow(currentStar);
                    float glowT = t * t * (3f - 2f * t);
                    float alpha = Mathf.Clamp01(maxGlow * glowT);
                    glowImage.color = new Color(StarVisualConfig.EncounterGlowColor.r, StarVisualConfig.EncounterGlowColor.g, StarVisualConfig.EncounterGlowColor.b, alpha);
                }

                yield return null;
            }
        }
    }
}
