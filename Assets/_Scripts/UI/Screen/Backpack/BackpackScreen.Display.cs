// ==================== BackpackScreen.Display.cs ====================
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GIC.Framework;
using GIC.Battle;
using GIC.Data;
namespace GIC.UI
{


    public partial class BackpackScreen
    {
        private void RefreshCurrentDeckCache()
        {
            currentDeckId = saveManager.CurrentSave.progress.currentDeck;
            currentDeckCards.Clear();

            if (currentDeckId >= 0 && currentDeckId < cardManager.decks.Length)
            {
                foreach (var card in cardManager.decks[currentDeckId].Cards)
                    currentDeckCards.Add(card);
            }
        }

        private List<SaveCardData> BuildDisplayList()
        {
            var result = new List<SaveCardData>();
            var added = new HashSet<SaveCardData>();

            // 1. 卡组中的卡牌
            if (currentDeckId >= 0 && currentDeckId < cardManager.decks.Length)
            {
                foreach (var card in cardManager.decks[currentDeckId].Cards)
                    if (added.Add(card)) result.Add(card);
            }

            // 2. 当前分类下的已拥有卡牌
            foreach (var card in saveManager.CurrentSave.ownedCards)
            {
                if (!added.Add(card)) continue;
                if (card.Config?.GetBackpackTab() != currentTab) continue;
                if (IsHidden(card)) continue;
                result.Add(card);
            }

            return result;
        }

        private bool IsHidden(SaveCardData card)
        {
            if (card.id.cardType == CardType.Unit)
            {
                var unitData = unitConfig.GetUnitData(card.id.AsUnitName());
                return unitData?.hideInBackpack ?? false;
            }
            return false;
        }

        public void RefreshCardList()
        {
            RefreshCurrentDeckCache();
            ReleaseSpawnedCards();
            // 清组引用（2026-09-27 SelectButton 改版返修）：卡牌归还池时 Group 置空但组的 Current 仍持旧实例——
            // 池化复用下同实例再当第一张会命中 Select 的 Current==item no-op，初始选中彻底失效；释放列表后清 Current
            if (cardSelectionGroup != null) cardSelectionGroup.ClearSelection(false);
            if (_spawnCoroutine != null) StopCoroutine(_spawnCoroutine);
            _spawnCoroutine = StartCoroutine(SpawnCardsWithDelay(BuildDisplayList()));
        }

        private IEnumerator SpawnCardsWithDelay(List<SaveCardData> cardDataList)
        {
            bool isFirst = true;
            float interval = 0.012f;
            float elapsed = 0f;
            foreach (var data in cardDataList)
            {
                SpawnCard(data);
                if (isFirst && spawnedCards.Count > 0 && spawnedCards[0]?.selectButton != null)
                {
                    // 初始选中第一张卡（2026-09-27 返修：旧 Toggle 语义=选中+详情面板自动开——
                    // 开详情已迁 onClick 边沿，此处显式补调恢复等价行为）
                    spawnedCards[0].selectButton.SetSelected(true);
                    spawnedCards[0].OpenDetailView();
                    isFirst = false;
                }
                // 帧率无关的间隔等待
                elapsed = 0f;
                while (elapsed < interval)
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                }
            }

            if (isEditMode)
            {
                foreach (var card in spawnedCards)
                    if (card != null) card.EnterEditMode();
            }
        }

        private void SpawnCard(SaveCardData data)
        {
            Card card = _cardPool.Get(data, cardDetailView);

            if (card == null) return;

            card.onDeck?.gameObject.SetActive(currentDeckCards.Contains(data));
            if (card.selectButton != null)
            {
                card.selectButton.SetSelected(false);
                card.selectButton.Group = cardSelectionGroup;
            }
            card.SetViewType(ViewType.Display);
            spawnedCards.Add(card);
        }

        private void ReleaseSpawnedCards()
        {
            foreach (var c in spawnedCards)
            {
                if (c != null)
                {
                    c.ExitEditDeck(); // 重置 overlay 和 isEditMode
                    _cardPool.Release(c);
                }
            }
            spawnedCards.Clear();
        }
    }

}


