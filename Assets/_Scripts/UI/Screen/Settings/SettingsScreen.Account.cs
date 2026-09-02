// ==================== SettingsScreen.Account.cs（账户设置 + 输入弹窗） ====================
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
using GIC.Tool;
namespace GIC.UI
{


    public partial class SettingsScreen
    {
        private InputPopupDialog inputPopupInstance;

        private void InitAccountSettings()
        {
            var saveManager = _saveManager;
            string playerName = saveManager.CurrentSave.playerName;

            playerNameSetting.Setup("Name", playerName,
                onClick: () =>
                {
                    // 点击时实时读存档——闭包捕获 Init 时的局部变量会回显改名前的旧值（PetApiKey 同款）
                    ShowInputPanel(playerNameSetting, saveManager.CurrentSave.playerName, (newName) =>
                    {
                        saveManager.CurrentSave.playerName = newName;
                        saveManager.SaveGame();
                        playerNameSetting.UpdateValue(newName);
                    });
                },
                onValueConfirmed: null
            );
            playerNameSetting.Initialize();

            // TODO: 指令功能暂未实现，仅预留输入弹窗入口
            commandSetting.Setup("Command", "",
                onClick: () =>
                {
                    ShowInputPanel(commandSetting, "", (newCommand) =>
                    {
                        GICLog.Info($"玩家指令: {newCommand}");
                    });
                },
                onValueConfirmed: null,
                placeholderKey: "Input"
            );
            commandSetting.Initialize();
        }

        private void ShowInputPanel(ButtonSettingItem settingItem, string currentValue, Action<string> callback)
        {
            ShowInputPanel(settingItem, currentValue, callback, null);
        }

        /// <summary>带自定义标题键（空=按设置项推断——玩家名/指令的既有键）；maxChars=输入弹窗字符上限（0=沿用弹窗默认）</summary>
        private void ShowInputPanel(ButtonSettingItem settingItem, string currentValue, Action<string> callback, string titleKey, int maxChars = 0)
        {
            if (inputPopupPrefab == null) return;

            if (inputPopupInstance == null)
            {
                inputPopupInstance = Instantiate(inputPopupPrefab, transform);
            }

            if (string.IsNullOrEmpty(titleKey))
                titleKey = settingItem == playerNameSetting ? "ModifyName" : "InputCommand";
            inputPopupInstance.Show(titleKey, currentValue, callback, maxChars);
        }
    }
}
