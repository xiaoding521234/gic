using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Tool;
namespace GIC.Framework
{


    /// <summary>
    /// JSON 存档管理器
    /// </summary>
    [Component]
    public class SaveManager : IWargameManager
    {
        private const string SAVE_FILE_NAME = "gic_save.json";

        /// <summary>
        /// 存档路径 — Clone 实例使用独立子目录，避免与主实例共用存档
        /// </summary>
        public static string SavePath
        {
            get
            {
                string dir = Application.persistentDataPath;
                if (File.Exists(Path.Combine(Application.dataPath, "..", ".clone")))
                    dir = Path.Combine(dir, "clone");
                return Path.Combine(dir, SAVE_FILE_NAME);
            }
        }

        // 存档版本策略：低于该版本的旧档不做迁移，直接删旧档创建新档（开发期无真实玩家，语义变更即升版重置）
        // v11（2026-09-05）：PlayerSaveData 分区重组（progress/settings/pet 嵌套结构），旧格式字段不可直接映射
        // v12（2026-09-13）：无 schema 变化——零迁移政策落定（docs/20 §1.6）后的开发期重置重启，旧档清零重建
        private const int CURRENT_SAVE_VERSION = 12;

        // 保留迭代期每次启动重建进度的策略；高版本或暂不可读的磁盘存档仍受写入保护。
        public static readonly bool 快速迭代期每次启动重建 = true;

        private float lastSaveTime = -999f;
        // 落盘间隔（2026-09-05 时机优化）：SaveGame() 只标脏，变更由 Update 在距上次写盘 ≥该间隔后合并落盘。
        // 旧语义="1 秒 CD 内连发的保存被静默丢弃"（连发修改会丢数据），新语义="延迟合并"——永不丢，只推迟 ≤1s。
        private const float SAVE_CD = 1f;
        private bool saveDirty;
        // 连续写盘失败只发一次 OnSaveFailedEvent（成功后复位）——失败保持脏=Update 每秒重试，不防抖会 toast 刷屏
        private bool saveFailureNotified;

        public PlayerSaveData CurrentSave { get; private set; } = new PlayerSaveData();

        // 删档测试模式：仅编辑器内可通过菜单 Tools/存档/删档测试模式 显式开启（EditorPrefs 持久化），
        // 打包版本恒为 false —— 上线不再依赖人工改回常量
#if UNITY_EDITOR
        public const string DeletionTestModeKey = "GIC.SaveManager.DeletionTestMode";
        private static bool IsDeletionTestMode =>
            UnityEditor.EditorPrefs.GetBool(DeletionTestModeKey, false);
#else
        private const bool IsDeletionTestMode = false;
#endif

        private PlayerSaveFile _fileStore;
        private PlayerSaveFile FileStore => _fileStore ??= new PlayerSaveFile(SavePath, CURRENT_SAVE_VERSION);
        private bool _writeProtected;
        private bool _rotateMainOnWrite;

        // 配置引用
        private readonly UnitConfig unitConfig;
        private readonly ItemConfig itemConfig;
        private readonly InitialSaveConfig initialSaveConfig;

        public SaveManager(UnitConfig unitConfig, ItemConfig itemConfig, InitialSaveConfig initialSaveConfig)
        {
            this.unitConfig = unitConfig;
            this.itemConfig = itemConfig;
            this.initialSaveConfig = initialSaveConfig;
        }

        [PostConstruct]
        public void Init()
        {
            GICLog.Info($"存档路径: {SavePath}");

            LoadSaveData();
        }

        public void Start() { }

        public void Update(float deltaTime)
        {
            // 延迟落盘（2026-09-05）：标脏后最迟 ~SAVE_CD 秒在此写盘，连发保存自动合并（设置滑条/卡组连点/位置切换）
            if (saveDirty && Time.time - lastSaveTime >= SAVE_CD)
                DoSave();
        }

        /// <summary>
        /// 请求存盘（标脏延迟合并）：变更最迟 ~SAVE_CD 秒后由 Update 落盘。高频/连发调用安全；
        /// 关键路径（祈愿扣石、读档固化、建档、退出/后台化兜底）必须用 SaveGameNow。
        /// </summary>
        public void SaveGame()
        {
            if (!_writeProtected) saveDirty = true;
        }

        /// <summary>立即写盘（绕过延迟窗）：货币/进度关键路径与进程生命周期兜底用（GameScene.OnApplicationQuit/Pause）</summary>
        public void SaveGameNow()
        {
            DoSave();
        }

        /// <summary>
        /// 统一变更入口（2026-09-05）：闭包内完成一次存档修改并自动标脏，取代"改字段 + 手动 SaveGame()"两步分离
        /// ——漏调标脏在中途崩溃/进程被杀时会丢数据（退出兜底救不了崩溃）。
        /// 闭包内做完全部变更（含对捕获的 SaveCardData 的操作）：Modify(s => s.settings.frameRate = 165)、
        /// Modify(_ => card.AddToDeck(1))。需要变更返回值参与业务判断的（TryConsumeItem 等）不适用本入口。
        /// </summary>
        public void Modify(Action<PlayerSaveData> mutation)
        {
            if (mutation == null) return;
            mutation(CurrentSave);
            SaveGame();
        }

        /// <summary>统一变更入口（立即落盘版）："丢了会疼"的数据（货币/进度）变更用</summary>
        public void ModifyNow(Action<PlayerSaveData> mutation)
        {
            if (mutation == null) return;
            mutation(CurrentSave);
            SaveGameNow();
        }

        private void DoSave()
        {
            if (_writeProtected) return;
            lastSaveTime = Time.time;
            saveDirty = true; // 立即写盘也必须在失败后保留重试。
            try
            {
                CurrentSave.saveVersion = CURRENT_SAVE_VERSION;
#if UNITY_EDITOR
                string json = JsonUtility.ToJson(CurrentSave, true);   // 编辑器 pretty 便于人工排查存档内容
#else
                string json = JsonUtility.ToJson(CurrentSave, false);  // 构建版关 pretty：序列化更快、文件更小（存档随游戏增长的存量成本优化）
#endif
                if (!FileStore.TryWrite(json, _rotateMainOnWrite))
                {
                    _writeProtected = true;
                    saveDirty = false;
                    GICLog.Warn("主档或备份已变为高版本或不可读，停止写入");
                    EventBusHub.Instance?.SendImmediate(new OnSaveFailedEvent());
                    return;
                }
                _rotateMainOnWrite = true;
                saveDirty = false;   // 仅成功后清脏；失败保持脏=下个间隔自动重试
                saveFailureNotified = false;   // 连续失败提示复位
                GICLog.Info($"存档成功: {SavePath}");
            }
            catch (Exception e)
            {
                GICLog.Error($"保存失败: {e.Message}");
                // 玩家可感知提示（磁盘满/权限异常时静默丢进度=最坏体验）：事件解耦 UI，
                // 由 PopupManager 订阅弹 toast；连续失败只提示一次（saveFailureNotified 防抖）
                if (!saveFailureNotified)
                {
                    saveFailureNotified = true;
                    EventBusHub.Instance?.SendImmediate(new OnSaveFailedEvent());
                }
            }
        }

        /// <summary>主档 → 备份 → 新档；高版本或 IO 失败进入不写盘会话。</summary>
        public void LoadSaveData()
        {
            var main = FileStore.Read(backup: false, out var mainData);
            var backup = FileStore.Read(backup: true, out var backupData);
            _writeProtected = PlayerSaveFile.ProtectsExistingFile(main) || PlayerSaveFile.ProtectsExistingFile(backup);
            _rotateMainOnWrite = main == PlayerSaveFile.State.Valid;
            saveDirty = false;
            if (_writeProtected)
                GICLog.Warn($"存档写入已禁用，保留原文件（主档={main}，备份={backup}）");

            if (IsDeletionTestMode || 快速迭代期每次启动重建)
            {
                if (IsDeletionTestMode && !_writeProtected) DeleteSaveCopiesForTesting();
                CreateNewSave();
                return;
            }

            if (main == PlayerSaveFile.State.Valid)
            {
                FinishLoad(mainData);
                return;
            }
            if (main != PlayerSaveFile.State.Older && backup == PlayerSaveFile.State.Valid)
            {
                GICLog.Warn($"主档不可用（{main}），从备份恢复");
                FinishLoad(backupData); // 下一次成功写入前保持 _rotateMainOnWrite=false，包括失败后的重试。
                return;
            }
            CreateNewSave();
        }

        private void FinishLoad(PlayerSaveData data)
        {
            CurrentSave = data;
            CurrentSave.EnsureValid();
            SyncMissingCards();
            SortAllCategories();
            DoSave();
        }

        private void CreateNewSave()
        {
            CurrentSave = new PlayerSaveData();
            CurrentSave.InitDefault();
            ApplyInitialData(CurrentSave);

            // 建档即补全+排序（2026-09-05 回归修复）：新档终态必须与"读档+Sync"一致——配置里的未拥有
            // 角色以 count=0 入档（背包锁定态显示、重获判定靠它）。否则版本重置当次会话背包只剩
            // 初始 13 角色（旧版靠 LoadSaveData 建档后 fall-through 顺带补全，重构时误当冗余去除）。
            SyncMissingCards();
            SortAllCategories();
#if UNITY_EDITOR
            // 开发 key 同步落 pet.json（2026-08-30）：聊天客户端每请求 PetPrefs.ReadChatCipher 直读
            // pet.json 密文——只写主存档不写 pet.json 的话编辑器/桌宠构建版聊天仍报未设置 key。
            // 编辑器与构建共享 persistentDataPath+同机设备指纹=密文互通。构建产物无此代码路径。
            // 分槽（2026-09-13）：DevKey 预填在新档供应商槽位（新档恒 0=DeepSeek），pet.json 同槽。
            if (!_writeProtected)
                GIC.Pet.PetPrefs.WriteChatCipher(CurrentSave.pet.petChatProvider,
                    CurrentSave.pet.GetChatCipher(CurrentSave.pet.petChatProvider));
#endif
            SaveGameNow();
        }

        /// <summary>
        /// 从 InitialSaveConfig（SO）填充新档的初始卡牌/货币/默认卡组（2026-09-05 外置，取代旧
        /// PlayerSaveData.InitCards 硬编码——旧实现的"列表序号→卡组"魔法索引在调条目顺序时会静默错乱）。
        /// 配置缺失时报错跳过：空进度仍可进游戏（SyncMissingCards 会按配置补 count=0 条目），但货币为 0——属配置事故须修。
        /// </summary>
        private void ApplyInitialData(PlayerSaveData save)
        {
            if (initialSaveConfig == null)
            {
                GICLog.Error("InitialSaveConfig 未加载（Resources/Configs/InitialSaveConfig.asset 缺失？），新档初始卡牌/货币为空");
                return;
            }

            foreach (var entry in initialSaveConfig.initialUnits)
            {
                var card = new SaveCardData();
                card.SaveUnit(entry.unit, entry.count);
                if (entry.decks != null) card.AddToDecks(entry.decks);   // 手作资产可能缺 decks 列表
                save.AddOwnedUnit(card);
            }

            foreach (var entry in initialSaveConfig.initialItems)
            {
                var card = new SaveCardData();
                card.SaveItem(entry.item, entry.count);
                if (entry.decks != null) card.AddToDecks(entry.decks);
                save.AddOwnedItem(card);
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // 开发测试物资（2026-09-07）：仅编辑器/开发构建建档发放，正式版编译剔除——新档命运之缘 0/0 拍板不受影响。
            // 必须走 AddItemCount（合并语义）：initialItems 里命运之缘已有 0 值占位条目，用 AddOwnedItem 会追加双条目
            // 而 GetItemCount 只读首条（0），发放会被白吞。
            foreach (var entry in initialSaveConfig.devTestItems)
                save.AddItemCount(entry.item, entry.count);
#endif

            // 初始卡组名（2026-10-04）：配置声明的卡组自定义名写入新档（deckId 直接寻址，无条目顺序依赖）。
            // 越界/空名跳过；超长截断与 CardManager.SetDeckName 同口径（粘贴/外部写入口径防御）。
            foreach (var entry in initialSaveConfig.initialDeckNames)
            {
                if (entry == null || string.IsNullOrEmpty(entry.name)) continue;
                var names = save.progress.deckNames;
                if (entry.deckId < 0 || entry.deckId >= names.Count) continue;
                names[entry.deckId] = entry.name.Length > CardManager.MaxDeckNameLength
                    ? entry.name.Substring(0, CardManager.MaxDeckNameLength)
                    : entry.name;
            }

            save.progress.currentDeck = initialSaveConfig.defaultDeck;
        }

        private void DeleteSaveCopiesForTesting()
        {
            try
            {
                FileStore.DeleteForTesting();
                _rotateMainOnWrite = false;
            }
            catch (Exception e)
            {
                _writeProtected = true;
                GICLog.Warn($"测试存档删除失败，结束本次写入：{e.Message}");
            }
        }

        #region 同步缺失卡片

        /// <summary>
        /// 检查并补充 Config 中有但存档中没有的角色和物品（count=0）
        /// </summary>
        private void SyncMissingCards()
        {
            SyncMissingUnits();
            SyncMissingItems();
            CurrentSave.RebuildOwnedCards();
        }

        /// <summary>
        /// 补充缺失的角色 — 按配置文件顺序重建列表，已有数据保留，缺失的补 count=0
        /// </summary>
        private void SyncMissingUnits()
        {
            if (unitConfig == null)
            {
                GICLog.Warn("UnitConfig 为空，跳过角色同步");
                return;
            }

            unitConfig.BuildCache();

            // 构建已有角色数据的索引
            var existingData = new Dictionary<int, SaveCardData>();
            foreach (var card in CurrentSave.progress.ownedUnits)
            {
                existingData[card.id.value] = card;
            }

            // 按 unitDataList 顺序重建列表
            var newList = new List<SaveCardData>();
            int added = 0;
            foreach (var unitData in unitConfig.unitDataList)
            {
                if (unitData == null) continue;
                int uid = (int)unitData.unitName;
                if (existingData.TryGetValue(uid, out var existing))
                {
                    newList.Add(existing);
                }
                else
                {
                    var newCard = new SaveCardData();
                    newCard.SaveUnit(unitData.unitName, 0);
                    newList.Add(newCard);
                    added++;
                }
            }

            if (added > 0)
                GICLog.Info($"补充缺失角色 {added} 个");

            CurrentSave.progress.ownedUnits = newList;
        }

        /// <summary>
        /// 补充缺失的物品 — 按配置文件顺序重建列表，已有数据保留，缺失的补 count=0
        /// </summary>
        private void SyncMissingItems()
        {
            if (itemConfig == null)
            {
                GICLog.Warn("ItemConfig 为空，跳过物品同步");
                return;
            }

            itemConfig.BuildCache();

            // 构建已有物品数据的索引
            var existingData = new Dictionary<int, SaveCardData>();
            foreach (var card in CurrentSave.progress.ownedNormalItems)
            {
                existingData[card.id.value] = card;
            }

            // 按 itemDataList 顺序重建列表
            var newList = new List<SaveCardData>();
            int added = 0;
            foreach (var itemData in itemConfig.itemDataList)
            {
                if (itemData == null) continue;
                int iid = (int)itemData.itemID;
                if (existingData.TryGetValue(iid, out var existing))
                {
                    newList.Add(existing);
                }
                else
                {
                    var newCard = new SaveCardData();
                    newCard.SaveItem(itemData.itemID, 0);
                    newList.Add(newCard);
                    added++;
                }
            }

            if (added > 0)
                GICLog.Info($"补充缺失物品 {added} 个");

            CurrentSave.progress.ownedNormalItems = newList;
        }

        #endregion

        #region 排序

        /// <summary>
        /// 对所有类别进行排序
        /// </summary>
        private void SortAllCategories()
        {
            SortUnits();
            SortItems(CurrentSave.progress.ownedNormalItems);
        }

        /// <summary>
        /// 角色排序：主势力值从小到大 → 同势力内星级从高到低
        /// </summary>
        private void SortUnits()
        {
            if (unitConfig == null)
            {
                GICLog.Warn("UnitConfig 为空，跳过角色排序");
                return;
            }

            CurrentSave.progress.ownedUnits.Sort((a, b) =>
            {
                return CardSortUtility.CompareByPrimaryThenStar(
                    a.SortOrder, b.SortOrder,
                    a.StarLevel, b.StarLevel,
                    a.ConfigIndex, b.ConfigIndex
                );
            });
        }

        /// <summary>
        /// 物品排序：主标签值从小到大 → 同标签内星级从高到低
        /// </summary>
        private void SortItems(List<SaveCardData> items)
        {
            if (itemConfig == null)
            {
                GICLog.Warn("ItemConfig 为空，跳过物品排序");
                return;
            }

            items.Sort((a, b) =>
            {
                return CardSortUtility.CompareByPrimaryThenStar(
                    a.SortOrder, b.SortOrder,
                    a.StarLevel, b.StarLevel,
                    a.ConfigIndex, b.ConfigIndex
                );
            });
        }

        #endregion

        #region 地图相关便捷方法

        public void SetPosition(PositionName position)
        {
            // 统一变更入口：变更+标脏一步完成（2026-09-05 Modify 迁移）
            Modify(s => s.progress.currentPosition = (int)position);
        }

        #endregion
    }
}



