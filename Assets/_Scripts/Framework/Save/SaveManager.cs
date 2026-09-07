using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// JSON 存档管理器
    /// </summary>
    [Component]
    public class SaveManager : IWargameManager
    {
        private const string SAVE_FILE_NAME = "gic_save.json";

        /// <summary>
        /// 存档路径 — Clone 实例使用独立子目录，避免与主实例共用存档
        /// </summary>
        private static string SavePath
        {
            get
            {
                string dir = Application.persistentDataPath;
                string cloneMarker = Path.Combine(Application.dataPath, "../.clone");
                if (File.Exists(cloneMarker))
                {
                    dir = Path.Combine(dir, "clone");
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                }
                return Path.Combine(dir, SAVE_FILE_NAME);
            }
        }

        // 存档版本策略：低于该版本的旧档不做迁移，直接删旧档创建新档（开发期无真实玩家，语义变更即升版重置）
        // v11（2026-09-05）：PlayerSaveData 分区重组（progress/settings/pet 嵌套结构），旧格式字段不可直接映射
        private const int CURRENT_SAVE_VERSION = 11;

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

        // 删档测试模式下，旧存档的备份标识（可选，用于调试）
        private const string BACKUP_SUFFIX = ".backup";

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

            // 删档测试模式：备份或删除旧存档
            if (IsDeletionTestMode)
            {
                HandleDeletionTestMode();
            }

            LoadSaveData();
        }

        public void Start() { }

        /// <summary>
        /// 处理删档测试模式 - 每次启动都强制使用新存档
        /// </summary>
        private void HandleDeletionTestMode()
        {
            // 不管存档是否存在，直接删除（不需要备份）
            if (File.Exists(SavePath))
            {
                try
                {
                    File.Delete(SavePath);
                    GICLog.Info("删档测试模式：已删除旧存档，将创建新存档");
                }
                catch (Exception e)
                {
                    GICLog.Warn($"删档测试模式删除失败: {e.Message}");
                }
            }

            // 可选：同时删除备份文件
            string backupPath = SavePath + BACKUP_SUFFIX;
            if (File.Exists(backupPath))
            {
                try
                {
                    File.Delete(backupPath);
                }
                catch { }
            }

            // 残留的半截 .tmp 一并清掉（正常会被下次保存覆盖，这里保证测试环境彻底干净）
            string tmpPath = SavePath + ".tmp";
            if (File.Exists(tmpPath))
            {
                try
                {
                    File.Delete(tmpPath);
                }
                catch { }
            }
        }

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
            saveDirty = true;
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

        /// <summary>
        /// 实际写盘。preserveBackup=true 时跳过备份位写入——仅备份恢复路径固化用：
        /// 此时主档位上是刚判定损坏的坏档，若照常 Replace 会把它滚进 .backup 顶掉刚恢复出来的好备份。
        /// </summary>
        private void DoSave(bool preserveBackup = false)
        {
            lastSaveTime = Time.time;
            try
            {
                CurrentSave.saveVersion = CURRENT_SAVE_VERSION;
#if UNITY_EDITOR
                string json = JsonUtility.ToJson(CurrentSave, true);   // 编辑器 pretty 便于人工排查存档内容
#else
                string json = JsonUtility.ToJson(CurrentSave, false);  // 构建版关 pretty：序列化更快、文件更小（存档随游戏增长的存量成本优化）
#endif
                // 原子写（2026-09-05）：先写 .tmp 再替换正式文件——写盘中途崩溃/断电不留半截损坏档
                //（旧直写的后果：下次读档 catch → CreateNewSave = 全进度清零）。同卷 File.Replace 为原子替换；
                // 首存（正式文件尚不存在）走 File.Move；残留 .tmp 无害，下次保存自然覆盖。
                string tmpPath = SavePath + ".tmp";
                File.WriteAllText(tmpPath, json, System.Text.Encoding.UTF8);
                if (File.Exists(SavePath))
                {
                    // 滚动备份（2026-09-05）：把替换下来的旧主档挪进 .backup（上一版已知完好存档）——
                    // 主档损坏/误删时读档侧可回退，坏档不再等于全进度清零（见 LoadSaveData 三级回退链）。
                    File.Replace(tmpPath, SavePath, preserveBackup ? null : SavePath + BACKUP_SUFFIX);
                }
                else
                    File.Move(tmpPath, SavePath);
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
                    EventBusHub.Instance.SendImmediate(new OnSaveFailedEvent());
                }
            }
        }

        /// <summary>
        /// 读档三级回退链（2026-09-05）：主档 → 滚动备份 → 新档。
        /// 主档损坏（半截 JSON/磁盘坏区）不再直接清零全部进度——先回退 .backup（上一版完好存档，最多回退一个存档窗口）；
        /// 仅当备份也不可用时才建档。版本过旧不回退：备份只会与主档同版或更旧，回退无意义（重置式版本策略）。
        /// </summary>
        public void LoadSaveData()
        {
            LoadOutcome main = TryLoadFile(SavePath, fromBackup: false);
            if (main == LoadOutcome.Loaded)
                return;

            if (main != LoadOutcome.VersionOutdated)
            {
                if (main == LoadOutcome.ParseFailed)
                    GICLog.Error("主档损坏，尝试从备份回退");
                else
                    GICLog.Info("未找到主档，尝试从备份回退");

                if (TryLoadFile(SavePath + BACKUP_SUFFIX, fromBackup: true) == LoadOutcome.Loaded)
                    return;
            }

            GICLog.Info("无可用存档，创建新存档");
            CreateNewSave();
        }

        /// <summary>单次读档尝试的结果，供 LoadSaveData 决定回退链走向</summary>
        private enum LoadOutcome { Loaded, FileMissing, ParseFailed, VersionOutdated }

        /// <summary>
        /// 从指定路径尝试读档，成功则完成全部读档后处理（校验修复 → 补缺 → 排序 → 固化写盘）并返回 Loaded；
        /// 文件缺失/解析失败/版本过旧各自返回对应结果，不产生副作用（CurrentSave 不动）。
        /// </summary>
        private LoadOutcome TryLoadFile(string path, bool fromBackup)
        {
            if (!File.Exists(path))
                return LoadOutcome.FileMissing;

            PlayerSaveData data;
            try
            {
                string json = File.ReadAllText(path, System.Text.Encoding.UTF8);
                data = JsonUtility.FromJson<PlayerSaveData>(json);
            }
            catch (Exception e)
            {
                GICLog.Error($"存档解析失败({path}): {e.Message}");
                return LoadOutcome.ParseFailed;
            }

            // 0. 版本过低：不迁移，由调用方决定是否回退其它副本（重置式版本策略）
            if (data == null || data.saveVersion < CURRENT_SAVE_VERSION)
            {
                GICLog.Warn($"存档版本 {data?.saveVersion.ToString() ?? "null"} 低于当前版本 {CURRENT_SAVE_VERSION}，不可用({path})");
                return LoadOutcome.VersionOutdated;
            }

            CurrentSave = data;
            CurrentSave.EnsureValid();

            // 1. 先补充缺失的角色/物品
            SyncMissingCards();

            // 2. 排序
            SortAllCategories();

            // 3. 版本兼容检查（高于当前版本仅警告）
            ApplySaveCompatibility();

            // 4. 固化一次（立即写）：把补缺/排序结果持久化；备份恢复路径同时把好档写回主档位。
            //    fromBackup 且主档尚存（=坏档还占着主档位）时 preserveBackup——防 Replace 把坏档滚进备份位。
            DoSave(preserveBackup: fromBackup && File.Exists(SavePath));

            GICLog.Info($"读档成功{(fromBackup ? "（从备份恢复）" : "")}，当前版本: {CurrentSave.saveVersion}");
            if (fromBackup)
                GICLog.Warn($"主档已损坏，进度回退到上一版备份存档: {path}");
            return LoadOutcome.Loaded;
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
            GIC.Pet.PetPrefs.WriteChatCipher(CurrentSave.pet.petApiKeyCipher);
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

            save.progress.currentDeck = initialSaveConfig.defaultDeck;
        }

        /// <summary>
        /// 手动清除所有存档（删档测试模式专用）
        /// </summary>
        public void ClearAllSaveData()
        {
            if (File.Exists(SavePath))
            {
                try
                {
                    File.Delete(SavePath);
                    GICLog.Info($"已删除存档: {SavePath}");
                }
                catch (Exception e)
                {
                    GICLog.Error($"删除存档失败: {e.Message}");
                }
            }

            // 同时删除备份文件与残留 .tmp（与删档测试模式对齐：连带 .backup/.tmp 一并清理）
            string backupPath = SavePath + BACKUP_SUFFIX;
            if (File.Exists(backupPath))
            {
                try
                {
                    File.Delete(backupPath);
                    GICLog.Info($"已删除备份存档: {backupPath}");
                }
                catch (Exception e)
                {
                    GICLog.Error($"删除备份存档失败: {e.Message}");
                }
            }

            string tmpPath = SavePath + ".tmp";
            if (File.Exists(tmpPath))
            {
                try
                {
                    File.Delete(tmpPath);
                }
                catch (Exception e)
                {
                    GICLog.Error($"删除临时存档失败: {e.Message}");
                }
            }

            // 重新创建新存档
            CreateNewSave();
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

        #region saveCompat

        /// <summary>
        /// 存档兼容性检查。
        /// 低于 CURRENT_SAVE_VERSION 的旧档已在 LoadSaveData 中直接重置（无迁移链），
        /// 这里只处理"存档版本高于游戏版本"的前向兼容警告。
        /// </summary>
        private void ApplySaveCompatibility()
        {
            if (CurrentSave == null) return;

            if (CurrentSave.saveVersion > CURRENT_SAVE_VERSION)
            {
                GICLog.Warn($"存档版本({CurrentSave.saveVersion})高于游戏版本({CURRENT_SAVE_VERSION})，可能存在兼容性问题");
            }
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



