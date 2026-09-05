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
        private const int CURRENT_SAVE_VERSION = 10;

        private float lastSaveTime = -999f;
        // 落盘间隔（2026-09-05 时机优化）：SaveGame() 只标脏，变更由 Update 在距上次写盘 ≥该间隔后合并落盘。
        // 旧语义="1 秒 CD 内连发的保存被静默丢弃"（连发修改会丢数据），新语义="延迟合并"——永不丢，只推迟 ≤1s。
        private const float SAVE_CD = 1f;
        private bool saveDirty;

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

        public SaveManager(UnitConfig unitConfig, ItemConfig itemConfig)
        {
            this.unitConfig = unitConfig;
            this.itemConfig = itemConfig;
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

        private void DoSave()
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
                    File.Replace(tmpPath, SavePath, null);
                else
                    File.Move(tmpPath, SavePath);
                saveDirty = false;   // 仅成功后清脏；失败保持脏=下个间隔自动重试
                GICLog.Info($"存档成功: {SavePath}");
            }
            catch (Exception e)
            {
                GICLog.Error($"保存失败: {e.Message}");
            }
        }

        public void LoadSaveData()
        {

            if (!File.Exists(SavePath))
            {
                GICLog.Info("未找到存档，创建新存档");
                CreateNewSave();
            }

            try
            {
                string json = File.ReadAllText(SavePath, System.Text.Encoding.UTF8);
                CurrentSave = JsonUtility.FromJson<PlayerSaveData>(json);

                // 0. 版本过低：不迁移，直接删旧档创建新档
                if (CurrentSave.saveVersion < CURRENT_SAVE_VERSION)
                {
                    GICLog.Warn($"存档版本 {CurrentSave.saveVersion} 低于当前版本 {CURRENT_SAVE_VERSION}，旧档不迁移，创建新存档");
                    CreateNewSave();
                    return;
                }

                // 1. 先补充缺失的角色/物品
                SyncMissingCards();

                // 2. 排序
                SortAllCategories();

                // 3. 版本兼容检查（高于当前版本仅警告）
                ApplySaveCompatibility();

                // 4. 保存一次，确保补充和排序的结果持久化（立即写：启动路径，不走延迟窗）
                SaveGameNow();

                GICLog.Info($"读档成功，当前版本: {CurrentSave.saveVersion}");
            }
            catch (Exception e)
            {
                GICLog.Error($"读取失败: {e.Message}");
                CreateNewSave();
            }
        }

        private void CreateNewSave()
        {
            CurrentSave = new PlayerSaveData();
            CurrentSave.InitDefault();
#if UNITY_EDITOR
            // 开发 key 同步落 pet.json（2026-08-30）：聊天客户端每请求 PetPrefs.ReadChatCipher 直读
            // pet.json 密文——只写主存档不写 pet.json 的话编辑器/桌宠构建版聊天仍报未设置 key。
            // 编辑器与构建共享 persistentDataPath+同机设备指纹=密文互通。构建产物无此代码路径。
            GIC.Pet.PetPrefs.WriteChatCipher(CurrentSave.petApiKeyCipher);
#endif
            SaveGameNow();
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

            // 同时删除备份文件
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
            foreach (var card in CurrentSave.ownedUnits)
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

            CurrentSave.ownedUnits = newList;
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
            foreach (var card in CurrentSave.ownedNormalItems)
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

            CurrentSave.ownedNormalItems = newList;
        }

        #endregion

        #region 排序

        /// <summary>
        /// 对所有类别进行排序
        /// </summary>
        private void SortAllCategories()
        {
            SortUnits();
            SortItems(CurrentSave.ownedNormalItems);
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

            CurrentSave.ownedUnits.Sort((a, b) =>
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
            CurrentSave.currentPosition = (int)position;
            SaveGame();
        }

        #endregion
    }
}



