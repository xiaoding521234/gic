using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Battle;

namespace GIC.UI
{
    /// <summary>
    /// 卡道生成 — 出卡、随机卡、卡片对象池、星级缓存
    /// </summary>
    public partial class WishDrawController
    {
        private IEnumerator SpawnCardsCoroutine()
        {
            while (_flow != null && _flow.CurrentState != WishFlowController.State.Idle &&
                   _flow.CurrentState != WishFlowController.State.Finished)
            {
                SpawnTrackCard();
                yield return Wait.Seconds(cardSpawnInterval);
            }
        }

        /// <summary>
        /// 预构建星级→候选列表缓存（在 StartWish 中从 _flow 获取，此方法保留兼容）
        /// </summary>
        private void BuildStarLevelCache()
        {
            if (_flow == null) return;
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

        private void SpawnTrackCard(float initialProgress = 0f)
        {
            GameObject cardObj = GetPooledCard();
            cardObj.SetActive(true);
            cardObj.transform.SetParent(cardTrack, false);
            cardObj.transform.SetAsFirstSibling();

            var trackResult = DrawRandomTrackCard();
            InitDisplayCard(cardObj, trackResult);

            var trackCard = cardObj.GetComponent<WishTrackCard>();
            if (trackCard == null)
                trackCard = cardObj.AddComponent<WishTrackCard>();

            trackCard.Init(cardSpawnPoint.anchoredPosition, cardEndPoint.anchoredPosition, cardMoveDuration, initialProgress);

            _activeCards.Add(trackCard);
        }

        /// <summary>
        /// 随机抽取一张卡道展示用结果（仅供卡道动画，不写入存档）
        /// </summary>
        private WishResult DrawRandomTrackCard()
        {
            int starLevel = _pool.RollStarLevel();
            bool isUnit = _pool.RollIsUnit();

            CardId cardId;
            Sprite sprite = null;

            if (isUnit)
            {
                var candidates = GetCachedUnitsByStar(ref starLevel);
                if (candidates.Count > 0)
                {
                    var unitName = candidates[Random.Range(0, candidates.Count)];
                    cardId = new CardId(unitName);
                    sprite = _wishManager.GetUnitConfig().GetUnitData(unitName)?.GetCard(0);
                    return new WishResult(cardId, starLevel, CardType.Unit, sprite, false);
                }
                isUnit = false;
            }

            // 物品卡
            {
                var itemCandidates = GetCachedItemsByStar(ref starLevel);
                if (itemCandidates.Count == 0)
                {
                    cardId = new CardId(ItemName.Mora);
                    var itemData = _wishManager.GetItemConfig().GetItemData(ItemName.Mora);
                    sprite = itemData?.GetIcon(0);
                    starLevel = itemData?.starLevel ?? 1;
                }
                else
                {
                    var itemName = itemCandidates[Random.Range(0, itemCandidates.Count)];
                    cardId = new CardId(itemName);
                    var itemData = _wishManager.GetItemConfig().GetItemData(itemName);
                    sprite = itemData?.GetIcon(0);
                    starLevel = itemData?.starLevel ?? starLevel;
                }
                return new WishResult(cardId, starLevel, CardType.Item, sprite, false);
            }
        }

        private List<UnitName> GetCachedUnitsByStar(ref int starLevel)
        {
            var candidates = _unitsByStar[starLevel];
            if (candidates.Count > 0) return candidates;
            for (int s = starLevel - 1; s >= 1; s--)
            {
                candidates = _unitsByStar[s];
                if (candidates.Count > 0) { starLevel = s; return candidates; }
            }
            return candidates;
        }

        private List<ItemName> GetCachedItemsByStar(ref int starLevel)
        {
            var candidates = _itemsByStar[starLevel];
            if (candidates.Count > 0) return candidates;
            for (int s = starLevel - 1; s >= 1; s--)
            {
                candidates = _itemsByStar[s];
                if (candidates.Count > 0) { starLevel = s; return candidates; }
            }
            return candidates;
        }

        /// <summary>
        /// 找到卡道上最接近中心的卡牌
        /// </summary>
        private WishTrackCard FindCardNearestCenter()
        {
            WishTrackCard nearest = null;
            float minDist = float.MaxValue;

            foreach (var card in _activeCards)
            {
                if (card == null || !card.gameObject.activeInHierarchy) continue;
                if (card.transform.parent != cardTrack) continue;
                var rect = card.GetComponent<RectTransform>();
                if (rect == null) continue;

                float dist = Mathf.Abs(rect.anchoredPosition.y);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = card;
                }
            }

            return nearest;
        }

        #region cardPool

        private GameObject GetPooledCard()
        {
            foreach (var card in _cardPool)
            {
                if (!card.activeInHierarchy)
                    return card;
            }

            var obj = CreateCardGameObject();
            _cardPool.Add(obj);
            obj.SetActive(false);
            return obj;
        }

        private GameObject CreateCardGameObject()
        {
            var obj = Instantiate(cardPrefab, cardTrack, false);
            obj.layer = cardTrack.gameObject.layer;
            obj.name = "WishCard";
            var rect = obj.GetComponent<RectTransform>();
            rect.localScale = Vector3.one * (cardWidth / rect.sizeDelta.x);

            var card = obj.GetComponent<Card>();
            if (card != null)
                card.SetViewType(ViewType.OnlyDisplay);

            return obj;
        }

        /// <summary>
        /// 用祈愿结果初始化展示卡牌
        /// </summary>
        private void InitDisplayCard(GameObject cardObj, WishResult result)
        {
            var card = cardObj.GetComponent<Card>();
            if (card == null) return;

            int count = 1;
            if (result.cardType == CardType.Item && _wishManager != null)
            {
                var itemData = _wishManager.GetItemConfig().GetItemData(result.cardId.AsItemName());
                if (itemData != null)
                    count = itemData.countPerServing;
            }

            var saveData = new SaveCardData
            {
                id = result.cardId,
                count = count,
                skin = 0,
            };

            card.Init(saveData, null);
        }

        #endregion
    }
}
