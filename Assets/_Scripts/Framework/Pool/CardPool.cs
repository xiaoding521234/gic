using System.Collections.Generic;
using UnityEngine;
using GIC.Battle;
using GIC.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Tool;
namespace GIC.Framework
{


    /// <summary>
    /// 卡牌对象池 — 避免频繁 Instantiate/Destroy
    /// </summary>
    public class CardPool
    {
        private readonly GameObject _prefab;
        private readonly Transform _parent;
        private readonly Stack<Card> _pool = new();

        public CardPool(GameObject prefab, Transform parent)
        {
            _prefab = prefab;
            _parent = parent;
        }

        /// <summary>
        /// 从池中获取一张卡牌（无可用时实例化新的）
        /// </summary>
        public Card Get(SaveCardData data, CardDetailView detailView)
        {
            Card card;
            if (_pool.Count > 0)
            {
                card = _pool.Pop();
                // 移到最后，确保排列顺序正确
                card.transform.SetAsLastSibling();
                card.gameObject.SetActive(true);
            }
            else
            {
                GameObject obj = Object.Instantiate(_prefab, _parent);
                card = obj.GetComponent<Card>();
                if (card == null)
                {
                    Object.Destroy(obj);
                    return null;
                }
            }

            card.Init(data, detailView);
            return card;
        }

        /// <summary>
        /// 将卡牌归还池中
        /// </summary>
        public void Release(Card card)
        {
            if (card == null) return;
            if (card.toggle != null)
            {
                card.toggle.group = null;
                card.toggle.isOn = false;
            }
            card.gameObject.SetActive(false);
            card.transform.SetParent(_parent, false);
            _pool.Push(card);
        }

        /// <summary>
        /// 清空池（销毁所有缓存的 GameObject）
        /// </summary>
        public void Clear()
        {
            foreach (var card in _pool)
            {
                if (card != null) Object.Destroy(card.gameObject);
            }
            _pool.Clear();
        }
    }

}


