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

        private const int CURRENT_SAVE_VERSION = 1;

        private float lastSaveTime = -999f;
        private const float SAVE_CD = 1f;

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

        public void Update(float deltaTime) { }

        public void SaveGame()
        {
            if (Time.time - lastSaveTime < SAVE_CD) return;
            lastSaveTime = Time.time;

            try
            {
                CurrentSave.saveVersion = CURRENT_SAVE_VERSION;
                string json = JsonUtility.ToJson(CurrentSave, true);
                File.WriteAllText(SavePath, json, System.Text.Encoding.UTF8);
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

                // 1. 先补充缺失的角色/物品
                SyncMissingCards();

                // 2. 排序
                SortAllCategories();

                // 3. 版本兼容升级
                ApplySaveCompatibility();

                // 4. 保存一次，确保补充和排序的结果持久化
                SaveGame();

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
            SaveGame();
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

        #region 存档兼容性升级

        /// <summary>
        /// 应用存档兼容性处理（版本号控制）
        /// </summary>
        private void ApplySaveCompatibility()
        {
            if (CurrentSave == null) return;

            int oldVersion = CurrentSave.saveVersion;

            if (CurrentSave.saveVersion > CURRENT_SAVE_VERSION)
            {
                GICLog.Warn($"存档版本({CurrentSave.saveVersion})高于游戏版本({CURRENT_SAVE_VERSION})，可能存在兼容性问题");
                return;
            }

            while (CurrentSave.saveVersion < CURRENT_SAVE_VERSION)
            {
                switch (CurrentSave.saveVersion)
                {
                    case 1:
                        UpgradeFromV1ToV2(CurrentSave);
                        break;
                    default:
                        GICLog.Warn($"未知的存档版本: {CurrentSave.saveVersion}，直接升级到最新");
                        CurrentSave.saveVersion = CURRENT_SAVE_VERSION;
                        break;
                }
            }

            if (oldVersion != CurrentSave.saveVersion)
            {
                GICLog.Info($"存档已从 V{oldVersion} 升级到 V{CurrentSave.saveVersion}");
                SaveGame();
            }
        }

        private void UpgradeFromV1ToV2(PlayerSaveData saveData)
        {
            GICLog.Info("执行存档升级: V1 → V2");

            saveData.masterVolume = Mathf.Clamp01(saveData.masterVolume);
            saveData.bgmVolume = Mathf.Clamp01(saveData.bgmVolume);
            saveData.sfxVolume = Mathf.Clamp01(saveData.sfxVolume);
            saveData.voiceVolume = Mathf.Clamp01(saveData.voiceVolume);

            saveData.saveVersion = 2;
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



