using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
using GIC.Battle;

namespace GIC.UI
{
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

        /// <summary>每获取 N 个星辉触发 1 次相遇之线</summary>
        public const int EncounterThreshold = 20;

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

        // ── 相遇之线 ──

        /// <summary>累计获取的星辉总量（只增不减，与可消费的星辉余额解耦）</summary>
        public int GetStarglitterEarned() => _saveManager.CurrentSave.starglitterEarned;

        /// <summary>待用的相遇之线次数 = 累计获取量/20 - 已用量</summary>
        public int GetEncounterCharges() => GetStarglitterEarned() / EncounterThreshold - _saveManager.CurrentSave.encounterUsed;

        /// <summary>当前进度条比例 (0~1) = (累计获取量%20) / 20</summary>
        public float GetStarglitterProgress() => (GetStarglitterEarned() % EncounterThreshold) / (float)EncounterThreshold;

        /// <summary>是否有可用的相遇之线</summary>
        public bool IsEncounterReady() => GetEncounterCharges() > 0;

        /// <summary>消耗 1 次相遇之线（不扣星辉，只增加 encounterUsed）</summary>
        public bool ConsumeEncounter()
        {
            if (GetEncounterCharges() <= 0) return false;
            _saveManager.CurrentSave.encounterUsed++;
            return true;
        }

        /// <summary>返还 1 次相遇之线（5★卡无法提升时退回）</summary>
        public void RefundEncounter()
        {
            if (_saveManager.CurrentSave.encounterUsed > 0)
                _saveManager.CurrentSave.encounterUsed--;
        }

        /// <summary>
        /// 相遇之线升级次数：使用 WishPoolConfig 的独立升级权重表
        /// </summary>
        public int RollUpgradeCount(WishPoolConfig pool)
        {
            return pool.RollUpgradeCount();
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
        /// 统一存盘（由 WishDrawController 在整个抽卡流程结束后调用一次）
        /// </summary>
        public void SaveGame()
        {
            _saveManager.SaveGame();
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
        /// 消耗原石
        /// </summary>
        public bool ConsumePrimogem(int count)
        {
            int cost = SingleWishCost * count;
            var save = _saveManager.CurrentSave;
            foreach (var card in save.ownedNormalItems)
            {
                if (card.id.AsItemName() == ItemName.Primogem)
                {
                    if (card.count < cost) return false;
                    card.count -= cost;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 获取当前原石数量
        /// </summary>
        public int GetPrimogemCount()
        {
            var save = _saveManager.CurrentSave;
            foreach (var card in save.ownedNormalItems)
            {
                if (card.id.AsItemName() == ItemName.Primogem)
                    return card.count;
            }
            return 0;
        }

        /// <summary>
        /// 添加卡牌到存档。重复角色卡不增加数量，转为星辉（当 awardStarglitter=true 时）。
        /// </summary>
        /// <param name="awardStarglitter">是否在发现重复角色卡时发放星辉。普通射击=true；相遇之线最终卡=false（已在展示时发放）。</param>
        private void AddCardToInventory(CardId cardId, bool isUnit, int starLevel, out int starglitter, bool awardStarglitter)
        {
            starglitter = 0;
            var save = _saveManager.CurrentSave;
            var list = isUnit ? save.ownedUnits : save.ownedNormalItems;

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
                        card.count += addCount;
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
        /// 向存档中添加星辉（同时累加 starglitterEarned 用于相遇之线计数）
        /// </summary>
        private void AddStarglitter(PlayerSaveData save, int amount)
        {
            save.starglitterEarned += amount;

            foreach (var card in save.ownedNormalItems)
            {
                if (card.id.AsItemName() == ItemName.Starglitter)
                {
                    card.count += amount;
                    return;
                }
            }
            // 星辉不在存档中（初始为0未创建），新建
            var newCard = new SaveCardData();
            newCard.SaveItem(ItemName.Starglitter, amount);
            save.AddOwnedItem(newCard);
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
            foreach (var card in save.ownedUnits)
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
