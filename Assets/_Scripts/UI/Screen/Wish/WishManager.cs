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

        public WishResult(CardId id, int star, CardType type, Sprite spr, bool isNew)
        {
            cardId = id;
            starLevel = star;
            cardType = type;
            sprite = spr;
            this.isNew = isNew;
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
        /// </summary>
        public void AddResultToInventory(WishResult result)
        {
            AddCardToInventory(result.cardId, result.cardType == CardType.Unit);
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

        private void AddCardToInventory(CardId cardId, bool isUnit)
        {
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
                    card.count += addCount;
                    save.RebuildOwnedCards();
                    return;
                }
            }

            // 新卡
            var newCard = new SaveCardData();
            if (isUnit)
                newCard.SaveUnit(cardId.AsUnitName(), 1);
            else
                newCard.SaveItem(cardId.AsItemName(), addCount);
            list.Add(newCard);
            save.RebuildOwnedCards();
        }
    }
}
