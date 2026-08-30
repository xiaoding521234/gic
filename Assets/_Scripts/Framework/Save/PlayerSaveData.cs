using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Framework
{


    /// <summary>
    /// 玩家存档数据
    /// </summary>
    [Serializable]
    public class PlayerSaveData
    {
        // 基础信息
        public int saveVersion = 1;  // 存档版本号
        public string playerName = "旅行者";

        // 卡片相关
        public List<SaveCardData> ownedUnits = new List<SaveCardData>();        // 已拥有的角色
        public List<SaveCardData> ownedNormalItems = new List<SaveCardData>();  // 已拥有的物品
        public int currentDeck = 0;

        /// <summary>
        /// 统一拥有卡牌列表（运行时合并 ownedUnits + ownedNormalItems）
        /// </summary>
        [NonSerialized] private List<SaveCardData> _ownedCards;
        public List<SaveCardData> ownedCards
        {
            get
            {
                if (_ownedCards == null)
                {
                    _ownedCards = new List<SaveCardData>();
                    _ownedCards.AddRange(ownedUnits);
                    _ownedCards.AddRange(ownedNormalItems);
                }
                return _ownedCards;
            }
        }

        /// <summary>
        /// 重建 ownedCards 缓存（添加新卡牌后调用）
        /// </summary>
        public void RebuildOwnedCards()
        {
            _ownedCards = null;
        }

        /// <summary>添加已拥有角色 —— 外部改库存的统一入口，自动失效 ownedCards 缓存</summary>
        public void AddOwnedUnit(SaveCardData card)
        {
            ownedUnits.Add(card);
            RebuildOwnedCards();
        }

        /// <summary>添加已拥有物品 —— 外部改库存的统一入口，自动失效 ownedCards 缓存</summary>
        public void AddOwnedItem(SaveCardData card)
        {
            ownedNormalItems.Add(card);
            RebuildOwnedCards();
        }

        // ========== 物品数量查询/变更（货币等，统一入口） ==========

        /// <summary>查询物品持有数量（不在存档中返回 0）</summary>
        public int GetItemCount(ItemName item)
        {
            foreach (var card in ownedNormalItems)
            {
                if (card.id.AsItemName() == item)
                    return card.count;
            }
            return 0;
        }

        /// <summary>
        /// 增加物品数量（不存在则新建卡片），自动失效 ownedCards 缓存。
        /// 原石/星辉等货币变动的统一入口。
        /// </summary>
        public void AddItemCount(ItemName item, int amount)
        {
            foreach (var card in ownedNormalItems)
            {
                if (card.id.AsItemName() == item)
                {
                    card.count += amount;
                    return;
                }
            }
            var newCard = new SaveCardData();
            newCard.SaveItem(item, amount);
            AddOwnedItem(newCard);
        }

        /// <summary>尝试消耗物品数量（数量不足返回 false 且不改动）</summary>
        public bool TryConsumeItem(ItemName item, int count)
        {
            foreach (var card in ownedNormalItems)
            {
                if (card.id.AsItemName() == item)
                {
                    if (card.count < count) return false;
                    card.count -= count;
                    return true;
                }
            }
            return false;
        }


        // ========== 音量设置 ==========
        [Range(0f, 1f)]
        public float masterVolume = 1.0f;      // 总音量

        [Range(0f, 1f)]
        public float bgmVolume = 0.4f;         // 音乐音量

        [Range(0f, 1f)]
        public float sfxVolume = 0.8f;         // 音效音量

        [Range(0f, 1f)]
        public float voiceVolume = 0.8f;       // 语音音量

        // ========== 显示设置 ==========
        public int languageIndex = 0;           // 语言索引
        public int resolutionIndex = 0;         // 0=全屏（当前桌面分辨率）, 1=3840x2160, 2=2560x1440, 3=1920x1080, 4=1280x720
        public int frameRate = 165;              // 帧率

        // ========== 桌宠设置 ==========
        /// <summary>游戏退出时是否连带关闭派蒙（true=随游戏退出，false=独立存活；仅桌面形态有意义）</summary>
        public bool closePetOnExit = true;

        /// <summary>派蒙形态（0=桌面版，1=游戏画面内版；docs/19 §6.4）。JsonUtility 对缺失字段反序列化为默认值 0
        /// ——老档升级语义=桌面版，恰与"桌面版一直是唯一形态"的历史一致，无需版本迁移。安卓上 0 由读取侧钳为 1（Win32 不适用）。</summary>
        public int petForm = 0;

        /// <summary>派蒙对话 API Key 密文（AES-128-CBC+设备指纹派生密钥，Base64(iv+ct)；空串=未设置。
        /// 2026-08-28 用户拍板：玩家自输自己的 key，存档加密存储——明文永不落盘。
        /// 加解密/脱敏一律走 PetApiKeyCrypto，勿直接读此字段。</summary>
        public string petApiKeyCipher = "";

        /// <summary>派蒙对话供应商（PetChatProviders 表下标，0=DeepSeek；2026-08-29 多供应商支持）。
        /// JsonUtility 对缺失字段反序列化为默认值 0——老档升级语义=DeepSeek，与历史唯一供应商一致，无需迁移。
        /// 同步双通道写：本字段（主存档，设置界面回显）+ pet.json chatProvider（桌面进程读取通道）。</summary>
        public int petChatProvider = 0;

        // 锚点位置信息
        public int currentPosition = (int)PositionName.SnezhnayaCastle;

        // ========== 按键绑定 ==========
        public List<KeyBindingEntry> keyBindings = new List<KeyBindingEntry>();

        // ========== 相遇之线 ==========
        /// <summary>累计获取的星辉总量（只增不减，与可消费的星辉余额解耦）</summary>
        public int starglitterEarned = 0;
        /// <summary>已使用的相遇之线次数</summary>
        public int encounterUsed = 0;


        /// <summary>
        /// 初始化默认数据
        /// </summary>
        public void InitDefault()
        {
            playerName = "旅行者";

            InitCards();

            masterVolume = 1.0f;
            bgmVolume = 0.4f;
            sfxVolume = 0.8f;
            voiceVolume = 0.8f;

            languageIndex = 0;
            resolutionIndex = 0;
            frameRate = 165;

            closePetOnExit = true;
            petForm = 0;
#if UNITY_EDITOR
            // 开发 key 预填（2026-08-30）：编辑器新档自带对话 key（AES 加密后入档），删档测试后聊天免重输。
            // DevKey 的 const 声明在 UNITY_EDITOR 内——构建产物无此代码路径，导出的存档初始化恒为空 key。
            petApiKeyCipher = GIC.Pet.PetApiKeyCrypto.Encrypt(GIC.Pet.PetApiKeyCrypto.DevKey);
#else
            petApiKeyCipher = "";
#endif
            petChatProvider = 0;

            currentPosition = (int)PositionName.SnezhnayaCastle;

            keyBindings.Clear();
        }

        public void InitCards()
        {
            // ==================== 角色 ====================

            // 角色（13个初始解锁）
            List<UnitName> unitList = new List<UnitName>
            {
                UnitName.Paimon,      // 索引 0
                UnitName.Zibai,       // 索引 1
                UnitName.Linnea,      // 索引 2
                UnitName.Illuga,      // 索引 3
                UnitName.Amber,       // 索引 4
                UnitName.Kaeya,       // 索引 5
                UnitName.Barbara,     // 索引 6
                UnitName.Xingqiu,     // 索引 7
                UnitName.Beidou,      // 索引 8
                UnitName.Hutao,       // 索引 9
                UnitName.Mizuki,      // 索引 10
                UnitName.Gorou,       // 索引 11
                UnitName.Kirara,      // 索引 12
            };

            unitList.ForEach(unitName =>
            {
                SaveCardData cardData = new SaveCardData();
                cardData.SaveUnit(unitName, 1);
                AddOwnedUnit(cardData);
            });

            // 卡组1：前三个蒙德角色（Amber, Kaeya, Barbara）
            ownedUnits[4].AddToDeck(0);   // Amber
            ownedUnits[5].AddToDeck(0);   // Kaeya
            ownedUnits[6].AddToDeck(0);   // Barbara

            // 卡组2：三个挪德卡莱角色（Zibai, Linnea, Illuga）
            ownedUnits[1].AddToDeck(1);   // Zibai
            ownedUnits[2].AddToDeck(1);   // Linnea
            ownedUnits[3].AddToDeck(1);   // Illuga

            // 卡组3：三个璃月角色（Xingqiu, Beidou, Hutao）
            ownedUnits[7].AddToDeck(2);   // Xingqiu
            ownedUnits[8].AddToDeck(2);   // Beidou
            ownedUnits[9].AddToDeck(2);   // Hutao

            // 卡组4：三个稻妻角色（Kirara, Gorou, Mizuki）
            ownedUnits[10].AddToDeck(3);  // Kirara
            ownedUnits[11].AddToDeck(3);  // Gorou
            ownedUnits[12].AddToDeck(3);  // Mizuki
            // ==================== 货币/珍贵物品 ====================

            List<ItemName> valuableItemList = new List<ItemName>
            {
                ItemName.Mora,
                ItemName.IntertwinedFate,
                ItemName.Stamina,
                ItemName.Primogem
            };

            int valuableStartIndex = ownedNormalItems.Count;

            valuableItemList.ForEach(itemID =>
            {
                SaveCardData cardData = new SaveCardData();
                // 设置初始数量
                int count = itemID switch
                {
                    ItemName.Mora => 100,
                    ItemName.IntertwinedFate => 60,
                    ItemName.Stamina => 10,
                    ItemName.Primogem => 16000,
                    _ => 1
                };
                cardData.SaveItem(itemID, count);
                AddOwnedItem(cardData);
            });

            // 纠缠之缘和体力加入卡组1
            ownedNormalItems[valuableStartIndex + 1].AddToDeck(0);  // IntertwinedFate
            ownedNormalItems[valuableStartIndex + 2].AddToDeck(0);  // Stamina

            // ==================== 普通物品 ====================

            List<ItemName> itemList = new List<ItemName>
            {
                ItemName.Apple,
                ItemName.RawMeat,
                ItemName.Egg,
                ItemName.Wheat,
                ItemName.Radish
            };

            itemList.ForEach(itemID =>
            {
                SaveCardData cardData = new SaveCardData();
                cardData.SaveItem(itemID, 10);
                AddOwnedItem(cardData);
            });

            // ==================== 默认卡组 ====================
            currentDeck = 1;
        }

    }

    [Serializable]
    public class SaveCardData
    {
        public CardId id;
        public int count;
        public int skin;
        public List<int> inDecks;

        // ── 属性兼容：CardType 由 CardId 派生 ──
        public CardType cardType => id.cardType;

        // ── 运行时配置引用（不序列化，延迟解析） ──
        [NonSerialized] private ICardConfig _config;
        [NonSerialized] private bool       _configResolved;

        public ICardConfig Config
        {
            get
            {
                if (!_configResolved)
                {
                    _config = CardConfigResolver.Instance?.Resolve(id);
                    _configResolved = true;
                }
                return _config;
            }
        }

        public int StarLevel => Config?.StarLevel ?? 0;
        public int SortOrder => Config?.SortOrder ?? int.MaxValue;
        public int ConfigIndex => Config?.ConfigIndex ?? int.MaxValue;

        // ── 卡组管理 ──

        public void AddToDeck(int deckId)
        {
            inDecks ??= new List<int>();
            if (!HasInDeck(deckId)) inDecks.Add(deckId);
        }

        public void RemoveFromDeck(int deckId) => inDecks?.RemoveAll(d => d == deckId);

        public bool HasInDeck(int deckId) => inDecks?.Contains(deckId) ?? false;

        public void AddToDecks(IEnumerable<int> deckIds)
        {
            inDecks ??= new List<int>();
            foreach (int d in deckIds)
                if (!inDecks.Contains(d)) inDecks.Add(d);
        }

        public void RemoveFromDecks(IEnumerable<int> deckIds)
        {
            if (inDecks == null) return;
            foreach (int d in deckIds) inDecks.RemoveAll(x => x == d);
        }

        public void ClearAllDecks() => inDecks?.Clear();

        // ── 构造方法 ──

        public void SaveItem(ItemName itemID, int count, int skin = 0)
        {
            id = new CardId(itemID);
            this.count = count;
            this.skin = skin;
        }

        public void SaveUnit(UnitName unitName, int count, int skin = 0)
        {
            id = new CardId(unitName);
            this.count = count;
            this.skin = skin;
        }

        public SaveCardData Clone()
        {
            return new SaveCardData
            {
                id      = this.id,
                count   = this.count,
                skin    = this.skin,
                inDecks = this.inDecks != null ? new List<int>(this.inDecks) : null,
            };
        }
    }


}


