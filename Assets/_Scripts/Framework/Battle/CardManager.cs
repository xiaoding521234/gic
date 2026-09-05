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
        public const int MaxDeckCount = 30;      // 卡组数量上限（2026-09-05 v2：动态卡组）
        public const int MinDeckCount = 1;       // 至少保留一个卡组
        public const int DefaultDeckCount = 7;   // 新档初始卡组数（EnsureDecksValid 空表补齐用）
        public const int MaxDeckSize = 8;
        public const int MaxDeckNameLength = 10; // 卡组名上限（字符数；2026-09-06 拍板）

        private readonly SaveManager saveManager;
        public readonly ItemConfig itemConfig;
        public readonly UnitConfig unitConfig;

        public CardManager(SaveManager saveManager, ItemConfig itemConfig, UnitConfig unitConfig)
        {
            this.saveManager = saveManager;
            this.itemConfig = itemConfig;
            this.unitConfig = unitConfig;
        }

        private CardDeck[] _decks;

        /// <summary>
        /// 卡组缓存数组，下标=deckId（稠密恒等：deckId 0..N-1 与 deckNames 下标一一对应）。
        /// 自愈式访问：长度与存档 deckNames.Count 不符时自动重建——存档加载时序无关，增删卡组后自动跟随。
        /// </summary>
        public CardDeck[] decks
        {
            get { EnsureDecksFresh(); return _decks; }
        }

        /// <summary>当前卡组总数</summary>
        public int DeckCount => decks.Length;

        [PostConstruct]
        public void Init()
        {
            EnsureDecksFresh();
        }

        public void Start() { }

        public void Update(float deltaTime) { }

        /// <summary>decks 数组与存档 deckNames.Count 对齐（不匹配则整体重建缓存；CardDeck 无状态可随时重建）</summary>
        private void EnsureDecksFresh()
        {
            int n = saveManager?.CurrentSave?.progress?.deckNames?.Count ?? 0;
            if (n < MinDeckCount) n = MinDeckCount; // 存档未就绪时的兜底（首次真实访问在存档加载后会再对齐）

            if (_decks == null || _decks.Length != n)
            {
                _decks = new CardDeck[n];
                for (int i = 0; i < n; i++)
                    _decks[i] = new CardDeck(i, saveManager);
            }
        }

        /// <summary>增删卡组后强制重建缓存数组</summary>
        private void RebuildDecksArray()
        {
            _decks = null;
            EnsureDecksFresh();
        }

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
            var current = decks;
            if (deckIndex < 0 || deckIndex >= current.Length) return;
            current[deckIndex].Invalidate();
        }

        /// <summary>
        /// 卡牌数据变动时通知受影响的卡组失效
        /// </summary>
        public void OnCardChanged(SaveCardData card)
        {
            if (card.inDecks == null) return;
            var current = decks;
            foreach (var deckId in card.inDecks)
                if (deckId >= 0 && deckId < current.Length)
                    current[deckId].Invalidate();
        }

        // ==================== 卡组名称 / 排序 / 数量 / 内容操作（2026-09-05 卡组管理面板） ====================

        /// <summary>卡组自定义名（""=未命名，UI 显示本地化默认"卡组N"）；越界防御回退空串</summary>
        public string GetDeckName(int deckId)
        {
            var names = saveManager.CurrentSave.progress.deckNames;
            return (deckId >= 0 && deckId < names.Count) ? names[deckId] : "";
        }

        /// <summary>重命名卡组（统一变更入口自动标脏）；超长截断到 MaxDeckNameLength（输入框已限，此处为粘贴/外部写入口径防御）</summary>
        public void SetDeckName(int deckId, string name)
        {
            if (!string.IsNullOrEmpty(name) && name.Length > MaxDeckNameLength)
                name = name.Substring(0, MaxDeckNameLength);
            saveManager.Modify(s =>
            {
                var names = s.progress.deckNames;
                if (deckId >= 0 && deckId < names.Count) names[deckId] = name ?? "";
            });
        }

        /// <summary>显示顺序表（元素=deckId，下标=显示编号-1；拖拽排序只改此表，卡牌 inDecks 引用恒定）</summary>
        public IReadOnlyList<int> DeckOrder => saveManager.CurrentSave.progress.deckOrder;

        /// <summary>拖拽排序：把显示位 fromPos 的卡组移到 toPos</summary>
        public void MoveDeckOrder(int fromPos, int toPos)
        {
            saveManager.Modify(s =>
            {
                var order = s.progress.deckOrder;
                if (fromPos < 0 || fromPos >= order.Count || toPos < 0 || toPos >= order.Count || fromPos == toPos) return;
                int id = order[fromPos];
                order.RemoveAt(fromPos);
                order.Insert(toPos, id);
            });
        }

        /// <summary>
        /// 新增卡组：尾部追加（新 deckId=当前数量，显示位也在末尾），空名（显示"卡组N"）。
        /// 达到上限返回 -1。返回新卡组 deckId。
        /// </summary>
        public int AddDeck()
        {
            int newId = -1;
            saveManager.Modify(s =>
            {
                var p = s.progress;
                if (p.deckNames.Count >= MaxDeckCount) return;
                newId = p.deckNames.Count;
                p.deckNames.Add("");
                p.deckOrder.Add(newId);
            });
            if (newId >= 0) RebuildDecksArray();
            return newId;
        }

        /// <summary>
        /// 删除卡组：deckId 必须保持稠密恒等——所有卡牌 inDecks 中移除该 id、大于该 id 的一律减一；
        /// deckOrder 同步重映射后移除该项；currentDeck 被删时改为显示顺序中的后继卡组（环形回卷到首位）。
        /// 低于最低保留数量返回 false。
        /// </summary>
        public bool RemoveDeck(int deckId)
        {
            bool removed = false;
            saveManager.Modify(s =>
            {
                var p = s.progress;
                int n = p.deckNames.Count;
                if (deckId < 0 || deckId >= n || n <= MinDeckCount) return;
                removed = true;

                // 卡牌引用重映射：删 deckId，>deckId 的减一（deckId 稠密性保持）
                foreach (var card in s.ownedCards)
                {
                    if (card.inDecks == null) continue;
                    card.inDecks.RemoveAll(d => d == deckId);
                    for (int i = 0; i < card.inDecks.Count; i++)
                        if (card.inDecks[i] > deckId) card.inDecks[i]--;
                }

                // 后继卡组（旧 id）：显示顺序中下一个，环形回卷
                int pos = p.deckOrder.IndexOf(deckId);
                int succ = p.deckOrder[(pos + 1) % n];
                if (succ == deckId) succ = 0; // 理论不可达（n≥2），防御
                if (succ > deckId) succ--;

                // 排列表：先重映射 >deckId 的值，再移除 deckId 本身
                for (int i = 0; i < p.deckOrder.Count; i++)
                    if (p.deckOrder[i] > deckId) p.deckOrder[i]--;

                p.deckOrder.Remove(deckId);
                p.deckNames.RemoveAt(deckId);

                // 当前卡组修正
                if (p.currentDeck == deckId) p.currentDeck = succ;
                else if (p.currentDeck > deckId) p.currentDeck--;
            });
            if (removed) RebuildDecksArray();
            return removed;
        }

        /// <summary>卡组内容快照（复制/导出密语用）：名称 + 有序 CardId 列表</summary>
        public (string name, List<CardId> cards) CopyDeck(int deckId)
        {
            var save = saveManager.CurrentSave;
            var cards = new List<CardId>();
            var current = decks;
            if (deckId >= 0 && deckId < current.Length)
                foreach (var c in current[deckId].Cards)
                    cards.Add(c.id);
            var names = save.progress.deckNames;
            string name = (deckId >= 0 && deckId < names.Count) ? names[deckId] : "";
            return (name, cards);
        }

        /// <summary>
        /// 整卡组内容替换（粘贴/导入密语共用）：清空目标卡组的 inDecks 标记 + 覆盖名称，再按拥有卡匹配写入；
        /// 未拥有的卡跳过。返回跳过数量。变更后自动标脏 + 目标卡组重建。
        /// </summary>
        public int ApplyDeckContent(int deckId, string name, List<CardId> cards)
        {
            if (deckId < 0 || deckId >= DeckCount) return cards?.Count ?? 0;
            cards ??= new List<CardId>();
            if (cards.Count > MaxDeckSize) cards = cards.GetRange(0, MaxDeckSize);

            var owned = new Dictionary<CardId, SaveCardData>();
            foreach (var card in saveManager.CurrentSave.ownedCards)
                owned[card.id] = card;

            int missing = 0;
            saveManager.Modify(s =>
            {
                foreach (var card in s.ownedCards)
                    card.RemoveFromDeck(deckId);
                var names = s.progress.deckNames;
                if (deckId < names.Count) names[deckId] = name;
                foreach (var id in cards)
                {
                    if (owned.TryGetValue(id, out var data))
                        data.AddToDeck(deckId);
                    else
                        missing++;
                }
            });
            RebuildDeck(deckId);
            return missing;
        }

        /// <summary>导出卡组密语（名称+卡牌内容 → 分享码）</summary>
        public string ExportDeckCode(int deckId)
        {
            var (name, cards) = CopyDeck(deckId);
            return DeckCodeCodec.Encode(name, cards);
        }
    }

}
