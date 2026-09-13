using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
using GIC.Battle;

namespace GIC.UI
{
    /// <summary>
    /// 祈愿射击线类型 — 由本发消耗的命运之缘物品决定
    /// </summary>
    public enum WishLineType
    {
        /// <summary>命运之线（白）——未消耗命运之缘的普通射击</summary>
        Normal = 0,
        /// <summary>相遇之线（金）——消耗 1 个相遇之缘</summary>
        Encounter,
        /// <summary>纠缠之线（粉）——消耗 1 个纠缠之缘（优先于相遇之缘）</summary>
        Intertwined,
    }

    /// <summary>
    /// 祈愿单次结果
    /// </summary>
    [Serializable]
    public struct WishResult
    {
        public CardId cardId;
        public int starLevel;
        public CardType cardType;
        public Sprite sprite;
        public bool isNew;
        /// <summary>重复角色卡转换为星辉的数量（0=非重复或物品）</summary>
        public int starglitterAmount;

        public WishResult(CardId id, int star, CardType type, Sprite spr, bool isNew, int starglitter = 0)
        {
            cardId = id;
            starLevel = star;
            cardType = type;
            sprite = spr;
            this.isNew = isNew;
            starglitterAmount = starglitter;
        }
    }

    /// <summary>
    /// 祈愿管理器 — 负责抽卡逻辑、货币消耗、写入存档
    /// </summary>
    public class WishManager
    {
        private readonly SaveManager _saveManager;
        private readonly UnitConfig _unitConfig;
        private readonly ItemConfig _itemConfig;

        public WishManager(SaveManager saveManager, UnitConfig unitConfig, ItemConfig itemConfig)
        {
            _saveManager = saveManager;
            _unitConfig = unitConfig;
            _itemConfig = itemConfig;
        }

        public UnitConfig GetUnitConfig() => _unitConfig;
        public ItemConfig GetItemConfig() => _itemConfig;

        /// <summary>
        /// 将一张祈愿结果写入存档（仅修改内存数据，不立即存盘）
        /// 普通射击路径：入账时判断重复，重复角色卡转星辉
        /// </summary>
        /// <param name="starglitter">重复角色卡转换的星辉数量（0=非重复或物品）</param>
        public void AddResultToInventory(WishResult result, out int starglitter)
        {
            AddCardToInventory(result.cardId, result.cardType == CardType.Unit, result.starLevel, out starglitter, awardStarglitter: true);
        }

        /// <summary>
        /// 将最终卡写入存档，但不再判重发星辉。
        /// 相遇之线流程中，每张展示卡（含最终卡）的星辉已在展示阶段通过 AwardDuplicateStarglitter 发放，
        /// 这里只负责把最终卡加入背包（新建 count / 堆叠物品），避免重复发放。
        /// </summary>
        public void AddFinalResultToInventory(WishResult result)
        {
            AddCardToInventory(result.cardId, result.cardType == CardType.Unit, result.starLevel, out _, awardStarglitter: false);
        }

        /// <summary>
        /// 重复角色卡→星辉转换表
        /// </summary>
        public static int GetStarglitterByStar(int starLevel)
        {
            return starLevel switch
            {
                5 => 50,
                4 => 25,
                3 => 15,
                2 => 8,
                1 => 3,
                _ => 0
            };
        }

        // ── 命运之缘（2026-09-06 物品化改版：相遇之缘/纠缠之缘为背包物品，射击自动消耗） ──

        /// <summary>累计获取的星辉总量（只增不减，与可消费的星辉余额解耦）——命运之缘里程碑的计数源</summary>
        public int GetStarglitterEarned() => _saveManager.CurrentSave.progress.starglitterEarned;

        /// <summary>当前进度条比例 (0~1) = (累计获取量 % 20) / 20 —— 距下一个相遇之缘的进度</summary>
        public float GetStarglitterProgress() =>
            (GetStarglitterEarned() % SaveProgress.AcquaintFateThreshold) / (float)SaveProgress.AcquaintFateThreshold;

        /// <summary>下一发将射出的线：背包有纠缠之缘→纠缠之线（优先消耗）；否则有相遇之缘→相遇之线；都没有→命运之线</summary>
        public WishLineType GetUpcomingLineType()
        {
            var save = _saveManager.CurrentSave;
            if (save.GetItemCount(ItemName.IntertwinedFate) > 0) return WishLineType.Intertwined;
            if (save.GetItemCount(ItemName.AcquaintFate) > 0) return WishLineType.Encounter;
            return WishLineType.Normal;
        }

        /// <summary>消耗 1 个命运之缘物品（纠缠之缘优先于相遇之缘）；返回消耗的线类型（Normal=背包无命运之缘可耗）</summary>
        public WishLineType ConsumeFateForShot()
        {
            var save = _saveManager.CurrentSave;
            if (save.TryConsumeItem(ItemName.IntertwinedFate, 1)) return WishLineType.Intertwined;
            if (save.TryConsumeItem(ItemName.AcquaintFate, 1)) return WishLineType.Encounter;
            return WishLineType.Normal;
        }

        /// <summary>返还 1 个命运之缘物品（射中 5★ 卡无法提升时，退回本发消耗的缘）</summary>
        public void RefundFate(WishLineType type)
        {
            if (type == WishLineType.Intertwined)
                _saveManager.CurrentSave.AddItemCount(ItemName.IntertwinedFate, 1);
            else if (type == WishLineType.Encounter)
                _saveManager.CurrentSave.AddItemCount(ItemName.AcquaintFate, 1);
        }

        /// <summary>
        /// 升级次数 Roll：相遇之线用 0-4 档权重表，纠缠之线用必升 1-4 档权重表（2026-09-06 拍板差异化）
        /// </summary>
        public int RollUpgradeCount(WishPoolConfig pool, WishLineType lineType)
        {
            return lineType == WishLineType.Intertwined
                ? pool.RollIntertwinedUpgradeCount()
                : pool.RollUpgradeCount();
        }

        /// <summary>
        /// 从指定星级+类型随机选一张卡，返回新 WishResult
        /// </summary>
        public WishResult RollCardByStar(WishPoolConfig pool, int starLevel, bool isUnit)
        {
            CardId cardId;
            Sprite sprite = null;

            if (isUnit)
            {
                var candidates = pool.GetUnitsByStar(_unitConfig, starLevel);
                if (candidates.Count == 0) return default;
                var name = candidates[UnityEngine.Random.Range(0, candidates.Count)];
                cardId = new CardId(name);
                sprite = _unitConfig.GetUnitData(name)?.GetCard(0);
                return new WishResult(cardId, starLevel, CardType.Unit, sprite, false);
            }
            else
            {
                var candidates = pool.GetItemsByStar(_itemConfig, starLevel);
                if (candidates.Count == 0) return default;
                var name = candidates[UnityEngine.Random.Range(0, candidates.Count)];
                cardId = new CardId(name);
                sprite = _itemConfig.GetItemData(name)?.GetIcon(0);
                return new WishResult(cardId, starLevel, CardType.Item, sprite, false);
            }
        }

        /// <summary>
        /// 统一存盘（由 WishDrawController 在整个抽卡流程结束后调用一次）——祈愿=货币关键路径，立即写盘不走延迟窗
        /// </summary>
        public void SaveGame()
        {
            _saveManager.SaveGameNow();
        }

        /// <summary>
        /// 单次祈愿消耗原石（160 原石/次）
        /// </summary>
        public const int SingleWishCost = 160;

        /// <summary>
        /// 检查是否有足够的原石
        /// </summary>
        public bool CanAfford(int count)
        {
            return GetPrimogemCount() >= SingleWishCost * count;
        }

        /// <summary>
        /// 消耗原石（数量不足返回 false）
        /// </summary>
        public bool ConsumePrimogem(int count)
        {
            return _saveManager.CurrentSave.TryConsumeItem(ItemName.Primogem, SingleWishCost * count);
        }

        /// <summary>
        /// 获取当前原石数量
        /// </summary>
        public int GetPrimogemCount()
        {
            return _saveManager.CurrentSave.GetItemCount(ItemName.Primogem);
        }

        /// <summary>
        /// 添加卡牌到存档。重复角色卡不增加数量，转为星辉（当 awardStarglitter=true 时）。
        /// </summary>
        /// <param name="awardStarglitter">是否在发现重复角色卡时发放星辉。普通射击=true；相遇之线最终卡=false（已在展示时发放）。</param>
        private void AddCardToInventory(CardId cardId, bool isUnit, int starLevel, out int starglitter, bool awardStarglitter)
        {
            starglitter = 0;
            var save = _saveManager.CurrentSave;
            var list = isUnit ? save.progress.ownedUnits : save.progress.ownedNormalItems;

            // 物品按 countPerServing 给数量，角色固定 1 张
            int addCount = 1;
            if (!isUnit)
            {
                var itemData = _itemConfig.GetItemData(cardId.AsItemName());
                if (itemData != null)
                    addCount = itemData.countPerServing;
            }

            foreach (var card in list)
            {
                if (card.id == cardId)
                {
                    if (isUnit)
                    {
                        if (card.count > 0)
                        {
                            // 已拥有（count>0）：重复角色卡
                            if (awardStarglitter)
                            {
                                starglitter = GetStarglitterByStar(starLevel);
                                AddStarglitter(save, starglitter);
                            }
                            // awardStarglitter=false 时星辉已在展示阶段发放，这里不重复
                        }
                        else
                        {
                            // count=0：重新获得
                            card.count = 1;
                        }
                    }
                    else
                    {
                        // 统一入口：内部按 ItemConfig.maxStack 钳制持有上限（2026-09-13 启用）
                        save.AddItemCount(cardId.AsItemName(), addCount);
                    }
                    save.RebuildOwnedCards();
                    return;
                }
            }

            // 新卡（走统一入口，自动失效 ownedCards 缓存）
            var newCard = new SaveCardData();
            if (isUnit)
            {
                newCard.SaveUnit(cardId.AsUnitName(), 1);
                save.AddOwnedUnit(newCard);
            }
            else
            {
                newCard.SaveItem(cardId.AsItemName(), addCount);
                save.AddOwnedItem(newCard);
            }
        }

        /// <summary>
        /// 向存档中添加星辉：累加余额与累计获取量，并结算命运之缘里程碑——
        /// 每满 20 累计星辉赠 1 个相遇之缘、每满 200 累计星辉赠 1 个纠缠之缘（2026-09-06 改版）。
        /// starglitterEarned 只增不减，按跨越档位的差值发放即可精确记账，无需额外 granted 计数
        /// （v11 旧档的一次性补偿见 PlayerSaveData.MigrateFateItems）。
        /// </summary>
        private void AddStarglitter(PlayerSaveData save, int amount)
        {
            int before = save.progress.starglitterEarned;
            save.progress.starglitterEarned += amount;
            save.AddItemCount(ItemName.Starglitter, amount);

            int acquaint = save.progress.starglitterEarned / SaveProgress.AcquaintFateThreshold
                         - before / SaveProgress.AcquaintFateThreshold;
            if (acquaint > 0)
                save.AddItemCount(ItemName.AcquaintFate, acquaint);

            int intertwined = save.progress.starglitterEarned / SaveProgress.IntertwinedFateThreshold
                            - before / SaveProgress.IntertwinedFateThreshold;
            if (intertwined > 0)
                save.AddItemCount(ItemName.IntertwinedFate, intertwined);
        }

        /// <summary>
        /// 判重复并发放星辉（不写入存档卡片列表）。
        /// 相遇之线每张展示卡（原始卡+中间卡+最终卡）都调用一次。
        /// </summary>
        /// <returns>发放的星辉数量（0=非重复或物品）</returns>
        public int AwardDuplicateStarglitter(WishResult result)
        {
            if (result.cardType != CardType.Unit) return 0;

            var save = _saveManager.CurrentSave;
            foreach (var card in save.progress.ownedUnits)
            {
                if (card.id == result.cardId && card.count > 0)
                {
                    int starglitter = GetStarglitterByStar(result.starLevel);
                    AddStarglitter(save, starglitter);
                    return starglitter;
                }
            }
            return 0;
        }
    }
}
