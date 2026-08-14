using System.Collections.Generic;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
using GIC.Battle;

namespace GIC.UI
{
    /// <summary>
    /// 祈愿流程状态机 — 纯 C# 类，预计算全部业务逻辑并构建有序动画步骤。
    /// 表现层（WishDrawController）订阅 OnShotPlanned 事件，按 steps 播放动画。
    /// </summary>
    public class WishFlowController
    {
        public enum State
        {
            Idle,       // 未开始
            Drawing,    // 卡道滚动中，等待玩家点击射击
            Revealing,  // 射击后揭示动画中（冻结冷却倒计时）
            Finished,   // 全部射击完成
        }

        public State CurrentState { get; private set; } = State.Idle;

        private readonly WishManager _wishManager;
        private readonly WishPoolConfig _pool;

        private int _totalShots;
        private int _shotsCompleted;

        // 星级→候选列表缓存
        private Dictionary<int, List<UnitName>> _unitsByStar;
        private Dictionary<int, List<ItemName>> _itemsByStar;

        public int ShotsCompleted => _shotsCompleted;
        public int TotalShots => _totalShots;
        public bool IsEncounterReady => _wishManager.IsEncounterReady();

        public WishFlowController(WishManager wishManager, WishPoolConfig pool)
        {
            _wishManager = wishManager;
            _pool = pool;
        }

        /// <summary>
        /// 开始祈愿流程
        /// </summary>
        public void StartFlow(int count)
        {
            _totalShots = count;
            _shotsCompleted = 0;
            CurrentState = State.Drawing;
            BuildStarLevelCache();
        }

        /// <summary>
        /// 构建星级→候选列表缓存
        /// </summary>
        private void BuildStarLevelCache()
        {
            var unitConfig = _wishManager.GetUnitConfig();
            var itemConfig = _wishManager.GetItemConfig();

            _unitsByStar = new Dictionary<int, List<UnitName>>();
            _itemsByStar = new Dictionary<int, List<ItemName>>();

            for (int star = 1; star <= 5; star++)
            {
                _unitsByStar[star] = _pool.GetUnitsByStar(unitConfig, star);
                _itemsByStar[star] = _pool.GetItemsByStar(itemConfig, star);
            }
        }

        /// <summary>
        /// 缓存查询接口（供 WishDrawController.Track 使用）
        /// </summary>
        public List<UnitName> GetCachedUnitsByStar(int star) => _unitsByStar.TryGetValue(star, out var u) ? u : new List<UnitName>();
        public List<ItemName> GetCachedItemsByStar(int star) => _itemsByStar.TryGetValue(star, out var i) ? i : new List<ItemName>();

        /// <summary>
        /// 预计算一次射击的完整结果（含全部业务逻辑写入），返回有序动画步骤。
        /// 调用后 CurrentState 变为 Revealing。
        /// </summary>
        public WishShotResult PlanShot(Card trackCard, int shotIndex)
        {
            if (CurrentState != State.Drawing) return null;
            if (trackCard == null || trackCard.saveCardData == null) return null;

            CurrentState = State.Revealing;
            _shotsCompleted++;

            int starLevel = trackCard.saveCardData.StarLevel;
            bool isEncounter = _wishManager.IsEncounterReady();

            if (isEncounter)
                _wishManager.ConsumeEncounter();

            WishShotResult result;
            if (isEncounter && starLevel < 5)
            {
                result = PlanEncounterShot(trackCard, shotIndex, starLevel);
            }
            else
            {
                // encounter 且 starLevel >= 5：退回相遇之线，按普通处理
                if (isEncounter)
                    _wishManager.RefundEncounter();
                result = PlanNormalShot(trackCard, shotIndex, starLevel);
            }

            result.isEncounter = isEncounter && starLevel < 5;
            result.shotIndex = shotIndex;

            // 状态保持 Revealing，等表现层调用 OnRevealComplete 后再转回
            return result;
        }

        /// <summary>
        /// 表现层动画播放完毕后调用，将状态从 Revealing 转回 Drawing 或 Finished。
        /// </summary>
        public void OnRevealComplete()
        {
            if (CurrentState != State.Revealing) return;

            if (_shotsCompleted >= _totalShots)
                CurrentState = State.Finished;
            else
                CurrentState = State.Drawing;
        }

        /// <summary>
        /// 普通射击路径（含5★和相遇之线已有5★退回）
        /// </summary>
        private WishShotResult PlanNormalShot(Card card, int shotIndex, int starLevel)
        {
            var result = new WishShotResult
            {
                finalStarLevel = starLevel,
            };

            // 5★: 视频在最前
            if (starLevel >= 5)
                result.steps.Add(new WishRevealStep { type = WishRevealStep.StepType.Star5Video });

            // 入账 + 判重发星辉（业务逻辑）
            var wishResult = BuildResultFromCard(card);
            _wishManager.AddResultToInventory(wishResult, out int starglitter);
            wishResult.starglitterAmount = starglitter;

            // 特效
            result.steps.Add(new WishRevealStep
            {
                type = WishRevealStep.StepType.CardEffects,
                starLevel = starLevel,
            });

            // 星辉雨
            if (starglitter > 0)
            {
                result.steps.Add(new WishRevealStep
                {
                    type = WishRevealStep.StepType.StarglitterRain,
                    starglitterAmount = starglitter,
                });
            }

            // 停留+飞行
            result.steps.Add(new WishRevealStep
            {
                type = WishRevealStep.StepType.HoldThenFly,
                starLevel = starLevel,
            });

            return result;
        }

        /// <summary>
        /// 相遇之线升级路径
        /// </summary>
        private WishShotResult PlanEncounterShot(Card card, int shotIndex, int baseStarLevel)
        {
            bool isUnit = card.saveCardData.cardType == CardType.Unit;

            // 1. Roll 升级次数
            int upgradeCount = _wishManager.RollUpgradeCount(_pool);
            upgradeCount = Mathf.Min(upgradeCount, 5 - baseStarLevel);

            var result = new WishShotResult();

            // 2. 开始抖动
            result.steps.Add(new WishRevealStep
            {
                type = WishRevealStep.StepType.EncounterShakeStart,
                baseStarLevel = baseStarLevel,
                upgradeCount = upgradeCount,
            });

            // 3. 原始卡立即结算：判重 → 发放星辉
            var currentResult = BuildResultFromCard(card);
            int sg = _wishManager.AwardDuplicateStarglitter(currentResult);
            result.steps.Add(new WishRevealStep
            {
                type = WishRevealStep.StepType.EncounterUpgrade,
                starLevel = baseStarLevel,
                starglitterAmount = sg,
                cardData = null, // 原始卡不换卡
                shakeDelay = 0f, // 原始卡无延迟
            });

            // 4. 逐级提升
            int starLevel = baseStarLevel;
            int pendingRain5 = 0;

            for (int i = 0; i < upgradeCount; i++)
            {
                starLevel++;
                if (starLevel > 5) starLevel = 5;

                var upgraded = _wishManager.RollCardByStar(_pool, starLevel, isUnit);
                if (upgraded.cardId.value == 0) break;

                // 构建新卡片数据
                var newSaveData = new SaveCardData();
                if (isUnit)
                    newSaveData.SaveUnit(upgraded.cardId.AsUnitName(), 1);
                else
                {
                    int itemCount = _wishManager.GetItemConfig().GetItemData(upgraded.cardId.AsItemName())?.countPerServing ?? 1;
                    newSaveData.SaveItem(upgraded.cardId.AsItemName(), itemCount);
                }

                // 判重发星辉（5★只入账不播雨）
                int stepSg = _wishManager.AwardDuplicateStarglitter(upgraded);
                if (starLevel >= 5)
                    pendingRain5 += stepSg;
                else
                {
                    result.steps.Add(new WishRevealStep
                    {
                        type = WishRevealStep.StepType.EncounterUpgrade,
                        starLevel = starLevel,
                        starglitterAmount = stepSg,
                        cardData = newSaveData,
                        shakeDelay = StarVisualConfig.GetShakeDuration(starLevel - 1),
                    });
                }

                // 5★升级步（只换卡+入账星辉，雨延迟到视频后）
                if (starLevel >= 5)
                {
                    result.steps.Add(new WishRevealStep
                    {
                        type = WishRevealStep.StepType.EncounterUpgrade,
                        starLevel = starLevel,
                        starglitterAmount = 0, // 雨延迟
                        cardData = newSaveData,
                        shakeDelay = StarVisualConfig.GetShakeDuration(starLevel - 1),
                    });
                }

                currentResult = upgraded;
            }

            // 5. 停止抖动
            result.steps.Add(new WishRevealStep
            {
                type = WishRevealStep.StepType.EncounterShakeEnd,
            });

            // 6. 最终卡入账
            _wishManager.AddFinalResultToInventory(currentResult);
            result.finalStarLevel = starLevel;

            // 7. 5★过渡视频（在特效之前）
            if (starLevel >= 5)
                result.steps.Add(new WishRevealStep { type = WishRevealStep.StepType.Star5Video });

            // 8. 最终特效
            result.steps.Add(new WishRevealStep
            {
                type = WishRevealStep.StepType.CardEffects,
                starLevel = starLevel,
            });

            // 9. 星辉雨（含延迟的5★星辉）
            int totalRain = pendingRain5;
            if (totalRain > 0)
            {
                result.steps.Add(new WishRevealStep
                {
                    type = WishRevealStep.StepType.StarglitterRain,
                    starglitterAmount = totalRain,
                });
            }

            // 10. 停留+飞行
            result.steps.Add(new WishRevealStep
            {
                type = WishRevealStep.StepType.HoldThenFly,
                starLevel = starLevel,
            });

            return result;
        }

        /// <summary>
        /// 从 Card 构建 WishResult
        /// </summary>
        private WishResult BuildResultFromCard(Card card)
        {
            bool isUnit = card.saveCardData.cardType == CardType.Unit;
            return new WishResult(
                card.saveCardData.id,
                card.saveCardData.StarLevel,
                card.saveCardData.cardType,
                isUnit
                    ? _wishManager.GetUnitConfig().GetUnitData(card.saveCardData.id.AsUnitName())?.GetCard(0)
                    : _wishManager.GetItemConfig().GetItemData(card.saveCardData.id.AsItemName())?.GetIcon(0),
                false
            );
        }

        /// <summary>
        /// 重置状态
        /// </summary>
        public void Reset()
        {
            CurrentState = State.Idle;
            _shotsCompleted = 0;
            _totalShots = 0;
        }
    }
}
