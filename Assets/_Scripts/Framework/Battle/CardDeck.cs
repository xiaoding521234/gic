using System.Collections.Generic;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Framework
{


    /// <summary>
    /// 单个卡组 — 持有该卡组内所有卡牌的视图，支持懒加载重建。
    /// 原 CardManager 中直接操作 decks[i] List 的所有逻辑上移到这里。
    /// </summary>
    public class CardDeck
    {
        private readonly int _deckId;
        private readonly SaveManager _saveManager;
        private  List<SaveCardData> _cards = new();
        private bool _dirty = true;

        public int DeckId => _deckId;
        public int Count  { get { EnsureFresh(); return _cards.Count; } }

        public CardDeck(int deckId, SaveManager saveManager)
        {
            _deckId = deckId;
            _saveManager = saveManager;
        }

        /// <summary>获取卡牌列表（自动重建）</summary>
        public IReadOnlyList<SaveCardData> Cards
        {
            get { EnsureFresh(); return _cards; }
        }

        /// <summary>标记为脏，下次访问时重建</summary>
        public void Invalidate() => _dirty = true;

        /// <summary>追加一张卡牌</summary>
        public void Add(SaveCardData card)
        {
            EnsureFresh();
            _cards.Add(card);
        }

        /// <summary>清空</summary>
        public void Clear()
        {
            _cards.Clear();
            _dirty = false;
        }

        /// <summary>排序：角色卡在前，物品卡在后；同类型内按 SortOrder + 星级</summary>
        public void Sort()
        {
            _cards.Sort((a, b) =>
            {
                // 先按卡牌类型：角色卡在前，物品卡在后（Unit=1 < Item=0 反转）
                int typeCmp = b.id.cardType.CompareTo(a.id.cardType);
                if (typeCmp != 0) return typeCmp;

                // 同类型内按 SortOrder
                int cmp = a.SortOrder.CompareTo(b.SortOrder);
                if (cmp != 0) return cmp;

                return b.StarLevel.CompareTo(a.StarLevel); // 星级从高到低
            });
        }

        private void EnsureFresh()
        {
            if (_dirty)
            {
                Rebuild();
                _dirty = false;
            }
        }

        /// <summary>从 SaveManager 全量重建</summary>
        private void Rebuild()
        {
            _cards.Clear();
            var save = _saveManager?.CurrentSave;
            if (save == null) return;

            // 收集所有卡牌中属于本卡组的
            var all = new List<SaveCardData>();
            all.AddRange(save.progress.ownedUnits);
            all.AddRange(save.progress.ownedNormalItems);

            foreach (var card in all)
            {
                if (card.inDecks != null && card.inDecks.Contains(_deckId))
                    _cards.Add(card);
            }

            Sort();
        }
    }

}

