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
    /// 玩家存档数据 — 分区结构（2026-09-05 重组，存档版本 11）：
    /// progress=游戏进度（卡牌/卡组/位置/祈愿统计）、settings=用户设置（音量/显示/按键绑定）、
    /// pet=桌宠设置。PlayerSaveData 保留全部便捷方法作为稳定 API 门面（外部调用不感知分区），
    /// 分区类只承载数据。JsonUtility 铁律：全部 public 字段、引用类型必带初始化器、不支持 Dictionary/多态。
    /// </summary>
    [Serializable]
    public class PlayerSaveData
    {
        // 基础信息
        public int saveVersion = 1;  // 存档版本号
        public string playerName = "旅行者";

        // ========== 分区 ==========
        public SaveProgress progress = new SaveProgress();
        public SaveSettings settings = new SaveSettings();
        public SavePetSettings pet = new SavePetSettings();

        /// <summary>
        /// 统一拥有卡牌列表（运行时合并 progress.ownedUnits + progress.ownedNormalItems）
        /// </summary>
        [NonSerialized] private List<SaveCardData> _ownedCards;
        public List<SaveCardData> ownedCards
        {
            get
            {
                if (_ownedCards == null)
                {
                    _ownedCards = new List<SaveCardData>();
                    _ownedCards.AddRange(progress.ownedUnits);
                    _ownedCards.AddRange(progress.ownedNormalItems);
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
            progress.ownedUnits.Add(card);
            RebuildOwnedCards();
        }

        /// <summary>添加已拥有物品 —— 外部改库存的统一入口，自动失效 ownedCards 缓存</summary>
        public void AddOwnedItem(SaveCardData card)
        {
            progress.ownedNormalItems.Add(card);
            RebuildOwnedCards();
        }

        // ========== 物品数量查询/变更（货币等，统一入口） ==========

        /// <summary>查询物品持有数量（不在存档中返回 0）</summary>
        public int GetItemCount(ItemName item)
        {
            foreach (var card in progress.ownedNormalItems)
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
            foreach (var card in progress.ownedNormalItems)
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
            foreach (var card in progress.ownedNormalItems)
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

        /// <summary>
        /// 初始化默认数据 — 分区全量重置。初始卡牌/货币不再在此硬编码：
        /// 由 SaveManager.ApplyInitialData 从 InitialSaveConfig（SO）填充（2026-09-05 外置）。
        /// </summary>
        public void InitDefault()
        {
            playerName = "旅行者";
            progress = new SaveProgress();
            progress.fateItemsMigrated = true; // 新档无历史相遇余量，直接视为已迁移
            settings = new SaveSettings();
            pet = new SavePetSettings();
            progress.EnsureDecksValid(); // 新档/重置路径：补齐 deckNames 到卡组数（改名写入依赖列表长度，CreateNewSave 不走读档 EnsureValid）
#if UNITY_EDITOR
            // 开发 key 预填（2026-08-30）：编辑器新档自带对话 key（AES 加密后入档），删档测试后聊天免重输。
            // DevKey 的 const 声明在 UNITY_EDITOR 内——构建产物无此代码路径，导出的存档初始化恒为空 key。
            pet.petApiKeyCipher = GIC.Pet.PetApiKeyCrypto.Encrypt(GIC.Pet.PetApiKeyCrypto.DevKey);
#endif
        }

        /// <summary>
        /// 读档后防御性校验修复（SaveManager.TryLoadFile 调用）。
        /// 本游戏自身产出的存档不会出现 null/负值（JsonUtility 缺字段保留初始化器、不序列化 null），
        /// 但手改档/外部工具可能产出"合法 JSON 坏数据"——null 列表会带进 SyncMissingCards/排序
        /// 直接 NRE，且每次启动必崩成死循环（用户无法进游戏自救）。就地修复优于崩溃：补空集合、剔 null 条目、负数量归零。
        /// </summary>
        public void EnsureValid()
        {
            playerName ??= "旅行者";
            progress ??= new SaveProgress();
            settings ??= new SaveSettings();
            pet ??= new SavePetSettings();
            settings.keyBindings ??= new List<KeyBindingEntry>();
            pet.petApiKeyCipher ??= "";
            progress.EnsureValid();
            MigrateFateItems();
        }

        /// <summary>
        /// 命运之缘物品化一次性迁移（2026-09-06 改版）：
        /// v11 及更早的相遇之线是虚拟计数（可用次数 = 累计星辉/20 - 已用次数），改版后命运之缘为背包物品——
        /// ①未用完的相遇余量 → 补偿为相遇之缘；②累计满 200 星辉的档位（旧版无此机制）→ 全量补偿为纠缠之缘。
        /// 幂等：fateItemsMigrated 置 true 后不再执行；新档 starglitterEarned=0 时补偿为 0，自然走完置位。
        /// 调用点：读档 EnsureValid（迁移后随读档固化写盘）。
        /// </summary>
        private void MigrateFateItems()
        {
            if (progress.fateItemsMigrated) return;

            int pendingAcquaint = Mathf.Max(0,
                progress.starglitterEarned / SaveProgress.AcquaintFateThreshold - progress.encounterUsed);
            int retroIntertwined = Mathf.Max(0,
                progress.starglitterEarned / SaveProgress.IntertwinedFateThreshold);

            if (pendingAcquaint > 0)
                AddItemCount(ItemName.AcquaintFate, pendingAcquaint);
            if (retroIntertwined > 0)
                AddItemCount(ItemName.IntertwinedFate, retroIntertwined);

            progress.fateItemsMigrated = true;
        }
    }

    /// <summary>
    /// 进度分区 — 卡牌库存/卡组/位置/祈愿统计（存档易变核心，丢失最疼的部分）
    /// </summary>
    [Serializable]
    public class SaveProgress
    {
        public List<SaveCardData> ownedUnits = new List<SaveCardData>();        // 已拥有的角色
        public List<SaveCardData> ownedNormalItems = new List<SaveCardData>();  // 已拥有的物品
        public int currentDeck = 0;

        // ========== 卡组名称与排序（2026-09-05 卡组管理面板） ==========
        /// <summary>卡组自定义名称（下标=deckId；""=未命名，UI 显示本地化默认"卡组N"）。JsonUtility 缺字段保留初始化器，EnsureValid 补齐到 7 项</summary>
        public List<string> deckNames = new List<string>();
        /// <summary>卡组显示顺序（值为 deckId；显示编号=下标+1）。拖拽排序只改此表，卡牌 inDecks 引用的 deckId 恒定不变</summary>
        public List<int> deckOrder = new List<int> { 0, 1, 2, 3, 4, 5, 6 };

        // 锚点位置信息
        public int currentPosition = (int)PositionName.SnezhnayaCastle;

        // ========== 命运之缘（2026-09-06 物品化改版） ==========
        /// <summary>每累计 N 个星辉赠送 1 个相遇之缘（祈愿发放与旧档补偿共用，规则属存档进度语义）</summary>
        public const int AcquaintFateThreshold = 20;
        /// <summary>每累计 N 个星辉赠送 1 个纠缠之缘</summary>
        public const int IntertwinedFateThreshold = 200;

        /// <summary>累计获取的星辉总量（只增不减，与可消费的星辉余额解耦）——命运之缘里程碑计数源</summary>
        public int starglitterEarned = 0;
        /// <summary>已使用的相遇之线次数（旧版虚拟计数，2026-09-06 改版后停用——命运之缘改为背包物品消耗；
        /// 仅供旧档一次性补偿换算用，勿再读写</summary>
        public int encounterUsed = 0;
        /// <summary>命运之缘物品化迁移完成标记（false=v11 旧档待补偿：把累计星辉里程碑换算成相遇之缘/纠缠之缘入包；
        /// JsonUtility 缺字段保留初始化器 false，老档加载即触发迁移，见 PlayerSaveData.MigrateFateItems）</summary>
        public bool fateItemsMigrated = false;

        /// <summary>分区数据修复（PlayerSaveData.EnsureValid 调用）</summary>
        public void EnsureValid()
        {
            ownedUnits ??= new List<SaveCardData>();
            ownedNormalItems ??= new List<SaveCardData>();

            ownedUnits.RemoveAll(c => c == null);
            ownedNormalItems.RemoveAll(c => c == null);

            foreach (var card in ownedUnits)
                EnsureCardValid(card);
            foreach (var card in ownedNormalItems)
                EnsureCardValid(card);

            // 货币类物品不参与卡组（2026-09-06 拍板）：清遗留 inDecks 成员资格。
            // maxPrepareCount=0 的卡在卡组编辑模式被遮罩锁定、点击无响应无法手动移除，
            // 遗留成员会永久占用卡组位（历史初始存货曾把纠缠之缘放进卡组0）。
            foreach (var card in ownedNormalItems)
            {
                if (card.inDecks != null && card.inDecks.Count > 0
                    && NonDeckableCurrencyItems.Contains(card.id.AsItemName()))
                    card.inDecks.Clear();
            }

            EnsureDecksValid();
        }

        /// <summary>不可入卡组的货币物品（对应 ItemConfig maxPrepareCount=0）：星辉/相遇之缘/纠缠之缘</summary>
        private static readonly HashSet<ItemName> NonDeckableCurrencyItems = new HashSet<ItemName>
        {
            ItemName.Starglitter,
            ItemName.AcquaintFate,
            ItemName.IntertwinedFate,
        };

        /// <summary>卡组名称/排序字段修复（v2 动态卡组）：
        /// deckNames.Count 即卡组数量的事实源（增删卡组由 CardManager 同步维护三张表）——
        /// 空表才补 DefaultDeckCount（新档/极旧档）；超上限裁剪；排列表必须是 0..N-1 的排列；
        /// currentDeck 越界钳回 0。public：InitDefault 新档路径直接调用。</summary>
        public void EnsureDecksValid()
        {
            deckNames ??= new List<string>();
            deckNames.RemoveAll(x => x == null);

            if (deckNames.Count == 0)
            {
                for (int i = 0; i < CardManager.DefaultDeckCount; i++)
                    deckNames.Add("");
            }
            if (deckNames.Count > CardManager.MaxDeckCount)
                deckNames.RemoveRange(CardManager.MaxDeckCount, deckNames.Count - CardManager.MaxDeckCount);

            int n = deckNames.Count;

            deckOrder ??= new List<int>();
            bool valid = deckOrder.Count == n;
            if (valid)
            {
                var seen = new HashSet<int>();
                foreach (var id in deckOrder)
                {
                    if (id < 0 || id >= n || !seen.Add(id)) { valid = false; break; }
                }
            }
            if (!valid)
            {
                deckOrder.Clear();
                for (int i = 0; i < n; i++) deckOrder.Add(i);
            }

            if (currentDeck < 0 || currentDeck >= n) currentDeck = 0;
        }

        private static void EnsureCardValid(SaveCardData card)
        {
            card.inDecks ??= new List<int>();
            if (card.count < 0)
                card.count = 0;
        }
    }

    /// <summary>
    /// 用户设置分区 — 音量/显示/按键绑定（丢失不疼、变更高频）
    /// </summary>
    [Serializable]
    public class SaveSettings
    {
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

        // ========== 按键绑定 ==========
        public List<KeyBindingEntry> keyBindings = new List<KeyBindingEntry>();
    }

    /// <summary>
    /// 桌宠设置分区 — 形态/对话供应商/API Key（双通道同步主档↔pet.json，细节 docs/19）
    /// </summary>
    [Serializable]
    public class SavePetSettings
    {
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
            foreach (int d in deckIds)
                inDecks.RemoveAll(x => x == d);
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
