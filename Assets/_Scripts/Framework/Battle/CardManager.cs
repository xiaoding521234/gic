using System.Collections.Generic;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Framework
{


    [Component]
    public class CardManager : IWargameManager
    {
        private const int DeckCount = 7;

        private readonly SaveManager saveManager;
        public readonly ItemConfig itemConfig;
        public readonly UnitConfig unitConfig;

        public CardManager(SaveManager saveManager, ItemConfig itemConfig, UnitConfig unitConfig)
        {
            this.saveManager = saveManager;
            this.itemConfig = itemConfig;
            this.unitConfig = unitConfig;
        }

        public CardDeck[] decks;

        [PostConstruct]
        public void Init()
        {
            decks = new CardDeck[DeckCount];
            for (int i = 0; i < DeckCount; i++)
                decks[i] = new CardDeck(i, saveManager);

            BuildAllDecks();
        }

        public void Start() { }

        public void Update(float deltaTime) { }

        /// <summary>
        /// 全量重建所有卡组
        /// </summary>
        public void BuildAllDecks()
        {
            foreach (var deck in decks)
                deck.Invalidate();
        }

        /// <summary>
        /// 增量更新单个卡组
        /// </summary>
        public void RebuildDeck(int deckIndex)
        {
            if (deckIndex < 0 || deckIndex >= DeckCount) return;
            decks[deckIndex].Invalidate();
        }

        /// <summary>
        /// 卡牌数据变动时通知受影响的卡组失效
        /// </summary>
        public void OnCardChanged(SaveCardData card)
        {
            if (card.inDecks == null) return;
            foreach (var deckId in card.inDecks)
                if (deckId >= 0 && deckId < DeckCount)
                    decks[deckId].Invalidate();
        }
    }

}


